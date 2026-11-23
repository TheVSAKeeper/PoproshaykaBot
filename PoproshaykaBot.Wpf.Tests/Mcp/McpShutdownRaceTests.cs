using KeepShell.Mcp;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using System.Diagnostics;
using System.Windows.Threading;

namespace PoproshaykaBot.Wpf.Tests.Mcp;

[TestFixture]
[Category("Mcp")]
[NonParallelizable]
public sealed class McpShutdownRaceTests
{
    private static readonly TimeSpan ShutdownBound = TimeSpan.FromSeconds(5);

    [Test]
    public async Task StopAsyncAndDispose_DuringSlowInFlightDisconnectBot_CompletePromptlyWithoutDeadlock()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var controller = new McpTestHarness.FakeBotConnectionController
        {
            CurrentPhase = BotLifecyclePhase.Connected,
            StopHandler = async _ =>
            {
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
            },
        };

        await using var harness = await McpTestHarness.StartAsync(
            allowMutations: true,
            connectionController: controller,
            shutdownTimeout: TimeSpan.FromMilliseconds(300));

        await using var client = await harness.ConnectClientAsync();

        Exception? clientException = null;
        var inflightCall = Task.Run(async () =>
        {
            try
            {
                _ = await client.CallToolAsync("disconnect_bot");
            }
            catch (Exception exception)
            {
                clientException = exception;
            }
        });

        try
        {
            Assert.That(await entered.Task.WaitAsync(ShutdownBound), Is.True, "Обработчик disconnect_bot должен начать выполнение.");

            using var stopCts = new CancellationTokenSource(ShutdownBound);
            var sw = Stopwatch.StartNew();

            var stopTask = harness.Host.StopAsync(stopCts.Token);
            var disposeTask = Task.Run(() => harness.Host.Dispose());

            await Task.WhenAll(stopTask, disposeTask).WaitAsync(ShutdownBound);
            await inflightCall.WaitAsync(ShutdownBound);
            sw.Stop();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(harness.Host.IsRunning, Is.False);
                Assert.That(sw.Elapsed, Is.LessThan(ShutdownBound), "Остановка и Dispose во время активного disconnect_bot не должны зависать.");
                Assert.That(inflightCall.IsCompleted, Is.True);
                Assert.That(clientException, Is.Null.Or.InstanceOf<Exception>());
            }
        }
        finally
        {
            release.TrySetResult(true);
            await inflightCall.WaitAsync(ShutdownBound);
        }
    }

    [Test]
    public async Task StopAsync_CancelsPendingMcpDispatchRunAsyncOnBlockedDispatcher_WithoutHang()
    {
        using var dispatcherReady = new ManualResetEventSlim();
        using var blockerEntered = new ManualResetEventSlim();
        using var releaseBlocker = new ManualResetEventSlim();
        Dispatcher? isolatedDispatcher = null;

        var staThread = new Thread(() =>
        {
            isolatedDispatcher = Dispatcher.CurrentDispatcher;
            dispatcherReady.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
        };
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();

        Assert.That(dispatcherReady.Wait(ShutdownBound), Is.True);

        var gate = new DispatchProbeGate();
        var dispatch = new McpDispatch(TimeSpan.FromSeconds(20), () => isolatedDispatcher);

        await using var harness = await McpTestHarness.StartAsync(
            allowMutations: false,
            dispatch: dispatch,
            shutdownTimeout: TimeSpan.FromSeconds(2),
            extraToolTypes: [typeof(DispatchProbeTools)],
            configureExtraTools: tools => tools.AddSingleton(gate));

        try
        {
            _ = isolatedDispatcher!.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                blockerEntered.Set();
                _ = releaseBlocker.Wait(TimeSpan.FromSeconds(10));
            }));
            Assert.That(blockerEntered.Wait(ShutdownBound), Is.True);

            await using var client = await harness.ConnectClientAsync();

            Exception? clientException = null;
            var inflightCall = Task.Run(async () =>
            {
                try
                {
                    _ = await client.CallToolAsync("dispatch_probe");
                }
                catch (Exception exception)
                {
                    clientException = exception;
                }
            });

            Assert.That(await gate.Queued.Task.WaitAsync(ShutdownBound), Is.True);
            await Task.Delay(50);

            var sw = Stopwatch.StartNew();
            await harness.Host.StopAsync().WaitAsync(ShutdownBound);
            sw.Stop();

            await inflightCall.WaitAsync(ShutdownBound);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(harness.Host.IsRunning, Is.False);
                Assert.That(sw.Elapsed, Is.LessThan(ShutdownBound), "StopAsync обязан прерывать ожидающий McpDispatch.RunAsync быстрее DispatchTimeout.");
                Assert.That(inflightCall.IsCompleted, Is.True);
                Assert.That(clientException, Is.Null.Or.InstanceOf<Exception>());
            }
        }
        finally
        {
            releaseBlocker.Set();
            isolatedDispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
            _ = staThread.Join(ShutdownBound);
        }
    }

    internal sealed class DispatchProbeGate
    {
        public TaskCompletionSource<bool> Queued { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    [McpServerToolType]
    internal static class DispatchProbeTools
    {
        [McpServerTool(Name = "dispatch_probe")]
        [System.ComponentModel.Description("Тестовый инструмент, ожидающий заблокированный UI-диспетчер через McpDispatch.RunAsync")]
        public static async Task<string> DispatchProbeAsync(McpDispatch dispatch, DispatchProbeGate gate, CancellationToken cancellationToken)
        {
            gate.Queued.TrySetResult(true);
            await dispatch.RunAsync(static () => { }, DispatcherPriority.Normal, "dispatch_probe", cancellationToken).ConfigureAwait(false);
            return "ok";
        }
    }
}
