using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace EkipppOptimizer;

// Insere un espace fin (U+2009) entre chaque caractere d'un TextBlock -- le meme effet de
// "letter-spacing" que les eyebrow labels de Stripe/Linear/Vercel, pose une fois dans les styles
// SectionTitle/MetricLabel plutot que repete partout ou ces styles sont utilises.
public static class TrackingText
{
    private const char ThinSpace = ' ';

    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(TrackingText),
            new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject d, bool value) => d.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb || e.NewValue is not true) return;

        tb.Loaded += (_, _) => Apply(tb);
        var descriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        descriptor?.AddValueChanged(tb, (_, _) => Apply(tb));
    }

    private static void Apply(TextBlock tb)
    {
        // Les TextBlocks composes de plusieurs <Run> (souvent avec bindings imbriques) ne passent
        // pas par la propriete Text simple -- les modifier ecraserait leur structure. On ne touche
        // qu'aux TextBlocks a contenu texte simple, seul cas ou c'est sans risque.
        if (tb.Inlines.Count > 1) return;

        var text = tb.Text;
        if (string.IsNullOrEmpty(text) || text.Contains(ThinSpace)) return; // deja espace -- evite la boucle infinie

        tb.Text = string.Join(ThinSpace.ToString(), text.ToCharArray());
    }
}
