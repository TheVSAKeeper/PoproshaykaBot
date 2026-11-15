using KeepShell.Mcp;
using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Services.Platform;
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
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Core.Update;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;
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
        var before = await File.ReadAllTextAsync(harness.SettingsPath);

        harness.Ranks.Entries[0].Emoji = "";
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

        harness.Ranks.Entries[0].Name = "ИМПЕРАТОР";
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

    [TestCase(false)]
    [TestCase(true)]
    public async Task YouTube_настройки_сохраняются_и_уважают_гейт_записи(bool gateClosed)
    {
        using var harness = new Harness(_root);

        if (gateClosed)
        {
            await harness.CloseYouTubeGateAsync();
        }

        harness.YouTube.Enabled = true;
        harness.YouTube.ProjectId = "yt-project-1";
        harness.YouTube.ClientId = "yt-client-1";
        harness.YouTube.ClientSecret = "yt-secret-1";
        harness.YouTube.Channel = "@ytchannel";
        harness.YouTube.DailyQuotaBudgetText = "12000";
        harness.YouTube.Bot.AccessTokenValue = "bot-at-1";

        await harness.Page.SaveCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(harness.YouTubeSettingsStore.Load().Enabled, Is.True);
            Assert.That(harness.YouTubeSettingsStore.Load().ProjectId, Is.EqualTo("yt-project-1"));
            Assert.That(harness.YouTubeAccountsStore.LoadClientSecret(), Is.EqualTo("yt-secret-1"));
            Assert.That(File.Exists(harness.YouTubeSettingsPath), Is.EqualTo(!gateClosed));
            Assert.That(harness.Dialogs.Infos, Has.Count.EqualTo(1));
            Assert.That(harness.Dialogs.Infos[0], gateClosed ? Does.Contain("не записаны") : Does.Contain("успешно сохранены"));
        });
    }

    [Test]
    public async Task YouTube_некорректный_бюджет_квоты_останавливает_сохранение()
    {
        using var harness = new Harness(_root);
        harness.YouTube.DailyQuotaBudgetText = "abc";

        await harness.Page.SaveCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(harness.YouTubeSettingsPath), Is.False);
            Assert.That(harness.Dialogs.Warnings, Has.Count.EqualTo(1));
            Assert.That(harness.Dialogs.Infos, Is.Empty);
        });
    }

    [Test]
    public async Task Сбой_после_записи_настроек_не_оставляет_лестницу_на_прежних_рангах()
    {
        using var harness = new Harness(_root);
        harness.UpdateStore.Save(new() { ApplyMode = UpdateApplyMode.SilentOnExit });
        harness.UpdateCoordinator.FailDiscard = true;

        harness.Ranks.Entries[0].Name = "ИМПЕРАТОР";

        Assert.ThrowsAsync<InvalidOperationException>(() => harness.Page.SaveCommand.ExecuteAsync(null),
            "Сбой шага обновлений доходит до окна ошибки");

        Assert.Multiple(() =>
        {
            Assert.That(harness.ReadRanksFromDisk()[0].Name, Is.EqualTo("ИМПЕРАТОР"));
            Assert.That(harness.RanksChanged, Is.EqualTo(1), "Записанные ранги объявлены до шагов, которые могут упасть");
        });
    }

    [Test]
    public async Task Закрытый_гейт_другой_секции_не_оставляет_ранги_несохранёнными()
    {
        using var harness = new Harness(_root);
        await harness.CloseYouTubeGateAsync();

        harness.Ranks.Entries[0].Name = "ИМПЕРАТОР";
        await harness.Page.SaveCommand.ExecuteAsync(null);
        await harness.Page.SaveCommand.ExecuteAsync(null);

        Assert.That(harness.RanksChanged, Is.EqualTo(1));
    }

    [Test]
    public async Task Смена_отладочного_канала_публикует_ChatDisplaySettingsChanged_с_новым_каналом()
    {
        using var harness = new Harness(_root);

        harness.DebugChannel.IsEnabled = true;
        harness.DebugChannel.Channel = "debug_target";
        await harness.Page.SaveCommand.ExecuteAsync(null);

        Assert.That(harness.ChatDisplayChannels, Is.EqualTo(new[] { "debug_target" }));
    }

    private sealed class Harness : IDisposable
    {
        private readonly SettingsWriteGate _gate = new();
        private readonly IDisposable _subscription;
        private readonly IDisposable _chatDisplaySubscription;

        public Harness(string root)
        {
            SettingsPath = Path.Combine(root, "settings.json");
            SettingsManager = new(NullLogger<SettingsManager>.Instance, SettingsPath, _gate);
            SettingsManager.SaveSettings(new() { Ranks = new() { Ranks = [.. Saved] } });

            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            _subscription = bus.Subscribe<RanksSettingsChanged>(_ => RanksChanged++);
            _chatDisplaySubscription = bus.Subscribe<ChatDisplaySettingsChanged>(e => ChatDisplayChannels.Add(e.Channel));

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
            YouTubeSettingsPath = Path.Combine(root, "youtube.json");
            YouTubeSettingsStore = new YouTubeSettingsStore(null, YouTubeSettingsPath, _gate);
            YouTubeAccountsStore = new YouTubeAccountsStore(null, Path.Combine(root, "youtube-accounts.json"), _gate);
            var targetChannelProvider = new StoreTargetChannelProvider(debugChannelStore);

            var oauth = new OAuthSettingsViewModel(
                DispatchProxy.Create<ITwitchOAuthService, InertProxy>(),
                accountsStore,
                Dialogs,
                DispatchProxy.Create<IShellLauncher, InertProxy>(),
                DispatchProxy.Create<IEmbeddedTwitchAuthDialog, InertProxy>(),
                new ManualUiDispatcher(),
                TimeProvider.System,
                NullLoggerFactory.Instance);

            var youTube = new YouTubeSettingsSectionViewModel(
                DispatchProxy.Create<IYouTubeOAuthService, InertProxy>(),
                DispatchProxy.Create<IShellLauncher, InertProxy>(),
                Dialogs,
                new ManualUiDispatcher(),
                TimeProvider.System,
                NullLoggerFactory.Instance);

            var obsChat = new ObsChatSettingsSectionViewModel(obsChatStore, SettingsManager, Dialogs, null!);
            var obsIntegration = new ObsIntegrationSectionViewModel(null!, SettingsManager, NullLogger<ObsIntegrationSectionViewModel>.Instance, Dialogs, null!);
            var polls = new PollsSettingsSectionViewModel(pollsStore, TimeProvider.System);
            var update = new UpdateSettingsSectionViewModel(UpdateCoordinator, updateStore, bus, Dialogs, new ManualUiDispatcher(), DispatchProxy.Create<IMainWindowController, InertProxy>());
            var debugChannel = new DebugChannelSectionViewModel(DebugChannelOverride.None, debugChannelStore, null!, TimeProvider.System, NullLogger<DebugChannelSectionViewModel>.Instance);
            var mcp = new McpSettingsSectionViewModel(mcpPreferences, new(mcpPreferences, null!, null!, mcpOptions, new EmptyServiceProvider()), mcpOptions, Dialogs, null!);
            var dashboardLayout = new DashboardLayoutSectionViewModel(Array.Empty<DashboardTileViewModel>(), uiSettings, layoutStore);
            var ranks = new RanksSettingsSectionViewModel();

            ISettingsSectionContributor[] contributors =
            [
                new BasicSettingsSectionContributor(new(), new(), new(), SettingsManager, targetChannelProvider),
                new TwitchConnectionSettingsContributor(oauth, accountsStore, SettingsManager, NullLogger<TwitchConnectionSettingsContributor>.Instance),
                new YouTubeConnectionSettingsContributor(youTube, YouTubeSettingsStore, YouTubeAccountsStore),
                new ObsSettingsSectionContributor(obsIntegration, obsChat, new(kestrel, Dialogs, null!), obsIntegrationStore, obsChatStore, Dialogs),
                new StreamSettingsSectionContributor(new(), new(), polls),
                new RanksSettingsSectionContributor(ranks),
                new DashboardSettingsSectionContributor(dashboardLayout, new DashboardLayoutCoordinator(layoutStore)),
                new UpdateSettingsSectionContributor(update, updateStore),
                new DebugChannelSettingsSectionContributor(debugChannel, debugChannelStore),
                new McpSettingsSectionContributor(mcp),
                new AppearanceSettingsSectionContributor(new AppearanceSettingsSectionViewModel(new(uiSettings), new(uiSettings))),
                new MiscSettingsSectionContributor(new(SettingsManager, null!, Dialogs, null!, null!, null!)),
            ];

            Page = new(
                contributors,
                SettingsManager,
                _gate,
                kestrel,
                bus,
                NullLogger<SettingsPageViewModel>.Instance,
                Dialogs,
                uiSettings);
        }

        public RanksSettingsSectionViewModel Ranks => Page.GetSectionContent<RanksSettingsSectionViewModel>();

        public YouTubeSettingsSectionViewModel YouTube => Page.GetSectionContent<YouTubeSettingsSectionViewModel>();

        public DebugChannelSectionViewModel DebugChannel => Page.GetSectionContent<DebugChannelSectionViewModel>();

        public string SettingsPath { get; }

        public SettingsManager SettingsManager { get; }

        public FakeDialogService Dialogs { get; } = new();

        public FakeUpdateCoordinator UpdateCoordinator { get; } = new();

        public UpdateStore UpdateStore { get; }

        public string YouTubeSettingsPath { get; }

        public YouTubeSettingsStore YouTubeSettingsStore { get; }

        public YouTubeAccountsStore YouTubeAccountsStore { get; }

        public SettingsPageViewModel Page { get; }

        public int RanksChanged { get; private set; }

        public List<string> ChatDisplayChannels { get; } = [];

        public Task CloseSettingsGateAsync()
        {
            return _gate.RunExternalWriteAsync(() => Task.FromResult(0), _ => new[] { "settings.json" });
        }

        public Task CloseYouTubeGateAsync()
        {
            return _gate.RunExternalWriteAsync(() => Task.FromResult(0), _ => new[] { "youtube.json", "youtube-accounts.json" });
        }

        public IReadOnlyList<UserRank> ReadRanksFromDisk()
        {
            return new SettingsManager(NullLogger<SettingsManager>.Instance, SettingsPath).Current.Ranks.Ranks;
        }

        public void Dispose()
        {
            Page.Dispose();
            _subscription.Dispose();
            _chatDisplaySubscription.Dispose();
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

    private sealed class StoreTargetChannelProvider(DebugChannelStore debugChannelStore) : ITargetChannelProvider
    {
        public TargetChannelState Current =>
            debugChannelStore.Load() is { IsEnabled: true, Channel: { Length: > 0 } channel }
                ? new(channel, "poproshayka", true, true)
                : new("poproshayka", "poproshayka", false, true);

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
