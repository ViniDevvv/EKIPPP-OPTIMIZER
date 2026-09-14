using System.Diagnostics;
using System.IO;
using System.Management;
using Microsoft.Win32;

namespace EkipppOptimizer.Services;

public enum IssueSeverity { Info, Warning, Critical }

public record DiagnosticIssue(
    string Title,
    string Description,
    string Recommendation,
    IssueSeverity Severity,
    string Category,
    string? FixKey = null)
{
    public bool HasFix => FixKey != null;
    public string FixLabel => FixKey switch
    {
        "high_perf"        => "Activer haute perf",
        "game_dvr"         => "Désactiver DVR",
        "net_throttle"     => "Désactiver throttle",
        "windows_update"   => "Ouvrir Windows Update",
        "reboot"           => "Ouvrir Windows Update",
        "clean"            => "Aller à Nettoyage →",
        "startup"          => "Aller à Démarrage →",
        "windows_security" => "Ouvrir Sécurité Windows",
        "drivers"          => "Voir les pilotes →",
        "task_manager"     => "Ouvrir Gestionnaire des tâches",
        "event_viewer"     => "Ouvrir Observateur d'événements",
        "reapply_drift"    => "Réappliquer le profil Gaming",
        _                  => "Corriger"
    };
}

public class DiagnosticsService
{
    private readonly WindowsOptimizerService _optimizer;
    private readonly CorePinningService _corePinning = new();

    public DiagnosticsService(WindowsOptimizerService? optimizer = null) => _optimizer = optimizer ?? new();

    public List<DiagnosticIssue> RunFullDiagnostics()
    {
        var issues = new List<DiagnosticIssue>();

        CheckOptimizationDrift(issues);
        CheckHybridCpu(issues);
        CheckRam(issues);
        CheckCpuUsage(issues);
        CheckDiskSpace(issues);
        CheckDiskHealth(issues);
        CheckStartupCount(issues);
        CheckPagefile(issues);
        CheckWindowsUpdate(issues);
        CheckPendingReboot(issues);
        CheckPowerPlan(issues);
        CheckAntivirus(issues);
        CheckTempFolder(issues);
        CheckGameDvr(issues);
        CheckNetworkThrottling(issues);
        CheckUnsignedDrivers(issues);
        CheckGpuDriver(issues);
        CheckBackgroundProcesses(issues);
        CheckSuspiciousProcesses(issues);
        CheckEventLogCrashes(issues);
        CheckMemorySpeed(issues);
        CheckMemoryChannels(issues);
        CheckMemoryIntegrity(issues);

        return issues.OrderByDescending(i => (int)i.Severity).ThenBy(i => i.Category).ToList();
    }

    // Aucun optimiseur PC classique ne fait de détection de régression persistante : ici, on compare
    // l'état "optimisé" mémorisé après le dernier profil Gaming à l'état réel actuel — Windows Update
    // ou une mise à jour de pilote GPU peut avoir tout annulé sans que l'utilisateur s'en aperçoive.
    private void CheckOptimizationDrift(List<DiagnosticIssue> issues)
    {
        try
        {
            var drifted = _optimizer.CheckOptimizationDrift();
            if (drifted.Count == 0) return;

            issues.Add(new($"Windows a annulé {drifted.Count} optimisation(s)",
                $"Depuis ta dernière optimisation Gaming, {string.Join(", ", drifted)} {(drifted.Count > 1 ? "sont revenus" : "est revenu")} à leur état non-optimisé — probablement après une mise à jour Windows ou un pilote GPU.",
                "Réapplique le profil Gaming en un clic pour restaurer tes optimisations.",
                IssueSeverity.Warning, "Performance", "reapply_drift"));
        }
        catch { }
    }

    // Sur CPU Intel hybride (12e-15e gen, cœurs Performance + Efficacité), Windows planifie parfois
    // GTA5.exe/FiveM sur des cœurs Efficacité, ce qui casse le framerate par micro-freezes invisibles
    // (souvent confondus avec un problème réseau ou de mods). L'app applique déjà automatiquement une
    // priorité CPU élevée dès que FiveM/GTA V est détecté (Game Booster) — une vraie épingle sur les
    // cœurs Performance précis nécessiterait de parser une structure Windows bas niveau non vérifiable
    // sans le matériel hybride réel, donc on informe plutôt que d'appliquer un fix non testable.
    private void CheckHybridCpu(List<DiagnosticIssue> issues)
    {
        try
        {
            if (!_corePinning.IsHybridCpu()) return;
            issues.Add(new("CPU hybride Performance/Efficacité détecté",
                "Ton CPU a des cœurs Performance et Efficacité — Windows place parfois GTA V/FiveM sur les mauvais cœurs, créant des micro-freezes invisibles souvent pris pour un problème réseau ou de mods.",
                "L'app booste déjà automatiquement la priorité de FiveM/GTA V dès sa détection (onglet Gaming). Pour aller plus loin manuellement : Gestionnaire des tâches → clic droit sur FiveM_GTAProcess.exe → Définir l'affinité → décoche les derniers cœurs (Efficacité).",
                IssueSeverity.Info, "Matériel"));
        }
        catch { }
    }

