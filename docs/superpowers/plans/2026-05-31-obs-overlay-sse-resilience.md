# OBS-оверлей: устойчивость SSE — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Сделать OBS-оверлей самовосстанавливающимся при «тихом» обрыве SSE — клиент сам переподключается, сервер отдаёт наблюдаемый пульс, отстреливает зависших клиентов и не держит «пустые» коннекты.

**Architecture:** Сервер (`SseService`/`SseEndpoint`) переводит keepalive из невидимого комментария в наблюдаемое событие `ping`, добавляет `IsRunning`, `AddClient→bool`, 503-ответ при остановленном сервисе и per-write таймаут для отстрела зомби-клиентов. Клиент (`obs.js`) получает watchdog по «тишине пульса», авто-reconnect с догрузкой истории и реакцию `onerror`. Памятка по настройкам OBS — в README и онбординге.

**Tech Stack:** .NET 8, ASP.NET Core (Kestrel + SSE), NUnit 4 + NSubstitute (`PoproshaykaBot.Core.Tests`, ссылается на `Microsoft.AspNetCore.TestHost`), ванильный JS (`obs.js`, embedded asset — без JS-тест-инфры).

**Спека:** `docs/superpowers/specs/2026-05-31-obs-overlay-sse-resilience-design.md`

---

## Структура файлов

| Файл | Ответственность | Изменение |
|---|---|---|
| `PoproshaykaBot.Core/Settings/InfrastructureSettings.cs` | новый параметр `SseClientWriteTimeoutSeconds` | Modify |
| `PoproshaykaBot.Core/Server/SseService.cs` | `IsRunning`, `AddClient→bool`, ping-событие, per-write таймаут | Modify |
| `PoproshaykaBot.Core/Server/Endpoints/SseEndpoint.cs` | 503, retry-преамбула, обработка `registered==false` | Modify |
| `PoproshaykaBot.Core/Assets/obs.js` | watchdog, reconnect, backfill, ping/markAlive | Modify |
| `PoproshaykaBot.Core.Tests/Server/SseServiceTests.cs` | новые серверные тесты + helper | Modify |
| `README.md` | секция про настройку OBS-оверлея | Modify |
| `PoproshaykaBot.WinForms/Forms/Onboarding/Pages/HealthCheckPage.cs` | подсказка у URL оверлея | Modify |

Тестовая команда (везде ниже): `dotnet test PoproshaykaBot.Core.Tests/PoproshaykaBot.Core.Tests.csproj`
Сборка: `dotnet build PoproshaykaBot.sln`

> **Замечание про `git add`:** в рабочем дереве есть несвязанные незакоммиченные правки (`BotStopMode`, `BotConnectionManager` и т.д.). В каждом коммите ниже добавляй **только перечисленные файлы по явным путям** — не используй `git add -A`/`git add .`.

---

## Task 1: Сервер — `IsRunning` + `AddClient` возвращает `bool`

**Files:**
- Modify: `PoproshaykaBot.Core/Server/SseService.cs`
- Test: `PoproshaykaBot.Core.Tests/Server/SseServiceTests.cs`

- [ ] **Step 1: Добавить поле `_registry` в SetUp теста (для наблюдения за реестром в Task 3)**

В `SseServiceTests.cs` добавь приватное поле и используй его в `SetUp` (замени строку создания `_service`):

```csharp
    private SseClientRegistry _registry = null!;
```

В методе `SetUp()` замени:

```csharp
        _service = new(_settingsManager, _logger, new(), new(), new());
```

на:

```csharp
        _registry = new();
        _service = new(_settingsManager, _logger, new(), _registry, new());
```

- [ ] **Step 2: Написать падающие тесты `IsRunning` и `AddClient→bool`**

Добавь в `SseServiceTests.cs`:

