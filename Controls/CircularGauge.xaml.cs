using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

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

    // Déclenché quand l'anneau finit de rejoindre sa nouvelle valeur — utile pour synchroniser
    // un effet (confettis, chime…) exactement sur l'instant où le score "atterrit".
    public event Action? AnimationSettled;

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var gauge = (CircularGauge)d;
        if (e.Property == ValueProperty) gauge.RetargetValue((double)e.NewValue);
        else gauge.Update();
    }

    // ── live references ────────────────────────────────────────────────────
    private Path?      _arc;
    private Path?       _celebrateRing;
    private TextBlock? _pctText;
    private TextBlock? _titleText;
    private TextBlock? _subText;
    private bool       _built;

    // ── anneau qui "suit" la valeur au lieu de sauter dessus ──────────────────
    private DispatcherTimer? _animTimer;
    private double   _displayValue;
    private double   _animTarget;
    private double   _animStartValue;
    private DateTime _animStartTime;
    private TimeSpan _animDuration;

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

        // Anneau fin, caché par défaut — un unique passage de lumière joué par Celebrate(),
        // réservé aux scores d'exception (voir MainViewModel.UpdatePcScore).
        _celebrateRing = new Path
        {
            StrokeThickness = 3,
            Opacity = 0,
            IsHitTestVisible = false,
            Stroke = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Colors.Transparent, 0),
                    new GradientStop(Colors.White, 0.5),
                    new GradientStop(Colors.Transparent, 1),
                }, new Point(0, 0), new Point(1, 1)),
            Data = new EllipseGeometry(new Point(cx, cy), r + 6, r + 6),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform()
        };

        var canvas = new Canvas { Width = size, Height = size };
        canvas.Children.Add(trackEllipse);
        canvas.Children.Add(_arc);
        canvas.Children.Add(_celebrateRing);

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
        // Premier affichage : l'anneau part de 0 et rejoint la vraie valeur au lieu d'apparaître déjà posé.
        RetargetValue(Value);
    }

    // Impose la valeur affichée instantanément, sans animation — utile pour repositionner l'anneau
    // sur un "avant" juste avant de lancer un sweep animé vers un "après" (écran de victoire).
    public void SetInstant(double value)
    {
        double clamped = Math.Max(0, Math.Min(100, value));
        _animTimer?.Stop();
        _displayValue = clamped;
        _animTarget   = clamped;
        if (_built) Update();
    }

    public void Celebrate()
    {
        if (_celebrateRing == null) return;
        var rotate = (RotateTransform)_celebrateRing.RenderTransform;

        var spin = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromMilliseconds(1200)))
        { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        var glow = new DoubleAnimationUsingKeyFrames();
        glow.KeyFrames.Add(new LinearDoubleKeyFrame(0,   KeyTime.FromPercent(0)));
        glow.KeyFrames.Add(new LinearDoubleKeyFrame(1,   KeyTime.FromPercent(0.5)));
        glow.KeyFrames.Add(new LinearDoubleKeyFrame(0,   KeyTime.FromPercent(1)));
        glow.Duration = new Duration(TimeSpan.FromMilliseconds(1200));

        rotate.BeginAnimation(RotateTransform.AngleProperty, spin);
        _celebrateRing.BeginAnimation(OpacityProperty, glow);
    }

    private void RetargetValue(double newValue)
    {
        double clamped = Math.Max(0, Math.Min(100, newValue));
        _animTarget = clamped;

        if (!_built) { _displayValue = clamped; return; }

        // Écart négligeable (ex. fluctuation de monitoring qui ne change pas le chiffre arrondi) :
        // pas besoin de lancer un timer d'animation pour ça.
        if (Math.Abs(clamped - _displayValue) < 0.05) { _displayValue = clamped; Update(); return; }

        _animStartValue = _displayValue;
        _animStartTime  = DateTime.UtcNow;
        // Durée proportionnelle à l'écart, plafonnée : un petit ajustement de monitoring reste
        // réactif, un grand saut (premier affichage, écran de victoire) profite d'un vrai sweep visible.
        _animDuration = TimeSpan.FromMilliseconds(Math.Clamp(Math.Abs(_animTarget - _animStartValue) * 12, 200, 900));

        if (_animTimer == null)
        {
            _animTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            _animTimer.Tick += OnAnimTick;
        }
        _animTimer.Start();
    }

    private void OnAnimTick(object? sender, EventArgs e)
    {
        double t = _animDuration.TotalMilliseconds <= 0
            ? 1
            : (DateTime.UtcNow - _animStartTime).TotalMilliseconds / _animDuration.TotalMilliseconds;

        if (t >= 1)
        {
            _displayValue = _animTarget;
            _animTimer!.Stop();
            Update();
            AnimationSettled?.Invoke();
            return;
        }

        double eased = 1 - Math.Pow(1 - t, 3); // cubic ease-out
        _displayValue = _animStartValue + (_animTarget - _animStartValue) * eased;
        Update();
    }

    private void Update()
    {
        if (!_built) return;

        const double cx = 60, cy = 60, r = 48;
        double v = Math.Max(0, Math.Min(100, _displayValue));

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