    // ── Diagnostics matériel avancés ─────────────────────────────────────────

    private void CheckMemorySpeed(List<DiagnosticIssue> issues)
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory");
            var modules = s.Get().Cast<ManagementObject>().ToList();
            if (modules.Count == 0) return;

            var speeds = modules.Select(m => Convert.ToInt32(m["Speed"] ?? 0)).Where(v => v > 0).ToList();
            if (speeds.Count == 0) return;

            // Sur un upgrade partiel (barrettes de vitesses nominales différentes), rated=Max et
            // actual=Max calculés indépendamment n'ont plus aucun sens à comparer entre eux — on
            // ne peut affirmer un "XMP désactivé" que si toutes les barrettes partagent la même
            // vitesse nominale annoncée par le fabricant.
            if (speeds.Distinct().Count() > 1)
            {
                issues.Add(new("Vitesses de RAM incompatibles entre barrettes",
                    $"Les barrettes installées annoncent des vitesses nominales différentes ({string.Join(" / ", speeds.Distinct().OrderBy(v => v))} MT/s) — la RAM tourne au minimum commun.",
                    "Pour de meilleures performances, utilisez des barrettes identiques (même vitesse, même capacité) plutôt qu'un kit mixte.",
                    IssueSeverity.Info, "Matériel"));
                return;
            }

            // Speed = vitesse nominale annoncée par le fabricant du module. ConfiguredClockSpeed =
            // vitesse à laquelle Windows fait réellement tourner la RAM. Un écart net entre les deux
            // suggère un profil XMP (Intel) / EXPO (AMD) non activé dans le BIOS — mais Win32_PhysicalMemory.Speed
            // n'est pas garanti fiable à 100% selon le fabricant de carte mère, d'où un message prudent plutôt qu'affirmatif.
            var rated  = speeds[0];
            var actual = modules.Select(m => Convert.ToInt32(m["ConfiguredClockSpeed"] ?? 0)).DefaultIfEmpty(0).Max();
            if (actual == 0) return;