```csharp
    [Test]
    public void IsRunning_BeforeStart_IsFalse()
    {
        Assert.That(_service.IsRunning, Is.False);
    }

    [Test]
    public void IsRunning_AfterStart_IsTrue()
    {
        _service.Start();
        Assert.That(_service.IsRunning, Is.True);
    }

    [Test]
    public async Task IsRunning_AfterStop_IsFalse()
    {
        _service.Start();
        await _service.StopAsync();
        Assert.That(_service.IsRunning, Is.False);
    }

    [Test]
    public void AddClient_BeforeStart_ReturnsFalse()
    {
        var http = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        Assert.That(_service.AddClient(http.Response), Is.False);
    }

    [Test]
    public void AddClient_AfterStart_ReturnsTrue()
    {
        _service.Start();
        var http = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        Assert.That(_service.AddClient(http.Response), Is.True);
    }
```

- [ ] **Step 3: Запустить тесты — убедиться, что не компилируется / падает**

Run: `dotnet test PoproshaykaBot.Core.Tests/PoproshaykaBot.Core.Tests.csproj --filter "IsRunning|AddClient"`
Expected: ошибка компиляции (`IsRunning` не существует; `AddClient` возвращает `void`, нельзя сравнить с `bool`).

- [ ] **Step 4: Реализовать `IsRunning` и `AddClient→bool` в `SseService.cs`**

Добавь свойство (рядом с `DroppedMessageCount`, около строки 50):

```csharp
    public bool IsRunning
    {
        get
        {
            lock (_lifecycleLock)
            {
                return _isRunning;
            }
        }
    }
```

Замени метод `AddClient` (строки ~138-167) — поменяй возвращаемый тип на `bool` и верни результат:

```csharp
    public bool AddClient(HttpResponse response)
    {
        _logger.LogDebug("Попытка регистрации нового SSE клиента");

        CancellationToken token;

        lock (_lifecycleLock)
        {
            if (!_isRunning || _cts is null)
            {
                _logger.LogWarning("Попытка зарегистрировать SSE клиента до Start() сервиса. Подключение отклонено");
                return false;
            }

            token = _cts.Token;
        }

        try
        {
            var pipeline = new SseClientPipeline(response, _options.ClientChannelCapacity);
            var clientCount = _registry.Add(response, pipeline);
            pipeline.WriterTask = Task.Run(() => ClientWriterLoopAsync(pipeline, token));

            _logger.LogInformation("Установлено новое SSE подключение. Всего активных клиентов: {ClientCount}", clientCount);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при установке нового SSE подключения");
            return false;
        }
    }
```

- [ ] **Step 5: Запустить тесты — убедиться, что проходят**

Run: `dotnet test PoproshaykaBot.Core.Tests/PoproshaykaBot.Core.Tests.csproj --filter "IsRunning|AddClient"`
Expected: PASS (5 новых тестов зелёные, существующие — не сломаны).

- [ ] **Step 6: Commit**

```bash
git add PoproshaykaBot.Core/Server/SseService.cs PoproshaykaBot.Core.Tests/Server/SseServiceTests.cs
git commit -m "SSE: IsRunning и AddClient с признаком регистрации"
```

---

## Task 2: Сервер — keepalive как наблюдаемое событие `ping`

**Files:**
- Modify: `PoproshaykaBot.Core/Server/SseService.cs`
- Test: `PoproshaykaBot.Core.Tests/Server/SseServiceTests.cs`

- [ ] **Step 1: Написать падающие тесты на ping-payload и формат**

Добавь в `SseServiceTests.cs` (вверху файла дополни `using PoproshaykaBot.Core.Server;` — уже есть):

```csharp
    [Test]
    public void BuildPingPayload_SerializesIntervalAsCamelCase()
    {
        Assert.That(SseService.BuildPingPayload(30), Is.EqualTo("{\"intervalSeconds\":30}"));
    }

    [Test]
    public void Formatter_PingEvent_ProducesObservableSseEvent()
    {
        var text = SseFormatter.Format(new SseEnvelope("ping", SseService.BuildPingPayload(30)));
        Assert.That(text, Is.EqualTo("event: ping\ndata: {\"intervalSeconds\":30}\n\n"));
    }
```

- [ ] **Step 2: Запустить — убедиться, что не компилируется**

