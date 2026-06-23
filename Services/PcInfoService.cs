using System.Management;
using Microsoft.Win32;

namespace EkipppOptimizer.Services;

public record CpuInfo(string Name, int Cores, int Threads, double SpeedGHz, double LoadPercent);
public record GpuInfo(string Name, long VramMB);
public record RamInfo(long TotalMB, long AvailableMB);
public record DiskInfo(string Letter, string Label, long TotalGB, long FreeGB, bool IsSSD);
public record NetworkAdapterInfo(string Name, string? IpAddress, string? MacAddress);
public record BatteryInfo(int ChargePercent, bool IsCharging, bool HasBattery);
public record MotherboardInfo(string Manufacturer, string Product);

public class PcInfoService
{
    public CpuInfo GetCpuInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                var name    = obj["Name"]?.ToString()?.Trim() ?? "CPU inconnu";
                var cores   = Convert.ToInt32(obj["NumberOfCores"]);
                var threads = Convert.ToInt32(obj["NumberOfLogicalProcessors"]);
                var speed   = Convert.ToDouble(obj["MaxClockSpeed"]) / 1000.0;
                var load    = GetCpuLoad();
                return new CpuInfo(name, cores, threads, Math.Round(speed, 2), load);
            }
        }
        catch { }
        return new CpuInfo("CPU inconnu", 4, 8, 3.0, 0);
    }

    private double GetCpuLoad()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT LoadPercentage FROM Win32_Processor");
            foreach (var obj in searcher.Get())
                return Convert.ToDouble(obj["LoadPercentage"]);
        }
        catch { }
        return 0;
    }

    public List<GpuInfo> GetGpuInfo()
    {
        var list = new List<GpuInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "GPU inconnu";
                if (name.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)) continue;
                long vram = 0;
                try { vram = Convert.ToInt64(obj["AdapterRAM"]) / (1024 * 1024); } catch { }
                list.Add(new GpuInfo(name, vram));
            }
        }
        catch { }
        if (list.Count == 0) list.Add(new GpuInfo("GPU inconnu", 0));
        return list;
    }

    public RamInfo GetRamInfo()
    {
        long totalMb = 0, availMb = 0;
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (var obj in searcher.Get())
            {
                totalMb = Convert.ToInt64(obj["TotalVisibleMemorySize"]) / 1024;
                availMb = Convert.ToInt64(obj["FreePhysicalMemory"]) / 1024;
            }
        }
        catch { }
        return new RamInfo(totalMb, availMb);
    }

    public List<DiskInfo> GetDiskInfo()
    {
        var list = new List<DiskInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT DeviceID, VolumeName, Size, FreeSpace, DriveType FROM Win32_LogicalDisk WHERE DriveType=3");
            foreach (var obj in searcher.Get())
            {
                var letter = obj["DeviceID"]?.ToString() ?? "?";
                var label  = obj["VolumeName"]?.ToString() ?? "";
                long size  = Convert.ToInt64(obj["Size"]);
                long free  = Convert.ToInt64(obj["FreeSpace"]);
                var isSSD  = IsSSD(letter.Replace(":", ""));
                list.Add(new DiskInfo(letter, label, size / (1024L * 1024 * 1024), free / (1024L * 1024 * 1024), isSSD));
            }
        }
        catch { }
        return list;
    }

    private bool IsSSD(string driveLetter)
    {
        try
        {
            var query = $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{driveLetter}:'}} WHERE AssocClass=Win32_LogicalDiskToPartition";
            using var partSearcher = new ManagementObjectSearcher(query);
            foreach (var part in partSearcher.Get())
            {
                var partId = part["DeviceID"]?.ToString() ?? "";
                var q2 = $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partId}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition";
                using var driveSearcher = new ManagementObjectSearcher(q2);
                foreach (var drive in driveSearcher.Get())
                {
                    var mediaType = drive["MediaType"]?.ToString() ?? "";
                    return mediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase)
                        || mediaType.Contains("Solid", StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch { }
        return false;
    }

    public List<NetworkAdapterInfo> GetNetworkAdapters()
    {
        var list = new List<NetworkAdapterInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Description, IPAddress, MACAddress FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=True");
            foreach (var obj in searcher.Get())
            {
                var name = obj["Description"]?.ToString() ?? "Adaptateur";
                var mac  = obj["MACAddress"]?.ToString();
                string? ip = null;
                if (obj["IPAddress"] is string[] ips && ips.Length > 0) ip = ips[0];
                list.Add(new NetworkAdapterInfo(name, ip, mac));
            }
        }
        catch { }
        return list;
    }

    public BatteryInfo GetBatteryInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery");
            foreach (var obj in searcher.Get())
            {
                var charge  = Convert.ToInt32(obj["EstimatedChargeRemaining"]);
                var status  = Convert.ToInt32(obj["BatteryStatus"]);
                var charging = status == 2;
                return new BatteryInfo(charge, charging, true);
            }
        }
        catch { }
        return new BatteryInfo(0, false, false);
    }

    public MotherboardInfo GetMotherboard()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
            foreach (var obj in searcher.Get())
            {
                return new MotherboardInfo(
                    obj["Manufacturer"]?.ToString() ?? "?",
                    obj["Product"]?.ToString() ?? "?");
            }
        }
        catch { }
        return new MotherboardInfo("Inconnu", "Inconnu");
    }

    public string GetWindowsVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key != null)
            {
                var name  = key.GetValue("ProductName")?.ToString() ?? "Windows";
                var build = key.GetValue("CurrentBuildNumber")?.ToString() ?? "";
                var ubr   = key.GetValue("UBR")?.ToString() ?? "";
                // Microsoft n'a jamais mis à jour ProductName pour Win11 — build >= 22000 = Windows 11
                if (int.TryParse(build, out int b) && b >= 22000)
                    name = name.Replace("Windows 10", "Windows 11");
                return $"{name} (Build {build}.{ubr})";
            }
        }
        catch { }
        return Environment.OSVersion.ToString();
    }

    public int ComputeHealthScore(CpuInfo cpu, RamInfo ram, List<DiskInfo> disks, GpuInfo? gpu)
    {
        double score = 0;

        // CPU: cores + speed (40 pts)
        double cpuPts = Math.Min(cpu.Cores / 8.0, 1.0) * 20 + Math.Min(cpu.SpeedGHz / 4.0, 1.0) * 20;
        score += cpuPts;

        // RAM (30 pts)
        double ramPts = Math.Min(ram.TotalMB / 16384.0, 1.0) * 30;
        score += ramPts;

        // Disque: espace libre (20 pts)
        if (disks.Count > 0)
        {
            var main = disks[0];
            double freeRatio = main.TotalGB > 0 ? (double)main.FreeGB / main.TotalGB : 0.5;
            double diskPts = Math.Min(freeRatio * 2, 1.0) * 15 + (main.IsSSD ? 5 : 0);
            score += diskPts;
        }

        // GPU (10 pts)
        if (gpu != null && gpu.VramMB > 0)
            score += Math.Min(gpu.VramMB / 8192.0, 1.0) * 10;

        return (int)Math.Clamp(score, 10, 100);
    }
}
