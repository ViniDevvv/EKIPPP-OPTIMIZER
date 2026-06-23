using System.IO;

namespace EkipppOptimizer.Services;

public record GameCache(string Game, string Icon, List<string> Paths)
{
    public long   SizeBytes { get; init; } = 0;
    public string SizeLabel => SizeBytes >= 1L << 30
        ? $"{SizeBytes / (1024.0 * 1024 * 1024):F1} Go"
        : SizeBytes >= 1L << 20 ? $"{SizeBytes / (1024.0 * 1024):F0} Mo"
        : SizeBytes > 0 ? $"{SizeBytes / 1024.0:F0} Ko" : "—";
    public bool IsEmpty => SizeBytes == 0;
}

public class GameCacheService
{
    // Env vars — fiables même en mode admin ou PC en domaine (contrairement à GetFolderPath)
    private readonly string _local   = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";
    private readonly string _appdata = Environment.GetEnvironmentVariable("APPDATA")      ?? "";

    private static readonly EnumerationOptions SafeEnum = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible    = true,
        AttributesToSkip      = FileAttributes.ReparsePoint,
    };

    public List<GameCache> ScanAll()
    {
        var result = new List<GameCache>();
        foreach (var def in BuildDefs())
        {
            // Timeout par jeu : 6s max pour éviter de bloquer sur un cache Steam de 300k fichiers
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            long sz = 0;
            foreach (var dir in def.Paths.Where(Directory.Exists))
                sz += DirSize(dir, cts.Token);
            result.Add(def with { SizeBytes = sz });
        }
        return result.OrderByDescending(c => c.SizeBytes).ToList();
    }

    public async Task<long> CleanAsync(GameCache cache, IProgress<string>? progress = null)
    {
        return await Task.Run(() =>
        {
            long freed = 0;
            foreach (var dir in cache.Paths.Where(Directory.Exists))
            {
                progress?.Report($"Nettoyage {Path.GetFileName(dir)}…");
                freed += DeleteContents(dir);
            }
            return freed;
        });
    }

    private List<GameCache> BuildDefs()
    {
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var pf   = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pd   = Environment.GetEnvironmentVariable("PROGRAMDATA") ?? "";

        var steamPaths = new List<string>
        {
            Path.Combine(pf86, "Steam", "appcache"),
            Path.Combine(pf,   "Steam", "appcache"),
            Path.Combine(_local, "Steam", "htmlcache"),
        };
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
                var sc1 = Path.Combine(drive.RootDirectory.FullName, "Program Files (x86)", "Steam", "steamapps", "shadercache");
                var sc2 = Path.Combine(drive.RootDirectory.FullName, "SteamLibrary", "steamapps", "shadercache");
                if (Directory.Exists(sc1)) steamPaths.Add(sc1);
                if (Directory.Exists(sc2)) steamPaths.Add(sc2);
            }
        }
        catch { }

        return
        [
            new("Steam", "🎮", steamPaths),

            new("Epic Games", "⚡",
            [
                Path.Combine(_local, "EpicGamesLauncher", "Saved", "webcache"),
                Path.Combine(_local, "EpicGamesLauncher", "Saved", "Logs"),
            ]),

            new("Discord", "💬",
            [
                Path.Combine(_appdata, "discord",       "Cache"),
                Path.Combine(_appdata, "discord",       "Code Cache"),
                Path.Combine(_appdata, "discord",       "GPUCache"),
                Path.Combine(_appdata, "discordptb",    "Cache"),
                Path.Combine(_appdata, "discordcanary", "Cache"),
            ]),

            new("Riot / Valorant", "🔫",
            [
                Path.Combine(_local, "Riot Games", "Riot Client", "Data", "Logs"),
                Path.Combine(_local, "Riot Games", "Riot Client", "Cache"),
                Path.Combine(_local, "VALORANT", "Saved", "Logs"),
            ]),

            new("FiveM / alt:V", "🚗",
            [
                Path.Combine(_local,   "FiveM", "FiveM.app", "data", "cache"),
                Path.Combine(_local,   "FiveM", "FiveM.app", "data", "priv"),
                Path.Combine(_local,   "FiveM", "FiveM.app", "data", "server-cache"),
                Path.Combine(_local,   "FiveM", "FiveM.app", "data", "server-cache-priv"),
                Path.Combine(_local,   "FiveM", "FiveM.app", "cache", "game"),
                Path.Combine(_local,   "FiveM", "FiveM.app", "logs"),
                Path.Combine(_local,   "FiveM", "FiveM.app", "crashes"),
                Path.Combine(_appdata, "CitizenFX", "logs"),
                Path.Combine(_appdata, "altv",       "cache"),
            ]),

            new("Minecraft", "⛏",
            [
                Path.Combine(_appdata, ".minecraft", "logs"),
                Path.Combine(_appdata, ".minecraft", "crash-reports"),
            ]),

            new("Battle.net", "🎯",
            [
                Path.Combine(_appdata, "Battle.net", "Logs"),
                Path.Combine(pd,       "Battle.net", "Agent", "Logs"),
            ]),

            new("EA / Origin", "🏆",
            [
                Path.Combine(_local, "Electronic Arts", "EA Desktop", "Cache"),
                Path.Combine(_local, "Origin", "cache"),
            ]),
        ];
    }

    private static long DirSize(string dir, CancellationToken ct = default)
    {
        long sz = 0;
        try
        {
            foreach (var fi in new DirectoryInfo(dir).EnumerateFiles("*", SafeEnum))
            {
                if (ct.IsCancellationRequested) break;
                try { sz += fi.Length; } catch { }
            }
        }
        catch { }
        return sz;
    }

    private static long DeleteContents(string dir)
    {
        long freed = 0;
        try
        {
            var opts = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible    = true,
                AttributesToSkip      = FileAttributes.ReparsePoint,
            };
            foreach (var fi in new DirectoryInfo(dir).EnumerateFiles("*", opts))
                try { freed += fi.Length; fi.Delete(); } catch { }
            foreach (var d in new DirectoryInfo(dir).EnumerateDirectories("*", opts).Reverse())
                try { d.Delete(true); } catch { }
        }
        catch { }
        return freed;
    }
}
