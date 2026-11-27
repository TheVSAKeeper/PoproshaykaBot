using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Services.Platform;
using KeepShell.Updates;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Update;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using System.IO;

namespace PoproshaykaBot.Wpf.Tests.Shell;

[TestFixture]
public sealed class ManualUpdateCancellationTests
{
    private string _directory = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"poproshayka-update-cancel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static UpdateCandidate CreateCandidate(string version = "3.1.0.16")
    {
        var name = $"PoproshaykaBot-v{version}-x64-portable.exe";
        return new(
            Version.Parse(version),
            "v" + version,
            new(KeepShell.Updates.UpdateKind.SingleFile, "x64", true, null, name, null, 100),
            "https://example/" + name,
            "https://example/notes",
            string.Empty);
    }

    private static IUpdateEnvironment CreateEnvironment()
    {
        return new UpdateEnvironment(
            UpdateKind.SingleFile,
            selfContained: true,
            currentVersion: Version.Parse("3.1.0.15"),
            runtimeMajor: 10,
            executablePath: "Bot.exe",
            architecture: "x64");
    }

    [Test]
    public async Task UpdateBanner_CancelInstallAsync_CancelsAndAwaitsDownload_WithoutErrorOrClose()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observedToken = default;

        var coordinator = new FakeCoordinator(CreateCandidate(), async (coord, token) =>
        {
            observedToken = token;
            coord.SetStage(UpdateStage.Downloading);
            started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return true;
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult();
                await finishCleanup.Task;
                coord.SetStage(UpdateStage.Available);
                return false;
            }
        });

        var dialogs = new RecordingDialogService();
        var mainWindow = new RecordingMainWindow();

        using var banner = new UpdateBannerViewModel(
            coordinator,
            CreateEnvironment(),
            dialogs,
            NullLogger<UpdateBannerViewModel>.Instance,
            new ManualUiDispatcher(),
            new NoopShellLauncher(),
            mainWindow);

        var downloadTask = banner.DownloadCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(banner.IsDownloading, Is.True);

        var cancelTask = banner.CancelInstallAsync();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(cancelTask.IsCompleted, Is.False);

        finishCleanup.SetResult();
        await cancelTask.WaitAsync(TimeSpan.FromSeconds(5));
        await downloadTask.WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(observedToken.CanBeCanceled, Is.True);
            Assert.That(observedToken.IsCancellationRequested, Is.True);
            Assert.That(banner.IsDownloading, Is.False);
            Assert.That(banner.IsFailed, Is.False);
            Assert.That(dialogs.Errors, Is.Empty);
            Assert.That(mainWindow.CloseRequests, Is.Zero);
        }
    }

    [Test]
    public async Task UpdateBanner_HttpTimeout_StillReportsError()
    {
        var coordinator = new FakeCoordinator(CreateCandidate(), (coord, _) =>
        {
            coord.SetFailed("Превышено время ожидания");
            return Task.FromResult(false);
        });

        var dialogs = new RecordingDialogService();
        var mainWindow = new RecordingMainWindow();

        using var banner = new UpdateBannerViewModel(
            coordinator,
            CreateEnvironment(),
            dialogs,
            NullLogger<UpdateBannerViewModel>.Instance,
            new ManualUiDispatcher(),
            new NoopShellLauncher(),
            mainWindow);

        await banner.DownloadCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(banner.IsDownloading, Is.False);
            Assert.That(banner.IsFailed, Is.True);
            Assert.That(banner.IsVisible, Is.True);
            Assert.That(banner.Message, Does.Contain("Превышено время ожидания"));
            Assert.That(mainWindow.CloseRequests, Is.Zero);
        }
    }

    [Test]
    public async Task UpdateSettingsSection_CancelInstallAsync_CancelsAndAwaitsDownload_WithoutDialogsOrClose()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observedToken = default;

        var coordinator = new FakeCoordinator(CreateCandidate(), async (coord, token) =>
        {
            observedToken = token;
            coord.SetStage(UpdateStage.Downloading);
            started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return true;
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult();
                await finishCleanup.Task;
                coord.SetStage(UpdateStage.Available);
                return false;
            }
        });

        var store = new UpdateStore(null, Path.Combine(_directory, "update.json"));
        var environment = CreateEnvironment();
        var options = PoproshaykaBot.Core.Update.UpdateServiceCollectionExtensions.CreateOptions("owner/repo");
        var dialogs = new RecordingDialogService();
        var mainWindow = new RecordingMainWindow();

        using var section = new UpdateSettingsSectionViewModel(
            new UpdateStoreSettingsAdapter(store),
            coordinator,
            options,
            environment,
            dialogs,
            new ManualUiDispatcher(),
            mainWindow);

        var installTask = section.InstallUpdateCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var cancelTask = section.CancelInstallAsync();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(cancelTask.IsCompleted, Is.False);

        finishCleanup.SetResult();
        await cancelTask.WaitAsync(TimeSpan.FromSeconds(5));
        await installTask.WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(observedToken.CanBeCanceled, Is.True);
            Assert.That(observedToken.IsCancellationRequested, Is.True);
            Assert.That(section.IsBusy, Is.False);
            Assert.That(dialogs.Errors, Is.Empty);
            Assert.That(dialogs.Infos, Is.Empty);
            Assert.That(mainWindow.CloseRequests, Is.Zero);
        }
    }

    [Test]
    public async Task UpdateSettingsSection_ResetInstallState_ClearsBusyAfterCancelledClose()
    {
        var candidate = CreateCandidate();
        var coordinator = new FakeCoordinator(candidate, (coord, _) =>
        {
            coord.SetReady(new PendingUpdate(candidate.Version.ToString(), Path.Combine(_directory, "bot.exe"), Path.Combine(_directory, "target.exe"), "sha"));
            return Task.FromResult(true);
        });
        var store = new UpdateStore(null, Path.Combine(_directory, "update.json"));
        var environment = CreateEnvironment();
        var options = PoproshaykaBot.Core.Update.UpdateServiceCollectionExtensions.CreateOptions("owner/repo");
        var dialogs = new RecordingDialogService();
        var mainWindow = new RecordingMainWindow();

        using var section = new UpdateSettingsSectionViewModel(
            new UpdateStoreSettingsAdapter(store),
            coordinator,
            options,
            environment,
            dialogs,
            new ManualUiDispatcher(),
            mainWindow);

        await section.InstallUpdateCommand.ExecuteAsync(null);

        Assert.That(section.IsBusy, Is.True);

        section.ResetInstallState();

        Assert.That(section.IsBusy, Is.False);
    }

    private sealed class FakeCoordinator(
        UpdateCandidate candidate,
        Func<FakeCoordinator, CancellationToken, Task<bool>> onDownload) : KeepShell.Updates.IUpdateCoordinator
    {
        private Task<bool>? _activeDownload;
        private CancellationTokenSource? _activeCts;

        public event EventHandler? Changed;

        public bool IsUpdatable => true;

        public UpdateStage Stage { get; private set; } = UpdateStage.Available;

        public UpdateCandidate? Candidate => candidate;

        public PendingUpdate? Pending { get; private set; }

        public int Progress => 0;

        public string? Error { get; private set; }

        public void SetStage(UpdateStage stage)
        {
            Stage = stage;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void SetReady(PendingUpdate pending)
        {
            Pending = pending;
            Error = null;
            Stage = UpdateStage.ReadyToApply;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void SetFailed(string error)
        {
            Error = error;
            Stage = UpdateStage.Failed;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public bool RestorePending() => false;

        public Task<bool> CheckAsync(bool manual, CancellationToken cancellationToken) => Task.FromResult(true);

        public async Task<bool> DownloadAsync(CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeCts = linked;
            var task = onDownload(this, linked.Token);
            _activeDownload = task;

            try
            {
                return await task;
            }
            finally
            {
                _activeDownload = null;
                _activeCts = null;
            }
        }

        public async Task CancelPendingDownloadsAsync(CancellationToken cancellationToken = default)
        {
            if (_activeCts is { } cts)
            {
                await cts.CancelAsync();
            }

            if (_activeDownload is { } task)
            {
                try
                {
                    await task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        public bool DiscardPending(Version? version = null) => false;

        public void Skip()
        {
        }

        public void Dismiss()
        {
        }
    }

    private sealed class NoopShellLauncher : IShellLauncher
    {
        public bool Open(string pathOrUrl) => true;

        public bool Reveal(string path) => true;

        public bool Start(string executable, params string[] arguments) => true;
    }

    private sealed class RecordingDialogService : IDialogService
    {
        public List<string> Infos { get; } = [];

        public List<string> Errors { get; } = [];

        public Task<bool> ShowAsync(IDialogViewModel viewModel) => Task.FromResult(false);

        public Task<bool> ReplaceAsync(IDialogViewModel viewModel) => Task.FromResult(false);

        public bool Confirm(string title, string message, bool defaultYes = false) => true;

        public bool ConfirmWarning(string title, string message, bool defaultYes = false) => true;

        public void Info(string title, string message) => Infos.Add(message);

        public void Warning(string title, string message) { }

        public void Error(string title, string message) => Errors.Add(message);
    }

    private sealed class RecordingMainWindow : IMainWindowController
    {
        public int CloseRequests { get; private set; }

        public void RequestClose() => CloseRequests++;
    }
}
