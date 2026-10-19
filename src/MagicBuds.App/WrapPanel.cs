using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace MagicBuds.App;

/// <summary>
/// Lays children out left to right at their natural width, wrapping to a new row when the next
/// one doesn't fit.
/// </summary>
/// <remarks>
/// Used for the battery row: in the flyout's headphone list, "Left 100%  Right 100%  Case 100%"
/// is wider than the space beside the picture, so the case level wraps instead of being cut
/// off. Given unlimited width (the tray tooltip) it stays on one line. Collapsed children take
/// no space or spacing, like a StackPanel.
/// </remarks>
public sealed partial class WrapPanel : Panel
{
    public double ColumnSpacing { get; set; }

    public double RowSpacing { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        }
        return Place(availableSize.Width, arrange: false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Place(finalSize.Width, arrange: true);
        return finalSize;
    }

    // One pass for both measure and arrange, so the two can't disagree on where rows break
    private Size Place(double width, bool arrange)
    {
        double x         = 0;
        double y         = 0;
        double rowHeight = 0;
        double widest    = 0;
        var    rowStart  = 0;
        var    visible   = Children.Where(child => child.Visibility == Visibility.Visible).ToList();

        for (var i = 0; i < visible.Count; i++)
        {
            var size = visible[i].DesiredSize;

            // Wrap, unless this is the row's first child (too wide either way)
            if (i > rowStart && x + ColumnSpacing + size.Width > width)
            {
                y        += rowHeight + RowSpacing;
                x         = 0;
                rowHeight = 0;
                rowStart  = i;
            }

            if (i > rowStart)
            {
                x += ColumnSpacing;
            }

            if (arrange)
            {
                visible[i].Arrange(new Rect(x, y, size.Width, size.Height));
            }

            x        += size.Width;
            rowHeight = Math.Max(rowHeight, size.Height);
            widest    = Math.Max(widest, x);
        }

        return new Size(widest, y + rowHeight);
    }
}
