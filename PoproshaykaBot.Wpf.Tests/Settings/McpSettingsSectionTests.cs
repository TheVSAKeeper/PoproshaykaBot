using KeepShell.Bootstrap;
using KeepShell.Mcp;
using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using KeepShell.ViewModels;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;
using PoproshaykaBot.Wpf.Views.Settings;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KeepShellMcpOptions = KeepShell.Mcp.McpServerOptions;

namespace PoproshaykaBot.Wpf.Tests.Settings;

[TestFixture]
[Apartment(ApartmentState.STA)]
[Category("Mcp")]
[NonParallelizable]
public sealed class McpSettingsSectionTests
{
    private static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(15);

    private static readonly string[] ViewResourceDictionaries =
    [
        "pack://application:,,,/KeepShell;component/Resources/Themes/Light.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Themes/Tokens.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Converters.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Styles/Controls.xaml",
        "pack://application:,,,/PoproshaykaBot.Wpf;component/Infrastructure/Converters/AppConverters.xaml",
    ];

    [OneTimeSetUp]
    public void EnsureApplication()
    {
        TestResources.Ensure(ViewResourceDictionaries);
        AppScaledSizes.Register(Application.Current!);
    }

    [Test]
    public void StatusText_UpdatesReactivelyAcrossDisabledStartingRunningElevatedAndFailedStates()
    {
        var options = CreateOptions();
        var preferences = CreatePreferences(options, enabled: false, isElevated: false);
        var server = CreateHost(preferences, options);

        using (server)
        using (var vm = new McpSettingsSectionViewModel(
            preferences,
            server,
            options,
            new RecordingDialogService(),
            new RecordingClipboardService(),
            new ManualUiDispatcher()))
        {
            var observedStatuses = new List<string> { vm.StatusText };
            vm.PropertyChanged += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(McpSettingsSectionViewModel.StatusText))
                {
                    observedStatuses.Add(vm.StatusText);
                }
            };

            Assert.That(vm.StatusText, Does.Contain("выключен"));

            preferences.ApplyRuntimeOverrides(enabled: true, port: 0, token: "status-test-token-1234567890abcdef");
            server.Apply();

