using Microsoft.Win32;
using System.Diagnostics;
using System.Management;

namespace EkipppOptimizer.Services;

public enum TweakState { On, Off, Unknown }

public class WindowsOptimizerService
{
    // ── Game DVR ───────────────────────────────────────────────────────────
    public TweakState GetGameDvr()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore");
        var v = k?.GetValue("GameDVR_Enabled");
        return v is int i ? (i == 0 ? TweakState.On : TweakState.Off) : TweakState.Unknown;
    }
    public void SetGameDvr(bool disable)
    {
        // Clé principale GameConfigStore
        using var k = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore");
        k.SetValue("GameDVR_Enabled", disable ? 0 : 1, RegistryValueKind.DWord);
        // Clé AppCapture (lue par certains jeux et par CheckGameDvr)
        using var k3 = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR");
        k3.SetValue("AppCaptureEnabled", disable ? 0 : 1, RegistryValueKind.DWord);
        // Politique machine (nécessite admin)
        try
        {
            using var k2 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR", writable: true)
                        ?? Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR");
            k2?.SetValue("AllowGameDVR", disable ? 0 : 1, RegistryValueKind.DWord);
        }
        catch { }
    }

    // ── Fullscreen Optimizations ───────────────────────────────────────────
    public TweakState GetFullscreenOptim()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore");
        var v = k?.GetValue("GameDVR_FSEBehaviorMode");
        return v is int i && i == 2 ? TweakState.On : TweakState.Off;
    }
    public void SetFullscreenOptim(bool disable)
    {
        using var k = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore");
        k.SetValue("GameDVR_FSEBehaviorMode", disable ? 2 : 0, RegistryValueKind.DWord);
        k.SetValue("GameDVR_HonorUserFSEBehaviorMode", disable ? 1 : 0, RegistryValueKind.DWord);
    }

    // ── GPU / CPU Priority ─────────────────────────────────────────────────
    public TweakState GetGpuPriority()
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games");
        var v = k?.GetValue("GPU Priority");
        return v is int i && i == 8 ? TweakState.On : TweakState.Off;
    }
    public bool SetGpuPriority(bool enable)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", writable: true);
            if (k == null) return false;
            k.SetValue("GPU Priority", enable ? 8 : 2, RegistryValueKind.DWord);
            k.SetValue("Priority", enable ? 6 : 2, RegistryValueKind.DWord);
            return true;
        }
        catch { return false; }
    }

    // ── System Responsiveness ──────────────────────────────────────────────
    public TweakState GetSystemResponsiveness()
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile");
        var v = k?.GetValue("SystemResponsiveness");
        return v is int i && i == 0 ? TweakState.On : TweakState.Off;
    }
    public bool SetSystemResponsiveness(bool enable)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", writable: true);
            k?.SetValue("SystemResponsiveness", enable ? 0 : 14, RegistryValueKind.DWord);
            return true;
        }
        catch { return false; }
    }

    // ── Win32 Priority Separation ──────────────────────────────────────────
    public TweakState GetWin32Priority()
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl");
        var v = k?.GetValue("Win32PrioritySeparation");
        return v is int i && i == 38 ? TweakState.On : TweakState.Off;
    }
    public bool SetWin32Priority(bool enable)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl", writable: true);
            k?.SetValue("Win32PrioritySeparation", enable ? 38 : 2, RegistryValueKind.DWord);
            return true;
        }
        catch { return false; }
    }

    // ── Mouse Precision (Acceleration) ────────────────────────────────────
    public TweakState GetMousePrecision()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
        var v = k?.GetValue("MouseSpeed");
        return v is string s && s == "0" ? TweakState.On : TweakState.Off;
    }
    public void SetMousePrecision(bool disable)
    {
        using var k = Registry.CurrentUser.CreateSubKey(@"Control Panel\Mouse");
        k.SetValue("MouseSpeed", disable ? "0" : "1");
        k.SetValue("MouseThreshold1", disable ? "0" : "6");
        k.SetValue("MouseThreshold2", disable ? "0" : "10");
    }

    // ── High Performance Power Plan ────────────────────────────────────────
    public TweakState GetHighPerfPlan()
    {
        var output = RunCmd("powercfg", "/getactivescheme");
        return output.Contains("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c") ? TweakState.On : TweakState.Off;
    }
    public bool SetHighPerfPlan(bool enable)
    {
        try
        {
            if (enable) RunCmd("powercfg", "/setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
            else        RunCmd("powercfg", "/setactive 381b4222-f694-41f0-9685-ff5bb260df2e");
            return true;
        }
        catch { return false; }
    }

    // ── Network Throttling ─────────────────────────────────────────────────
    public TweakState GetNetworkThrottling()
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile");
        var v = k?.GetValue("NetworkThrottlingIndex");
        return v is int i && i == unchecked((int)0xFFFFFFFF) ? TweakState.On : TweakState.Off;
    }
    public bool SetNetworkThrottling(bool disable)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", writable: true);
            k?.SetValue("NetworkThrottlingIndex", disable ? unchecked((int)0xFFFFFFFF) : 10, RegistryValueKind.DWord);
            return true;
        }
        catch { return false; }
    }

    // ── Telemetry ──────────────────────────────────────────────────────────
    public TweakState GetTelemetry()
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection");
        var v = k?.GetValue("AllowTelemetry");
        return v is int i && i == 0 ? TweakState.On : TweakState.Off;
    }
    public bool SetTelemetry(bool disable)
    {
        try
        {
            using var k = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection");
            k.SetValue("AllowTelemetry", disable ? 0 : 1, RegistryValueKind.DWord);
            return true;
        }
        catch { return false; }
    }

    // ── Advertising ID ─────────────────────────────────────────────────────
    public TweakState GetAdvertisingId()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo");
        var v = k?.GetValue("Enabled");
        return v is int i && i == 0 ? TweakState.On : TweakState.Off;
    }
    public bool SetAdvertisingId(bool disable)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo");
            k.SetValue("Enabled", disable ? 0 : 1, RegistryValueKind.DWord);
            return true;
        }
        catch { return false; }
    }

    // ── Location ───────────────────────────────────────────────────────────
    public TweakState GetLocation()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\DeviceAccess\Global\{BFA794E4-F964-4FDB-90F6-51056BFE4B44}");
        var v = k?.GetValue("Value");
        return v is string s && s == "Deny" ? TweakState.On : TweakState.Off;
    }
    public bool SetLocation(bool disable)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\DeviceAccess\Global\{BFA794E4-F964-4FDB-90F6-51056BFE4B44}");
            k.SetValue("Value", disable ? "Deny" : "Allow");
            return true;
        }
        catch { return false; }
    }

    // ── Cortana ────────────────────────────────────────────────────────────
    public TweakState GetCortana()
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Windows Search");
        var v = k?.GetValue("AllowCortana");
        return v is int i && i == 0 ? TweakState.On : TweakState.Off;
    }
    public bool SetCortana(bool disable)
    {
        try
        {
            using var k = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Windows Search");
            k.SetValue("AllowCortana", disable ? 0 : 1, RegistryValueKind.DWord);
            return true;
        }
        catch { return false; }
    }

    // ── Visual Effects ─────────────────────────────────────────────────────
    public TweakState GetVisualEffects()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects");
        var v = k?.GetValue("VisualFXSetting");
        return v is int i && i == 2 ? TweakState.On : TweakState.Off;
    }
    public bool SetVisualEffects(bool disable)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects");
            k.SetValue("VisualFXSetting", disable ? 2 : 1, RegistryValueKind.DWord);
            return true;
        }
        catch { return false; }
    }

    // ── Profiles ───────────────────────────────────────────────────────────
    public void ApplyGamingProfile()
    {
        SetGameDvr(true);
        SetFullscreenOptim(true);
        SetGpuPriority(true);
        SetSystemResponsiveness(true);
        SetWin32Priority(true);
        SetMousePrecision(true);
        SetHighPerfPlan(true);
        SetNetworkThrottling(true);
        SaveDriftSnapshot();
    }

    // ── Détecteur de dérive d'optimisation ─────────────────────────────────
    // Windows Update et les mises à jour de pilotes (notamment GPU) réinitialisent parfois
    // silencieusement certains réglages sans prévenir l'utilisateur. On mémorise l'état
    // "optimisé" juste après le profil Gaming, pour pouvoir détecter un retour en arrière plus tard.
    private const string DriftRegKey = @"SOFTWARE\EKIPPP-OPTIMIZER\Drift";

    private void SaveDriftSnapshot()
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(DriftRegKey);
            k.SetValue("Applied",       1, RegistryValueKind.DWord);
            k.SetValue("GameDvr",       GetGameDvr()             == TweakState.On ? 1 : 0, RegistryValueKind.DWord);
            k.SetValue("GpuPriority",   GetGpuPriority()         == TweakState.On ? 1 : 0, RegistryValueKind.DWord);
            k.SetValue("SystemResp",    GetSystemResponsiveness()== TweakState.On ? 1 : 0, RegistryValueKind.DWord);
            k.SetValue("Win32Priority", GetWin32Priority()       == TweakState.On ? 1 : 0, RegistryValueKind.DWord);
            k.SetValue("NetThrottle",   GetNetworkThrottling()   == TweakState.On ? 1 : 0, RegistryValueKind.DWord);
            k.SetValue("HighPerf",      GetHighPerfPlan()        == TweakState.On ? 1 : 0, RegistryValueKind.DWord);
        }
        catch { }
    }

    // Renvoie les réglages qui étaient actifs juste après la dernière application du profil Gaming
    // mais qui sont revenus à leur état non-optimisé depuis — liste vide si rien n'a dérivé ou si
    // aucun profil Gaming n'a jamais été appliqué.
    public List<string> CheckOptimizationDrift()
    {
        var drifted = new List<string>();
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(DriftRegKey);
            if (k?.GetValue("Applied") is not int applied || applied != 1) return drifted;

            void Check(string regValue, string label, Func<TweakState> getter)
            {
                if (k.GetValue(regValue) is int wasOn && wasOn == 1 && getter() != TweakState.On)
                    drifted.Add(label);
            }
            Check("GameDvr",       "Xbox Game DVR",              GetGameDvr);
            Check("GpuPriority",   "Priorité GPU pour les jeux", GetGpuPriority);
            Check("SystemResp",    "Réactivité système",         GetSystemResponsiveness);
            Check("Win32Priority", "Priorité des programmes",    GetWin32Priority);
            Check("NetThrottle",   "Limitation réseau",          GetNetworkThrottling);
            Check("HighPerf",      "Plan Haute Performance",     GetHighPerfPlan);
        }
        catch { }
        return drifted;
    }
    public void ApplyBureautiqueProfile()
    {
        SetTelemetry(true);
        SetAdvertisingId(true);
        SetVisualEffects(true);
        SetHighPerfPlan(false);
    }
    public void ApplyMultitacheProfile()
    {
        SetSystemResponsiveness(true);
        SetNetworkThrottling(true);
        SetHighPerfPlan(true);
    }
    public void ApplyPrivacyProfile()
    {
        SetTelemetry(true);
        SetAdvertisingId(true);
        SetLocation(true);
        SetCortana(true);
    }

    // ── Power Plan (Ultimate Performance) ────────────────────────────────
    public string GetActivePowerPlanGuid()
    {
        var output = RunCmd("powercfg", "/getactivescheme");
        var m = System.Text.RegularExpressions.Regex.Match(
            output,
            @"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? m.Value : "381b4222-f694-41f0-9685-ff5bb260df2e";
    }

    private const string UltimatePerfGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    private const string BalancedGuid     = "381b4222-f694-41f0-9685-ff5bb260df2e";

    public bool SetUltimatePerfPlan(bool enable)
    {
        try
        {
            if (enable)
            {
                // Ne duplique le schéma que s'il n'existe pas déjà — sinon "/duplicatescheme"
                // crée un nouveau plan à chaque activation ("Ultimate Performance (2)", "(3)"…)
                // qui s'accumulent silencieusement dans les Options d'alimentation Windows.
                var list = RunCmd("powercfg", "/list");
                if (!list.Contains("Ultimate Performance", StringComparison.OrdinalIgnoreCase))
                    RunCmd("powercfg", $"/duplicatescheme {UltimatePerfGuid}");
                RunCmd("powercfg", $"/setactive {UltimatePerfGuid}");
            }
            else
            {
                RunCmd("powercfg", $"/setactive {BalancedGuid}");
            }
            return true;
        }
        catch { return false; }
    }

    // IsTurboActive (ViewModel) est piloté par ce constat, pas par un souvenir de "j'ai cliqué
    // Turbo" : si l'utilisateur change de plan d'alimentation depuis les Paramètres Windows
    // pendant que l'app tourne, ce reflet redevient faux au prochain LoadTweakStates().
    public bool GetUltimatePerfPlanActive()
        => string.Equals(GetActivePowerPlanGuid(), UltimatePerfGuid, StringComparison.OrdinalIgnoreCase);

    public void SetPowerPlan(string guid)
    {
        try { RunCmd("powercfg", $"/setactive {guid}"); } catch { }
    }

    // ── Network ────────────────────────────────────────────────────────────
    public bool OptimizeTcp()
    {
        bool ok = true;
        ok &= RunCmdOk("netsh", "int tcp set global autotuninglevel=normal");
        ok &= RunCmdOk("netsh", "int tcp set global rss=enabled");
        ok &= RunCmdOk("netsh", "int tcp set global dca=enabled");
        return ok;
    }
    public void FlushDns()
    {
        RunCmd("ipconfig", "/flushdns");
        RunCmd("ipconfig", "/registerdns");
    }

    // ── Core Parking ──────────────────────────────────────────────────────
    // Empêche Windows de mettre des cœurs CPU en veille sous charge légère puis de devoir les
    // "réveiller" brutalement — cause connue de micro-saccades dans les jeux à charge irrégulière
    // comme GTA V/FiveM. Ne nécessite aucune distinction cœurs P/E : s'applique uniformément.
    public TweakState GetCoreParking()
    {
        var output = RunCmd("powercfg", "/query SCHEME_CURRENT SUB_PROCESSOR CPMINCORES");
        var m = System.Text.RegularExpressions.Regex.Match(output, @"Current AC Power Setting Index:\s*0x([0-9a-fA-F]+)");
        if (!m.Success) return TweakState.Unknown;
        int val = Convert.ToInt32(m.Groups[1].Value, 16);
        return val >= 100 ? TweakState.On : TweakState.Off;
    }
    public bool SetCoreParking(bool disableParking)
    {
        try
        {
            int idx = disableParking ? 100 : 5;
            RunCmd("powercfg", $"/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR CPMINCORES {idx}");
            RunCmd("powercfg", $"/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR CPMINCORES {idx}");
            RunCmd("powercfg", "/setactive SCHEME_CURRENT");
            return true;
        }
        catch { return false; }
    }

    // ── Mode MSI (interruptions GPU) ──────────────────────────────────────
    // Bascule le GPU des interruptions "à la ligne" (IRQ partagée, plus sujette aux micro-latences)
    // vers les Message Signaled Interrupts — réduit la micro-saccade. Purement registre, aucun
    // appel bas niveau : le chemin d'instance de périphérique vient de WMI (même mécanisme, déjà
    // utilisé et validé dans CorePinningService pour détecter le CPU).
    private static IEnumerable<string> GetGpuDeviceIds()
    {
        var ids = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT PNPDeviceID FROM Win32_VideoController");
            foreach (ManagementObject o in searcher.Get())
            {
                var id = o["PNPDeviceID"]?.ToString();
                if (!string.IsNullOrEmpty(id)) ids.Add(id);
            }
        }
        catch { }
        return ids;
    }
    public TweakState GetMsiMode()
    {
        var gpus = GetGpuDeviceIds().ToList();
        if (gpus.Count == 0) return TweakState.Unknown;
        foreach (var pnp in gpus)
        {
            using var k = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Enum\{pnp}\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties");
            var v = k?.GetValue("MSISupported");
            if (!(v is int i && i == 1)) return TweakState.Off;
        }
        return TweakState.On;
    }
    public bool SetMsiMode(bool enable)
    {
        bool anyOk = false;
        foreach (var pnp in GetGpuDeviceIds())
        {
            try
            {
                using var k = Registry.LocalMachine.CreateSubKey(
                    $@"SYSTEM\CurrentControlSet\Enum\{pnp}\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties");
                k.SetValue("MSISupported", enable ? 1 : 0, RegistryValueKind.DWord);
                anyOk = true;
            }
            catch { }
        }
        return anyOk;
    }

    // ── Planification GPU matérielle (HAGS) ──────────────────────────────────
    // Laisse le GPU gérer lui-même sa file de commandes au lieu de passer par le scheduler logiciel
    // Windows — réduit la latence sur les GPU/pilotes qui le supportent (Win10 2004+/Win11). Pur
    // registre documenté par Microsoft. Nécessite un redémarrage pour prendre effet.
    public TweakState GetHagsEnabled()
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
        var v = k?.GetValue("HwSchMode");
        return v is int i ? (i == 2 ? TweakState.On : TweakState.Off) : TweakState.Unknown;
    }
    public bool SetHagsEnabled(bool enable)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", writable: true);
            k?.SetValue("HwSchMode", enable ? 2 : 1, RegistryValueKind.DWord);
            return true;
        }
        catch { return false; }
    }

    // ── TCP NoDelay (anti-Nagle) ──────────────────────────────────────────────
    // L'algorithme de Nagle regroupe les petits paquets avant envoi pour économiser la bande
    // passante — au prix d'un délai pouvant aller jusqu'à 200ms, perceptible en jeu multijoueur
    // sur des paquets de position/input très fréquents et minuscules. Clé registre documentée
    // depuis Windows 2000, appliquée sur toutes les interfaces réseau actives.
    public TweakState GetTcpNoDelay()
    {
        try
        {
            using var interfaces = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces");
            if (interfaces == null) return TweakState.Unknown;
            foreach (var name in interfaces.GetSubKeyNames())
            {
                using var k = interfaces.OpenSubKey(name);
                var v = k?.GetValue("TCPNoDelay");
                if (v is int i && i == 1) return TweakState.On;
            }
        }
        catch { return TweakState.Unknown; }
        return TweakState.Off;
    }
    public bool SetTcpNoDelay(bool enable)
    {
        bool anyOk = false;
        try
        {
            using var interfaces = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces", writable: true);
            if (interfaces == null) return false;
            foreach (var name in interfaces.GetSubKeyNames())
            {
                try
                {
                    using var k = interfaces.OpenSubKey(name, writable: true);
                    if (k == null) continue;
                    k.SetValue("TcpAckFrequency", enable ? 1 : 2, RegistryValueKind.DWord);
                    k.SetValue("TCPNoDelay", enable ? 1 : 0, RegistryValueKind.DWord);
                    anyOk = true;
                }
                catch { }
            }
        }
        catch { }
        return anyOk;
    }

    // ── Profil MMCSS "Games" ───────────────────────────────────────────────
    // Windows priorise déjà les threads audio/jeu via MMCSS (Multimedia Class Scheduler Service),
    // mais le profil "Games" par défaut n'utilise pas toujours la priorité GPU maximale. Clé
    // registre officielle et documentée, utilisée par Windows lui-même pour tout process qui
    // s'enregistre dans la catégorie "Games" (dont GTA V/FiveM).
    private const string GamesTaskKey =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";
    public TweakState GetGamesTaskProfile()
    {
        using var k = Registry.LocalMachine.OpenSubKey(GamesTaskKey);
        var v = k?.GetValue("GPU Priority");
        return v is int i ? (i == 8 ? TweakState.On : TweakState.Off) : TweakState.Unknown;
    }
    public bool SetGamesTaskProfile(bool enable)
    {
        try
        {
            using var k = Registry.LocalMachine.CreateSubKey(GamesTaskKey);
            k.SetValue("GPU Priority", enable ? 8 : 2, RegistryValueKind.DWord);
            k.SetValue("Priority", enable ? 6 : 2, RegistryValueKind.DWord);
            k.SetValue("Scheduling Category", enable ? "High" : "Medium", RegistryValueKind.String);
            k.SetValue("SFIO Priority", enable ? "High" : "Normal", RegistryValueKind.String);
            return true;
        }
        catch { return false; }
    }

    // Lit la sortie de façon asynchrone AVANT d'attendre la fin du process : si on attendait
    // WaitForExit() en premier sans drainer le pipe de sortie, un process qui écrit plus que la
    // taille du buffer (quelques Ko) se bloque en écriture — deadlock classique, silencieux, qui
    // fige l'appelant indéfiniment. Timeout dur en filet de sécurité (antivirus/pare-feu tiers qui
    // intercepterait la commande) : le process est tué plutôt que d'attendre indéfiniment.
    private static string RunCmd(string exe, string args, int timeoutMs = 8000)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            if (p == null) return "";
            var readTask = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                return "";
            }
            return readTask.Wait(2000) ? readTask.Result : "";
        }
        catch { return ""; }
    }

    private static bool RunCmdOk(string exe, string args, int timeoutMs = 8000)
    {
        try
        {
            // Pas de redirection de sortie : on ne s'en sert pas, et rediriger sans jamais lire
            // est exactement ce qui cause le deadlock décrit ci-dessus.
            using var p = Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (p == null) return false;
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                return false;
            }
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}
