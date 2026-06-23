using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EkipppOptimizer;

public partial class SparklineChart : ContentControl
{
    public static readonly DependencyProperty LatestValueProperty =
        DependencyProperty.Register(nameof(LatestValue), typeof(double), typeof(SparklineChart),
            new PropertyMetadata(0.0, OnValueChanged));
    public static readonly DependencyProperty StrokeBrushProperty =
        DependencyProperty.Register(nameof(StrokeBrush), typeof(Brush), typeof(SparklineChart),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xA7, 0x8B, 0xFA))));

    public double LatestValue { get => (double)GetValue(LatestValueProperty); set => SetValue(LatestValueProperty, value); }
    public Brush  StrokeBrush { get => (Brush)GetValue(StrokeBrushProperty);  set => SetValue(StrokeBrushProperty,  value); }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SparklineChart)d).AddPoint((double)e.NewValue);

    private readonly Queue<double> _history = new();
    private const int MaxPoints = 50;

    private Polyline? _line;
    private Polygon?  _fill;
    private Canvas?   _canvas;

    public SparklineChart() { Loaded += (_, _) => Build(); }

    private void Build()
    {
        _fill = new Polygon { Opacity = 0.15, Fill = StrokeBrush };
        _line = new Polyline
        {
            StrokeThickness = 1.8,
            StrokeLineJoin  = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round,
            Stroke = StrokeBrush
        };

        _canvas = new Canvas { ClipToBounds = true, Background = Brushes.Transparent };
        _canvas.Children.Add(_fill);
        _canvas.Children.Add(_line);
        Content = _canvas;

        SizeChanged += (_, _) => Redraw();
    }

    private void AddPoint(double v)
    {
        _history.Enqueue(Math.Max(0, v));
        while (_history.Count > MaxPoints) _history.Dequeue();
        Redraw();
    }

    private void Redraw()
    {
        if (_line == null || _canvas == null || _history.Count < 2) return;

        double w = _canvas.ActualWidth;
        double h = _canvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        var vals = _history.ToArray();
        double max = Math.Max(1, vals.Max());
        double step = w / (MaxPoints - 1);
        double padding = h * 0.05;

        var linePoints = new PointCollection();
        var fillPoints = new PointCollection();

        fillPoints.Add(new Point((vals.Length - 1) * step, h));
        fillPoints.Add(new Point(0, h));

        for (int i = 0; i < vals.Length; i++)
        {
            double x = i * step;
            double y = h - padding - vals[i] / max * (h - padding * 2);
            linePoints.Add(new Point(x, y));
            fillPoints.Add(new Point(x, y));
        }

        _line.Stroke = StrokeBrush;
        _fill!.Fill  = StrokeBrush;
        _line.Points = linePoints;
        _fill.Points  = fillPoints;
    }
}
