using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using KeepShell.Services.Platform;
using KeepShell.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Streaming.Switching;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.YouTube.Client;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;
using PoproshaykaBot.Wpf.ViewModels.Tiles;

namespace PoproshaykaBot.Wpf.Tests.Channels;

[TestFixture]
public sealed class Stage3MultiChannelWpfTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"poproshayka-wpf-mc-{Guid.NewGuid():N}");
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
    public async Task Signing_in_from_a_channel_role_saves_the_grant_and_verifies_that_assignment()
    {
        var accounts = new AccountsStore(filePath: Path.Combine(_root, "accounts.json"));
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_root, "channels.json"));
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_root, "settings.json"));
        settings.Mutate(s =>
        {
            s.Twitch.ClientId = "test-client";
            s.Twitch.ClientSecret = "test-secret";
        });
        var channel = new ChannelKey(StreamingPlatform.Twitch, "10");
        catalog.Save(new() { Channels = [new() { Platform = channel.Platform, ChannelId = channel.ChannelId, IsConfigured = true }] });
        var auth = new TestEmbeddedAuth();
        var adapter = new NullResolveAdapter(StreamingPlatform.Twitch, (key, role, context) =>
        {
            Assert.That((key, role, context), Is.EqualTo((channel, PlatformAuthRole.Bot, "tw-bot-90")));
            return new(true, "90");
        });

        var editor = new ChannelCatalogSettingsSectionViewModel(catalog, [adapter], twitchAccountsStore: accounts,
            twitchOAuth: DispatchProxy.Create<ITwitchOAuthService, InertProxy>(), settingsManager: settings,
            shellLauncher: new NoOpLauncher(), embeddedTwitchAuth: auth);
        editor.Load();
        var bot = editor.VisibleChannels.Single().Roles.Single(role => role.Role == PlatformAuthRole.Bot);
        await bot.AuthorizeCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(bot.AccountId, Is.EqualTo("90"));
            Assert.That(bot.Status, Is.EqualTo("Доступ подтверждён"));
            Assert.That(editor.ValidateAssignments(), Is.Null);
            Assert.That(accounts.ListAccounts().Single().Key.AccountId, Is.EqualTo("90"));
            Assert.That(editor.VisibleChannels.Single().ToEntry().GetAccountAssignment(PlatformAuthRole.Broadcaster), Is.Null);
        });
    }

    [Test]
    public async Task Empty_catalog_authorizes_a_default_bot_for_channel_lookup_without_adding_the_bot_as_a_channel()
    {
        var accounts = new AccountsStore(filePath: Path.Combine(_root, "accounts.json"));
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_root, "channels.json"));
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_root, "settings.json"));
        settings.Mutate(current =>
        {
            current.Twitch.ClientId = "test-client";
            current.Twitch.ClientSecret = "test-secret";
        });
        var adapter = new NullResolveAdapter(StreamingPlatform.Twitch, resolve: (raw, context, _) =>
        {
            Assert.That(context, Is.EqualTo(AuthContextIds.Default));
            return Task.FromResult<ResolvedChannelIdentity?>(new(new(StreamingPlatform.Twitch, "10"), raw, raw, "https://twitch.tv/streamer"));
        });
        var vm = new ChannelCatalogSettingsSectionViewModel(catalog, [adapter], twitchAccountsStore: accounts,
            twitchOAuth: DispatchProxy.Create<ITwitchOAuthService, InertProxy>(), settingsManager: settings,
            shellLauncher: new NoOpLauncher(), embeddedTwitchAuth: new TestEmbeddedAuth());
        vm.Load();

        Assert.That(vm.AuthorizeDefaultBotCommand.CanExecute(null), Is.True);
        await vm.AuthorizeDefaultBotCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.HasChannels, Is.False);
            Assert.That(vm.StatusNoticeSeverity, Is.EqualTo(StatusSeverity.Success));
            Assert.That(accounts.Load(PlatformAuthRole.Bot).UserId, Is.EqualTo("90"));
        });

        vm.NewChannelInput = "streamer";
        await vm.AddChannelCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.SelectedChannel?.ChannelId, Is.EqualTo("10"));
            Assert.That(vm.SelectedChannel?.ToEntry().GetAccountAssignment(PlatformAuthRole.Bot), Is.Null);
            Assert.That(vm.AuthorizeDefaultBotCommand.CanExecute(null), Is.False);
        });
    }

    [Test]
    public async Task An_unresolved_channel_is_not_added_and_a_default_account_is_not_implicitly_assigned()
    {
        var accounts = new AccountsStore(filePath: Path.Combine(_root, "accounts.json"));
        accounts.Mutate(PlatformAuthRole.Bot, account =>
        {
            account.UserId = "90";
            account.Login = "thebot";
            account.AccessToken = "test-token";
        });
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_root, "channels.json"));
        catalog.Save(new() { Channels = [new() { Platform = StreamingPlatform.Twitch, ChannelId = "10", IsConfigured = true }] });
        var adapter = new NullResolveAdapter(StreamingPlatform.Twitch, (_, _, _) => new(true, "90"));
        var editor = new ChannelCatalogSettingsSectionViewModel(catalog, [adapter], twitchAccountsStore: accounts);
        editor.Load();
        editor.NewChannelInput = "https://twitch.tv/unresolved";

        await editor.AddChannelCommand.ExecuteAsync(null);
        var role = editor.VisibleChannels.Single().Roles.Single(value => value.Role == PlatformAuthRole.Bot);
        Assert.Multiple(() =>
        {
            Assert.That(role.SelectedAccount, Is.Null);
            Assert.That(role.VerifyCommand.CanExecute(null), Is.False);
            Assert.That(editor.StatusNoticeSeverity, Is.EqualTo(StatusSeverity.Error));
        });
        role.SetVerification(new(true, "90"));
        Assert.That(role.AccountId, Is.Null);
        role.SelectedAccount = role.Accounts.Single();
        Assert.That(role.VerifyCommand.CanExecute(null), Is.True);
        Assert.That(editor.ValidateAssignments(), Is.Not.Null);
    }

    [Test]
    public async Task One_account_can_be_verified_for_two_channels_and_survives_reloading_the_profile()
    {
        var accountsPath = Path.Combine(_root, "accounts.json");
        var channelsPath = Path.Combine(_root, "channels.json");
        var accounts = new AccountsStore(filePath: accountsPath);
        accounts.UpsertContext(PlatformAuthRole.Bot, "shared-bot", "Бот", new() { UserId = "90", Login = "shared", AccessToken = "test-bot" });
        accounts.UpsertContext(PlatformAuthRole.Broadcaster, "same-user", "Стример", new() { UserId = "90", Login = "shared", AccessToken = "test-owner" });

        var catalog = new ChannelCatalogStore(filePath: channelsPath);
        catalog.Save(new()
        {
            Channels =
            [
                new() { Platform = StreamingPlatform.Twitch, ChannelId = "10", DisplayName = "Канал A", IsConfigured = true, BotContextId = "shared-bot", BotVerifiedSubjectId = "90" },
                new() { Platform = StreamingPlatform.Twitch, ChannelId = "20", DisplayName = "Канал B", IsConfigured = true },
            ],
        });
        var secondKey = new ChannelKey(StreamingPlatform.Twitch, "20");
        var subject = new Queue<string>(["other-id", "90"]);
        var adapter = new NullResolveAdapter(StreamingPlatform.Twitch, (key, role, context) =>
        {
            Assert.That((key, role, context), Is.EqualTo((secondKey, PlatformAuthRole.Bot, "shared-bot")));
            return new(true, subject.Dequeue());
        });

        var editor = new ChannelCatalogSettingsSectionViewModel(catalog, [adapter], twitchAccountsStore: accounts);
        editor.Load();
        var platformAccount = accounts.ListAccounts().Single();
        var firstRole = editor.VisibleChannels.Single(c => c.ChannelId == "10").Roles.Single(role => role.Role == PlatformAuthRole.Bot);
        var secondRole = editor.VisibleChannels.Single(c => c.ChannelId == "20").Roles.Single(role => role.Role == PlatformAuthRole.Bot);
        secondRole.SelectedAccount = secondRole.Accounts.Single();
        await secondRole.VerifyCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(platformAccount.Key, Is.EqualTo(new PlatformAccountKey(StreamingPlatform.Twitch, "90")));
            Assert.That(platformAccount.Authorizations, Has.Count.EqualTo(2));
            Assert.That(firstRole.SelectedAccount?.AccountId, Is.EqualTo("90"));
            Assert.That(firstRole.Status, Is.EqualTo("Доступ подтверждён"));
            Assert.That(secondRole.Status, Does.Contain("не подтвердила"));
            Assert.That(editor.ValidateAssignments(), Does.Contain("Канал B"));
        });

        await secondRole.VerifyCommand.ExecuteAsync(null);
        Assert.That(editor.ValidateAssignments(), Is.Null);
        var settings = catalog.Load();
        editor.ApplyTo(settings);
        Assert.That(catalog.Save(settings), Is.True);

        var reopened = new ChannelCatalogStore(filePath: channelsPath);
        var bindings = reopened.Load().Channels.Select(channel => channel.GetAccountAssignment(PlatformAuthRole.Bot)).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(bindings.Select(binding => binding?.Account), Is.EqualTo(new[] { platformAccount.Key, platformAccount.Key }));
            Assert.That(bindings.Select(binding => binding?.IsVerified), Is.All.True);
            Assert.That(new AccountsStore(filePath: accountsPath).Load(PlatformAuthRole.Bot, "shared-bot").AccessToken, Is.EqualTo("test-bot"));
        });
    }

    [Test]
    public async Task ChannelsContributor_saves_switch_mode_preselected_next_preserves_live_active_and_respects_SettingsWriteGate()
    {
        var gate = new SettingsWriteGate();
        var catalog = new ChannelCatalogStore(null, Path.Combine(_root, "channels.json"), gate);
        catalog.Mutate(s =>
        {
            s.Channels =
            [
                new() { Platform = StreamingPlatform.YouTube, ChannelId = "UC_A", DisplayName = "Канал A", IsConfigured = true, MonitoringEnabled = true },
                new() { Platform = StreamingPlatform.YouTube, ChannelId = "UC_B", DisplayName = "Канал B", IsConfigured = true, MonitoringEnabled = true },
                new() { Platform = StreamingPlatform.YouTube, ChannelId = "UC_C", DisplayName = "Канал C", IsConfigured = true, MonitoringEnabled = true },
            ];
            s.Platforms =
            [
                new() { Platform = StreamingPlatform.YouTube, ActiveChannelId = "UC_A", SwitchMode = ChannelSwitchMode.Manual },
            ];
        });

        var stats = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var catalogVm = new ChannelCatalogSettingsSectionViewModel(catalog, [new NullResolveAdapter(StreamingPlatform.YouTube)]);
        var groupsVm = new ChannelGroupsSettingsSectionViewModel(catalog, stats);
        var contributor = new ChannelsSettingsSectionContributor(catalogVm, groupsVm, catalog);

        contributor.Load(new SettingsSessionState { Settings = new() });
        using var dirtySub = contributor.SubscribeDirty(new SettingsSessionState { Settings = new() }, static () => { }, static () => { });
        catalogVm.SelectedPlatform = StreamingPlatform.YouTube;
        catalogVm.SelectedSwitchModeOption = catalogVm.SwitchModeOptions.First(o => o.Mode == ChannelSwitchMode.PreselectedNext);
        catalogVm.SelectedPreselectedNextOption = catalogVm.PreselectedNextOptions.First(o => o.ChannelId == "UC_B");

        catalog.Mutate(s =>
        {
            s.Platforms[0].ActiveChannelId = "UC_C";
        });

        var saved = await contributor.SaveAsync(new(), appSettingsWritten: true);
        Assert.Multiple(() =>
        {
            Assert.That(saved.Written, Is.True);
            var routing = catalog.GetPlatformSettings(StreamingPlatform.YouTube);
            Assert.That(routing.SwitchMode, Is.EqualTo(ChannelSwitchMode.PreselectedNext));
            Assert.That(routing.PreselectedNextChannelId, Is.EqualTo("UC_B"));
            Assert.That(routing.ActiveChannelId, Is.EqualTo("UC_C"));
        });

        await gate.RunExternalWriteAsync(() => Task.FromResult(0), static _ => ["channels.json"]);
        catalogVm.SelectedSwitchModeOption = catalogVm.SwitchModeOptions.First(o => o.Mode == ChannelSwitchMode.Manual);
        var blocked = await contributor.SaveAsync(new(), appSettingsWritten: true);
        Assert.That(blocked.Written, Is.False);
    }

    [Test]
    public async Task Refused_active_switch_preserves_saved_channel_and_removal_can_be_retried()
    {
        const StreamingPlatform platform = StreamingPlatform.VkLive;
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_root, "refused.json"));
        catalog.Mutate(settings =>
        {
            settings.Channels =
            [
                new() { Platform = platform, ChannelId = "a", IsConfigured = true },
                new() { Platform = platform, ChannelId = "b", IsConfigured = true },
                new() { Platform = platform, ChannelId = "c", IsConfigured = true },
            ];
            settings.Platforms = [new() { Platform = platform, ActiveChannelId = "a" }];
        });
        var coordinator = new StubSwitchCoordinator(catalog);
        var vm = new ChannelCatalogSettingsSectionViewModel(catalog) { SelectedPlatform = platform };
        var contributor = new ChannelsSettingsSectionContributor(
            vm, new(catalog, new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance)), catalog, coordinator);
        var session = new SettingsSessionState { Settings = new() };
        contributor.Load(session);
        using var dirty = contributor.SubscribeDirty(session, static () => { }, static () => { });

        var refuse = true;
        coordinator.Switch = target =>
        {
            Assert.That(catalog.GetActiveChannel(platform)?.ChannelId, Is.EqualTo("a"));
            Assert.That(catalog.FindEntry(new(platform, "a"))?.IsConfigured, Is.True);
            if (refuse)
            {
                return new(ChannelSwitchOutcome.BlockedByDebugSession, new ChannelKey(platform, "a"), "Отказ переключения");
            }

            return new(ChannelSwitchOutcome.Switched, target);
        };

        vm.VisibleChannels.First().RemoveCommand.Execute(null);
        var result = await contributor.SaveAsync(new(), appSettingsWritten: true);
        Assert.Multiple(() =>
        {
            Assert.That(result.Written, Is.False);
            Assert.That(result.FailureMessage, Is.EqualTo("Отказ переключения"));
            Assert.That(catalog.GetPlatformSettings(platform).ActiveChannelId, Is.EqualTo("a"));
            Assert.That(vm.SelectedChannel?.ChannelId, Is.EqualTo("b"));
        });

        refuse = false;
        Assert.That((await contributor.SaveAsync(new(), appSettingsWritten: true)).Written, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(catalog.GetPlatformSettings(platform).ActiveChannelId, Is.EqualTo("b"));
            Assert.That(catalog.FindEntry(new(platform, "a")), Is.Null);
            Assert.That(coordinator.Switched, Has.Count.EqualTo(2));
        });

        catalog.Mutate(settings =>
        {
            settings.Channels.Add(new() { Platform = platform, ChannelId = "a", IsConfigured = true });
            settings.Platforms.Single().ActiveChannelId = "c";
        });
        vm.SelectedChannel!.DisplayName = "Правка после сохранения";
        Assert.That((await contributor.SaveAsync(new(), appSettingsWritten: true)).Written, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(catalog.GetPlatformSettings(platform).ActiveChannelId, Is.EqualTo("c"));
            Assert.That(catalog.FindEntry(new(platform, "a"))?.IsConfigured, Is.True);
            Assert.That(vm.SelectedChannel.ChannelId, Is.EqualTo("b"));
            Assert.That(coordinator.Switched, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task Saving_again_preserves_a_new_external_switch_policy_and_next_channel()
    {
        const StreamingPlatform platform = StreamingPlatform.VkLive;
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_root, "repeat.json"));
        catalog.Mutate(settings =>
        {
            settings.Channels =
            [
                new() { Platform = platform, ChannelId = "a", IsConfigured = true },
                new() { Platform = platform, ChannelId = "b", IsConfigured = true },
            ];
            settings.Platforms = [new() { Platform = platform, ActiveChannelId = "a" }];
        });
        var vm = new ChannelCatalogSettingsSectionViewModel(catalog, [new NullResolveAdapter(platform)]) { SelectedPlatform = platform };
        var contributor = new ChannelsSettingsSectionContributor(
            vm, new(catalog, new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance)), catalog);
        var session = new SettingsSessionState { Settings = new() };
        contributor.Load(session);
        using var dirty = contributor.SubscribeDirty(session, static () => { }, static () => { });

        vm.SelectedSwitchModeOption = vm.SwitchModeOptions.Single(option => option.Mode == ChannelSwitchMode.PreselectedNext);
        vm.SelectedPreselectedNextOption = vm.PreselectedNextOptions.Single(option => option.ChannelId == "b");
        Assert.That((await contributor.SaveAsync(new(), appSettingsWritten: true)).Written, Is.True);

        catalog.Mutate(settings =>
        {
            settings.Platforms.Single().SwitchMode = ChannelSwitchMode.AutoDiscover;
            settings.Platforms.Single().PreselectedNextChannelId = null;
            settings.Channels.Single(channel => channel.ChannelId == "a").Handle = "external";
        });
        vm.SelectedChannel!.DisplayName = "Локальное имя";
        Assert.That((await contributor.SaveAsync(new(), appSettingsWritten: true)).Written, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(catalog.GetPlatformSettings(platform).SwitchMode, Is.EqualTo(ChannelSwitchMode.AutoDiscover));
            Assert.That(catalog.GetPlatformSettings(platform).PreselectedNextChannelId, Is.Null);
            Assert.That(catalog.FindEntry(new(platform, "a"))?.Handle, Is.EqualTo("external"));
            Assert.That(vm.SelectedChannel.ChannelId, Is.EqualTo("a"));
        });
    }

    [Test]
    public void Supported_empty_account_list_shows_warning_and_existing_roles_do_not_block_unrelated_edits()
    {
        const StreamingPlatform platform = StreamingPlatform.VkLive;
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_root, "empty-accounts.json"));
        catalog.Mutate(settings => settings.Channels =
        [
            new() { Platform = StreamingPlatform.Twitch, ChannelId = "local", IsConfigured = true },
            new() { Platform = platform, ChannelId = "external", IsConfigured = true, BotAccountId = "missing", BotVerifiedSubjectId = "missing" },
            new() { Platform = platform, ChannelId = "empty", IsConfigured = true },
        ]);
        var vm = new ChannelCatalogSettingsSectionViewModel(catalog, [new NullResolveAdapter(platform, Accounts: [])],
            platformRegistry: new StubPlatformRegistry(new StubPlatform(platform, "Площадка из реестра", "external")));
        vm.Load();
        vm.SelectedChannel!.DisplayName = "Локальное имя";
        Assert.That(vm.ValidateAssignments(), Is.Null);

        vm.SelectedPlatform = platform;
        var role = vm.VisibleChannels.Single(channel => channel.ChannelId == "empty").Roles.Single(role => role.Role == PlatformAuthRole.Bot);
        Assert.Multiple(() =>
        {
            Assert.That(role.CanManageAccount, Is.True);
            Assert.That(role.StatusSeverity, Is.EqualTo(StatusSeverity.Warning));
            Assert.That(vm.ValidateAssignments(), Is.Null);
        });
        role.Assign("candidate", "vk-bot");
        Assert.That(vm.ValidateAssignments(), Does.Contain("Площадка из реестра"));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Next_channel_selector_keeps_a_valid_selection_after_draft_removal()
    {
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_root, "next-selector.json"));
        catalog.Mutate(settings =>
        {
            settings.Channels =
            [
                new() { Platform = StreamingPlatform.Twitch, ChannelId = "a", IsConfigured = true },
                new() { Platform = StreamingPlatform.Twitch, ChannelId = "b", IsConfigured = true },
                new() { Platform = StreamingPlatform.Twitch, ChannelId = "c", IsConfigured = true },
            ];
            settings.Platforms = [new() { Platform = StreamingPlatform.Twitch, ActiveChannelId = "a", PreselectedNextChannelId = "b" }];
        });
        var vm = new ChannelCatalogSettingsSectionViewModel(catalog);
        vm.Load();
        var selector = new ComboBox { DataContext = vm };
        selector.SetBinding(ItemsControl.ItemsSourceProperty, nameof(vm.PreselectedNextOptions));
        selector.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(vm.SelectedPreselectedNextOption)) { Mode = BindingMode.TwoWay });
        Assert.That(selector.SelectedItem, Is.SameAs(vm.PreselectedNextOptions.Single(option => option.ChannelId == "b")));

        var next = vm.VisibleChannels.Single(channel => channel.ChannelId == "b");
        next.DisplayName = "Новое название";
        Assert.That(selector.SelectedItem, Is.EqualTo(new PreselectedNextChannelOption("b", "Новое название (b)")));

        next.RemoveCommand.Execute(null);
        Assert.That(selector.SelectedItem, Is.SameAs(vm.PreselectedNextOptions.Single(option => option.ChannelId is null)));
        Assert.That(vm.SelectedPreselectedNextOption.ChannelId, Is.Null);
    }

    [Test]
    public async Task Registered_platform_resolves_verifies_and_saves_channel_routing()
    {
        const StreamingPlatform platform = StreamingPlatform.VkLive;
        var target = new ChannelKey(platform, "vk-c");
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_root, "channels.json"));
        Assert.That(catalog.Save(new()
        {
            Channels =
            [
                new() { Platform = platform, ChannelId = "vk-a", IsConfigured = true },
                new() { Platform = platform, ChannelId = "vk-b", IsConfigured = true },
                new() { Platform = platform, ChannelId = "vk-history", KnownAliases = ["Архивный канал"] },
            ],
            Platforms = [new() { Platform = platform, ActiveChannelId = "vk-a", SwitchMode = ChannelSwitchMode.AutoDiscover }],
        }), Is.True);

        var adapter = new NullResolveAdapter(
            platform,
            (channel, role, context) =>
            {
                Assert.That((channel, role, context), Is.EqualTo((target, PlatformAuthRole.Bot, "vk-bot-grant")));
                return new(true, "vk-bot");
            },
            (raw, context, _) =>
            {
                Assert.That((raw, context), Is.EqualTo(("@vk-c", "vk-bot-grant")));
                return Task.FromResult<ResolvedChannelIdentity?>(new(target, "Третий канал", raw, "https://vkvideo.ru/@vk-c"));
            },
            Accounts:
            [
                new(new(platform, "vk-bot"), "Бот VK",
                    [new("vk-bot-grant", PlatformAuthRole.Bot, "Бот", "vk-bot", "Бот VK", HasToken: true, IsDefault: false)]),
            ]);

        var catalogVm = new ChannelCatalogSettingsSectionViewModel(catalog, [adapter]) { SelectedPlatform = platform };
        var coordinator = new StubSwitchCoordinator(catalog);
        var contributor = new ChannelsSettingsSectionContributor(
            catalogVm, new(catalog, new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance)), catalog, coordinator);
        var session = new SettingsSessionState { Settings = new() };
        contributor.Load(session);
        using var dirty = contributor.SubscribeDirty(session, static () => { }, static () => { });

        Assert.Multiple(() =>
        {
            Assert.That(catalogVm.SelectedChannel?.ChannelId, Is.EqualTo("vk-a"));
            Assert.That(catalogVm.SelectedSwitchModeOption.Mode, Is.EqualTo(ChannelSwitchMode.AutoDiscover));
            Assert.That(catalogVm.SupportsOwnChannelAuthorization, Is.False);
        });

        catalogVm.NewChannelInput = "@vk-c";
        await catalogVm.AddChannelCommand.ExecuteAsync(null);
        var channel = catalogVm.SelectedChannel!;
        var bot = channel.Roles.Single(role => role.Role == PlatformAuthRole.Bot);
        Assert.Multiple(() =>
        {
            Assert.That(channel.ChannelId, Is.EqualTo(target.ChannelId));
            Assert.That(channel.SupportsMonitoring, Is.True);
            Assert.That(channel.MonitoringEnabled, Is.True);
            Assert.That(bot.CanAuthorize, Is.False);
        });
        bot.SelectedAccount = bot.Accounts.Single();
        await bot.VerifyCommand.ExecuteAsync(null);
        Assert.That(contributor.ValidateBeforeSave(), Is.Null);
        channel.MonitoringEnabled = false;
        channel.MakeActiveCommand.Execute(null);
        catalogVm.SelectedSwitchModeOption = catalogVm.SwitchModeOptions.Single(option => option.Mode == ChannelSwitchMode.PreselectedNext);
        catalogVm.SelectedPreselectedNextOption = catalogVm.PreselectedNextOptions.Single(option => option.ChannelId == "vk-b");

        catalog.Mutate(settings =>
        {
            settings.Channels.Add(new() { Platform = StreamingPlatform.YouTube, ChannelId = "yt-external", IsConfigured = true });
            settings.Platforms.Add(new() { Platform = StreamingPlatform.YouTube, ActiveChannelId = "yt-external" });
        });
        Assert.That((await contributor.SaveAsync(new(), appSettingsWritten: true)).Written, Is.True);
        Assert.That(coordinator.Switched, Is.EqualTo(new[] { (platform, target, true) }));

        catalogVm.Load();
        var saved = catalog.FindEntry(target)!;
        Assert.Multiple(() =>
        {
            Assert.That(catalogVm.SelectedChannel?.ChannelId, Is.EqualTo(target.ChannelId));
            Assert.That(catalogVm.SelectedSwitchModeOption.Mode, Is.EqualTo(ChannelSwitchMode.PreselectedNext));
            Assert.That(catalogVm.SelectedPreselectedNextOption.ChannelId, Is.EqualTo("vk-b"));
            Assert.That(saved.MonitoringEnabled, Is.False);
            Assert.That(saved.GetAccountAssignment(PlatformAuthRole.Bot)?.IsVerified, Is.True);
            Assert.That(catalog.FindEntry(new(platform, "vk-history"))?.IsConfigured, Is.False);
            Assert.That(catalog.GetActiveChannel(StreamingPlatform.YouTube)?.ChannelId, Is.EqualTo("yt-external"));
        });
    }

    [Test]
    public void Platform_without_account_or_monitoring_capabilities_keeps_saved_assignments_and_policy()
    {
        const StreamingPlatform platform = StreamingPlatform.VkLive;
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_root, "channels.json"));
        Assert.That(catalog.Save(new()
        {
            Channels = [new() { Platform = platform, ChannelId = "vk-a", IsConfigured = true, BotAccountId = "vk-bot", BotVerifiedSubjectId = "vk-bot" }],
            Platforms = [new() { Platform = platform, ActiveChannelId = "vk-a", SwitchMode = ChannelSwitchMode.AutoDiscover }],
        }), Is.True);
        var adapter = new NullResolveAdapter(platform, SupportedCapabilities: PlatformCapabilities.None);
        var catalogVm = new ChannelCatalogSettingsSectionViewModel(catalog, [adapter]) { SelectedPlatform = platform };
        catalogVm.Load();
        var channel = catalogVm.VisibleChannels.Single();
        Assert.Multiple(() =>
        {
            Assert.That(channel.SupportsMonitoring, Is.False);
            Assert.That(catalogVm.SwitchModeOptions.Select(option => option.Mode), Is.EqualTo(new[] { ChannelSwitchMode.Manual }));
            Assert.That(channel.Roles.Select(role => role.CanManageAccount), Is.All.False);
            Assert.That(catalogVm.ValidateAssignments(), Is.Null);
        });
        channel.DisplayName = "Канал VK";
        var settings = catalog.Load();
        catalogVm.ApplyTo(settings);
        Assert.Multiple(() =>
        {
            Assert.That(settings.Platforms.Single().SwitchMode, Is.EqualTo(ChannelSwitchMode.AutoDiscover));
            Assert.That(settings.Channels.Single().GetAccountAssignment(PlatformAuthRole.Bot)?.IsVerified, Is.True);
        });
    }

    [Test]
    public async Task ChannelCatalogVm_rejects_unresolved_channel_input_and_preserves_externally_cleared_preselected_next()
    {
        var gate = new SettingsWriteGate();
        var catalog = new ChannelCatalogStore(null, Path.Combine(_root, "channels2.json"), gate);
        catalog.Mutate(s =>
        {
            s.Channels =
            [
                new() { Platform = StreamingPlatform.Twitch, ChannelId = "tw-a", DisplayName = "A", IsConfigured = true },
                new() { Platform = StreamingPlatform.Twitch, ChannelId = "tw-b", DisplayName = "B", IsConfigured = true },
            ];
            s.Platforms =
            [
                new()
                {
                    Platform = StreamingPlatform.Twitch,
                    ActiveChannelId = "tw-a",
                    SwitchMode = ChannelSwitchMode.PreselectedNext,
                    PreselectedNextChannelId = "tw-b",
                },
            ];
        });

        var adapter = new NullResolveAdapter(StreamingPlatform.Twitch);
        var catalogVm = new ChannelCatalogSettingsSectionViewModel(catalog, [adapter]);
        catalogVm.Load();
        catalogVm.NewChannelInput = "nonexistent_user";
        await catalogVm.AddChannelCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(catalogVm.VisibleChannels, Has.Count.EqualTo(2));
            Assert.That(catalogVm.StatusNotice, Is.Not.Empty);
        });

        var target = catalog.Load();
        target.Platforms[0].PreselectedNextChannelId = null;
        catalogVm.ApplyTo(target);
        Assert.That(target.Platforms[0].PreselectedNextChannelId, Is.Null);
    }

    [Test]
    public async Task AddChannel_on_youtube_logs_failure_and_reports_youtube_error_instead_of_twitch()
    {
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_root, "channels3.json"));
        var adapter = new NullResolveAdapter(
            StreamingPlatform.YouTube,
            resolve: (_, _, _) => Task.FromException<ResolvedChannelIdentity?>(
                new YouTubeApiException(YouTubeApiErrorKind.Unauthorized, "Токен отсутствует.")));
        var logger = new CapturingLogger<ChannelCatalogSettingsSectionViewModel>();
        var catalogVm = new ChannelCatalogSettingsSectionViewModel(catalog, [adapter], logger: logger);
        catalogVm.Load();
        catalogVm.SelectedPlatform = StreamingPlatform.YouTube;
        catalogVm.NewChannelInput = "https://www.youtube.com/@bob217games";

        await catalogVm.AddChannelCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(catalogVm.StatusNoticeSeverity, Is.EqualTo(StatusSeverity.Error));
            Assert.That(catalogVm.StatusNotice, Does.Contain("YouTube").And.Not.Contains("Twitch"));
            Assert.That(logger.Warnings, Has.Count.EqualTo(1));
            Assert.That(logger.Warnings[0].Exception, Is.InstanceOf<YouTubeApiException>());
        });
    }

    [Test]
    public async Task AddChannel_on_youtube_with_bot_only_account_passes_bot_context_and_adds_channel()
    {
        var accountsPath = Path.Combine(_root, "yt-accounts.json");
        var ytAccounts = new YouTubeAccountsStore(
            NullLogger<YouTubeAccountsStore>.Instance,
            accountsPath);
        ytAccounts.UpsertContext(
            PlatformAuthRole.Bot,
            "yt-bot-only",
            "YouTube Bot",
            new()
            {
                ChannelId = "UC_BOT",
                ChannelTitle = "YouTube Bot",
                AccessToken = "bot-token",
            });

        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_root, "yt-channels.json"));
        string? capturedContext = null;
        var resolvedKey = new ChannelKey(StreamingPlatform.YouTube, "UC_FOREIGN");
        var adapter = new NullResolveAdapter(
            StreamingPlatform.YouTube,
            resolve: (raw, preferredContext, _) =>
            {
                capturedContext = preferredContext;
                return Task.FromResult<ResolvedChannelIdentity?>(new(
                    resolvedKey,
                    raw,
                    raw,
                    "https://youtube.com/@foreign"));
            });

        var catalogVm = new ChannelCatalogSettingsSectionViewModel(
            catalog,
            [adapter],
            youtubeAccountsStore: ytAccounts);
        catalogVm.Load();
        catalogVm.SelectedPlatform = StreamingPlatform.YouTube;
        catalogVm.NewChannelInput = "@foreign";

        await catalogVm.AddChannelCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(capturedContext, Is.EqualTo("yt-bot-only"));
            Assert.That(catalogVm.VisibleChannels, Has.Count.EqualTo(1));
            Assert.That(catalogVm.VisibleChannels[0].ChannelId, Is.EqualTo("UC_FOREIGN"));
        });
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception, string Message)> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
            {
                Warnings.Add((logLevel, exception, formatter(state, exception)));
            }
        }
    }

    private sealed class NullResolveAdapter(
        StreamingPlatform platform,
        Func<ChannelKey, PlatformAuthRole, string, ChannelAccessVerification>? verify = null,
        Func<string, string?, CancellationToken, Task<ResolvedChannelIdentity?>>? resolve = null,
        IReadOnlyList<PlatformAccount>? Accounts = null,
        PlatformCapabilities SupportedCapabilities = PlatformCapabilities.BackgroundStreamMonitoring | PlatformCapabilities.MultiChannelMonitoring)
        : IPlatformChannelAdapter
    {
        public StreamingPlatform Platform => platform;
        public PlatformCapabilities Capabilities => SupportedCapabilities;
        public IReadOnlyList<PlatformAccount>? ListAccounts() => Accounts;
        public Task<ResolvedChannelIdentity?> ResolveChannelIdentityAsync(string rawInput, string? preferredContextId, CancellationToken cancellationToken)
            => resolve?.Invoke(rawInput, preferredContextId, cancellationToken) ?? Task.FromResult<ResolvedChannelIdentity?>(null);
        public Task<ChannelAccessVerification> VerifyChannelAccessAsync(ChannelKey channel, PlatformAuthRole role, string contextId, CancellationToken cancellationToken)
            => Task.FromResult(verify?.Invoke(channel, role, contextId) ?? new(true, "sub"));
        public Task<ChannelLiveProbeResult> ProbeChannelLiveAsync(ChannelCatalogEntry channel, CancellationToken cancellationToken)
            => Task.FromResult(new ChannelLiveProbeResult(false, true, null));
    }

    private sealed class TestEmbeddedAuth : IEmbeddedTwitchAuthDialog
    {
        public Task<EmbeddedTwitchAuthResult> AuthorizeAsync(EmbeddedTwitchAuthRequest request)
        {
            Assert.That((request.Role, request.CheckBroadcasterChannel), Is.EqualTo((PlatformAuthRole.Bot, false)));
            var flow = new OAuthFlowResult("fake-token", "fake-refresh", [], "somebot", "90", 3600);
            return Task.FromResult(EmbeddedTwitchAuthResult.Completed(flow));
        }
    }

    public class InertProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new NotSupportedException(targetMethod?.Name);
    }

    [Test]
    public async Task ChannelSwitchPromptBanner_handles_one_and_two_starts_dismiss_and_suppress()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var coordinator = new StubSwitchCoordinator();
        using var banner = new ChannelSwitchPromptBannerViewModel(
            coordinator,
            bus,
            NullLogger<ChannelSwitchPromptBannerViewModel>.Instance);

        var statuses = new List<(string Message, StatusSeverity Severity)>();
        banner.StatusReported += (msg, sev) => statuses.Add((msg, sev));

        Assert.That(banner.IsVisible, Is.False);

        var ytB = new ChannelKey(StreamingPlatform.YouTube, "UC_B");
        var ytC = new ChannelKey(StreamingPlatform.YouTube, "UC_C");
        var single = new AutoDiscoverPrompt(
            StreamingPlatform.YouTube,
            new(StreamingPlatform.YouTube, "UC_A"),
            "Канал A",
            [new(ytB, "Канал B", "sb", "Эфир B", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)]);
        await bus.PublishAsync(new AutoDiscoverPromptChanged(StreamingPlatform.YouTube, single));

        Assert.Multiple(() =>
        {
            Assert.That(banner.IsVisible, Is.True);
            Assert.That(banner.HasMultipleCandidates, Is.False);
            Assert.That(banner.SelectedCandidate?.Channel, Is.EqualTo(ytB));
        });

        await banner.ConfirmCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(coordinator.Confirmed, Is.EqualTo(new[] { (StreamingPlatform.YouTube, ytB) }));
            Assert.That(statuses[^1].Severity, Is.EqualTo(StatusSeverity.Info));
        });

        var two = new AutoDiscoverPrompt(
            StreamingPlatform.YouTube,
            ytB,
            "Канал B",
            [
                new(new(StreamingPlatform.YouTube, "UC_A"), "Канал A", "sa", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
                new(ytC, "Канал C", "sc", "Эфир C", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            ]);
        await bus.PublishAsync(new AutoDiscoverPromptChanged(StreamingPlatform.YouTube, two));

        Assert.Multiple(() =>
        {
            Assert.That(banner.IsVisible, Is.True);
            Assert.That(banner.HasMultipleCandidates, Is.True);
            Assert.That(banner.Candidates, Has.Count.EqualTo(2));
        });

        banner.SelectedCandidate = banner.Candidates[1];
        await banner.ConfirmCommand.ExecuteAsync(null);
        Assert.That(coordinator.Confirmed[^1], Is.EqualTo((StreamingPlatform.YouTube, ytC)));

        await banner.DismissCommand.ExecuteAsync(null);
        Assert.That(coordinator.DismissedPlatforms, Contains.Item(StreamingPlatform.YouTube));

        await bus.PublishAsync(new AutoDiscoverPromptChanged(StreamingPlatform.YouTube, single));
        Assert.That(banner.IsVisible, Is.True);
        banner.Suppress();
        Assert.That(banner.IsVisible, Is.False);
    }

    [Test]
    public async Task StreamInfoTile_and_StreamMonitoringViewModel_surface_active_channel_preselected_next_and_monitoring_issues()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var gate = new SettingsWriteGate();
        var catalog = new ChannelCatalogStore(null, Path.Combine(_root, "channels.json"), gate);
        var ytA = new ChannelKey(StreamingPlatform.YouTube, "UC_A");
        var ytB = new ChannelKey(StreamingPlatform.YouTube, "UC_B");
        catalog.Mutate(s =>
        {
            s.Channels =
            [
                new() { Platform = StreamingPlatform.YouTube, ChannelId = "UC_A", DisplayName = "Канал A", IsConfigured = true, MonitoringEnabled = true },
                new() { Platform = StreamingPlatform.YouTube, ChannelId = "UC_B", DisplayName = "Канал B", IsConfigured = true, MonitoringEnabled = true },
            ];
            s.Platforms =
            [
                new()
                {
                    Platform = StreamingPlatform.YouTube,
                    ActiveChannelId = "UC_A",
                    SwitchMode = ChannelSwitchMode.PreselectedNext,
                    PreselectedNextChannelId = "UC_B",
                },
            ];
        });

        var platform = new StubPlatform(StreamingPlatform.YouTube, "YouTube", "Канал A");
        var registry = new StubPlatformRegistry(platform);

        using var tile = new StreamInfoTileViewModel(
            registry,
            bus,
            NullLogger<StreamInfoTileViewModel>.Instance,
            new ManualUiDispatcher(),
            new NoOpLauncher(),
            catalogStore: catalog);

        Assert.Multiple(() =>
        {
            Assert.That(tile.HasActiveChannel, Is.True);
            Assert.That(tile.ActiveChannelDisplayName, Is.EqualTo("Канал A"));
            Assert.That(tile.HasPreselectedNext, Is.True);
            Assert.That(tile.PreselectedNextText, Does.Contain("Канал B"));
            Assert.That(tile.HasMonitoringIssue, Is.False);
        });

        var unavailableSnap = new ChannelMonitoringSnapshot(
            ytB,
            "Канал B",
            ChannelMonitoringAvailability.Unavailable,
            MonitoringUnavailableReason.AccessUnverified,
            "Доступ канала B не подтверждён",
            StreamStatus.Unknown,
            null,
            DateTimeOffset.UtcNow,
            false);

        using var monitoring = new StreamMonitoringViewModel(bus, registry);
        await bus.PublishAsync(new ChannelMonitoringStateChanged(StreamingPlatform.YouTube, ytB, unavailableSnap));

        var ytGroup = monitoring.Groups.First(g => g.Platform == StreamingPlatform.YouTube);
        Assert.Multiple(() =>
        {
            Assert.That(tile.HasMonitoringIssue, Is.True);
            Assert.That(tile.MonitoringIssueText, Does.Contain("Канал B"));
            Assert.That(ytGroup.Channels.IsReported, Is.True);
            Assert.That(ytGroup.Channels.Severity, Is.EqualTo(StatusSeverity.Error));
            Assert.That(ytGroup.Channels.Tooltip, Does.Contain("Доступ канала B"));
        });

        catalog.Mutate(settings =>
        {
            var routing = settings.Platforms.Single();
            routing.ActiveChannelId = "UC_B";
            routing.PreselectedNextChannelId = "UC_A";
        });

        Assert.Multiple(() =>
        {
            Assert.That(tile.ActiveChannelDisplayName, Is.EqualTo("Канал B"));
            Assert.That(tile.PreselectedNextText, Does.Contain("Канал A"));
        });
    }

    [Test]
    public void StreamInfoTile_uses_the_twitch_debug_target_instead_of_the_catalog()
    {
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_root, "channels.json"));
        catalog.Mutate(settings => settings.Channels.Add(new()
        {
            Platform = StreamingPlatform.Twitch,
            ChannelId = "123",
            DisplayName = "Канал из каталога",
            IsConfigured = true,
        }));

        using var tile = new StreamInfoTileViewModel(
            new StubPlatformRegistry(new StubPlatform(StreamingPlatform.Twitch, "Twitch", "debug-channel")
            {
                UsesChannelCatalog = false,
            }),
            new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance),
            NullLogger<StreamInfoTileViewModel>.Instance,
            new ManualUiDispatcher(),
            new NoOpLauncher(),
            catalogStore: catalog);

        Assert.That(tile.ActiveChannelDisplayName, Is.EqualTo("debug-channel"));
    }

    private sealed class NoOpLauncher : IShellLauncher
    {
        public bool Open(string target) => true;
        public bool Reveal(string path) => true;
        public bool Start(string executable, params string[] arguments) => true;
    }

    private sealed class StubSwitchCoordinator(ChannelCatalogStore? catalog = null) : IChannelSwitchCoordinator
    {
        public Func<ChannelKey, ChannelSwitchResult>? Switch { get; set; }
        public List<(StreamingPlatform Platform, ChannelKey Channel, bool PreservePreselectedNext)> Switched { get; } = [];
        public List<(StreamingPlatform Platform, ChannelKey Channel)> Confirmed { get; } = [];
        public List<StreamingPlatform> DismissedPlatforms { get; } = [];

        public AutoDiscoverPrompt? GetActivePrompt(StreamingPlatform platform) => null;
        public IReadOnlyList<AutoDiscoverPrompt> GetActivePrompts() => [];

        public Task<ChannelSwitchResult> SwitchActiveChannelAsync(StreamingPlatform platform, ChannelKey targetChannel,
            bool preservePreselectedNext, CancellationToken cancellationToken = default)
        {
            Switched.Add((platform, targetChannel, preservePreselectedNext));
            return SwitchActiveChannelAsync(platform, targetChannel, cancellationToken);
        }

        public Task<ChannelSwitchResult> SwitchActiveChannelAsync(StreamingPlatform platform, ChannelKey targetChannel,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Switch?.Invoke(targetChannel) ?? new ChannelSwitchResult(ChannelSwitchOutcome.Switched, targetChannel));

        public async Task<ChannelSwitchResult> SwitchCatalogChannelAsync(ChannelCatalogEntry targetChannel,
            CancellationToken cancellationToken = default)
        {
            var result = await SwitchActiveChannelAsync(targetChannel.Platform, targetChannel.Key, preservePreselectedNext: true, cancellationToken);
            if (result.Succeeded && catalog is not null)
            {
                catalog.Mutate(settings =>
                {
                    settings.Channels.RemoveAll(channel => channel.Key == targetChannel.Key);
                    settings.Channels.Add(targetChannel);
                    settings.Platforms.Single(routing => routing.Platform == targetChannel.Platform).ActiveChannelId = targetChannel.ChannelId;
                });
            }

            return result;
        }

        public Task<ChannelSwitchResult?> SetPreselectedNextAsync(StreamingPlatform platform, ChannelKey? targetChannel, CancellationToken cancellationToken = default)
            => Task.FromResult<ChannelSwitchResult?>(null);

        public Task SetSwitchModeAsync(StreamingPlatform platform, ChannelSwitchMode mode, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<ChannelSwitchResult> ConfirmAutoDiscoverCandidateAsync(StreamingPlatform platform, ChannelKey candidateChannel, CancellationToken cancellationToken = default)
        {
            Confirmed.Add((platform, candidateChannel));
            return Task.FromResult(new ChannelSwitchResult(ChannelSwitchOutcome.Switched, candidateChannel));
        }

        public Task DismissAutoDiscoverPromptAsync(StreamingPlatform platform, CancellationToken cancellationToken = default)
        {
            DismissedPlatforms.Add(platform);
            return Task.CompletedTask;
        }

        public Task EvaluateStartupPendingAsync(StreamingPlatform platform, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubPlatformRegistry(params IChatPlatform[] platforms) : IChatPlatformRegistry
    {
        public IReadOnlyList<IChatPlatform> All => platforms;
        public bool TryGet(StreamingPlatform platform, [NotNullWhen(true)] out IChatPlatform? chatPlatform)
        {
            chatPlatform = platforms.FirstOrDefault(p => p.Id == platform);
            return chatPlatform is not null;
        }

        public IChatPlatform? Find(StreamingPlatform platform) => platforms.FirstOrDefault(p => p.Id == platform);
        public IChatPlatform GetRequired(StreamingPlatform platform) => Find(platform)!;
        public IReadOnlyList<IChatPlatform> GetEnabled() => platforms.Where(p => p.IsEnabled).ToArray();
        public IReadOnlyList<IChatPlatform> GetConnected() => platforms.Where(p => p.IsConnected).ToArray();
    }

    private sealed class StubPlatform(StreamingPlatform id, string displayName, string configuredChannel) : IChatPlatform
    {
        public StreamingPlatform Id => id;
        public string DisplayName => displayName;
        public PlatformCapabilities Capabilities => PlatformCapabilities.None;
        public bool IsEnabled => true;
        public bool IsConnected => true;
        public IStreamStatus StreamStatus { get; } = new StubStreamStatus();
        public PlatformBotIdentity? BotIdentity => null;
        public string? ConfiguredChannel => configuredChannel;
        public bool UsesChannelCatalog { get; init; } = true;
        public IPlatformMessenger Messenger => null!;
        public IChatDecorations Decorations => NullChatDecorations.Instance;
        public string? BuildStreamUrl(StreamInfo? currentStream = null) => null;
    }

    private sealed class StubStreamStatus : IStreamStatus
    {
        public StreamStatus CurrentStatus => StreamStatus.Offline;
        public StreamInfo? CurrentStream => null;
        public StreamStatusSnapshot GetSnapshot() => new(CurrentStatus, CurrentStream, null);
        public Task RefreshLiveSnapshotAsync() => Task.CompletedTask;
    }
}
