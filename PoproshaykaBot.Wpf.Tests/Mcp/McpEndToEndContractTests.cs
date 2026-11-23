using KeepShell.Bootstrap;
using KeepShell.Mcp;
using KeepShell.Testing;
using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Infrastructure.Logging;
using PoproshaykaBot.Core.Infrastructure.Runtime;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Mcp;
using PoproshaykaBot.Wpf.ViewModels;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;

namespace PoproshaykaBot.Wpf.Tests.Mcp;

[TestFixture]
[Category("Mcp")]
[NonParallelizable]
public sealed class McpEndToEndContractTests
{
    private static readonly string[] ExpectedKeepShellTools =
    [
        "get_shell_diagnostics",
        "query_elements",
        "inspect_element",
        "get_binding_errors",
        "get_ui_health",
        "audit_layout",
        "read_disk_logs",
        "wait_for",
        "scroll",
        "select_item",
        "set_range_value",
        "send_keys",
        "open_context_menu",
        "set_window_bounds",
        "set_appearance",
        "read_unhandled_exceptions",
    ];

    private static readonly string[] ExpectedDomainTools =
    [
        "get_bot_state",
        "get_diagnostics",
        "get_channels_state",
        "connect_bot",
        "disconnect_bot",
        "get_runtime_health",
        "get_settings_summary",
        "get_accounts_readiness",
        "get_chat_history",
        "get_commands_state",
        "get_broadcasts_state",
        "get_polls_state",
        "get_statistics_summary",
        "get_obs_state",
        "dry_run_command",
        "refresh_channel_monitoring",
        "switch_active_channel",
        "refresh_obs_chat_sources",
        "flush_statistics",
        "export_diagnostics_bundle",
    ];

    private static readonly string[] ForbiddenChatPublishingSubstrings =
    [
        "send_chat",
        "publish_chat",
        "whisper",
        "send_message",
    ];

