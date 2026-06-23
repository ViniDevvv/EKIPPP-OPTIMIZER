using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using EkipppOptimizer.ViewModels;

namespace EkipppOptimizer;

public partial class MainWindow : Window
{
    internal bool AllowClose { get; set; } = false;

    private ScrollViewer? _activePanel;

    private readonly ScrollViewer?[] _panels;

    public MainWindow()
    {
        InitializeComponent();

        _panels = new ScrollViewer?[]
        {
            Tab0Panel, Tab1Panel, Tab2Panel, Tab3Panel, Tab4Panel, Tab5Panel,
            Tab6Panel, Tab7Panel, Tab8Panel, Tab9Panel, Tab10Panel, Tab11Panel, Tab12Panel
        };

        _activePanel = Tab0Panel;

        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged += OnVmPropertyChanged;
            vm.ShowToast       = ShowToast;
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.SelectedTab)) return;
        if (sender is not MainViewModel vm) return;

        int idx = vm.SelectedTab;
        if (idx < 0 || idx >= _panels.Length) return;

        var newPanel = _panels[idx];
        if (newPanel == null || newPanel == _activePanel) return;

        // Update sidebar button styles
        UpdateNavStyles(idx);

        var oldPanel = _activePanel;
        _activePanel = newPanel;

        if (oldPanel != null)
        {
            var fadeOut = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(110)));
            fadeOut.Completed += (_, _) => oldPanel.Visibility = Visibility.Collapsed;
            oldPanel.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        newPanel.Opacity    = 0;
        newPanel.Visibility = Visibility.Visible;
        var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(180)));
        fadeIn.BeginTime = TimeSpan.FromMilliseconds(60);
        newPanel.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }

    private void UpdateNavStyles(int activeIdx)
    {
        var activeStyle  = (System.Windows.Style)FindResource("NavBtnActive");
        var defaultStyle = (System.Windows.Style)FindResource("NavBtn");

        Button?[] btns = [Btn0, Btn1, Btn2, Btn3, Btn4, Btn5,
                          Btn6, Btn7, Btn8, Btn9, Btn10, Btn11, Btn12];
        for (int i = 0; i < btns.Length; i++)
        {
            if (btns[i] != null)
                btns[i]!.Style = i == activeIdx ? activeStyle : defaultStyle;
        }
    }

    private void ShowToast(string title, string message)
    {
        Dispatcher.Invoke(() =>
        {
            ToastTitle.Text = title;
            ToastMsg.Text   = message;

            var show = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(250)));
            ToastBorder.BeginAnimation(OpacityProperty, show);

            var hide = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(400)));
            hide.BeginTime = TimeSpan.FromSeconds(3);
            ToastBorder.BeginAnimation(OpacityProperty, hide);
        });
    }

    private double _restoreLeft, _restoreTop, _restoreWidth, _restoreHeight;
    private bool _isFullscreen = false;

    private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleFullscreen();
            return;
        }
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;

        if (_isFullscreen)
        {
            // Récupère la position souris AVANT de restaurer
            var screenPos = PointToScreen(e.GetPosition(this));
            double relX = screenPos.X / ActualWidth; // position relative dans la fenêtre (0..1)
            ToggleFullscreen();
            // Repositionne la fenêtre pour que la souris reste sous le curseur
            Left = screenPos.X - _restoreWidth * relX;
            Top  = screenPos.Y - 20;
        }
        try { DragMove(); } catch { }
    }

    private void ToggleFullscreen()
    {
        if (_isFullscreen)
        {
            Left   = _restoreLeft;
            Top    = _restoreTop;
            Width  = _restoreWidth;
            Height = _restoreHeight;
            _isFullscreen = false;
            FullscreenIcon.Text = "⛶";
            MainBorder.CornerRadius = new System.Windows.CornerRadius(14);
        }
        else
        {
            _restoreLeft   = Left;
            _restoreTop    = Top;
            _restoreWidth  = Width;
            _restoreHeight = Height;

            var area = System.Windows.SystemParameters.WorkArea;
            Left   = area.Left;
            Top    = area.Top;
            Width  = area.Width;
            Height = area.Height;
            _isFullscreen = true;
            FullscreenIcon.Text = "❐";
            MainBorder.CornerRadius = new System.Windows.CornerRadius(0);
        }
    }

    private void FullscreenClick(object sender, RoutedEventArgs e) => ToggleFullscreen();

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
    }

    private void HideToTray()
    {
        Hide();
        if (Application.Current is App app) app.ShowTrayBalloon();
    }

    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseClick(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app) app.ExplicitShutdown();
    }

    private void OpenNetworkSettings_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:network") { UseShellExecute = true }); }
        catch { try { Process.Start("ncpa.cpl"); } catch { } }
    }
}
