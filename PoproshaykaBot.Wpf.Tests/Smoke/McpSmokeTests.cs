using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PoproshaykaBot.Wpf.Tests.Smoke;

[TestFixture]
[NonParallelizable]
[Category("Smoke")]
[Category("Mcp")]
public sealed class McpSmokeTests
{
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan UiAppearTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(15);

    private SmokeTestSession? _session;
    private string? _discoveryPath;

    [TearDown]
    public void TearDown()
    {
        try
        {
            _session?.Dispose();
        }
        finally
        {
            _session = null;
            TryDeleteFile(_discoveryPath);
            _discoveryPath = null;
        }
    }

    [Test]
    public async Task McpSmoke_FullEndToEndWalkthrough_AndCleanExit()
    {
        var token = $"mcp-smoke-{Guid.NewGuid():N}";
        _discoveryPath = Path.Combine(Path.GetTempPath(), $"PoproshaykaBot-mcp-smoke-{Guid.NewGuid():N}.json");

        _session = SmokeTestSession.Launch(new()
        {
            SeedConfiguredSettings = true,
            ExtraArguments = ["--enable-mcp"],
            EnvironmentOverrides = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["POPROSHAYKA_MCP_PORT"] = "0",
                ["POPROSHAYKA_MCP_TOKEN"] = token,
                ["POPROSHAYKA_MCP_ALLOW_MUTATIONS"] = "1",
                ["POPROSHAYKA_MCP_DISCOVERY_FILE"] = _discoveryPath,
            },
        });

