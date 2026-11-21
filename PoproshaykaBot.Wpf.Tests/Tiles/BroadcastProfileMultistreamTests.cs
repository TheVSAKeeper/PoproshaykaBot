using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Broadcasting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels.Controls;
using PoproshaykaBot.Wpf.ViewModels.Dialogs;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using PoproshaykaBot.Wpf.Views.Dialogs;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace PoproshaykaBot.Wpf.Tests.Tiles;

[TestFixture]
public sealed class BroadcastProfileMultistreamTests
{
    private static readonly string[] Dictionaries =
    [
        "pack://application:,,,/KeepShell;component/Resources/Themes/Light.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Themes/Tokens.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Converters.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Styles/Controls.xaml",
        "pack://application:,,,/PoproshaykaBot.Wpf;component/Infrastructure/Converters/AppConverters.xaml",
        "pack://application:,,,/PoproshaykaBot.Wpf;component/Resources/TableStyles.xaml",
        "pack://application:,,,/PoproshaykaBot.Wpf;component/Resources/RatingStyles.xaml",
        "pack://application:,,,/PoproshaykaBot.Wpf;component/Resources/PlatformIcons.xaml",
    ];

    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "wpf-multistream-profiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public void BroadcastProfileEditDialog_Toggles_PlatformSections_And_RoundTrips_YouTubeMetadata()
    {
        var registry = CreateRegistry();
        using var game = new GameAutocompleteViewModel(new StubGameResolver(), new ManualUiDispatcher());
        using var dialog = new BroadcastProfileEditDialogViewModel(
            game,
            null!,
            null!,
            NullLogger<BroadcastProfileEditDialogViewModel>.Instance,
            registry);

        dialog.LoadFrom(
            new BroadcastProfile { Name = "Эфир", Title = "Общий #{n}", CurrentNumber = 4 },
            new HashSet<StreamingPlatform> { StreamingPlatform.Twitch },
            new BroadcastProfileYouTubeMetadata
            {
                TitleOverride = "YT заголовок #{n}",
                Description = "Описание серии {n}",
                PrivacyStatus = YouTubeBroadcastPrivacyStatus.Unlisted,
            });

        Assert.Multiple(() =>
        {
            Assert.That(dialog.IsTwitchSelected, Is.True);
            Assert.That(dialog.IsYouTubeSelected, Is.False);
            Assert.That(dialog.YouTubeTitleOverride, Is.EqualTo("YT заголовок #{n}"));
            Assert.That(dialog.YouTubeDescription, Is.EqualTo("Описание серии {n}"));
            Assert.That(dialog.YouTubePrivacyStatus, Is.EqualTo(YouTubeBroadcastPrivacyStatus.Unlisted));
        });

        var raised = new List<string?>();
        dialog.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        var youtubeOption = dialog.TargetPlatforms.Single(o => o.Platform == StreamingPlatform.YouTube);
        var twitchOption = dialog.TargetPlatforms.Single(o => o.Platform == StreamingPlatform.Twitch);

        youtubeOption.IsSelected = true;
        twitchOption.IsSelected = false;

        Assert.Multiple(() =>
        {
            Assert.That(dialog.IsYouTubeSelected, Is.True);
            Assert.That(dialog.IsTwitchSelected, Is.False);
            Assert.That(raised, Does.Contain(nameof(BroadcastProfileEditDialogViewModel.IsYouTubeSelected)));
            Assert.That(raised, Does.Contain(nameof(BroadcastProfileEditDialogViewModel.IsTwitchSelected)));
        });

        dialog.YouTubeTitleOverride = "  Новый YT  ";
        dialog.YouTubeDescription = "  Новое описание  ";
        dialog.YouTubePrivacyStatus = YouTubeBroadcastPrivacyStatus.Private;

        var built = dialog.BuildYouTubeMetadata();
        Assert.Multiple(() =>
        {
            Assert.That(built.TitleOverride, Is.EqualTo("Новый YT"));
            Assert.That(built.Description, Is.EqualTo("Новое описание"));
            Assert.That(built.PrivacyStatus, Is.EqualTo(YouTubeBroadcastPrivacyStatus.Private));
            Assert.That(built.IsEmpty, Is.False);
        });
    }

