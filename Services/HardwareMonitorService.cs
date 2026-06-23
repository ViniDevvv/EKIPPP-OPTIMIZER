using System.Diagnostics;
using System.IO;
using System.Management;

namespace EkipppOptimizer.Services;

public record HardwareSnapshot(
    double   CpuPercent,
    double   RamUsedMB,
    double   RamTotalMB,
    double   DiskReadMBs,
    double   DiskWriteMBs,
    double   NetworkDownKBs,
    double   NetworkUpKBs,
    double[] CorePercents,
    double   DiskCUsedPercent);

public class HardwareMonitorService : IDisposable
{
    private PerformanceCounter?   _cpu;
    private PerformanceCounter?   _ramAvail;
    private PerformanceCounter?   _diskRead;
    private PerformanceCounter?   _diskWrite;
    private PerformanceCounter[]  _netRecvCounters = [];
    private PerformanceCounter[]  _netSentCounters = [];
    private PerformanceCounter[]  _coreCounters    = [];
    private double                _ramTotalMB;
    private bool                  _initialized;

    public void Initialize()
    {
        if (_initialized) return;
        try
        {
            _cpu      = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
            _ramAvail = new PerformanceCounter("Memory",    "Available MBytes",  "",       true);
            _cpu.NextValue();
        }
        catch { }

        try
        {
            _diskRead  = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec",  "_Total", true);
            _diskWrite = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total", true);
            _diskRead.NextValue();
            _diskWrite.NextValue();
        }
        catch { }

        try
        {
            var cat      = new PerformanceCounterCategory("Network Interface");
            var adapters = cat.GetInstanceNames();
            _netRecvCounters = adapters
                .Select(a => new PerformanceCounter("Network Interface", "Bytes Received/sec", a, true))
                .ToArray();
            _netSentCounters = adapters
                .Select(a => new PerformanceCounter("Network Interface", "Bytes Sent/sec", a, true))
                .ToArray();
            foreach (var c in _netRecvCounters) c.NextValue();
            foreach (var c in _netSentCounters) c.NextValue();
        }
        catch { }

        try
        {
            var cat = new PerformanceCounterCategory("Processor");
            var instances = cat.GetInstanceNames()
                .Where(n => n != "_Total")
                .OrderBy(n => { int.TryParse(n, out var v); return v; })
                .ToArray();
            _coreCounters = instances
                .Select(i => new PerformanceCounter("Processor", "% Processor Time", i, true))
                .ToArray();
            foreach (var c in _coreCounters) c.NextValue();
        }
        catch { }

        _ramTotalMB  = GetTotalRamMB();
        _initialized = true;
    }

    public HardwareSnapshot Sample()
    {
        double cpu = 0, availMB = 0, diskR = 0, diskW = 0, netD = 0, netU = 0;
        try { cpu     = _cpu?.NextValue()      ?? 0; } catch { }
        try { availMB = _ramAvail?.NextValue() ?? 0; } catch { }
        try { diskR   = (_diskRead?.NextValue()  ?? 0) / (1024 * 1024); } catch { }
        try { diskW   = (_diskWrite?.NextValue() ?? 0) / (1024 * 1024); } catch { }
        try { netD = _netRecvCounters.Sum(c => { try { return c.NextValue(); } catch { return 0f; } }) / 1024; } catch { }
        try { netU = _netSentCounters.Sum(c => { try { return c.NextValue(); } catch { return 0f; } }) / 1024; } catch { }

        var cores = _coreCounters
            .Select(c => { try { return Math.Round((double)c.NextValue(), 1); } catch { return 0.0; } })
            .ToArray();

        double diskCPct = 0;
        try
        {
            var winRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
            var d = new DriveInfo(winRoot);
            diskCPct = Math.Round((1.0 - (double)d.AvailableFreeSpace / d.TotalSize) * 100, 1);
        }
        catch { }

        var ramUsed = _ramTotalMB - availMB;
        return new HardwareSnapshot(
            Math.Round(cpu, 1),
            Math.Round(Math.Max(0, ramUsed), 0),
            _ramTotalMB,
            Math.Round(Math.Max(0, diskR), 2),
            Math.Round(Math.Max(0, diskW), 2),
            Math.Round(Math.Max(0, netD), 1),
            Math.Round(Math.Max(0, netU), 1),
            cores,
            diskCPct);
    }

    private double GetTotalRamMB()
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
            foreach (var o in s.Get())
                return Convert.ToDouble(o["TotalVisibleMemorySize"]) / 1024.0;
        }
        catch { }
        return 8192;
    }

    public void Dispose()
    {
        _cpu?.Dispose();
        _ramAvail?.Dispose();
        _diskRead?.Dispose();
        _diskWrite?.Dispose();
        foreach (var c in _netRecvCounters) c.Dispose();
        foreach (var c in _netSentCounters) c.Dispose();
        foreach (var c in _coreCounters) c.Dispose();
    }
}
