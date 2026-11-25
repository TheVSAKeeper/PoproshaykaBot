using KeepShell.Bootstrap;
using KeepShell.Mcp;
using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using KeepShell.Views.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Onboarding;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.ViewModels.Onboarding;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.Views.Onboarding;
using PoproshaykaBot.Wpf.Views.Settings;
using Serilog.Events;
using Serilog.Parsing;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using KeepShellMcpOptions = KeepShell.Mcp.McpServerOptions;

namespace PoproshaykaBot.Wpf.Tests.Mcp;

[TestFixture]
[Category("Mcp")]
[NonParallelizable]
public sealed class McpSecurityRedactionTests
{
    private const string TwitchClientSecretCanary = "CANARY-TW-CLIENT-SECRET-9f8e7d6c5b4a";
    private const string TwitchBotAccessCanary = "CANARY-TW-BOT-ACCESS-1a2b3c4d5e6f";
    private const string TwitchBotRefreshCanary = "CANARY-TW-BOT-REFRESH-2b3c4d5e6f7a";
    private const string TwitchBroadcasterAccessCanary = "CANARY-TW-CAST-ACCESS-3c4d5e6f7a8b";
    private const string TwitchBroadcasterRefreshCanary = "CANARY-TW-CAST-REFRESH-4d5e6f7a8b9c";
    private const string YouTubeClientSecretCanary = "CANARY-YT-CLIENT-SECRET-5e6f7a8b9c0d";
    private const string YouTubeBotAccessCanary = "CANARY-YT-BOT-ACCESS-6f7a8b9c0d1e";
    private const string YouTubeBotRefreshCanary = "CANARY-YT-BOT-REFRESH-7a8b9c0d1e2f";
    private const string YouTubeBroadcasterAccessCanary = "CANARY-YT-CAST-ACCESS-8b9c0d1e2f3a";
    private const string YouTubeBroadcasterRefreshCanary = "CANARY-YT-CAST-REFRESH-9c0d1e2f3a4b";
    private const string ObsPasswordCanary = "CANARY-OBS-PASSWORD-0d1e2f3a4b5c";
    private const string PersistedMcpTokenCanary = "CANARY-MCP-PERSISTED-1e2f3a4b5c6d";
    private const string EffectiveMcpTokenCanary = "CANARY-MCP-EFFECTIVE-2f3a4b5c6d7e";

    private static readonly string[] AllCanaries =
    [
        TwitchClientSecretCanary,
        TwitchBotAccessCanary,
        TwitchBotRefreshCanary,
        TwitchBroadcasterAccessCanary,
        TwitchBroadcasterRefreshCanary,
        YouTubeClientSecretCanary,
        YouTubeBotAccessCanary,
        YouTubeBotRefreshCanary,
        YouTubeBroadcasterAccessCanary,
        YouTubeBroadcasterRefreshCanary,
        ObsPasswordCanary,
        PersistedMcpTokenCanary,
        EffectiveMcpTokenCanary,
    ];

    private static readonly string[] ViewResourceDictionaries =
    [
        "pack://application:,,,/KeepShell;component/Resources/Themes/Light.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Themes/Tokens.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Converters.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Styles/Controls.xaml",
        "pack://application:,,,/PoproshaykaBot.Wpf;component/Infrastructure/Converters/AppConverters.xaml",
    ];

    [Test]
    public async Task AllReadTools_Logs_DiskLogs_AndDiagnosticsBundle_NeverLeakSeededSecrets()
    {
        await using var harness = await McpTestHarness.StartAsync(
            allowMutations: true,
            token: EffectiveMcpTokenCanary,
            seedStores: static (settings, accounts, ytAccounts, obs) =>
            {
                settings.Mutate(current =>
                {
                    current.Twitch.ClientId = "test-client-id";
                    current.Twitch.ClientSecret = TwitchClientSecretCanary;
                    current.Twitch.Channel = "streamer";
                });

                accounts.SaveAll(
                    new()
                    {
                        Login = "bot",
                        AccessToken = TwitchBotAccessCanary,
                        RefreshToken = TwitchBotRefreshCanary,
                    },
                    new()
                    {
                        Login = "broadcaster",
                        AccessToken = TwitchBroadcasterAccessCanary,
                        RefreshToken = TwitchBroadcasterRefreshCanary,
                    });

                ytAccounts.SaveAll(
                    YouTubeClientSecretCanary,
                    new()
                    {
                        ChannelId = "UCbot",
                        AccessToken = YouTubeBotAccessCanary,
                        RefreshToken = YouTubeBotRefreshCanary,
                    },
                    new()
                    {
                        ChannelId = "UCcaster",
                        AccessToken = YouTubeBroadcasterAccessCanary,
                        RefreshToken = YouTubeBroadcasterRefreshCanary,
                    });

                obs.Save(new ObsIntegrationSettings
                {
                    Enabled = true,
                    Host = "127.0.0.1",
                    Port = 4455,
                    Password = ObsPasswordCanary,
                });
            });

        harness.Preferences.Token = PersistedMcpTokenCanary;

        var leakLine = "Диагностическая строка со всеми канарейками: " + string.Join(' ', AllCanaries);

        harness.LogSink.Emit(new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Warning,
            null,
            new MessageTemplateParser().Parse(leakLine),
            [new LogEventProperty("SourceContext", new ScalarValue("PoproshaykaBot.SecurityTest"))]));

