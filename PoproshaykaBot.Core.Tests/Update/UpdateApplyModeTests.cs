using KeepShell.Updates;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Update;
using System.Net.Http;
using CoreUpdateApplyMode = PoproshaykaBot.Core.Settings.Update.UpdateApplyMode;
using CoreUpdateSettings = PoproshaykaBot.Core.Settings.Update.UpdateSettings;

namespace PoproshaykaBot.Core.Tests.Update;

[TestFixture]
public sealed class UpdateApplyModeTests
{
    private const string PreparedVersion = "3.1.0.7";
    private const string ValidSha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private string _root = null!;
    private string _staging = null!;
    private string _executable = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "poproshayka-apply-mode-" + Guid.NewGuid().ToString("N"));
        _staging = Path.Combine(_root, "update");
        _executable = Path.Combine(_root, "Bot.exe");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_staging);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static string AssetName(string version, bool selfContained = true) =>
        selfContained
            ? $"PoproshaykaBot-v{version}-x64-portable.zip"
            : $"PoproshaykaBot-v{version}-x64.zip";

    private static UpdateCandidate Candidate(string version = PreparedVersion)
    {
        var name = AssetName(version);
        return new(
            Version.Parse(version),
            "v" + version,
            new(UpdateKind.SingleFile, "x64", true, null, name, ValidSha256, 100),
            "https://example/download/" + name,
            "https://example/notes",
            string.Empty);
    }

    private static IReleaseSource ReleaseSourceFor(string version = PreparedVersion, bool selfContained = true, Action? onGetLatest = null)
    {
        var asset = AssetName(version, selfContained);
        var source = Substitute.For<IReleaseSource>();
        List<ReleaseAsset> assets =
        [
            new(asset, "https://example/download/" + asset, 100),
            new("SHA256SUMS-x64.txt", "https://example/download/SHA256SUMS-x64.txt", 100),
        ];
        if (!selfContained)
        {
            assets.Add(new("RUNTIME-x64.txt", "https://example/download/RUNTIME-x64.txt", 16));
        }

        source.GetLatestReleaseAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                onGetLatest?.Invoke();
                return new ReleaseInfo("v" + version, "https://example/notes", "body", false, false, assets);
            });
        source.DownloadTextAsync("https://example/download/SHA256SUMS-x64.txt", Arg.Any<CancellationToken>())
            .Returns($"{ValidSha256}  {asset}");
        if (!selfContained)
        {
            source.DownloadTextAsync("https://example/download/RUNTIME-x64.txt", Arg.Any<CancellationToken>())
                .Returns("net10.0-windows");
        }

        return source;
    }

    private PendingUpdate WritePending(string version = PreparedVersion, string? stagedPath = null, string? targetPath = null)
    {
        var staged = stagedPath ?? Path.Combine(_staging, $"PoproshaykaBot-v{version}-x64-portable.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
        File.WriteAllText(staged, "new");

        var pending = new PendingUpdate(version, staged, targetPath ?? _executable, ValidSha256);
        PendingUpdateStore.Save(_staging, pending, NullLogger.Instance);
        return pending;
    }

    private UpdateStore Store(CoreUpdateApplyMode mode, bool allowFrameworkDependent = false, string? skippedVersion = null)
    {
        var store = new UpdateStore(null, Path.Combine(_root, "update.json"));
        store.Save(new()
        {
            ApplyMode = mode,
            AllowFrameworkDependentUpdate = allowFrameworkDependent,
            SkippedVersion = skippedVersion,
        });

        return store;
    }

    private static void SetStored(UpdateStore store, Action<CoreUpdateSettings> change)
    {
        var settings = store.Load();
        change(settings);
        store.Save(settings);
    }

    private IUpdateEnvironment CreateEnvironment(UpdateKind kind = UpdateKind.SingleFile, bool selfContained = true)
    {
        return new UpdateEnvironment(
            kind,
            selfContained,
            Version.Parse("3.1.0.6"),
            10,
            _executable,
            "x64");
    }

    private UpdateCoordinator Coordinator(
        UpdateStore store,
        IUpdateInstaller installer,
        UpdateKind kind = UpdateKind.SingleFile,
        bool selfContained = true,
        IReleaseSource? releaseSource = null)
    {
        var environment = CreateEnvironment(kind, selfContained);
        var checker = new UpdateChecker(
            releaseSource ?? Substitute.For<IReleaseSource>(),
            environment,
            PoproshaykaBot.Core.Update.UpdateServiceCollectionExtensions.CreateCheckOptions(),
            NullLogger<UpdateChecker>.Instance);

        return new(checker, installer, new UpdateStoreSettingsAdapter(store), environment, TimeProvider.System, NullLogger<UpdateCoordinator>.Instance);
    }

    [TestCase(CoreUpdateApplyMode.SilentOnExit, UpdateKind.SingleFile, true, false, true)]
    [TestCase(CoreUpdateApplyMode.SilentOnExit, UpdateKind.SingleFile, false, true, true)]
    [TestCase(CoreUpdateApplyMode.SilentOnExit, UpdateKind.SingleFile, false, false, false)]
    [TestCase(CoreUpdateApplyMode.SilentOnExit, UpdateKind.None, false, true, false)]
    [TestCase(CoreUpdateApplyMode.NotifyAndConfirm, UpdateKind.SingleFile, true, false, false)]
    [TestCase(CoreUpdateApplyMode.NotifyAndConfirm, UpdateKind.SingleFile, false, true, false)]
    public async Task CheckAsync_ManualFalse_DownloadsOnlyInAutomaticModeOnUpdatableBuild(
        CoreUpdateApplyMode mode,
        UpdateKind kind,
        bool selfContained,
        bool allowFrameworkDependent,
        bool expectedPrepared)
    {
        var installer = Substitute.For<IUpdateInstaller>();
        installer.PrepareAsync(Arg.Any<UpdateCandidate>(), Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(WritePending()));
        var source = ReleaseSourceFor(PreparedVersion, selfContained);
        var coordinator = Coordinator(Store(mode, allowFrameworkDependent), installer, kind, selfContained, source);

        var result = await coordinator.CheckAsync(manual: false, CancellationToken.None);

        Assert.That(coordinator.Pending is not null, Is.EqualTo(expectedPrepared));
        if (mode == CoreUpdateApplyMode.NotifyAndConfirm && kind == UpdateKind.SingleFile && selfContained)
        {
            Assert.That(result, Is.True);
            Assert.That(coordinator.Stage, Is.EqualTo(UpdateStage.Available));
        }

        await installer.Received(expectedPrepared ? 1 : 0)
            .PrepareAsync(Arg.Any<UpdateCandidate>(), Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DownloadAsync_DoesNotDownloadAgain_WhenSameVersionAlreadyPrepared()
    {
        var pending = WritePending();
        var installer = Substitute.For<IUpdateInstaller>();
        installer.ReadPending().Returns(pending);
        var coordinator = Coordinator(Store(CoreUpdateApplyMode.NotifyAndConfirm), installer, releaseSource: ReleaseSourceFor());

        await coordinator.CheckAsync(manual: true, CancellationToken.None);
        var prepared = await coordinator.DownloadAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(prepared, Is.True);
            Assert.That(coordinator.Stage, Is.EqualTo(UpdateStage.ReadyToApply));
            Assert.That(File.Exists(Path.Combine(_staging, PendingUpdateStore.FileName)), Is.True);
        }

        await installer.DidNotReceiveWithAnyArgs().PrepareAsync(default!, default, default);
    }

    [Test]
    public async Task CheckAsync_ManualFalse_SetsFailedStage_AndSwallowsInstallerFailure()
    {
        var installer = Substitute.For<IUpdateInstaller>();
        installer.PrepareAsync(Arg.Any<UpdateCandidate>(), Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PendingUpdate>(new UpdateException("Нет прав на запись")));
        var coordinator = Coordinator(Store(CoreUpdateApplyMode.SilentOnExit), installer, releaseSource: ReleaseSourceFor());

        var result = await coordinator.CheckAsync(manual: false, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(coordinator.Stage, Is.EqualTo(UpdateStage.Failed));
            Assert.That(coordinator.Error, Is.EqualTo("Нет прав на запись"));
            Assert.That(coordinator.Pending, Is.Null);
        }
    }

    [TestCase(true, false, true)]
    [TestCase(false, true, true)]
    [TestCase(true, false, false)]
    public async Task CheckAsync_ManualFalse_DiscardsDownload_WhenSkippedOrModeTurnedOffDuringDownload(bool skip, bool turnModeOff, bool deleted)
    {
        var installer = Substitute.For<IUpdateInstaller>();
        installer.ReadPending().Returns(_ => PendingUpdateStore.Read(_staging, NullLogger.Instance));
        var environment = CreateEnvironment();
        var realInstaller = new SingleFileSwapInstaller(new UpdateDownloader(Substitute.For<IReleaseSource>()), environment);
        installer.DiscardPending(Arg.Any<Version>()).Returns(call => realInstaller.DiscardPending(call.Arg<Version>()));

        var store = Store(CoreUpdateApplyMode.SilentOnExit);
        FileStream? lockStream = null;

        installer.PrepareAsync(Arg.Any<UpdateCandidate>(), Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var pending = WritePending();
                if (!deleted)
                {
                    lockStream = new(Path.Combine(_staging, PendingUpdateStore.FileName), FileMode.Open, FileAccess.Read, FileShare.Read);
                }

                SetStored(store, settings =>
                {
                    settings.SkippedVersion = skip ? PreparedVersion : null;
                    settings.ApplyMode = turnModeOff ? CoreUpdateApplyMode.NotifyAndConfirm : CoreUpdateApplyMode.SilentOnExit;
                });
                return Task.FromResult(pending);
            });

        try
        {
            var coordinator = Coordinator(store, installer, releaseSource: ReleaseSourceFor());

            await coordinator.CheckAsync(manual: false, CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(coordinator.Pending, Is.Null);
                Assert.That(coordinator.Stage, Is.Not.EqualTo(UpdateStage.ReadyToApply));
                Assert.That(File.Exists(Path.Combine(_staging, PendingUpdateStore.FileName)), Is.EqualTo(!deleted));
            }
        }
        finally
        {
            if (lockStream is not null)
            {
                await lockStream.DisposeAsync();
            }
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task DiscardPending_ReturnsTrueOnlyWhenPendingFileIsGone(bool deleted)
    {
        WritePending();
        var environment = CreateEnvironment();
        var installer = new SingleFileSwapInstaller(new UpdateDownloader(Substitute.For<IReleaseSource>()), environment);
        var coordinator = Coordinator(Store(CoreUpdateApplyMode.NotifyAndConfirm), installer);
        coordinator.RestorePending();

        FileStream? lockStream = deleted
            ? null
            : new(Path.Combine(_staging, PendingUpdateStore.FileName), FileMode.Open, FileAccess.Read, FileShare.Read);

        try
        {
            var discarded = coordinator.DiscardPending();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(discarded, Is.EqualTo(deleted));
                Assert.That(File.Exists(Path.Combine(_staging, PendingUpdateStore.FileName)), Is.EqualTo(!deleted));
            }
        }
        finally
        {
            if (lockStream is not null)
            {
                await lockStream.DisposeAsync();
            }
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void PrepareAsync_RemovesPartialDownload_OnCancellationOrFailure(bool cancelled)
    {
        var environment = CreateEnvironment();
        var source = Substitute.For<IReleaseSource>();
        source.DownloadFileAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                File.WriteAllText(call.ArgAt<string>(1), "half");
                return cancelled
                    ? Task.FromCanceled(new(true))
                    : Task.FromException(new HttpRequestException("обрыв"));
            });
        var installer = new SingleFileSwapInstaller(new UpdateDownloader(source), environment);

        Assert.CatchAsync(async () => _ = await installer.PrepareAsync(Candidate(), null, CancellationToken.None));

        Assert.That(Directory.GetFiles(_staging, "*.part"), Is.Empty);
    }

    [Test]
    public async Task DownloadAsync_DiscardsAlreadyPreparedVersion_WhenItIsSkipped()
    {
        WritePending();
        var environment = CreateEnvironment();
        var installer = Substitute.For<IUpdateInstaller>();
        installer.ReadPending().Returns(_ => PendingUpdateStore.Read(_staging, NullLogger.Instance));
        var realInstaller = new SingleFileSwapInstaller(new UpdateDownloader(Substitute.For<IReleaseSource>()), environment);
        installer.DiscardPending(Arg.Any<Version>()).Returns(call => realInstaller.DiscardPending(call.Arg<Version>()));

        var store = Store(CoreUpdateApplyMode.NotifyAndConfirm);
        var coordinator = Coordinator(store, installer, releaseSource: ReleaseSourceFor());

        await coordinator.CheckAsync(manual: true, CancellationToken.None);
        SetStored(store, settings => settings.SkippedVersion = PreparedVersion);

        var prepared = await coordinator.DownloadAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(prepared, Is.False);
            Assert.That(File.Exists(Path.Combine(_staging, PendingUpdateStore.FileName)), Is.False);
        }
    }

    [Test]
    public async Task CheckAsync_KeepsSkipMadeDuringCheck_AndDropsSkippedCandidate()
    {
        var store = Store(CoreUpdateApplyMode.SilentOnExit);
        var source = ReleaseSourceFor(PreparedVersion, selfContained: true,
            onGetLatest: () => SetStored(store, settings => settings.SkippedVersion = PreparedVersion));

        var coordinator = Coordinator(store, Substitute.For<IUpdateInstaller>(), releaseSource: source);

        var found = await coordinator.CheckAsync(manual: false, CancellationToken.None);

        var stored = store.Load();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.False);
            Assert.That(coordinator.Candidate, Is.Null);
            Assert.That(stored.SkippedVersion, Is.EqualTo(PreparedVersion));
            Assert.That(stored.LastCheckUtc, Is.Not.Null);
        }
    }

    [Test]
    public async Task DownloadAsync_ReturnsFalse_OnUnsupportedBuild()
    {
        var installer = Substitute.For<IUpdateInstaller>();
        var coordinator = Coordinator(Store(CoreUpdateApplyMode.SilentOnExit), installer, UpdateKind.None);

        var prepared = await coordinator.DownloadAsync(CancellationToken.None);

        Assert.That(prepared, Is.False);
        await installer.DidNotReceiveWithAnyArgs().PrepareAsync(default!, default, default);
    }

    [TestCase(PreparedVersion, false)]
    [TestCase("3.1.0.8", true)]
    public void DiscardPending_DiscardsPreparedUpdate_OnlyOfNamedVersion(string targetVersion, bool pendingRemains)
    {
        WritePending();
        var environment = CreateEnvironment();
        var installer = new SingleFileSwapInstaller(new UpdateDownloader(Substitute.For<IReleaseSource>()), environment);
        var coordinator = Coordinator(Store(CoreUpdateApplyMode.SilentOnExit), installer);
        coordinator.RestorePending();

        coordinator.DiscardPending(Version.Parse(targetVersion));

        Assert.That(File.Exists(Path.Combine(_staging, PendingUpdateStore.FileName)), Is.EqualTo(pendingRemains));
    }

    [Test]
    public async Task Skip_DuringDownload_LeavesDiscardToTheDownload()
    {
        var installer = Substitute.For<IUpdateInstaller>();
        var environment = CreateEnvironment();
        var realInstaller = new SingleFileSwapInstaller(new UpdateDownloader(Substitute.For<IReleaseSource>()), environment);
        installer.DiscardPending(Arg.Any<Version>()).Returns(call => realInstaller.DiscardPending(call.Arg<Version>()));
        installer.ReadPending().Returns(_ => PendingUpdateStore.Read(_staging, NullLogger.Instance));

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var download = new TaskCompletionSource<PendingUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        installer.PrepareAsync(Arg.Any<UpdateCandidate>(), Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                started.TrySetResult();
                return download.Task;
            });

        var coordinator = Coordinator(Store(CoreUpdateApplyMode.SilentOnExit), installer, releaseSource: ReleaseSourceFor());

        var silent = coordinator.CheckAsync(manual: false, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var previous = WritePending("3.1.0.5");

        coordinator.Skip();
        Assert.That(File.Exists(previous.StagedPath), Is.True);

        var downloaded = WritePending();
        download.SetResult(downloaded);

        await silent;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(Path.Combine(_staging, PendingUpdateStore.FileName)), Is.False);
            Assert.That(File.Exists(downloaded.StagedPath), Is.False);
        }
    }

    [Test]
    public async Task DiscardPending_DuringAnotherVersionsFailedDownload_DiscardsPreviousPending()
    {
        var installer = Substitute.For<IUpdateInstaller>();
        var environment = CreateEnvironment();
        var realInstaller = new SingleFileSwapInstaller(new UpdateDownloader(Substitute.For<IReleaseSource>()), environment);
        installer.DiscardPending(Arg.Any<Version>()).Returns(call => realInstaller.DiscardPending(call.Arg<Version>()));
        installer.ReadPending().Returns(_ => PendingUpdateStore.Read(_staging, NullLogger.Instance));

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var download = new TaskCompletionSource<PendingUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        installer.PrepareAsync(Arg.Any<UpdateCandidate>(), Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                started.TrySetResult();
                return download.Task;
            });

        WritePending("3.1.0.5");
        var store = Store(CoreUpdateApplyMode.NotifyAndConfirm);
        var coordinator = Coordinator(store, installer, releaseSource: ReleaseSourceFor("3.1.0.7"));
        coordinator.RestorePending();
        await coordinator.CheckAsync(manual: true, CancellationToken.None);

        var activeDownload = coordinator.DownloadAsync(CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var discardedImmediately = coordinator.DiscardPending();

        Assert.That(discardedImmediately, Is.False);
        Assert.That(File.Exists(Path.Combine(_staging, PendingUpdateStore.FileName)), Is.True);

        download.SetException(new UpdateException("Обрыв сети"));
        Assert.That(await activeDownload, Is.False);

        Assert.That(File.Exists(Path.Combine(_staging, PendingUpdateStore.FileName)), Is.False);
    }

    [Test]
    public async Task CancelPendingDownloadsAsync_CancelsAndAwaitsActiveManualDownload()
    {
        var installer = Substitute.For<IUpdateInstaller>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCleanupFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        installer.PrepareAsync(Arg.Any<UpdateCandidate>(), Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var token = call.ArgAt<CancellationToken>(2);
                started.TrySetResult();

                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                catch (OperationCanceledException)
                {
                    cancelled.TrySetResult();
                    await allowCleanupFinish.Task;
                    throw;
                }

                return WritePending();
            });

        var coordinator = Coordinator(Store(CoreUpdateApplyMode.NotifyAndConfirm), installer, releaseSource: ReleaseSourceFor());
        await coordinator.CheckAsync(manual: true, CancellationToken.None);

        var manualDownload = coordinator.DownloadAsync(CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var stopTask = coordinator.CancelPendingDownloadsAsync(CancellationToken.None);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(stopTask.IsCompleted, Is.False, "Остановка должна дожидаться завершения отмены и очистки .part до ApplyPendingUpdate");

        allowCleanupFinish.SetResult();
        await stopTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(await manualDownload, Is.False);
    }

    [Test]
    public void DiscardPending_RemovesOnlyTheNamedVersion_AndNothingOutsideStaging()
    {
        var outside = Path.Combine(_root, "outside.exe");
        File.WriteAllText(outside, "keep");

        var environment = CreateEnvironment();
        var installer = new SingleFileSwapInstaller(new UpdateDownloader(Substitute.For<IReleaseSource>()), environment);
        var coordinator = Coordinator(Store(CoreUpdateApplyMode.NotifyAndConfirm), installer);

        WritePending(PreparedVersion, stagedPath: outside);
        coordinator.DiscardPending(Version.Parse(PreparedVersion));
        Assert.That(File.Exists(outside), Is.True);
        Assert.That(File.Exists(Path.Combine(_staging, PendingUpdateStore.FileName)), Is.False);

        var store = Store(CoreUpdateApplyMode.SilentOnExit);
        var coordinator2 = Coordinator(store, installer);
        var staged = Path.Combine(_staging, "new.exe");
        WritePending(PreparedVersion, stagedPath: staged);

        coordinator2.DiscardPending(Version.Parse("3.1.0.8"));
        var pendingPath = Path.Combine(_staging, PendingUpdateStore.FileName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(pendingPath), Is.True);
            Assert.That(coordinator2.DiscardPending(), Is.True);
            Assert.That(File.Exists(outside), Is.True);
            Assert.That(File.Exists(staged), Is.False);
            Assert.That(File.Exists(pendingPath), Is.False);
        }
    }

    [TestCase("3.1.0.6")]
    [TestCase("3.1.0.5")]
    [TestCase("не версия")]
    public void TryApplyPending_DiscardsPendingNotNewerThanCurrent(string pendingVersion)
    {
        File.WriteAllText(_executable, "current");
        WritePending(pendingVersion, stagedPath: Path.Combine(_staging, "Bot.new.exe"));
        var pendingPath = Path.Combine(_staging, PendingUpdateStore.FileName);
        var relauncher = Substitute.For<IUpdateRelauncher>();

        var applied = UpdateApplier.TryApplyPending(_staging, _executable, Version.Parse("3.1.0.6"), relauncher, NullLogger.Instance);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied, Is.False);
            Assert.That(File.ReadAllText(_executable), Is.EqualTo("current"));
            Assert.That(File.Exists(_executable + ".old"), Is.False);
            Assert.That(File.Exists(pendingPath), Is.False);
            relauncher.DidNotReceiveWithAnyArgs().Relaunch(default!, default!);
        }
    }

    [Test]
    public async Task BackgroundLoop_RestoresPendingAndDelegatesCheckToCoordinator()
    {
        var checkedEvent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = Substitute.For<IUpdateCoordinator>();
        coordinator.IsUpdatable.Returns(true);
        coordinator.CheckAsync(false, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                checkedEvent.TrySetResult();
                return true;
            });

        using var service = new UpdateBackgroundService(
            coordinator,
            CreateEnvironment(),
            new UpdateStoreSettingsAdapter(Store(CoreUpdateApplyMode.SilentOnExit)),
            new ShortDelaysOnlyTimeProvider(),
            NullLogger<UpdateBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await checkedEvent.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            coordinator.Received(1).RestorePending();
            await coordinator.Received(1).CheckAsync(false, Arg.Any<CancellationToken>());
            await coordinator.Received(1).CancelPendingDownloadsAsync(Arg.Any<CancellationToken>());
        }
    }

    private sealed class ShortDelaysOnlyTimeProvider : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime < TimeSpan.FromHours(1))
            {
                ThreadPool.QueueUserWorkItem(_ => callback(state));
            }

            return new IdleTimer();
        }

        private sealed class IdleTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
