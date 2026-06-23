using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EkipppOptimizer;

public class InvertBoolConverter : IValueConverter
{
    public static readonly InvertBoolConverter Instance = new();
    public object Convert(object v, Type t, object p, CultureInfo c)  => v is bool b && !b;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => v is bool b && !b;
}

public class SizeConverter : IValueConverter
{
    public static readonly SizeConverter Instance = new();
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        if (v is not long bytes) return "0 o";
        if (bytes >= 1L << 30) return $"{bytes / (1024.0 * 1024 * 1024):F1} Go";
        if (bytes >= 1L << 20) return $"{bytes / (1024.0 * 1024):F1} Mo";
        if (bytes >= 1L << 10) return $"{bytes / 1024.0:F1} Ko";
        return $"{bytes} o";
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => DependencyProperty.UnsetValue;
}

public class PercentToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type t, object p, CultureInfo c)
    {
        if (values[0] is double pct && values[1] is double max && max > 0)
            return Math.Max(0, Math.Min(max, max * pct / 100.0));
        return 0.0;
    }
    public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class PercentToHeightConverter : IValueConverter
{
    public static readonly PercentToHeightConverter Instance = new();
    public double MaxHeight { get; set; } = 56;
    public object Convert(object v, Type t, object p, CultureInfo c)
        => v is double pct ? Math.Max(2, pct / 100.0 * MaxHeight) : 2.0;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => DependencyProperty.UnsetValue;
}

public class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance = new();
    public object Convert(object v, Type t, object p, CultureInfo c)
        => v is bool b && b ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => v is Visibility vis && vis == Visibility.Visible;
}

// ConverterParameter = "TextWhenTrue|TextWhenFalse"
public class BoolToTextConverter : IValueConverter
{
    public static readonly BoolToTextConverter Instance = new();
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        bool b = v is bool bv && bv;
        var parts = (p?.ToString() ?? "|").Split('|');
        return b ? (parts.Length > 0 ? parts[0] : "") : (parts.Length > 1 ? parts[1] : "");
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => DependencyProperty.UnsetValue;
}