Run: `dotnet test PoproshaykaBot.Core.Tests/PoproshaykaBot.Core.Tests.csproj --filter "Ping"`
Expected: ошибка компиляции (`SseService.BuildPingPayload` не существует).

- [ ] **Step 3: Реализовать `BuildPingPayload` и переключить keepalive на ping**

В `SseService.cs` добавь внутренний метод и вложенный record (например, рядом с `DroppedNotice` в конце класса):

```csharp
    internal static string BuildPingPayload(int intervalSeconds)
    {
        return JsonSerializer.Serialize(new PingPayload(intervalSeconds), ServerJsonOptions.Default);
    }

    private sealed record PingPayload(int IntervalSeconds);
```

Замени тело `KeepAliveLoopAsync` (строки ~267-286), чтобы слать `ping` вместо комментария:

```csharp
    private async Task KeepAliveLoopAsync(int intervalSeconds, CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
        var pingPayload = BuildPingPayload(intervalSeconds);

        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                Enqueue(new("ping", pingPayload));
            }
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogDebug(ex, "Цикл keep-alive остановлен штатно");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Неожиданная ошибка в цикле keep-alive SSE");
        }
    }
```

> Примечание: `SseFormatter` для не-комментария выдаёт `event: ping\ndata: ...\n\n`. Наличие `data:` обязательно — без него `EventSource` не диспатчит событие. `SseEnvelope.Comment` больше не используется в keepalive, но сам метод оставь (может использоваться в тестах/будущем).

- [ ] **Step 4: Запустить — убедиться, что проходят**

Run: `dotnet test PoproshaykaBot.Core.Tests/PoproshaykaBot.Core.Tests.csproj --filter "Ping"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add PoproshaykaBot.Core/Server/SseService.cs PoproshaykaBot.Core.Tests/Server/SseServiceTests.cs
git commit -m "SSE: keepalive как наблюдаемое событие ping"
```

---

## Task 3: Сервер — per-write таймаут отстреливает зависшего клиента

**Files:**
- Modify: `PoproshaykaBot.Core/Settings/InfrastructureSettings.cs`
- Modify: `PoproshaykaBot.Core/Server/SseService.cs`
- Test: `PoproshaykaBot.Core.Tests/Server/SseServiceTests.cs`

- [ ] **Step 1: Добавить helper ожидания условия в тест**

Добавь в `SseServiceTests.cs` (рядом с `WaitForBytesAsync`):

```csharp
    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Условие не выполнилось за {timeout.TotalSeconds:F1}с.");
    }
```

- [ ] **Step 2: Написать падающий тест на отстрел по таймауту записи**

Добавь в `SseServiceTests.cs`:

```csharp
    [Test]
    public async Task ClientWriter_WhenWriteExceedsTimeout_RemovesStuckClient()
    {
        _settings.Twitch.Infrastructure.SseClientWriteTimeoutSeconds = 1;
        _service.Start();

        var http = new DefaultHttpContext();
        var blocking = new BlockingStream(); // Release() не зовём — запись висит, но уважает CancellationToken
        http.Response.Body = blocking;

        Assert.That(_service.AddClient(http.Response), Is.True);
        Assert.That(_registry.Count, Is.EqualTo(1));

        _service.AddChatMessage(SampleMessage());

        await WaitForConditionAsync(() => _registry.Count == 0, TimeSpan.FromSeconds(4));
        Assert.That(_registry.Count, Is.EqualTo(0),
            "Клиент с зависшей записью должен быть отключён по таймауту.");
    }
```

- [ ] **Step 3: Запустить — убедиться, что падает**

Run: `dotnet test PoproshaykaBot.Core.Tests/PoproshaykaBot.Core.Tests.csproj --filter "ClientWriter_WhenWriteExceedsTimeout"`
Expected: ошибка компиляции (`SseClientWriteTimeoutSeconds` не существует) либо TimeoutException (клиент не отключается — таймаута записи пока нет).

- [ ] **Step 4: Добавить параметр в `InfrastructureSettings.cs`**

