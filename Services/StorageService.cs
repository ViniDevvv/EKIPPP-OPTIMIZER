using System.Diagnostics;
using System.IO;
using System.Management;

namespace EkipppOptimizer.Services;

public record DriveHealth(string Model, string MediaType, long SizeGB, long FreeGB, int HealthPercent, string Status, bool IsSSD, long ReadSpeedMBs, long WriteSpeedMBs)
{
    public string SpeedLabel => ReadSpeedMBs > 0 ? $"  ·  Lecture ~{ReadSpeedMBs} Mo/s" : "";
}

public record PartitionInfo(string Letter, string Label, long TotalGB, long FreeGB, bool IsSSD)
{
    public long   UsedGB    => TotalGB - FreeGB;
    public double UsedPct   => TotalGB > 0 ? Math.Min(100, (double)UsedGB / TotalGB * 100) : 0;
    public string UsedLabel => $"{UsedGB} Go utilisé  ·  {FreeGB} Go libre  /  {TotalGB} Go";
    public string TypeLabel => IsSSD ? "SSD" : "HDD";
    public string PctLabel  => $"{UsedPct:F0}%";
}

public record BigFileEntry(string Path, long SizeBytes)
{
    public string Name      => System.IO.Path.GetFileName(Path);
    public string Folder    => System.IO.Path.GetDirectoryName(Path) ?? "";
    public string SizeLabel => SizeBytes >= 1L << 30 ? $"{SizeBytes / (1024.0 * 1024 * 1024):F1} Go"
                             : $"{SizeBytes / (1024.0 * 1024):F0} Mo";
}

public record FolderSizeEntry(string Name, long SizeBytes, int Pct = 0)
{
    public string SizeLabel => SizeBytes >= 1L << 30 ? $"{SizeBytes / (1024.0 * 1024 * 1024):F1} Go"
                             : SizeBytes >= 1L << 20 ? $"{SizeBytes / (1024.0 * 1024):F0} Mo"
                             : $"{SizeBytes / 1024.0:F0} Ko";
}

public record BenchmarkResult(long ReadMBs, long WriteMBs)
{
    public string Label => ReadMBs >= 3000
        ? $"Lecture : {ReadMBs} Mo/s  ·  Écriture : {WriteMBs} Mo/s  ·  NVMe ⚡  —  Ultra-rapide, performances maximales ✓"
        : ReadMBs >= 2000
        ? $"Lecture : {ReadMBs} Mo/s  ·  Écriture : {WriteMBs} Mo/s  ·  NVMe ⚡  —  Très rapide ✓"
        : ReadMBs >= 400
        ? $"Lecture : {ReadMBs} Mo/s  ·  Écriture : {WriteMBs} Mo/s  ·  SSD ✓  —  Bonnes performances"
        : $"Lecture : {ReadMBs} Mo/s  ·  Écriture : {WriteMBs} Mo/s  ·  HDD  —  Performances mécaniques (mise à niveau SSD recommandée)";
}

