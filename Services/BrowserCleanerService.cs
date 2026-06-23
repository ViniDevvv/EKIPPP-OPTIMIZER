using System.IO;

namespace EkipppOptimizer.Services;

public record BrowserProfile(
    string Browser, string Icon, string ProfilePath,
    long CacheBytes, long CookiesBytes, long HistoryBytes)
{
    public long   TotalBytes   => CacheBytes + CookiesBytes + HistoryBytes;
    public string CacheLabel   => FormatSize(CacheBytes);
    public string CookiesLabel => FormatSize(CookiesBytes);
    public string HistoryLabel => FormatSize(HistoryBytes);
    public string TotalLabel   => FormatSize(TotalBytes);

    private static string FormatSize(long b) =>
        b >= 1L << 30 ? $"{b / (1024.0 * 1024 * 1024):F1} Go"
      : b >= 1L << 20 ? $"{b / (1024.0 * 1024):F0} Mo"
      : b > 0         ? $"{b / 1024.0:F0} Ko"
      : "—";
}

public class BrowserCleanerService
{
    // ── Scan instantané (< 10 ms) — aucune mesure de taille ──────────────────
    // Utilise les variables d'environnement Windows qui sont toujours définies
    // correctement, même en mode administrateur ou sur des PCs en domaine.
    public List<BrowserProfile> ScanAll()
    {
        var local   = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";
        var appdata = Environment.GetEnvironmentVariable("APPDATA")      ?? "";

        var result = new List<BrowserProfile>();

        void Add(string name, string icon, string path)
        {
            try { if (Directory.Exists(path)) result.Add(new BrowserProfile(name, icon, path, 0, 0, 0)); }
            catch { }
        }

        Add("Chrome",   "🌐", Path.Combine(local,   "Google",        "Chrome",        "User Data", "Default"));
        Add("Edge",     "🔵", Path.Combine(local,   "Microsoft",     "Edge",          "User Data", "Default"));
        Add("Brave",    "🦁", Path.Combine(local,   "BraveSoftware", "Brave-Browser", "User Data", "Default"));
        Add("Vivaldi",  "🎵", Path.Combine(local,   "Vivaldi",       "User Data",     "Default"));
        Add("Opera",    "🎭", Path.Combine(appdata, "Opera Software","Opera Stable"));
        Add("Opera GX", "🎮", Path.Combine(appdata, "Opera Software","Opera GX Stable"));

        // Firefox : trouve le premier profil valide
        try
        {
            var ffDir = Path.Combine(appdata, "Mozilla", "Firefox", "Profiles");
            if (Directory.Exists(ffDir))
            {
                var profile = Directory.EnumerateDirectories(ffDir)
                    .FirstOrDefault(d => d.EndsWith(".default-release", StringComparison.OrdinalIgnoreCase))
                    ?? Directory.EnumerateDirectories(ffDir).FirstOrDefault();
                if (profile != null)
                    result.Add(new BrowserProfile("Firefox", "🦊", profile, 0, 0, 0));
            }
        }
        catch { }

        return result;
    }

    // ── Calcul des tailles (appelé en arrière-plan, par navigateur) ───────────
    public (long cache, long cookies, long history) MeasureSizes(BrowserProfile p)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        try
        {
            if (p.Browser == "Firefox")
            {
                return (
                    DirSize(Path.Combine(p.ProfilePath, "cache2"), cts.Token),
                    FileSize(Path.Combine(p.ProfilePath, "cookies.sqlite")),
                    FileSize(Path.Combine(p.ProfilePath, "places.sqlite"))
                );
            }
            long cache = DirSize(Path.Combine(p.ProfilePath, "Cache"),                    cts.Token)
                       + DirSize(Path.Combine(p.ProfilePath, "Code Cache"),               cts.Token)
                       + DirSize(Path.Combine(p.ProfilePath, "GPUCache"),                 cts.Token)
                       + DirSize(Path.Combine(p.ProfilePath, "Network", "Cache"),         cts.Token);
            long cookies = FileSize(Path.Combine(p.ProfilePath, "Cookies"));
            long history = FileSize(Path.Combine(p.ProfilePath, "History"));
            return (cache, cookies, history);
        }
        catch { return (0, 0, 0); }
    }

    // ── Nettoyage ─────────────────────────────────────────────────────────────

    public async Task<long> CleanCacheAsync(BrowserProfile p, IProgress<string>? prog = null) =>
        await Task.Run(() =>
        {
            prog?.Report($"Cache {p.Browser}…");
            return p.Browser == "Firefox"
                ? DeleteContents(Path.Combine(p.ProfilePath, "cache2"))
                : DeleteContents(Path.Combine(p.ProfilePath, "Cache"))
                + DeleteContents(Path.Combine(p.ProfilePath, "Code Cache"))
                + DeleteContents(Path.Combine(p.ProfilePath, "GPUCache"))
                + DeleteContents(Path.Combine(p.ProfilePath, "Network", "Cache"));
        });

    public async Task<long> CleanAllAsync(BrowserProfile p, IProgress<string>? prog = null) =>
        await Task.Run(() =>
        {
            prog?.Report($"Nettoyage {p.Browser}…");
            long freed = 0;
            if (p.Browser == "Firefox")
            {
                freed += DeleteContents(Path.Combine(p.ProfilePath, "cache2"));
                freed += TryDeleteFile(Path.Combine(p.ProfilePath, "cookies.sqlite"));
                freed += TryDeleteFile(Path.Combine(p.ProfilePath, "places.sqlite"));
            }
            else
            {
                freed += DeleteContents(Path.Combine(p.ProfilePath, "Cache"));
                freed += DeleteContents(Path.Combine(p.ProfilePath, "Code Cache"));
                freed += DeleteContents(Path.Combine(p.ProfilePath, "GPUCache"));
                freed += DeleteContents(Path.Combine(p.ProfilePath, "Network", "Cache"));
                freed += TryDeleteFile(Path.Combine(p.ProfilePath, "Cookies"));
                freed += TryDeleteFile(Path.Combine(p.ProfilePath, "History"));
            }
            return freed;
        });

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static long DirSize(string dir, CancellationToken ct = default)
    {
        if (!Directory.Exists(dir)) return 0;
        long sz = 0;
        try
        {
            var opts = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible    = true,
                AttributesToSkip      = FileAttributes.ReparsePoint,
            };
            foreach (var fi in new DirectoryInfo(dir).EnumerateFiles("*", opts))
            {
                if (ct.IsCancellationRequested) break;
                sz += fi.Length;
            }
        }
        catch { }
        return sz;
    }

    private static long FileSize(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }

    private static long DeleteContents(string dir)
    {
        long freed = 0;
        if (!Directory.Exists(dir)) return 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                try { freed += new FileInfo(f).Length; File.Delete(f); } catch { }
            foreach (var d in Directory.GetDirectories(dir).Reverse())
                try { Directory.Delete(d, true); } catch { }
        }
        catch { }
        return freed;
    }

    private static long TryDeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return 0;
            long sz = new FileInfo(path).Length;
            File.Delete(path);
            return sz;
        }
        catch { return 0; }
    }
}
