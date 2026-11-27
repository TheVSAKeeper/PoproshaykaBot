using KeepShell.Bootstrap;
using KeepShell.Testing;
using KeepShell.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Obs;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.IO;
using System.Text.Json;

namespace PoproshaykaBot.Wpf.Tests.Tiles;

[TestFixture]
public class ObsTileTests
{
    private const long TotalFrames = 1000;

    [TestCase(0L, null, "Кадры не теряются", TestName = "Дропов_нет_нейтрально")]
    [TestCase(3L, null, "Пропущено кадров: 3 (0,3 %)", TestName = "Доли_процента_нейтрально")]
    [TestCase(10L, "Warning", "Пропущено кадров: 10 (1 %)", TestName = "Один_процент_жёлтый")]
    [TestCase(30L, "Warning", "Пропущено кадров: 30 (3 %)", TestName = "Три_процента_жёлтый")]
    [TestCase(50L, "Warning", "Пропущено кадров: 50 (5 %)", TestName = "Пять_процентов_ещё_жёлтый")]
    [TestCase(70L, "Error", "Пропущено кадров: 70 (7 %)", TestName = "Семь_процентов_красный")]
    public void Дропы_окрашены_по_порогу(long skipped, string? severity, string text)
    {
        var health = ObsStreamHealth.Describe(true, 0D, skipped, TotalFrames);

        Assert.That(health, Is.Not.Null);
        Assert.That(health!.Value, Is.EqualTo((text, severity)));
    }