    [Test]
    public void BroadcastProfileEditDialog_Reflects_YouTubeWriteAccess_For_ReadOnly_And_Combined_Auth()
    {
        var registry = CreateRegistry();
        var catalogStore = new ChannelCatalogStore(null, Path.Combine(_root, "channels.json"));
        var accountsStore = new YouTubeAccountsStore(null, Path.Combine(_root, "youtube-accounts.json"));

        accountsStore.Mutate(PlatformAuthRole.Broadcaster, account =>
        {
            account.AccessToken = "ro-token";
            account.StoredScopes = [YouTubeScopes.ReadOnly];
        });

        using var game = new GameAutocompleteViewModel(new StubGameResolver(), new ManualUiDispatcher());
        using var readOnlyDialog = new BroadcastProfileEditDialogViewModel(
            game,
            null!,
            null!,
            NullLogger<BroadcastProfileEditDialogViewModel>.Instance,
            registry,
            catalogStore,
            accountsStore);

        Assert.Multiple(() =>
        {
            Assert.That(readOnlyDialog.HasYouTubeWriteAccessWarning, Is.True);
            Assert.That(readOnlyDialog.YouTubeWriteAccessHint, Does.Contain("Нет прав записи"));
        });

        accountsStore.Mutate(PlatformAuthRole.Broadcaster, account =>
        {
            account.AccessToken = "rw-token";
            account.StoredScopes = [YouTubeScopes.ForceSsl, YouTubeScopes.ReadOnly];
        });

        readOnlyDialog.LoadFrom(
            new BroadcastProfile { Name = "Мульти" },
            new HashSet<StreamingPlatform> { StreamingPlatform.Twitch, StreamingPlatform.YouTube });

        Assert.Multiple(() =>
        {
            Assert.That(readOnlyDialog.HasYouTubeWriteAccessWarning, Is.False);
            Assert.That(readOnlyDialog.YouTubeWriteAccessHint, Does.Contain("Права записи YouTube активны"));
        });
    }

