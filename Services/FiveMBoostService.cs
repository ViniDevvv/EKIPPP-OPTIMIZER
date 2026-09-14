using System.Diagnostics;
using System.IO;
using System.Management;
using System.Xml.Linq;

namespace EkipppOptimizer.Services;

public record FiveMStatus(
    bool FiveMInstalled,
    bool SettingsFound,
    string? SettingsPath,
    bool BackupExists,
    bool GameProcessRunning);

public record GraphicsApplyResult(bool Success, List<string> ChangedLabels, string Message);

// Optimise les FPS réels sur FiveM : réglages graphiques en jeu (le levier le plus efficace,
// bien avant les tweaks Windows) + priorité process immédiate. Le fichier settings.xml de FiveM
// partage le même format que celui de GTA V — on ne modifie QUE les éléments déjà présents dans
// le fichier (correspondance par mot-clé sur le nom, insensible à la casse) : si une balise
// attendue n'existe pas sous ce nom dans une version donnée du jeu/launcher, on l'ignore
// silencieusement plutôt que de fabriquer un élément au schéma incertain. Sauvegarde automatique
// avant toute écriture — restauration en un clic.
public class FiveMBoostService
{
    private static readonly (Func<string, bool> Match, string Value, string Label)[] GraphicsRules =
    [
        (n => n.Contains("msaa", StringComparison.OrdinalIgnoreCase), "0", "Anti-aliasing MSAA"),
        (n => n.Contains("reflection", StringComparison.OrdinalIgnoreCase) && n.Contains("resolution", StringComparison.OrdinalIgnoreCase), "0", "Résolution des reflets"),
        (n => n.Contains("reflection", StringComparison.OrdinalIgnoreCase), "1", "Qualité des reflets"),
        (n => n.Contains("shadow", StringComparison.OrdinalIgnoreCase) && n.Contains("quality", StringComparison.OrdinalIgnoreCase), "1", "Qualité des ombres"),
        (n => n.Contains("ssao", StringComparison.OrdinalIgnoreCase), "1", "Occlusion ambiante (SSAO)"),
        (n => n.Contains("particle", StringComparison.OrdinalIgnoreCase) && n.Contains("quality", StringComparison.OrdinalIgnoreCase), "1", "Qualité des particules"),
        (n => n.Contains("grass", StringComparison.OrdinalIgnoreCase) && n.Contains("quality", StringComparison.OrdinalIgnoreCase), "1", "Qualité de l'herbe"),
        (n => n.Contains("tessellation", StringComparison.OrdinalIgnoreCase), "1", "Tessellation"),
        (n => n.Contains("dof", StringComparison.OrdinalIgnoreCase), "0", "Profondeur de champ"),
        (n => n.Contains("motionblur", StringComparison.OrdinalIgnoreCase), "0", "Flou de mouvement"),
        (n => n.Contains("distance", StringComparison.OrdinalIgnoreCase) && n.Contains("scale", StringComparison.OrdinalIgnoreCase), "0.500000", "Distance d'affichage"),
        (n => n.Contains("variety", StringComparison.OrdinalIgnoreCase), "0.500000", "Variété piétons/véhicules"),
        (n => n.Contains("population", StringComparison.OrdinalIgnoreCase) && n.Contains("density", StringComparison.OrdinalIgnoreCase), "0.500000", "Densité de population"),
        (n => n.Contains("extended", StringComparison.OrdinalIgnoreCase), "0.500000", "Distance/population étendue"),
        (n => n.Contains("ultra", StringComparison.OrdinalIgnoreCase), "0", "Options Ultra"),
        (n => n.Contains("water", StringComparison.OrdinalIgnoreCase) && n.Contains("quality", StringComparison.OrdinalIgnoreCase), "1", "Qualité de l'eau"),
        (n => (n.Contains("light", StringComparison.OrdinalIgnoreCase) || n.Contains("lighting", StringComparison.OrdinalIgnoreCase)) && n.Contains("quality", StringComparison.OrdinalIgnoreCase), "1", "Qualité de l'éclairage"),
        (n => n.Contains("city", StringComparison.OrdinalIgnoreCase) && n.Contains("density", StringComparison.OrdinalIgnoreCase), "0.500000", "Densité de la ville"),
        (n => n.Contains("txaa", StringComparison.OrdinalIgnoreCase) || n.Contains("fxaa", StringComparison.OrdinalIgnoreCase), "0", "Anti-aliasing additionnel"),
        (n => n.Contains("lodbias", StringComparison.OrdinalIgnoreCase), "0.000000", "Niveau de détail (LOD)"),
    ];

