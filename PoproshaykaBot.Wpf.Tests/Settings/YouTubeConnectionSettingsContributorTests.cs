using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Services.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;
using System.IO;
using System.Reflection;

namespace PoproshaykaBot.Wpf.Tests.Settings;

[TestFixture]
public sealed class YouTubeConnectionSettingsContributorTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "poproshayka-yt-contributor-" + Guid.NewGuid().ToString("N"));
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
    public async Task Changing_client_id_publishes_authorization_refreshed_for_both_roles()
    {
        var (contributor, vm, _, _) = CreateFixture();
        LoadInitial(contributor);

        vm.ClientId = "new-yt-client";
        contributor.BeginSave(new AppSettings());
        var outcome = await contributor.SaveAsync(new AppSettings(), appSettingsWritten: true);

        var roles = outcome.EventsOfType<PoproshaykaBot.Core.Platforms.Auth.PlatformAuthorizationRefreshed>()
            .Where(e => e.Platform == StreamingPlatform.YouTube)
            .Select(e => e.Role)
            .ToArray();

        Assert.That(roles, Is.EquivalentTo([PlatformAuthRole.Bot, PlatformAuthRole.Broadcaster]));
    }

    [Test]
    public async Task Changing_client_secret_publishes_authorization_refreshed_for_both_roles()
    {
        var (contributor, vm, _, _) = CreateFixture();
        LoadInitial(contributor);

        vm.ClientSecret = "new-yt-secret";
        contributor.BeginSave(new AppSettings());
        var outcome = await contributor.SaveAsync(new AppSettings(), appSettingsWritten: true);

        var roles = outcome.EventsOfType<PoproshaykaBot.Core.Platforms.Auth.PlatformAuthorizationRefreshed>()
            .Where(e => e.Platform == StreamingPlatform.YouTube)
            .Select(e => e.Role)
            .ToArray();

        Assert.That(roles, Is.EquivalentTo([PlatformAuthRole.Bot, PlatformAuthRole.Broadcaster]));
    }

    [Test]
    public async Task Changing_single_role_token_publishes_only_that_role_event()
    {
        var (contributor, vm, _, _) = CreateFixture();
        LoadInitial(contributor);

        vm.Bot.AccessTokenValue = "updated-bot-token";
        contributor.BeginSave(new AppSettings());
        var outcome = await contributor.SaveAsync(new AppSettings(), appSettingsWritten: true);

        var events = outcome.EventsOfType<PoproshaykaBot.Core.Platforms.Auth.PlatformAuthorizationRefreshed>()
            .ToArray();

        Assert.That(events, Has.Length.EqualTo(1));
        Assert.That(events[0].Platform, Is.EqualTo(StreamingPlatform.YouTube));
        Assert.That(events[0].Role, Is.EqualTo(PlatformAuthRole.Bot));
    }

    [Test]
    public async Task Saving_without_credential_or_token_changes_publishes_no_events()
    {
        var (contributor, vm, _, _) = CreateFixture();
        LoadInitial(contributor);

        vm.ProjectId = "updated-project";
        contributor.BeginSave(new AppSettings());
        var outcome = await contributor.SaveAsync(new AppSettings(), appSettingsWritten: true);

        Assert.That(outcome.Events, Is.Empty);
    }

    [Test]
    public async Task Changing_client_id_without_tokens_publishes_no_events()
    {
        var (contributor, vm, settingsStore, accountsStore) = CreateFixture();
        accountsStore.SaveAll("initial-secret", new YouTubeAccountSettings(), new YouTubeAccountSettings());
        LoadInitial(contributor);

        vm.ClientId = "new-yt-client";
        contributor.BeginSave(new AppSettings());
        var outcome = await contributor.SaveAsync(new AppSettings(), appSettingsWritten: true);

        Assert.That(outcome.Events, Is.Empty);
    }

    [Test]
    public async Task Changing_refresh_token_alone_publishes_authorization_refreshed()
    {
        var (contributor, vm, _, accountsStore) = CreateFixture();
        accountsStore.SaveAll("initial-secret", new YouTubeAccountSettings(), new YouTubeAccountSettings());
        LoadInitial(contributor);

        vm.Broadcaster.RefreshTokenValue = "new-refresh-token";
        contributor.BeginSave(new AppSettings());
        var outcome = await contributor.SaveAsync(new AppSettings(), appSettingsWritten: true);

        var events = outcome.EventsOfType<PlatformAuthorizationRefreshed>().ToArray();

        Assert.That(events, Has.Length.EqualTo(1));
        Assert.That(events[0].Platform, Is.EqualTo(StreamingPlatform.YouTube));
        Assert.That(events[0].Role, Is.EqualTo(PlatformAuthRole.Broadcaster));
    }

    [Test]
    public void Invalid_quota_budget_fails_validation_before_save()
    {
        var (contributor, vm, _, _) = CreateFixture();
        LoadInitial(contributor);

        vm.DailyQuotaBudgetText = "0";

        Assert.That(contributor.ValidateBeforeSave(), Is.Not.Null);
    }

    private (
        YouTubeConnectionSettingsContributor Contributor,
        YouTubeSettingsSectionViewModel ViewModel,
        YouTubeSettingsStore SettingsStore,
        YouTubeAccountsStore AccountsStore) CreateFixture()
    {
        var settingsStore = new YouTubeSettingsStore(null, Path.Combine(_root, "youtube.json"));
        settingsStore.Save(new YouTubeSettings { ClientId = "initial-client", ProjectId = "initial-project" });

        var accountsStore = new YouTubeAccountsStore(null, Path.Combine(_root, "youtube-accounts.json"));
        accountsStore.SaveAll(
            "initial-secret",
            new YouTubeAccountSettings { AccessToken = "initial-bot-at", RefreshToken = "bot-rt" },
            new YouTubeAccountSettings { AccessToken = "initial-caster-at", RefreshToken = "caster-rt" });

        var vm = new YouTubeSettingsSectionViewModel(
            DispatchProxy.Create<IYouTubeOAuthService, InertProxy>(),
            DispatchProxy.Create<IShellLauncher, InertProxy>(),
            DispatchProxy.Create<IDialogService, InertProxy>(),
            new ManualUiDispatcher(),
            TimeProvider.System,
            NullLoggerFactory.Instance);

        var contributor = new YouTubeConnectionSettingsContributor(vm, settingsStore, accountsStore);
        return (contributor, vm, settingsStore, accountsStore);
    }

    private static void LoadInitial(ISettingsSectionContributor contributor)
    {
        contributor.Load(new SettingsSessionState { Settings = new AppSettings() });
    }

    private class InertProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
    }
}
