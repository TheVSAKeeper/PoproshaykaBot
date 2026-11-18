using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using System.Windows.Threading;

namespace PoproshaykaBot.Wpf.Tests.Bootstrap;

[TestFixture]
public sealed class StartupRollbackTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task Частичный_запуск_откатывается_без_блокировки_Dispatcher(bool streamMonitoring)
    {
        var first = new Component(1);
        var second = new Component(2);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            _ = dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    if (streamMonitoring)
                    {
                        var host = new StreamMonitoringHost([first, second], NullLogger<StreamMonitoringHost>.Instance);
                        await App.StartStreamMonitoringAsync([host], new List<StreamMonitoringHost>());
                    }
                    else
                    {
                        var lifetime = new AppLifetime([first, second], NullLogger<AppLifetime>.Instance);
                        await App.StartAppLifetimeAsync(lifetime);
                    }

                    completion.SetResult();
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
                }
            });
            Dispatcher.Run();
        }) { IsBackground = true };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(first.Stopped, Is.True);
        Assert.That(second.Stopped, Is.False);
    }

    private sealed class Component(int order) : IStreamHostedComponent, IAppLifetimeComponent
    {
        public string Name => $"Компонент {order}";
        public int StartOrder => order;
        public bool Stopped { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken) => order == 1
            ? Task.CompletedTask : Task.FromException(new InvalidOperationException());

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await Task.Yield();
            Stopped = true;
        }

        public Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken) => StartAsync(cancellationToken);

        public Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken) => StopAsync(cancellationToken);
    }
}
