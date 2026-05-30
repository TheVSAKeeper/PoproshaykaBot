# OBS-оверлей: устойчивость SSE-соединения

**Дата:** 2026-05-31
**Статус:** дизайн утверждён, готов к плану реализации
**Проблема:** периодическое «отваливание» OBS-оверлея — чат замирает и не оживает до ручного refresh источника.

## Контекст и корневая причина

Оверлей — это browser source в OBS, который грузит `/chat` (`obs.js`) и слушает SSE-поток `/events`. Путь данных:

```
Бот → ChatMessageReceived (шина) → SseChatBridgeHandler → SseService.AddChatMessage
   → глобальный Channel(512, DropOldest) → BroadcastLoop
   → per-client Channel(256, DropOldest) → ClientWriterLoop → Response.Body.Write/Flush
   → TCP(loopback) → OBS CEF → EventSource('/events') → obs.js
   ⟂ keepalive: PeriodicTimer 30s → тот же путь
```

Диагностика (по симптомам, подтверждённым пользователем): «висит мёртвым до ручного refresh», лечится **без** перезапуска приложения → сервер (Kestrel + `SseService`) в момент сбоя **жив**. Значит корень — на стороне клиента: соединение `EventSource` уходит в «тихо-полумёртвое» состояние, а в `obs.js` нет ни одного механизма восстановления.

Три конкретные дыры:
1. **`onerror` — no-op** (`obs.js:349-351`): только `console.error`, никакого форс-reconnect.
2. **Keepalive невидим для JS**: сервер шлёт keepalive как SSE-комментарий `: keep-alive` (`SseService.cs:275` → `SseFormatter.cs:9`). По спецификации SSE строки-комментарии **не порождают событий** в `EventSource`. Поэтому клиент физически не слышит «пульс» сервера и не может отличить тихий чат от мёртвого коннекта.
3. **Reconnect не доделывает работу**: `initOverlay()` (`obs.js:840-852`) не вызывается при авто-reconnect, `id:`/`retry:` сервер не шлёт — реплея и догрузки истории нет. Латентно: при reconnect в окно рестарта сервера `AddClient` молча не регистрирует клиента (`SseService.cs:144-150`), коннект висит «здоровым» 200 OK навсегда.

## Scope

В работе: устойчивость SSE кодом (углы A/C/D/F) + памятка по настройкам OBS (угол E).
Вне работы: поиск утечки памяти (угол B — отдельная задача). Клиентский watchdog транзитивно лечит *симптом* B (после перезапуска процесса оверлей сам переподключится), но не его причину.

Выбранный подход — **Вариант 2**: наблюдаемый пульс-событие + клиентский watchdog + аккуратные серверные сигналы. Без буфера событий на сервере (`Last-Event-ID`-реплей отвергнут как YAGNI — пропущенное добирается через `/api/history`).

## Компоненты

### 1. Сервер: наблюдаемый пульс `ping` (ядро)

`SseService.KeepAliveLoopAsync` (`SseService.cs:267-286`): вместо `Enqueue(SseEnvelope.Comment("keep-alive"))` слать `Enqueue(new SseEnvelope("ping", json))`, где `json = {"intervalSeconds": <keepAliveSeconds>}` (сериализация через `ServerJsonOptions.Default`).

`SseFormatter` уже выдаёт `event: ping\ndata: {...}\n\n` для не-комментариев (`SseFormatter.cs:11-19`). Наличие `data:` обязательно — событие без data-буфера по спеке не диспатчится. Плюмбинг (канал → broadcast → per-client) не меняется. Интервал в payload нужен, чтобы клиент адаптировал порог тишины под серверный `SseKeepAliveSeconds` (он конфигурируем).

### 2. Сервер: 503 вместо «пустого» коннекта (угол C)