Добавь свойство (рядом с другими `Sse*`):

```csharp
    public int SseClientWriteTimeoutSeconds { get; set; } = 10;
```

- [ ] **Step 5: Прокинуть таймаут в `SseService` и обернуть запись**

В `SseService.cs` добавь поле (рядом с прочими полями, около строки 28):

```csharp
    private TimeSpan _clientWriteTimeout = TimeSpan.FromSeconds(10);
```

В методе `Start()` (внутри `lock (_lifecycleLock)`, рядом со строкой чтения `keepAliveSeconds`) добавь:

```csharp
            var writeTimeoutSeconds = Math.Max(1, _settingsManager.Current.Twitch.Infrastructure.SseClientWriteTimeoutSeconds);
            _clientWriteTimeout = TimeSpan.FromSeconds(writeTimeoutSeconds);
```

Замени `ClientWriterLoopAsync` (строки ~360-394) на версию с per-write таймаутом:

```csharp
    private async Task ClientWriterLoopAsync(SseClientPipeline pipeline, CancellationToken token)
    {
        try
        {
            await foreach (var buffer in pipeline.Channel.Reader.ReadAllAsync(token))
            {
                using var writeCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                writeCts.CancelAfter(_clientWriteTimeout);

                try
                {
                    await pipeline.Response.Body.WriteAsync(buffer, writeCts.Token);
                    await pipeline.Response.Body.FlushAsync(writeCts.Token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug("SSE клиент не принял данные за {Timeout} c — отключаю зависшего клиента",
                        _clientWriteTimeout.TotalSeconds);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Сбой записи в поток SSE. Клиент будет удалён");
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // client cancelled or shutdown
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Неожиданная ошибка в цикле записи SSE-клиента");
        }
        finally
        {
            RemoveClient(pipeline.Response);
        }
    }
```

- [ ] **Step 6: Запустить — убедиться, что проходит (и не сломаны соседние тесты)**

Run: `dotnet test PoproshaykaBot.Core.Tests/PoproshaykaBot.Core.Tests.csproj --filter "SseServiceTests"`
Expected: PASS все тесты `SseServiceTests` (включая `AddChatMessage_WhenSlowClient_DoesNotBlockOtherClient` — он освобождает `slowStream` за ~2с, а таймаут по умолчанию 10с, конфликта нет).

- [ ] **Step 7: Commit**

```bash
git add PoproshaykaBot.Core/Settings/InfrastructureSettings.cs PoproshaykaBot.Core/Server/SseService.cs PoproshaykaBot.Core.Tests/Server/SseServiceTests.cs
git commit -m "SSE: таймаут записи отключает зависшего клиента"
```

---

## Task 4: Сервер — `SseEndpoint`: 503 + retry-преамбула + обработка незарегистрированного клиента

**Files:**
- Modify: `PoproshaykaBot.Core/Server/Endpoints/SseEndpoint.cs`

> **Без unit-теста:** эндпоинт — тонкий клей поверх `SseService` (контракт которого покрыт Task 1-3). В кодовой базе нет endpoint-уровневых тестов; проверяем вручную (Step 3). Это сознательное решение, а не пропуск TDD.

- [ ] **Step 1: Переписать обработчик `/events`**

Замени метод `Map` в `SseEndpoint.cs` целиком:

```csharp
    public void Map(IEndpointRouteBuilder endpoints)
    {
        var lifetime = endpoints.ServiceProvider.GetRequiredService<IHostApplicationLifetime>();

        endpoints.MapGet("/events", async ctx =>
        {
            if (!sseService.IsRunning)
            {
                ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }

            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            var bufferingFeature = ctx.Features.Get<IHttpResponseBodyFeature>();
            bufferingFeature?.DisableBuffering();

            await ctx.Response.Body.FlushAsync();

            // retry-преамбула: единственная прямая запись в Body, строго ДО AddClient,
            // т.к. AddClient запускает writer-loop, пишущий в тот же поток.
            await ctx.Response.Body.WriteAsync("retry: 3000\n\n"u8.ToArray());
            await ctx.Response.Body.FlushAsync();

            if (!sseService.AddClient(ctx.Response))
            {
                // сервис остановили между проверкой IsRunning и AddClient —
                // завершаем ответ, клиент получит EOF и переподключится.
                return;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted,
                lifetime.ApplicationStopping);

            try
            {
                await Task.Delay(Timeout.Infinite, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // client disconnected or app stopping
            }
            finally
            {
                sseService.RemoveClient(ctx.Response);
            }
        });
    }
```

