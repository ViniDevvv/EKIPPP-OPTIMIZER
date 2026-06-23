using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EkipppOptimizer;

public partial class TweakRow : ContentControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(TweakRow), new PropertyMetadata("", Rebuild));
    public static readonly DependencyProperty SubProperty =
        DependencyProperty.Register(nameof(Sub), typeof(string), typeof(TweakRow), new PropertyMetadata("", Rebuild));
    public static readonly DependencyProperty IsCheckedProperty =
        DependencyProperty.Register(nameof(IsChecked), typeof(bool), typeof(TweakRow),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, Rebuild));
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(TweakRow), new PropertyMetadata(null, Rebuild));
    public static readonly DependencyProperty IsLastProperty =
        DependencyProperty.Register(nameof(IsLast), typeof(bool), typeof(TweakRow), new PropertyMetadata(false, Rebuild));

    public string    Title     { get => (string)GetValue(TitleProperty);     set => SetValue(TitleProperty,     value); }
    public string    Sub       { get => (string)GetValue(SubProperty);       set => SetValue(SubProperty,       value); }
    public bool      IsChecked { get => (bool)GetValue(IsCheckedProperty);   set => SetValue(IsCheckedProperty, value); }
    public ICommand? Command   { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty,  value); }
    public bool      IsLast    { get => (bool)GetValue(IsLastProperty);      set => SetValue(IsLastProperty,    value); }

    private static void Rebuild(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TweakRow)d).Build();

    public TweakRow() { Loaded += (_, _) => Build(); }

    private static SolidColorBrush Brush(string hex)
        => new((Color)ColorConverter.ConvertFromString(hex));

    private void Build()
    {
        var accent     = Brush("#A78BFA");
        var txtPrimary = Brush("#F0E6FF");
        var txtSecond  = Brush("#9B8BB0");
        var txtMuted   = Brush("#5C4F72");
        var trackOff   = Brush("#1E1232");
        var trackOn    = Brush("#4C1D95");
        var border1    = Brush("#2D1F45");
        var accentBtn  = Brush("#7C3AED");

        var thumb = new Border
        {
            Width = 18, Height = 18, CornerRadius = new CornerRadius(9),
            Background = IsChecked ? accent : txtMuted,
            HorizontalAlignment = IsChecked ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Margin = IsChecked ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0)
        };
        var track = new Border
        {
            Width = 44, Height = 24, CornerRadius = new CornerRadius(12),
            Background = IsChecked ? trackOn : trackOff,
            BorderBrush = IsChecked ? accentBtn : border1,
            BorderThickness = new Thickness(1),
            Child = thumb
        };

        var tpl = new ControlTemplate(typeof(Button));
        var cp  = new FrameworkElementFactory(typeof(ContentPresenter));
        tpl.VisualTree = cp;

        var btn = new Button
        {
            Content = track, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand, Command = Command,
            VerticalAlignment = VerticalAlignment.Center,
            Template = tpl
        };

        var titleTb = new TextBlock { Text = Title, FontWeight = FontWeights.SemiBold, Foreground = txtPrimary, FontSize = 13 };
        var subTb   = new TextBlock { Text = Sub,   Foreground = txtSecond, FontSize = 11 };
        var labels  = new StackPanel();
        labels.Children.Add(titleTb);
        labels.Children.Add(subTb);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(labels, 0);
        Grid.SetColumn(btn,    1);
        grid.Children.Add(labels);
        grid.Children.Add(btn);

        Content = new Border
        {
            Background = Brush("#12091C"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = IsLast ? new Thickness(0) : new Thickness(0, 0, 0, 8),
            Child = grid
        };
    }
}
