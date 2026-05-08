using PoproshaykaBot.WinForms.Controls;

namespace PoproshaykaBot.Core.Tests.Controls;

[TestFixture]
[Apartment(ApartmentState.STA)]
public class PanelSlotTests
{
    [Test]
    public void SetBody_WithNull_ShouldDetachPrevious()
    {
        using var slot = new PanelSlot();
        using var label = new Label();
        slot.SetBody(label);

        slot.SetBody(null);

        Assert.That(label.Parent, Is.Null);
    }

    [Test]
    public void SetBody_WithControl_ShouldAddItDocked()
    {
        using var slot = new PanelSlot();
        using var label = new Label();

        slot.SetBody(label);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(label.Dock, Is.EqualTo(DockStyle.Fill));
            Assert.That(slot.Contains(label), Is.True);
        }
    }

    [Test]
    public void SetBody_ReplacingControl_ShouldMoveWithoutDisposing()
    {
        using var slot = new PanelSlot();
        using var first = new Label { Text = "first" };
        using var second = new Label { Text = "second" };

        slot.SetBody(first);
        slot.SetBody(second);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.IsDisposed, Is.False);
            Assert.That(first.Parent, Is.Null);
            Assert.That(slot.Contains(second), Is.True);
        }
    }
}