- `SseService`: добавить публичное `bool IsRunning` (чтение `_isRunning` под `_lifecycleLock`); `AddClient` меняет сигнатуру на `bool` (true = зарегистрирован).
- `SseEndpoint` (`SseEndpoint.cs:15-43`):
  - Если `!sseService.IsRunning` → `ctx.Response.StatusCode = 503` и `return` **до** установки SSE-заголовков и флаша.
  - Иначе строгий порядок: установить заголовки → флаш → `await` запись retry-преамбулы (см. компонент 4) → `var registered = sseService.AddClient(ctx.Response)`. Преамбула пишется **до** `AddClient`, потому что `AddClient` запускает `ClientWriterLoopAsync`, который пишет в тот же `Response.Body` — два одновременных писателя в один поток недопустимы. Запись преамбулы должна завершиться (await) до старта writer-loop.
  - Если `registered == false` (микро-гонка: сервис остановили между проверкой и `AddClient`) → `return` сразу (без `Task.Delay(Infinite)`); ответ завершится, клиент получит EOF и переподключится — не вечный «пустой» коннект.

### 3. Сервер: таймаут записи, отстрел зомби-клиента (углы D/F)

`ClientWriterLoopAsync` (`SseService.cs:360-394`): обернуть `WriteAsync`+`FlushAsync` в linked-CTS `CancellationTokenSource.CreateLinkedTokenSource(token)` + `CancelAfter(writeTimeout)`. Если запись зависла на полу-открытом сокете дольше таймаута → `OperationCanceledException` → `break` → `finally` → `RemoveClient` → ответ завершается → живой клиент переподключится.

Различать причины отмены:
- `token.IsCancellationRequested` (общая остановка сервиса) → тихий выход, без лога-предупреждения.
- иначе (сработал только per-write таймаут) → `LogDebug` «клиент не успел принять данные за {timeout}, отключаю» и выход.

Новый параметр `InfrastructureSettings.SseClientWriteTimeoutSeconds = 10`. Прокинуть в `SseChannelOptions` (или передать в `SseService` тем же путём, что и остальные SSE-настройки через `Start()` / опции) — выбрать при реализации по существующему паттерну чтения `SseKeepAliveSeconds` в `Start()`.

### 4. Сервер: `retry:` преамбула (низкоценное, но в Варианте 2 заявлено)

`SseEndpoint` после флаша заголовков пишет в тело `retry: 3000\n\n` (один раз, до `AddClient`). `SseFormatter` не трогаем. Влияет только на задержку *нативного* reconnect (наш сбой решает клиентский watchdog, а не задержка) — поэтому помечено как опциональное, риск нулевой.

### 5. Клиент `obs.js`: watchdog + reconnect + бэкфилл (угол A)

Состояние модуля: `lastEventTime`, `staleThresholdMs` (старт 70000), `reconnecting` (гард), ссылки на `eventSource` и id watchdog-таймера.

- `markAlive()` → `lastEventTime = Date.now()`. Вызывается во **всех** слушателях: `message`, `clear`, `chat_settings_changed`, `dropped` и новом `ping`.
- Слушатель `ping`: `markAlive()` + адаптация порога `staleThresholdMs = Math.max(70000, intervalSeconds * 2000 + 10000)`.
- Watchdog: `setInterval` ~10 с. Реконнект при `Date.now() - lastEventTime > staleThresholdMs` **или** `eventSource.readyState === EventSource.CLOSED`.
- `onerror` (`obs.js:349-351`): при `readyState === EventSource.CLOSED` → `reconnect()` (вместо текущего no-op); при `CONNECTING` — оставить нативный авто-retry.
- `reconnect()`:
  - гард `reconnecting` от параллельных запусков;
  - `eventSource.close()`;
  - повторно `fetch('/api/history')` → бэкфилл через `addMessage(msg, true)` (дедуп `seenMessageIds` переживает reconnect — это модульное состояние, не сбрасывается);
  - настройки (`/api/chat-settings`) на reconnect **не** перезапрашиваем (приходят через SSE-событие `chat_settings_changed`; при полном reload — тянутся как раньше);
  - показать баннер «🔁 Чат-источник перезагружен» (`showReloadBanner`) — **подтверждено: показываем и при авто-реконнекте**;
  - `initEventSource()` заново; сбросить `lastEventTime`; снять гард.
