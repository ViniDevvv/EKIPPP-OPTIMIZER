using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;

namespace EkipppOptimizer.Services;

public record SpeedTestResult(
    double DownloadMbps,
    double UploadMbps,
    long   PingMs,
    long   JitterMs,
    bool   Success)
{
    public string DownloadLabel => DownloadMbps > 0 ? $"{DownloadMbps:F0} Mbps" : "—";
    public string UploadLabel   => UploadMbps   > 0 ? $"{UploadMbps:F0} Mbps"   : "—";
    public string PingLabel     => PingMs       > 0 ? $"{PingMs} ms"             : "—";
    public string JitterLabel   => JitterMs     > 0 ? $"±{JitterMs} ms"          : "—";

    public string ConnectionType => DownloadMbps switch
    {
        >= 900 => "Fibre Gigabit",
        >= 500 => "Fibre Ultra",
        >= 100 => "Fibre",
        >= 30  => "VDSL / Câble",
        >= 8   => "ADSL",
        > 0    => "Connexion lente",
        _      => "Indisponible",
    };

    public string Grade => DownloadMbps switch
    {
        >= 500 => "Exceptionnel",
        >= 100 => "Excellent",
        >= 30  => "Très bon",
        >= 10  => "Correct",
        > 0    => "Lent",
        _      => "Non disponible",
    };

    public string GradeAdvice => DownloadMbps switch
    {
        >= 500 => "Parfait pour le gaming compétitif, streaming 4K/8K et téléchargements ultra-rapides.",
        >= 100 => "Excellent pour le gaming, le streaming 4K et les visioconférences HD.",
        >= 30  => "Bon pour le gaming et le streaming 1080p. Légères latences possibles en pic.",
        >= 10  => "Suffisant pour le streaming 720p et le gaming casual.",
        > 0    => "Connexion lente — streaming limité, gaming avec latence élevée.",
        _      => "Aucune connexion détectée. Vérifiez votre réseau.",
    };
}

public record BufferbloatResult(
    long IdlePingMs, long LoadedPingMs, long IncreaseMs, string Grade, string GradeLabel,
    bool IdleMeasured = true, bool LoadedMeasured = true)
{
    public string IdleLabel     => IdleMeasured                  ? $"{IdlePingMs} ms"   : "—";
    public string LoadedLabel   => LoadedMeasured                ? $"{LoadedPingMs} ms" : "—";
    public string IncreaseLabel => IdleMeasured && LoadedMeasured ? $"+{IncreaseMs} ms"  : "—";
}

public class SpeedTestService
{
    private static readonly string[] DownloadUrls =
    [
        "https://speed.cloudflare.com/__down?bytes=25000000",
        "https://proof.ovh.net/files/10Mb.dat",
        "https://ash-speed.hetzner.com/10MB.bin",
    ];

    private const string UploadUrl = "https://speed.cloudflare.com/__up";

    public async Task<SpeedTestResult> TestAsync(IProgress<(string Phase, double LiveMbps)> progress)
    {
        // 1. Latence — on essaie, mais une éventuelle absence de ping n'arrête plus le test.
        //    Certains firewalls bloquent ICMP + HTTP mais laissent passer les downloads CDN.
        progress.Report(("Mesure de la latence…", 0));
        var (pingMs, jitter) = await MeasureLatencyAsync();

        // 2. Download — seul critère réel d'échec
        progress.Report(("Téléchargement…", 0));
        double download = await MeasureDownloadAsync(progress);

        if (download == 0)
            return new SpeedTestResult(0, 0, 0, 0, false);

        // 3. Upload
        progress.Report(("Upload…", download));
        double upload = await MeasureUploadAsync(progress);

        return new SpeedTestResult(download, upload, pingMs, jitter, true);
    }

