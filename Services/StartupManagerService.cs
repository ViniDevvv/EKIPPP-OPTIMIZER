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

    // Clé composite Name+Source (deux entrées de même nom dans HKCU et HKLM ne se collisionnent plus)
    // et valeur "Source|Command" pour restaurer dans la bonne ruche depuis Enable().
    private static string DisabledKey(StartupEntry entry) => $"{entry.Source}::{entry.Name}";

    private static void ReadDisabled(List<StartupEntry> list)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(DisabledRoot);
            if (k == null) return;
            foreach (var valueName in k.GetValueNames())
            {
                var raw = k.GetValue(valueName)?.ToString() ?? "";
                var sepIdx = raw.IndexOf('|');
                var origSource = sepIdx > 0 ? raw[..sepIdx] : "Utilisateur";
                var command    = sepIdx > 0 ? raw[(sepIdx + 1)..] : raw;
                var nameSepIdx = valueName.IndexOf("::", StringComparison.Ordinal);
                var name       = nameSepIdx > 0 ? valueName[(nameSepIdx + 2)..] : valueName;
                list.Add(new StartupEntry { Name = name, Command = command, Source = origSource, Enabled = false });
            }
        }
        catch { }
    }

    public bool Disable(StartupEntry entry)
    {
        try
        {
            var root = entry.Source == "Système" ? Registry.LocalMachine : Registry.CurrentUser;
            using var k = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            k?.DeleteValue(entry.Name, throwOnMissingValue: false);
            using var d = Registry.CurrentUser.CreateSubKey(DisabledRoot);
            d.SetValue(DisabledKey(entry), $"{entry.Source}|{entry.Command}");
            return true;
        }
        catch { return false; }
    }

    public bool Enable(StartupEntry entry)
    {
        try
        {
            using var d = Registry.CurrentUser.OpenSubKey(DisabledRoot, writable: true);
            var raw = d?.GetValue(DisabledKey(entry))?.ToString() ?? "";
            var sepIdx = raw.IndexOf('|');
            var origSource = sepIdx > 0 ? raw[..sepIdx] : entry.Source;
            var command    = sepIdx > 0 ? raw[(sepIdx + 1)..] : entry.Command;

            var root = origSource == "Système" ? Registry.LocalMachine : Registry.CurrentUser;
            using var k = root.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
            k.SetValue(entry.Name, command);
            d?.DeleteValue(DisabledKey(entry), throwOnMissingValue: false);
            return true;
        }
        catch { return false; }
    }
}
