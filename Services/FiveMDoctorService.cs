using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace EkipppOptimizer.Services;

public record FiveMHopResult(int Hop, string Address, long AvgMs, int LossPct)
{
    public string AddressLabel => Address == "?" ? "— (pas de réponse)" : Address;
    public string LatencyLabel => AvgMs >= 0 ? $"{AvgMs} ms" : "—";
    public string LossLabel    => $"{LossPct}%";
}

public record FiveMDiagnosisResult(bool Success, string ResolvedTarget, List<FiveMHopResult> Hops, string Verdict);

public class FiveMDoctorService
{
    private const int MaxHops    = 24;
    private const int PingsPerHop = 3;
    private const int TimeoutMs   = 1200;

    // Résout un code cfx.re/join/XXXXX (ou juste XXXXX) vers son adresse IP:port réelle via l'API
    // publique FiveM déjà utilisée par les navigateurs de serveurs communautaires — si la résolution
    // échoue (format inattendu, API indisponible), on retombe proprement sur "entrée non résolue"
    // plutôt que de planter : l'utilisateur peut toujours coller directement une IP:port.
    public async Task<FiveMDiagnosisResult> DiagnoseAsync(string input, IProgress<string>? progress = null)
    {
        var hops = new List<FiveMHopResult>();
        string target = input.Trim();

        var code = ExtractJoinCode(target);
        if (code != null)
        {
            progress?.Report($"Résolution du code {code}…");
            var resolved = await ResolveJoinCodeAsync(code);
            if (resolved == null)
                return new FiveMDiagnosisResult(false, target,
                    hops, $"Impossible de résoudre le code « {code} » (serveur hors ligne, code invalide, ou API FiveM temporairement indisponible). Réessaie avec l'IP:port directe du serveur si tu la connais.");
            target = resolved;
        }

        var host = target.Split(':')[0];
        if (!TryResolveHost(host, out var ip))
            return new FiveMDiagnosisResult(false, target, hops, $"Impossible de résoudre l'adresse « {host} ».");

        progress?.Report($"Traçage de la route vers {target}…");
        bool reachedTarget = false;
        for (int ttl = 1; ttl <= MaxHops && !reachedTarget; ttl++)
        {
            progress?.Report($"Saut {ttl}/{MaxHops}…");
            var (addr, avgMs, lossPct, isTarget) = await ProbeHopAsync(ip, ttl);
            hops.Add(new FiveMHopResult(ttl, addr, avgMs, lossPct));
            reachedTarget = isTarget;
        }

        var verdict = BuildVerdict(hops, reachedTarget);
        return new FiveMDiagnosisResult(true, target, hops, verdict);
    }

    private static string? ExtractJoinCode(string input)
    {
        var s = input.Trim();
        if (s.Contains(':') && !s.Contains("cfx.re") && !s.Contains("fivem://")) return null; // déjà une IP:port
        var idx = s.LastIndexOf('/');
        var code = idx >= 0 ? s[(idx + 1)..] : s;
        code = code.Trim();
        return code.Length is >= 3 and <= 10 && !code.Contains('.') ? code : null;
    }

    private static async Task<string?> ResolveJoinCodeAsync(string code)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("EKIPPP-Optimizer/1.0");
            var json = await http.GetStringAsync($"https://servers-frontend.fivem.net/api/servers/single/{code}");
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Data", out var data)) return null;
            if (data.TryGetProperty("connectEndPoints", out var endpoints) && endpoints.GetArrayLength() > 0)
                return endpoints[0].GetString();
            if (data.TryGetProperty("ip", out var ipProp) && data.TryGetProperty("port", out var portProp))
                return $"{ipProp.GetString()}:{portProp.GetInt32()}";
            return null;
        }
        catch { return null; }
    }

    private static bool TryResolveHost(string host, out IPAddress ip)
    {
        ip = IPAddress.None;
        try
        {
            if (IPAddress.TryParse(host, out var parsed)) { ip = parsed; return true; }
            var entry = System.Net.Dns.GetHostEntry(host);
            if (entry.AddressList.Length == 0) return false;
            ip = entry.AddressList[0];
            return true;
        }
        catch { return false; }
    }

    private static async Task<(string address, long avgMs, int lossPct, bool isTarget)> ProbeHopAsync(IPAddress target, int ttl)
    {
        string? address = null;
        var times = new List<long>();
        int replies = 0;
        bool isTarget = false;

        using var ping = new Ping();
        var options = new PingOptions(ttl, true);
        var buffer  = new byte[32];

        for (int i = 0; i < PingsPerHop; i++)
        {
            try
            {
                var reply = await ping.SendPingAsync(target, TimeoutMs, buffer, options);
                if (reply.Status is IPStatus.TtlExpired or IPStatus.Success)
                {
                    address ??= reply.Address?.ToString() ?? "?";
                    times.Add(reply.RoundtripTime);
                    replies++;
                    if (reply.Status == IPStatus.Success) isTarget = true;
                }
            }
            catch { }
        }

        long avg = times.Count > 0 ? (long)times.Average() : -1;
        int lossPct = (int)Math.Round((PingsPerHop - replies) / (double)PingsPerHop * 100);
        return (address ?? "?", avg, lossPct, isTarget);
    }

    // Verdict simple et honnête : où la perte de paquets / latence apparaît en premier dans la
    // route détermine le responsable probable — sans jamais prétendre à une certitude absolue.
    private static string BuildVerdict(List<FiveMHopResult> hops, bool reachedTarget)
    {
        if (hops.Count == 0) return "Aucun saut mesurable — vérifie ta connexion réseau.";

        var firstBadHop = hops.Take(Math.Max(1, hops.Count - 2))
            .FirstOrDefault(h => h.LossPct >= 50 || h.AvgMs > 150);

        if (firstBadHop != null && firstBadHop.Hop <= 3)
            return $"Le problème semble venir de ta connexion locale ou de ton FAI (perte/latence dès le saut {firstBadHop.Hop}). Vérifie ta box, ton câble/Wi-Fi, ou lance le test de latence sous charge dans cet onglet.";

        if (firstBadHop != null)
            return $"Le problème apparaît en route, au saut {firstBadHop.Hop} ({firstBadHop.AddressLabel}) — probablement un nœud réseau intermédiaire ou l'hébergeur du serveur, pas ta connexion.";

        var lastHops = hops.TakeLast(2).ToList();
        if (lastHops.Any(h => h.LossPct >= 30))
            return "La perte de paquets apparaît uniquement sur les derniers sauts, proches du serveur — le souci vient probablement de l'hébergeur du serveur FiveM, pas de ta connexion.";

        if (!reachedTarget)
            return "Impossible de confirmer l'arrivée jusqu'au serveur (fréquent si son pare-feu bloque le ping) — mais aucune perte de paquets significative détectée sur la route. Ta connexion semble propre.";

        return "Aucune perte de paquets ni latence anormale détectée sur toute la route — ta connexion est propre. Si le lag persiste en jeu, il vient probablement du serveur lui-même (scripts/ressources), pas de ton PC ni de ton FAI.";
    }
}
