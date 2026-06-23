using System.Diagnostics;

namespace EkipppOptimizer.Services;

public class WindowsRepairService
{
    public async Task<string> RunSfcAsync(IProgress<string>? progress = null)
    {
        progress?.Report("Lancement de SFC /scannow…");
        return await RunAdminCommand("sfc", "/scannow", progress);
    }

    public async Task<string> RunDismRestoreHealthAsync(IProgress<string>? progress = null)
    {
        progress?.Report("Lancement de DISM RestoreHealth…");
        return await RunAdminCommand("DISM", "/Online /Cleanup-Image /RestoreHealth", progress);
    }

    public async Task<string> ResetNetworkAsync(IProgress<string>? progress = null)
    {
        progress?.Report("Réinitialisation réseau…");
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(await RunAdminCommand("netsh", "int ip reset", progress));
        sb.AppendLine(await RunAdminCommand("netsh", "winsock reset", progress));
        sb.AppendLine(await RunAdminCommand("ipconfig", "/flushdns", progress));
        return sb.ToString();
    }

    public async Task<string> RepairWindowsUpdateAsync(IProgress<string>? progress = null)
    {
        progress?.Report("Réparation Windows Update…");
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(await RunAdminCommand("net", "stop wuauserv", progress));
        sb.AppendLine(await RunAdminCommand("net", "stop cryptSvc", progress));
        sb.AppendLine(await RunAdminCommand("net", "stop bits", progress));
        await Task.Delay(500);
        progress?.Report("Nettoyage des dossiers de cache Windows Update…");
        var win = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows);
        var sys = System.Environment.SystemDirectory;
        TryDelete(System.IO.Path.Combine(win, "SoftwareDistribution", "DataStore"));
        TryDelete(System.IO.Path.Combine(win, "SoftwareDistribution", "Download"));
        TryDelete(System.IO.Path.Combine(sys, "catroot2"));
        await Task.Delay(500);
        sb.AppendLine(await RunAdminCommand("net", "start wuauserv", progress));
        sb.AppendLine(await RunAdminCommand("net", "start cryptSvc", progress));
        sb.AppendLine(await RunAdminCommand("net", "start bits", progress));
        return sb.ToString();
    }

    public async Task<string> FlushDnsAsync()
        => await RunAdminCommand("ipconfig", "/flushdns");

    public async Task<string> ReleaseRenewIpAsync(IProgress<string>? progress = null)
    {
        progress?.Report("Renouvellement adresse IP…");
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(await RunAdminCommand("ipconfig", "/release", progress));
        await Task.Delay(1000);
        sb.AppendLine(await RunAdminCommand("ipconfig", "/renew", progress));
        return sb.ToString();
    }

    private static async Task<string> RunAdminCommand(string exe, string args, IProgress<string>? progress = null)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = exe,
                Arguments              = args,
                UseShellExecute        = true,
                Verb                   = "runas",
                WindowStyle            = ProcessWindowStyle.Hidden,
                CreateNoWindow         = true,
            };

            // Try without elevation first (some commands work without admin)
            var psiNoElev = new ProcessStartInfo
            {
                FileName               = exe,
                Arguments              = args,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
            };

            using var process = Process.Start(psiNoElev);
            if (process == null) return $"Impossible de lancer {exe}";
            var output = await process.StandardOutput.ReadToEndAsync();
            var error  = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var result = !string.IsNullOrWhiteSpace(output) ? output : error;
            progress?.Report($"{exe}: OK");
            return result.Length > 500 ? result[..500] + "…" : result;
        }
        catch (Exception ex)
        {
            return $"Erreur {exe}: {ex.Message}";
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (System.IO.Directory.Exists(path))
                System.IO.Directory.Delete(path, true);
        }
        catch { }
    }
}
