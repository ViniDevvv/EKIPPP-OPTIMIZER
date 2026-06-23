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
    public void SetAdvertisingId(bool disable)
    {
        using var k = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo");
        k.SetValue("Enabled", disable ? 0 : 1, RegistryValueKind.DWord);
    }

    // ── Location ───────────────────────────────────────────────────────────
    public TweakState GetLocation()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\DeviceAccess\Global\{BFA794E4-F964-4FDB-90F6-51056BFE4B44}");
        var v = k?.GetValue("Value");
        return v is string s && s == "Deny" ? TweakState.On : TweakState.Off;
    }
    public void SetLocation(bool disable)
    {
        using var k = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\DeviceAccess\Global\{BFA794E4-F964-4FDB-90F6-51056BFE4B44}");
        k.SetValue("Value", disable ? "Deny" : "Allow");
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
    public void SetVisualEffects(bool disable)
    {
        using var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects");
        k.SetValue("VisualFXSetting", disable ? 2 : 1, RegistryValueKind.DWord);
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
                // Active le schéma s'il n'est pas encore présent sur ce PC
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
    public void OptimizeTcp()
    {
        RunCmd("netsh", "int tcp set global autotuninglevel=normal");
        RunCmd("netsh", "int tcp set global rss=enabled");
        RunCmd("netsh", "int tcp set global dca=enabled");
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
}
