using KeepShell.Services;
using KeepShell.Services.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Obs;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace PoproshaykaBot.Wpf.Tests.Settings;

[TestFixture]
public sealed class ObsIntegrationSectionTests
{
    private DirectoryInfo _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("obs-integration-section");
    }

    [TearDown]
    public void TearDown()
    {
        _directory.Delete(recursive: true);
    }

    [Test]
    public void Load_WhenObsOffline_DoesNotMarkSavedSourcesAsMissing()
    {
        var vm = CreateViewModel(new FakeObsWebSocketClient(), out _);

        vm.Load(new ObsIntegrationSettings
        {
            Enabled = true,
            DashboardSourceNames = ["Старый микрофон"],
            ChatRefreshSources = ["Чатик попрошайка"],
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.DashboardSources.Single().IsMissingInObs, Is.False);
            Assert.That(vm.ChatRefreshSources.Single().IsMissingInObs, Is.False);
            Assert.That(vm.HasMissingDashboardSources, Is.False);
            Assert.That(vm.HasMissingChatRefreshSources, Is.False);
        }
    }

    [Test]
    public async Task TestConnectionAsync_WhenObsOnline_MarksMissingSourcesAndUpdatesWarningOnUncheck()
    {
        var client = new FakeObsWebSocketClient();
        client.EnqueueResponse("GetVersion", """{"obsVersion":"31.0.0","obsWebSocketVersion":"5.5.2"}""");
        client.EnqueueResponse("GetSceneList", """{"currentProgramSceneName":"Main","scenes":[{"sceneName":"Main"}]}""");
        client.EnqueueResponse("GetInputList", """
                                                {
                                                  "inputs": [
                                                    {"inputName": "Mic/Aux", "inputKind": "wasapi_input_capture", "unversionedInputKind": "wasapi_input_capture"},
                                                    {"inputName": "Chat Overlay", "inputKind": "browser_source", "unversionedInputKind": "browser_source"}
                                                  ]
                                                }
                                                """);
        client.EnqueueResponse("GetInputList", """
                                                {
                                                  "inputs": [
                                                    {"inputName": "Mic/Aux", "inputKind": "wasapi_input_capture", "unversionedInputKind": "wasapi_input_capture"},
                                                    {"inputName": "Chat Overlay", "inputKind": "browser_source", "unversionedInputKind": "browser_source"}
                                                  ]
                                                }
                                                """);

        var vm = CreateViewModel(client, out _);
        vm.Load(new ObsIntegrationSettings
        {
            Enabled = true,
            DashboardSourceNames = ["Mic/Aux", "Отключённый микшер"],
            ChatRefreshSources = ["Чатик попрошайка", "Chat Overlay"],
        });

        await vm.TestConnectionCommand.ExecuteAsync(null);

        var missingDashboard = vm.DashboardSources.Single(i => i.Name == "Отключённый микшер");
        var presentDashboard = vm.DashboardSources.Single(i => i.Name == "Mic/Aux");
        var missingChat = vm.ChatRefreshSources.Single(i => i.Name == "Чатик попрошайка");
        var presentChat = vm.ChatRefreshSources.Single(i => i.Name == "Chat Overlay");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(missingDashboard.IsMissingInObs, Is.True);
            Assert.That(presentDashboard.IsMissingInObs, Is.False);
            Assert.That(missingChat.IsMissingInObs, Is.True);
            Assert.That(presentChat.IsMissingInObs, Is.False);
            Assert.That(vm.HasMissingDashboardSources, Is.True);
            Assert.That(vm.MissingDashboardSourcesWarning, Does.Contain("Отключённый микшер"));
            Assert.That(vm.HasMissingChatRefreshSources, Is.True);
            Assert.That(vm.MissingChatRefreshSourcesWarning, Does.Contain("Чатик попрошайка"));
        }

        missingChat.IsChecked = false;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.HasMissingChatRefreshSources, Is.False);
            Assert.That(vm.MissingChatRefreshSourcesWarning, Is.Empty);
        }
    }

    [Test]
    public async Task TestConnectionAsync_WhenSubsequentCheckFails_ClearsStaleMissingMarkers()
    {
        var client = new FakeObsWebSocketClient();
        client.EnqueueResponse("GetVersion", """{"obsVersion":"31.0.0","obsWebSocketVersion":"5.5.2"}""");
        client.EnqueueResponse("GetSceneList", """{"currentProgramSceneName":"Main","scenes":[{"sceneName":"Main"}]}""");
        client.EnqueueResponse("GetInputList", """{"inputs":[{"inputName":"Chat Overlay","inputKind":"browser_source","unversionedInputKind":"browser_source"}]}""");
        client.EnqueueResponse("GetInputList", """{"inputs":[{"inputName":"Chat Overlay","inputKind":"browser_source","unversionedInputKind":"browser_source"}]}""");

        var vm = CreateViewModel(client, out _);
        vm.Load(new ObsIntegrationSettings
        {
            Enabled = true,
            ChatRefreshSources = ["Чатик попрошайка"],
        });

        await vm.TestConnectionCommand.ExecuteAsync(null);
        Assert.That(vm.HasMissingChatRefreshSources, Is.True);

        client.FailNextConnect = true;
        await vm.TestConnectionCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.ChatRefreshSources.Single(i => i.Name == "Чатик попрошайка").IsMissingInObs, Is.False);
            Assert.That(vm.HasMissingChatRefreshSources, Is.False);
            Assert.That(vm.MissingChatRefreshSourcesWarning, Is.Empty);
        }
    }

    private ObsIntegrationSectionViewModel CreateViewModel(IObsWebSocketClient client, out SettingsManager settingsManager)
    {
        settingsManager = new SettingsManager(
            NullLogger<SettingsManager>.Instance,
            Path.Combine(_directory.FullName, "settings.json"));
        var obsService = new ObsIntegrationService(client, settingsManager, NullLogger<ObsIntegrationService>.Instance);

        return new ObsIntegrationSectionViewModel(
            obsService,
            settingsManager,
            NullLogger<ObsIntegrationSectionViewModel>.Instance,
            DispatchProxy.Create<IDialogService, InertProxy>(),
            DispatchProxy.Create<IClipboardService, InertProxy>());
    }

    private sealed class FakeObsWebSocketClient : IObsWebSocketClient
    {
        private readonly Dictionary<string, Queue<JsonElement?>> _responses = new(StringComparer.Ordinal);

        public event EventHandler<ObsWebSocketEventArgs>? EventReceived
        {
            add { }
            remove { }
        }

        public bool IsConnected { get; private set; }

        public DateTimeOffset? LastExchangeAt { get; private set; }

        public bool FailNextConnect { get; set; }

        public Task<ObsConnectionSnapshot> ConnectAsync(ObsConnectionOptions options, CancellationToken cancellationToken)
        {
            if (FailNextConnect)
            {
                IsConnected = false;
                return Task.FromResult(new ObsConnectionSnapshot(false, null, null, "OBS недоступен"));
            }

            IsConnected = true;
            return Task.FromResult(new ObsConnectionSnapshot(true, "31.0.0", "5.5.2", null));
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public Task<JsonElement?> SendRequestAsync(string requestType, object? requestData, CancellationToken cancellationToken)
        {
            if (_responses.TryGetValue(requestType, out var queue) && queue.Count > 0)
            {
                LastExchangeAt = DateTimeOffset.UtcNow;
                return Task.FromResult(queue.Dequeue());
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

            using var document = JsonDocument.Parse(json);
            queue.Enqueue(document.RootElement.Clone());
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    public class InertProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.ReturnType.IsValueType == true
                ? Activator.CreateInstance(targetMethod.ReturnType)
                : null;
        }
    }
}
