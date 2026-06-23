using Microsoft.Win32;

namespace EkipppOptimizer.Services;

public class StartupEntry
{
    public string Name    { get; set; } = "";
    public string Command { get; set; } = "";
    public string Source  { get; set; } = "";
    public bool   Enabled { get; set; } = true;

    // Nom propre sans suffixe hexadécimal (ex: "GoogleChromeAutoLaunch_BA0E09..." → "GoogleChromeAutoLaunch")
    public string DisplayName
    {
        get
        {
            var n = Name;
            var idx = n.LastIndexOf('_');
            if (idx > 2 && idx < n.Length - 1)
            {
                var suffix = n[(idx + 1)..];
                if (suffix.Length >= 8 && suffix.All(c => Uri.IsHexDigit(c)))
                    n = n[..idx];
            }
            return n;
        }
    }

    // Juste le nom de l'exe (ex: "C:\Program Files\Discord\Update.exe" → "Update.exe")
    public string ExeName
    {
        get
        {
            try
            {
                var cmd = Command.Trim();
                string path = cmd.StartsWith('"')
                    ? cmd[1..Math.Max(1, cmd.IndexOf('"', 1))]
                    : cmd.Split(' ', 2)[0];
                return System.IO.Path.GetFileName(path);
            }
            catch { return ""; }
        }
    }
}

public class StartupManagerService
{
    private const string DisabledRoot = @"SOFTWARE\EKIPPP-OPTIMIZER\DisabledStartup";

    public List<StartupEntry> GetAll()
    {
        var list = new List<StartupEntry>();
        ReadRegistry(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "Utilisateur", list);
        ReadRegistry(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "Système", list);
        ReadDisabled(list);
        return list.OrderBy(e => e.Name).ToList();
    }

    private static void ReadRegistry(RegistryKey root, string path, string src, List<StartupEntry> list)
    {
        try
        {
            using var k = root.OpenSubKey(path);
            if (k == null) return;
            foreach (var name in k.GetValueNames())
                list.Add(new StartupEntry { Name = name, Command = k.GetValue(name)?.ToString() ?? "", Source = src, Enabled = true });
        }
        catch { }
    }

    private static void ReadDisabled(List<StartupEntry> list)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(DisabledRoot);
            if (k == null) return;
            foreach (var name in k.GetValueNames())
                list.Add(new StartupEntry { Name = name, Command = k.GetValue(name)?.ToString() ?? "", Source = "Désactivé", Enabled = false });
        }
        catch { }
    }

    public void Disable(StartupEntry entry)
    {
        try
        {
            var root = entry.Source == "Système" ? Registry.LocalMachine : Registry.CurrentUser;
            using var k = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            k?.DeleteValue(entry.Name, throwOnMissingValue: false);
            using var d = Registry.CurrentUser.CreateSubKey(DisabledRoot);
            d.SetValue(entry.Name, entry.Command);
        }
        catch { }
    }

    public void Enable(StartupEntry entry)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
            k.SetValue(entry.Name, entry.Command);
            using var d = Registry.CurrentUser.OpenSubKey(DisabledRoot, writable: true);
            d?.DeleteValue(entry.Name, throwOnMissingValue: false);
        }
        catch { }
    }
}
