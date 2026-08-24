using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using EkipppOptimizer.ViewModels;

namespace EkipppOptimizer;

public partial class MainWindow : Window
{
    internal bool AllowClose { get; set; } = false;

    private ScrollViewer? _activePanel;

    private readonly ScrollViewer?[] _panels;

    public MainWindow(bool isFirstLaunch = false)
    {
        InitializeComponent();

        _panels = new ScrollViewer?[]
        {
            Tab0Panel, Tab1Panel, Tab2Panel, Tab3Panel, Tab4Panel, Tab5Panel,
            Tab6Panel, Tab7Panel, Tab8Panel, Tab9Panel, Tab10Panel, Tab11Panel, Tab12Panel,
            Tab13Panel
        };

        _activePanel = Tab0Panel;
        VictorySoundToggle.Content = _victorySoundEnabled ? "🔊" : "🔇";

        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged += OnVmPropertyChanged;
            vm.ShowToast             = ShowToast;
            vm.ShowVictory           = ShowVictory;
            vm.CelebratePerfectScore = () => DashboardScoreGauge.Celebrate();
            vm.SetFirstLaunch(isFirstLaunch);
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
                          Btn6, Btn7, Btn8, Btn9, Btn10, Btn11, Btn12, Btn13];
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

    // ══════════════════════════════════════════════════════════════════════════
    // ÉCRAN DE VICTOIRE — fin d'optimisation 1 clic
    // ══════════════════════════════════════════════════════════════════════════
    private int  _victoryBefore, _victoryAfter;
    private long _victoryFreed;

    private void ShowVictory(int before, int after, long freedBytes)
    {
        Dispatcher.Invoke(() =>
        {
            _victoryBefore = before;
            _victoryAfter  = after;
            _victoryFreed  = freedBytes;

            int delta = after - before;
            VictoryDeltaText.Text = delta > 0 ? $"+{delta} points" : delta < 0 ? $"{delta} points" : "Score stable";
            VictoryDeltaText.Foreground = new SolidColorBrush(delta >= 0
                ? Color.FromRgb(0x22, 0xC5, 0x5E)
                : Color.FromRgb(0xF8, 0x71, 0x71));
            VictoryFreedText.Text = $"{FormatBytesShare(freedBytes)} libérés durant cette optimisation.";

            VictoryOverlay.Visibility = Visibility.Visible;
            VictoryOverlay.Opacity    = 0;
            VictoryOverlay.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(280))));