public class StorageService
{
    // ── Partitions — DriveInfo + WMI pour mapper lettre → disque physique ───────
    public List<PartitionInfo> GetPartitions()
    {
        var letterToSsd = GetDriveLetterToSSDMap();
        var result      = new List<PartitionInfo>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Fixed) continue;
                try
                {
                    var letter = d.Name.TrimEnd('\\');
                    var label  = d.VolumeLabel;
                    var total  = d.TotalSize / (1024L * 1024 * 1024);
                    var free   = d.AvailableFreeSpace / (1024L * 1024 * 1024);
                    letterToSsd.TryGetValue(letter, out var isSSD);
                    result.Add(new PartitionInfo(letter, label, total, free, isSSD));
                }
                catch { }
            }
        }
        catch { }
        return result;
    }

    // Index du disque physique (Win32_DiskDrive.Index == MSFT_Disk.Number) → SSD, basé sur le
    // MediaType officiel (fiable) plutôt que sur un filtrage de texte du nom de modèle.
    private static Dictionary<int, bool> GetDiskIndexToSSDMap()
    {
        var map = new Dictionary<int, bool>();
        try
        {
            var scope = new ManagementScope(@"\\.\ROOT\Microsoft\Windows\Storage");
            scope.Connect();
            using var s = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT Number, MediaType FROM MSFT_Disk"));
            foreach (ManagementObject o in s.Get())
            {
                var number    = Convert.ToInt32(o["Number"] ?? -1);
                var mediaType = Convert.ToInt32(o["MediaType"] ?? 0);
                if (number >= 0) map[number] = mediaType == 4 || mediaType == 5;
            }
        }
        catch { }
        return map;
    }

    private static Dictionary<string, bool> GetDriveLetterToSSDMap()
    {
        var map        = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var diskBySsd  = GetDiskIndexToSSDMap();
        try
        {
            using var ldSearcher = new ManagementObjectSearcher(
                "SELECT DeviceID FROM Win32_LogicalDisk WHERE DriveType=3");
            foreach (ManagementObject ld in ldSearcher.Get())
            {
                var letter = ld["DeviceID"]?.ToString();
                if (string.IsNullOrEmpty(letter)) continue;
                try
                {
                    using var partSearcher = new ManagementObjectSearcher(
                        $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{letter}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");
                    foreach (ManagementObject part in partSearcher.Get())
                    {
                        try
                        {
                            using var diskSearcher = new ManagementObjectSearcher(
                                $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{part["DeviceID"]}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");
                            foreach (ManagementObject disk in diskSearcher.Get())
                            {
                                var index = Convert.ToInt32(disk["Index"] ?? -1);
                                map[letter] = diskBySsd.TryGetValue(index, out var isSsd)
                                    ? isSsd
                                    : IsSSDByModel(disk["Model"]?.ToString() ?? "");
                                break;
                            }
                        }
                        catch { }
                        break;
                    }
                }
                catch { }
            }
        }
        catch { }
        return map;
    }

    // ── Disques physiques ─────────────────────────────────────────────────────
    public List<DriveHealth> GetAllDrives()
    {
        var result = new List<DriveHealth>();
        try
        {
            // MSFT_Disk donne le vrai état de santé Windows (SMART analysé par le système)
            // HealthStatus : 0=Sain, 1=Avertissement, 2=Problème
            // MediaType    : 3=HDD, 4=SSD, 5=SCM/NVMe
            var scope = new ManagementScope(@"\\.\ROOT\Microsoft\Windows\Storage");
            scope.Connect();
            using var s = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT FriendlyName, Size, MediaType, HealthStatus FROM MSFT_Disk"));
            foreach (ManagementObject o in s.Get())
            {
                var model      = o["FriendlyName"]?.ToString() ?? "Disque";
                var sizeGB     = Convert.ToInt64(o["Size"] ?? 0L) / (1024L * 1024 * 1024);
                var mediaType  = Convert.ToInt32(o["MediaType"] ?? 0);
                var healthCode = Convert.ToInt32(o["HealthStatus"] ?? 0);
                var isSSD      = mediaType == 4 || mediaType == 5 || IsSSDByModel(model);
                var (healthPct, healthLabel) = healthCode switch
                {
                    0 => (100, "✓ Sain"),
                    1 => (50,  "⚠ Avertissement"),
                    _ => (10,  "✗ Problème détecté"),
                };
                // Vitesses non mesurées ici — 0 signale à l'UI d'afficher "non mesuré"
                // plutôt qu'une valeur générique par type qui n'a rien à voir avec le disque réel.
                result.Add(new DriveHealth(model, isSSD ? "SSD" : "HDD", sizeGB, 0,
                    healthPct, healthLabel, isSSD, 0, 0));
            }
        }
        catch { }

        // Fallback Win32_DiskDrive si MSFT_Disk indisponible
        if (result.Count == 0)
        {
            try
            {
                using var s = new ManagementObjectSearcher("SELECT Model, Size, Status FROM Win32_DiskDrive");
                foreach (var o in s.Get())
                {
                    var model  = o["Model"]?.ToString() ?? "Disque";
                    var sizeGB = Convert.ToInt64(o["Size"] ?? 0L) / (1024L * 1024 * 1024);
                    var status = o["Status"]?.ToString() ?? "OK";
                    var isSSD  = IsSSDByModel(model);
                    result.Add(new DriveHealth(model, isSSD ? "SSD" : "HDD", sizeGB, 0,
                        0, status == "OK" ? "✓ OK" : "✗ Erreur", isSSD, 0, 0));
                }
            }
            catch { }
        }
        return result;
    }

    // ── SMART ────────────────────────────────────────────────────────────────
    public string GetSmartStatus()
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT DeviceID, Status FROM Win32_DiskDrive");
            var parts = new List<string>();
            foreach (var o in s.Get())
            {
                var id = o["DeviceID"]?.ToString()?.Replace(@"\\.\PHYSICALDRIVE", "Disque ") ?? "";
                parts.Add($"{id}: {o["Status"] ?? "OK"}");
            }
            return parts.Count > 0 ? string.Join("  ·  ", parts) : "OK";
        }
        catch { return "Non disponible"; }
    }

    // ── TRIM ─────────────────────────────────────────────────────────────────
    public async Task<bool> RunTrimAsync()
    {
        try
        {
            var drive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))?.TrimEnd('\\') ?? "C:";
            using var p = Process.Start(new ProcessStartInfo("defrag", $"{drive} /L /U")
            { UseShellExecute = false, CreateNoWindow = true });
            if (p == null) return false;
            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    // ── Benchmark ────────────────────────────────────────────────────────────
    // 3 passes, médiane retenue — une seule mesure de débit disque varie facilement de ~10% d'un
    // essai à l'autre (cache, activité de fond, thermal), ce qui suffit à afficher un "gain" après
    // optimisation qui n'est en réalité que du bruit de mesure. La médiane amortit ça sans la
    // complexité d'un calcul de variance.
    public async Task<BenchmarkResult> BenchmarkAsync(IProgress<string> progress)
    {
        return await Task.Run(() =>
        {
            const int Passes = 3;
            var reads  = new List<long>();
            var writes = new List<long>();

            for (int pass = 1; pass <= Passes; pass++)
            {
                progress.Report($"Passe {pass}/{Passes}…");
                var (r, w) = RunSinglePass();
                if (r > 0) reads.Add(r);
                if (w > 0) writes.Add(w);
            }

            long readMBs  = Median(reads);
            long writeMBs = Median(writes);
            progress.Report($"Terminé — médiane sur {Passes} passes.");
            return new BenchmarkResult(readMBs, writeMBs);
        });
    }

    private static long Median(List<long> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[sorted.Count / 2];
    }

    private static (long readMBs, long writeMBs) RunSinglePass()
    {
        var tmp   = Path.Combine(Path.GetTempPath(), $"ekippp_bench_{Guid.NewGuid():N}.tmp");
        const int MB    = 256;
        const int Block = 4 * 1024 * 1024;
        var data  = new byte[Block];
        new Random(42).NextBytes(data);
        long readMBs = 0, writeMBs = 0;
        try
        {
            var sw = Stopwatch.StartNew();
            // FileOptions.WriteThrough force l'écriture réelle sur le média (contourne le
            // cache d'écriture Windows) pour que le débit mesuré soit celui du disque, pas de la RAM.
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, Block, FileOptions.WriteThrough))
                for (int i = 0; i < MB * 1024 * 1024 / Block; i++) fs.Write(data, 0, Block);
            sw.Stop();
            writeMBs = sw.Elapsed.TotalSeconds > 0 ? (long)(MB / sw.Elapsed.TotalSeconds) : MB;

            var buf = new byte[Block];
            sw.Restart();
            using (var fs = new FileStream(tmp, FileMode.Open, FileAccess.Read, FileShare.None, Block))
                while (fs.Read(buf, 0, Block) > 0) { }
            sw.Stop();
            readMBs = sw.Elapsed.TotalSeconds > 0 ? (long)(MB / sw.Elapsed.TotalSeconds) : MB;
        }
        catch { }
        finally { try { File.Delete(tmp); } catch { } }
        return (readMBs, writeMBs);
    }

    // ── Gros fichiers ─────────────────────────────────────────────────────────
    public async Task<List<BigFileEntry>> FindLargestFilesAsync(IProgress<string> progress)
    {
        return await Task.Run(() =>
        {
            var results = new List<BigFileEntry>();
            var dirs = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures"),
            };
            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                progress.Report($"Scan {Path.GetFileName(dir)}…");
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        try { var fi = new FileInfo(f); if (fi.Length >= 50L << 20) results.Add(new BigFileEntry(f, fi.Length)); }
                        catch { }
                    }
                }
                catch { }
            }
            return results.OrderByDescending(x => x.SizeBytes).Take(30).ToList();
        });
    }

    // ── Taille des dossiers ───────────────────────────────────────────────────
    public async Task<List<FolderSizeEntry>> GetTopFolderSizesAsync(IProgress<string> progress)
    {
        return await Task.Run(() =>
        {
            var root   = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
            var result = new List<FolderSizeEntry>();
            try
            {
                foreach (var dir in Directory.GetDirectories(root))
                {
                    var name = Path.GetFileName(dir);
                    if (name.StartsWith('$') || name.Equals("Windows", StringComparison.OrdinalIgnoreCase)) continue;
                    progress.Report($"Calcul {name}…");
                    try
                    {
                        long sz = 0;
                        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                            try { sz += new FileInfo(f).Length; } catch { }
                        if (sz > 0) result.Add(new FolderSizeEntry(name, sz));
                    }
                    catch { }
                }
            }
            catch { }
            return result.OrderByDescending(x => x.SizeBytes).Take(10).ToList();
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static List<string> GetSsdModels()
    {
        var list = new List<string>();
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Model FROM Win32_DiskDrive");
            foreach (var o in s.Get())
                list.Add(o["Model"]?.ToString() ?? "");
        }
        catch { }
        return list;
    }

    private static bool IsSSDByModel(string model)
    {
        var m = model.ToUpperInvariant();
        return m.Contains("SSD") || m.Contains("NVME") || m.Contains("M.2")
            || m.Contains("WD_BLACK") || m.Contains("SABRENT") || m.Contains("MSI")
            || m.Contains("CRUCIAL") || m.Contains("KINGSTON") || m.Contains("SAMSUNG 870")
            || m.Contains("SAMSUNG 980") || m.Contains("SAMSUNG 970") || m.Contains("FIRECUDA");
    }
}