    [Test]
    public async Task HttpTransport_AuthHostOriginAndDuplicateHeaders_AreEnforced()
    {
        await using var harness = await McpTestHarness.StartAsync(allowMutations: false);
        using var http = McpTestHarness.CreateRawHttpClient();

        using var missingTokenResponse = await http.PostAsync(harness.Endpoint, McpTestHarness.CreateListToolsPayload());

        using var wrongTokenRequest = new HttpRequestMessage(HttpMethod.Post, harness.Endpoint)
        {
            Content = McpTestHarness.CreateListToolsPayload(),
        };
        wrongTokenRequest.Headers.Add("Authorization", "Bearer wrong-token-value");
        using var wrongTokenResponse = await http.SendAsync(wrongTokenRequest);

        using var dnsRebindRequest = new HttpRequestMessage(HttpMethod.Post, harness.Endpoint)
        {
            Content = McpTestHarness.CreateListToolsPayload(),
        };
        dnsRebindRequest.Headers.Add("Authorization", $"Bearer {harness.Token}");
        dnsRebindRequest.Headers.Host = $"evil.example:{harness.BoundPort}";
        using var dnsRebindResponse = await http.SendAsync(dnsRebindRequest);

        using var fileOriginRequest = new HttpRequestMessage(HttpMethod.Post, harness.Endpoint)
        {
            Content = McpTestHarness.CreateListToolsPayload(),
        };
        fileOriginRequest.Headers.Add("Authorization", $"Bearer {harness.Token}");
        fileOriginRequest.Headers.Add("Origin", "file:///C:/evil.html");
        using var fileOriginResponse = await http.SendAsync(fileOriginRequest);

        using var crossOriginRequest = new HttpRequestMessage(HttpMethod.Post, harness.Endpoint)
        {
            Content = McpTestHarness.CreateListToolsPayload(),
        };
        crossOriginRequest.Headers.Add("Authorization", $"Bearer {harness.Token}");
        crossOriginRequest.Headers.Add("Origin", "https://evil.example");
        using var crossOriginResponse = await http.SendAsync(crossOriginRequest);

        var dupAuthStatus = await McpTestHarness.SendRawHttpStatusAsync(
            harness.BoundPort,
            $"POST /mcp HTTP/1.1\r\nHost: 127.0.0.1:{harness.BoundPort}\r\nAuthorization: Bearer {harness.Token}\r\nAuthorization: Bearer {harness.Token}\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{{}}");

        var dupTokenHeaderStatus = await McpTestHarness.SendRawHttpStatusAsync(
            harness.BoundPort,
            $"POST /mcp HTTP/1.1\r\nHost: 127.0.0.1:{harness.BoundPort}\r\n{McpAuth.TokenHeader}: {harness.Token}\r\n{McpAuth.TokenHeader}: {harness.Token}\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{{}}");

        var dupOriginStatus = await McpTestHarness.SendRawHttpStatusAsync(
            harness.BoundPort,
            $"POST /mcp HTTP/1.1\r\nHost: 127.0.0.1:{harness.BoundPort}\r\nOrigin: http://127.0.0.1\r\nOrigin: http://localhost\r\nAuthorization: Bearer {harness.Token}\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{{}}");

        await using var bearerClient = await harness.ConnectClientAsync();
        var bearerTools = await bearerClient.ListToolsAsync();

        await using var tokenHeaderClient = await harness.ConnectClientWithHeaderAsync(McpAuth.TokenHeader, harness.Token);
        var tokenHeaderTools = await tokenHeaderClient.ListToolsAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(missingTokenResponse.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(wrongTokenResponse.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(dnsRebindResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(fileOriginResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(crossOriginResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(dupAuthStatus, Is.EqualTo(400));
            Assert.That(dupTokenHeaderStatus, Is.EqualTo(400));
            Assert.That(dupOriginStatus, Is.EqualTo(400));
            Assert.That(bearerTools, Is.Not.Empty);
            Assert.That(tokenHeaderTools, Is.Not.Empty);
        }
    }

    [Test]
    public async Task ToolCatalog_HasZeroCollisions_AllRequiredTools_AndNoChatPublishingTools()
    {
        await using var harness = await McpTestHarness.StartAsync(allowMutations: false);
        await using var client = await harness.ConnectClientAsync();

        var tools = await client.ListToolsAsync();
        var names = tools.Select(static t => t.Name).ToList();
        var distinctCount = names.Distinct(StringComparer.OrdinalIgnoreCase).Count();

        var forbiddenMatches = names
            .Where(static name => ForbiddenChatPublishingSubstrings.Any(sub => name.Contains(sub, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(distinctCount, Is.EqualTo(names.Count), "В каталоге MCP-инструментов не должно быть дубликатов имён.");
            Assert.That(names, Is.SupersetOf(ExpectedKeepShellTools));
            Assert.That(names, Is.SupersetOf(ExpectedDomainTools));
            Assert.That(forbiddenMatches, Is.Empty, "MCP не должен предоставлять инструменты отправки сообщений в чат.");
        }
    }

    [Test]
    public async Task ReadOnlyDomainTools_ReturnValidJsonWithoutErrors()
    {
        await using var harness = await McpTestHarness.StartAsync(allowMutations: false);
        await using var client = await harness.ConnectClientAsync();

        var readCalls = new (string Name, Dictionary<string, object?>? Args)[]
        {
            ("get_bot_state", null),
            ("get_diagnostics", null),
            ("get_channels_state", null),
            ("get_runtime_health", null),
            ("get_settings_summary", null),
            ("get_accounts_readiness", null),
            ("get_chat_history", null),
            ("get_commands_state", null),
            ("get_broadcasts_state", null),
            ("get_polls_state", null),
            ("get_statistics_summary", null),
            ("get_obs_state", null),
            ("dry_run_command", new Dictionary<string, object?> { ["text"] = "!помощь" }),
        };

        using (Assert.EnterMultipleScope())
        {
            foreach (var (name, args) in readCalls)
            {
                using var doc = await McpTestHarness.CallToolJsonAsync(client, name, args);
                Assert.That(doc.RootElement.ValueKind, Is.EqualTo(System.Text.Json.JsonValueKind.Object), $"Инструмент {name} должен вернуть JSON-объект.");
            }
        }
    }

    [Test]
    public async Task MutatingDomainTools_WhenMutationsDisabled_AreRejected()
    {
        await using var harness = await McpTestHarness.StartAsync(allowMutations: false);
        await using var client = await harness.ConnectClientAsync();

        var mutatingCalls = new (string Name, Dictionary<string, object?>? Args)[]
        {
            ("connect_bot", null),
            ("disconnect_bot", null),
            ("refresh_channel_monitoring", null),
            ("switch_active_channel", new Dictionary<string, object?> { ["platform"] = "Twitch", ["channelId"] = "123" }),
            ("refresh_obs_chat_sources", null),
            ("flush_statistics", null),
            ("export_diagnostics_bundle", null),
        };

        using (Assert.EnterMultipleScope())
        {
            foreach (var (name, args) in mutatingCalls)
            {
                var (isError, text) = await McpTestHarness.CallToolExpectResultAsync(client, name, args);
                Assert.That(isError, Is.True, $"Мутирующий инструмент {name} должен отклоняться при AllowMutations=false.");
                Assert.That(text, Is.Not.Empty);
            }
        }
    }

    [TestCase("0")]
    [TestCase("1")]
    [TestCase("999")]
    [TestCase("Twitch,Twitch")]
    [TestCase("Twitch, YouTube")]
    [TestCase("kick")]
    [TestCase("invalid")]
    public async Task DomainTools_RejectInvalidPlatformValues(string invalidPlatform)
    {
        await using var harness = await McpTestHarness.StartAsync(allowMutations: true);
        await using var client = await harness.ConnectClientAsync();

        var calls = new (string Name, Dictionary<string, object?> Args)[]
        {
            ("connect_bot", new Dictionary<string, object?> { ["platform"] = invalidPlatform }),
            ("get_chat_history", new Dictionary<string, object?> { ["platform"] = invalidPlatform }),
            ("get_commands_state", new Dictionary<string, object?> { ["platform"] = invalidPlatform }),
            ("dry_run_command", new Dictionary<string, object?> { ["text"] = "!помощь", ["platform"] = invalidPlatform }),
        };

        using (Assert.EnterMultipleScope())
        {
            foreach (var (name, args) in calls)
            {
                var (isError, text) = await McpTestHarness.CallToolExpectResultAsync(client, name, args);
                Assert.That(isError, Is.True, $"Инструмент {name} обязан отклонить недопустимую площадку «{invalidPlatform}».");
                Assert.That(text, Is.Not.Empty);
            }
        }
    }

    [Test]
    public async Task AppConfigureServices_BuildsValidatedProvider_AndResolvesAllMcpAndBotAutomationDependencies()
    {
        var configureServices = typeof(App).GetMethod(
            "ConfigureServices",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.That(configureServices, Is.Not.Null, "Метод App.ConfigureServices не найден.");

        var tempDir = Path.Combine(Path.GetTempPath(), $"PoproshaykaBot-di-mcp-{Guid.NewGuid():N}");
        var logsDir = Path.Combine(tempDir, "logs");
        Directory.CreateDirectory(logsDir);

        try
        {
            var services = new ServiceCollection();
            var uiLogSink = new UiLogSink();
            ISettingsStore uiSettings = new MemorySettings();
            var loggingOptions = new KeepShellLoggingOptions
            {
                LogsDirectory = logsDir,
                FileNamePrefix = AppInfo.LogFilePrefix,
            };
            using var bindingErrors = new BindingErrorSink();

            configureServices!.Invoke(
                null,
                [services, uiLogSink, uiSettings, loggingOptions, bindingErrors, null]);

            services.AddSingleton(new WpfLogSink(100));
            services.AddSingleton(loggingOptions);
            services.AddSingleton(PreviousRunState.Clean);
            services.AddSingleton(new UnobservedTaskExceptionTracker());

            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });

            var botAutomation = provider.GetRequiredService<BotAutomation>();
            var mcpHost = provider.GetRequiredService<McpServerHost>();
            var mcpPreferences = provider.GetRequiredService<McpPreferences>();
            var resolvedBindingErrors = provider.GetRequiredService<BindingErrorSink>();
            var notificationGate = provider.GetRequiredService<IMcpConnectNotificationGate>();
            var wizardLauncher = provider.GetRequiredService<IOnboardingWizardLauncher>();

            var ctor = typeof(BotAutomation).GetConstructors().Single();
            var optionalDependencies = ctor.GetParameters()
                .Where(static p => p.HasDefaultValue)
                .ToArray();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(botAutomation, Is.Not.Null);
                Assert.That(mcpHost, Is.Not.Null);
                Assert.That(mcpPreferences, Is.Not.Null);
                Assert.That(resolvedBindingErrors, Is.SameAs(bindingErrors));
                Assert.That(notificationGate, Is.Not.Null);
                Assert.That(wizardLauncher, Is.Not.Null);

                foreach (var parameter in optionalDependencies)
                {
                    var resolved = provider.GetService(parameter.ParameterType);
                    Assert.That(
                        resolved,
                        Is.Not.Null,
                        $"Необязательная зависимость BotAutomation.{parameter.Name} ({parameter.ParameterType.Name}) должна разрешаться из DI-контейнера.");
                }
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
    public void ParseMcpStartupOptions_RejectsMissingOrInvalidValuesWithoutSwallowingNextFlag()
    {
        Assert.Throws<ArgumentException>(() => App.ParseMcpStartupOptions(["--mcp-port", "--mcp-allow-mutations=false"], static _ => null));
        Assert.Throws<ArgumentException>(() => App.ParseMcpStartupOptions(["--mcp-port"], static _ => null));
        Assert.Throws<ArgumentException>(() => App.ParseMcpStartupOptions(["--mcp-port", "not-a-port"], static _ => null));
        Assert.Throws<ArgumentException>(() => App.ParseMcpStartupOptions(["--mcp-token", "--enable-mcp"], static _ => null));
        Assert.Throws<ArgumentException>(() => App.ParseMcpStartupOptions(["--mcp-discovery-file"], static _ => null));
        Assert.Throws<ArgumentException>(() => App.ParseMcpStartupOptions(["--mcp-allow-mutations=maybe"], static _ => null));
    }

    [Test]
    public void McpConnectNotificationGate_ConcurrentFailedStartDoesNotClearActiveAttemptSuppression()
    {
        var gate = new McpConnectNotificationGate();
        gate.SuppressNextErrorDialog();
        gate.SuppressNextErrorDialog();
        gate.ResetErrorDialogSuppression();

        Assert.That(gate.TryConsumeErrorDialogSuppression(), Is.True);
        Assert.That(gate.TryConsumeErrorDialogSuppression(), Is.False);
    }

    [Test]
    public async Task DisconnectAsync_PreCancelledToken_DoesNotTriggerStateTransition()
    {
        var connection = new McpTestHarness.FakeBotConnectionController
        {
            CurrentPhase = PoproshaykaBot.Core.Infrastructure.Events.Lifecycle.BotLifecyclePhase.Connected,
        };
        await using var harness = await McpTestHarness.StartAsync(allowMutations: true, connectionController: connection);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Assert.ThrowsAsync<InvalidOperationException>(() => harness.BotAutomation.DisconnectAsync(cancellationToken: cts.Token));
        Assert.That(connection.StopCalls, Is.Zero);
        Assert.That(connection.CurrentPhase, Is.EqualTo(PoproshaykaBot.Core.Infrastructure.Events.Lifecycle.BotLifecyclePhase.Connected));
    }
}
