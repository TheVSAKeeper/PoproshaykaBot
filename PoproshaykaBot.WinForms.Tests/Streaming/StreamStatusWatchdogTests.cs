using PoproshaykaBot.WinForms.Streaming;

namespace PoproshaykaBot.WinForms.Tests.Streaming;

[TestFixture]
public sealed class StreamStatusWatchdogTests
{
    [SetUp]
    public void SetUp()
    {
        _stream = Substitute.For<IStreamStatus>();
        _watchdog = new(_stream, TimeProvider.System, TimeSpan.FromMilliseconds(40),
            NullLogger<StreamStatusWatchdog>.Instance);
    }

    private static readonly IProgress<string> NullProgress = new Progress<string>(_ => { });

    private IStreamStatus _stream = null!;
    private StreamStatusWatchdog _watchdog = null!;

    [Test]
    public async Task StartStop_DoesNotLeakLoopTask()
    {
        await _watchdog.StartAsync(NullProgress, CancellationToken.None);
        await Task.Delay(100);
        await _watchdog.StopAsync(NullProgress, CancellationToken.None);
    }

    [Test]
    public async Task RunLoop_CallsRefreshLiveSnapshotOnlyWhenOnline()
    {
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _stream.CurrentStatus.Returns(StreamStatus.Online);
        _stream.RefreshLiveSnapshotAsync()
            .Returns(_ =>
            {
                refreshed.TrySetResult();
                return Task.CompletedTask;
            });

        await _watchdog.StartAsync(NullProgress, CancellationToken.None);
        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await _watchdog.StopAsync(NullProgress, CancellationToken.None);

        await _stream.Received().RefreshLiveSnapshotAsync();
    }

    [Test]
    public async Task RunLoop_SkipsRefreshWhenOffline()
    {
        _stream.CurrentStatus.Returns(StreamStatus.Offline);

        await _watchdog.StartAsync(NullProgress, CancellationToken.None);
        await Task.Delay(150);
        await _watchdog.StopAsync(NullProgress, CancellationToken.None);

        await _stream.DidNotReceive().RefreshLiveSnapshotAsync();
    }
}
