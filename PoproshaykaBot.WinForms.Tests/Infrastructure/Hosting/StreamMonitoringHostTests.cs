using PoproshaykaBot.WinForms.Infrastructure.Hosting;

namespace PoproshaykaBot.WinForms.Tests.Infrastructure.Hosting;

[TestFixture]
public sealed class StreamMonitoringHostTests
{
    [Test]
    public async Task StartAsync_RunsStreamComponentsInStartOrder()
    {
        var trace = new List<string>();
        var components = new IStreamHostedComponent[]
        {
            new RecordingStreamComponent("third", 300, trace),
            new RecordingStreamComponent("first", 100, trace),
            new RecordingStreamComponent("second", 200, trace),
        };

        var host = new StreamMonitoringHost(components, NullLogger<StreamMonitoringHost>.Instance);
        await host.StartAsync(CancellationToken.None);

        Assert.That(trace, Is.EqualTo(["start:first", "start:second", "start:third"]));
    }

    [Test]
    public async Task StopAsync_RunsComponentsInReverseOrder()
    {
        var trace = new List<string>();
        var components = new IStreamHostedComponent[]
        {
            new RecordingStreamComponent("a", 100, trace),
            new RecordingStreamComponent("b", 200, trace),
        };

        var host = new StreamMonitoringHost(components, NullLogger<StreamMonitoringHost>.Instance);
        await host.StartAsync(CancellationToken.None);
        trace.Clear();

        await host.StopAsync(CancellationToken.None);

        Assert.That(trace, Is.EqualTo(["stop:b", "stop:a"]));
    }

    [Test]
    public void StreamComponentMarker_ExtendsHostedComponent()
    {
        Assert.That(typeof(IHostedComponent).IsAssignableFrom(typeof(IStreamHostedComponent)),
            Is.True,
            "IStreamHostedComponent должен наследоваться от IHostedComponent, чтобы можно было переиспользовать раннер компонентов");
    }

    private sealed class RecordingStreamComponent(string name, int order, List<string> trace) : IStreamHostedComponent
    {
        public string Name { get; } = name;

        public int StartOrder { get; } = order;

        public Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken)
        {
            trace.Add($"start:{Name}");
            return Task.CompletedTask;
        }

        public Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken)
        {
            trace.Add($"stop:{Name}");
            return Task.CompletedTask;
        }
    }
}