            VictoryGauge.ArcBrush = new SolidColorBrush(GradeColorFor(after));
            // L'anneau anime lui-même le sweep before → after (mécanisme partagé par toutes les
            // jauges de l'app) — on se contente de le repositionner sur "before" sans animation.
            VictoryGauge.SetInstant(before);
            VictoryGauge.AnimationSettled -= OnVictoryGaugeSettled;
            VictoryGauge.AnimationSettled += OnVictoryGaugeSettled;
            VictoryGauge.Value = after;
        });
    }

    // Déclenché à l'instant précis où l'anneau termine sa course — confettis, onde de choc,
    // micro-secousse et chime arrivent tous ensemble, synchronisés sur le "verdict" final.
    private void OnVictoryGaugeSettled()
    {
        VictoryGauge.AnimationSettled -= OnVictoryGaugeSettled;
        int delta = _victoryAfter - _victoryBefore;
        SpawnConfetti(delta);
        SpawnShockwave();
        ShakeVictoryCard();
        PlayVictoryChime();
    }

    private void SpawnShockwave()
    {
        VictoryShockwave.Opacity = 0;
        ((ScaleTransform)VictoryShockwave.RenderTransform).ScaleX = 0.8;
        ((ScaleTransform)VictoryShockwave.RenderTransform).ScaleY = 0.8;

        var fade  = new DoubleAnimationUsingKeyFrames { Duration = new Duration(TimeSpan.FromMilliseconds(500)) };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromPercent(0)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0,   KeyTime.FromPercent(1)));
        var scale = new DoubleAnimation(0.8, 1.6, new Duration(TimeSpan.FromMilliseconds(500)))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };

        VictoryShockwave.BeginAnimation(OpacityProperty, fade);
        var t = (ScaleTransform)VictoryShockwave.RenderTransform;
        t.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        t.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
    }

    private void ShakeVictoryCard()
    {
        var shake = new DoubleAnimationUsingKeyFrames { Duration = new Duration(TimeSpan.FromMilliseconds(220)) };
        shake.KeyFrames.Add(new LinearDoubleKeyFrame(0,  KeyTime.FromPercent(0)));
        shake.KeyFrames.Add(new LinearDoubleKeyFrame(-3, KeyTime.FromPercent(0.2)));
        shake.KeyFrames.Add(new LinearDoubleKeyFrame(3,  KeyTime.FromPercent(0.45)));
        shake.KeyFrames.Add(new LinearDoubleKeyFrame(-2, KeyTime.FromPercent(0.7)));
        shake.KeyFrames.Add(new LinearDoubleKeyFrame(0,  KeyTime.FromPercent(1)));

        var translate = new TranslateTransform();
        VictoryCardContent.RenderTransform = translate;
        translate.BeginAnimation(TranslateTransform.XProperty, shake);
    }

    private static bool _victorySoundEnabled = LoadVictorySoundPref();

    private static bool LoadVictorySoundPref()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\EKIPPP-OPTIMIZER\App");
            return !(k?.GetValue("VictorySoundDisabled") is int v && v == 1);
        }
        catch { return true; }
    }

    private void ToggleVictorySound_Click(object sender, RoutedEventArgs e)
    {
        _victorySoundEnabled = !_victorySoundEnabled;
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"SOFTWARE\EKIPPP-OPTIMIZER\App");
            k.SetValue("VictorySoundDisabled", _victorySoundEnabled ? 0 : 1, Microsoft.Win32.RegistryValueKind.DWord);
        }
        catch { }
        VictorySoundToggle.Content = _victorySoundEnabled ? "🔊" : "🔇";
    }

    private void PlayVictoryChime()
    {
        if (!_victorySoundEnabled) return;
        try
        {
            using var ms     = BuildChimeWav();
            using var player = new System.Media.SoundPlayer(ms);
            player.Play();
        }
        catch { }
    }

    // Deux notes montantes synthétisées en mémoire (aucun asset audio à embarquer) — chime court
    // et discret joué à l'instant où le score "atterrit".
    private static System.IO.MemoryStream BuildChimeWav()
    {
        const int sampleRate = 44100;
        double[] freqs = [660, 990];
        const double noteDur = 0.11;
        var samples = new List<short>();

        foreach (var f in freqs)
        {
            int n = (int)(sampleRate * noteDur);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / sampleRate;
                double envelope = 1.0 - (double)i / n;
                double v = Math.Sin(2 * Math.PI * f * t) * envelope * 0.25;
                samples.Add((short)(v * short.MaxValue));
            }
        }

        var ms = new System.IO.MemoryStream();
        using (var bw = new System.IO.BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            int dataSize = samples.Count * 2;
            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + dataSize);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)1);
            bw.Write(sampleRate);
            bw.Write(sampleRate * 2);
            bw.Write((short)2);
            bw.Write((short)16);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            bw.Write(dataSize);
            foreach (var s in samples) bw.Write(s);
        }
        ms.Position = 0;
        return ms;
    }

    private void VictoryOverlay_MouseDown(object sender, MouseButtonEventArgs e) => CloseVictory();
    private void VictoryCard_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;
    private void VictoryContinue_Click(object sender, RoutedEventArgs e) => CloseVictory();

    private void CloseVictory()
    {
        var fadeOut = new DoubleAnimation(VictoryOverlay.Opacity, 0, new Duration(TimeSpan.FromMilliseconds(220)));
        fadeOut.Completed += (_, _) => VictoryOverlay.Visibility = Visibility.Collapsed;
        VictoryOverlay.BeginAnimation(OpacityProperty, fadeOut);
    }

    private static readonly Color[] ConfettiPalette =
    [
        Color.FromRgb(0x7C, 0x3A, 0xED),
        Color.FromRgb(0xA7, 0x8B, 0xFA),
        Color.FromRgb(0x22, 0xC5, 0x5E),
        Color.FromRgb(0xFD, 0xE0, 0x47),
    ];

    // Nombre de particules proportionnel au gain réel : un petit gain reste discret,
    // un gros gain déclenche la salve complète — la célébration reste honnête et ne lasse pas.
    private void SpawnConfetti(int delta)
    {
        ConfettiCanvas.Children.Clear();
        if (delta <= 0) return;

        int count = delta switch { <= 5 => 15, <= 15 => 40, _ => 80 };
        double w  = Math.Max(VictoryOverlay.ActualWidth, 500);
        var rnd   = Random.Shared;

        for (int i = 0; i < count; i++)
        {
            double size = 5 + rnd.NextDouble() * 4;
            Shape shape = rnd.Next(2) == 0
                ? new Ellipse { Width = size, Height = size }
                : new System.Windows.Shapes.Rectangle { Width = size, Height = size };
            shape.Fill = new SolidColorBrush(ConfettiPalette[rnd.Next(ConfettiPalette.Length)]);
            shape.RenderTransformOrigin = new Point(0.5, 0.5);

            double startX = rnd.NextDouble() * w;
            Canvas.SetLeft(shape, startX);
            Canvas.SetTop(shape, -16);

            var translate = new TranslateTransform();
            var rotate    = new RotateTransform();
            shape.RenderTransform = new TransformGroup { Children = { rotate, translate } };
            ConfettiCanvas.Children.Add(shape);

            double duration = 1.1 + rnd.NextDouble() * 0.7;
            var delayTs      = TimeSpan.FromSeconds(rnd.NextDouble() * 0.35);

            var fall = new DoubleAnimation(0, 240 + rnd.NextDouble() * 220, TimeSpan.FromSeconds(duration))
            { BeginTime = delayTs, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            var drift = new DoubleAnimation(0, (rnd.NextDouble() - 0.5) * 140, TimeSpan.FromSeconds(duration))
            { BeginTime = delayTs };
            var spin = new DoubleAnimation(0, 360 * (2 + rnd.Next(3)), TimeSpan.FromSeconds(duration))
            { BeginTime = delayTs };
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(duration * 0.3))
            { BeginTime = delayTs + TimeSpan.FromSeconds(duration * 0.7) };

            var capturedShape = shape;
            fade.Completed += (_, _) => ConfettiCanvas.Children.Remove(capturedShape);

            translate.BeginAnimation(TranslateTransform.YProperty, fall);
            translate.BeginAnimation(TranslateTransform.XProperty, drift);
            rotate.BeginAnimation(RotateTransform.AngleProperty, spin);
            shape.BeginAnimation(OpacityProperty, fade);
        }
    }

    // ── Carte de résultat partageable (Discord/réseaux) ──────────────────────
    private void CopyShareCard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetImage(RenderShareCard(_victoryBefore, _victoryAfter, _victoryFreed));
            ShowToast("Partage", "Image copiée dans le presse-papier — colle-la dans Discord ✓");
        }
        catch (Exception ex) { ShowToast("Partage", $"Erreur : {ex.Message}"); }
    }

    private void SaveShareCard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var rtb = RenderShareCard(_victoryBefore, _victoryAfter, _victoryFreed);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var path    = System.IO.Path.Combine(desktop, $"EKIPPP-Score-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Create))
                encoder.Save(fs);
            ShowToast("Partage", "Image enregistrée sur le Bureau ✓");
        }
        catch (Exception ex) { ShowToast("Partage", $"Erreur : {ex.Message}"); }
    }

    private static RenderTargetBitmap RenderShareCard(int before, int after, long freedBytes)
    {
        const int size = 1080;
        var card = BuildShareCard(before, after, freedBytes);
        card.Measure(new Size(size, size));
        card.Arrange(new Rect(0, 0, size, size));
        card.UpdateLayout();
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(card);
        return rtb;
    }

    private static Border BuildShareCard(int before, int after, long freedBytes)
    {
        var root = new Border
        {
            Width  = 1080,
            Height = 1080,
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(0x0D, 0x0A, 0x1A), 0),
                    new GradientStop(Color.FromRgb(0x1A, 0x10, 0x28), 0.5),
                    new GradientStop(Color.FromRgb(0x0D, 0x0A, 0x1A), 1),
                },
                new Point(0, 0), new Point(1, 1))
        };

        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center
        };

        try
        {
            stack.Children.Add(new System.Windows.Controls.Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png")),
                Width = 140, Height = 140,
                Margin = new Thickness(0, 0, 0, 28),
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }
        catch { }

        stack.Children.Add(new TextBlock
        {
            Text = "EKIPPP-OPTIMISATEUR", FontSize = 30, FontWeight = FontWeights.Black,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE2, 0xD9, 0xF3)),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 50)
        });

        var scoreRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        scoreRow.Children.Add(ShareScoreBlock(before.ToString(), "AVANT", Color.FromRgb(0x6B, 0x72, 0x80)));
        scoreRow.Children.Add(new TextBlock
        {
            Text = "→", FontSize = 60, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED)),
            Margin = new Thickness(30, 0, 30, 0), VerticalAlignment = VerticalAlignment.Center
        });
        scoreRow.Children.Add(ShareScoreBlock(after.ToString(), "APRÈS", GradeColorFor(after)));
        stack.Children.Add(scoreRow);

        stack.Children.Add(new Border
        {
            Height = 1, Width = 300, Margin = new Thickness(0, 50, 0, 50),
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x1F, 0x45))
        });

        int delta = after - before;
        stack.Children.Add(new TextBlock
        {
            Text = (delta >= 0 ? $"+{delta} points" : $"{delta} points") + $" · {FormatBytesShare(freedBytes)} libérés",
            FontSize = 26, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xD0)),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        root.Child = stack;
        return root;
    }

    private static StackPanel ShareScoreBlock(string number, string label, Color color)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(new TextBlock
        {
            Text = number, FontSize = 110, FontWeight = FontWeights.Black,
            Foreground = new SolidColorBrush(color), HorizontalAlignment = HorizontalAlignment.Center
        });
        sp.Children.Add(new TextBlock
        {
            Text = label, FontSize = 18, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x6B, 0x9A)),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0)
        });
        return sp;
    }

    private static Color GradeColorFor(int score) => score switch
    {
        >= 90 => Color.FromRgb(0x4A, 0xDE, 0x80),
        >= 80 => Color.FromRgb(0x86, 0xEF, 0xAC),
        >= 70 => Color.FromRgb(0xFD, 0xE0, 0x47),
        >= 55 => Color.FromRgb(0xFB, 0x92, 0x3C),
        >= 40 => Color.FromRgb(0xF8, 0x71, 0x71),
        _     => Color.FromRgb(0xEF, 0x44, 0x44),
    };

    private static string FormatBytesShare(long b) =>
        b >= 1L << 30 ? $"{b / (1024.0 * 1024 * 1024):F1} Go"
      : b >= 1L << 20 ? $"{b / (1024.0 * 1024):F0} Mo"
      : b > 0         ? $"{b / 1024.0:F0} Ko"
      : "0 o";
}