    // Un ping à vide ne dit rien de la connexion en pleine partie : beaucoup de box/routeurs
    // laissent la latence grimper fortement dès qu'un téléchargement sature la bande passante
    // (bufferbloat). On mesure donc la latence au repos, PUIS pendant une saturation volontaire
    // de la connexion, pour donner une note qui reflète l'usage réel en jeu.
    public async Task<BufferbloatResult> MeasureBufferbloatAsync(IProgress<string>? progress = null)
    {
        const string host = "1.1.1.1";

        progress?.Report("Mesure de la latence au repos…");
        var idleSamples   = await PingSamplesAsync(host, 6, 150);
        bool idleMeasured = idleSamples.Count > 0;
        long idle         = idleMeasured ? (long)idleSamples.Average() : 0;
        if (!idleMeasured)
        {
            var (httpPing, _) = await MeasureHttpLatencyAsync();
            if (httpPing > 0) { idle = httpPing; idleMeasured = true; }
        }

        progress?.Report("Saturation de la connexion — mesure de la latence en charge…");
        // Fenêtre totale généreuse : la boucle de pings "en charge" (8 échantillons, jusqu'à 1.5s
        // chacun en cas de perte de paquets) doit rester intégralement couverte par le téléchargement
        // de saturation, sinon les derniers échantillons mesurent une latence qui n'est plus "sous charge".
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(16));
        var downloadTask = SaturateDownloadAsync(TimeSpan.FromSeconds(14), cts.Token);
        await Task.Delay(400, CancellationToken.None); // laisse le débit monter avant de mesurer

        var loadedSamples   = await PingSamplesAsync(host, 8, 150, cts.Token);
        bool loadedMeasured = loadedSamples.Count > 0;
        long loaded         = loadedMeasured ? (long)loadedSamples.Average() : 0;

        try { await downloadTask; } catch { }

        if (!idleMeasured && !loadedMeasured)
            return new BufferbloatResult(0, 0, 0, "?",
                "Latence non mesurable — ICMP et HTTP semblent bloqués sur ce réseau.", false, false);

        // Perte de paquets totale pendant la saturation : c'est le pire scénario possible, pas
        // une absence d'impact — surtout ne pas le confondre avec une augmentation de 0 ms/note A+.
        if (!loadedMeasured)
            return new BufferbloatResult(idle, 0, 0, "F",
                "Perte de paquets totale sous charge — signe d'un bufferbloat sévère ou d'une connexion instable en pleine saturation.",
                idleMeasured, false);

        if (!idleMeasured)
            return new BufferbloatResult(0, loaded, 0, "?",
                "Latence au repos non mesurable — impossible de calculer l'augmentation sous charge.", false, true);

