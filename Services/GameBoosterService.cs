using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EkipppOptimizer.Services;

public record BoostResult(string GameName, int Pid, bool Success, string Message);

public record GameSession(string GameName, DateTime StartTime, DateTime EndTime)
{
    public TimeSpan Duration     => EndTime - StartTime;
    public string   DurationLabel => Duration.TotalHours >= 1
        ? $"{(int)Duration.TotalHours}h{Duration.Minutes:00}m"
        : $"{(int)Duration.TotalMinutes} min";
    public string   DateLabel     => StartTime.ToString("dd/MM HH:mm");
}

public class GameBoosterService
{
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint uPeriod);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint uPeriod);

    private bool _timerResSet = false;
    // ── Processus connus comme étant des jeux ────────────────────────────────
    private static readonly HashSet<string> KnownGames = new(StringComparer.OrdinalIgnoreCase)
    {
        // FPS / Battle Royale
        "GTA5","GTAV","RDR2","Cyberpunk2077","eldenring","SEKIRO","ds3","darksouls3",
        "Borderlands3","BL3","DeepRock","DRG","Satisfactory","valheim","v-rising",
        "VALORANT","VALORANT-Win64-Shipping","csgo","cs2","cod","modernwarfare","warzone",
        "Fortnite","FortniteClient-Win64-Shipping","ApexLegends","r5apex",
        "overwatch","overwatch2","paladins","destiny2","EscapeFromTarkov",
        "RainbowSix","RainbowSixGame","r6siege","siege",
        // MMO / RPG
        "wow","WoWClassic","ffxiv_dx11","LeagueOfLegends","League Of Legends",
        "dota2","TFT","PathOfExile","PathOfExile2","Diablo IV","Diablo4",
        // FiveM / RAGE
        "FiveM","FiveM_GTAProcess","GTA5","fivem-win32-release",
        // Sport / Course
        "FIFA23","FIFA24","FC24","eafc","NFS","nfs","F12023","F12024","Forza",
        "ForzaHorizon5","FM8","rFactor","AssettoCorsa","acs",
        // Survie / Builder
        "Minecraft","javaw","FortniteClient","7DaysToDie","TheForest","SonsOfTheForest",
        "ARK","ShooterGame","RustClient","rust","Terraria",
        // Autres populaires
        "RocketLeague","rocket_league","PUBG","TslGame","BattlefieldV","bf","bfv","bf2042",
        "AmongUs","Hades","DeepRockGalactic","Phasmophobia","GroundedGame",
        "MonsterHunterWorld","MonsterHunterRise","Palworld","Pal","Enshrouded",
        "helldivers2","HellDivers","StreetFighter6","tekken8","MortalKombat1",
        "Hogwarts","HogwartsLegacy","starfield","Starfield","bg3","baldursgate3",
        "alan_wake_2","AlanWake2","SpiderMan","SpiderManPC","GodOfWar","GTFO",
        "DayZ","DayzSA","SquadGame","Squad","Hell Let Loose","HLL",
        "Battlefield2042","bf2042_client","mw2","mwii","mwiii","codmw",
    };

    // ── Overlays à killer pour booster ──────────────────────────────────────
    // "Discord" (app complète) exclue volontairement pour préserver le vocal — seul DiscordOverlayHost est ciblé.
    private static readonly string[] OverlayProcesses =
    [
        "DiscordOverlayHost",
        "GameBarFTServer", "GameBar",
        "SearchUI", "SearchApp",
        "XboxApp", "XboxGameBarWidgets",
        "EpicGamesLauncher",
        "Origin", "EADesktop",
        "GalaxyClient",
        "NvNodeLauncher", "nvcontainer",
        "GeForceExperience",
        "RadeonSoftware", "CNext", "cnext",
    ];

    private List<(Process proc, ProcessPriorityClass prevPriority)> _boostedProcesses = [];
    private System.Timers.Timer? _watchTimer;
    private DateTime? _sessionStart;
    private string    _sessionGame = "";

    public event Action<string>?      GameDetected;
    public event Action<string>?      GameEnded;
    public event Action<GameSession>? SessionCompleted;

    public bool IsWatching { get; private set; }
    public string? CurrentGame { get; private set; }

    // ── Scan manuel ─────────────────────────────────────────────────────────
    public List<BoostResult> BoostAllRunningGames()
    {
        var results = new List<BoostResult>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (!IsGameProcess(p.ProcessName)) continue;
                var prev = p.PriorityClass;
                p.PriorityClass = ProcessPriorityClass.High;
                SetAffinityAll(p);
                results.Add(new BoostResult(p.ProcessName, p.Id, true, $"Priorité élevée appliquée (était: {prev})"));
                _boostedProcesses.Add((p, prev));
            }
            catch (Exception ex)
            {
                results.Add(new BoostResult(p.ProcessName, -1, false, ex.Message));
            }
        }
        return results;
    }

    // ── Surveillance automatique ─────────────────────────────────────────────
    public void StartWatching()
    {
        if (IsWatching) return;
        IsWatching = true;
        // Timer résolution 1ms — réduit la latence système et améliore la régularité des frames
        if (!_timerResSet) { timeBeginPeriod(1); _timerResSet = true; }
        _watchTimer = new System.Timers.Timer(3000);
        _watchTimer.Elapsed += (_, _) => Tick();
        _watchTimer.Start();
    }

    public void StopWatching()
    {
        IsWatching = false;
        _watchTimer?.Stop();
        _watchTimer?.Dispose();
        _watchTimer = null;
        if (_timerResSet) { timeEndPeriod(1); _timerResSet = false; }
        RestoreAll();
        CurrentGame = null;
    }

    private void Tick()
    {
        try
        {
            string? found = null;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (!IsGameProcess(p.ProcessName)) continue;
                    found = p.ProcessName;
                    if (!_boostedProcesses.Any(b => b.proc.Id == p.Id))
                    {
                        var prev = p.PriorityClass;
                        p.PriorityClass = ProcessPriorityClass.High;
                        SetAffinityAll(p);
                        _boostedProcesses.Add((p, prev));
                        if (_sessionStart == null)
                        {
                            _sessionStart = DateTime.Now;
                            _sessionGame  = p.ProcessName;
                        }
                        GameDetected?.Invoke(p.ProcessName);
                    }
                }
                catch { }
            }

            if (found != CurrentGame)
            {
                if (found == null && CurrentGame != null)
                {
                    GameEnded?.Invoke(CurrentGame);
                    if (_sessionStart.HasValue)
                    {
                        SessionCompleted?.Invoke(new GameSession(_sessionGame, _sessionStart.Value, DateTime.Now));
                        _sessionStart = null;
                        _sessionGame  = "";
                    }
                }
                CurrentGame = found;
            }

            // Nettoyer les processus qui ont fermé
            _boostedProcesses.RemoveAll(b => { try { return b.proc.HasExited; } catch { return true; } });
        }
        catch { }
    }

    private void RestoreAll()
    {
        foreach (var (proc, prev) in _boostedProcesses)
        {
            try { if (!proc.HasExited) proc.PriorityClass = prev; } catch { }
        }
        _boostedProcesses.Clear();
    }

    private static bool IsGameProcess(string name)
        => KnownGames.Contains(name)
        || KnownGames.Any(k => name.StartsWith(k, StringComparison.OrdinalIgnoreCase));

    private static void SetAffinityAll(Process p)
    {
        try
        {
            var cores = Environment.ProcessorCount;
            if (cores <= 1) return;
            nint mask = ((nint)1 << cores) - 1;
            p.ProcessorAffinity = mask;
        }
        catch { }
    }

    // ── Killer overlays ──────────────────────────────────────────────────────
    public List<string> GetRunningOverlays()
    {
        var running = new List<string>();
        foreach (var name in OverlayProcesses)
        {
            var procs = Process.GetProcessesByName(name);
            if (procs.Length > 0) running.Add(name);
        }
        return running;
    }

    public (int killed, List<string> names) KillOverlays()
    {
        var killed = new List<string>();
        foreach (var name in OverlayProcesses)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { p.Kill(true); killed.Add(name); } catch { }
            }
        }
        return (killed.Count, killed);
    }
}
