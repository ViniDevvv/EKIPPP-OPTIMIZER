using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace EkipppOptimizer;

public partial class TweakRow : ContentControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(TweakRow), new PropertyMetadata("", OnStaticChanged));
    public static readonly DependencyProperty SubProperty =
        DependencyProperty.Register(nameof(Sub), typeof(string), typeof(TweakRow), new PropertyMetadata("", OnStaticChanged));
    public static readonly DependencyProperty IsCheckedProperty =
        DependencyProperty.Register(nameof(IsChecked), typeof(bool), typeof(TweakRow),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnCheckedChanged));
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(TweakRow), new PropertyMetadata(null, OnStaticChanged));
    public static readonly DependencyProperty IsLastProperty =
        DependencyProperty.Register(nameof(IsLast), typeof(bool), typeof(TweakRow), new PropertyMetadata(false, OnStaticChanged));

    public string    Title     { get => (string)GetValue(TitleProperty);     set => SetValue(TitleProperty,     value); }
    public string    Sub       { get => (string)GetValue(SubProperty);       set => SetValue(SubProperty,       value); }
    public bool      IsChecked { get => (bool)GetValue(IsCheckedProperty);   set => SetValue(IsCheckedProperty, value); }
    public ICommand? Command   { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty,  value); }
    public bool      IsLast    { get => (bool)GetValue(IsLastProperty);      set => SetValue(IsLastProperty,    value); }

    private const double TrackW = 44, TrackH = 24, ThumbD = 18, Pad = 3;
    private const double ThumbTravel = TrackW - ThumbD - Pad * 2;
    private static readonly TimeSpan Dur = TimeSpan.FromMilliseconds(180);

    private static Color Col(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    private static readonly Color CAccent    = Col("#A78BFA");
    private static readonly Color CTxtPrim   = Col("#F0E6FF");
    private static readonly Color CTxtSec    = Col("#9B8BB0");
    private static readonly Color CTxtMuted  = Col("#5C4F72");
    private static readonly Color CTrackOff  = Col("#1E1232");
    private static readonly Color CTrackOn   = Col("#4C1D95");
    private static readonly Color CBorder    = Col("#2D1F45");
    private static readonly Color CAccentBtn = Col("#7C3AED");
    private static readonly Color CRowBg     = Col("#12091C");
    private static readonly Color CHoverBd   = Color.FromArgb(90, 124, 58, 237);
    private static readonly Color CNoBorder  = Colors.Transparent;

    // Éléments construits UNE SEULE fois (au premier Loaded), puis animés/mis à jour en place —
    // avant, tout l'arbre visuel était détruit et reconstruit à chaque clic, donc le toggle
    // "sautait" instantanément sans aucune transition. Ici le thumb glisse et les couleurs
    // fondent (ColorAnimation/DoubleAnimation), comme un vrai composant natif poli.
    private bool                _built;
    private Border?             _wrapper;
    private Border?             _track;
    private Border?             _thumb;
    private TranslateTransform? _thumbX;
    private SolidColorBrush?    _trackBg;
    private SolidColorBrush?    _trackBd;
    private SolidColorBrush?    _thumbBg;
    private SolidColorBrush?    _wrapperBd;
    private Button?             _btn;
    private TextBlock?          _titleTb;
    private TextBlock?          _subTb;

    public TweakRow() { Loaded += (_, _) => { if (!_built) Build(); }; }

    private static void OnStaticChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TweakRow)d).RefreshStatic();

    private static void OnCheckedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TweakRow)d).AnimateChecked((bool)e.NewValue);

    private void RefreshStatic()
    {
        if (!_built) return; // Build() lira les valeurs à jour au premier Loaded
        _titleTb!.Text = Title;
        _subTb!.Text   = Sub;
        _btn!.Command   = Command;
        _wrapper!.Margin = IsLast ? new Thickness(0) : new Thickness(0, 0, 0, 8);
    }

    private void Build()
    {
        _built = true;

        _trackBg = new SolidColorBrush(IsChecked ? CTrackOn : CTrackOff);
        _trackBd = new SolidColorBrush(IsChecked ? CAccentBtn : CBorder);
        _thumbBg = new SolidColorBrush(IsChecked ? CAccent : CTxtMuted);
        _thumbX  = new TranslateTransform(IsChecked ? ThumbTravel : 0, 0);

        _thumb = new Border
        {
            Width = ThumbD, Height = ThumbD, CornerRadius = new CornerRadius(ThumbD / 2),
            Background = _thumbBg,
            Margin = new Thickness(Pad, 0, 0, 0),
            RenderTransform = _thumbX,
            Effect = new DropShadowEffect { Color = Colors.Black, Opacity = .35, BlurRadius = 4, ShadowDepth = 1 }
        };

        _track = new Border
        {
            Width = TrackW, Height = TrackH, CornerRadius = new CornerRadius(TrackH / 2),
            Background = _trackBg, BorderBrush = _trackBd, BorderThickness = new Thickness(1),
            Child = _thumb
        };

        var tpl = new ControlTemplate(typeof(Button));
        tpl.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));

        _btn = new Button
        {
            Content = _track, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand, Command = Command, VerticalAlignment = VerticalAlignment.Center,
            Template = tpl, Focusable = false
        };

        _titleTb = new TextBlock { Text = Title, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(CTxtPrim), FontSize = 13 };
        _subTb   = new TextBlock { Text = Sub, Foreground = new SolidColorBrush(CTxtSec), FontSize = 11, Margin = new Thickness(0, 2, 0, 0) };
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(_titleTb);
        labels.Children.Add(_subTb);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(labels, 0);
        Grid.SetColumn(_btn, 1);
        grid.Children.Add(labels);
        grid.Children.Add(_btn);

        _wrapperBd = new SolidColorBrush(CNoBorder);
        _wrapper = new Border
        {
            Background = new SolidColorBrush(CRowBg),
            BorderBrush = _wrapperBd,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = IsLast ? new Thickness(0) : new Thickness(0, 0, 0, 8),
            Child = grid
        };
        _wrapper.MouseEnter += (_, _) => AnimateHover(true);
        _wrapper.MouseLeave += (_, _) => AnimateHover(false);

        Content = _wrapper;
    }

    private void AnimateChecked(bool isChecked)
    {
        if (!_built) return; // Build() n'a pas encore tourné, il lira IsChecked à ce moment-là
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _thumbX!.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(isChecked ? ThumbTravel : 0, Dur) { EasingFunction = ease });
        _trackBg!.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(isChecked ? CTrackOn : CTrackOff, Dur));
        _trackBd!.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(isChecked ? CAccentBtn : CBorder, Dur));
        _thumbBg!.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(isChecked ? CAccent : CTxtMuted, Dur));
    }

    private void AnimateHover(bool hover)
        => _wrapperBd?.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(hover ? CHoverBd : CNoBorder, TimeSpan.FromMilliseconds(150)));
}
