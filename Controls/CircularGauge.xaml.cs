using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EkipppOptimizer;

public partial class CircularGauge : ContentControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(CircularGauge),
            new PropertyMetadata(0.0, OnChanged));
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(CircularGauge),
            new PropertyMetadata("", OnChanged));
    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(CircularGauge),
            new PropertyMetadata("", OnChanged));
    public static readonly DependencyProperty ArcBrushProperty =
        DependencyProperty.Register(nameof(ArcBrush), typeof(Brush), typeof(CircularGauge),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xA7, 0x8B, 0xFA)), OnChanged));
    public static readonly DependencyProperty CenterTextProperty =
        DependencyProperty.Register(nameof(CenterText), typeof(string), typeof(CircularGauge),
            new PropertyMetadata("", OnChanged));

    public double Value      { get => (double)GetValue(ValueProperty);      set => SetValue(ValueProperty,      value); }
    public string Title      { get => (string)GetValue(TitleProperty);      set => SetValue(TitleProperty,      value); }
    public string Subtitle   { get => (string)GetValue(SubtitleProperty);   set => SetValue(SubtitleProperty,   value); }
    public Brush  ArcBrush   { get => (Brush)GetValue(ArcBrushProperty);    set => SetValue(ArcBrushProperty,   value); }
    public string CenterText { get => (string)GetValue(CenterTextProperty); set => SetValue(CenterTextProperty, value); }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((CircularGauge)d).Update();

    // ── live references ────────────────────────────────────────────────────
    private Path?      _arc;
    private TextBlock? _pctText;
    private TextBlock? _titleText;
    private TextBlock? _subText;
    private bool       _built;

    private static SolidColorBrush HexBrush(string hex)
        => new((Color)ColorConverter.ConvertFromString(hex));

    public CircularGauge() { Loaded += (_, _) => Build(); }

    private void Build()
    {
        const double size = 120, cx = 60, cy = 60, r = 48, sw = 9;

        var trackEllipse = new Ellipse
        {
            Width = size, Height = size,
            Stroke = HexBrush("#1E1232"), StrokeThickness = sw, Fill = Brushes.Transparent
        };
        _arc = new Path
        {
            StrokeThickness = sw,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round,
            Fill = Brushes.Transparent
        };

        var canvas = new Canvas { Width = size, Height = size };
        canvas.Children.Add(trackEllipse);
        canvas.Children.Add(_arc);

        _pctText = new TextBlock
        {
            FontSize = 26, FontWeight = FontWeights.Black,
            Foreground = HexBrush("#FFFFFF"),
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        TextOptions.SetTextRenderingMode(_pctText, TextRenderingMode.ClearType);

        var gaugeGrid = new Grid { Width = size, Height = size };
        gaugeGrid.Children.Add(canvas);
        gaugeGrid.Children.Add(_pctText);

        _titleText = new TextBlock
        {
            FontSize = 9, FontWeight = FontWeights.Bold,
            Foreground = HexBrush("#7A6B9A"),
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10),
            MaxWidth = 130
        };
        TextOptions.SetTextRenderingMode(_titleText, TextRenderingMode.ClearType);

        _subText = new TextBlock
        {
            FontSize = 11, Foreground = HexBrush("#9B8BB0"),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 130,
            MinHeight = 38,
            Margin = new Thickness(0, 10, 0, 0)
        };
        TextOptions.SetTextRenderingMode(_subText, TextRenderingMode.ClearType);

        var sp = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Top
        };
        sp.Children.Add(_titleText);
        sp.Children.Add(gaugeGrid);
        sp.Children.Add(_subText);

        Content = new Border
        {
            Background = HexBrush("#0D0818"), CornerRadius = new CornerRadius(14),
            Padding = new Thickness(18, 16, 18, 16), Child = sp,
            VerticalAlignment = VerticalAlignment.Top
        };

        _built = true;
        Update();
    }

    private void Update()
    {
        if (!_built) return;

        const double cx = 60, cy = 60, r = 48;
        double v = Math.Max(0, Math.Min(100, Value));

        _pctText!.Text   = CenterText.Length > 0 ? CenterText : $"{v:F0}%";
        _titleText!.Text = Title;
        _subText!.Text   = Subtitle;
        _arc!.Stroke     = ArcBrush;

        double angle = v / 100.0 * 360.0;
        if (angle < 0.1) { _arc.Data = null; return; }

        if (angle >= 359.99)
        {
            _arc.Data = new PathGeometry(new[]
            {
                new PathFigure(new Point(cx, cy - r), new PathSegment[]
                {
                    new ArcSegment(new Point(cx + 0.01, cy - r),
                        new Size(r, r), 0, true, SweepDirection.Clockwise, true)
                }, false)
            });
            return;
        }

        double sRad = -Math.PI / 2;
        double eRad = sRad + angle * Math.PI / 180.0;
        _arc.Data = new PathGeometry(new[]
        {
            new PathFigure(
                new Point(cx + r * Math.Cos(sRad), cy + r * Math.Sin(sRad)),
                new PathSegment[]
                {
                    new ArcSegment(
                        new Point(cx + r * Math.Cos(eRad), cy + r * Math.Sin(eRad)),
                        new Size(r, r), 0, angle > 180,
                        SweepDirection.Clockwise, true)
                }, false)
        });
    }
}