        var increase = Math.Max(0, loaded - idle);
        var (grade, label) = GradeBufferbloat(increase);
        return new BufferbloatResult(idle, loaded, increase, grade, label, true, true);
    }

    private static async Task<List<long>> PingSamplesAsync(string host, int count, int delayMs, CancellationToken ct = default)
    {
        var samples = new List<long>();
        try
        {
            using var ping = new Ping();
            for (int i = 0; i < count; i++)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    var reply = await ping.SendPingAsync(host, 1500);
                    if (reply.Status == IPStatus.Success) samples.Add(reply.RoundtripTime);
                }
                catch { }
                if (i < count - 1)
                {
                    try { await Task.Delay(delayMs, ct); } catch (OperationCanceledException) { break; }
                }
            }
        }
        catch { }
        return samples;
    }

    // Télécharge en boucle pendant la durée donnée pour saturer la connexion — peu importe le
    // débit final, seul compte le fait de maintenir la ligne occupée pendant la mesure de ping.
    private static async Task SaturateDownloadAsync(TimeSpan duration, CancellationToken token)
    {
        var endAt = DateTime.UtcNow + duration;
        try
        {
            using var http = CreateHttpClient();
            while (DateTime.UtcNow < endAt && !token.IsCancellationRequested)
            {
                try
                {
                    using var response = await http.GetAsync(DownloadUrls[0], HttpCompletionOption.ResponseHeadersRead, token);
                    using var stream = await response.Content.ReadAsStreamAsync(token);
                    var buffer = new byte[65536];
                    while (DateTime.UtcNow < endAt && await stream.ReadAsync(buffer, token) > 0) { }
                }
                catch { break; }
            }
        }
        catch { }
    }

    private static (string grade, string label) GradeBufferbloat(long increaseMs) => increaseMs switch
    {
        < 5   => ("A+", "Excellent — aucun impact même en pleine charge, idéal pour le jeu en ligne."),
        < 30  => ("A",  "Très bon — impact quasi imperceptible en jeu."),
        < 60  => ("B",  "Bon — léger à-coup possible si un gros téléchargement tourne en fond."),
        < 200 => ("C",  "Moyen — pics de latence perceptibles en jeu si autre chose sature la connexion."),
        < 400 => ("D",  "Faible — la box/le routeur souffre sous charge, le ping peut fortement grimper en jeu."),
        _     => ("F",  "Très mauvais (bufferbloat sévère) — active le QoS / Smart Queue Management sur ta box si disponible."),
    };

    // HttpClient sans proxy explicite : .NET utilise le proxy système Windows automatiquement (WinINet)
    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("EKIPPP-Optimizer/1.0");
        return http;
    }

    private static async Task<double> MeasureDownloadAsync(IProgress<(string, double)> progress)
    {
        foreach (var url in DownloadUrls)
        {
            long totalBytes = 0;
            var  sw         = Stopwatch.StartNew();

            try
            {
                using var cts  = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var http = CreateHttpClient();

                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                response.EnsureSuccessStatusCode();
                using var stream = await response.Content.ReadAsStreamAsync(cts.Token);

                var buffer = new byte[65536];
                int read;

                try
                {
                    while ((read = await stream.ReadAsync(buffer, cts.Token)) > 0)
                    {
                        totalBytes += read;
                        double secs = sw.Elapsed.TotalSeconds;
                        if (secs >= 0.5)
                        {
                            double mbps = totalBytes * 8.0 / (1024 * 1024) / secs;
                            progress.Report(($"↓ {mbps:F0} Mbps", mbps));
                        }
                    }
                }
                catch (OperationCanceledException) { }
            }
            catch { }

            // Pas de seuil de durée minimale : sur une connexion très rapide (fibre),
            // le fichier peut être téléchargé entièrement en bien moins de 2s.
            double elapsed = sw.Elapsed.TotalSeconds;
            if (totalBytes > 0 && elapsed > 0.1)
                return totalBytes * 8.0 / (1024 * 1024) / elapsed;
        }

        return 0;
    }

    private static async Task<double> MeasureUploadAsync(IProgress<(string, double)> progress)
    {
        try
        {
            const int dataSize = 5 * 1024 * 1024;
            var data = new byte[dataSize];
            Random.Shared.NextBytes(data);

            progress.Report(("↑ Upload…", 0));
            var sw = Stopwatch.StartNew();

            using var cts  = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var http = CreateHttpClient();

            using var content = new ByteArrayContent(data);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            await http.PostAsync(UploadUrl, content, cts.Token);

            double elapsed = sw.Elapsed.TotalSeconds;
            double mbps    = elapsed > 0.05 ? dataSize * 8.0 / (1024 * 1024) / elapsed : 0;
            if (mbps > 0) progress.Report(($"↑ {mbps:F0} Mbps", mbps));
            return mbps;
        }
        catch { return 0; }
    }

    private static async Task<(long ping, long jitter)> MeasureLatencyAsync()
    {
        var pings = new List<long>();
        try
        {
            using var ping = new Ping();
            foreach (var host in new[] { "1.1.1.1", "8.8.8.8" })
            {
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        var reply = await ping.SendPingAsync(host, 2000);
                        if (reply.Status == IPStatus.Success)
                            pings.Add(reply.RoundtripTime);
                    }
                    catch { }
                    if (pings.Count < 5 && i < 2) await Task.Delay(80);
                }
                if (pings.Count >= 4) break;
            }
        }
        catch { }

        if (pings.Count > 0)
        {
            long avg    = (long)pings.Average();
            long jitter = pings.Max() - pings.Min();
            return (avg, jitter);
        }

        return await MeasureHttpLatencyAsync();
    }

    private static async Task<(long ping, long jitter)> MeasureHttpLatencyAsync()
    {
        // Ordre de fiabilité décroissant :
        // 1. HTTP Microsoft — utilisé par Windows lui-même, jamais bloqué sur un PC Windows
        // 2. HTTP Google — fallback universel
        // 3. HTTPS Cloudflare — en dernier (peut être bloqué par SSL inspection)
        string[] endpoints =
        [
            "http://www.msftconnecttest.com/connecttest.txt",
            "http://connectivitycheck.gstatic.com/generate_204",
            "https://speed.cloudflare.com/__down?bytes=1",
        ];

        foreach (var url in endpoints)
        {
            var times = new List<long>();
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("EKIPPP-Optimizer/1.0");

                for (int i = 0; i < 3; i++)
                {
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                        sw.Stop();
                        times.Add(sw.ElapsedMilliseconds);
                    }
                    catch { sw.Stop(); }
                    if (i < 2) await Task.Delay(80);
                }
            }
            catch { }

            if (times.Count >= 2)
            {
                long avg    = (long)times.Average();
                long jitter = times.Max() - times.Min();
                return (avg, jitter);
            }
        }

        return (0, 0);
    }
}