    [Test]
    public async Task BroadcastProfilesTile_PartialApply_Sets_WarningStatus_With_PlatformDetails()
    {
        var registry = CreateRegistry();
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var profilesStore = new BroadcastProfilesStore(null, Path.Combine(_root, "broadcast-profiles.json"));
        var platformsStore = new BroadcastProfilePlatformsStore(null, Path.Combine(_root, "broadcast-profile-platforms.json"));
        var youtubeStore = new BroadcastProfileYouTubeStore(null, Path.Combine(_root, "broadcast-profile-youtube.json"));
        var manager = new BroadcastProfilesManager(
            profilesStore,
            null!,
            bus,
            TimeProvider.System,
            NullLogger<BroadcastProfilesManager>.Instance,
            platformsStore,
            youtubeStore);

        var profile = new BroadcastProfile { Name = "Вечерний стрим", Title = "Прохождение" };
        manager.Upsert(profile, [StreamingPlatform.Twitch, StreamingPlatform.YouTube]);

        using var tile = new BroadcastProfilesTileViewModel(
            manager,
            profilesStore,
            new IdleStreamStatus(),
            null!,
            null!,
            null!,
            new StubDialogService(),
            null!,
            bus,
            registry);

        await bus.PublishAsync(new BroadcastProfileApplied(
            profile,
            [
                new(StreamingPlatform.Twitch, true),
                new(StreamingPlatform.YouTube, false, "Нет прав записи YouTube"),
            ]));

        Assert.Multiple(() =>
        {
            Assert.That(tile.HasStatusMessage, Is.True);
            Assert.That(tile.IsStatusError, Is.False);
            Assert.That(tile.IsStatusWarning, Is.True);
            Assert.That(tile.StatusMessage, Does.Contain("Вечерний стрим"));
            Assert.That(tile.StatusMessage, Does.Contain("Twitch обновлён"));
            Assert.That(tile.StatusMessage, Does.Contain("YouTube – Нет прав записи YouTube"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public void BroadcastProfileEditDialogView_Instantiates_And_Binds_Cleanly_With_YouTubeSection()
    {
        TestResources.Ensure(Dictionaries);

        var registry = CreateRegistry();
        using var game = new GameAutocompleteViewModel(new StubGameResolver(), new ManualUiDispatcher());
        using var vm = new BroadcastProfileEditDialogViewModel(
            game,
            null!,
            null!,
            NullLogger<BroadcastProfileEditDialogViewModel>.Instance,
            registry);

        vm.LoadFrom(
            new BroadcastProfile { Name = "Мульти", Title = "Серия #{n}" },
            new HashSet<StreamingPlatform> { StreamingPlatform.Twitch, StreamingPlatform.YouTube },
            new BroadcastProfileYouTubeMetadata
            {
                TitleOverride = "YT #{n}",
                Description = "Описание",
                PrivacyStatus = YouTubeBroadcastPrivacyStatus.Public,
            });

        var view = new BroadcastProfileEditDialogView { DataContext = vm };
        view.Measure(new(640, 720));
        view.Arrange(new(0, 0, 640, 720));
        view.UpdateLayout();

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsTwitchSelected, Is.True);
            Assert.That(vm.IsYouTubeSelected, Is.True);
            Assert.That(view.ActualWidth, Is.GreaterThan(0));
            Assert.That(view.ActualHeight, Is.GreaterThan(0));
        });
    }

    private static StubPlatformRegistry CreateRegistry()
    {
        return new(
            new StubChatPlatform(StreamingPlatform.Twitch, "Twitch"),
            new StubChatPlatform(StreamingPlatform.YouTube, "YouTube"));
    }

    private sealed class StubChatPlatform(StreamingPlatform id, string displayName) : IChatPlatform
    {
        public StreamingPlatform Id => id;
        public string DisplayName => displayName;
        public PlatformCapabilities Capabilities => PlatformCapabilities.None;
        public bool IsEnabled => true;
        public bool IsConnected => false;
        public IStreamStatus StreamStatus => new IdleStreamStatus();
        public IPlatformMessenger Messenger => null!;
    }

    private sealed class StubPlatformRegistry(params IChatPlatform[] platforms) : IChatPlatformRegistry
    {
        public IReadOnlyList<IChatPlatform> All => platforms;
        public IChatPlatform? Find(StreamingPlatform platform) => platforms.FirstOrDefault(p => p.Id == platform);
        public bool TryGet(StreamingPlatform platform, [NotNullWhen(true)] out IChatPlatform? chatPlatform)
        {
            chatPlatform = Find(platform);
            return chatPlatform is not null;
        }
        public IChatPlatform GetRequired(StreamingPlatform platform) => Find(platform) ?? throw new KeyNotFoundException();
        public IReadOnlyList<IChatPlatform> GetEnabled() => platforms.Where(p => p.IsEnabled).ToArray();
        public IReadOnlyList<IChatPlatform> GetConnected() => platforms.Where(p => p.IsConnected).ToArray();
        public string? GetConfiguredChannel(StreamingPlatform platform) => null;
        public string? BuildStreamUrl(StreamingPlatform platform, StreamInfo? stream = null) => null;
    }

    private sealed class IdleStreamStatus : IStreamStatus
    {
        public StreamStatus CurrentStatus => StreamStatus.Unknown;
        public StreamInfo? CurrentStream => null;
        public StreamStatusSnapshot GetSnapshot() => new(StreamStatus.Unknown, null, null);
        public Task RefreshLiveSnapshotAsync() => Task.CompletedTask;
    }

    private sealed class StubGameResolver : IGameCategoryResolver
    {
        public Task<IReadOnlyList<GameSuggestion>> SearchAsync(string query, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<GameSuggestion>>([]);

        public Task<GameCategoryResolution> ResolveAsync(string query, CancellationToken cancellationToken)
            => Task.FromResult(new GameCategoryResolution(null, []));

        public Task RememberAsync(GameSuggestion suggestion) => Task.CompletedTask;
    }

    private sealed class StubDialogService : IDialogService
    {
        public Task<bool> ShowAsync(IDialogViewModel viewModel) => Task.FromResult(false);
        public Task<bool> ReplaceAsync(IDialogViewModel viewModel) => Task.FromResult(false);
        public bool Confirm(string title, string message, bool defaultYes = false) => true;
        public bool ConfirmWarning(string title, string message, bool defaultYes = false) => true;
        public void Info(string title, string message) { }
        public void Warning(string title, string message) { }
        public void Error(string title, string message) { }
    }
}
