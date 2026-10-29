using System.Windows;
using System.Windows.Controls;

namespace PawnAppraiser.Desktop;

public sealed class ResponsiveOptionsPanel : Panel
{
    private int columns = 2;
    private double[] rows = [];
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 300;
        columns = Math.Max(2, (int)(width / 130));
        rows = new double[(InternalChildren.Count + columns - 1) / columns];
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            InternalChildren[i].Measure(new(width / columns, double.PositiveInfinity));
            rows[i / columns] = Math.Max(rows[i / columns], InternalChildren[i].DesiredSize.Height);
        }
        return new(width, rows.Sum());
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            InternalChildren[i].Arrange(new((i % columns) * finalSize.Width / columns, y, finalSize.Width / columns, rows[i / columns]));
            if (i % columns == columns - 1) y += rows[i / columns];
        }
        return finalSize;
    }
}
