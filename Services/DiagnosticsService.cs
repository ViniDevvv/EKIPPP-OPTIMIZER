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
        _                  => "Corriger"
    };
}

public class DiagnosticsService
{
    public List<DiagnosticIssue> RunFullDiagnostics()
    {
        var issues = new List<DiagnosticIssue>();

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
        CheckBackgroundProcesses(issues);
        CheckSuspiciousProcesses(issues);
        CheckEventLogCrashes(issues);

        return issues.OrderByDescending(i => (int)i.Severity).ThenBy(i => i.Category).ToList();
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
            proc.WaitForExit(3000);

            if (output.Contains("381b4222-f694-41f0-9685-ff5bb260df2e", StringComparison.OrdinalIgnoreCase))
                issues.Add(new("Plan d'alimentation équilibré",
                    "Windows utilise le plan Équilibré — limite les performances du CPU.",
                    "Activez le plan Haute Performance dans l'onglet Gaming pour des meilleures performances.",
                    IssueSeverity.Warning, "Performance", "high_perf"));
            else if (output.Contains("a1841308-3541-4fab-bc81-f71556f20b4a", StringComparison.OrdinalIgnoreCase))
                issues.Add(new("Plan d'alimentation Économie d'énergie",
                    "Windows utilise le plan Économie d'énergie — performances très réduites.",
                    "Passez au plan Haute Performance dans l'onglet Gaming immédiatement.",
                    IssueSeverity.Critical, "Performance", "high_perf"));
            else
                issues.Add(new("Plan d'alimentation optimal",
                    "Plan Haute Performance ou Ultimate actif.",
                    "Performances CPU maximales.",
                    IssueSeverity.Info, "Performance"));
        }
        catch { }
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
                issues.Add(new("Aucun antivirus actif détecté",
                    "Aucun antivirus actif n'a été trouvé sur ce PC.",
                    "Activez Windows Defender ou installez un antivirus pour protéger votre PC.",
                    IssueSeverity.Critical, "Sécurité", "windows_security"));
            else
                issues.Add(new("Antivirus actif",
                    $"{string.Join(", ", avs)} — protection active.",
                    "Aucune action requise.",
                    IssueSeverity.Info, "Sécurité"));
        }
        catch { }
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

            // Clé secondaire pour confirmation
            using var kCapture = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR");
            var appCapture = kCapture?.GetValue("AppCaptureEnabled");
            bool captureEnabled = appCapture == null || Convert.ToInt32(appCapture) != 0;

            // DVR est OFF seulement si les deux clés indiquent désactivé
            bool dvrActive = enabled || captureEnabled;

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
        try
        {
            var since = DateTime.Now.AddDays(-7);
            int bsods  = 0;
            int appCrashes = 0;

            // BSOD : Kernel-Power EventID 41 + BugCheck 1001
            using var sysLog = new System.Diagnostics.EventLog("System");
            foreach (System.Diagnostics.EventLogEntry e in sysLog.Entries)
            {
                if (e.TimeGenerated < since) continue;
                if (e.EntryType == System.Diagnostics.EventLogEntryType.Error &&
                    (e.InstanceId == 41 || e.InstanceId == 1001))
                    bsods++;
            }

            // Crashs applicatifs : Application EventID 1000
            using var appLog = new System.Diagnostics.EventLog("Application");
            foreach (System.Diagnostics.EventLogEntry e in appLog.Entries)
            {
                if (e.TimeGenerated < since) continue;
                if (e.EntryType == System.Diagnostics.EventLogEntryType.Error && e.InstanceId == 1000)
                    appCrashes++;
            }

            if (bsods > 0)
                issues.Add(new($"{bsods} crash(s) système (BSOD) en 7 jours",
                    $"Windows a planté {bsods} fois cette semaine avec un écran bleu. Causes fréquentes : pilote défaillant, RAM instable, surchauffe.",
                    "Ouvrez l'Observateur d'événements → Journaux Windows → Système → cherchez les erreurs récentes ID 41 (Kernel-Power) pour identifier la cause.",
                    IssueSeverity.Critical, "Stabilité", "event_viewer"));
            else if (appCrashes > 5)
                issues.Add(new($"{appCrashes} erreurs d'applications en 7 jours",
                    $"{appCrashes} événements d'erreur détectés cette semaine dans les logs Windows (ID 1000). Peut inclure des erreurs mineures en arrière-plan.",
                    "Ouvrez l'Observateur d'événements pour voir quelles applications sont concernées et les mettre à jour.",
                    IssueSeverity.Warning, "Stabilité", "event_viewer"));
            else
                issues.Add(new("Aucun crash système détecté",
                    $"Aucun BSOD en 7 jours. {appCrashes} crash(s) applicatif(s).",
                    "Système stable.",
                    IssueSeverity.Info, "Stabilité"));
        }
        catch { }
    }

    private static bool IsSuspicious(string name)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("miner") || n.Contains("crypto") || n.Contains("xmrig");
    }
}
