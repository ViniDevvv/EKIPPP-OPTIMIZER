using System.IO;
using System.ServiceProcess;

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
        var files     = new List<string>();
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenDirs  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total    = 0;

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                var full = Path.GetFullPath(path);
                if (!seenFiles.Add(full)) continue;
                try { total += new FileInfo(full).Length; files.Add(full); } catch { }
                continue;
            }
            if (!Directory.Exists(path)) continue;

            // Deux chemins déclarés peuvent pointer vers le même dossier réel (ex: Path.GetTempPath()
            // == LocalApplicationData\Temp sur une install Windows standard) — sans cette garde, le
            // même dossier est scanné/supprimé deux fois, et le 2e passage compte à tort chaque fichier
            // déjà supprimé par le 1er comme "verrouillé".
            var fullDir = Path.GetFullPath(path).TrimEnd('\\');
            if (!seenDirs.Add(fullDir)) continue;

            try
            {
                var opts = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible    = true,
                    AttributesToSkip      = FileAttributes.ReparsePoint,
                };
                foreach (var fi in new DirectoryInfo(fullDir).EnumerateFiles("*", opts))
                {
                    if (!seenFiles.Add(fi.FullName)) continue;
                    try { total += fi.Length; files.Add(fi.FullName); } catch { }
                }
            }
            catch { }
        }

        return new CleanCategory(name, desc, files.AsReadOnly(), total);
    }

    public (int deleted, long freed, int failed) Clean(IEnumerable<CleanCategory> categories, IProgress<string>? progress = null)
    {
        int count = 0; long freed = 0; int failed = 0;
        var categoryList = categories as IReadOnlyList<CleanCategory> ?? categories.ToList();

        // Le cache Windows Update (SoftwareDistribution\Download) reste verrouillé en permanence
        // par les services wuauserv/bits qui gardent des handles ouverts dessus — une suppression
        // classique échoue systématiquement sur TOUS ses fichiers tant qu'ils tournent (0 o libéré,
        // "fichiers verrouillés" pour chacun). On les arrête le temps du nettoyage puis on les relance,
        // exactement comme le fait déjà la réparation Windows Update de l'onglet dédié.
        bool needsWuStop = categoryList.Any(c => c.Name == "Cache Windows Update" && c.Paths.Count > 0);
        var stoppedServices = needsWuStop ? StopWuServices(progress) : [];

        try
        {
            foreach (var cat in categoryList)
            {
                progress?.Report($"Nettoyage: {cat.Name}…");
                foreach (var file in cat.Paths)
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (!info.Exists) continue;
                        if (info.IsReadOnly) info.IsReadOnly = false;
                        long size = info.Length;
                        info.Delete();
                        freed += size;
                        count++;
                    }
                    catch { failed++; }
                }
            }
        }
        finally
        {
            if (stoppedServices.Count > 0) RestartServices(stoppedServices, progress);
        }

        return (count, freed, failed);
    }

    private static List<string> StopWuServices(IProgress<string>? progress)
    {
        var stopped = new List<string>();
        foreach (var name in new[] { "wuauserv", "bits" })
        {
            try
            {
                using var svc = new ServiceController(name);
                if (svc.Status == ServiceControllerStatus.Running)
                {
                    progress?.Report($"Arrêt temporaire du service {name} pour libérer le cache Windows Update…");
                    svc.Stop();
                    svc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(6));
                    stopped.Add(name);
                }
            }
            catch { }
        }
        return stopped;
    }

    private static void RestartServices(List<string> names, IProgress<string>? progress)
    {
        foreach (var name in names)
        {
            try
            {
                progress?.Report($"Redémarrage du service {name}…");
                using var svc = new ServiceController(name);
                if (svc.Status == ServiceControllerStatus.Stopped) svc.Start();
            }
            catch { }
        }
    }

    private static readonly EnumerationOptions RecycleBinEnumOpts = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible    = true,
        AttributesToSkip      = FileAttributes.ReparsePoint,
    };

    // $Recycle.Bin contient un sous-dossier par SID (compte Windows) sur le disque, y compris
    // d'anciens comptes supprimés/orphelins. On ne s'occupe QUE du SID de l'utilisateur courant :
    // c'est le seul que "Vider la corbeille" peut réellement nettoyer — inclure les autres donnait
    // un chiffre qui ne pouvait jamais descendre à zéro, quoi que fasse l'utilisateur.
    private static IEnumerable<DirectoryInfo> GetUserRecycleBinFolders()
    {
        var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
        if (sid == null) yield break;

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
            var userBin = new DirectoryInfo(Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin", sid));
            if (userBin.Exists) yield return userBin;
        }
    }

    public long GetRecycleBinSize()
    {
        long size = 0;
        try
        {
            foreach (var userBin in GetUserRecycleBinFolders())
                foreach (var f in userBin.EnumerateFiles("*", RecycleBinEnumOpts))
                    try { size += f.Length; } catch { }
        }
        catch { }
        return size;
    }

    public (bool ok, uint hresult, int deleted, int failed, string? firstError) EmptyRecycleBinDetailed()
    {
        uint hr = 0xFFFFFFFF;
        try { hr = SHEmptyRecycleBin(); } catch { }
        if (hr == 0) return (true, hr, 0, 0, null);

        // SHEmptyRecycleBin a échoué (souvent un code générique 0x8000FFFF qui ne dit jamais
        // pourquoi). Repli : suppression directe des éléments de la corbeille de l'utilisateur,
        // fichier par fichier, ce qui contourne le blocage shell et donne la vraie raison de
        // chaque échec au lieu d'un code opaque.
        var (deleted, failed, firstError) = ForceDeleteRecycleBinContents();
        return (failed == 0 && deleted > 0, hr, deleted, failed, firstError);
    }

    private static (int deleted, int failed, string? firstError) ForceDeleteRecycleBinContents()
    {
        int deleted = 0, failed = 0;
        string? firstError = null;

        foreach (var userBin in GetUserRecycleBinFolders())
        {
            foreach (var f in userBin.EnumerateFiles("*", RecycleBinEnumOpts).ToList())
            {
                try
                {
                    if (f.IsReadOnly) f.IsReadOnly = false;
                    f.Delete();
                    deleted++;
                }
                catch (Exception ex)
                {
                    failed++;
                    firstError ??= $"{f.Name} ({ex.GetType().Name}: {ex.Message.Trim()})";
                }
            }

            // Dossiers vides restants, du plus profond au moins profond (ordre de suppression sûr).
            foreach (var d in userBin.EnumerateDirectories("*", RecycleBinEnumOpts)
                         .OrderByDescending(d => d.FullName.Length).ToList())
            {
                try { d.Delete(false); } catch { }
            }
        }

        return (deleted, failed, firstError);
    }

    public bool EmptyRecycleBin() => EmptyRecycleBinDetailed().ok;

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
