using KeepShell.Mcp;
using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Dashboard;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Settings;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Settings.Update;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Update;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.IO;
using System.Reflection;

namespace PoproshaykaBot.Wpf.Tests.Settings;

[TestFixture]
public class SettingsPageRanksSaveTests
{
    private static readonly UserRank[] Saved =
    [
        new("♟", "ПЕШКА", 0, 3),
        new("♔", "КОРОЛЬ", 5000),
        new("♞", "КОНЬ", 300, 3),
    ];

    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "poproshayka-settings-ranks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }

    [Test]
    public async Task Ошибка_в_правленых_рангах_останавливает_сохранение_до_записи()
    {
        using var harness = new Harness(_root);
        var before = File.ReadAllText(harness.SettingsPath);

        harness.Page.Ranks.Entries[0].Emoji = "";
        await harness.Page.SaveCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Dialogs.Warnings, Has.Count.EqualTo(1));
            Assert.That(harness.Dialogs.Warnings[0], Does.Contain("«Ранги»"));
            Assert.That(harness.Dialogs.Infos, Is.Empty);
            Assert.That(File.ReadAllText(harness.SettingsPath), Is.EqualTo(before));
            Assert.That(harness.SettingsManager.Current.Ranks.Ranks, Is.EqualTo(Saved));
            Assert.That(harness.RanksChanged, Is.Zero);
            Assert.That(harness.Page.HasChanges, Is.True, "Черновик с ошибкой остаётся несохранённым");
        });
    }

    [Test]
    public async Task Сохранение_без_правки_рангов_оставляет_список_профиля_как_был()
    {
        using var harness = new Harness(_root);

        await harness.Page.SaveCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Dialogs.Infos, Has.Count.EqualTo(1));
            Assert.That(harness.SettingsManager.Current.Ranks.Ranks, Is.EqualTo(Saved), "Порядок файла не пересобран по порогу");
            Assert.That(harness.ReadRanksFromDisk(), Is.EqualTo(Saved));
            Assert.That(harness.RanksChanged, Is.Zero);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Правка_рангов_применяется_и_объявляется_а_на_диск_ложится_только_при_открытом_гейте(bool gateClosed)
    {
        using var harness = new Harness(_root);

        if (gateClosed)
        {
            await harness.CloseSettingsGateAsync();
        }

        harness.Page.Ranks.Entries[0].Name = "ИМПЕРАТОР";
        await harness.Page.SaveCommand.ExecuteAsync(null);

        UserRank[] edited =
        [
            new("♔", "ИМПЕРАТОР", 5000),
            new("♞", "КОНЬ", 300, 3),
            new("♟", "ПЕШКА", 0, 3),
        ];

        Assert.Multiple(() =>
        {
            Assert.That(harness.SettingsManager.Current.Ranks.Ranks, Is.EqualTo(edited), "Правка действует в памяти в обоих исходах");
            Assert.That(harness.RanksChanged, Is.EqualTo(1), "Лестница пересобирается по тому, что действует");
            Assert.That(harness.ReadRanksFromDisk(), Is.EqualTo(gateClosed ? Saved : edited));
            Assert.That(harness.Dialogs.Infos, Has.Count.EqualTo(1));
            Assert.That(harness.Dialogs.Infos[0], gateClosed ? Does.Contain("не записаны") : Does.Contain("успешно сохранены"));
        });
    }

    [Test]
    public async Task Сбой_после_записи_настроек_не_оставляет_лестницу_на_прежних_рангах()
    {
        using var harness = new Harness(_root);
        harness.UpdateStore.Save(new() { ApplyMode = UpdateApplyMode.SilentOnExit });
        harness.UpdateCoordinator.FailDiscard = true;

        harness.Page.Ranks.Entries[0].Name = "ИМПЕРАТОР";

        Assert.ThrowsAsync<InvalidOperationException>(() => harness.Page.SaveCommand.ExecuteAsync(null),
            "Сбой шага обновлений доходит до окна ошибки");

        Assert.Multiple(() =>
        {
            Assert.That(harness.ReadRanksFromDisk()[0].Name, Is.EqualTo("ИМПЕРАТОР"));
            Assert.That(harness.RanksChanged, Is.EqualTo(1), "Записанные ранги объявлены до шагов, которые могут упасть");
        });
    }

    private sealed class Harness : IDisposable
    {
        private readonly SettingsWriteGate _gate = new();
        private readonly IDisposable _subscription;

        public Harness(string root)
        {
            SettingsPath = Path.Combine(root, "settings.json");
            SettingsManager = new(NullLogger<SettingsManager>.Instance, SettingsPath, _gate);
            SettingsManager.SaveSettings(new() { Ranks = new() { Ranks = [.. Saved] } });

            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            _subscription = bus.Subscribe<RanksSettingsChanged>(_ => RanksChanged++);

            var accountsStore = new AccountsStore(null, Path.Combine(root, "accounts.json"), _gate);
            var obsChatStore = new ObsChatStore(bus, null, Path.Combine(root, "obs-chat.json"), _gate);
            var obsIntegrationStore = new ObsIntegrationStore(bus, null, Path.Combine(root, "obs-integration.json"), _gate);
            var updateStore = UpdateStore = new(null, Path.Combine(root, "update.json"), _gate);
            var debugChannelStore = new DebugChannelStore(null, Path.Combine(root, "debug-channel.json"));
            var pollsStore = new PollsStore(null, Path.Combine(root, "polls.json"), _gate);
            var layoutStore = new DashboardLayoutStore(null, Path.Combine(root, "dashboard-layout.json"), _gate);
            var uiSettings = new MemorySettings();
            var mcpOptions = new McpServerOptions
            {
                ServerName = "test",
                AppName = "test",
                AppVersion = "1.0",
                ShotsDirectory = Path.Combine(root, "shots"),
            };

            var mcpPreferences = new McpPreferences(uiSettings, mcpOptions);
            var kestrel = new KestrelHttpServer(null!, SettingsManager, [], NullLogger<KestrelHttpServer>.Instance, NullLoggerFactory.Instance);

            Page = new(
                new(),
                new(),
                new(kestrel, Dialogs, null!),
                new(),
                new(DispatchProxy.Create<ITwitchOAuthService, InertProxy>(), accountsStore, Dialogs, null!, null!, null!, NullLogger<OAuthAccountViewModel>.Instance),
                new(obsChatStore, SettingsManager, Dialogs, null!),
                new(null!, SettingsManager, NullLogger<ObsIntegrationSectionViewModel>.Instance, Dialogs, null!),
                new(),
                new(),
                new(pollsStore, TimeProvider.System),
                new(SettingsManager, null!, Dialogs, null!, null!, null!),
                new(UpdateCoordinator, updateStore, bus, Dialogs),
                new(DebugChannelOverride.None, debugChannelStore, null!, TimeProvider.System, NullLogger<DebugChannelSectionViewModel>.Instance),
                new(mcpPreferences, new(mcpPreferences, null!, null!, mcpOptions, new EmptyServiceProvider()), mcpOptions, Dialogs, null!),
                new(Array.Empty<DashboardTileViewModel>(), uiSettings, layoutStore),
                new(),
                null!,
                null!,
                SettingsManager,
                accountsStore,
                obsChatStore,
                obsIntegrationStore,
                updateStore,
                debugChannelStore,
                _gate,
                new FakeTargetChannelProvider(),
                new DashboardLayoutCoordinator(layoutStore),
                bus,
                kestrel,
                NullLogger<SettingsPageViewModel>.Instance,
                Dialogs,
                uiSettings);
        }

        public string SettingsPath { get; }

        public SettingsManager SettingsManager { get; }

        public FakeDialogService Dialogs { get; } = new();

        public FakeUpdateCoordinator UpdateCoordinator { get; } = new();

        public UpdateStore UpdateStore { get; }

        public SettingsPageViewModel Page { get; }

        public int RanksChanged { get; private set; }

        public Task CloseSettingsGateAsync()
        {
            return _gate.RunExternalWriteAsync(() => Task.FromResult(0), _ => new[] { "settings.json" });
        }

        public IReadOnlyList<UserRank> ReadRanksFromDisk()
        {
            return new SettingsManager(NullLogger<SettingsManager>.Instance, SettingsPath).Current.Ranks.Ranks;
        }

        public void Dispose()
        {
            Page.Dispose();
            _subscription.Dispose();
        }
    }

    private sealed class FakeDialogService : IDialogService
    {
        public List<string> Warnings { get; } = [];

        public List<string> Infos { get; } = [];

        public Task<bool> ShowAsync(IDialogViewModel viewModel) => Task.FromResult(false);

        public Task<bool> ReplaceAsync(IDialogViewModel viewModel) => Task.FromResult(false);

        public bool Confirm(string title, string message, bool defaultYes = false) => true;

        public bool ConfirmWarning(string title, string message, bool defaultYes = false) => true;

        public void Info(string title, string message) => Infos.Add(message);

        public void Warning(string title, string message) => Warnings.Add(message);

        public void Error(string title, string message) => throw new InvalidOperationException(message);
    }

    private sealed class FakeTargetChannelProvider : ITargetChannelProvider
    {
        public TargetChannelState Current { get; } = new("poproshayka", "poproshayka", false, true);

        public void BeginSession()
        {
        }

        public void EndSession()
        {
        }
    }

    private sealed class FakeUpdateCoordinator : IUpdateCoordinator
    {
        public UpdateKind Kind => UpdateKind.Unsupported;

        public bool IsUpdatable => false;

        public string DefaultRepositorySlug => "owner/repo";

        public Version CurrentVersion { get; } = new(1, 0);

        public UpdateCandidate? LatestCandidate => null;

        public bool HasPreparedUpdate => false;

        public string? PreparedVersion => null;

        public Task<UpdateCandidate?> CheckNowAsync(CancellationToken cancellationToken) => Task.FromResult<UpdateCandidate?>(null);

        public Task PrepareAsync(UpdateCandidate candidate, IProgress<int>? progress, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> TryPrepareSilentlyAsync(UpdateCandidate candidate, CancellationToken cancellationToken) => Task.FromResult(false);

        public bool FailDiscard { get; set; }

        public Task<bool> DiscardPreparedUpdateAsync(CancellationToken cancellationToken)
        {
            return FailDiscard
                ? Task.FromException<bool>(new IOException("Файл обновления занят"))
                : Task.FromResult(false);
        }

        public void SkipVersion(string version)
        {
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    public class InertProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
    }
}