- [ ] **Step 2: Добавить недостающий using**

В начало `SseEndpoint.cs` добавь (для `StatusCodes`):

```csharp
using Microsoft.AspNetCore.Http;
```

- [ ] **Step 3: Сборка + ручная проверка**

Run: `dotnet build PoproshaykaBot.sln`
Expected: BUILD succeeded, 0 errors.

Ручная проверка (после реализации Task 5 удобнее, но базово):
1. Запусти приложение, открой `http://localhost:{port}/events` в браузере — поток открывается (`text/event-stream`), приходят `event: ping` каждые 30с.
2. В DevTools → Network → EventStream видно `retry` и события `ping`.

- [ ] **Step 4: Commit**

```bash
git add PoproshaykaBot.Core/Server/Endpoints/SseEndpoint.cs
git commit -m "SSE: 503 при остановленном сервисе и retry-преамбула"
```

---

## Task 5: Клиент — `obs.js`: watchdog + reconnect + backfill + ping/markAlive

**Files:**
- Modify: `PoproshaykaBot.Core/Assets/obs.js`

> **Без автотестов:** JS-тест-инфры в репозитории нет. Проверка ручная (Step 6). При реализации сверяйся с текущей структурой `obs.js`.

- [ ] **Step 1: Добавить состояние watchdog и `markAlive()`**

В `obs.js`, рядом с верхними `let`-объявлениями (например, сразу после блока `const seenMessageIds`/`seenMessageIdsOrder`, ~строка 41), добавь:

```javascript
    let eventSource = null;
    let lastEventTime = Date.now();
    let staleThresholdMs = 70000;
    let reconnecting = false;
    let watchdogIntervalId = null;
    const watchdogTickMs = 10000;

    function markAlive() {
        lastEventTime = Date.now();
    }
```

- [ ] **Step 2: Переписать `initEventSource()` — модульный `eventSource`, `markAlive`, `ping`, реакция `onerror`**

Замени функцию `initEventSource` (строки ~313-352) целиком:

```javascript
    function initEventSource() {
        if (eventSource) {
            try { eventSource.close(); } catch (e) { /* noop */ }
        }

        eventSource = new EventSource('/events');

        eventSource.addEventListener('message', function (event) {
            markAlive();
            try {
                addMessage(JSON.parse(event.data));
            } catch (e) {
                console.error('Ошибка парсинга SSE message:', e);
            }
        });

        eventSource.addEventListener('clear', function () {
            markAlive();
            clearChat();
        });

        eventSource.addEventListener('chat_settings_changed', function (event) {
            markAlive();
            try {
                updateChatSettings(JSON.parse(event.data));
            } catch (e) {
                console.error('Ошибка парсинга SSE chat_settings_changed:', e);
            }
        });

        eventSource.addEventListener('ping', function (event) {
            markAlive();
            try {
                const payload = JSON.parse(event.data);
                if (typeof payload.intervalSeconds === 'number' && payload.intervalSeconds > 0) {
                    staleThresholdMs = Math.max(70000, payload.intervalSeconds * 2000 + 10000);
                }
            } catch (e) {
                console.debug('Не удалось разобрать ping SSE:', e);
            }
        });

        eventSource.addEventListener('dropped', function (event) {
            markAlive();
            try {
                const payload = JSON.parse(event.data);
                console.warn(
                    'Сервер сбросил сообщения SSE. Глобальная очередь:',
                    payload.count,
                    '| По клиентам:',
                    payload.clientCount);
            } catch (e) {
                console.warn('Сервер сбросил сообщения SSE (детали недоступны).', e);
            }
        });

        eventSource.onerror = function (event) {
            console.error('SSE ошибка:', event);
            if (eventSource && eventSource.readyState === EventSource.CLOSED) {
                reconnect();
            }
        };
    }
```

