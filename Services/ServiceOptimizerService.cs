using System.ServiceProcess;

namespace EkipppOptimizer.Services;

public record ServiceToggleResult(string Name, string DisplayName, bool WasStopped);

public class ServiceOptimizerService
{
    // Services sûrs à suspendre pendant le gaming — non critiques, redémarrables
    private static readonly string[] GamingTargets =
    [
        "SysMain",        // Superfetch / Memory Compression (RAM gaspillée)
        "WSearch",        // Windows Search indexing (I/O en arrière-plan)
        "DiagTrack",      // Télémétrie Microsoft
        "WMPNetworkSvc",  // Windows Media Player Network Sharing
        "XblGameSave",    // Xbox Game Save (cloud saves Xbox)
        "XboxNetApiSvc",  // Xbox Network API
        "Fax",            // Service Fax (inutile sur 99% des PC gaming)
    ];

    private readonly Dictionary<string, bool> _stoppedByUs = [];

    public bool IsOptimized { get; private set; }

    public List<ServiceToggleResult> OptimizeForGaming()
    {
        _stoppedByUs.Clear();
        var results = new List<ServiceToggleResult>();

        foreach (var name in GamingTargets)
        {
            try
            {
                using var svc = new ServiceController(name);
                // Vérifie que le service existe sur ce PC
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
    }
}
