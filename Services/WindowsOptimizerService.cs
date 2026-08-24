using Microsoft.Win32;
using System.Diagnostics;

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

    public bool SetUltimatePerfPlan(bool enable)
    {
        const string ultimateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";
        const string balancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
        try
        {
            if (enable)
            {
                // Ne duplique le schéma que s'il n'existe pas déjà — sinon "/duplicatescheme"
                // crée un nouveau plan à chaque activation ("Ultimate Performance (2)", "(3)"…)
                // qui s'accumulent silencieusement dans les Options d'alimentation Windows.
                var list = RunCmd("powercfg", "/list");
                if (!list.Contains("Ultimate Performance", StringComparison.OrdinalIgnoreCase))
                    RunCmd("powercfg", $"/duplicatescheme {ultimateGuid}");
                RunCmd("powercfg", $"/setactive {ultimateGuid}");
            }
            else
            {
                RunCmd("powercfg", $"/setactive {balancedGuid}");
            }
            return true;
        }
        catch { return false; }
    }

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

    private static string RunCmd(string exe, string args)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            var output = p?.StandardOutput.ReadToEnd() ?? "";
            p?.WaitForExit();
            return output;
        }
        catch { return ""; }
    }

    private static bool RunCmdOk(string exe, string args)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            if (p == null) return false;
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}
