using KeepShell.Services;
using KeepShell.Services.Modal;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Infrastructure.Events;
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

    private static UpdateCandidate Candidate(string version = "3.1.0.16")
    {
        var name = $"PoproshaykaBot-v{version}-x64-portable.exe";
        return new(Version.Parse(version), "v" + version, new(name, "https://example/" + name, 100, "application/octet-stream"), "https://example/notes");
    }

    [Test]
    public async Task UpdateBanner_CancelInstallAsync_CancelsAndAwaitsDownload_WithoutErrorOrClose()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observedToken = default;

        var coordinator = new FakeCoordinator(Candidate(), async (_, _, token) =>
        {
            observedToken = token;
            started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult();
                await finishCleanup.Task;
                throw;
            }
        });

        var dialogs = new RecordingDialogService();
        var mainWindow = new RecordingMainWindow();
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);

        using var banner = new UpdateBannerViewModel(
            coordinator,
            bus,
            dialogs,
            NullLogger<UpdateBannerViewModel>.Instance,
            new ManualUiDispatcher(),
            mainWindow);

        var installTask = banner.InstallCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        banner.ResetInstallState();
        Assert.That(banner.IsBusy, Is.True, "Сброс при отмене закрытия не должен разблокировать идущую загрузку");

        var cancelTask = banner.CancelInstallAsync();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(cancelTask.IsCompleted, Is.False);

        finishCleanup.SetResult();
        await cancelTask.WaitAsync(TimeSpan.FromSeconds(5));
        await installTask.WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(observedToken.CanBeCanceled, Is.True);
            Assert.That(observedToken.IsCancellationRequested, Is.True);
            Assert.That(banner.IsBusy, Is.False);
            Assert.That(dialogs.Errors, Is.Empty);
            Assert.That(mainWindow.CloseRequests, Is.Zero);
        }
    }

    [Test]
    public async Task UpdateBanner_HttpTimeout_StillReportsError()
    {
        var coordinator = new FakeCoordinator(Candidate(), (_, _, _) =>
            Task.FromException(new TaskCanceledException("Превышено время ожидания")));

        var dialogs = new RecordingDialogService();
        var mainWindow = new RecordingMainWindow();
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);

        using var banner = new UpdateBannerViewModel(
            coordinator,
            bus,
            dialogs,
            NullLogger<UpdateBannerViewModel>.Instance,
            new ManualUiDispatcher(),
            mainWindow);

        await banner.InstallCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(banner.IsBusy, Is.False);
            Assert.That(dialogs.Errors, Has.Count.EqualTo(1));
            Assert.That(dialogs.Errors[0], Does.Contain("Превышено время ожидания"));
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

        var coordinator = new FakeCoordinator(Candidate(), async (_, _, token) =>
        {
            observedToken = token;
            started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult();
                await finishCleanup.Task;
                throw;
            }
        });

        var store = new UpdateStore(null, Path.Combine(_directory, "update.json"));
        var dialogs = new RecordingDialogService();
        var mainWindow = new RecordingMainWindow();
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);

        using var section = new UpdateSettingsSectionViewModel(
            coordinator,
            store,
            bus,
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
        var coordinator = new FakeCoordinator(Candidate(), (_, _, _) => Task.CompletedTask);
        var store = new UpdateStore(null, Path.Combine(_directory, "update.json"));
        var dialogs = new RecordingDialogService();
        var mainWindow = new RecordingMainWindow();
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);

        using var section = new UpdateSettingsSectionViewModel(
            coordinator,
            store,
            bus,
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
        Func<UpdateCandidate, IProgress<int>?, CancellationToken, Task> onPrepare) : IUpdateCoordinator
    {
        public UpdateKind Kind => UpdateKind.Portable;

        public bool IsUpdatable => true;

        public string DefaultRepositorySlug => "owner/repo";

        public Version CurrentVersion { get; } = Version.Parse("3.1.0.15");

        public UpdateCandidate? LatestCandidate => candidate;

        public bool HasPreparedUpdate => false;

        public string? PreparedVersion => null;

        public Task<UpdateCandidate?> CheckNowAsync(CancellationToken cancellationToken) => Task.FromResult<UpdateCandidate?>(candidate);

        public Task PrepareAsync(UpdateCandidate updateCandidate, IProgress<int>? progress, CancellationToken cancellationToken) =>
            onPrepare(updateCandidate, progress, cancellationToken);

        public Task<bool> TryPrepareSilentlyAsync(UpdateCandidate updateCandidate, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> DiscardPreparedUpdateAsync(CancellationToken cancellationToken) => Task.FromResult(false);

        public Task CancelPendingDownloadsAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void SkipVersion(string version)
        {
        }
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
