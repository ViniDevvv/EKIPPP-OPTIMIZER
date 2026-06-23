using System.Diagnostics.Eventing.Reader;
using System.Text.RegularExpressions;

namespace EkipppOptimizer.Services;

public record BsodEntry(DateTime Time, string StopCode, string Description);

public class BsodAnalyzerService
{
    private static readonly Dictionary<string, string> KnownCodes = new()
    {
        ["0x0000003B"] = "SYSTEM_SERVICE_EXCEPTION — souvent un driver défectueux",
        ["0x00000050"] = "PAGE_FAULT_IN_NONPAGED_AREA — RAM ou driver corrompus",
        ["0x0000007E"] = "SYSTEM_THREAD_EXCEPTION — driver incompatible",
        ["0x0000007F"] = "UNEXPECTED_KERNEL_MODE_TRAP — souvent RAM défectueuse",
        ["0x000000EF"] = "CRITICAL_PROCESS_DIED — processus système planté",
        ["0x000000D1"] = "DRIVER_IRQL_NOT_LESS_OR_EQUAL — driver réseau ou GPU",
        ["0x0000001E"] = "KMODE_EXCEPTION_NOT_HANDLED — driver ou mémoire",
        ["0xC000021A"] = "WINLOGON/CSRSS planté — fichiers système corrompus",
        ["0x00000116"] = "VIDEO_TDR_FAILURE — driver GPU planté",
        ["0xDEADDEAD"] = "MANUALLY_INITIATED_CRASH",
    };

    public List<BsodEntry> GetRecentCrashes(int maxCount = 10)
    {
        var results = new List<BsodEntry>();

        // Source 1 : BugCheck (BSOD réels)
        TryReadEvents("*[System[Provider[@Name='BugCheck'] and EventID=1001]]",
            results, maxCount, isBugCheck: true);

        // Source 2 : Kernel-Power 41 (arrêts inattendus — coupure courant ou BSOD sans dump)
        if (results.Count < maxCount)
        {
            TryReadEvents("*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41]]",
                results, maxCount - results.Count, isBugCheck: false);
        }

        return results.OrderByDescending(r => r.Time).ToList();
    }

    private static void TryReadEvents(string queryStr, List<BsodEntry> results, int max, bool isBugCheck)
    {
        try
        {
            var query  = new EventLogQuery("System", PathType.LogName, queryStr);
            using var reader = new EventLogReader(query);
            EventRecord? record;
            while ((record = reader.ReadEvent()) != null && results.Count < max)
            {
                using (record)
                {
                    try
                    {
                        var time  = record.TimeCreated ?? DateTime.MinValue;
                        string desc, code;

                        if (isBugCheck)
                        {
                            var raw = record.FormatDescription() ?? "";
                            code    = ExtractStopCode(raw);
                            desc    = KnownCodes.TryGetValue(code, out var known) ? known : raw.Split('\n').FirstOrDefault()?.Trim() ?? "—";
                        }
                        else
                        {
                            code = "Arrêt inattendu";
                            desc = "Windows s'est arrêté sans BSOD (coupure courant ou gel système).";
                        }

                        results.Add(new BsodEntry(time, code, desc));
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    private static string ExtractStopCode(string text)
    {
        var m = Regex.Match(text, @"0x[0-9A-Fa-f]{8}", RegexOptions.IgnoreCase);
        return m.Success ? m.Value.ToUpperInvariant() : "Inconnu";
    }
}
