using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Diagnostics;

namespace EkipppOptimizer.Services;

public record DnsServer(string Name, string Primary, string Secondary, string Description);
public record DnsResult(DnsServer Server, long PingMs, bool Available)
{
    public string PingLabel => Available ? $"{PingMs} ms" : "Hors ligne";
    public string Grade     => PingMs < 10 ? "Excellent" : PingMs < 25 ? "Très bon" : PingMs < 50 ? "Bon" : "Lent";
}

public class DnsSpeedTestService
{
    private static readonly DnsServer[] Servers =
    [
        new("Cloudflare",  "1.1.1.1",        "1.0.0.1",        "Reconnu parmi les plus rapides — axé vie privée"),
        new("Google",      "8.8.8.8",         "8.8.4.4",        "Très fiable — bonne compatibilité"),
        new("Quad9",       "9.9.9.9",         "149.112.112.112","Bloque les domaines malveillants"),
        new("OpenDNS",     "208.67.222.222",  "208.67.220.220", "Filtrage familial disponible"),
        new("AdGuard",     "94.140.14.14",    "94.140.15.15",   "Bloque publicités et trackers"),
        new("NextDNS",     "45.90.28.0",      "45.90.30.0",     "DNS personnalisable — très moderne"),
        new("Comodo",      "8.26.56.26",      "8.20.247.20",    "Sécurité renforcée"),
        new("Neustar",     "64.6.64.6",       "64.6.65.6",      "Haute disponibilité"),
    ];

    public async Task<List<DnsResult>> TestAllAsync(IProgress<string>? progress = null)
    {
        var results = new List<DnsResult>();
        foreach (var srv in Servers)
        {
            progress?.Report($"Test {srv.Name} ({srv.Primary})…");
            var result = await PingDnsAsync(srv);
            results.Add(result);
        }
        return results.OrderBy(r => r.Available ? r.PingMs : long.MaxValue).ToList();
    }

    private static async Task<DnsResult> PingDnsAsync(DnsServer srv)
    {
        // Moyenne de 3 pings
        var times = new List<long>();
        for (int i = 0; i < 3; i++)
        {
            try
            {
                using var ping = new Ping();
                var sw    = Stopwatch.StartNew();
                var reply = await ping.SendPingAsync(srv.Primary, 1500);
                sw.Stop();
                if (reply.Status == IPStatus.Success)
                    times.Add(reply.RoundtripTime > 0 ? reply.RoundtripTime : sw.ElapsedMilliseconds);
            }
            catch { }
            await Task.Delay(50);
        }
        if (times.Count == 0)
            return new DnsResult(srv, 9999, false);
        return new DnsResult(srv, (long)times.Average(), true);
    }

    // DNS d'origine par adaptateur, capturé avant la toute première application depuis l'app —
    // permet un vrai retour à la config personnelle de l'utilisateur (pas juste "forcer le DHCP").
    private readonly Dictionary<string, List<string>> _previousDns = new(StringComparer.OrdinalIgnoreCase);
    public bool HasPreviousDns => _previousDns.Count > 0;

    public async Task<bool> ApplyDnsAsync(DnsResult result)
    {
        return await Task.Run(() =>
        {
            try
            {
                var adapters = GetActiveAdapters();
                if (adapters.Count == 0) return false;

                CaptureCurrentDnsIfNeeded(adapters);

                bool allOk = true;
                foreach (var a in adapters)
                {
                    allOk &= Run("netsh", $"interface ip set dns \"{a}\" static {result.Server.Primary} primary");
                    if (!string.IsNullOrEmpty(result.Server.Secondary))
                        allOk &= Run("netsh", $"interface ip add dns \"{a}\" {result.Server.Secondary} index=2");
                }
                return allOk;
            }
            catch { return false; }
        });
    }

    public async Task<bool> ResetDnsAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                bool allOk = true;
                foreach (var a in GetActiveAdapters())
                    allOk &= Run("netsh", $"interface ip set dns \"{a}\" dhcp");
                return allOk;
            }
            catch { return false; }
        });
    }

    // Réapplique le DNS statique/DHCP exact que l'adaptateur avait avant la première
    // modification faite par l'app dans cette session (plutôt que de forcer le DHCP à l'aveugle).
    public async Task<bool> RestorePreviousDnsAsync()
    {
        if (_previousDns.Count == 0) return false;
        return await Task.Run(() =>
        {
            try
            {
                bool allOk = true;
                foreach (var (adapter, dns) in _previousDns)
                {
                    if (dns.Count == 0)
                    {
                        allOk &= Run("netsh", $"interface ip set dns \"{adapter}\" dhcp");
                        continue;
                    }
                    allOk &= Run("netsh", $"interface ip set dns \"{adapter}\" static {dns[0]} primary");
                    if (dns.Count > 1)
                        allOk &= Run("netsh", $"interface ip add dns \"{adapter}\" {dns[1]} index=2");
                }
                return allOk;
            }
            catch { return false; }
        });
    }

    private void CaptureCurrentDnsIfNeeded(List<string> adapters)
    {
        foreach (var a in adapters)
        {
            if (_previousDns.ContainsKey(a)) continue;
            try
            {
                var ni = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(n => n.Name == a);
                var dns = ni?.GetIPProperties().DnsAddresses
                    .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                    .Select(ip => ip.ToString())
                    .ToList() ?? [];
                _previousDns[a] = dns;
            }
            catch { _previousDns[a] = []; }
        }
    }

    // Lit le DNS actuellement configuré sur l'adaptateur actif et retourne le nom du serveur connu
    public string GetCurrentDnsName()
    {
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up
                    || ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                var dnsAddrs = ni.GetIPProperties().DnsAddresses
                    .Where(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(ip => ip.ToString())
                    .ToList();
                if (dnsAddrs.Count == 0) continue;
                var match = Servers.FirstOrDefault(s => dnsAddrs.Contains(s.Primary) || dnsAddrs.Contains(s.Secondary));
                if (match != null) return match.Name;
            }
        }
        catch { }
        return "";
    }

    private static List<string> GetActiveAdapters()
    {
        var list = new List<string>();
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                    && ni.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                    list.Add(ni.Name);
            }
        }
        catch { }
        return list;
    }

    private static bool Run(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args)
            { UseShellExecute = false, CreateNoWindow = true });
        if (p == null) return false;
        p.WaitForExit(5000);
        return p.HasExited && p.ExitCode == 0;
    }
}