    private static string DataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FiveM", "FiveM.app");

    // Le lanceur FiveM.exe vit dans %LOCALAPPDATA%\FiveM\, UN NIVEAU AU-DESSUS de FiveM.app (qui ne
    // contient que les données/cache) — vérifié en conditions réelles, FiveM.app\FiveM.exe n'existe
    // jamais. Erreur d'hypothèse initiale qui cassait silencieusement le plein écran ciblé et le
    // GPU dédié (chemin introuvable → aucun des deux tweaks n'était réellement appliqué).
    private static string LauncherPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FiveM", "FiveM.exe");

    // FiveM tourne sur le moteur GTA V "legacy" et réutilise donc son fichier de réglages
    // graphiques natif — PAS un fichier propre à FiveM sous AppData. Il vit dans le dossier
    // Documents (déjà correctement redirigé par SpecialFolder.MyDocuments si OneDrive/Documents
    // est déplacé), exactement comme le vrai jeu Rockstar. Vérifié en conditions réelles :
    // AppData\Local\FiveM\FiveM.app\data\GTA V\settings.xml n'existe jamais, même après des
    // heures de jeu — c'est Documents\Rockstar Games\GTA V\settings.xml qui contient le vrai
    // <graphics> (MSAA, ShadowQuality, ReflectionQuality, etc.).
    private static string RockstarGamesFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Rockstar Games");

    private static string BackupPath(string settingsPath) => settingsPath + ".ekippp-backup";