        var diskLogPath = Path.Combine(harness.LogsDirectory, $"{AppInfo.LogFilePrefix}20261123.log");
        await File.WriteAllTextAsync(diskLogPath, $"[12:00:00 WRN] [SecurityTest] {leakLine}{Environment.NewLine}", Encoding.UTF8);

        await using var client = await harness.ConnectClientAsync();

        var toolCalls = new (string Name, Dictionary<string, object?>? Args)[]
        {
            ("get_bot_state", null),
            ("get_diagnostics", null),
            ("get_channels_state", null),
            ("get_runtime_health", null),
            ("get_settings_summary", null),
            ("get_accounts_readiness", new Dictionary<string, object?> { ["verifyPersisted"] = true }),
            ("get_chat_history", null),
            ("get_commands_state", null),
            ("get_broadcasts_state", null),
            ("get_polls_state", null),
            ("get_statistics_summary", null),
            ("get_obs_state", null),
            ("dry_run_command", new Dictionary<string, object?> { ["text"] = "!помощь" }),
            ("read_logs", new Dictionary<string, object?> { ["minLevel"] = "Debug", ["count"] = 50 }),
            ("read_disk_logs", new Dictionary<string, object?> { ["tailLines"] = 50 }),
        };

        using (Assert.EnterMultipleScope())
        {
            foreach (var (name, args) in toolCalls)
            {
                var result = await client.CallToolAsync(name, args);
                var text = McpTestHarness.ExtractText(result);

                Assert.That(result.IsError, Is.Not.True, $"Инструмент {name} вернул ошибку: {text}");
                AssertContainsNoCanaries(text, $"ответе инструмента {name}");
            }

            using var exportDoc = await McpTestHarness.CallToolJsonAsync(client, "export_diagnostics_bundle");
            var exportText = exportDoc.RootElement.GetRawText();
            AssertContainsNoCanaries(exportText, "ответе export_diagnostics_bundle");

            var zipPath = exportDoc.RootElement.GetProperty("path").GetString();
            Assert.That(zipPath, Is.Not.Null.And.Not.Empty);
            Assert.That(File.Exists(zipPath), Is.True, "Экспортированный архив диагностики должен существовать на диске.");

            await using var archive = await ZipFile.OpenReadAsync(zipPath!);
            Assert.That(archive.Entries, Is.Not.Empty);

            foreach (var entry in archive.Entries)
            {
                await using var entryStream = await entry.OpenAsync();
                using var reader = new StreamReader(entryStream, Encoding.UTF8);
                var content = await reader.ReadToEndAsync();
                AssertContainsNoCanaries(content, $"файле архива {entry.FullName}");
            }
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void McpSettingsSectionView_ViewSnapshot_RedactsTokenAndConnectSnippet()
    {
        TestResources.Ensure(ViewResourceDictionaries);
        AppScaledSizes.Register(Application.Current!);

        var options = new KeepShellMcpOptions
        {
            ServerName = "poproshaykabot",
            AppName = AppInfo.Name,
            AppVersion = AppInfo.Version,
            DefaultPort = 7656,
            ShotsDirectory = Path.Combine(Path.GetTempPath(), "pb-mcp-redaction-shots"),
        };
        var preferences = new McpPreferences(new MemorySettings(), options, isElevated: false)
        {
            Token = EffectiveMcpTokenCanary,
        };
        using var server = new McpServerHost(
            preferences,
            automation: null,
            dispatch: null,
            options,
            new EmptyProvider(),
            NullLoggerFactory.Instance,
            NullLogger<McpServerHost>.Instance);
        using var viewModel = new McpSettingsSectionViewModel(
            preferences,
            server,
            options,
            new StubDialogService(),
            new StubClipboardService());

        var view = new McpSettingsSectionView { DataContext = viewModel };
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            Content = view,
        };

        using var visualHost = VisualTestHost.Show(window, 800, 760);
        visualHost.Settle();

        var snapshot = ViewSnapshot.Capture(view);
        var serialized = JsonSerializer.Serialize(snapshot);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(serialized, Does.Not.Contain(EffectiveMcpTokenCanary), "Токен MCP не должен попадать в снимок визуального дерева.");
            Assert.That(serialized, Does.Contain(McpPrivacy.RedactedText), "Чувствительные поля токена и строки подключения должны маскироваться через McpPrivacy.");
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void CredentialsPageView_ViewSnapshot_RedactsRevealableTextBoxWhenMaskedAndRevealed()
    {
        TestResources.Ensure(ViewResourceDictionaries);
        AppScaledSizes.Register(Application.Current!);

        using var viewModel = new CredentialsPageViewModel(
            new StubClientCredentialsValidator(),
            new StubOnboardingChannelValidator(),
            NullLogger<CredentialsPageViewModel>.Instance,
            new ManualUiDispatcher())
        {
            ClientId = "test-client-id",
            ClientSecret = TwitchClientSecretCanary,
        };

        var view = new CredentialsPageView { DataContext = viewModel };
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            Content = view,
        };

        using var visualHost = VisualTestHost.Show(window, 760, 620);
        visualHost.Settle();

        var maskedSnapshot = ViewSnapshot.Capture(view);
        var maskedSerialized = JsonSerializer.Serialize(maskedSnapshot);

        var revealables = FindVisualChildren<RevealableTextBox>(view).ToList();
        Assert.That(revealables, Has.Count.EqualTo(2));

        foreach (var revealable in revealables)
        {
            revealable.IsRevealed = true;
        }

        visualHost.Settle();

        var revealedSnapshot = ViewSnapshot.Capture(view);
        var revealedSerialized = JsonSerializer.Serialize(revealedSnapshot);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(maskedSerialized, Does.Not.Contain(TwitchClientSecretCanary), "Секрет в маскированном RevealableTextBox не должен попадать в снимок.");
            Assert.That(maskedSerialized, Does.Contain(McpPrivacy.RedactedText), "Маскированный RevealableTextBox должен вымарываться через McpPrivacy.");
            Assert.That(revealedSerialized, Does.Not.Contain(TwitchClientSecretCanary), "Секрет в раскрытом RevealableTextBox не должен попадать в снимок.");
            Assert.That(revealedSerialized, Does.Contain(McpPrivacy.RedactedText), "Раскрытый RevealableTextBox должен вымарываться через McpPrivacy.");
        }
    }

    [Test]
    public async Task UnhandledExceptionJournal_RegisteredAfterAddKeepShell_PreservesPreBuildRecordsInMcp()
    {
        var journal = new UnhandledExceptionJournal();
        journal.Record("AppDomain.UnhandledException", new InvalidOperationException("pre-di-crash"), handled: false);

        var services = new ServiceCollection();
        services.AddKeepShell();
        services.AddSingleton(journal);

        await using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<UnhandledExceptionJournal>();

        Assert.That(resolved, Is.SameAs(journal), "Регистрация экземпляра журнала после AddKeepShell должна сохранять доконтейнерные записи.");
        Assert.That(resolved.Count, Is.EqualTo(1));

        await using var harness = await McpTestHarness.StartAsync(unhandledExceptionJournal: journal);
        await using var client = await harness.ConnectClientAsync();
        using var response = await McpTestHarness.CallToolJsonAsync(client, "read_unhandled_exceptions");
        var text = response.RootElement.GetRawText();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text, Does.Contain("AppDomain.UnhandledException"));
            Assert.That(text, Does.Contain("pre-di-crash"));
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindVisualChildren<T>(child))
            {
                yield return nested;
            }
        }
    }

    private static void AssertContainsNoCanaries(string text, string context)
    {
        foreach (var canary in AllCanaries)
        {
            Assert.That(
                text,
                Does.Not.Contain(canary),
                $"Утечка секрета ({canary}) обнаружена в {context}.");
        }
    }

    private sealed class EmptyProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class StubClientCredentialsValidator : IClientCredentialsValidator
    {
        public Task<ClientCredentialsValidationResult> ValidateAsync(
            string clientId,
            string clientSecret,
            CancellationToken cancellationToken) => Task.FromResult(ClientCredentialsValidationResult.Valid);
    }

    private sealed class StubOnboardingChannelValidator : IOnboardingChannelValidator
    {
        public Task<ChannelValidationResult> ValidateAsync(
            string login,
            string clientId,
            string accessToken,
            CancellationToken cancellationToken) => Task.FromResult(ChannelValidationResult.Skipped);

        public Task<ChannelValidationResult> ValidateWithAppTokenAsync(
            string login,
            string clientId,
            string clientSecret,
            CancellationToken cancellationToken) => Task.FromResult(ChannelValidationResult.Skipped);
    }

    private sealed class StubClipboardService : IClipboardService
    {
        public bool TrySetText(string? text) => true;
    }

    private sealed class StubDialogService : IDialogService
    {
        public Task<bool> ShowAsync(IDialogViewModel viewModel) => Task.FromResult(false);

        public Task<bool> ReplaceAsync(IDialogViewModel viewModel) => Task.FromResult(false);

        public bool Confirm(string title, string message, bool defaultYes = false) => defaultYes;

        public bool ConfirmWarning(string title, string message, bool defaultYes = false) => defaultYes;

        public void Info(string title, string message)
        {
        }

        public void Warning(string title, string message)
        {
        }

        public void Error(string title, string message)
        {
        }
    }
}
