using System.IO;

namespace EkipppOptimizer.Services;

public record CleanCategory(string Name, string Description, IReadOnlyList<string> Paths, long SizeBytes);

public class CleanerService
{
    public List<CleanCategory> ScanAll(IProgress<string>? progress = null)
    {
        // Navigateurs, jeux (Steam/Epic/Discord/FiveM) : gérés dans leurs sections dédiées
        // pour éviter tout doublon avec GameCacheService et BrowserCleanerService.
        var categories = new List<CleanCategory>
        {
            Scan("Temp utilisateur",     "100% sans risque — fichiers temporaires inutilisés créés par Windows et vos apps. Aucune donnée personnelle.",             GetUserTempPaths(),    progress),
            Scan("Temp Windows",         "100% sans risque — cache système que Windows recrée automatiquement. Aucun impact sur le fonctionnement.",                 GetWinTempPaths(),     progress),
            Scan("Cache Windows Update", "100% sans risque — fichiers d'installation déjà appliqués. Les mises à jour restent actives.",                             GetWuCachePaths(),     progress),
            Scan("Rapports d'erreurs",   "100% sans risque — journaux de crash envoyés à Microsoft. Supprimés, ils seront recréés si nécessaire.",                  GetErrorReportPaths(), progress),
            Scan("Miniatures",           "100% sans risque — aperçus d'images régénérés automatiquement à l'ouverture du dossier.",                                 GetThumbnailPaths(),   progress),
            Scan("Cache NVIDIA",         "Sans risque — shaders GPU recompilés automatiquement. 1ère session peut avoir de légers stutters (quelques min).",        GetNvidiaCachePaths(), progress),
            Scan("Fichiers .log",        "100% sans risque — journaux d'applications recréés automatiquement. Aucune donnée utile supprimée.",                      GetLogPaths(),         progress),
        };
        return categories;
    }

    private CleanCategory Scan(string name, string desc, IEnumerable<string> paths, IProgress<string>? progress)
    {
        progress?.Report($"Analyse: {name}…");
        var files   = new List<string>();
        long total  = 0;

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                try { total += new FileInfo(path).Length; files.Add(path); } catch { }
                continue;
            }
            if (!Directory.Exists(path)) continue;
            try
            {
                var opts = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible    = true,
                    AttributesToSkip      = FileAttributes.ReparsePoint,
                };
                foreach (var fi in new DirectoryInfo(path).EnumerateFiles("*", opts))
                {
                    try { total += fi.Length; files.Add(fi.FullName); } catch { }
                }
            }
            catch { }
        }

        return new CleanCategory(name, desc, files.AsReadOnly(), total);
    }

    public (int deleted, long freed) Clean(IEnumerable<CleanCategory> categories, IProgress<string>? progress = null)
    {
        int count = 0; long freed = 0;
        foreach (var cat in categories)
        {
            progress?.Report($"Nettoyage: {cat.Name}…");
            foreach (var file in cat.Paths)
            {
                try
                {
                    var info = new FileInfo(file);
                    if (!info.Exists) continue;
                    freed += info.Length;
                    info.Delete();
                    count++;
                }
                catch { }
            }
        }
        return (count, freed);
    }

    public long GetRecycleBinSize()
    {
        long size = 0;
        try
        {
            // Scan corbeille sur tous les lecteurs fixes
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
                var recycleRoot = new DirectoryInfo(Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin"));
                if (!recycleRoot.Exists) continue;
                foreach (var f in recycleRoot.EnumerateFiles("*", SearchOption.AllDirectories))
                    try { size += f.Length; } catch { }
            }
        }
        catch { }
        return size;
    }

    public bool EmptyRecycleBin()
    {
        try
        {
            SHEmptyRecycleBin();
            return true;
        }
        catch { return false; }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern uint SHEmptyRecycleBin(IntPtr hwnd = default, string? pszRootPath = null, uint dwFlags = 7);

    private IEnumerable<string> GetUserTempPaths() =>
    [
        Path.GetTempPath(),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
    ];

    private IEnumerable<string> GetWinTempPaths() =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
    ];

    private IEnumerable<string> GetWuCachePaths() =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download"),
    ];

    private IEnumerable<string> GetErrorReportPaths()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return
        [
            Path.Combine(local, "Microsoft", "Windows", "WER", "ReportArchive"),
            Path.Combine(local, "Microsoft", "Windows", "WER", "ReportQueue"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER", "ReportArchive"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER", "ReportQueue"),
        ];
    }

    private IEnumerable<string> GetThumbnailPaths()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return
        [
            Path.Combine(local, "Microsoft", "Windows", "Explorer"),
        ];
    }

    private IEnumerable<string> GetNvidiaCachePaths()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return
        [
            Path.Combine(local, "NVIDIA", "DXCache"),
            Path.Combine(local, "NVIDIA", "GLCache"),
            Path.Combine(local, "NVIDIA", "OptixCache"),
            Path.Combine(local, "D3DSCache"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NVIDIA", "ComputeCache"),
        ];
    }

    private IEnumerable<string> GetLogPaths()
    {
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return
        [
            Path.Combine(win, "Logs"),
            Path.Combine(programData, "Microsoft", "Windows", "WER"),
        ];
    }
}
