using System.Management;

namespace EkipppOptimizer.Services;

public record DriverEntry(
    string DeviceName,
    string DriverVersion,
    string Manufacturer,
    string DeviceClass,
    string Status,
    bool NeedsAttention);

public class DriverService
{
    private static readonly string[] PriorityClasses =
        ["Display", "Net", "AudioEndpoint", "Media", "USB", "Bluetooth", "HDC", "SCSIAdapter", "DiskDrive"];

    private static readonly string[] SkipClasses =
        ["System", "Computer", "Processor", "Unknown"];

    public List<DriverEntry> GetDrivers()
    {
        // Récupère les appareils avec un code d'erreur WMI (vrais problèmes matériels)
        var errorDevices = GetErrorDeviceNames();
        var list = new List<DriverEntry>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceName, DriverVersion, Manufacturer, DeviceClass, IsSigned FROM Win32_PnPSignedDriver");

            foreach (var obj in searcher.Get())
            {
                var name    = obj["DeviceName"]?.ToString()   ?? "";
                var version = obj["DriverVersion"]?.ToString() ?? "N/A";
                var mfr     = obj["Manufacturer"]?.ToString() ?? "";
                var cls     = obj["DeviceClass"]?.ToString()  ?? "";
                var signed  = obj["IsSigned"] is bool b && b;

                if (string.IsNullOrWhiteSpace(name)) continue;
                if (SkipClasses.Contains(cls, StringComparer.OrdinalIgnoreCase)) continue;

                // Nécessite attention si : pilote non signé OU appareil en erreur WMI
                var hasError = errorDevices.Contains(name);
                var needs = !signed || hasError;
                var status = hasError ? "Erreur" : "OK";

                list.Add(new DriverEntry(name, version, mfr, cls, status, needs));
            }
        }
        catch { }

        return list
            .OrderByDescending(d => d.NeedsAttention)
            .ThenBy(d => d.DeviceClass)
            .ThenBy(d => d.DeviceName)
            .ToList();
    }

    public List<DriverEntry> GetPriorityDrivers()
    {
        return GetDrivers()
            .Where(d => PriorityClasses.Contains(d.DeviceClass, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static HashSet<string> GetErrorDeviceNames()
    {
        var errors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode != 0");
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString();
                if (!string.IsNullOrEmpty(name)) errors.Add(name);
            }
        }
        catch { }
        return errors;
    }
}