- [ ] **Step 3: Добавить `loadHistoryAndBackfill()`, `reconnect()`, `startWatchdog()`**

Добавь эти функции рядом с `initOverlay` (внизу файла, перед `initOverlay()`):

```javascript
    function loadHistoryAndBackfill() {
        return fetch('/api/history')
            .then(response => response.json())
            .then(messages => messages.forEach(message => addMessage(message, true)));
    }

    function reconnect() {
        if (reconnecting) {
            return;
        }
        reconnecting = true;
        console.warn('SSE: переподключение оверлея');

        if (eventSource) {
            try { eventSource.close(); } catch (e) { /* noop */ }
        }

        loadHistoryAndBackfill()
            .catch(error => console.error('Ошибка догрузки истории при реконнекте:', error))
            .finally(() => {
                showReloadBanner();
                initEventSource();
                markAlive();
                reconnecting = false;
            });
    }

    function startWatchdog() {
        if (watchdogIntervalId !== null) {
            return;
        }
        watchdogIntervalId = setInterval(() => {
            if (reconnecting) {
                return;
            }
            const stale = Date.now() - lastEventTime > staleThresholdMs;
            const closed = eventSource !== null && eventSource.readyState === EventSource.CLOSED;
            if (stale || closed) {
                reconnect();
            }
        }, watchdogTickMs);
    }
```

- [ ] **Step 4: Переписать `initOverlay()` — переиспользовать backfill, поднять markAlive + watchdog**

Замени функцию `initOverlay` (строки ~840-852) целиком:

```javascript
    function initOverlay() {
        fetch('/api/chat-settings')
            .then(response => response.json())
            .then(settings => updateChatSettings(settings))
            .then(() => loadHistoryAndBackfill())
            .catch(error => console.error('Ошибка инициализации оверлея:', error))
            .finally(() => {
                showReloadBanner();
                initEventSource();
                markAlive();
                startWatchdog();
            });
    }
```

> Дедуп сохраняется: `seenMessageIds` — модульное состояние, переживает reconnect, поэтому бэкфилл `/api/history` не задвоит уже показанные сообщения.

- [ ] **Step 5: Сборка (asset попадает в сборку как EmbeddedResource)**

Run: `dotnet build PoproshaykaBot.sln`
Expected: BUILD succeeded, 0 errors.

- [ ] **Step 6: Ручная проверка восстановления**

1. Запусти приложение, открой `http://localhost:{port}/chat` в Chrome, открой DevTools (Console + Network).
2. **Пульс наблюдаем:** в Network → запрос `/events` → вкладка EventStream показывает события `ping` каждые ~30с; в Console нет ошибок.
3. **Авто-восстановление:** DevTools → Network → переключи на **Offline**, подожди > порога тишины (для скорости можно временно выставить в `obs.js` `staleThresholdMs = 15000` и `watchdogTickMs = 3000`, пересобрать), затем верни **Online**. Ожидание: в Console «SSE: переподключение оверлея», появляется баннер «🔁 Чат-источник перезагружен», история догружается, живые сообщения снова идут, дубликатов нет.
4. Верни временные значения `staleThresholdMs`/`watchdogTickMs` обратно (70000 / 10000), если менял; пересобери.

- [ ] **Step 7: Commit**

```bash
git add PoproshaykaBot.Core/Assets/obs.js
git commit -m "OBS-оверлей: watchdog и авто-переподключение SSE"
```

---

## Task 6: Памятка по настройкам OBS (угол E)

**Files:**
- Modify: `README.md`
- Modify: `PoproshaykaBot.WinForms/Forms/Onboarding/Pages/HealthCheckPage.cs`

