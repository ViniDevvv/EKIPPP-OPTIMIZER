using System.Management;

namespace EkipppOptimizer.Services;

// Détection des CPU hybrides Performance/Efficacité (Intel 12e-15e génération, Core Ultra) par
// nom de modèle uniquement — aucun accès mémoire bas niveau. Identifier avec certitude quels
// cœurs logiques exacts sont P ou E nécessiterait de parser à la main une structure Windows
// (GetSystemCpuSetInformation / EfficiencyClass) sur du matériel hybride réel pour vérifier les
// offsets : un bug de marshaling y serait un vrai risque mémoire (crash, lecture hors buffer), pas
// un simple raté cosmétique — et ce risque ne peut pas être validé sans la machine physique
// correspondante. On préfère donc une détection sûre à 100%, quitte à rester moins précis.
public class CorePinningService
{
    private static readonly string[] HybridPatterns =
    [
        "12th Gen Intel", "13th Gen Intel", "14th Gen Intel", "15th Gen Intel",
        "Core(TM) Ultra", "Core Ultra",
    ];

    public bool IsHybridCpu()
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
            foreach (var o in s.Get())
            {
                var name = o["Name"]?.ToString() ?? "";
                if (HybridPatterns.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
        }
        catch { }
        return false;
    }
}
