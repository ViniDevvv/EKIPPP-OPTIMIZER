using System.IO;
using Microsoft.Win32;

namespace EkipppOptimizer.Services;

public record DetectedGame(string Name, string Source, string InstallPath);

public class GameDetectionService
{
    public List<DetectedGame> DetectGames()
    {
        var games = new List<DetectedGame>();
        DetectSteam(games);
        DetectEpic(games);
        DetectRiot(games);
        DetectFiveM(games);
        return games.OrderBy(g => g.Name).ToList();
    }

    private void DetectSteam(List<DetectedGame> list)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam")
                         ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            if (key?.GetValue("InstallPath") is not string steamPath) return;
            var apps = Path.Combine(steamPath, "steamapps");
            if (!Directory.Exists(apps)) return;
            foreach (var acf in Directory.GetFiles(apps, "appmanifest_*.acf"))
            {
                var content = File.ReadAllText(acf);
                var name = Extract(content, "name");
                var dir  = Extract(content, "installdir");
                if (!string.IsNullOrEmpty(name))
                    list.Add(new DetectedGame(name, "Steam", Path.Combine(apps, "common", dir ?? "")));
            }
        }
        catch { }
    }

    private void DetectEpic(List<DetectedGame> list)
    {
        try
        {
            var manifests = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (!Directory.Exists(manifests)) return;
            foreach (var f in Directory.GetFiles(manifests, "*.item"))
            {
                var content = File.ReadAllText(f);
                var name = ExtractJson(content, "DisplayName");
                var path = ExtractJson(content, "InstallLocation");
                if (!string.IsNullOrEmpty(name))
                    list.Add(new DetectedGame(name, "Epic", path ?? ""));
            }
        }
        catch { }
    }

    private void DetectRiot(List<DetectedGame> list)
    {
        try
        {
            var riotData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Riot Games");
            if (!Directory.Exists(riotData)) return;
            var clientExe = Path.Combine(riotData, "Riot Client", "RiotClientServices.exe");
            if (!File.Exists(clientExe)) return;
            var valorantPath = Path.Combine(riotData, "Riot Client", "apps", "valorant");
            if (Directory.Exists(valorantPath))
                list.Add(new DetectedGame("VALORANT", "Riot", valorantPath));
            var lolPath = Path.Combine(riotData, "Riot Client", "apps", "bacon");
            if (Directory.Exists(lolPath))
                list.Add(new DetectedGame("League of Legends", "Riot", lolPath));
        }
        catch { }
    }

    private void DetectFiveM(List<DetectedGame> list)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FiveM", "FiveM.app");
            if (Directory.Exists(path))
                list.Add(new DetectedGame("FiveM", "FiveM", path));
        }
        catch { }
    }

    private static string? Extract(string content, string key)
    {
        var pattern = $"\"{key}\"\t\t\"";
        var idx = content.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        idx += pattern.Length;
        var end = content.IndexOf('"', idx);
        return end > idx ? content[idx..end] : null;
    }

    private static string? ExtractJson(string content, string key)
    {
        var pattern = $"\"{key}\": \"";
        var idx = content.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        idx += pattern.Length;
        var end = content.IndexOf('"', idx);
        return end > idx ? content[idx..end] : null;
    }
}
