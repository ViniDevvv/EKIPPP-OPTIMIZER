using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace EkipppOptimizer;

public partial class SplashWindow : Window
{
    private readonly bool _isFirstLaunch;
    private readonly TaskCompletionSource<bool> _tcs = new();
    private bool _accepted = false;

    public Task<bool> CompletionTask => _tcs.Task;

    public SplashWindow(bool isFirstLaunch)
    {
        _isFirstLaunch = isFirstLaunch;
        InitializeComponent();
    }

    // Drag pour déplacer la fenêtre
    private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); } catch { }
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        // S'assure que la fenêtre est bien centrée après le premier rendu
        CenterOnScreen();

        // Anime la barre de 0 → 1 en 1.8 s
        var anim = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromSeconds(1.8)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        FillScale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        LoadingLabel.Text = "Initialisation…";

        await Task.Delay(1000);
        LoadingLabel.Text = "Chargement des modules…";

        await Task.Delay(800);
        LoadingLabel.Text = "Prêt !";

        await Task.Delay(400);

        if (_isFirstLaunch)
        {
            LoadingLabel.Text = "Bienvenue — veuillez accepter les conditions.";
            TermsSection.Visibility = Visibility.Visible;

            // Re-centrer après que la hauteur ait augmenté (les CGU s'affichent)
            await Dispatcher.InvokeAsync(CenterOnScreen, System.Windows.Threading.DispatcherPriority.Loaded);
        }
        else
        {
            _tcs.TrySetResult(true);
            Close();
        }
    }

    private void CenterOnScreen()
    {
        var area = SystemParameters.WorkArea;
        double newTop  = area.Top  + (area.Height - ActualHeight) / 2;
        double newLeft = area.Left + (area.Width  - ActualWidth)  / 2;
        Top  = Math.Max(area.Top,  newTop);
        Left = Math.Max(area.Left, newLeft);
    }

    private void AcceptCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        AcceptBtn.IsEnabled = AcceptCheckbox.IsChecked == true;
    }

    private void AcceptBtn_Click(object sender, RoutedEventArgs e)
    {
        _accepted = true;
        _tcs.TrySetResult(true);
        Close();
    }

    private void DeclineBtn_Click(object sender, RoutedEventArgs e)
    {
        _tcs.TrySetResult(false);
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _tcs.TrySetResult(!_isFirstLaunch || _accepted);
        base.OnClosed(e);
    }
}