    [Test]
    public void Перегрузка_сети_видна_когда_дропы_нейтральны_и_не_заслоняет_красные()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ObsStreamHealth.Describe(true, 0.2D, 3L, TotalFrames),
                Is.EqualTo(("Сеть перегружена: 20 %", (string?)"Warning")));
            Assert.That(ObsStreamHealth.Describe(true, 0.2D, 70L, TotalFrames)?.Severity, Is.EqualTo("Error"));
            Assert.That(ObsStreamHealth.Describe(false, 0.2D, 70L, TotalFrames), Is.Null,
                "Без эфира о здоровье эфира говорить нечего");
        }
    }

    [TestCase(ObsSourceMeterState.Active, "-3,5 дБ", true)]
    [TestCase(ObsSourceMeterState.Muted, "выключен", false)]
    [TestCase(ObsSourceMeterState.Missing, "не найден в OBS", false)]
    public void Состояние_аудиоисточника_названо_один_раз(ObsSourceMeterState state, string stateText, bool showsLevel)
    {
        const string name = "Звук рабочего стола";
        var meter = new ObsSourceMeterViewModel(name);

        switch (state)
        {
            case ObsSourceMeterState.Active:
                meter.ShowActive(name, -3.5D);
                break;
            case ObsSourceMeterState.Muted:
                meter.ShowMuted(name);
                break;
            default:
                meter.ShowMissing(name);
                break;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(meter.State, Is.EqualTo(state));
            Assert.That(meter.DisplayName, Is.EqualTo(name), "Имя источника идёт как есть, без пометок состояния");
            Assert.That(meter.StateText, Is.EqualTo(stateText));
            Assert.That(meter.ShowsLevel, Is.EqualTo(showsLevel),
                "Полоса уровня есть только у включённого источника – у выключенного пустая полоса повторяла бы подпись");
        }
    }

    [TestCase(ObsOutputCardKind.Stream, true, false, "В эфире", true, false)]
    [TestCase(ObsOutputCardKind.Stream, false, false, "Эфир не идёт", false, false)]
    [TestCase(ObsOutputCardKind.Record, true, false, "Идёт запись", true, true)]
    [TestCase(ObsOutputCardKind.Record, true, true, "Запись на паузе", true, true)]
    [TestCase(ObsOutputCardKind.Record, false, false, "Запись не идёт", false, false)]
    public void Карточка_выхода_называет_состояние_одной_строкой(
        ObsOutputCardKind kind,
        bool active,
        bool paused,
        string title,
        bool running,
        bool showsPause)
    {
        var card = new ObsOutputCardViewModel(kind, null!, NullLogger.Instance);

        card.ApplySnapshot(active, paused, "00:00:00.000", null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(card.Title, Is.EqualTo(title));
            Assert.That(card.IsRunning, Is.EqualTo(running), "Таймер виден только у идущего выхода");
            Assert.That(card.ShowsSecondaryButton, Is.EqualTo(showsPause), "Пауза есть только у идущей записи");
        }
    }

    [TestCase(true, false, 0L, 0D, "В эфире", "Success", "Кадры не теряются", null)]
    [TestCase(true, true, 0L, 0D, "Переподключение…", "Warning", "Кадры не теряются", null)]
    [TestCase(true, false, 20L, 0D, "В эфире", "Success", "Пропущено кадров: 20 (2 %)", "Warning")]
    [TestCase(true, false, 70L, 0.2D, "В эфире", "Success", "Пропущено кадров: 70 (7 %)", "Error")]
    [TestCase(false, false, 70L, 0D, "Не идёт", null, null, null)]
    public void Доп_выход_отображает_статус_и_пороги_дропов(
        bool active,
        bool reconnecting,
        long skipped,
        double congestion,
        string stateText,
        string? expectedStateSeverity,
        string? expectedHealthText,
        string? expectedHealthSeverity)
    {
        var snapshot = new ObsSecondaryOutputSnapshot(
            "YouTube",
            "aitum_multi_output_YouTube",
            "rtmp_output",
            ObsMultistreamVendor.AitumMultistream,
            active,
            reconnecting,
            active ? "00:12:34.000" : null,
            active ? 754000 : null,
            active ? 2_097_152 : null,
            congestion,
            skipped,
            TotalFrames,
            CanControl: true,
            IsVertical: false);

        var row = new ObsSecondaryOutputViewModel(snapshot, _ => Task.CompletedTask);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.StateText, Is.EqualTo(stateText));
            Assert.That(row.StateSeverity, Is.EqualTo(expectedStateSeverity));
            Assert.That(row.Health, Is.EqualTo(expectedHealthText));
            Assert.That(row.HealthSeverity, Is.EqualTo(expectedHealthSeverity));
            Assert.That(row.BytesText, Is.EqualTo(active ? "2 МБ" : null));
            Assert.That(row.ActionButtonText, Is.EqualTo(active ? "■ Стоп" : "▶ Старт"));
        }
    }

    [Test]
    public async Task Плитка_OBS_без_ограничения_ширины_380_показывает_доп_выходы_и_защищает_старт_без_эфира()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"obs-tile-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var store = new ObsIntegrationStore(
                bus,
                NullLogger<ObsIntegrationStore>.Instance,
                Path.Combine(tempDir, "obs-integration.json"));
            store.Save(new ObsIntegrationSettings { Enabled = true, AutoConnect = false });

            var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(tempDir, "settings.json"));
            var client = new FakeTileObsClient();
            using var integration = new ObsIntegrationService(client, settingsManager, NullLogger<ObsIntegrationService>.Instance);
            var dispatcher = new ManualUiDispatcher();
            var toasts = new ToastHostViewModel(dispatcher);
            var settingsStore = new MemorySettings();
            var preferences = new ShellPreferences(settingsStore);

            using var tile = new ObsInfoTileViewModel(
                store,
                integration,
                client,
                bus,
                NullLogger<ObsInfoTileViewModel>.Instance,
                dispatcher,
                toasts,
                preferences);

            Assert.That(tile.MaxWidth, Is.Null);

            tile.BeginPreview(new(
                new(true, "33.0.1", "5.7.6", null),
                "Main",
                IsStreaming: false,
                StreamTimecode: null,
                StreamCongestion: null,
                StreamSkippedFrames: null,
                StreamTotalFrames: null,
                IsRecording: false,
                IsRecordingPaused: false,
                RecordTimecode: null,
                RecordBytes: null,
                [],
                DateTimeOffset.Now,
                [
                    new("YouTube", "aitum_multi_output_YouTube", "rtmp_output", ObsMultistreamVendor.AitumMultistream,
                        IsActive: false, IsReconnecting: false, null, null, null, null, null, null,
                        CanControl: true, IsVertical: false),
                ]));

            tile.EndPreview();

            client.EnqueueResponse("GetVersion", """{"obsVersion":"33.0.1","obsWebSocketVersion":"5.7.6"}""");
            client.EnqueueResponse("GetCurrentProgramScene", """{"currentProgramSceneName":"Main"}""");
            client.EnqueueResponse("GetStreamStatus", """{"outputActive":false}""");
            client.EnqueueResponse("GetRecordStatus", """{"outputActive":false}""");
            client.EnqueueResponse("GetInputList", """{"inputs":[]}""");
            client.EnqueueVendorResponse("aitum-stream-suite", "version", code: 600);
            client.EnqueueVendorResponse("aitum-multistream", "version", """{"responseData":{"version":"1.0.9","success":true}}""");
            client.EnqueueVendorResponse("aitum-multistream", "get_outputs", """{"responseData":{"outputs":[{"name":"YouTube","vertical":false}],"success":true}}""");
            client.EnqueueResponse("GetOutputList", """{"outputs":[]}""");

            await tile.RefreshCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(tile.ShowSecondaryOutputs, Is.True);
                Assert.That(tile.SecondaryOutputs, Has.Count.EqualTo(1));
                Assert.That(tile.SecondaryOutputs[0].Name, Is.EqualTo("YouTube"));
                Assert.That(tile.SecondaryOutputs[0].IsActive, Is.False);
            }

            await tile.SecondaryOutputs[0].ToggleCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(toasts.Toasts, Has.Count.EqualTo(1));
                Assert.That(toasts.Toasts[0].Severity, Is.EqualTo(StatusSeverity.Warning));
                Assert.That(toasts.Toasts[0].Message, Is.EqualTo("Сначала запустите основной эфир OBS"));
                Assert.That(client.VendorRequestsSent("start_output"), Is.Zero);
            }

            toasts.DismissCommand.Execute(toasts.Toasts[0]);
            preferences.EnableToastNotifications = false;
            await tile.SecondaryOutputs[0].ToggleCommand.ExecuteAsync(null);
            Assert.That(toasts.Toasts, Is.Empty);

            using var doc = JsonDocument.Parse("""{"vendorName":"aitum-multistream","eventType":"output_started","eventData":{"name":"aitum_multi_output_YouTube"}}""");
            client.RaiseEvent(new("VendorEvent", 512, doc.RootElement.Clone()));

            Assert.That(tile.SecondaryOutputs[0].IsActive, Is.True);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Test]
    public async Task Плитка_OBS_выделяет_микрофон_сверху_без_мута_и_держит_каркас_при_обрыве()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"obs-tile-resilience-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var store = new ObsIntegrationStore(
                bus,
                NullLogger<ObsIntegrationStore>.Instance,
                Path.Combine(tempDir, "obs-integration.json"));
            store.Save(new ObsIntegrationSettings
            {
                Enabled = true,
                AutoConnect = false,
                DashboardSourceNames = ["Звук рабочего стола", "Основной микрофон"],
            });

            var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(tempDir, "settings.json"));
            var client = new FakeTileObsClient();
            using var integration = new ObsIntegrationService(client, settingsManager, NullLogger<ObsIntegrationService>.Instance);
            var dispatcher = new ManualUiDispatcher();

            using var tile = new ObsInfoTileViewModel(
                store,
                integration,
                client,
                bus,
                NullLogger<ObsInfoTileViewModel>.Instance,
                dispatcher);

            client.EnqueueResponse("GetVersion", """{"obsVersion":"33.0.1","obsWebSocketVersion":"5.7.6"}""");
            client.EnqueueResponse("GetCurrentProgramScene", """{"currentProgramSceneName":"Main"}""");
            client.EnqueueResponse("GetStreamStatus", """{"outputActive":true,"outputTimecode":"00:10:00.000"}""");
            client.EnqueueResponse("GetRecordStatus", """{"outputActive":false}""");
            client.EnqueueResponse("GetInputList", """{"inputs":[{"inputName":"Звук рабочего стола","inputKind":"wasapi_output_capture"},{"inputName":"Основной микрофон","inputKind":"wasapi_input_capture"}]}""");
            client.EnqueueResponse("GetInputMute", """{"inputMuted":false}""");
            client.EnqueueResponse("GetInputVolume", """{"inputVolumeDb":-6.0,"inputVolumeMul":0.5}""");
            client.EnqueueResponse("GetInputMute", """{"inputMuted":true}""");
            client.EnqueueResponse("GetInputVolume", """{"inputVolumeDb":-100.0,"inputVolumeMul":0.0}""");
            client.EnqueueVendorResponse("aitum-stream-suite", "version", code: 600);
            client.EnqueueVendorResponse("aitum-multistream", "version", """{"responseData":{"version":"1.0.9","success":true}}""");
            client.EnqueueVendorResponse("aitum-multistream", "get_outputs", """{"responseData":{"outputs":[{"name":"YouTube","vertical":false}],"success":true}}""");
            client.EnqueueVendorResponse("aitum-multistream", "get_output_status", """{"responseData":{"active":true,"reconnecting":false,"success":true}}""");
            client.EnqueueResponse("GetOutputList", """{"outputs":[]}""");

            await tile.RefreshCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(tile.ForegroundMicrophone, Is.Not.Null);
                Assert.That(tile.ForegroundMicrophone!.SourceName, Is.EqualTo("Основной микрофон"));
                Assert.That(tile.ForegroundMicrophone.State, Is.EqualTo(ObsSourceMeterState.Muted));
                Assert.That(tile.ForegroundMicrophone.MicrophoneStatusText, Is.EqualTo("Микрофон выключен"));
                Assert.That(tile.OtherAudioSources.Select(static x => x.SourceName), Is.EqualTo(new[] { "Звук рабочего стола" }));
                Assert.That(tile.HasOutputs, Is.True);
                Assert.That(tile.ReconnectBannerText, Is.Null);
            }

            await client.DisconnectAsync(CancellationToken.None);
            client.FailConnect = true;
            await tile.RefreshCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(tile.HasOutputs, Is.True, "Каркас карточек Эфир/Запись не должен схлопываться при обрыве связи.");
                Assert.That(tile.ReconnectBannerText, Is.EqualTo("Связь с OBS потеряна – переподключение…"));
                Assert.That(tile.StreamCard.State, Is.EqualTo(ObsOutputCardState.Unknown));
                Assert.That(tile.RecordCard.State, Is.EqualTo(ObsOutputCardState.Unknown));
                Assert.That(tile.ForegroundMicrophone, Is.Not.Null);
                Assert.That(tile.ForegroundMicrophone!.State, Is.EqualTo(ObsSourceMeterState.Reconnecting));
                Assert.That(tile.ForegroundMicrophone.MicrophoneStatusText, Is.EqualTo("Переподключение…"));
                Assert.That(tile.ShowSecondaryOutputs, Is.True);
                Assert.That(tile.SecondaryOutputs, Has.Count.EqualTo(1));
                Assert.That(tile.SecondaryOutputs[0].IsDisconnected, Is.True);
                Assert.That(tile.SecondaryOutputs[0].ToggleCommand.CanExecute(null), Is.False);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Test]
    public async Task Двухшаговое_подтверждение_защищает_стоп_эфира_записи_и_доп_выхода_от_случайного_клика()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"obs-stop-guard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var client = new FakeTileObsClient();
            await client.ConnectAsync(new("127.0.0.1", 4455, string.Empty), CancellationToken.None);
            var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(tempDir, "settings.json"));
            using var integration = new ObsIntegrationService(client, settingsManager, NullLogger<ObsIntegrationService>.Instance);
            var dispatcher = new ManualUiDispatcher();

            using var streamCard = new ObsOutputCardViewModel(ObsOutputCardKind.Stream, integration, NullLogger.Instance, dispatcher);
            streamCard.ApplySnapshot(active: true, paused: false, "00:05:00.000", null);

            await streamCard.InvokePrimaryCommand.ExecuteAsync(null);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(streamCard.IsStopConfirmationArmed, Is.True);
                Assert.That(streamCard.PrimaryButtonText, Is.EqualTo("Точно стоп?"));
                Assert.That(client.SentRequests.Count(static r => r.RequestType == "StopStream"), Is.Zero);
            }

            dispatcher.Timers[0].Tick();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(streamCard.IsStopConfirmationArmed, Is.False);
                Assert.That(streamCard.PrimaryButtonText, Is.EqualTo("■ Стоп"));
                Assert.That(client.SentRequests.Count(static r => r.RequestType == "StopStream"), Is.Zero);
            }

            await streamCard.InvokePrimaryCommand.ExecuteAsync(null);
            Assert.That(streamCard.IsStopConfirmationArmed, Is.True);
            streamCard.CancelStopConfirmationCommand.Execute(null);
            Assert.That(streamCard.IsStopConfirmationArmed, Is.False);
            Assert.That(client.SentRequests.Count(static r => r.RequestType == "StopStream"), Is.Zero);

            await streamCard.InvokePrimaryCommand.ExecuteAsync(null);
            await streamCard.InvokePrimaryCommand.ExecuteAsync(null);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(streamCard.IsStopConfirmationArmed, Is.False);
                Assert.That(client.SentRequests.Count(static r => r.RequestType == "StopStream"), Is.EqualTo(1));
            }

            using var recordCard = new ObsOutputCardViewModel(ObsOutputCardKind.Record, integration, NullLogger.Instance, dispatcher);
            recordCard.ApplySnapshot(active: true, paused: true, "00:02:00.000", null);
            await recordCard.InvokePrimaryCommand.ExecuteAsync(null);
            Assert.That(recordCard.IsStopConfirmationArmed, Is.True);
            Assert.That(client.SentRequests.Count(static r => r.RequestType == "StopRecord"), Is.Zero);
            recordCard.ApplySnapshot(active: false, paused: false, null, null);
            Assert.That(recordCard.IsStopConfirmationArmed, Is.False);

            var secondaryStopCalls = 0;
            var secondarySnapshot = new ObsSecondaryOutputSnapshot(
                "YouTube", "aitum_multi_output_YouTube", "rtmp_output", ObsMultistreamVendor.AitumMultistream,
                IsActive: true, IsReconnecting: false, "00:03:00.000", 500, 1024, 0, 0, 1000, CanControl: true, IsVertical: false);
            using var secondary = new ObsSecondaryOutputViewModel(
                secondarySnapshot,
                _ =>
                {
                    secondaryStopCalls++;
                    return Task.CompletedTask;
                },
                dispatcher);

            await secondary.ToggleCommand.ExecuteAsync(null);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(secondary.IsStopConfirmationArmed, Is.True);
                Assert.That(secondary.ActionButtonText, Is.EqualTo("Точно стоп?"));
                Assert.That(secondaryStopCalls, Is.Zero);
            }

            await secondary.ToggleCommand.ExecuteAsync(null);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(secondary.IsStopConfirmationArmed, Is.False);
                Assert.That(secondaryStopCalls, Is.EqualTo(1));
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Test]
    public async Task Просроченное_подтверждение_стопа_OBS_без_тика_таймера_не_останавливает_выход()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"obs-stop-deadline-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var client = new FakeTileObsClient();
            await client.ConnectAsync(new("127.0.0.1", 4455, string.Empty), CancellationToken.None);
            var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(tempDir, "settings.json"));
            using var integration = new ObsIntegrationService(client, settingsManager, NullLogger<ObsIntegrationService>.Instance);
            var dispatcher = new ManualUiDispatcher();
            var time = new ManualTimeProvider();

            using var streamCard = new ObsOutputCardViewModel(ObsOutputCardKind.Stream, integration, NullLogger.Instance, dispatcher, time);
            streamCard.ApplySnapshot(active: true, paused: false, "00:05:00.000", null);

            await streamCard.InvokePrimaryCommand.ExecuteAsync(null);
            Assert.That(streamCard.IsStopConfirmationArmed, Is.True);

            time.Advance(TimeSpan.FromSeconds(3.1));

            await streamCard.InvokePrimaryCommand.ExecuteAsync(null);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(client.SentRequests.Count(static r => r.RequestType == "StopStream"), Is.Zero,
                    "Клик после истечения 3 с без тика UI-таймера не должен останавливать эфир.");
                Assert.That(streamCard.IsStopConfirmationArmed, Is.True,
                    "Просроченный клик перевзводит подтверждение заново.");
            }

            time.Advance(TimeSpan.FromSeconds(1));
            await streamCard.InvokePrimaryCommand.ExecuteAsync(null);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(client.SentRequests.Count(static r => r.RequestType == "StopStream"), Is.EqualTo(1));
                Assert.That(streamCard.IsStopConfirmationArmed, Is.False);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Test]
    public async Task Плитка_OBS_не_называет_звук_рабочего_стола_микрофоном_когда_микрофон_не_найден()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"obs-tile-nomic-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var store = new ObsIntegrationStore(
                bus,
                NullLogger<ObsIntegrationStore>.Instance,
                Path.Combine(tempDir, "obs-integration.json"));
            store.Save(new ObsIntegrationSettings
            {
                Enabled = true,
                AutoConnect = false,
                DashboardSourceNames = ["Звук рабочего стола"],
            });

            var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(tempDir, "settings.json"));
            var client = new FakeTileObsClient();
            using var integration = new ObsIntegrationService(client, settingsManager, NullLogger<ObsIntegrationService>.Instance);
            var dispatcher = new ManualUiDispatcher();

            using var tile = new ObsInfoTileViewModel(
                store,
                integration,
                client,
                bus,
                NullLogger<ObsInfoTileViewModel>.Instance,
                dispatcher);

            client.EnqueueResponse("GetVersion", """{"obsVersion":"33.0.1","obsWebSocketVersion":"5.7.6"}""");
            client.EnqueueResponse("GetCurrentProgramScene", """{"currentProgramSceneName":"Main"}""");
            client.EnqueueResponse("GetStreamStatus", """{"outputActive":false}""");
            client.EnqueueResponse("GetRecordStatus", """{"outputActive":false}""");
            client.EnqueueResponse("GetInputList", """{"inputs":[{"inputName":"Звук рабочего стола","inputKind":"wasapi_output_capture"}]}""");
            client.EnqueueResponse("GetInputMute", """{"inputMuted":false}""");
            client.EnqueueResponse("GetInputVolume", """{"inputVolumeDb":-8.0,"inputVolumeMul":0.4}""");
            client.EnqueueVendorResponse("aitum-stream-suite", "version", code: 600);
            client.EnqueueVendorResponse("aitum-multistream", "version", code: 600);
            client.EnqueueResponse("GetOutputList", """{"outputs":[]}""");

            await tile.RefreshCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(tile.ForegroundMicrophone, Is.Not.Null);
                Assert.That(tile.ForegroundMicrophone!.State, Is.EqualTo(ObsSourceMeterState.Unidentified));
                Assert.That(tile.ForegroundMicrophone.MicrophoneStatusText, Is.EqualTo("Микрофон не определён"));
                Assert.That(tile.ForegroundMicrophone.ShowsLevel, Is.False);
                Assert.That(tile.AudioSources.Select(static x => x.SourceName), Is.EqualTo(new[] { "Звук рабочего стола" }));
                Assert.That(tile.OtherAudioSources.Select(static x => x.SourceName), Is.EqualTo(new[] { "Звук рабочего стола" }));
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private sealed class FakeTileObsClient : IObsWebSocketClient
    {
        private readonly Dictionary<string, Queue<Func<Task<JsonElement?>>>> _responses = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Vendor, string RequestType), Queue<Func<Task<JsonElement?>>>> _vendorResponses = new();

        public event EventHandler<ObsWebSocketEventArgs>? EventReceived;

        public bool IsConnected { get; private set; }

        public bool FailConnect { get; set; }

        public DateTimeOffset? LastExchangeAt { get; private set; }

        public List<(string RequestType, string? VendorRequestType)> SentRequests { get; } = [];

        public int VendorRequestsSent(string vendorRequestType)
        {
            return SentRequests.Count(item =>
                string.Equals(item.RequestType, "CallVendorRequest", StringComparison.Ordinal)
                && string.Equals(item.VendorRequestType, vendorRequestType, StringComparison.Ordinal));
        }

        public void RaiseEvent(ObsWebSocketEventArgs args)
        {
            EventReceived?.Invoke(this, args);
        }

        public Task<ObsConnectionSnapshot> ConnectAsync(ObsConnectionOptions options, CancellationToken cancellationToken)
        {
            if (FailConnect)
            {
                IsConnected = false;
                return Task.FromResult(new ObsConnectionSnapshot(false, null, null, "Соединение прервано"));
            }

            IsConnected = true;
            return Task.FromResult(new ObsConnectionSnapshot(true, "33.0.1", "5.7.6", null));
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public Task<JsonElement?> SendRequestAsync(string requestType, object? requestData, CancellationToken cancellationToken)
        {
            var payload = requestData is null ? (JsonElement?)null : JsonSerializer.SerializeToElement(requestData);
            string? vendorReq = null;
            if (string.Equals(requestType, "CallVendorRequest", StringComparison.Ordinal)
                && payload is { } element)
            {
                var vendor = element.GetProperty("vendorName").GetString() ?? string.Empty;
                vendorReq = element.GetProperty("requestType").GetString() ?? string.Empty;
                SentRequests.Add((requestType, vendorReq));

                if (_vendorResponses.TryGetValue((vendor, vendorReq), out var vendorQueue) && vendorQueue.Count > 0)
                {
                    LastExchangeAt = DateTimeOffset.UtcNow;
                    return vendorQueue.Dequeue().Invoke();
                }
            }
            else
            {
                SentRequests.Add((requestType, vendorReq));
            }

            if (_responses.TryGetValue(requestType, out var queue) && queue.Count > 0)
            {
                LastExchangeAt = DateTimeOffset.UtcNow;
                return queue.Dequeue().Invoke();
            }

            return Task.FromResult<JsonElement?>(null);
        }

        public void EnqueueResponse(string requestType, string json)
        {
            if (!_responses.TryGetValue(requestType, out var queue))
            {
                queue = new();
                _responses[requestType] = queue;
            }

            using var doc = JsonDocument.Parse(json);
            var clone = doc.RootElement.Clone();
            queue.Enqueue(() => Task.FromResult<JsonElement?>(clone));
        }

        public void EnqueueVendorResponse(string vendorName, string requestType, string? json = null, int? code = null)
        {
            if (!_vendorResponses.TryGetValue((vendorName, requestType), out var queue))
            {
                queue = new();
                _vendorResponses[(vendorName, requestType)] = queue;
            }

            if (code is { } errorCode)
            {
                queue.Enqueue(() => Task.FromException<JsonElement?>(new ObsRequestException("CallVendorRequest", errorCode, "No vendor")));
                return;
            }

            using var doc = JsonDocument.Parse(json!);
            var clone = doc.RootElement.Clone();
            queue.Enqueue(() => Task.FromResult<JsonElement?>(clone));
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
