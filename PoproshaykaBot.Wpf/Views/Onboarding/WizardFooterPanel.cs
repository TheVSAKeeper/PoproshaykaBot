using System.Windows;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Views.Onboarding;

public sealed class WizardFooterPanel : Panel
{
    public double HorizontalGap { get; set; } = 16;

    public double VerticalGap { get; set; } = 8;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (InternalChildren.Count == 0)
        {
            return default;
        }

        if (InternalChildren.Count == 1)
        {
            InternalChildren[0].Measure(availableSize);
            return InternalChildren[0].DesiredSize;
        }

        var left = InternalChildren[0];
        var right = InternalChildren[1];

        left.Measure(availableSize);
        right.Measure(availableSize);

        var singleRowWidth = left.DesiredSize.Width + HorizontalGap + right.DesiredSize.Width;
        if (double.IsInfinity(availableSize.Width) || singleRowWidth <= availableSize.Width)
        {
            var width = double.IsInfinity(availableSize.Width) ? singleRowWidth : availableSize.Width;
            return new Size(width, Math.Max(left.DesiredSize.Height, right.DesiredSize.Height));
        }

        return new Size(
            Math.Min(availableSize.Width, Math.Max(left.DesiredSize.Width, right.DesiredSize.Width)),
            left.DesiredSize.Height + VerticalGap + right.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count == 0)
        {
            return finalSize;
        }

        if (InternalChildren.Count == 1)
        {
            var only = InternalChildren[0];
            only.Arrange(new Rect(0, 0, Math.Min(only.DesiredSize.Width, finalSize.Width), only.DesiredSize.Height));
            return finalSize;
        }

        var left = InternalChildren[0];
        var right = InternalChildren[1];

        var leftWidth = left.DesiredSize.Width;
        var leftHeight = left.DesiredSize.Height;
        var rightWidth = right.DesiredSize.Width;
        var rightHeight = right.DesiredSize.Height;

        if (leftWidth + HorizontalGap + rightWidth <= finalSize.Width)
        {
            var leftY = Math.Max(0, (finalSize.Height - leftHeight) / 2);
            var rightY = Math.Max(0, (finalSize.Height - rightHeight) / 2);
            left.Arrange(new Rect(0, leftY, leftWidth, leftHeight));
            right.Arrange(new Rect(finalSize.Width - rightWidth, rightY, rightWidth, rightHeight));
        }
        else
        {
            var clampedLeft = Math.Min(leftWidth, finalSize.Width);
            var clampedRight = Math.Min(rightWidth, finalSize.Width);
            left.Arrange(new Rect(0, 0, clampedLeft, leftHeight));
            right.Arrange(new Rect(Math.Max(0, finalSize.Width - clampedRight), leftHeight + VerticalGap, clampedRight, rightHeight));
        }

        return finalSize;
    }
}