- Рефактор: выделить путь «история + initEventSource» так, чтобы `initOverlay()` (первый запуск) и `reconnect()` его переиспользовали.

### 6. Памятка по OBS (угол E)

Совет: в Browser Source **снять** «Shutdown source when not visible» и «Refresh browser when scene becomes active» для чат-оверлея, чтобы переключение сцен не перезагружало источник.

- **README** — короткая секция про настройку OBS-оверлея.
- **Подсказка в UI** — рядом с URL оверлея в онбординге: `HealthCheckPage` (`_overlayUrlLabel` / `_overlayHeader`, см. `Forms/Onboarding/Pages/HealthCheckPage`). Однострочный поясняющий `Label`/подпись. (Альтернатива/дополнение — дашборд-тайл `ChatOverlayPreviewTileControl`.)

## Параметры (дефолты)

| Параметр | Значение | Где |
|---|---|---|
| keepalive интервал | 30 с | `SseKeepAliveSeconds` (без изменений) |
| порог тишины (клиент) | `max(70с, 2×интервал + 10с)` | `obs.js`, адаптируется по payload `ping` |
| тик watchdog | 10 с | `obs.js` |
| таймаут записи клиенту | 10 с | новый `SseClientWriteTimeoutSeconds` |
| retry | 3000 мс | преамбула в `SseEndpoint` |

## Обработка ошибок

- Сервер: исключения записи/флаша уже ловятся в `ClientWriterLoopAsync` → удаление клиента; добавляем только таймаут-ветку.
- Клиент: `reconnect()` идемпотентен (гард); `fetch('/api/history')` в `.catch` логирует и всё равно поднимает `initEventSource()` (не блокировать восстановление потока из-за неудачной догрузки истории).

## Тестирование

- **Сервер (NUnit, `PoproshaykaBot.Core.Tests/Server/SseServiceTests.cs` уже есть):**
  - `IsRunning` отражает состояние; `AddClient` возвращает `false` до `Start()` / после `StopAsync()`.
  - Отстрел по таймауту записи: фейковый `HttpResponse` с `Body`, чья `WriteAsync` зависает → writer-loop завершает клиента за `writeTimeout`, клиент удаляется из реестра.
  - Формат `ping`-события: `SseFormatter.Format(new("ping", json))` == `event: ping\ndata: {...}\n\n`.
  - Тайминг keepalive юнит-тестом не покрываем (реальный `PeriodicTimer`).
- **Клиент (`obs.js`):** JS-тест-инфры в репозитории нет → проверка ручная (OBS / браузер, можно через `/run` или `/verify`): симулировать обрыв (остановить/поднять сервер, либо порвать коннект) и убедиться, что оверлей восстанавливается сам с баннером и бэкфиллом.

## Затрагиваемые файлы

- `PoproshaykaBot.Core/Server/SseService.cs` — ping-событие, `IsRunning`, `AddClient→bool`, таймаут записи.
- `PoproshaykaBot.Core/Server/Endpoints/SseEndpoint.cs` — 503, retry-преамбула, обработка `registered==false`.
- `PoproshaykaBot.Core/Settings/InfrastructureSettings.cs` — `SseClientWriteTimeoutSeconds`.
- `PoproshaykaBot.Core/Server/SseChannelOptions.cs` (или путь чтения опций в `Start()`) — прокинуть таймаут записи.
- `PoproshaykaBot.Core/Assets/obs.js` — watchdog, reconnect, бэкфилл, `markAlive`, `ping`.
- `PoproshaykaBot.Core.Tests/Server/SseServiceTests.cs` — новые тесты.
- `README.md` + `PoproshaykaBot.WinForms/Forms/Onboarding/Pages/HealthCheckPage*` — памятка/подсказка (угол E).

## Открытые риски

- `obs.js` — embedded asset; правки покрываются только ручной проверкой.
- Прокидывание `SseClientWriteTimeoutSeconds` в `SseService` должно повторить существующий паттерн (как `SseKeepAliveSeconds` читается из `_settingsManager.Current.Twitch.Infrastructure` в `Start()`), а не плодить новый канал конфигурации.