        var expectedPid = _session.App.ProcessId;
        var (endpoint, discoveryPort, isElevated, discoveryRaw) = await WaitForDiscoveryFileAsync(_discoveryPath, expectedPid, DiscoveryTimeout);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(discoveryPort, Is.GreaterThan(0), "Файл обнаружения должен содержать выданный ОС порт.");
            Assert.That(endpoint, Does.StartWith($"http://127.0.0.1:{discoveryPort}/"), "Endpoint в файле обнаружения должен указывать на loopback и выданный порт.");
            Assert.That(discoveryRaw, Does.Not.Contain(token), "Файл обнаружения не должен содержать токен доступа.");
        }

        string effectiveToken;

        if (isElevated)
        {
            await AssertUnauthorizedTokenRejectedAsync(endpoint, token);

            effectiveToken = ReadElevatedSessionTokenFromSettingsUi();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(effectiveToken, Is.Not.Empty, "В процессе с правами администратора должен быть сгенерирован одноразовый сессионный токен.");
                Assert.That(effectiveToken, Is.Not.EqualTo(token), "Процесс с правами администратора не должен принимать токен из переменной окружения.");
                Assert.That(discoveryRaw, Does.Not.Contain(effectiveToken), "Файл обнаружения не должен содержать сессионный токен.");
            }
        }
        else
        {
            effectiveToken = token;
        }

        using var process = Process.GetProcessById(expectedPid);

        await using (var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new(endpoint),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Authorization"] = $"Bearer {effectiveToken}",
            },
        }))
        await using (var client = await McpClient.CreateAsync(transport))
        {
            using (var shellState = await CallToolJsonAsync(client, "get_shell_state"))
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(shellState.Document.RootElement.GetProperty("appName").GetString(), Is.EqualTo("PoproshaykaBot"));
                    Assert.That(shellState.Document.RootElement.GetProperty("elevated").GetBoolean(), Is.EqualTo(isElevated));
                    Assert.That(shellState.Document.RootElement.GetProperty("allowMutations").GetBoolean(), Is.EqualTo(!isElevated));
                    Assert.That(shellState.Document.RootElement.GetProperty("sections").GetArrayLength(), Is.GreaterThan(0));
                    Assert.That(shellState.Document.RootElement.GetProperty("windows").GetArrayLength(), Is.GreaterThanOrEqualTo(1));
                }
            }

            using (var botState = await CallToolJsonAsync(client, "get_bot_state"))
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(botState.Document.RootElement.GetProperty("appVersion").GetString(), Is.Not.Null.And.Not.Empty);
                    Assert.That(botState.Document.RootElement.GetProperty("phase").GetString(), Is.Not.Null.And.Not.Empty);
                    Assert.That(botState.Document.RootElement.GetProperty("targetChannel").ValueKind, Is.EqualTo(JsonValueKind.Object));
                }
            }

            using (var runtimeHealth = await CallToolJsonAsync(client, "get_runtime_health"))
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(runtimeHealth.Document.RootElement.GetProperty("storage").GetProperty("mode").GetString(), Is.EqualTo("override"));
                    Assert.That(runtimeHealth.Document.RootElement.GetProperty("bindingErrors").GetProperty("totalCount").GetInt32(), Is.Zero);
                }
            }

            using (var diagnostics = await CallToolJsonAsync(client, "get_diagnostics"))
            {
                Assert.That(diagnostics.Document.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object));
            }

            using (var channelsState = await CallToolJsonAsync(client, "get_channels_state"))
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(channelsState.Document.RootElement.GetProperty("platforms").ValueKind, Is.EqualTo(JsonValueKind.Array));
                    Assert.That(channelsState.Document.RootElement.GetProperty("channels").ValueKind, Is.EqualTo(JsonValueKind.Array));
                }
            }

            using (var settingsSummary = await CallToolJsonAsync(client, "get_settings_summary"))
            {
                Assert.That(settingsSummary.Document.RootElement.GetProperty("entries").GetArrayLength(), Is.GreaterThan(0));
            }

            using (var accountsReadiness = await CallToolJsonAsync(client, "get_accounts_readiness"))
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(accountsReadiness.Document.RootElement.GetProperty("onboarding").ValueKind, Is.EqualTo(JsonValueKind.Object));
                    Assert.That(accountsReadiness.Document.RootElement.GetProperty("platforms").ValueKind, Is.EqualTo(JsonValueKind.Array));
                }
            }

            using (var commandsState = await CallToolJsonAsync(client, "get_commands_state"))
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(commandsState.Document.RootElement.GetProperty("prefix").GetString(), Is.Not.Null.And.Not.Empty);
                    Assert.That(commandsState.Document.RootElement.GetProperty("commands").GetArrayLength(), Is.GreaterThan(0));
                }
            }

            using (var dryRun = await CallToolJsonAsync(
                client,
                "dry_run_command",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["text"] = "!помощь",
                    ["platform"] = "Twitch",
                }))
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(dryRun.Document.RootElement.GetProperty("wouldExecute").GetBoolean(), Is.True);
                    Assert.That(dryRun.Document.RootElement.GetProperty("canonical").GetString(), Is.EqualTo("помощь"));
                }
            }

            using (var navigate = await CallToolJsonAsync(
                client,
                "navigate",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["key"] = "settings",
                }))
            {
                Assert.That(navigate.Document.RootElement.GetProperty("page").GetProperty("key").GetString(), Is.EqualTo("settings"));
            }

            using (var query = await CallToolJsonAsync(
                client,
                "query_elements",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["type"] = "Button",
                    ["limit"] = 20,
                }))
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(query.Document.RootElement.GetProperty("totalMatched").GetInt32(), Is.GreaterThanOrEqualTo(1));
                    Assert.That(query.Document.RootElement.GetProperty("elements").GetArrayLength(), Is.GreaterThanOrEqualTo(1));
                }
            }

            using (var viewTree = await CallToolJsonAsync(
                client,
                "get_view_tree",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["compact"] = true,
                    ["limit"] = 128,
                }))
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(viewTree.Document.RootElement.GetProperty("windowId").GetString(), Does.StartWith("0x"));
                    Assert.That(viewTree.Document.RootElement.GetProperty("count").GetInt32(), Is.GreaterThan(0));
                }
            }

            using (var capture = await CallToolJsonAsync(client, "capture_view"))
            {
                var capturedPath = capture.Document.RootElement.GetProperty("path").GetString();

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(capturedPath, Is.Not.Null.And.Not.Empty);
                    Assert.That(File.Exists(capturedPath), Is.True, $"Скриншот capture_view не найден: {capturedPath}");
                    Assert.That(capture.Document.RootElement.GetProperty("width").GetInt32(), Is.GreaterThan(0));
                    Assert.That(capture.Document.RootElement.GetProperty("height").GetInt32(), Is.GreaterThan(0));
                }

                Assert.That(new FileInfo(capturedPath!).Length, Is.GreaterThan(0), $"Файл скриншота пуст: {capturedPath}");
            }

            using (var logs = await CallToolJsonAsync(client, "read_logs"))
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(logs.Document.RootElement.GetProperty("entries").GetArrayLength(), Is.GreaterThan(0));
                    Assert.That(logs.RawText, Does.Not.Contain(token), "Ответ read_logs не должен содержать токен доступа.");
                    Assert.That(logs.RawText, Does.Not.Contain(effectiveToken), "Ответ read_logs не должен содержать активный токен доступа.");
                }
            }

            using (var bindingErrors = await CallToolJsonAsync(client, "get_binding_errors"))
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(bindingErrors.Document.RootElement.GetProperty("available").GetBoolean(), Is.True);
                    Assert.That(bindingErrors.Document.RootElement.GetProperty("totalCount").GetInt32(), Is.Zero,
                        $"Обнаружены ошибки привязок: {bindingErrors.RawText}");
                    Assert.That(bindingErrors.Document.RootElement.GetProperty("errors").GetArrayLength(), Is.Zero);
                }
            }

            if (isElevated)
            {
                await AssertMutatingCloseRejectedWhenElevatedAsync(client);
            }
            else
            {
                using var close = await CallToolJsonAsync(client, "close_app");

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(close.Document.RootElement.GetProperty("accepted").GetBoolean(), Is.True);
                    Assert.That(close.Document.RootElement.GetProperty("status").GetString(), Is.EqualTo("closing"));
                }
            }
        }

        if (isElevated)
        {
            _session.MainWindow.Close();
        }

        using var exitCts = new CancellationTokenSource(ProcessExitTimeout);

        try
        {
            await process.WaitForExitAsync(exitCts.Token);
        }
        catch (OperationCanceledException)
        {
            Assert.Fail($"Процесс PoproshaykaBot.Wpf не завершился после close_app за {ProcessExitTimeout.TotalSeconds:0} с.");
        }

        Assert.That(process.ExitCode, Is.Zero, "После close_app процесс должен завершиться с кодом выхода 0.");
    }

    [Test]
    public async Task UiSmoke_WithoutEnableMcp_DoesNotStartMcpServer()
    {
        var token = $"mcp-disabled-{Guid.NewGuid():N}";
        _discoveryPath = Path.Combine(Path.GetTempPath(), $"PoproshaykaBot-mcp-disabled-{Guid.NewGuid():N}.json");

        _session = SmokeTestSession.Launch(new()
        {
            SeedConfiguredSettings = true,
            EnvironmentOverrides = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["POPROSHAYKA_MCP_ENABLE"] = string.Empty,
                ["POPROSHAYKA_MCP_PORT"] = "0",
                ["POPROSHAYKA_MCP_TOKEN"] = token,
                ["POPROSHAYKA_MCP_DISCOVERY_FILE"] = _discoveryPath,
            },
        });

        await Task.Delay(TimeSpan.FromSeconds(1));

        Assert.That(File.Exists(_discoveryPath), Is.False,
            "Под --ui-smoke без --enable-mcp и POPROSHAYKA_MCP_ENABLE MCP-сервер не должен запускаться.");
    }

    private static async Task<(string Endpoint, int Port, bool IsElevated, string RawText)> WaitForDiscoveryFileAsync(
        string path,
        int expectedPid,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                try
                {
                    string raw;

                    await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        raw = await reader.ReadToEndAsync();
                    }

                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        using var doc = JsonDocument.Parse(raw);
                        var root = doc.RootElement;
                        var endpoint = root.GetProperty("endpoint").GetString();
                        var port = root.GetProperty("port").GetInt32();
                        var pid = root.GetProperty("pid").GetInt32();
                        var isElevated = root.TryGetProperty("isElevated", out var elevatedProp) && elevatedProp.GetBoolean();

                        if (!string.IsNullOrWhiteSpace(endpoint) && port > 0 && pid == expectedPid)
                        {
                            return (endpoint, port, isElevated, raw);
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
                {
                    lastError = exception;
                }
            }

            await Task.Delay(100);
        }

        Assert.Fail($"Файл обнаружения MCP «{path}» не появился или не содержит валидный JSON с pid={expectedPid} за {timeout.TotalSeconds:0} с. Последняя ошибка: {lastError?.Message}");
        return default;
    }

    private static async Task AssertUnauthorizedTokenRejectedAsync(string endpoint, string rejectedToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Authorization", $"Bearer {rejectedToken}");

        using var response = await http.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "В процессе с правами администратора токен из переменной окружения должен отклоняться с 401.");
    }

    private static async Task AssertMutatingCloseRejectedWhenElevatedAsync(McpClient client)
    {
        try
        {
            var result = await client.CallToolAsync("close_app");
            var text = string.Join(
                Environment.NewLine,
                result.Content.OfType<TextContentBlock>().Select(static block => block.Text));
            Assert.That(result.IsError, Is.True, $"В процессе с правами администратора close_app должен отклоняться: {text}");
        }
        catch (McpException)
        {
            // Ожидаемый отказ мутирующего инструмента в режиме администратора.
        }
    }

    private string ReadElevatedSessionTokenFromSettingsUi()
    {
        InvokeNamedButton("Настройки");
        InvokeNamedButton("Агентная отладка");

        var tokenEdit = WaitForDescendant(cf => cf.ByControlType(ControlType.Edit).And(cf.ByName("Токен доступа MCP")));
        Assert.That(tokenEdit, Is.Not.Null, "Поле «Токен доступа MCP» должно отображаться в разделе настроек «Агентная отладка».");

        var token = tokenEdit!.AsTextBox().Text.Trim();

        InvokeNamedButton("Обзор");

        return token;
    }

    private void InvokeNamedButton(string name)
    {
        var button = WaitForDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(name)));
        Assert.That(button, Is.Not.Null, $"Кнопка «{name}» должна быть доступна.");
        button!.AsButton().Invoke();
    }

    private AutomationElement? WaitForDescendant(Func<ConditionFactory, ConditionBase> condition)
    {
        var deadline = DateTime.UtcNow + UiAppearTimeout;

        while (DateTime.UtcNow < deadline)
        {
            var match = _session!.MainWindow.FindFirstDescendant(condition);

            if (match is not null)
            {
                return match;
            }

            Thread.Sleep(100);
        }

        return null;
    }

    private static async Task<ToolJsonResult> CallToolJsonAsync(
        McpClient client,
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments = null)
    {
        var result = arguments is null
            ? await client.CallToolAsync(toolName)
            : await client.CallToolAsync(toolName, arguments);

        var text = string.Join(
            Environment.NewLine,
            result.Content.OfType<TextContentBlock>().Select(static block => block.Text));

        Assert.That(result.IsError, Is.Not.True, $"Инструмент {toolName} вернул ошибку: {text}");
        Assert.That(text, Is.Not.Empty, $"Инструмент {toolName} вернул пустой ответ.");

        return new(JsonDocument.Parse(text), text);
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort cleanup
        }
    }

    private sealed class ToolJsonResult(JsonDocument document, string rawText) : IDisposable
    {
        public JsonDocument Document { get; } = document;

        public string RawText { get; } = rawText;

        public void Dispose()
        {
            Document.Dispose();
        }
    }
}
