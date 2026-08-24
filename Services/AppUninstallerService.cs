using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace EkipppOptimizer.Services;

public record InstalledApp(
    string Name, string Publisher, string Version,
    string UninstallString, string InstallLocation,
    long   SizeKB, string InstallDate)
{
    public string SizeLabel => SizeKB >= 1024 * 1024 ? $"{SizeKB / (1024.0 * 1024):F1} Go"
                             : SizeKB >= 1024 ? $"{SizeKB / 1024.0:F0} Mo" : $"{SizeKB} Ko";
    public bool   HasLocation => !string.IsNullOrEmpty(InstallLocation) && Directory.Exists(InstallLocation);
}

public class AppUninstallerService
{
    public List<InstalledApp> GetInstalledApps()
    {
        var result = new List<InstalledApp>();
        var keys = new[]
        {
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",         RegistryHive.LocalMachine),
            (@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", RegistryHive.LocalMachine),
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",         RegistryHive.CurrentUser),
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, hive) in keys)
        {
            try
            {
                using var root = hive == RegistryHive.LocalMachine
                    ? Registry.LocalMachine.OpenSubKey(path)
                    : Registry.CurrentUser.OpenSubKey(path);
                if (root == null) continue;

                foreach (var subName in root.GetSubKeyNames())
                {
                    try
                    {
                        using var sub = root.OpenSubKey(subName);
                        if (sub == null) continue;

                        var name     = sub.GetValue("DisplayName")?.ToString()?.Trim();
                        if (string.IsNullOrEmpty(name)) continue;
                        if (sub.GetValue("SystemComponent") is int sc && sc == 1) continue;
                        if (sub.GetValue("ParentKeyName") != null) continue;
                        if (!seen.Add(name)) continue;

                        var uninstall = sub.GetValue("UninstallString")?.ToString() ?? "";
                        if (string.IsNullOrEmpty(uninstall)) continue;

                        var app = new InstalledApp(
                            Name:            name,
                            Publisher:       sub.GetValue("Publisher")?.ToString() ?? "",
                            Version:         sub.GetValue("DisplayVersion")?.ToString() ?? "",
                            UninstallString: uninstall,
                            InstallLocation: sub.GetValue("InstallLocation")?.ToString() ?? "",
                            SizeKB:          Convert.ToInt64(sub.GetValue("EstimatedSize") ?? 0L),
                            InstallDate:     FormatDate(sub.GetValue("InstallDate")?.ToString())
                        );
                        result.Add(app);
                    }
                    catch { }
                }
            }
            catch { }
        }
        return result.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Codes de sortie considérés comme un succès : 0 = OK, 3010 = OK mais redémarrage requis.
    private static bool IsSuccessExitCode(int code) => code == 0 || code == 3010;

    public async Task<bool> UninstallAsync(InstalledApp app)
    {
        return await Task.Run(() =>
        {
            try
            {
                var str = app.UninstallString.Trim();

                // MSI based: msiexec /x {GUID}
                if (str.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
                {
                    var args = str.Replace("msiexec.exe", "", StringComparison.OrdinalIgnoreCase)
                                  .Replace("MsiExec.exe", "", StringComparison.OrdinalIgnoreCase)
                                  .Trim();
                    if (!args.Contains("/quiet", StringComparison.OrdinalIgnoreCase)
                        && !args.Contains("/passive", StringComparison.OrdinalIgnoreCase))
                        args += " /passive";
                    using var p = Process.Start(new ProcessStartInfo("msiexec.exe", args)
                        { UseShellExecute = true });
                    if (p == null) return false;
                    bool exited = p.WaitForExit(120_000);
                    return exited && IsSuccessExitCode(p.ExitCode);
                }

                // Quoted exe
                if (str.StartsWith('"'))
                {
                    int end = str.IndexOf('"', 1);
                    var exe  = str[1..end];
                    var args = str.Length > end + 2 ? str[(end + 2)..] : "";
                    if (File.Exists(exe))
                    {
                        using var p = Process.Start(new ProcessStartInfo(exe, args)
                            { UseShellExecute = true });
                        if (p == null) return false;
                        bool exited = p.WaitForExit(120_000);
                        return exited && IsSuccessExitCode(p.ExitCode);
                    }
                }

                // Exe brut sans guillemets, éventuellement suivi d'arguments (ex: "C:\...\uninstall.exe /S") —
                // sépare le vrai chemin exécutable des arguments au lieu de tout passer comme FileName.
                {
                    string exe = str, args = "";
                    if (!File.Exists(str))
                    {
                        var spaceIdx = str.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                        if (spaceIdx > 0)
                        {
                            exe  = str[..(spaceIdx + 4)];
                            args = str.Length > spaceIdx + 4 ? str[(spaceIdx + 4)..].Trim() : "";
                        }
                    }
                    using var proc = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
                    if (proc == null) return false;
                    bool exited = proc.WaitForExit(120_000);
                    return exited && IsSuccessExitCode(proc.ExitCode);
                }
            }
            catch { return false; }
        });
    }

    public async Task<long> CleanResidualsAsync(InstalledApp app, IProgress<string>? progress = null)
    {
        return await Task.Run(() =>
        {
            // Re-vérifie que l'app n'a plus d'entrée Uninstall avant de supprimer son dossier —
            // évite de casser une app encore installée (cache UI pas rafraîchi, appel erroné).
            bool stillInstalled = GetInstalledApps().Any(a => string.Equals(a.Name, app.Name, StringComparison.OrdinalIgnoreCase));
            if (stillInstalled)
            {
                progress?.Report($"{app.Name} est toujours installé — nettoyage annulé.");
                return 0L;
            }

            long freed = 0;
            // Dossier d'installation
            if (app.HasLocation)
            {
                progress?.Report($"Suppression {Path.GetFileName(app.InstallLocation)}…");
                freed += DeleteDir(app.InstallLocation);
            }
            // Clés de registre résiduelles
            freed += CleanRegistryResiuals(app.Name);
            return freed;
        });
    }

    private static long DeleteDir(string path)
    {
        long freed = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var size = new FileInfo(f).Length;
                    File.Delete(f);
                    freed += size;
                }
                catch { }
            }
            Directory.Delete(path, recursive: true);
        }
        catch { }
        return freed;
    }

    private static long CleanRegistryResiuals(string appName)
    {
        long count = 0;
        // Cherche uniquement dans les clés Uninstall connues — ne touche jamais SOFTWARE\<nom> directement
        var uninstallPaths = new (string path, RegistryKey hive)[]
        {
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",             Registry.CurrentUser),
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",             Registry.LocalMachine),
            (@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", Registry.LocalMachine),
        };

        foreach (var (path, hive) in uninstallPaths)
        {
            try
            {
                using var root = hive.OpenSubKey(path, writable: true);
                if (root == null) continue;
                foreach (var subName in root.GetSubKeyNames().ToArray())
                {
                    try
                    {
                        using var sub = root.OpenSubKey(subName);
                        var name = sub?.GetValue("DisplayName")?.ToString();
                        if (string.Equals(name, appName, StringComparison.OrdinalIgnoreCase))
                        {
                            root.DeleteSubKeyTree(subName, throwOnMissingSubKey: false);
                            count++;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
        return count * 512;
    }

    private static string FormatDate(string? raw)
    {
        if (string.IsNullOrEmpty(raw) || raw.Length < 8) return "";
        try
        {
            return $"{raw[6..8]}/{raw[4..6]}/{raw[0..4]}";
        }
        catch { return raw; }
    }
}