            if (actual < rated * 0.9)
                issues.Add(new("RAM possiblement sous son potentiel (XMP/EXPO à vérifier)",
                    $"Votre RAM est annoncée pour {rated} MT/s mais Windows la fait tourner à {actual} MT/s.",
                    "Vérifiez le profil XMP (Intel) ou EXPO (AMD) dans le BIOS (touche Suppr ou F2 au démarrage, selon la carte mère) — l'activer peut apporter un vrai gain de performance, surtout en jeu.",
                    IssueSeverity.Warning, "Matériel"));
            else
                issues.Add(new("RAM à pleine vitesse",
                    $"La RAM tourne à sa vitesse nominale ({actual} MT/s).",
                    "Aucune action requise.",
                    IssueSeverity.Info, "Matériel"));
        }
        catch { }
    }

    private void CheckMemoryChannels(List<DiagnosticIssue> issues)
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory");
            var capacities = s.Get().Cast<ManagementObject>()
                .Select(m => Convert.ToInt64(m["Capacity"] ?? 0)).Where(c => c > 0).ToList();
            var populated = capacities.Count;

            // Une seule barrette = canal simple à coup sûr, quel que soit le nombre de slots sur
            // la carte mère. Avec 2+ barrettes, on ne peut pas garantir un vrai double canal sans
            // lire l'appariement exact des slots (BankLabel/DeviceLocator) — on ne signale donc que
            // le cas certain plutôt que de risquer une fausse alerte.
            if (populated == 1)
                issues.Add(new("Une seule barrette de RAM (canal simple)",
                    "Une seule barrette de RAM divise la bande passante mémoire par deux par rapport à une configuration double canal (deux barrettes identiques) — impact réel sur les performances, notamment avec un GPU intégré ou en jeu.",
                    "Ajoutez une seconde barrette identique (même capacité, même vitesse) pour activer le double canal.",
                    IssueSeverity.Warning, "Matériel"));
            else if (populated >= 2 && capacities.Distinct().Count() > 1)
                issues.Add(new("Capacités de RAM asymétriques entre barrettes",
                    $"Les barrettes installées n'ont pas toutes la même capacité ({string.Join(" / ", capacities.Distinct().OrderBy(c => c).Select(c => $"{c / (1024 * 1024 * 1024)} Go"))}) — le double canal peut être partiellement désactivé sur une partie de la mémoire.",
                    "Pour un vrai double canal sur toute la mémoire, utilisez des barrettes de capacité identique.",
                    IssueSeverity.Info, "Matériel"));
        }
        catch { }
    }

    private void CheckMemoryIntegrity(List<DiagnosticIssue> issues)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
            if (key?.GetValue("Enabled") is int v && v == 1)
                issues.Add(new("Memory Integrity (isolation du noyau) activé",
                    "Cette protection Windows renforce la sécurité mais peut coûter quelques pourcents de performances CPU/GPU selon votre matériel, surtout en jeu.",
                    "Compromis sécurité/performance à arbitrer vous-même : Paramètres → Confidentialité et sécurité → Sécurité Windows → Isolation du noyau. Ne désactivez que si vous comprenez les implications de sécurité.",
                    IssueSeverity.Info, "Matériel"));
        }
        catch { }
    }

    private void CheckRam(List<DiagnosticIssue> issues)
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (var o in s.Get())
            {
                var total   = Convert.ToInt64(o["TotalVisibleMemorySize"]) / 1024;
                var free    = Convert.ToInt64(o["FreePhysicalMemory"]) / 1024;
                var usedPct = (double)(total - free) / total * 100;

                if (total < 4096)
                    issues.Add(new("RAM insuffisante",
                        $"Seulement {total / 1024} Go de RAM détecté.",
                        "Augmenter la RAM à 8 Go minimum pour de meilleures performances.",
                        IssueSeverity.Critical, "Mémoire"));
                else if (usedPct > 85)
                    issues.Add(new("Utilisation RAM élevée",
                        $"RAM utilisée à {usedPct:F0}% ({(total - free) / 1024} / {total / 1024} Go).",
                        "Fermez les applications inutiles ou ajoutez de la RAM.",
                        IssueSeverity.Warning, "Mémoire"));
                else
                    issues.Add(new("RAM correcte",
                        $"{total / 1024} Go disponible, {usedPct:F0}% utilisé.",
                        "Aucune action requise.",
                        IssueSeverity.Info, "Mémoire"));
            }
        }
        catch { }
    }

    private void CheckCpuUsage(List<DiagnosticIssue> issues)
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT LoadPercentage FROM Win32_Processor");
            foreach (var o in s.Get())
            {
                var load = Convert.ToInt32(o["LoadPercentage"]);
                if (load > 90)
                    issues.Add(new("CPU saturé",
                        $"Charge CPU: {load}%.",
                        "Un processus consomme excessivement le CPU. Vérifiez l'onglet Dashboard.",
                        IssueSeverity.Critical, "CPU"));
                else if (load > 70)
                    issues.Add(new("Charge CPU élevée",
                        $"Charge CPU: {load}%.",
                        "Fermez les applications gourmandes en arrière-plan.",
                        IssueSeverity.Warning, "CPU"));
                else
                    issues.Add(new("CPU en bonne santé",
                        $"Charge CPU: {load}%.",
                        "Performances normales.",
                        IssueSeverity.Info, "CPU"));
            }
        }
        catch { }
    }

    private void CheckDiskSpace(List<DiagnosticIssue> issues)
    {
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
                var freeGB  = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                var totalGB = drive.TotalSize / (1024.0 * 1024 * 1024);
                var freePct = freeGB / totalGB * 100;

                if (freePct < 5)
                    issues.Add(new($"Disque {drive.Name} presque plein",
                        $"Seulement {freeGB:F1} Go libres sur {totalGB:F0} Go ({freePct:F0}% libre).",
                        "Lancez un nettoyage depuis l'onglet Nettoyage pour récupérer de l'espace.",
                        IssueSeverity.Critical, "Stockage", "clean"));
                else if (freePct < 15)
                    issues.Add(new($"Espace disque {drive.Name} faible",
                        $"{freeGB:F1} Go libres sur {totalGB:F0} Go.",
                        "Envisagez un nettoyage ou une extension de stockage.",
                        IssueSeverity.Warning, "Stockage", "clean"));
            }
        }
        catch { }
    }

    private void CheckDiskHealth(List<DiagnosticIssue> issues)
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Model, Status FROM Win32_DiskDrive");
            foreach (var o in s.Get())
            {
                var model  = o["Model"]?.ToString()  ?? "Disque inconnu";
                var status = o["Status"]?.ToString() ?? "";

                if (status == "OK")
                    issues.Add(new("Disque en bonne santé",
                        $"{model} — état SMART: OK.",
                        "Aucune action requise.",
                        IssueSeverity.Info, "Stockage"));
                else if (!string.IsNullOrEmpty(status))
                    issues.Add(new("Disque potentiellement défaillant",
                        $"{model} — état SMART: {status}.",
                        "Sauvegardez vos données immédiatement et envisagez un remplacement du disque.",
                        IssueSeverity.Critical, "Stockage"));
            }
        }
        catch { }
    }

    private void CheckStartupCount(List<DiagnosticIssue> issues)
    {
        try
        {
            int count = 0;
            using var k1 = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
            using var k2 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
            if (k1 != null) count += k1.ValueCount;
            if (k2 != null) count += k2.ValueCount;

            if (count > 15)
                issues.Add(new("Trop de programmes au démarrage",
                    $"{count} programmes se lancent au démarrage de Windows.",
                    "Désactivez les programmes inutiles dans l'onglet Démarrage.",
                    IssueSeverity.Warning, "Démarrage", "startup"));
            else if (count > 8)
                issues.Add(new("Démarrage chargé",
                    $"{count} programmes au démarrage.",
                    "Quelques programmes pourraient être désactivés pour accélérer le démarrage.",
                    IssueSeverity.Info, "Démarrage", "startup"));
            else
                issues.Add(new("Démarrage optimisé",
                    $"{count} programmes au démarrage — excellent.",
                    "Aucune action requise.",
                    IssueSeverity.Info, "Démarrage"));
        }
        catch { }
    }

    private void CheckPagefile(List<DiagnosticIssue> issues)
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT AllocatedBaseSize, CurrentUsage FROM Win32_PageFileUsage");
            foreach (var o in s.Get())
            {
                var alloc = Convert.ToInt64(o["AllocatedBaseSize"]);
                var used  = Convert.ToInt64(o["CurrentUsage"]);
                if (alloc == 0) continue;
                var pct = (double)used / alloc * 100;

                if (pct > 80)
                    issues.Add(new("Fichier de pagination saturé",
                        $"Swap utilisé à {pct:F0}% ({used} / {alloc} Mo).",
                        "Le système manque de RAM physique et utilise le disque comme RAM (beaucoup plus lent).",
                        IssueSeverity.Warning, "Mémoire"));
            }
        }
        catch { }
    }

    private void CheckWindowsUpdate(List<DiagnosticIssue> issues)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\Results\Install");
            if (key?.GetValue("LastSuccessTime") is string lastUpdate)
            {
                if (DateTime.TryParse(lastUpdate, out var dt))
                {
                    var days = (int)(DateTime.Now - dt).TotalDays;
                    if (days > 90)
                        issues.Add(new("Windows Update en retard",
                            $"Dernière mise à jour il y a {days} jours.",
                            "Effectuez une mise à jour Windows pour corriger les failles de sécurité.",
                            IssueSeverity.Warning, "Sécurité", "windows_update"));
                    else
                        issues.Add(new("Windows à jour",
                            $"Dernière mise à jour il y a {days} jours.",
                            "Aucune action requise.",
                            IssueSeverity.Info, "Sécurité"));
                }
            }
        }
        catch { }
    }

    private void CheckPendingReboot(List<DiagnosticIssue> issues)
    {
        try
        {
            bool pending = false;
            using var k1 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
            using var k2 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
            if (k1 != null || k2 != null) pending = true;

            if (pending)
                issues.Add(new("Redémarrage Windows en attente",
                    "Une mise à jour Windows attend un redémarrage pour s'appliquer complètement.",
                    "Redémarrez votre PC pour finaliser les mises à jour et améliorer la stabilité.",
                    IssueSeverity.Warning, "Sécurité", "reboot"));
        }
        catch { }
    }

    private void CheckPowerPlan(List<DiagnosticIssue> issues)
    {
        try
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo("powercfg", "/getactivescheme")
            {
                RedirectStandardOutput = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };
            proc.Start();
            var output = proc.StandardOutput.ReadToEnd();
            bool exited = proc.WaitForExit(3000);

            if (!exited || string.IsNullOrWhiteSpace(output))
            {
                // powercfg n'a pas répondu à temps — on ne peut rien affirmer sur le plan actif.
                AddPowerPlanUnknownIssue(issues);
            }
            else if (output.Contains("381b4222-f694-41f0-9685-ff5bb260df2e", StringComparison.OrdinalIgnoreCase))
                issues.Add(new("Plan d'alimentation équilibré",
                    "Windows utilise le plan Équilibré — limite les performances du CPU.",
                    "Activez le plan Haute Performance dans l'onglet Gaming pour des meilleures performances.",
                    IssueSeverity.Warning, "Performance", "high_perf"));
            else if (output.Contains("a1841308-3541-4fab-bc81-f71556f20b4a", StringComparison.OrdinalIgnoreCase))
                issues.Add(new("Plan d'alimentation Économie d'énergie",
                    "Windows utilise le plan Économie d'énergie — performances très réduites.",
                    "Passez au plan Haute Performance dans l'onglet Gaming immédiatement.",
                    IssueSeverity.Critical, "Performance", "high_perf"));
            else if (output.Contains("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", StringComparison.OrdinalIgnoreCase)
                  || output.Contains("e9a42b02-d5df-448d-aa00-03f14749eb61", StringComparison.OrdinalIgnoreCase))
                issues.Add(new("Plan d'alimentation optimal",
                    "Plan Haute Performance ou Ultimate actif.",
                    "Performances CPU maximales.",
                    IssueSeverity.Info, "Performance"));
            else
                // GUID reconnu par aucun des plans connus (souvent un plan OEM personnalisé) —
                // ne pas affirmer un état qu'on n'a pas vérifié.
                issues.Add(new("Plan d'alimentation personnalisé",
                    "Un plan d'alimentation non standard (OEM) est actif — ses performances réelles ne sont pas vérifiables automatiquement.",
                    "Vérifiez manuellement dans Options d'alimentation si besoin de performances maximales.",
                    IssueSeverity.Info, "Performance"));
        }
        catch { }
    }

    private static void AddPowerPlanUnknownIssue(List<DiagnosticIssue> issues)
    {
        issues.Add(new("Plan d'alimentation — état inconnu",
            "Impossible de lire le plan d'alimentation actif (powercfg n'a pas répondu).",
            "Réessayez l'analyse, ou vérifiez manuellement dans les Options d'alimentation Windows.",
            IssueSeverity.Info, "Performance"));
    }

    private void CheckAntivirus(List<DiagnosticIssue> issues)
    {
        try
        {
            using var s = new ManagementObjectSearcher(
                @"root\SecurityCenter2", "SELECT displayName, productState FROM AntiVirusProduct");
            var avs = new List<string>();
            bool anyEnabled = false;
            foreach (var o in s.Get())
            {
                var name  = o["displayName"]?.ToString() ?? "";
                var state = Convert.ToInt32(o["productState"] ?? 0);
                // bit 12-15 of productState = enabled, bit 4-7 = up to date
                bool enabled = ((state >> 12) & 0xF) == 1;
                if (!string.IsNullOrEmpty(name)) avs.Add(name);
                if (enabled) anyEnabled = true;
            }

            if (avs.Count == 0 || !anyEnabled)
            {
                // SecurityCenter2 peut renvoyer 0 résultat juste après le démarrage, ou pour un
                // AV/EDR géré en entreprise qui ne s'y enregistre pas — on croise avec le vrai
                // statut du service Defender avant d'afficher une alerte Critical potentiellement fausse.
                if (IsWindowsDefenderActive())
                    issues.Add(new("Antivirus actif",
                        "Windows Defender — protection active.",
                        "Aucune action requise.",
                        IssueSeverity.Info, "Sécurité"));
                else
                    issues.Add(new("Aucun antivirus actif détecté",
                        "Aucun antivirus actif n'a été trouvé sur ce PC.",
                        "Activez Windows Defender ou installez un antivirus pour protéger votre PC.",
                        IssueSeverity.Critical, "Sécurité", "windows_security"));
            }
            else
                issues.Add(new("Antivirus actif",
                    $"{string.Join(", ", avs)} — protection active.",
                    "Aucune action requise.",
                    IssueSeverity.Info, "Sécurité"));
        }
        catch { }
    }

    private static bool IsWindowsDefenderActive()
    {
        try
        {
            using var s = new ManagementObjectSearcher(@"root\Microsoft\Windows\Defender",
                "SELECT AntivirusEnabled, RealTimeProtectionEnabled FROM MSFT_MpComputerStatus");
            foreach (var o in s.Get())
            {
                var avEnabled = o["AntivirusEnabled"] is bool b1 && b1;
                var rtEnabled = o["RealTimeProtectionEnabled"] is bool b2 && b2;
                if (avEnabled || rtEnabled) return true;
            }
        }
        catch { }
        return false;
    }

    private void CheckTempFolder(List<DiagnosticIssue> issues)
    {
        try
        {
            var tempPath = Path.GetTempPath();
            var tempDir  = new DirectoryInfo(tempPath);
            if (!tempDir.Exists) return;

            long sizeBytes = tempDir.EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => { try { return f.Length; } catch { return 0L; } });
            var sizeGB = sizeBytes / (1024.0 * 1024 * 1024);
            var sizeMB = sizeBytes / (1024.0 * 1024);

            if (sizeGB > 2)
                issues.Add(new("Dossier Temp volumineux",
                    $"Le dossier temporaire occupe {sizeGB:F1} Go.",
                    "Lancez un nettoyage depuis l'onglet Nettoyage pour libérer de l'espace.",
                    IssueSeverity.Warning, "Stockage", "clean"));
            else if (sizeMB > 500)
                issues.Add(new("Dossier Temp à nettoyer",
                    $"Le dossier temporaire occupe {sizeMB:F0} Mo.",
                    "Un nettoyage est conseillé via l'onglet Nettoyage.",
                    IssueSeverity.Info, "Stockage", "clean"));
        }
        catch { }
    }

    private void CheckGameDvr(List<DiagnosticIssue> issues)
    {
        try
        {
            // Clé primaire : celle que SetGameDvr() écrit réellement
            using var kConfig = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore");
            var gameDvrVal = kConfig?.GetValue("GameDVR_Enabled");
            // Si la clé n'existe pas → DVR actif par défaut Windows
            bool enabled = gameDvrVal == null || Convert.ToInt32(gameDvrVal) != 0;

            // Clé secondaire, utilisée seulement si la clé primaire n'a jamais été configurée —
            // sinon un utilisateur qui a désactivé DVR via le switch principal "Xbox Game Bar"
            // (qui n'écrit QUE GameDVR_Enabled) se voit signalé à tort "DVR activé" à cause de
            // cette clé secondaire absente qui défaut à "activé".
            bool dvrActive;
            if (gameDvrVal != null)
            {
                dvrActive = enabled;
            }
            else
            {
                using var kCapture = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR");
                var appCapture = kCapture?.GetValue("AppCaptureEnabled");
                dvrActive = appCapture == null || Convert.ToInt32(appCapture) != 0;
            }

            if (dvrActive)
                issues.Add(new("Xbox Game DVR activé",
                    "Game DVR enregistre en arrière-plan et consomme CPU/GPU en permanence.",
                    "Désactivez-le ici en un clic — gain immédiat de FPS garanti.",
                    IssueSeverity.Warning, "Performance", "game_dvr"));
            else
                issues.Add(new("Xbox Game DVR désactivé",
                    "Game DVR désactivé — pas de capture en arrière-plan.",
                    "Configuration optimale pour le jeu.",
                    IssueSeverity.Info, "Performance"));
        }
        catch { }
    }

    private void CheckNetworkThrottling(List<DiagnosticIssue> issues)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile");
            var val = key?.GetValue("NetworkThrottlingIndex");
            bool throttled = val == null || Convert.ToInt32(val) != unchecked((int)0xFFFFFFFF);

            if (throttled)
                issues.Add(new("Network Throttling actif",
                    "Windows limite la bande passante réseau pour les applications multimédia.",
                    "Désactivez-le dans l'onglet Gaming → Tweaks pour réduire la latence.",
                    IssueSeverity.Warning, "Réseau", "net_throttle"));
            else
                issues.Add(new("Network Throttling désactivé",
                    "Aucune limitation réseau imposée par Windows.",
                    "Configuration optimale pour le jeu en ligne.",
                    IssueSeverity.Info, "Réseau"));
        }
        catch { }
    }

    private void CheckUnsignedDrivers(List<DiagnosticIssue> issues)
    {
        try
        {
            using var s = new ManagementObjectSearcher(
                "SELECT DeviceName, IsSigned FROM Win32_PnPSignedDriver WHERE IsSigned = FALSE");
            var unsigned = new List<string>();
            foreach (var o in s.Get())
            {
                var name = o["DeviceName"]?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(name)) unsigned.Add(name);
            }

            if (unsigned.Count > 0)
                issues.Add(new($"{unsigned.Count} pilote(s) non signé(s)",
                    $"Pilotes non signés: {string.Join(", ", unsigned.Take(3))}{(unsigned.Count > 3 ? "…" : "")}",
                    "Les pilotes non signés peuvent causer des instabilités. Vérifiez l'onglet Pilotes.",
                    IssueSeverity.Warning, "Pilotes", "drivers"));
            else
                issues.Add(new("Tous les pilotes sont signés",
                    "Aucun pilote non signé détecté.",
                    "Aucune action requise.",
                    IssueSeverity.Info, "Pilotes"));
        }
        catch { }
    }

    // Deux signaux distincts, tous deux invisibles dans le Gestionnaire de périphériques tant
    // qu'on ne sait pas où regarder : (1) le pilote "Microsoft Basic Display Adapter" générique
    // qui reste actif sur un vrai GPU NVIDIA/AMD/Intel après une réinstallation Windows — souvent
    // le plus gros manque à gagner en FPS puisque le GPU tourne sans accélération réelle ; (2) un
    // pilote GPU dédié mais simplement trop ancien pour connaître les optimisations des jeux
    // récents. Purement informatif : on ne touche jamais aux pilotes automatiquement.
    private void CheckGpuDriver(List<DiagnosticIssue> issues)
    {
        try
        {
            using var s = new ManagementObjectSearcher(
                "SELECT DeviceName, DriverDate, DriverProviderName, Manufacturer FROM Win32_PnPSignedDriver WHERE DeviceClass='Display'");
            foreach (var o in s.Get())
            {
                var name     = o["DeviceName"]?.ToString() ?? "";
                var provider = o["DriverProviderName"]?.ToString() ?? o["Manufacturer"]?.ToString() ?? "";
                var dateStr  = o["DriverDate"]?.ToString();

                bool isGenericOnRealGpu = provider.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)
                    && (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
                     || name.Contains("AMD", StringComparison.OrdinalIgnoreCase)
                     || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase)
                     || name.Contains("Intel", StringComparison.OrdinalIgnoreCase));

                if (isGenericOnRealGpu)
                {
                    issues.Add(new("Pilote GPU générique actif",
                        $"« {name} » tourne avec le pilote d'affichage générique de Microsoft, pas son vrai pilote — le GPU fonctionne sans accélération correcte. Ça arrive typiquement après une réinstallation de Windows.",
                        "Installe le vrai pilote depuis le site du fabricant (NVIDIA/AMD/Intel) — souvent le plus gros gain de FPS disponible.",
                        IssueSeverity.Warning, "Pilotes"));
                    continue;
                }

                DateTime? driverDate = null;
                if (!string.IsNullOrEmpty(dateStr))
                {
                    try { driverDate = ManagementDateTimeConverter.ToDateTime(dateStr); } catch { }
                }
                if (driverDate.HasValue && driverDate.Value < DateTime.Now.AddMonths(-18))
                {
                    issues.Add(new("Pilote GPU ancien",
                        $"Le pilote de « {name} » date du {driverDate.Value:dd/MM/yyyy}. Les optimisations spécifiques aux jeux récents arrivent avec les mises à jour de pilote.",
                        "Mets à jour le pilote graphique depuis le site du fabricant ou son utilitaire (GeForce Experience, AMD Software, Intel Driver & Support).",
                        IssueSeverity.Info, "Pilotes"));
                }
            }
        }
        catch { }
    }

    private void CheckBackgroundProcesses(List<DiagnosticIssue> issues)
    {
        try
        {
            var procs  = Process.GetProcesses();
            var count  = procs.Length;
            var highRam = procs.Count(p => { try { return p.PrivateMemorySize64 > 500L * 1024 * 1024; } catch { return false; } });

            if (count > 150)
                issues.Add(new("Trop de processus actifs",
                    $"{count} processus en cours d'exécution, dont {highRam} qui utilisent plus de 500 Mo de mémoire privée chacun. Un nombre élevé ralentit le démarrage des jeux et consomme RAM inutilement.",
                    "Ouvrez le Gestionnaire des tâches → onglet Processus → triez par Mémoire ou CPU → clic droit → Terminer la tâche sur les processus inutiles (navigateurs ouverts, Discord, etc.).",
                    IssueSeverity.Warning, "Performance", "task_manager"));
            else
                issues.Add(new("Nombre de processus normal",
                    $"{count} processus actifs.",
                    "Aucune action requise.",
                    IssueSeverity.Info, "Performance"));
        }
        catch { }
    }

    private void CheckSuspiciousProcesses(List<DiagnosticIssue> issues)
    {
        try
        {
            var suspects = new List<string>();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.WorkingSet64 > 500L * 1024 * 1024 && IsSuspicious(p.ProcessName))
                        suspects.Add($"{p.ProcessName} ({p.WorkingSet64 / (1024 * 1024)} Mo)");
                }
                catch { }
            }

            if (suspects.Count > 0)
                issues.Add(new("Processus suspects détectés",
                    $"Processus inhabituels: {string.Join(", ", suspects)}",
                    "Vérifiez ces processus dans le Gestionnaire des tâches.",
                    IssueSeverity.Warning, "Sécurité"));
        }
        catch { }
    }

    private void CheckEventLogCrashes(List<DiagnosticIssue> issues)
    {
        var since = DateTime.Now.AddDays(-7);
        int realBsods = 0, unexpectedShutdowns = 0, appCrashes = 0;
        bool systemLogOk = true, appLogOk = true;

        // Journal Système : isolé dans son propre try/catch pour qu'un échec ici (service
        // Journal d'événements arrêté/inaccessible) n'empêche pas de lire le journal Application.
        try
        {
            using var sysLog = new System.Diagnostics.EventLog("System");
            foreach (System.Diagnostics.EventLogEntry e in sysLog.Entries)
            {
                if (e.TimeGenerated < since) continue;
                if (e.EntryType != System.Diagnostics.EventLogEntryType.Error) continue;
                // BSOD réel confirmé : source BugCheck (même filtre que BsodAnalyzerService).
                if (e.InstanceId == 1001 && e.Source == "BugCheck")
                    realBsods++;
                // Arrêt inattendu (Kernel-Power 41) : peut être un BSOD sans dump, une coupure
                // de courant ou un hard reset — pas forcément un écran bleu, à ne pas confondre.
                else if (e.InstanceId == 41 && e.Source.Contains("Kernel-Power", StringComparison.OrdinalIgnoreCase))
                    unexpectedShutdowns++;
            }
        }
        catch { systemLogOk = false; }

        // Crashs applicatifs : Application EventID 1000
        try
        {
            using var appLog = new System.Diagnostics.EventLog("Application");
            foreach (System.Diagnostics.EventLogEntry e in appLog.Entries)
            {
                if (e.TimeGenerated < since) continue;
                if (e.EntryType == System.Diagnostics.EventLogEntryType.Error && e.InstanceId == 1000)
                    appCrashes++;
            }
        }
        catch { appLogOk = false; }

        if (!systemLogOk && !appLogOk)
        {
            issues.Add(new("Stabilité — non vérifiable",
                "Impossible d'accéder aux journaux d'événements Windows (Système et Application).",
                "Vérifiez que le service 'Journal d'événements Windows' est démarré (services.msc).",
                IssueSeverity.Info, "Stabilité"));
            return;
        }

        if (realBsods > 0)
            issues.Add(new($"{realBsods} crash(s) système (BSOD) confirmé(s) en 7 jours",
                $"Windows a planté {realBsods} fois cette semaine avec un véritable écran bleu (BugCheck). Causes fréquentes : pilote défaillant, RAM instable, surchauffe.",
                "Ouvrez l'Observateur d'événements → Journaux Windows → Système → cherchez les erreurs récentes source 'BugCheck' pour identifier la cause.",
                IssueSeverity.Critical, "Stabilité", "event_viewer"));
        else if (unexpectedShutdowns > 0)
            issues.Add(new($"{unexpectedShutdowns} arrêt(s) inattendu(s) en 7 jours",
                $"Windows s'est arrêté {unexpectedShutdowns} fois de façon inattendue cette semaine — cause possible : BSOD sans dump, coupure de courant, ou blocage système forçant un arrêt matériel. Aucun BSOD confirmé.",
                "Si ça se reproduit souvent, vérifiez l'alimentation électrique et la stabilité matérielle (RAM, température).",
                IssueSeverity.Warning, "Stabilité", "event_viewer"));
        else if (appCrashes > 5)
            issues.Add(new($"{appCrashes} erreurs d'applications en 7 jours",
                $"{appCrashes} événements d'erreur détectés cette semaine dans les logs Windows (ID 1000). Peut inclure des erreurs mineures en arrière-plan.",
                "Ouvrez l'Observateur d'événements pour voir quelles applications sont concernées et les mettre à jour.",
                IssueSeverity.Warning, "Stabilité", "event_viewer"));
        else
            issues.Add(new("Aucun crash système détecté",
                $"Aucun BSOD ni arrêt inattendu en 7 jours. {appCrashes} crash(s) applicatif(s).",
                "Système stable.",
                IssueSeverity.Info, "Stabilité"));
    }

    private static bool IsSuspicious(string name)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("miner") || n.Contains("crypto") || n.Contains("xmrig");
    }
}
