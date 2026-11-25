using KeepShell.Updates;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Update;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Update;
using CoreUpdateApplyMode = PoproshaykaBot.Core.Settings.Update.UpdateApplyMode;
using CoreUpdateSettings = PoproshaykaBot.Core.Settings.Update.UpdateSettings;
using UpdateCoordinator = PoproshaykaBot.Core.Update.UpdateCoordinator;
using UpdateKind = PoproshaykaBot.Core.Update.UpdateKind;

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

    private static string AssetName(string version) => $"PoproshaykaBot-v{version}-x64-portable.exe";

    private static UpdateCandidate Candidate(string version = PreparedVersion)
    {
        var name = AssetName(version);
        return new(
            Version.Parse(version),
            "v" + version,
            new(KeepShell.Updates.UpdateKind.SingleFile, "x64", true, null, name, ValidSha256, 100),
            "https://example/download/" + name,
            "https://example/notes",
            string.Empty);
    }

    private PendingUpdate WritePending(string version = PreparedVersion, string? stagedPath = null, string? targetPath = null)
    {
        var staged = stagedPath ?? Path.Combine(_staging, AssetName(version));
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

    private PoproshaykaBot.Core.Update.IUpdateEnvironment CreateEnvironment(UpdateKind kind)
    {
        var environment = Substitute.For<PoproshaykaBot.Core.Update.IUpdateEnvironment>();
        environment.Kind.Returns(kind);
        ((KeepShell.Updates.IUpdateEnvironment)environment).Kind.Returns(kind == UpdateKind.Unsupported
            ? KeepShell.Updates.UpdateKind.None
            : KeepShell.Updates.UpdateKind.SingleFile);
        environment.SelfContained.Returns(kind == UpdateKind.Portable);
        environment.CurrentVersion.Returns(Version.Parse("3.1.0.6"));
        environment.Architecture.Returns("x64");
        environment.RuntimeMajor.Returns(10);
        environment.ExecutablePath.Returns(_executable);
        environment.StagingDirectory.Returns(_staging);
        environment.RepositorySlug.Returns("owner/repo");
        return environment;
    }

    private UpdateCoordinator Coordinator(
        UpdateStore store,
        IUpdateInstaller installer,
        UpdateKind kind,
        IEventBus? eventBus = null,
        IReleaseSource? releaseSource = null)
    {
        var environment = CreateEnvironment(kind);
        var checker = new UpdateChecker(
            releaseSource ?? Substitute.For<IReleaseSource>(),
            environment,
            PoproshaykaBot.Core.Update.UpdateServiceCollectionExtensions.CreateCheckOptions(),
            NullLogger<UpdateChecker>.Instance);

        return new(checker, installer, store, eventBus ?? Substitute.For<IEventBus>(), environment, NullLogger<UpdateCoordinator>.Instance);
    }

    [TestCase(CoreUpdateApplyMode.SilentOnExit, UpdateKind.Portable, false, true)]
    [TestCase(CoreUpdateApplyMode.SilentOnExit, UpdateKind.FrameworkDependent, true, true)]
    [TestCase(CoreUpdateApplyMode.SilentOnExit, UpdateKind.FrameworkDependent, false, false)]
    [TestCase(CoreUpdateApplyMode.SilentOnExit, UpdateKind.Unsupported, true, false)]
    [TestCase(CoreUpdateApplyMode.NotifyAndConfirm, UpdateKind.Portable, false, false)]
    [TestCase(CoreUpdateApplyMode.NotifyAndConfirm, UpdateKind.FrameworkDependent, true, false)]
    public async Task TryPrepareSilentlyAsync_DownloadsOnlyInSilentModeOnUpdatableBuild(
        CoreUpdateApplyMode mode,
        UpdateKind kind,
        bool allowFrameworkDependent,
        bool expectedPrepared)
    {
        var installer = Substitute.For<IUpdateInstaller>();
        using var coordinator = Coordinator(Store(mode, allowFrameworkDependent), installer, kind);

        var prepared = await coordinator.TryPrepareSilentlyAsync(Candidate(), CancellationToken.None);

        Assert.That(prepared, Is.EqualTo(expectedPrepared));
        await installer.Received(expectedPrepared ? 1 : 0)
            .PrepareAsync(Arg.Any<UpdateCandidate>(), Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryPrepareSilentlyAsync_DoesNotDownloadAgain_WhenSameVersionAlreadyPrepared()
    {
        var pending = WritePending();
        var installer = Substitute.For<IUpdateInstaller>();
        installer.ReadPending().Returns(pending);
        using var coordinator = Coordinator(Store(CoreUpdateApplyMode.SilentOnExit), installer, UpdateKind.Portable);

        var prepared = await coordinator.TryPrepareSilentlyAsync(Candidate(), CancellationToken.None);

        Assert.That(prepared, Is.True);
        await installer.DidNotReceiveWithAnyArgs().PrepareAsync(default!, default, default);
        Assert.That(File.Exists(Path.Combine(_staging, PendingUpdateStore.FileName)), Is.True);
    }

    [Test]
    public async Task TryPrepareSilentlyAsync_PublishesPrepared_AndSwallowsInstallerFailure()
    {
        var installer = Substitute.For<IUpdateInstaller>();
        var eventBus = Substitute.For<IEventBus>();
        using var coordinator = Coordinator(Store(CoreUpdateApplyMode.SilentOnExit), installer, UpdateKind.Portable, eventBus);

        Assert.That(await coordinator.TryPrepareSilentlyAsync(Candidate(), CancellationToken.None), Is.True);
        await eventBus.Received(1).PublishAsync(Arg.Is<UpdatePrepared>(e => e.Version == PreparedVersion), Arg.Any<CancellationToken>());

        installer.PrepareAsync(Arg.Any<UpdateCandidate>(), Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PendingUpdate>(new UpdateException("Нет прав на запись")));

        Assert.That(await coordinator.TryPrepareSilentlyAsync(Candidate("3.1.0.8"), CancellationToken.None), Is.False);
    }

    [TestCase(true, false, true)]
    [TestCase(false, true, true)]
    [TestCase(true, false, false)]
    public async Task TryPrepareSilentlyAsync_DiscardsDownload_WhenSkippedOrModeTurnedOffDuringDownload(bool skip, bool turnModeOff, bool deleted)
    {
        var installer = Substitute.For<IUpdateInstaller>();
        installer.ReadPending().Returns(_ => PendingUpdateStore.Read(_staging, NullLogger.Instance));
        var eventBus = Substitute.For<IEventBus>();
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
            using var coordinator = Coordinator(store, installer, UpdateKind.Portable, eventBus);

            var prepared = await coordinator.TryPrepareSilentlyAsync(Candidate(), CancellationToken.None);

            Assert.That(prepared, Is.False);
            Assert.That(File.Exists(Path.Combine(_staging, PendingUpdateStore.FileName)), Is.EqualTo(!deleted));
            await eventBus.Received(deleted ? 1 : 0).PublishAsync(Arg.Any<UpdateDiscarded>(), Arg.Any<CancellationToken>());
            await eventBus.DidNotReceive().PublishAsync(Arg.Any<UpdatePrepared>(), Arg.Any<CancellationToken>());
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
    public async Task DiscardPreparedUpdateAsync_PublishesOnlyWhenPendingFileIsGone(bool deleted)
    {
        WritePending();
        var installer = Substitute.For<IUpdateInstaller>();
        installer.ReadPending().Returns(_ => PendingUpdateStore.Read(_staging, NullLogger.Instance));
        var eventBus = Substitute.For<IEventBus>();
        using var coordinator = Coordinator(Store(CoreUpdateApplyMode.NotifyAndConfirm), installer, UpdateKind.Portable, eventBus);

        FileStream? lockStream = deleted
            ? null
            : new(Path.Combine(_staging, PendingUpdateStore.FileName), FileMode.Open, FileAccess.Read, FileShare.Read);

        try
        {
            var discarded = await coordinator.DiscardPreparedUpdateAsync(CancellationToken.None);

            Assert.That(discarded, Is.EqualTo(deleted));
            await eventBus.Received(deleted ? 1 : 0).PublishAsync(Arg.Is<UpdateDiscarded>(e => e.Version == PreparedVersion), Arg.Any<CancellationToken>());
        }
        finally
        {
            if (lockStream is not null)
            {
                await lockStream.DisposeAsync();
            }
        }
    }

    [Test]
    public void DiscardPendingFiles_ReturnsFalse_WhenPendingFileCannotBeDeleted()
    {
        var pending = WritePending();
        var pendingPath = Path.Combine(_staging, PendingUpdateStore.FileName);

        bool discarded;

        using (new FileStream(pendingPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            discarded = UpdateCoordinator.DiscardPendingFiles(_staging, pending);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(discarded, Is.False);
            Assert.That(File.Exists(pendingPath), Is.True);
            Assert.That(File.Exists(pending.StagedPath), Is.True);
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void PrepareAsync_RemovesPartialDownload_OnCancellationOrFailure(bool cancelled)
    {
        var environment = CreateEnvironment(UpdateKind.Portable);
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
    public async Task TryPrepareSilentlyAsync_DiscardsAlreadyPreparedVersion_WhenItIsSkipped()
    {
        WritePending();
        var installer = Substitute.For<IUpdateInstaller>();
        installer.ReadPending().Returns(_ => PendingUpdateStore.Read(_staging, NullLogger.Instance));
        using var coordinator = Coordinator(Store(CoreUpdateApplyMode.SilentOnExit, skippedVersion: PreparedVersion), installer, UpdateKind.Portable);

        var prepared = await coordinator.TryPrepareSilentlyAsync(Candidate(), CancellationToken.None);

        Assert.That(prepared, Is.False);
        Assert.That(File.Exists(Path.Combine(_staging, PendingUpdateStore.FileName)), Is.False);
    }

    [Test]
    public async Task CheckNowAsync_KeepsSkipMadeDuringCheck_AndDropsSkippedCandidate()
    {
        var store = Store(CoreUpdateApplyMode.SilentOnExit);
        var source = Substitute.For<IReleaseSource>();
        source.GetLatestReleaseAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                SetStored(store, settings => settings.SkippedVersion = PreparedVersion);

                return new ReleaseInfo("v" + PreparedVersion, "https://example/notes", "body", false, false,
                [
                    new(AssetName(PreparedVersion), "https://example/download/" + AssetName(PreparedVersion), 100),
                    new("SHA256SUMS-x64.txt", "https://example/download/SHA256SUMS-x64.txt", 100),
                ]);
            });
        source.DownloadTextAsync("https://example/download/SHA256SUMS-x64.txt", Arg.Any<CancellationToken>())
            .Returns($"{ValidSha256}  {AssetName(PreparedVersion)}");

        using var coordinator = Coordinator(store, Substitute.For<IUpdateInstaller>(), UpdateKind.Portable, releaseSource: source);

        var candidate = await coordinator.CheckNowAsync(CancellationToken.None);

        var stored = store.Load();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(candidate, Is.Null);
            Assert.That(coordinator.LatestCandidate, Is.Null);
            Assert.That(stored.SkippedVersion, Is.EqualTo(PreparedVersion));
            Assert.That(stored.LastCheckUtc, Is.Not.Null);
        }
    }

    [Test]
    public void PrepareAsync_Throws_OnUnsupportedBuild()
    {
        var installer = Substitute.For<IUpdateInstaller>();
        using var coordinator = Coordinator(Store(CoreUpdateApplyMode.SilentOnExit), installer, UpdateKind.Unsupported);

        Assert.ThrowsAsync<UpdateException>(() => coordinator.PrepareAsync(Candidate(), null, CancellationToken.None));
        _ = installer.DidNotReceiveWithAnyArgs().PrepareAsync(default!, default, default);
    }

    [TestCase(PreparedVersion, false)]
    [TestCase("3.1.0.8", true)]
    public void SkipVersion_DiscardsPreparedUpdate_OnlyOfThatVersion(string skipped, bool pendingRemains)
    {
        WritePending();
        var installer = Substitute.For<IUpdateInstaller>();
        installer.ReadPending().Returns(_ => PendingUpdateStore.Read(_staging, NullLogger.Instance));
        using var coordinator = Coordinator(Store(CoreUpdateApplyMode.SilentOnExit), installer, UpdateKind.Portable);

        coordinator.SkipVersion(skipped);

        Assert.That(File.Exists(Path.Combine(_staging, PendingUpdateStore.FileName)), Is.EqualTo(pendingRemains));
    }

    [Test]
    public async Task SkipVersion_DuringDownload_LeavesDiscardToTheDownload()
    {
        var installer = Substitute.For<IUpdateInstaller>();
        var download = new TaskCompletionSource<PendingUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        installer.PrepareAsync(Arg.Any<UpdateCandidate>(), Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>())
            .Returns(download.Task);
        installer.ReadPending().Returns(_ => PendingUpdateStore.Read(_staging, NullLogger.Instance));
        using var coordinator = Coordinator(Store(CoreUpdateApplyMode.SilentOnExit), installer, UpdateKind.Portable);

        var silent = coordinator.TryPrepareSilentlyAsync(Candidate(), CancellationToken.None);
        var previous = WritePending("3.1.0.5");

        coordinator.SkipVersion(PreparedVersion);
        Assert.That(File.Exists(previous.StagedPath), Is.True);

        var downloaded = WritePending();
        download.SetResult(downloaded);

        Assert.That(await silent, Is.False);
        Assert.That(File.Exists(Path.Combine(_staging, PendingUpdateStore.FileName)), Is.False);
        Assert.That(File.Exists(downloaded.StagedPath), Is.False);
    }

    [Test]
    public async Task SkipVersion_DuringAnotherVersionsFailedDownload_DiscardsPreviousPending()
    {
        var installer = Substitute.For<IUpdateInstaller>();
        var eventBus = Substitute.For<IEventBus>();
        var download = new TaskCompletionSource<PendingUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        WritePending("3.1.0.5");
        installer.ReadPending().Returns(_ => PendingUpdateStore.Read(_staging, NullLogger.Instance));
        installer.PrepareAsync(Arg.Any<UpdateCandidate>(), Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>())
            .Returns(download.Task);
        using var coordinator = Coordinator(Store(CoreUpdateApplyMode.SilentOnExit), installer, UpdateKind.Portable, eventBus);

        var silent = coordinator.TryPrepareSilentlyAsync(Candidate("3.1.0.7"), CancellationToken.None);

        coordinator.SkipVersion("3.1.0.5");
        Assert.That(File.Exists(Path.Combine(_staging, PendingUpdateStore.FileName)), Is.True);

        download.SetException(new UpdateException("Обрыв сети"));

        Assert.That(await silent, Is.False);
        Assert.That(File.Exists(Path.Combine(_staging, PendingUpdateStore.FileName)), Is.False);
        await eventBus.Received(1).PublishAsync(Arg.Is<UpdateDiscarded>(e => e.Version == "3.1.0.5"), Arg.Any<CancellationToken>());
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task DiscardPreparedUpdateAsync_DuringAnotherVersionsFailedDownload_DiscardsPreviousPending(bool silent)
    {
        var installer = Substitute.For<IUpdateInstaller>();
        var eventBus = Substitute.For<IEventBus>();
        var download = new TaskCompletionSource<PendingUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        WritePending("3.1.0.5");
        installer.ReadPending().Returns(_ => PendingUpdateStore.Read(_staging, NullLogger.Instance));
        installer.PrepareAsync(Arg.Any<UpdateCandidate>(), Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>())
            .Returns(download.Task);
        var store = Store(CoreUpdateApplyMode.SilentOnExit);
        using var coordinator = Coordinator(store, installer, UpdateKind.Portable, eventBus);

        Task activeDownload = silent
            ? coordinator.TryPrepareSilentlyAsync(Candidate("3.1.0.7"), CancellationToken.None)
            : coordinator.PrepareAsync(Candidate("3.1.0.7"), null, CancellationToken.None);

        SetStored(store, settings => settings.ApplyMode = CoreUpdateApplyMode.NotifyAndConfirm);
        var discardedImmediately = await coordinator.DiscardPreparedUpdateAsync(CancellationToken.None);

        Assert.That(discardedImmediately, Is.False);
        Assert.That(File.Exists(Path.Combine(_staging, PendingUpdateStore.FileName)), Is.True);

        download.SetException(new UpdateException("Обрыв сети"));

        if (silent)
        {
            Assert.That(await (Task<bool>)activeDownload, Is.False);
        }
        else
        {
            Assert.ThrowsAsync<UpdateException>(async () => await activeDownload);
        }

        Assert.That(File.Exists(Path.Combine(_staging, PendingUpdateStore.FileName)), Is.False);
        await eventBus.Received(1).PublishAsync(Arg.Is<UpdateDiscarded>(e => e.Version == "3.1.0.5"), Arg.Any<CancellationToken>());
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task DiscardInWindowBetweenDiscardReasonAndGateRelease_DeletesPreparedVersion(bool skip)
    {
        var installer = Substitute.For<IUpdateInstaller>();
        var eventBus = Substitute.For<IEventBus>();
        var store = Store(CoreUpdateApplyMode.SilentOnExit);
        installer.ReadPending().Returns(_ => PendingUpdateStore.Read(_staging, NullLogger.Instance));
        installer.PrepareAsync(Arg.Any<UpdateCandidate>(), Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(WritePending(PreparedVersion)));

        using var coordinator = Coordinator(store, installer, UpdateKind.Portable, eventBus);
        coordinator.BeforePrepareGateRelease = () =>
        {
            if (skip)
            {
                coordinator.SkipVersion(PreparedVersion);
            }
            else
            {
                SetStored(store, settings => settings.ApplyMode = CoreUpdateApplyMode.NotifyAndConfirm);
                var discardedNow = coordinator.DiscardPreparedUpdateAsync(CancellationToken.None).GetAwaiter().GetResult();
                Assert.That(discardedNow, Is.False);
            }
        };

        var prepared = await coordinator.TryPrepareSilentlyAsync(Candidate(), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(prepared, Is.False);
            Assert.That(PendingUpdateStore.Read(_staging, NullLogger.Instance), Is.Null);
            Assert.That(File.Exists(Path.Combine(_staging, PendingUpdateStore.FileName)), Is.False);
            await eventBus.Received(1).PublishAsync(Arg.Is<UpdateDiscarded>(e => e.Version == PreparedVersion), Arg.Any<CancellationToken>());
            await eventBus.DidNotReceive().PublishAsync(Arg.Any<UpdatePrepared>(), Arg.Any<CancellationToken>());
        }
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

        using var coordinator = Coordinator(Store(CoreUpdateApplyMode.NotifyAndConfirm), installer, UpdateKind.Portable);

        var manualDownload = coordinator.PrepareAsync(Candidate(), null, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var stopTask = coordinator.CancelPendingDownloadsAsync(CancellationToken.None);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(stopTask.IsCompleted, Is.False, "Остановка должна дожидаться завершения отмены и очистки .part до ApplyPendingUpdate");

        allowCleanupFinish.SetResult();
        await stopTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.CatchAsync<OperationCanceledException>(async () => await manualDownload);
    }

    [Test]
    public async Task DiscardPending_RemovesOnlyTheNamedVersion_AndNothingOutsideStaging()
    {
        var outside = Path.Combine(_root, "outside.exe");
        await File.WriteAllTextAsync(outside, "keep");

        var environment = CreateEnvironment(UpdateKind.Portable);
        var installer = new SingleFileSwapInstaller(new UpdateDownloader(Substitute.For<IReleaseSource>()), environment);
        using var coordinator = Coordinator(Store(CoreUpdateApplyMode.NotifyAndConfirm), installer, UpdateKind.Portable);

        WritePending(PreparedVersion, stagedPath: outside);
        coordinator.SkipVersion(PreparedVersion);
        Assert.That(File.Exists(outside), Is.True);
        Assert.That(File.Exists(Path.Combine(_staging, PendingUpdateStore.FileName)), Is.False);

        var store = Store(CoreUpdateApplyMode.SilentOnExit);
        using var coordinator2 = Coordinator(store, installer, UpdateKind.Portable);
        var staged = Path.Combine(_staging, "new.exe");
        WritePending(PreparedVersion, stagedPath: staged);

        coordinator2.SkipVersion("3.1.0.8");
        var pendingPath = Path.Combine(_staging, PendingUpdateStore.FileName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(pendingPath), Is.True);
            Assert.That(await coordinator2.DiscardPreparedUpdateAsync(CancellationToken.None), Is.True);
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
    public async Task BackgroundLoop_HandsFoundCandidateToSilentPrepare()
    {
        var candidate = Candidate();
        var handedOver = new TaskCompletionSource<UpdateCandidate>(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = Substitute.For<IUpdateCoordinator>();
        coordinator.Kind.Returns(UpdateKind.Portable);
        coordinator.IsUpdatable.Returns(true);
        coordinator.CheckNowAsync(Arg.Any<CancellationToken>()).Returns(candidate);
        coordinator.TryPrepareSilentlyAsync(Arg.Any<UpdateCandidate>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                handedOver.TrySetResult(call.Arg<UpdateCandidate>());
                return true;
            });

        using var service = new UpdateBackgroundService(coordinator,
            Store(CoreUpdateApplyMode.SilentOnExit),
            new ShortDelaysOnlyTimeProvider(),
            NullLogger<UpdateBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        var received = await handedOver.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(received, Is.SameAs(candidate));
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
