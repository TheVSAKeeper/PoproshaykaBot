using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Core.YouTube.Quota;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.Views.Settings;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PoproshaykaBot.Wpf.Tests.Settings;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class YouTubeSettingsSectionViewTests
{
    private static readonly string[] Dictionaries =
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
        TestResources.Ensure(Dictionaries);
    }

    [Test]
    public void Блоки_транспорта_и_категорий_квоты_отрисовываются_без_обрезки_по_ширине()
    {
        var quotaStatus = new YouTubeQuotaStatus(
            "proj-1",
            new DateOnly(2026, 11, 24),
            new DateTimeOffset(2026, 11, 25, 8, 0, 0, TimeSpan.Zero),
            10_000,
            91,
            0,
            75,
            16,
            9_909,
            false,
            YouTubeQuotaReliability.Reliable,
            true,
            new Dictionary<YouTubeQuotaCategory, int>
            {
                [YouTubeQuotaCategory.ChatSend] = 60,
                [YouTubeQuotaCategory.StreamMonitoring] = 12,
                [YouTubeQuotaCategory.ChatRead] = 15,
                [YouTubeQuotaCategory.ChannelAndBroadcastInfo] = 4,
            });

        var chatStatus = new YouTubeChatStatus(
            ConnectionState.Connected,
            YouTubeChatTransport.InnerTube,
            ChatReadIssue.None,
            "@streamer",
            "chat-1",
            null,
            null,
            null,
            10,
            0,
            0);

        var monitorStatus = new YouTubeMonitoringStatus(
            true,
            StreamStatus.Online,
            YouTubeMonitorIssue.None,
            "UC1",
            "vid-1",
            "chat-1",
            1,
            DateTimeOffset.UtcNow,
            0,
            YouTubeMonitoringTransport.HybridInnerTubeAndDataApi);

        using var vm = new YouTubeSettingsSectionViewModel(
            new StubOAuthService(),
            new StubShellLauncher(),
            new StubDialogService(),
            new ManualUiDispatcher(),
            TimeProvider.System,
            NullLoggerFactory.Instance,
            new StubQuotaGovernor(quotaStatus),
            new StubChatDiagnostics(chatStatus),
            new StubMonitorDiagnostics(monitorStatus));

        vm.Load(new YouTubeSettings { Enabled = true, ProjectId = "proj-1" }, string.Empty);

        var view = new YouTubeSettingsSectionView { DataContext = vm };
        view.Measure(new Size(740, 1400));
        view.Arrange(new Rect(0, 0, 740, 1400));
        view.UpdateLayout();

        var texts = Descendants<TextBlock>(view)
            .Where(static tb => tb.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(tb.Text))
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(texts.Any(tb => tb.Text.Contains("Как сейчас работает YouTube", StringComparison.Ordinal)), Is.True);
            Assert.That(texts.Any(tb => tb.Text.Contains("InnerTube (без расхода квоты)", StringComparison.Ordinal)), Is.True);
            Assert.That(texts.Any(tb => tb.Text.Contains("Куда уходит квота за сегодня", StringComparison.Ordinal)), Is.True);
            Assert.That(texts.Any(tb => tb.Text == "Отправка в чат"), Is.True);
            Assert.That(texts.Any(tb => tb.Text == "60 ед."), Is.True);
            Assert.That(texts.All(tb => tb.ActualWidth <= 740.0 + 0.5), Is.True);
        }
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

    private sealed class StubQuotaGovernor(YouTubeQuotaStatus status) : IYouTubeQuotaGovernor
    {
        public bool CanAfford(YouTubeQuotaPriority priority, int cost) => true;

        public bool TryReserve(
            PlatformAuthRole role,
            YouTubeQuotaPriority priority,
            int cost,
            out YouTubeQuotaLease lease,
            YouTubeQuotaCategory category = YouTubeQuotaCategory.Other)
        {
            lease = default!;
            return false;
        }

        public void MarkExhaustedFromServer(PlatformAuthRole role)
        {
        }

        public YouTubeQuotaStatus CaptureDiagnostics() => status;
    }

    private sealed class StubChatDiagnostics(YouTubeChatStatus status) : IYouTubeChatDiagnostics
    {
        public YouTubeChatStatus CaptureDiagnostics() => status;
    }

    private sealed class StubMonitorDiagnostics(YouTubeMonitoringStatus status) : IYouTubeMonitorDiagnostics
    {
        public YouTubeMonitoringStatus CaptureDiagnostics() => status;
    }

    private sealed class StubShellLauncher : IShellLauncher
    {
        public bool Open(string pathOrUrl) => true;

        public bool Reveal(string path) => true;

        public bool Start(string executable, params string[] arguments) => true;
    }

    private sealed class StubDialogService : IDialogService
    {
        public Task<bool> ShowAsync(IDialogViewModel viewModel) => Task.FromResult(false);

        public Task<bool> ReplaceAsync(IDialogViewModel viewModel) => Task.FromResult(false);

        public bool Confirm(string title, string message, bool defaultYes = false) => true;

        public bool ConfirmWarning(string title, string message, bool defaultYes = false) => true;

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

    private sealed class StubOAuthService : IYouTubeOAuthService
    {
        public event Action<PlatformAuthRole, string>? StatusChanged
        {
            add { }
            remove { }
        }

        public Task<YouTubeOAuthFlowResult> StartOAuthFlowAsync(
            PlatformAuthRole role,
            string clientId,
            string clientSecret,
            string[]? scopes,
            Action<string> onAuthUrlReady,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<YouTubeOAuthFlowResult> StartOAuthFlowToDraftAsync(
            PlatformAuthRole role,
            string clientId,
            string clientSecret,
            string[]? scopes,
            Action<string> onAuthUrlReady,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<string> RefreshTokenAsync(PlatformAuthRole role, string clientId, string clientSecret, string refreshToken, CancellationToken ct = default)
            => Task.FromResult(string.Empty);

        public Task<string?> GetAccessTokenAsync(PlatformAuthRole role, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public bool TryClearAccessToken(PlatformAuthRole role, string expectedToken) => false;

        public void ClearAccount(PlatformAuthRole role, bool publishAuthorizationRefreshed = false)
        {
        }
    }
}