            WaitOnDispatcher(() => server.IsRunning && !server.IsStarting, StateTimeout);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(server.BoundPort, Is.Not.Null);
                Assert.That(vm.Url, Is.EqualTo($"http://127.0.0.1:{server.BoundPort}{options.EndpointPath}"));
                Assert.That(vm.StatusText, Is.EqualTo($"Сервер слушает {server.Endpoint}"));
                Assert.That(observedStatuses, Has.Some.Contains("запускается"));
            }
        }

        var elevatedOptions = CreateOptions();
        var elevatedPreferences = CreatePreferences(elevatedOptions, enabled: false, isElevated: true);
        var elevatedServer = CreateHost(elevatedPreferences, elevatedOptions);

        using (elevatedServer)
        using (var elevatedVm = new McpSettingsSectionViewModel(
            elevatedPreferences,
            elevatedServer,
            elevatedOptions,
            new RecordingDialogService(),
            new RecordingClipboardService(),
            new ManualUiDispatcher()))
        {
            elevatedPreferences.ApplyRuntimeOverrides(enabled: true, port: 0);
            elevatedServer.Apply();

            WaitOnDispatcher(() => elevatedServer.IsRunning && !elevatedServer.IsStarting, StateTimeout);

            Assert.That(
                elevatedVm.StatusText,
                Is.EqualTo($"Сервер слушает {elevatedServer.Endpoint} (только чтение – права администратора)"));
        }

        var blocker = new TcpListener(IPAddress.Loopback, 0);
        blocker.Start();
        var occupiedPort = ((IPEndPoint)blocker.LocalEndpoint).Port;

        try
        {
            var failedOptions = CreateOptions(occupiedPort);
            var failedPreferences = CreatePreferences(failedOptions, enabled: true, isElevated: false, port: occupiedPort);
            var failedServer = CreateHost(failedPreferences, failedOptions);

            using (failedServer)
            using (var failedVm = new McpSettingsSectionViewModel(
                failedPreferences,
                failedServer,
                failedOptions,
                new RecordingDialogService(),
                new RecordingClipboardService(),
                new ManualUiDispatcher()))
            {
                failedServer.Apply();
                WaitOnDispatcher(() => failedServer.LastError is not null && !failedServer.IsStarting, StateTimeout);

                Assert.That(failedVm.StatusText, Is.EqualTo("Сервер включён, но не поднялся"));
            }
        }
        finally
        {
            blocker.Stop();
        }
    }

    [Test]
    public void SnippetFormats_HasFiveFormatsMatchingMcpConnectSnippets()
    {
        var options = CreateOptions();
        var preferences = CreatePreferences(options, enabled: false, isElevated: false);
        using var server = CreateIdleHost(preferences, options);
        using var vm = new McpSettingsSectionViewModel(
            preferences,
            server,
            options,
            new RecordingDialogService(),
            new RecordingClipboardService(),
            new ManualUiDispatcher());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.SnippetFormats, Has.Count.EqualTo(McpConnectSnippets.FormatCount));
            Assert.That(vm.SnippetFormats, Has.Count.EqualTo(5));

            for (var index = 0; index < McpConnectSnippets.FormatCount; index++)
            {
                vm.SelectedSnippetFormatIndex = index;
                var expected = McpConnectSnippets.For(index, vm.Url, vm.DisplayToken, options.ServerName);
                Assert.That(vm.ConnectSnippet, Is.EqualTo(expected), $"Формат индекса {index} должен совпадать с McpConnectSnippets.For.");
            }
        }
    }

    [Test]
    public void CopyCommands_ShowInlineFeedbackWithoutDialog_AndClearOnTimerEditOrDispose()
    {
        var options = CreateOptions();
        var preferences = CreatePreferences(options, enabled: false, isElevated: false);
        using var server = CreateIdleHost(preferences, options);
        var dialog = new RecordingDialogService();
        var clipboard = new RecordingClipboardService();
        var dispatcher = new ManualUiDispatcher();

        using var vm = new McpSettingsSectionViewModel(preferences, server, options, dialog, clipboard, dispatcher);

        Assert.That(dispatcher.Timers, Has.Count.EqualTo(1));
        var feedbackTimer = dispatcher.Timers[0];

        vm.CopyTokenCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(clipboard.LastText, Is.EqualTo(vm.DisplayToken));
            Assert.That(vm.CopyFeedback, Is.Not.Null.And.Not.Empty);
            Assert.That(dialog.ErrorCalls, Is.Zero);
            Assert.That(feedbackTimer.IsRunning, Is.True);
        }

        feedbackTimer.Tick();
        Assert.That(vm.CopyFeedback, Is.Null);

        vm.CopyConnectSnippetCommand.Execute(null);
        Assert.That(vm.CopyFeedback, Is.Not.Null.And.Not.Empty);
        Assert.That(clipboard.LastText, Is.EqualTo(vm.ConnectSnippet));

        vm.SelectedSnippetFormatIndex = 2;
        Assert.That(vm.CopyFeedback, Is.Null);

        vm.CopyTokenCommand.Execute(null);
        Assert.That(vm.CopyFeedback, Is.Not.Null.And.Not.Empty);

        vm.PortText = "7778";
        Assert.That(vm.CopyFeedback, Is.Null);

        vm.CopyTokenCommand.Execute(null);
        Assert.That(feedbackTimer.IsRunning, Is.True);
        vm.Dispose();
        Assert.That(feedbackTimer.IsRunning, Is.False);
    }

    [Test]
    public void PortText_ValidationRejectsInvalidValues_AcceptsValidPort_AndProducesZeroBindingErrors()
    {
        var options = CreateOptions(7656);
        var preferences = CreatePreferences(options, enabled: false, isElevated: false, port: 7656);
        using var server = CreateIdleHost(preferences, options);
        using var vm = new McpSettingsSectionViewModel(
            preferences,
            server,
            options,
            new RecordingDialogService(),
            new RecordingClipboardService(),
            new ManualUiDispatcher());

        using var bindingErrors = BindingErrorSink.Attach();
        var view = new McpSettingsSectionView { DataContext = vm };
        var window = new Window { WindowStyle = WindowStyle.None, Content = view };

        using var visualHost = VisualTestHost.Show(window, 760, 720);
        visualHost.Settle();

        using (Assert.EnterMultipleScope())
        {
            foreach (var invalid in new[] { "abc", "80", "70000" })
            {
                vm.PortText = invalid;
                visualHost.Settle();

                Assert.That(vm.HasPortError, Is.True, $"Порт «{invalid}» должен давать ошибку валидации.");
                Assert.That(vm.PortError, Is.Not.Null.And.Not.Empty);
                Assert.That(preferences.Port, Is.EqualTo(7656), $"Невалидный порт «{invalid}» не должен менять Preferences.Port.");
            }

            vm.PortText = "7777";
            visualHost.Settle();

            Assert.That(vm.HasPortError, Is.False);
            Assert.That(vm.PortError, Is.Null);
            Assert.That(preferences.Port, Is.EqualTo(7777));
            Assert.That(bindingErrors.Count, Is.Zero, () => bindingErrors.Report);
        }
    }

    [Test]
    public void ElevatedMode_DisablesMutationToggle_ExposesEffectiveToken_AndMarksViewInputsSensitive()
    {
        var options = CreateOptions();
        var preferences = CreatePreferences(options, enabled: false, isElevated: true);
        using var server = CreateIdleHost(preferences, options);
        using var vm = new McpSettingsSectionViewModel(
            preferences,
            server,
            options,
            new RecordingDialogService(),
            new RecordingClipboardService(),
            new ManualUiDispatcher());

        vm.AllowMutations = true;

        var view = new McpSettingsSectionView { DataContext = vm };
        var window = new Window { WindowStyle = WindowStyle.None, Content = view };
        using var visualHost = VisualTestHost.Show(window, 760, 720);
        visualHost.Settle();

        var sensitiveBoxes = Descendants<TextBox>(view)
            .Where(static tb => tb.TemplatedParent is not ComboBox && McpPrivacy.GetIsSensitive(tb))
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.CanToggleMutations, Is.False);
            Assert.That(vm.AllowMutations, Is.False);
            Assert.That(vm.DisplayToken, Is.EqualTo(preferences.EffectiveToken));
            Assert.That(vm.DisplayToken, Is.Not.EqualTo(preferences.Token));
            Assert.That(sensitiveBoxes, Has.Length.EqualTo(2));
            Assert.That(sensitiveBoxes.Any(tb => string.Equals(tb.Text, vm.DisplayToken, StringComparison.Ordinal)), Is.True);
            Assert.That(sensitiveBoxes.Any(tb => string.Equals(tb.Text, vm.ConnectSnippet, StringComparison.Ordinal)), Is.True);
        }
    }

    [Test]
    public void Dispose_DetachesPropertyChangedListeners()
    {
        var options = CreateOptions();
        var preferences = CreatePreferences(options, enabled: false, isElevated: false);
        using var server = CreateIdleHost(preferences, options);
        var vm = new McpSettingsSectionViewModel(
            preferences,
            server,
            options,
            new RecordingDialogService(),
            new RecordingClipboardService(),
            new ManualUiDispatcher());

        var raised = 0;
        vm.PropertyChanged += (_, _) => raised++;

        vm.Dispose();

        preferences.Port = 7890;
        preferences.Token = "detached-listener-token-1234567890";
        preferences.AllowMutations = true;

        Assert.That(raised, Is.Zero);
    }

    [Test]
    public void Contributor_ReportsMcpKeyAndIsDraftless()
    {
        var options = CreateOptions();
        var preferences = CreatePreferences(options, enabled: false, isElevated: false);
        using var server = CreateIdleHost(preferences, options);
        using var vm = new McpSettingsSectionViewModel(
            preferences,
            server,
            options,
            new RecordingDialogService(),
            new RecordingClipboardService(),
            new ManualUiDispatcher());

        var contributor = new McpSettingsSectionContributor(vm);
        var section = contributor.CreateSection();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(contributor.SectionKey, Is.EqualTo("mcp"));
            Assert.That(section.Key, Is.EqualTo("mcp"));
            Assert.That(contributor.IsDraftless, Is.True);
        }
    }

    [Test]
    public void EphemeralPortBeforeBind_HidesConnectSnippetAndDisablesCopyCommand()
    {
        var options = CreateOptions();
        var preferences = CreatePreferences(options, enabled: false, isElevated: false, port: 7656);
        preferences.ApplyRuntimeOverrides(enabled: true, port: 0, token: "ephemeral-port-test-token-1234", allowMutations: false);

        using var server = CreateIdleHost(preferences, options);
        var clipboard = new RecordingClipboardService();
        using var vm = new McpSettingsSectionViewModel(
            preferences,
            server,
            options,
            new RecordingDialogService(),
            clipboard,
            new ManualUiDispatcher());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.HasConnectEndpoint, Is.False);
            Assert.That(vm.Url, Is.Empty);
            Assert.That(vm.ConnectSnippet, Is.Empty);
            Assert.That(vm.CopyConnectSnippetCommand.CanExecute(null), Is.False);
        }
    }

    private static KeepShellMcpOptions CreateOptions(int defaultPort = 7656)
    {
        return new()
        {
            ServerName = "poproshaykabot",
            AppName = AppInfo.Name,
            AppVersion = AppInfo.Version,
            DefaultPort = defaultPort,
            ShotsDirectory = Path.Combine(Path.GetTempPath(), "pb-mcp-settings-tests"),
        };
    }

    private static McpPreferences CreatePreferences(
        KeepShellMcpOptions options,
        bool enabled,
        bool isElevated,
        int? port = null)
    {
        var settings = new MemorySettings();
        ISettingsStore store = settings;
        store.SetBool(McpPreferences.EnabledKey, enabled);
        store.SetValue(McpPreferences.TokenKey, "persisted-settings-token-12345678");

        if (port is { } value)
        {
            store.SetInt(McpPreferences.PortKey, value);
        }

        return new(settings, options, isElevated);
    }

    private static McpServerHost CreateIdleHost(McpPreferences preferences, KeepShellMcpOptions options)
    {
        return new(
            preferences,
            automation: null,
            dispatch: null,
            options,
            new EmptyProvider(),
            NullLoggerFactory.Instance,
            NullLogger<McpServerHost>.Instance);
    }

    private static McpServerHost CreateHost(McpPreferences preferences, KeepShellMcpOptions options)
    {
        var dispatch = new McpDispatch(TimeSpan.FromSeconds(5));
        var shell = new StubShellViewModel();
        var automation = new ShellAutomation(
            new Lazy<ShellViewModelBase>(() => shell),
            logSink: null,
            new StubLifetime(),
            preferences,
            options,
            dispatch,
            NullLogger<ShellAutomation>.Instance);

        return new(
            preferences,
            automation,
            dispatch,
            options,
            new EmptyProvider(),
            NullLoggerFactory.Instance,
            NullLogger<McpServerHost>.Instance);
    }

    private static void WaitOnDispatcher(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (!predicate() && DateTime.UtcNow < deadline)
        {
            VisualTestHost.Pump();
            Thread.Sleep(20);
        }

        VisualTestHost.Pump();
        Assert.That(predicate(), Is.True, "Ожидаемое состояние MCP-сервера не наступило за отведённое время.");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);

            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    private sealed class EmptyProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class StubLifetime : IApplicationLifetime
    {
        public bool IsMainWindowMinimized => false;

        public void Shutdown()
        {
        }
    }

    private sealed class StubShellViewModel : ShellViewModelBase
    {
        public StubShellViewModel()
            : base(new ModalHostViewModel())
        {
            Sections.Add(new NavigationItem("Обзор", PackIconLucideKind.House, key: "overview"));
        }

        protected override void NavigateToSettings()
        {
        }
    }

    private sealed class RecordingClipboardService : IClipboardService
    {
        public string? LastText { get; private set; }

        public bool TrySetText(string? text)
        {
            LastText = text;
            return true;
        }
    }

    private sealed class RecordingDialogService : IDialogService
    {
        public int ErrorCalls { get; private set; }

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
            ErrorCalls++;
        }
    }
}