> При правке `HealthCheckPage.cs` (UI-инициализация) уместно свериться со скиллом `winforms-poproshaykabot`, хотя изменение — только текст существующего лейбла, без дизайнера/триплета.

- [ ] **Step 1: Добавить секцию в README.md**

Добавь в `README.md` секцию (место — рядом с описанием OBS-оверлея; если такого раздела нет, добавь новый раздел верхнего уровня):

```markdown
## Настройка OBS Browser Source

Адрес оверлея: `http://localhost:<порт>/chat` (порт см. в настройках HTTP-сервера).

В свойствах Browser Source **снимите** галочки:
- «Shutdown source when not visible» (Выключать источник, когда не виден);
- «Refresh browser when scene becomes active» (Обновлять браузер при активации сцены).

Иначе при каждом переключении сцены источник перезагружается, и оверлей кратко «моргает».
Оверлей сам переподключается при обрыве связи, поэтому ручное обновление источника обычно не требуется.
```

- [ ] **Step 2: Добавить подсказку у URL оверлея в онбординге**

В `HealthCheckPage.cs`, в методе `OnEnter` (строки ~40-41), замени установку текста:

```csharp
        var url = $"http://localhost:{context.Settings.Twitch.HttpServerPort}/chat";
        _overlayUrlLabel.Text = $"Адрес для Browser Source в OBS: {url}";
```

на:

```csharp
        var url = $"http://localhost:{context.Settings.Twitch.HttpServerPort}/chat";
        _overlayUrlLabel.Text =
            $"Адрес для Browser Source в OBS: {url}{Environment.NewLine}" +
            "Совет: в свойствах источника снимите «Shutdown source when not visible» и " +
            "«Refresh browser when scene becomes active», иначе оверлей перезагружается при смене сцены.";
```

- [ ] **Step 3: Сборка + быстрая визуальная проверка**

Run: `dotnet build PoproshaykaBot.sln`
Expected: BUILD succeeded, 0 errors.

Визуально: запусти приложение так, чтобы открылся онбординг (или открой страницу диагностики), и убедись, что подсказка читается и не обрезается (лейбл `_overlayUrlLabel` — AutoSize; при необходимости проверь, что текст в две строки помещается).

- [ ] **Step 4: Commit**

```bash
git add README.md PoproshaykaBot.WinForms/Forms/Onboarding/Pages/HealthCheckPage.cs
git commit -m "Памятка по настройке OBS Browser Source"
```

---

## Task 7: Финальная проверка

- [ ] **Step 1: Полная сборка**

Run: `dotnet build PoproshaykaBot.sln`
Expected: BUILD succeeded, 0 errors (предупреждения-анализаторы допустимы — гейт «ноль ошибок», не «ноль предупреждений»).

- [ ] **Step 2: Полный прогон серверных тестов**

Run: `dotnet test PoproshaykaBot.Core.Tests/PoproshaykaBot.Core.Tests.csproj`
Expected: PASS, включая все `SseServiceTests` (старые + новые).

- [ ] **Step 3: Итоговая ручная проверка устойчивости (см. Task 5, Step 6)**

Чек-лист:
- [ ] `ping` приходит каждые ~30с (Network → EventStream).
- [ ] Offline > порога → авто-reconnect с баннером и догрузкой истории, без дубликатов.
- [ ] Подсказка по OBS видна в онбординге; секция в README на месте.

---

## Покрытие спеки (self-review)

| Раздел спеки | Задача |
|---|---|
| 1. Пульс `ping` | Task 2 |
| 2. 503 / `IsRunning` / `AddClient→bool` | Task 1 (контракт) + Task 4 (эндпоинт) |
| 3. Таймаут записи (D/F) | Task 3 |
| 4. `retry:` преамбула | Task 4 |
| 5. Клиент: watchdog/reconnect/backfill/markAlive/onerror | Task 5 |
| 6. Памятка OBS (README + UI) | Task 6 |
| Тесты (сервер NUnit; клиент — вручную) | Task 1-3 (NUnit), Task 5/7 (ручная) |
