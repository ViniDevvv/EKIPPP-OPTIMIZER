using System.IO;
using System.Text.Json;

namespace EkipppOptimizer.Services;

public record HistoryEntry(DateTime Time, string Category, string Action, string Result)
{
    public string TimeLabel => Time.ToString("dd/MM HH:mm");
    public string Icon => Category switch
    {
        "Nettoyage"    => "◈",
        "Stockage"     => "▤",
        "Réseau"       => "◎",
        "Gaming"       => "◆",
        "Optimisation" => "◐",
        "Sécurité"     => "⊕",
        "Système"      => "✱",
        _              => "◉",
    };
}

public class ActionHistoryService
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EKIPPP-OPTIMIZER", "history.json");

    private readonly List<HistoryEntry> _entries = [];
    private static readonly JsonSerializerOptions JOpt = new() { WriteIndented = false };

    public ActionHistoryService() => Load();

    public void Add(string category, string action, string result)
    {
        _entries.Insert(0, new HistoryEntry(DateTime.Now, category, action, result));
        if (_entries.Count > 200) _entries.RemoveRange(200, _entries.Count - 200);
        Save();
    }

    public List<HistoryEntry> GetRecent(int n = 50) => _entries.Take(n).ToList();

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.WriteAllText(LogPath, JsonSerializer.Serialize(_entries, JOpt));
        }
        catch { }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(LogPath)) return;
            var loaded = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(LogPath));
            if (loaded != null) _entries.AddRange(loaded.Take(200));
        }
        catch { }
    }
}
