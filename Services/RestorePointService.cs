using System.Diagnostics;
using System.Management;
using Microsoft.Win32;

namespace EkipppOptimizer.Services;

public record RestorePointEntry(int SequenceNumber, string Description, DateTime CreatedAt);

public record RestorePointResult(bool Success, string Message);

public class RestorePointService
{
    public Task<RestorePointResult> CreateRestorePointAsync(string description = "EKIPPP-OPTIMIZER sauvegarde")
    {
        return Task.Run(() =>
        {
            // Lever la limite de fréquence 24h
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", writable: true);
                k?.SetValue("SystemRestorePointCreationFrequency", 0, RegistryValueKind.DWord);
            }
            catch { }

            // Tenter WMI Enable + CreateRestorePoint directement (app runs as admin)
            try
            {
                var scope = new ManagementScope(@"\\.\root\default");
                scope.Connect();
                using var mc = new ManagementClass(scope, new ManagementPath("SystemRestore"), null);
                mc.Get();

                // Activer la protection système sur C:
                try
                {
                    var enableParams = mc.GetMethodParameters("Enable");
                    enableParams["Drive"] = "C:\\";
                    mc.InvokeMethod("Enable", enableParams, null);
                }
                catch { }

                // Créer le point de restauration
                var inParams = mc.GetMethodParameters("CreateRestorePoint");
                inParams["Description"]      = description;
                inParams["RestorePointType"] = 12;  // MODIFY_SETTINGS
                inParams["EventType"]        = 100; // BEGIN_SYSTEM_CHANGE

                var outParams = mc.InvokeMethod("CreateRestorePoint", inParams, null);
                int returnVal = Convert.ToInt32(outParams["ReturnValue"]);

                if (returnVal == 0)
                    return new RestorePointResult(true, "Point de restauration créé ✓");

                // Code 1 = déjà créé aujourd'hui (fréquence)
                if (returnVal == 1)
                    return new RestorePointResult(false,
                        "Un point a déjà été créé dans les dernières 24h. La limite a été levée — réessaie.");

                return new RestorePointResult(false,
                    $"WMI a retourné le code {returnVal}. La Protection du système est peut-être désactivée sur C:.");
            }
            catch (Exception ex)
            {
                return new RestorePointResult(false,
                    $"Erreur WMI : {ex.Message} — Active manuellement : Panneau de configuration → Système → Protection du système → C: → Activer la protection.");
            }
        });
    }

    public async Task<bool> DeleteRestorePointAsync(int sequenceNumber)
    {
        return await Task.Run(() =>
        {
            // Remove-ComputerRestorePoint appelle SRRemoveRestorePoint() de srclient.dll
            // → supprime la shadow copy VSS réelle (contrairement à Remove-WmiObject qui ne
            //   supprime que l'entrée catalogue et laisse la shadow copy intacte).
            string? tmpFile = null;
            try
            {
                tmpFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    $"ekippp_delrp_{sequenceNumber}.ps1");
                System.IO.File.WriteAllText(tmpFile,
                    $"try {{ Remove-ComputerRestorePoint -RestorePoint {sequenceNumber}; exit 0 }} catch {{ exit 1 }}");

                var psi = new ProcessStartInfo("powershell.exe",
                    $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{tmpFile}\"")
                {
                    UseShellExecute = false, RedirectStandardOutput = true,
                    RedirectStandardError = true, CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                p?.WaitForExit();
                if (p?.ExitCode == 0) return true;
            }
            catch { }
            finally
            {
                try { if (tmpFile != null) System.IO.File.Delete(tmpFile); } catch { }
            }

            // Fallback P/Invoke SRRemoveRestorePoint via srsclient.dll
            try
            {
                SRRemoveRestorePoint((uint)sequenceNumber);
                return true;
            }
            catch { }

            return false;
        });
    }

    [System.Runtime.InteropServices.DllImport("srclient.dll")]
    private static extern int SRRemoveRestorePoint(uint dwRPNum);

    public List<RestorePointEntry> GetRestorePoints()
    {
        // PowerShell Get-ComputerRestorePoint is more reliable than WMI enumeration on Windows 11
        try
        {
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -Command \"Get-ComputerRestorePoint | ForEach-Object { $_.SequenceNumber.ToString() + '|' + $_.Description + '|' + $_.CreationTime }\"")
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (output.Trim().Length > 0)
                {
                    var list = new List<RestorePointEntry>();
                    foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        var parts = line.Split('|', 3);
                        if (parts.Length < 3) continue;
                        if (!int.TryParse(parts[0].Trim(), out int seq)) continue;
                        var desc = parts[1].Trim();
                        var dt   = ParseWmiDate(parts[2].Trim());
                        list.Add(new RestorePointEntry(seq, desc, dt));
                    }
                    list.Sort((a, b) => b.SequenceNumber.CompareTo(a.SequenceNumber));
                    return list;
                }
            }
        }
        catch { }

        // Fallback: WMI direct
        var wmiList = new List<RestorePointEntry>();
        try
        {
            var scope = new ManagementScope(@"\\.\root\default");
            scope.Connect();
            using var s = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT * FROM SystemRestore ORDER BY SequenceNumber DESC"));
            foreach (ManagementObject o in s.Get())
            {
                var seq   = Convert.ToInt32(o["SequenceNumber"]);
                var desc  = o["Description"]?.ToString() ?? "";
                var dtRaw = o["CreationTime"]?.ToString() ?? "";
                var dt    = ParseWmiDate(dtRaw);
                wmiList.Add(new RestorePointEntry(seq, desc, dt));
            }
        }
        catch { }
        return wmiList;
    }

    private static DateTime ParseWmiDate(string raw)
    {
        try
        {
            if (raw.Length >= 14)
                return new DateTime(
                    int.Parse(raw[..4]),    int.Parse(raw[4..6]),
                    int.Parse(raw[6..8]),   int.Parse(raw[8..10]),
                    int.Parse(raw[10..12]), int.Parse(raw[12..14]));
        }
        catch { }
        return DateTime.MinValue;
    }
}
