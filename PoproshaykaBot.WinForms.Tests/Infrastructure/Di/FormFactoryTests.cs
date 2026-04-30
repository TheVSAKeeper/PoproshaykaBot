using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.WinForms.Infrastructure.Di;

namespace PoproshaykaBot.WinForms.Tests.Infrastructure.Di;

[TestFixture]
[Apartment(ApartmentState.STA)]
public class FormFactoryTests
{
    [Test]
    public void Create_ScopeDisposedOnFormDisposed_NotOnFormClosed()
    {
        var services = new ServiceCollection();
        services.AddScoped<TrackingService>();
        services.AddTransient<TestForm>();
        var provider = services.BuildServiceProvider();
        var factory = new FormFactory(provider.GetRequiredService<IServiceScopeFactory>());

        var form = factory.Create<TestForm>();
        var tracker = form.Tracker;

        form.OnFormClosedManual();

        Assert.That(tracker.Disposed, Is.False, "Scope must not be disposed on FormClosed so callers can read form state after ShowDialog returns");

        form.Dispose();

        Assert.That(tracker.Disposed, Is.True, "Scope must be disposed after the form itself is disposed");
    }

    private sealed class TrackingService : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private sealed class TestForm(TrackingService tracker) : Form
    {
        public TrackingService Tracker { get; } = tracker;

        public void OnFormClosedManual()
        {
            OnFormClosed(new(CloseReason.UserClosing));
        }
    }
}
