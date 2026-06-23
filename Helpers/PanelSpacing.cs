using System.Windows;
using System.Windows.Controls;

namespace EkipppOptimizer.Helpers;

public static class PanelSpacing
{
    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.RegisterAttached("Spacing", typeof(double), typeof(PanelSpacing),
            new PropertyMetadata(0d, OnSpacingChanged));

    public static double GetSpacing(DependencyObject obj) => (double)obj.GetValue(SpacingProperty);
    public static void SetSpacing(DependencyObject obj, double v) => obj.SetValue(SpacingProperty, v);

    private static void OnSpacingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Panel panel) return;
        panel.Loaded += (_, _) => Apply(panel, (double)e.NewValue);
        Apply(panel, (double)e.NewValue);
    }

    private static void Apply(Panel panel, double spacing)
    {
        bool isHorizontal = panel is StackPanel sp && sp.Orientation == Orientation.Horizontal;
        for (int i = 0; i < panel.Children.Count; i++)
        {
            if (panel.Children[i] is not FrameworkElement fe) continue;
            fe.Margin = i == 0
                ? new Thickness(0)
                : isHorizontal
                    ? new Thickness(spacing, 0, 0, 0)
                    : new Thickness(0, spacing, 0, 0);
        }
    }
}
