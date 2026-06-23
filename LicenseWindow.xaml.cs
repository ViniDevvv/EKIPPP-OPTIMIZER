using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using EkipppOptimizer.Services;

namespace EkipppOptimizer;

public partial class LicenseWindow : Window
{
    private readonly LicenseService _license;
    private bool _activating = false;

    public LicenseWindow(LicenseService license)
    {
        _license = license;
        InitializeComponent();
        Loaded += (_, _) => KeyInput.Focus();
    }

    private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); } catch { }
    }

    private void KeyInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        // Auto-format XXXX-XXXX-XXXX-XXXX
        var raw = KeyInput.Text.Replace("-", "").ToUpperInvariant();
        if (raw.Length > 16) raw = raw[..16];

        var parts = new List<string>();
        for (int i = 0; i < raw.Length; i += 4)
            parts.Add(raw[i..Math.Min(i + 4, raw.Length)]);
        var formatted = string.Join("-", parts);

        if (formatted != KeyInput.Text)
        {
            KeyInput.TextChanged -= KeyInput_TextChanged;
            int pos = Math.Max(0, Math.Min(KeyInput.CaretIndex + formatted.Length - KeyInput.Text.Length, formatted.Length));
            KeyInput.Text = formatted;
            KeyInput.CaretIndex = pos;
            KeyInput.TextChanged += KeyInput_TextChanged;
        }

        ActivateBtn.IsEnabled = raw.Length == 16 && !_activating;
        StatusMsg.Visibility = Visibility.Collapsed;
    }

    private void KeyInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ActivateBtn.IsEnabled)
            _ = DoActivateAsync();
    }

    private void ActivateBtn_Click(object sender, RoutedEventArgs e)
        => _ = DoActivateAsync();

    private async Task DoActivateAsync()
    {
        if (_activating) return;
        _activating = true;
        ActivateBtn.IsEnabled = false;
        ShowStatus("Vérification en cours…", "#A78BFA");

        var result = await _license.ActivateAsync(KeyInput.Text);
        _activating = false;

        if (result.Success)
        {
            ShowStatus("Licence activée ✓  Bienvenue !", "#34D399");
            await Task.Delay(900);
            DialogResult = true;
            Close();
            return;
        }

        var msg = result.Status switch
        {
            LicenseStatus.InvalidKey   => "Clé invalide — vérifiez et réessayez.",
            LicenseStatus.Revoked      => "Cette licence a été révoquée.",
            LicenseStatus.Expired      => "Cette licence est expirée.",
            LicenseStatus.MaxMachines  => "Limite de PC atteinte pour cette licence.",
            LicenseStatus.NetworkError => "Connexion impossible — vérifiez votre Internet.",
            _                          => "Erreur inconnue, contactez le support.",
        };
        ShowStatus(msg, "#F87171");
        ActivateBtn.IsEnabled = KeyInput.Text.Replace("-", "").Length == 16;
    }

    private void ShowStatus(string msg, string hex)
    {
        StatusMsg.Text = msg;
        StatusMsg.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(hex));
        StatusMsg.Visibility = Visibility.Visible;
    }

    // Remplace l'URL par ton site de vente
    private void BuyLink_Click(object sender, RoutedEventArgs e)
    {
        const string url = "https://votre-site.com/licence"; // ← à modifier
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }
}
