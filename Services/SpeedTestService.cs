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

            double elapsed = sw.Elapsed.TotalSeconds;
            if (elapsed >= 2.0 && totalBytes > 0)
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
            double mbps    = elapsed > 0.5 ? dataSize * 8.0 / (1024 * 1024) / elapsed : 0;
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