    public string? FindSettingsXml()
    {
        try
        {
            var expected = Path.Combine(RockstarGamesFolder, "GTA V", "settings.xml");
            if (File.Exists(expected)) return expected;

            if (!Directory.Exists(RockstarGamesFolder)) return null;

            // Filet de sécurité si Rockstar a renommé le dossier (ex: "GTAV Enhanced") : recherche
            // bornée en profondeur (évite un scan disque complet involontaire).
            foreach (var dir in Directory.EnumerateDirectories(RockstarGamesFolder, "*", SearchOption.TopDirectoryOnly))
            {
                var candidate = Path.Combine(dir, "settings.xml");
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch { }
        return null;
    }

    public FiveMStatus GetStatus()
    {
        bool installed = Directory.Exists(DataFolder);
        var settingsPath = installed ? FindSettingsXml() : null;
        bool backupExists = settingsPath != null && File.Exists(BackupPath(settingsPath));
        bool running = GetGameProcess() != null;
        return new FiveMStatus(installed, settingsPath != null, settingsPath, backupExists, running);
    }

    // Le vrai process de jeu FiveM s'appelle "FiveM_b<numéro de build>_GTAProcess.exe" — le
    // numéro de build change à CHAQUE mise à jour du client FiveM/GTA V (ex: FiveM_b2699_GTAProcess).
    // Une correspondance de nom exact ou même un préfixe fixe finit toujours par casser. On le
    // retrouve donc par ce qui NE change PAS : c'est un descendant du lanceur FiveM.exe et il
    // tourne depuis le dossier de données FiveM — pas de P/Invoke, uniquement WMI (même pattern
    // que DriverService/HardwareMonitorService).
    //
    // Vérifié en conditions réelles : une liste d'EXCLUSION (noms connus à ignorer) est fragile —
    // FiveM_DumpServer (un helper de crash-report, absent de la liste) s'est fait passer pour le
    // jeu et a reçu la priorité/EcoQoS/GPU preference à SA place, laissant le vrai process de jeu
    // sans aucun boost. Le nom "GTAProcess" est en revanche stable et documenté sur toutes les
    // versions — critère POSITIF utilisé en priorité, la liste d'exclusion ne sert plus que de
    // filet en dernier recours si ce nom venait à disparaître.
    private static readonly string[] KnownFiveMAuxiliaryNames =
        ["FiveM", "CrashHandler", "crashpad_handler", "FiveM_Updater", "FiveM_BootstrapV2", "FiveM_DumpServer"];

    public static Process? GetGameProcess()
    {
        // 1) Chemin rapide : noms encore valides sur une partie du parc.
        foreach (var name in new[] { "FiveM_GTAProcess", "fivem-win32-release" })
        {
            var procs = Process.GetProcessesByName(name);
            if (procs.Length > 0) return procs[0];
        }

        // 2) Filet robuste au renommage.
        try
        {
            var launcherIds = Process.GetProcessesByName("FiveM").Select(p => p.Id).ToHashSet();
            if (launcherIds.Count == 0) return null; // FiveM pas lancé du tout

            var descendants = GetDescendantProcessIds(launcherIds);
            var dataFolder = DataFolder;
            Process? pathFallback = null;

            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (!descendants.Contains(p.Id)) continue;

                    // Critère positif prioritaire : le vrai process de jeu contient toujours
                    // "GTAProcess" dans son nom, quel que soit le numéro de build.
                    if (p.ProcessName.Contains("GTAProcess", StringComparison.OrdinalIgnoreCase)) return p;

                    if (pathFallback != null) continue; // dernier recours déjà trouvé, inutile de re-scanner
                    if (KnownFiveMAuxiliaryNames.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase)) continue;

                    var path = p.MainModule?.FileName;
                    if (string.IsNullOrEmpty(path)) continue;
                    if (path.StartsWith(dataFolder, StringComparison.OrdinalIgnoreCase)) pathFallback = p;
                }
                catch { } // accès refusé sur certains process — normal, on continue.
            }
            if (pathFallback != null) return pathFallback;
        }
        catch { }
        return null;
    }

    // PID descendants (directs + indirects) d'un ensemble de PID racines, via WMI Win32_Process.
    private static HashSet<int> GetDescendantProcessIds(IEnumerable<int> rootIds)
    {
        var result = new HashSet<int>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId FROM Win32_Process");
            using var all = searcher.Get();

            var childrenOf = new Dictionary<int, List<int>>();
            foreach (ManagementObject obj in all)
            {
                var pid = Convert.ToInt32(obj["ProcessId"]);
                var parent = Convert.ToInt32(obj["ParentProcessId"]);
                if (!childrenOf.TryGetValue(parent, out var list))
                    childrenOf[parent] = list = [];
                list.Add(pid);
            }

            var queue = new Queue<int>(rootIds);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!childrenOf.TryGetValue(current, out var kids)) continue;
                foreach (var kid in kids)
                    if (result.Add(kid)) queue.Enqueue(kid);
            }
        }
        catch { }
        return result;
    }

    // ── Désactiver les optimisations plein écran, ciblé FiveM ──────────────
    // Distinct du tweak global de l'onglet Gaming (GameDVR_FSEBehaviorMode, système entier) :
    // ceci pose le flag de compatibilité PAR EXÉCUTABLE (case "Désactiver les optimisations plein
    // écran" dans les propriétés d'un .exe), directement sur le vrai processus de jeu — plus ciblé
    // et plus fiable. Le processus de jeu tourne depuis un dossier de cache versionné qui change à
    // chaque mise à jour FiveM : on l'applique donc au chemin RÉEL du processus en cours si trouvé,
    // en plus du lanceur (chemin stable), pour couvrir les deux cas.
    private const string LayersKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";

    public bool SetFullscreenOptOff(bool enable)
    {
        var paths = new List<string>();
        try
        {
            var launcher = LauncherPath;
            if (File.Exists(launcher)) paths.Add(launcher);
        }
        catch { }
        try
        {
            var proc = GetGameProcess();
            var running = proc?.MainModule?.FileName;
            if (!string.IsNullOrEmpty(running) && !paths.Contains(running)) paths.Add(running);
        }
        catch { }

        if (paths.Count == 0) return false;

        bool anyOk = false;
        foreach (var path in paths)
        {
            try
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(LayersKey);
                if (enable)
                {
                    var existing = k.GetValue(path) as string ?? "";
                    if (!existing.Contains("DISABLEDXMAXIMIZEDWINDOWEDMODE"))
                    {
                        var combined = string.IsNullOrEmpty(existing) ? "~ DISABLEDXMAXIMIZEDWINDOWEDMODE" : existing + " DISABLEDXMAXIMIZEDWINDOWEDMODE";
                        k.SetValue(path, combined, Microsoft.Win32.RegistryValueKind.String);
                    }
                }
                else
                {
                    k.DeleteValue(path, throwOnMissingValue: false);
                }
                anyOk = true;
            }
            catch { }
        }
        return anyOk;
    }

    public bool GetFullscreenOptOff()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(LayersKey);
            if (k == null) return false;
            var launcher = LauncherPath;
            var v = k.GetValue(launcher) as string;
            return v != null && v.Contains("DISABLEDXMAXIMIZEDWINDOWEDMODE");
        }
        catch { return false; }
    }

    // ── GPU dédié (utile sur laptop double GPU Intel/NVIDIA ou Intel/AMD) ────────────────────
    // Sur un laptop avec carte graphique intégrée + dédiée, Windows peut lancer FiveM sur l'iGPU
    // par défaut — perte de FPS massive et invisible pour l'utilisateur (aucune erreur, juste des
    // performances mauvaises). Cette clé (Paramètres graphiques Windows > Performances élevées)
    // force le GPU dédié pour ce .exe précis. Documentée par Microsoft, un seul REG_SZ par chemin
    // d'exe — même stratégie double-chemin (lanceur + process réel) que SetFullscreenOptOff.
    private const string GpuPrefKey = @"Software\Microsoft\DirectX\UserGpuPreference";

    public bool SetGpuPreference(bool highPerformance)
    {
        var paths = new List<string>();
        try
        {
            var launcher = LauncherPath;
            if (File.Exists(launcher)) paths.Add(launcher);
        }
        catch { }
        try
        {
            var proc = GetGameProcess();
            var running = proc?.MainModule?.FileName;
            if (!string.IsNullOrEmpty(running) && !paths.Contains(running)) paths.Add(running);
        }
        catch { }

        if (paths.Count == 0) return false;

        bool anyOk = false;
        foreach (var path in paths)
        {
            try
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(GpuPrefKey);
                if (highPerformance) k.SetValue(path, "GpuPreference=2;", Microsoft.Win32.RegistryValueKind.String);
                else k.DeleteValue(path, throwOnMissingValue: false);
                anyOk = true;
            }
            catch { }
        }
        return anyOk;
    }

    public bool GetGpuPreference()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(GpuPrefKey);
            if (k == null) return false;
            var launcher = LauncherPath;
            var v = k.GetValue(launcher) as string;
            return v != null && v.Contains("GpuPreference=2");
        }
        catch { return false; }
    }

    private readonly PowerThrottlingService _powerThrottling = new();

    public (bool success, string message) BoostRunningProcessNow()
    {
        var p = GetGameProcess();
        if (p == null) return (false, "FiveM n'est pas lancé — relance-le puis reviens sur cet onglet.");
        try
        {
            var prev = p.PriorityClass;
            p.PriorityClass = ProcessPriorityClass.High;
            // Retire aussi le bridage EcoQoS (mode Efficacité) que Windows a pu imposer au
            // process — indépendant de la priorité, et c'est souvent lui le vrai coupable sur
            // CPU hybride (12e gen Intel et +) quand le process a déjà tourné en arrière-plan.
            _powerThrottling.ClearEfficiencyMode(p.Handle);
            return (true, prev == ProcessPriorityClass.High
                ? "Priorité déjà élevée."
                : $"Priorité passée de {prev} à High.");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public GraphicsApplyResult ApplyFpsGraphicsPreset()
    {
        var path = FindSettingsXml();
        if (path == null)
            return new GraphicsApplyResult(false, [],
                "Fichier de réglages FiveM introuvable — lance FiveM au moins une fois pour qu'il soit créé, puis réessaie.");

        try
        {
            var backup = BackupPath(path);
            if (!File.Exists(backup)) File.Copy(path, backup, overwrite: false);

            var doc = XDocument.Load(path);
            var changed = new List<string>();
            var touched = new HashSet<XElement>();

            foreach (var (match, value, label) in GraphicsRules)
            {
                foreach (var el in doc.Descendants())
                {
                    if (touched.Contains(el)) continue;
                    var valueAttr = el.Attribute("value");
                    if (valueAttr == null) continue;
                    if (!match(el.Name.LocalName)) continue;

                    valueAttr.Value = value;
                    touched.Add(el);
                    changed.Add(label);
                }
            }

            if (changed.Count == 0)
                return new GraphicsApplyResult(false, [],
                    "Le fichier de réglages a été trouvé mais aucun paramètre reconnu n'y figure — ta version de FiveM utilise peut-être un format différent. Aucune modification effectuée.");

            doc.Save(path);
            return new GraphicsApplyResult(true, changed,
                $"{changed.Count} réglage(s) graphique(s) optimisé(s).");
        }
        catch (Exception ex)
        {
            return new GraphicsApplyResult(false, [], $"Erreur : {ex.Message}");
        }
    }

    public (bool success, string message) RestoreOriginalGraphics()
    {
        var path = FindSettingsXml();
        if (path == null) return (false, "Fichier de réglages FiveM introuvable.");
        var backup = BackupPath(path);
        if (!File.Exists(backup)) return (false, "Aucune sauvegarde à restaurer.");

        try
        {
            File.Copy(backup, path, overwrite: true);
            File.Delete(backup);
            return (true, "Réglages graphiques d'origine restaurés.");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}
