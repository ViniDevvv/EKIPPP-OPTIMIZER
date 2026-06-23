using System.ServiceProcess;
using Microsoft.Win32;

namespace EkipppOptimizer.Services;

public record ServiceToggleResult(string Name, string DisplayName, bool WasStopped);

public class ServiceOptimizerService
{
    private static readonly string[] GamingTargets =
    [
        "SysMain",        // Superfetch / Memory Compression
        "WSearch",        // Windows Search indexing (I/O en arrière-plan)
        "DiagTrack",      // Télémétrie Microsoft
        "WMPNetworkSvc",  // Windows Media Player Network Sharing
        "XblGameSave",    // Xbox Game Save
        "XboxNetApiSvc",  // Xbox Network API
        "Fax",            // Service Fax
    ];

    private const string RegPath = @"SOFTWARE\EKIPPP-OPTIMIZER\ServiceOptimizer";

    private readonly Dictionary<string, bool> _stoppedByUs = [];

    public bool IsOptimized { get; private set; }

    public ServiceOptimizerService()
    {
        LoadPersistedState();
    }

    public List<ServiceToggleResult> OptimizeForGaming()
    {
        _stoppedByUs.Clear();
        var results = new List<ServiceToggleResult>();

        foreach (var name in GamingTargets)
        {
            try
            {
                using var svc = new ServiceController(name);
                _ = svc.Status;
                bool wasRunning = svc.Status == ServiceControllerStatus.Running;
                if (wasRunning)
                {
                    svc.Stop();
                    svc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(4));
                    _stoppedByUs[name] = true;
                    results.Add(new ServiceToggleResult(name, svc.DisplayName, WasStopped: true));
                }
            }
            catch { }
        }

        IsOptimized = true;
        PersistState();
        return results;
    }

    public void RestoreServices()
    {
        foreach (var name in _stoppedByUs.Keys)
        {
            try
            {
                using var svc = new ServiceController(name);
                if (svc.Status == ServiceControllerStatus.Stopped)
                    svc.Start();
            }
            catch { }
        }
        _stoppedByUs.Clear();
        IsOptimized = false;
        ClearPersistedState();
    }

    private void PersistState()
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RegPath);
            k.SetValue("Stopped", string.Join(",", _stoppedByUs.Keys));
            k.SetValue("IsOptimized", 1);
        }
        catch { }
    }

    private void LoadPersistedState()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RegPath);
            if (k == null) return;
            IsOptimized = Convert.ToInt32(k.GetValue("IsOptimized", 0)) == 1;
            var stopped = k.GetValue("Stopped")?.ToString() ?? "";
            if (!string.IsNullOrEmpty(stopped))
                foreach (var name in stopped.Split(','))
                    if (!string.IsNullOrEmpty(name)) _stoppedByUs[name] = true;
        }
        catch { }
    }

    private static void ClearPersistedState()
    {
        try { Registry.CurrentUser.DeleteSubKey(RegPath, false); } catch { }
    }
}
