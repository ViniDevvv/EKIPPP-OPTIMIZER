using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EkipppOptimizer.Services;

namespace EkipppOptimizer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    // ── Services ──────────────────────────────────────────────────────────────
    private readonly WindowsOptimizerService _optimizer    = new();
    private readonly GameDetectionService    _gameDetect   = new();
    private readonly StartupManagerService   _startupMgr   = new();
    private readonly PcInfoService           _pcInfo       = new();
    private readonly CleanerService          _cleaner      = new();
    private readonly HardwareMonitorService  _hwMonitor    = new();
    private readonly WindowsRepairService    _repair       = new();
    private readonly StorageService          _storage      = new();
    private readonly DriverService           _drivers      = new();
    private readonly DiagnosticsService      _diagnostics  = new();
    private readonly RestorePointService     _restore      = new();
    private readonly ScheduledTaskService    _scheduler    = new();
    private readonly GameBoosterService      _booster          = new();
    private readonly RamOptimizerService     _ramOptimizer     = new();
    private readonly TemperatureService      _thermal          = new();
    private readonly ServiceOptimizerService _serviceOptimizer = new();
    private readonly BsodAnalyzerService     _bsodAnalyzer     = new();
    private readonly ActionHistoryService    _history          = new();
    private readonly DnsSpeedTestService     _dnsTest          = new();
    private readonly SpeedTestService        _speedTest        = new();
    private readonly GameCacheService        _gameCache        = new();
    private readonly AppUninstallerService   _uninstaller      = new();
    private readonly BrowserCleanerService   _browserCleaner   = new();

    private DispatcherTimer? _monitorTimer;
    private DispatcherTimer? _diagDebounce;
    private DispatcherTimer? _diagPeriodic;

    // ── Snapshot de configuration ─────────────────────────────────────────────
    private record TweakSnapshot(
        bool GameDvr, bool FullscreenOptim, bool GpuPriority, bool SystemResp,
        bool Win32Priority, bool MousePrecision, bool HighPerfPlan, bool NetThrottle,
        bool Telemetry, bool AdvertisingId, bool Location, bool Cortana, bool VisualEffects);

    private TweakSnapshot? _savedSnapshot;

    [ObservableProperty] private bool   _hasSnapshot   = false;
    [ObservableProperty] private string _snapshotLabel = "";

    private const string SnapshotRegKey = @"SOFTWARE\EKIPPP-OPTIMIZER\Snapshot";
    private const string AppRegKey      = @"SOFTWARE\EKIPPP-OPTIMIZER\App";

    private void PersistSnapshot(TweakSnapshot s, string label)
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(SnapshotRegKey);
            k.SetValue("GameDvr",        s.GameDvr        ? 1 : 0);
            k.SetValue("FullscreenOptim",s.FullscreenOptim? 1 : 0);
            k.SetValue("GpuPriority",    s.GpuPriority    ? 1 : 0);
            k.SetValue("SystemResp",     s.SystemResp     ? 1 : 0);
            k.SetValue("Win32Priority",  s.Win32Priority  ? 1 : 0);
            k.SetValue("MousePrecision", s.MousePrecision ? 1 : 0);
            k.SetValue("HighPerfPlan",   s.HighPerfPlan   ? 1 : 0);
            k.SetValue("NetThrottle",    s.NetThrottle    ? 1 : 0);
            k.SetValue("Telemetry",      s.Telemetry      ? 1 : 0);
            k.SetValue("AdvertisingId",  s.AdvertisingId  ? 1 : 0);
            k.SetValue("Location",       s.Location       ? 1 : 0);
            k.SetValue("Cortana",        s.Cortana        ? 1 : 0);
            k.SetValue("VisualEffects",  s.VisualEffects  ? 1 : 0);
            k.SetValue("Label",          label);
        }
        catch { }
    }

    private void LoadPersistedSnapshot()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(SnapshotRegKey);
            if (k == null) return;
            bool B(string n) => Convert.ToInt32(k.GetValue(n, 0)) == 1;
            _savedSnapshot = new TweakSnapshot(
                B("GameDvr"), B("FullscreenOptim"), B("GpuPriority"), B("SystemResp"),
                B("Win32Priority"), B("MousePrecision"), B("HighPerfPlan"), B("NetThrottle"),
                B("Telemetry"), B("AdvertisingId"), B("Location"), B("Cortana"), B("VisualEffects"));
            SnapshotLabel = k.GetValue("Label")?.ToString() ?? "sauvegardée précédemment";
            HasSnapshot   = true;
            ProfileStatus = $"✓ Configuration {SnapshotLabel} — appliquez un profil en toute sécurité.";
        }
        catch { }
    }

    public bool HasCriticalIssues => CriticalCount > 0;

    partial void OnCriticalCountChanged(int value) => OnPropertyChanged(nameof(HasCriticalIssues));

    [RelayCommand]
    private void SaveCurrentState()
    {
        _savedSnapshot = new TweakSnapshot(
            TwGameDvr, TwFullscreenOptim, TwGpuPriority, TwSystemResp,
            TwWin32Priority, TwMousePrecision, TwHighPerfPlan, TwNetThrottle,
            TwTelemetry, TwAdvertisingId, TwLocation, TwCortana, TwVisualEffects);
        var label = $"sauvegardée le {DateTime.Now:dd/MM/yyyy à HH:mm}";
        HasSnapshot   = true;
        SnapshotLabel = label;
        ProfileStatus = $"✓ Configuration {label} — appliquez un profil en toute sécurité.";
        PersistSnapshot(_savedSnapshot, label);
        ShowToast?.Invoke("Sauvegarde", "Votre configuration actuelle est sauvegardée ✓");
    }

    [RelayCommand]
    private void ClearSnapshot()
    {
        _savedSnapshot = null;
        HasSnapshot    = false;
        SnapshotLabel  = "";
        ProfileStatus  = "Aucun profil appliqué — choisissez un profil ci-dessous.";
        try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(SnapshotRegKey, throwOnMissingSubKey: false); } catch { }
        ShowToast?.Invoke("Sauvegarde", "Sauvegarde supprimée ✓");
    }

    [RelayCommand]
    private async Task RestoreSnapshotAsync()
    {
        if (_savedSnapshot is not { } s) return;
        ProfileStatus = "Restauration de votre configuration sauvegardée…";
        await Task.Run(() =>
        {
            _optimizer.SetGameDvr(s.GameDvr);
            _optimizer.SetFullscreenOptim(s.FullscreenOptim);
            _optimizer.SetGpuPriority(s.GpuPriority);
            _optimizer.SetSystemResponsiveness(s.SystemResp);
            _optimizer.SetWin32Priority(s.Win32Priority);
            _optimizer.SetMousePrecision(s.MousePrecision);
            _optimizer.SetHighPerfPlan(s.HighPerfPlan);
            _optimizer.SetNetworkThrottling(s.NetThrottle);
            _optimizer.SetTelemetry(s.Telemetry);
            _optimizer.SetAdvertisingId(s.AdvertisingId);
            _optimizer.SetLocation(s.Location);
            _optimizer.SetCortana(s.Cortana);
            _optimizer.SetVisualEffects(s.VisualEffects);
        });
        LoadTweakStates();
        ActiveProfile = "";
        ProfileStatus = $"✓ Configuration restaurée exactement comme {SnapshotLabel}.";
        ShowToast?.Invoke("Restauration", "Votre configuration personnelle restaurée ✓");
        ScheduleDiagRefresh();
    }

    // ── Tab navigation ────────────────────────────────────────────────────────
    [ObservableProperty] private int _selectedTab = 0;

    partial void OnSelectedTabChanged(int value)
    {
        for (int i = 0; i <= 12; i++) OnPropertyChanged($"IsTab{i}Active");
        // Tab 0=Dashboard  1=Nettoyage  2=Démarrage  3=Réseau  4=Stockage
        //     5=Pilotes   6=Problèmes  7=Profils    8=Gaming  9=Confidentialité
        //     10=Sécurité 11=Automatisation 12=Désinstalleur
        switch (value)
        {
            case 0:  _ = RunAnalysisAsync(); StartMonitoring();    break;
            case 2:  RefreshStartup();                             break;
            case 4:  _ = LoadStorageAsync();                       break;
            case 5:  _ = LoadDriversAsync();                       break;
            case 6:  _ = RunDiagnosticsAsync();                    break;
            case 8:  RefreshGames();                                break;
            case 10: _ = LoadRestorePointsAsync(); RefreshBsod();  break;
            case 11: _ = RefreshScheduledTasksAsync();             break;
            case 12: _ = RefreshAppsAsync();                       break;
            default: StopMonitoring();                             break;
        }
    }

    public bool IsTab0Active  => SelectedTab == 0;
    public bool IsTab1Active  => SelectedTab == 1;
    public bool IsTab2Active  => SelectedTab == 2;
    public bool IsTab3Active  => SelectedTab == 3;
    public bool IsTab4Active  => SelectedTab == 4;
    public bool IsTab5Active  => SelectedTab == 5;
    public bool IsTab6Active  => SelectedTab == 6;
    public bool IsTab7Active  => SelectedTab == 7;
    public bool IsTab8Active  => SelectedTab == 8;
    public bool IsTab9Active  => SelectedTab == 9;
    public bool IsTab10Active => SelectedTab == 10;
    public bool IsTab11Active => SelectedTab == 11;
    public bool IsTab12Active => SelectedTab == 12;

    [RelayCommand]
    private void SelectTab(string? tabStr)
    {
        if (int.TryParse(tabStr, out int tab)) SelectedTab = tab;
    }

    public Action<string, string>? ShowToast { get; set; }

    // Détection droits admin (certaines ops — netsh, TRIM, registry — nécessitent admin)
    public bool IsAdmin { get; } = new System.Security.Principal.WindowsPrincipal(
        System.Security.Principal.WindowsIdentity.GetCurrent())
        .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

    [RelayCommand]
    private void RestartAsAdmin()
    {
        try
        {
            var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
                { UseShellExecute = true, Verb = "runas" });
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                if (System.Windows.Application.Current is App app) app.ExplicitShutdown();
            });
        }
        catch { }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // TAB 0 — ANALYSE PC
    // ══════════════════════════════════════════════════════════════════════════
    [ObservableProperty] private int    _healthScore     = 0;
    [ObservableProperty] private string _healthLabel     = "Analyse en cours…";
    [ObservableProperty] private string _cpuSummary      = "…";
    [ObservableProperty] private string _gpuSummary      = "…";
    [ObservableProperty] private string _ramSummary      = "…";
    [ObservableProperty] private string _diskSummary     = "…";
    [ObservableProperty] private string _netSummary      = "…";
    [ObservableProperty] private string _batterySummary  = "…";
    [ObservableProperty] private string _boardSummary    = "…";
    [ObservableProperty] private string _osSummary       = "…";
    [ObservableProperty] private bool   _isAnalyzing     = false;

    [RelayCommand]
    private async Task RunAnalysisAsync()
    {
        IsAnalyzing = true;
        HealthLabel = "Analyse en cours…";
        try
        {
            var (cpu, gpus, ram, disks, nets, battery, board, os, score) = await Task.Run(() =>
            {
                var c   = _pcInfo.GetCpuInfo();
                var g   = _pcInfo.GetGpuInfo();
                var r   = _pcInfo.GetRamInfo();
                var d   = _pcInfo.GetDiskInfo();
                var n   = _pcInfo.GetNetworkAdapters();
                var b   = _pcInfo.GetBatteryInfo();
                var mb  = _pcInfo.GetMotherboard();
                var win = _pcInfo.GetWindowsVersion();
                var sc  = _pcInfo.ComputeHealthScore(c, r, d, g.FirstOrDefault());
                return (c, g, r, d, n, b, mb, win, sc);
            });

            CpuSummary     = $"{cpu.Name}  ·  {cpu.Cores} cœurs / {cpu.Threads} threads  ·  {cpu.SpeedGHz} GHz  ·  Charge: {cpu.LoadPercent:F0}%";
            GpuSummary     = gpus.Count > 0 ? string.Join(" | ", gpus.Select(g => g.VramMB > 0 ? $"{g.Name} ({g.VramMB} Mo)" : g.Name)) : "GPU non détecté";
            RamSummary     = $"{ram.TotalMB / 1024} Go total  ·  {ram.AvailableMB / 1024} Go disponible  ·  {(ram.TotalMB - ram.AvailableMB) * 100 / Math.Max(ram.TotalMB, 1):F0}% utilisé";
            DiskSummary    = disks.Count > 0 ? string.Join(" | ", disks.Select(d => $"{d.Letter} {(d.IsSSD ? "SSD" : "HDD")} {d.TotalGB} Go ({d.FreeGB} Go libre)")) : "Aucun disque";
            NetSummary     = nets.Count > 0 ? string.Join(" | ", nets.Select(n => $"{n.Name.Split(' ').First()} {n.IpAddress}")) : "Aucun adaptateur";
            BatterySummary = battery.HasBattery ? $"{battery.ChargePercent}%  {(battery.IsCharging ? "· En charge" : "· Sur batterie")}" : "Pas de batterie (PC fixe)";
            BoardSummary   = $"{board.Manufacturer} {board.Product}";
            OsSummary      = os;
            HealthScore    = score;
            HealthLabel    = score >= 85 ? "Excellent" : score >= 70 ? "Bon" : score >= 50 ? "Moyen" : "Faible";
        }
        catch (Exception ex) { HealthLabel = $"Erreur: {ex.Message}"; }
        finally { IsAnalyzing = false; }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // TAB 1 — SURVEILLANCE TEMPS RÉEL
    // ══════════════════════════════════════════════════════════════════════════
    [ObservableProperty] private double _monCpuPercent    = 0;
    [ObservableProperty] private double _monRamPercent    = 0;
    [ObservableProperty] private double _monRamUsedGB     = 0;
    [ObservableProperty] private double _monRamTotalGB    = 0;
    [ObservableProperty] private double _monDiskReadMBs   = 0;
    [ObservableProperty] private double _monDiskWriteMBs  = 0;
    [ObservableProperty] private double _monNetDownKBs    = 0;
    [ObservableProperty] private double _monNetUpKBs      = 0;
    [ObservableProperty] private double _monNetPercent    = 8;
    [ObservableProperty] private string _monNetCenter     = "0K";
    [ObservableProperty] private double _monDiskCPercent  = 0;
    [ObservableProperty] private string _monNetLabel      = "0 Ko/s";
    [ObservableProperty] private string _monCpuBar        = "";
    [ObservableProperty] private string _monRamBar        = "";

    // Températures temps réel
    [ObservableProperty] private double _monCpuTemp     = 0;
    [ObservableProperty] private double _monGpuTemp     = 0;
    [ObservableProperty] private double _monGpuLoad     = 0;
    [ObservableProperty] private double _monGpuVramUsed = 0;
    [ObservableProperty] private double _monGpuVramTotal= 0;
    [ObservableProperty] private string _monCpuTempLabel  = "—";
    [ObservableProperty] private string _monGpuTempLabel  = "—";
    [ObservableProperty] private string _monGpuVramLabel  = "—";

    // Ventilateurs temps réel
    [ObservableProperty] private string _monCpuFanLabel = "—";
    [ObservableProperty] private string _monGpuFanLabel = "—";

    // Ping live
    [ObservableProperty] private string _monPingLabel = "—";

    // Score PC global
    [ObservableProperty] private int    _pcScore       = 0;
    [ObservableProperty] private string _pcScoreGrade  = "—";
    [ObservableProperty] private System.Windows.Media.Brush _pcScoreBrush =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA8, 0x99, 0xC4));

    public ObservableCollection<CoreUsageItem> CoreUsages { get; } = [];
    public ObservableCollection<ProcessStat>   TopProcesses { get; } = [];

    private void ScheduleDiagRefresh()
    {
        _diagDebounce?.Stop();
        _diagDebounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _diagDebounce.Tick += async (_, _) =>
        {
            _diagDebounce?.Stop();
            if (!IsDiagBusy) await RunDiagnosticsAsync();
        };
        _diagDebounce.Start();
    }

    private void StartMonitoring()
    {
        _hwMonitor.Initialize();
        _thermal.Initialize();
        if (_monitorTimer != null) return;
        _monitorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _monitorTimer.Tick += OnMonitorTick;
        _monitorTimer.Start();
        // Re-scan diagnostics every 2 minutes to keep score live
        _diagPeriodic = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        _diagPeriodic.Tick += async (_, _) => { if (!IsDiagBusy) await RunDiagnosticsAsync(); };
        _diagPeriodic.Start();
    }

    private void StopMonitoring()
    {
        _monitorTimer?.Stop();
        _monitorTimer = null;
        _diagPeriodic?.Stop();
        _diagPeriodic = null;
    }

    private int    _thermalTick    = 0;
    private int    _processTick    = 0;
    private int    _pingTick       = 0;
    private bool   _pinging        = false;
    private bool   _cpuAlertSent   = false;
    private bool   _gpuAlertSent   = false;
    private string _previousPowerPlanGuid = "";

    private void OnMonitorTick(object? sender, EventArgs e)
    {
        try
        {
            var snap = _hwMonitor.Sample();
            MonCpuPercent   = snap.CpuPercent;
            MonRamPercent   = snap.RamTotalMB > 0 ? snap.RamUsedMB / snap.RamTotalMB * 100 : 0;
            MonRamUsedGB    = Math.Round(snap.RamUsedMB / 1024, 1);
            MonRamTotalGB   = Math.Round(snap.RamTotalMB / 1024, 1);
            MonDiskReadMBs  = snap.DiskReadMBs;
            MonDiskWriteMBs = snap.DiskWriteMBs;
            MonNetDownKBs   = snap.NetworkDownKBs;
            MonNetUpKBs     = snap.NetworkUpKBs;
            double netKbps  = snap.NetworkDownKBs;
            MonNetPercent   = netKbps <= 0
                ? 8  // arc minimal visible même à 0 Ko/s (3% = 10° trop petit)
                : Math.Max(10, Math.Min(100, Math.Log10(netKbps + 1) / Math.Log10(10001) * 100));
            MonNetCenter    = netKbps >= 1024
                ? $"{netKbps / 1024:F1}M"
                : $"{netKbps:F0}K";
            MonDiskCPercent = snap.DiskCUsedPercent;
            MonNetLabel     = snap.NetworkDownKBs >= 1024
                ? $"{snap.NetworkDownKBs / 1024:F1} Mo/s ↓"
                : $"{snap.NetworkDownKBs:F0} Ko/s ↓";
            MonCpuBar       = BuildBar(snap.CpuPercent);
            MonRamBar       = BuildBar(MonRamPercent);

            // Per-core bars
            while (CoreUsages.Count < snap.CorePercents.Length)
                CoreUsages.Add(new CoreUsageItem(CoreUsages.Count));
            for (int i = 0; i < snap.CorePercents.Length; i++)
                CoreUsages[i].Percent = snap.CorePercents[i];
        }
        catch { }

        // Processus : refresh toutes les 5 secondes
        if (++_processTick % 5 == 0) RefreshTopProcesses();

        // Ping : refresh toutes les 5 secondes
        if (++_pingTick % 5 == 0 && !_pinging) _ = UpdatePingAsync();

        // Températures : refresh toutes les 2 secondes (LHM est plus lourd)
        if (++_thermalTick % 2 == 0)
        {
            try
            {
                var t = _thermal.Sample();
                MonCpuTemp      = t.CpuTemp;
                MonGpuTemp      = t.GpuTemp;
                MonGpuLoad      = t.GpuLoad;
                MonGpuVramUsed  = t.GpuVramUsedMB;
                MonGpuVramTotal = t.GpuVramTotalMB;
                MonCpuTempLabel = t.CpuTemp > 0 ? $"{t.CpuTemp:F0}°C" : "—";
                MonGpuTempLabel = t.GpuTemp > 0 ? $"{t.GpuTemp:F0}°C" : "—";
                MonGpuVramLabel = t.GpuVramTotalMB > 0
                    ? $"{t.GpuVramUsedMB:F0} / {t.GpuVramTotalMB:F0} Mo"
                    : "—";
                MonCpuFanLabel = t.CpuFanRpm > 0 ? $"{t.CpuFanRpm:F0} tr/min" : "—";
                MonGpuFanLabel = t.GpuFanRpm > 0 ? $"{t.GpuFanRpm:F0} tr/min" : "—";

                // Alertes température
                if (t.CpuTemp >= 85 && !_cpuAlertSent)
                {
                    _cpuAlertSent = true;
                    ShowToast?.Invoke("⚠ CPU surchauffe", $"Température CPU: {t.CpuTemp:F0}°C — risque de throttling");
                }
                else if (t.CpuTemp < 80)
                    _cpuAlertSent = false;

                if (t.GpuTemp >= 80 && !_gpuAlertSent)
                {
                    _gpuAlertSent = true;
                    ShowToast?.Invoke("⚠ GPU chaud", $"Température GPU: {t.GpuTemp:F0}°C — surveillez les performances");
                }
                else if (t.GpuTemp < 75)
                    _gpuAlertSent = false;
            }
            catch { }
        }
    }

    private async Task UpdatePingAsync()
    {
        _pinging = true;
        try
        {
            using var ping = new System.Net.NetworkInformation.Ping();
            var reply = await ping.SendPingAsync("8.8.8.8", 1500);
            MonPingLabel = reply.Status == System.Net.NetworkInformation.IPStatus.Success
                ? $"{reply.RoundtripTime} ms"
                : "—";
        }
        catch { MonPingLabel = "—"; }
        finally { _pinging = false; }
    }

    [RelayCommand]
    private void RefreshTopProcesses()
    {
        Task.Run(() =>
        {
            var procs = System.Diagnostics.Process.GetProcesses()
                .Where(p => { try { return p.WorkingSet64 > 0; } catch { return false; } })
                .OrderByDescending(p => { try { return p.WorkingSet64; } catch { return 0L; } })
                .Take(10)
                .Select(p =>
                {
                    try { return new ProcessStat(p.ProcessName, p.WorkingSet64 / (1024 * 1024), p.Id); }
                    catch { return null; }
                })
                .Where(x => x != null)
                .Cast<ProcessStat>()
                .ToList();

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                TopProcesses.Clear();
                foreach (var p in procs) TopProcesses.Add(p);
            });
        });
    }

    private void UpdatePcScore(int criticals, int warnings)
    {
        int score = 100 - (criticals * 15) - (warnings * 5);
        score = Math.Clamp(score, 0, 100);
        PcScore = score;
        var (grade, hex) = score switch
        {
            >= 90 => ("A+", 0x4ADE80),
            >= 80 => ("A",  0x86EFAC),
            >= 70 => ("B",  0xFDE047),
            >= 55 => ("C",  0xFB923C),
            >= 40 => ("D",  0xF87171),
            _     => ("F",  0xEF4444),
        };
        PcScoreGrade = grade;
        var c = System.Windows.Media.Color.FromRgb(
            (byte)((hex >> 16) & 0xFF),
            (byte)((hex >> 8)  & 0xFF),
            (byte)(hex         & 0xFF));
        PcScoreBrush = new System.Windows.Media.SolidColorBrush(c);

        // Advice to improve score
        if (score >= 90)
        {
            PcScoreAdvice = "PC en excellente santé — aucune action requise.";
        }
        else
        {
            var tips = Issues
                .Where(i => i.Severity is IssueSeverity.Critical or IssueSeverity.Warning)
                .OrderByDescending(i => (int)i.Severity)
                .Take(3)
                .Select(i => $"• {i.Title} (+{(i.Severity == IssueSeverity.Critical ? 15 : 5)} pts)")
                .ToList();
            PcScoreAdvice = tips.Count > 0
                ? "Pour augmenter le score :\n" + string.Join("\n", tips)
                : "Aucune action requise.";
        }
    }

    private static string BuildBar(double percent)
    {
        int filled = (int)(percent / 5);
        return new string('█', Math.Clamp(filled, 0, 20)) + new string('░', 20 - Math.Clamp(filled, 0, 20));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // HISTORIQUE DES ACTIONS (persistant)
    // ══════════════════════════════════════════════════════════════════════════
    public ObservableCollection<HistoryEntry> History { get; } = [];

    private void LogAction(string category, string action, string result)
    {
        _history.Add(category, action, result);
        var entry = _history.GetRecent(1).FirstOrDefault();
        if (entry == null) return;
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            History.Insert(0, entry);
            while (History.Count > 50) History.RemoveAt(History.Count - 1);
        });
    }

    private void LoadHistory()
    {
        var entries = _history.GetRecent(50);
        History.Clear();
        foreach (var e in entries) History.Add(e);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SCORE HEBDOMADAIRE — comparaison vs semaine précédente
    // ══════════════════════════════════════════════════════════════════════════
    private const string ScoreRegKey = @"SOFTWARE\EKIPPP-OPTIMIZER\WeeklyScore";

    [ObservableProperty] private int    _weeklyScoreDelta = 0;
    [ObservableProperty] private string _scoreDeltaLabel  = "";

    private void SaveWeeklyScore(int score)
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(ScoreRegKey);
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var stored = k.GetValue("Date")?.ToString() ?? "";
            if (stored == today) return; // ne pas écraser le même jour
            k.SetValue("PrevScore", k.GetValue("Score") ?? score);
            k.SetValue("Score",     score);
            k.SetValue("Date",      today);
        }
        catch { }
    }

    private void LoadWeeklyScore()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ScoreRegKey);
            if (k == null) return;
            int cur  = Convert.ToInt32(k.GetValue("Score",     0));
            int prev = Convert.ToInt32(k.GetValue("PrevScore", 0));
            if (cur == 0 || prev == 0) return;
            WeeklyScoreDelta = cur - prev;
            ScoreDeltaLabel  = WeeklyScoreDelta >= 0
                ? $"+{WeeklyScoreDelta} vs semaine dernière"
                : $"{WeeklyScoreDelta} vs semaine dernière";
        }
        catch { }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // OPTIMISATION EN 1 CLIC — Dashboard
    // ══════════════════════════════════════════════════════════════════════════
    [ObservableProperty] private bool   _isOneClickBusy    = false;
    [ObservableProperty] private string _oneClickStatus    = "Lance une optimisation complète — nettoyage, TRIM, TCP, RAM, profil Gaming.";
    [ObservableProperty] private string _oneClickBeforeAfter = "";

    [RelayCommand]
    private async Task OneClickOptimizeAsync()
    {
        if (IsOneClickBusy) return;
        IsOneClickBusy   = true;
        OneClickBeforeAfter = "";
        int scoreBefore  = PcScore;
        long totalFreed  = 0;

        try
        {
            OneClickStatus = "Scan du système…";
            var cats = await Task.Run(() => _cleaner.ScanAll(new Progress<string>()));
            totalFreed += cats.Sum(c => c.SizeBytes);

            OneClickStatus = "Nettoyage en cours…";
            var progress = new Progress<string>();
            var (_, freed) = await Task.Run(() => _cleaner.Clean(cats, progress));
            _cleaner.EmptyRecycleBin();
            totalFreed = freed;

            OneClickStatus = "Optimisation TCP/IP…";
            await Task.Run(() => _optimizer.OptimizeTcp());
            _optimizer.FlushDns();

            OneClickStatus = "TRIM SSD…";
            await _storage.RunTrimAsync();

            OneClickStatus = "Libération RAM…";
            await Task.Run(() => _ramOptimizer.OptimizeRam());

            OneClickStatus = "Profil Gaming…";
            await Task.Run(() => _optimizer.ApplyGamingProfile());
            LoadTweakStates();

            OneClickStatus = "Analyse finale…";
            await RunDiagnosticsAsync();

            var label = totalFreed >= 1L << 30
                ? $"{totalFreed / (1024.0 * 1024 * 1024):F1} Go"
                : $"{totalFreed / (1024.0 * 1024):F0} Mo";
            OneClickStatus      = $"Optimisation terminée — {label} libérés, profil Gaming actif.";
            OneClickBeforeAfter = $"Score avant : {scoreBefore}/100  →  Score après : {PcScore}/100";

            LogAction("Optimisation", "1 clic complet", $"{label} libérés, score {scoreBefore}→{PcScore}");
            ShowToast?.Invoke("Optimisation 1 Clic", $"{label} libérés — Score: {PcScore}/100 ✓");
        }
        catch (Exception ex) { OneClickStatus = $"Erreur: {ex.Message}"; }
        finally { IsOneClickBusy = false; }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // TAB 2 — NETTOYAGE
    // ══════════════════════════════════════════════════════════════════════════
    public ObservableCollection<CleanCategoryVm> CleanCategories { get; } = [];
    [ObservableProperty] private long   _totalCleanableBytes = 0;
    [ObservableProperty] private string _cleanStatus         = "Cliquez sur 'Analyser' pour commencer.";
    [ObservableProperty] private bool   _isScanning          = false;
    [ObservableProperty] private bool   _isCleaning          = false;
    [ObservableProperty] private long   _recycleSize         = 0;

    [RelayCommand]
    private async Task ScanCleanAsync()
    {
        IsScanning  = true;
        CleanStatus = "Analyse en cours…";
        CleanCategories.Clear();
        try
        {
            var progress = new Progress<string>(msg => CleanStatus = msg);
            var cats = await Task.Run(() => _cleaner.ScanAll(progress));
            var recycleSz = await Task.Run(() => _cleaner.GetRecycleBinSize());
            RecycleSize = recycleSz;

            long total = recycleSz;
            foreach (var cat in cats)
            {
                CleanCategories.Add(new CleanCategoryVm(cat));
                total += cat.SizeBytes;
            }
            TotalCleanableBytes = total;
            var recycleInfo = recycleSz > 0 ? $" (dont {FormatSize(recycleSz)} en corbeille)" : "";
            CleanStatus = $"Analyse terminée. {FormatSize(total)} récupérables{recycleInfo}.";
        }
        catch (Exception ex) { CleanStatus = $"Erreur: {ex.Message}"; }
        finally { IsScanning = false; }
    }

    [RelayCommand]
    private async Task CleanSelectedAsync()
    {
        IsCleaning  = true;
        CleanStatus = "Nettoyage en cours…";
        try
        {
            var selected = CleanCategories.Where(c => c.IsSelected).Select(c => c.Category).ToList();
            var progress = new Progress<string>(msg => CleanStatus = msg);
            var (deleted, freed) = await Task.Run(() => _cleaner.Clean(selected, progress));
            _cleaner.EmptyRecycleBin();
            CleanStatus = $"Nettoyage terminé. {deleted} fichiers supprimés, {FormatSize(freed)} libérés.";
            LogAction("Nettoyage", $"{deleted} fichiers supprimés", FormatSize(freed));
            ShowToast?.Invoke("Nettoyage", $"{FormatSize(freed)} libérés ✓");
            await ScanCleanAsync();
        }
        catch (Exception ex) { CleanStatus = $"Erreur: {ex.Message}"; }
        finally { IsCleaning = false; }
    }

    [RelayCommand]
    private void EmptyRecycleBin()
    {
        if (_cleaner.EmptyRecycleBin())
        {
            RecycleSize = 0;
            ShowToast?.Invoke("Corbeille", "Corbeille vidée ✓");
        }
    }

    // ── Caches Jeux ──────────────────────────────────────────────────────────
    public ObservableCollection<GameCache> GameCaches { get; } = [];
    [ObservableProperty] private string _gameCacheStatus  = "Scannez pour voir les caches Steam, Discord, FiveM, Valorant…";
    [ObservableProperty] private bool   _isGameCacheBusy  = false;

    [RelayCommand]
    private async Task ScanGameCachesAsync()
    {
        IsGameCacheBusy = true;
        GameCacheStatus = "Scan des caches jeux…";
        try
        {
            var caches = await Task.Run(() => _gameCache.ScanAll());
            GameCaches.Clear();
            foreach (var c in caches) GameCaches.Add(c);
            var total = caches.Sum(c => c.SizeBytes);
            var label = FormatSize(total);
            GameCacheStatus = total > 0
                ? $"{caches.Count(c => !c.IsEmpty)} jeux détectés  ·  {label} récupérables"
                : "Aucun cache de jeu détecté.";
        }
        catch (Exception ex) { GameCacheStatus = $"Erreur: {ex.Message}"; }
        finally { IsGameCacheBusy = false; }
    }

    [RelayCommand]
    private async Task CleanGameCacheAsync(GameCache? cache)
    {
        if (cache == null) return;
        IsGameCacheBusy = true;
        GameCacheStatus = $"Nettoyage {cache.Game}…";
        try
        {
            var progress = new Progress<string>(msg => GameCacheStatus = msg);
            var freed = await _gameCache.CleanAsync(cache, progress);
            GameCacheStatus = freed > 0
                ? $"{cache.Game} nettoyé — {FormatSize(freed)} libérés ✓"
                : $"{cache.Game} : rien à nettoyer.";
            LogAction("Nettoyage", $"Cache {cache.Game}", FormatSize(freed));
            ShowToast?.Invoke("Cache Jeux", $"{cache.Game}: {FormatSize(freed)} libérés ✓");
            await ScanGameCachesAsync();
        }
        catch (Exception ex) { GameCacheStatus = $"Erreur: {ex.Message}"; }
        finally { IsGameCacheBusy = false; }
    }

    [RelayCommand]
    private async Task CleanAllGameCachesAsync()
    {
        IsGameCacheBusy = true;
        GameCacheStatus = "Nettoyage de tous les caches…";
        try
        {
            long total = 0;
            var progress = new Progress<string>(msg => GameCacheStatus = msg);
            foreach (var c in GameCaches.Where(x => !x.IsEmpty).ToList())
                total += await _gameCache.CleanAsync(c, progress);
            GameCacheStatus = total > 0
                ? $"Tous les caches nettoyés — {FormatSize(total)} libérés ✓"
                : "Aucun cache à nettoyer.";
            LogAction("Nettoyage", "Tous les caches jeux", FormatSize(total));
            ShowToast?.Invoke("Caches Jeux", $"Tout nettoyé — {FormatSize(total)} libérés ✓");
            await ScanGameCachesAsync();
        }
        catch (Exception ex) { GameCacheStatus = $"Erreur: {ex.Message}"; }
        finally { IsGameCacheBusy = false; }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // TAB 3 — DÉMARRAGE
    // ══════════════════════════════════════════════════════════════════════════
    public ObservableCollection<StartupEntry> Startup { get; } = [];
    [ObservableProperty] private string _startupStatus = "Chargement…";

    private void RefreshStartup()
    {
        StartupStatus = "Chargement des programmes de démarrage…";
        Task.Run(() =>
        {
            var entries = _startupMgr.GetAll();
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                Startup.Clear();
                foreach (var e in entries) Startup.Add(e);
                StartupStatus = $"{entries.Count} entrées trouvées.";
            });
        });
    }

    [RelayCommand]
    private void ToggleStartupEntry(StartupEntry? entry)
    {
        if (entry == null) return;
        if (entry.Enabled) _startupMgr.Disable(entry);
        else               _startupMgr.Enable(entry);
        RefreshStartup();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // TAB 4 — RÉSEAU
    // ══════════════════════════════════════════════════════════════════════════
    [ObservableProperty] private string _netOptStatus  = "Prêt — cliquez sur un bouton pour optimiser.";
    [ObservableProperty] private string _pingResult    = "Cliquez sur 'Tester le ping' pour mesurer la latence.";
    [ObservableProperty] private bool   _isNetBusy     = false;

    [RelayCommand]
    private async Task PingTestAsync()
    {
        IsNetBusy   = true;
        PingResult  = "Ping en cours vers 8.8.8.8…";
        try
        {
            var ping = new System.Net.NetworkInformation.Ping();
            var results = new List<long>();
            for (int i = 0; i < 5; i++)
            {
                var reply = await ping.SendPingAsync("8.8.8.8", 2000);
                if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                    results.Add(reply.RoundtripTime);
            }
            if (results.Count > 0)
            {
                long avg  = (long)results.Average();
                long min  = results.Min();
                long max  = results.Max();
                PingResult = $"Min: {min}ms  ·  Avg: {avg}ms  ·  Max: {max}ms  ·  Jitter: {max - min}ms";
            }
            else PingResult = "Ping échoué — pas de connexion Internet.";
        }
        catch (Exception ex) { PingResult = $"Erreur: {ex.Message}"; }
        finally { IsNetBusy = false; }
    }

    [RelayCommand]
    private async Task OptimizeTcpAsync()
    {
        IsNetBusy    = true;
        NetOptStatus = "Optimisation TCP/IP en cours…";
        await Task.Run(() => _optimizer.OptimizeTcp());
        NetOptStatus = "TCP/IP optimisé — paramètres réseau améliorés pour réduire la latence ✓";
        LogAction("Réseau", "TCP/IP optimisé", "Latence réduite");
        ShowToast?.Invoke("Réseau", "TCP/IP optimisé ✓");
        IsNetBusy = false;
    }

    [RelayCommand]
    private async Task FlushDnsAsync()
    {
        IsNetBusy    = true;
        NetOptStatus = "Vidage DNS…";
        var result   = await Task.Run(() => { _optimizer.FlushDns(); return "DNS vidé et réenregistré ✓"; });
        NetOptStatus = result;
        ShowToast?.Invoke("Réseau", result);
        IsNetBusy = false;
    }

    // ── DNS Speed Test ───────────────────────────────────────────────────────
    public ObservableCollection<DnsResult> DnsResults { get; } = [];
    [ObservableProperty] private string _dnsTestStatus = "Testez les serveurs DNS pour trouver le plus rapide pour votre connexion.";
    [ObservableProperty] private bool   _isDnsBusy     = false;
    [ObservableProperty] private string _appliedDns    = "";

    [RelayCommand]
    private async Task TestAllDnsAsync()
    {
        if (IsDnsBusy) return;
        IsDnsBusy = true;
        DnsTestStatus = "Test en cours — 8 serveurs DNS…";
        DnsResults.Clear();
        try
        {
            var progress = new Progress<string>(msg => DnsTestStatus = msg);
            var results  = await _dnsTest.TestAllAsync(progress);
            foreach (var r in results) DnsResults.Add(r);
            var best = results.FirstOrDefault(r => r.Available);
            DnsTestStatus = best != null
                ? $"Le plus rapide : {best.Server.Name} ({best.PingMs} ms) — cliquez Appliquer pour l'utiliser."
                : "Aucun serveur DNS disponible — vérifiez votre connexion.";
        }
        catch (Exception ex) { DnsTestStatus = $"Erreur: {ex.Message}"; }
        finally { IsDnsBusy = false; }
    }

    [RelayCommand]
    private async Task ApplyDnsServerAsync(DnsResult? result)
    {
        if (result == null || !result.Available) return;
        IsDnsBusy = true;
        DnsTestStatus = $"Application de {result.Server.Name} ({result.Server.Primary})…";
        bool ok = await _dnsTest.ApplyDnsAsync(result);
        DnsTestStatus = ok
            ? $"DNS {result.Server.Name} appliqué — {result.Server.Primary} / {result.Server.Secondary} ✓"
            : $"Impossible d'appliquer {result.Server.Name} — lancez en tant qu'administrateur.";
        AppliedDns = ok ? result.Server.Name : "";
        if (ok) LogAction("Réseau", $"DNS {result.Server.Name}", $"{result.Server.Primary} appliqué");
        ShowToast?.Invoke("DNS", ok ? $"{result.Server.Name} appliqué ✓" : "Erreur — admin requis");
        IsDnsBusy = false;
    }

    [RelayCommand]
    private async Task ResetDnsToAutoAsync()
    {
        IsDnsBusy = true;
        DnsTestStatus = "Remise en automatique (DHCP)…";
        bool ok = await _dnsTest.ResetDnsAsync();
        DnsTestStatus = ok ? "DNS remis en automatique ✓" : "Erreur lors de la remise en auto.";
        AppliedDns = "";
        IsDnsBusy = false;
    }

    [RelayCommand]
    private async Task OptimizeDnsServersAsync()
    {
        IsNetBusy    = true;
        NetOptStatus = "Configuration DNS Cloudflare (1.1.1.1)…";
        try
        {
            var result = await Task.Run(() =>
            {
                // Set DNS to Cloudflare via netsh
                var adapters = GetNetAdapters();
                foreach (var a in adapters)
                {
                    RunNetsh($"interface ip set dns \"{a}\" static 1.1.1.1 primary");
                    RunNetsh($"interface ip add dns \"{a}\" 1.0.0.1 index=2");
                }
                return $"DNS optimisés sur {adapters.Count} adaptateur(s)";
            });
            NetOptStatus = result;
            ShowToast?.Invoke("DNS", result + " ✓");
        }
        catch (Exception ex) { NetOptStatus = $"Erreur: {ex.Message}"; }
        finally { IsNetBusy = false; }
    }

    private static List<string> GetNetAdapters()
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

    private static void RunNetsh(string args)
    {
        try
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("netsh", args)
            {
                UseShellExecute = false, CreateNoWindow = true
            });
            p?.WaitForExit();
        }
        catch { }
    }

    // ── Test de vitesse Internet ─────────────────────────────────────────────
    [ObservableProperty] private string _speedTestStatus     = "Testez votre débit Internet — téléchargement, upload et latence.";
    [ObservableProperty] private bool   _isSpeedTesting      = false;
    [ObservableProperty] private bool   _speedTestDone       = false;
    [ObservableProperty] private double _speedGaugeValue     = 0;
    [ObservableProperty] private string _speedLiveLabel      = "—";
    [ObservableProperty] private string _speedPhase          = "Mbps";
    [ObservableProperty] private string _speedDownLabel      = "—";
    [ObservableProperty] private string _speedUpLabel        = "—";
    [ObservableProperty] private string _speedPingLabel      = "—";
    [ObservableProperty] private string _speedJitterLabel    = "—";
    [ObservableProperty] private string _speedGrade          = "";
    [ObservableProperty] private string _speedAdvice         = "";
    [ObservableProperty] private string _speedConnectionType = "";

    public bool SpeedGaugeVisible => IsSpeedTesting || SpeedTestDone;
    partial void OnIsSpeedTestingChanged(bool value) => OnPropertyChanged(nameof(SpeedGaugeVisible));
    partial void OnSpeedTestDoneChanged(bool value)  => OnPropertyChanged(nameof(SpeedGaugeVisible));

    [RelayCommand]
    private async Task RunSpeedTestAsync()
    {
        if (IsSpeedTesting) return;
        IsSpeedTesting  = true;
        SpeedTestDone   = false;
        SpeedGaugeValue = 0;
        SpeedLiveLabel  = "0";
        SpeedPhase      = "Mbps";
        SpeedDownLabel  = SpeedUpLabel = SpeedPingLabel = SpeedJitterLabel = "—";
        SpeedGrade      = SpeedAdvice = SpeedConnectionType = "";

        try
        {
            var progress = new Progress<(string Phase, double LiveMbps)>(p =>
            {
                SpeedTestStatus = p.Phase;
                SpeedPhase      = p.Phase;
                if (p.LiveMbps > 0)
                {
                    SpeedLiveLabel  = $"{p.LiveMbps:F0}";
                    SpeedGaugeValue = Math.Min(Math.Log10(p.LiveMbps + 1) / Math.Log10(1001) * 100, 100);
                }
            });

            var r = await _speedTest.TestAsync(progress);

            if (r.Success)
            {
                SpeedDownLabel      = r.DownloadLabel;
                SpeedUpLabel        = r.UploadLabel;
                SpeedPingLabel      = r.PingLabel;
                SpeedJitterLabel    = r.JitterLabel;
                SpeedGrade          = r.Grade;
                SpeedAdvice         = r.GradeAdvice;
                SpeedConnectionType = r.ConnectionType;
                SpeedLiveLabel      = $"{r.DownloadMbps:F0}";
                SpeedGaugeValue     = Math.Min(Math.Log10(r.DownloadMbps + 1) / Math.Log10(1001) * 100, 100);
                SpeedPhase          = "Mbps";
                SpeedTestStatus     = $"Test terminé — {r.DownloadLabel} ↓   {r.UploadLabel} ↑   {r.PingLabel}";
                SpeedTestDone       = true;
                LogAction("Réseau", "Speed Test", $"{r.DownloadLabel} ↓ · {r.UploadLabel} ↑ · {r.PingLabel}");
                ShowToast?.Invoke("Speed Test", $"{r.DownloadLabel} download · {r.PingLabel} latence");
            }
            else
            {
                SpeedTestStatus = "Test échoué — vérifiez votre connexion Internet.";
                SpeedLiveLabel  = "—";
                SpeedPhase      = "Mbps";
            }
        }
        catch (Exception ex)
        {
            SpeedTestStatus = $"Erreur: {ex.Message}";
            SpeedLiveLabel  = "—";
            SpeedPhase      = "Mbps";
        }
        finally { IsSpeedTesting = false; }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // TAB 5 — RÉPARATION WINDOWS
    // ══════════════════════════════════════════════════════════════════════════
    [ObservableProperty] private string _repairLog    = "";
    [ObservableProperty] private bool   _isRepairing  = false;

    [RelayCommand]
    private async Task RunSfcAsync()
    {
        IsRepairing = true;
        RepairLog   = "Lancement de SFC /scannow (peut prendre plusieurs minutes)…\n";
        try
        {
            var progress = new Progress<string>(msg => RepairLog += msg + "\n");
            var result   = await _repair.RunSfcAsync(progress);
            RepairLog   += "\n" + result;
            ShowToast?.Invoke("SFC", "Vérification système terminée ✓");
        }
        catch (Exception ex) { RepairLog += $"\nErreur: {ex.Message}"; }
        finally { IsRepairing = false; }
    }

    [RelayCommand]
    private async Task RunDismAsync()
    {
        IsRepairing = true;
        RepairLog   = "Lancement de DISM RestoreHealth (peut prendre 10-20 min)…\n";
        try
        {
            var progress = new Progress<string>(msg => RepairLog += msg + "\n");
            var result   = await _repair.RunDismRestoreHealthAsync(progress);
            RepairLog   += "\n" + result;
            ShowToast?.Invoke("DISM", "Réparation Windows terminée ✓");
        }
        catch (Exception ex) { RepairLog += $"\nErreur: {ex.Message}"; }
        finally { IsRepairing = false; }
    }

    [RelayCommand]
    private async Task ResetNetworkStackAsync()
    {
        IsRepairing = true;
        RepairLog   = "Réinitialisation complète du réseau…\n";
        try
        {
            var progress = new Progress<string>(msg => RepairLog += msg + "\n");
            var result   = await _repair.ResetNetworkAsync(progress);
            RepairLog   += "\n" + result;
            ShowToast?.Invoke("Réseau", "Stack réseau réinitialisée ✓");
        }
        catch (Exception ex) { RepairLog += $"\nErreur: {ex.Message}"; }
        finally { IsRepairing = false; }
    }

    [RelayCommand]
    private async Task RepairWindowsUpdateAsync()
    {
        IsRepairing = true;
        RepairLog   = "Réparation de Windows Update…\n";
        try
        {
            var progress = new Progress<string>(msg => RepairLog += msg + "\n");
            var result   = await _repair.RepairWindowsUpdateAsync(progress);
            RepairLog   += "\n" + result;
            ShowToast?.Invoke("Windows Update", "Réparation terminée ✓");
        }
        catch (Exception ex) { RepairLog += $"\nErreur: {ex.Message}"; }
        finally { IsRepairing = false; }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // TAB 6 — STOCKAGE
    // ══════════════════════════════════════════════════════════════════════════
    public ObservableCollection<DriveHealth>     Drives     { get; } = [];
    public ObservableCollection<PartitionInfo>   Partitions { get; } = [];
    public ObservableCollection<BigFileEntry>    BigFiles   { get; } = [];
    public ObservableCollection<FolderSizeEntry> FolderSizes{ get; } = [];

    [ObservableProperty] private string _storageStatus    = "";
    [ObservableProperty] private bool   _isStorageBusy    = false;
    [ObservableProperty] private string _benchmarkResult  = "Mesure la vraie vitesse lecture/écriture de votre disque.";
    [ObservableProperty] private bool   _isBenchmarking   = false;
    [ObservableProperty] private bool   _isScanningFiles  = false;
    [ObservableProperty] private string _bigFilesStatus   = "Scanne Downloads, Bureau, Vidéos, Documents — fichiers > 50 Mo.";
    [ObservableProperty] private string _folderSizeStatus = "Calcule la taille des dossiers principaux de C:\\";

    private async Task LoadStorageAsync()
    {
        IsStorageBusy  = true;
        StorageStatus  = "Analyse des disques…";
        try
        {
            var drives     = await Task.Run(() => _storage.GetAllDrives());
            var partitions = await Task.Run(() => _storage.GetPartitions());
            var smart      = await Task.Run(() => _storage.GetSmartStatus());

            Drives.Clear();
            foreach (var d in drives) Drives.Add(d);
            Partitions.Clear();
            foreach (var p in partitions) Partitions.Add(p);

            StorageStatus = $"{drives.Count} disque(s)  ·  {partitions.Count} partition(s)  ·  SMART: {smart}";
        }
        catch (Exception ex) { StorageStatus = $"Erreur: {ex.Message}"; }
        finally { IsStorageBusy = false; }
    }

    [RelayCommand]
    private async Task RunTrimAsync()
    {
        IsStorageBusy = true;
        StorageStatus = "Exécution du TRIM SSD…";
        bool ok = await _storage.RunTrimAsync();
        StorageStatus = ok ? "TRIM exécuté avec succès ✓" : "TRIM: erreur ou non nécessaire";
        if (ok) LogAction("Stockage", "TRIM SSD", "Succès");
        ShowToast?.Invoke("Stockage", ok ? "TRIM SSD exécuté ✓" : "TRIM indisponible");
        IsStorageBusy = false;
    }

    [RelayCommand]
    private async Task RunBenchmarkAsync()
    {
        IsBenchmarking  = true;
        BenchmarkResult = "Benchmark en cours…";
        var progress = new Progress<string>(msg => BenchmarkResult = msg);
        try
        {
            var r = await _storage.BenchmarkAsync(progress);
            BenchmarkResult = r.Label;
            ShowToast?.Invoke("Benchmark", r.Label);
        }
        catch (Exception ex) { BenchmarkResult = $"Erreur: {ex.Message}"; }
        finally { IsBenchmarking = false; }
    }

    [RelayCommand]
    private async Task ScanLargeFilesAsync()
    {
        IsScanningFiles = true;
        BigFilesStatus  = "Scan en cours…";
        BigFiles.Clear();
        var progress = new Progress<string>(msg => BigFilesStatus = msg);
        try
        {
            var files = await _storage.FindLargestFilesAsync(progress);
            foreach (var f in files) BigFiles.Add(f);
            BigFilesStatus = files.Count > 0
                ? $"{files.Count} gros fichier(s) trouvé(s)."
                : "Aucun fichier > 50 Mo dans les dossiers personnels.";
        }
        catch (Exception ex) { BigFilesStatus = $"Erreur: {ex.Message}"; }
        finally { IsScanningFiles = false; }
    }

    [RelayCommand]
    private void OpenBigFile(BigFileEntry? entry)
    {
        if (entry == null) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{entry.Path}\"") { UseShellExecute = true }); } catch { }
    }

    [RelayCommand]
    private async Task ScanFolderSizesAsync()
    {
        FolderSizeStatus = "Calcul en cours…";
        FolderSizes.Clear();
        var progress = new Progress<string>(msg => FolderSizeStatus = msg);
        try
        {
            var folders = await _storage.GetTopFolderSizesAsync(progress);
            long maxSz = folders.Count > 0 ? folders.Max(f => f.SizeBytes) : 1;
            foreach (var f in folders)
                FolderSizes.Add(f with { Pct = maxSz > 0 ? (int)(f.SizeBytes * 100 / maxSz) : 0 });
            FolderSizeStatus = folders.Count > 0
                ? $"{folders.Count} dossier(s) calculé(s)."
                : "Aucun dossier trouvé sur C:\\";
        }
        catch (Exception ex) { FolderSizeStatus = $"Erreur: {ex.Message}"; }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // TAB 7 — PILOTES
    // ══════════════════════════════════════════════════════════════════════════
    public ObservableCollection<DriverEntry> Drivers { get; } = [];
    [ObservableProperty] private string _driverStatus  = "";
    [ObservableProperty] private bool   _isDriverBusy  = false;
    [ObservableProperty] private bool   _showAllDrivers = false;

    private async Task LoadDriversAsync()
    {
        IsDriverBusy = true;
        DriverStatus = "Chargement des pilotes…";
        try
        {
            var drv = await Task.Run(() => ShowAllDrivers ? _drivers.GetDrivers() : _drivers.GetPriorityDrivers());
            Drivers.Clear();
            foreach (var d in drv) Drivers.Add(d);
            int problems = drv.Count(d => d.NeedsAttention);
            DriverStatus = $"{drv.Count} pilotes. {(problems > 0 ? $"{problems} problème(s) détecté(s) !" : "Tout est OK.")}";
        }
        catch (Exception ex) { DriverStatus = $"Erreur: {ex.Message}"; }
        finally { IsDriverBusy = false; }
    }

    [RelayCommand]
    private async Task ToggleShowAllDrivers()
    {
        ShowAllDrivers = !ShowAllDrivers;
        await LoadDriversAsync();
    }

    [RelayCommand]
    private void OpenDeviceManager()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = "devmgmt.msc",
                UseShellExecute = true
            });
        }
        catch { }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // TAB 8 — PROBLÈMES DÉTECTÉS
    // ══════════════════════════════════════════════════════════════════════════
    public ObservableCollection<DiagnosticIssue> Issues { get; } = [];
    [ObservableProperty] private string _diagStatus   = "Cliquez sur 'Analyser' pour détecter les problèmes.";
    [ObservableProperty] private bool   _isDiagBusy   = false;
    [ObservableProperty] private int    _criticalCount = 0;
    [ObservableProperty] private int    _warningCount  = 0;

    private async Task RunDiagnosticsAsync()
    {
        if (IsDiagBusy) return;
        IsDiagBusy  = true;
        DiagStatus  = "Analyse en cours…";
        Issues.Clear();
        try
        {
            var found = await Task.Run(() => _diagnostics.RunFullDiagnostics());
            foreach (var i in found) Issues.Add(i);
            CriticalCount = found.Count(i => i.Severity == IssueSeverity.Critical);
            WarningCount  = found.Count(i => i.Severity == IssueSeverity.Warning);
            DiagStatus = CriticalCount > 0
                ? $"⚠ {CriticalCount} problème(s) critique(s), {WarningCount} avertissement(s)."
                : WarningCount > 0
                    ? $"{WarningCount} avertissement(s) détecté(s)."
                    : "Aucun problème détecté. Votre PC est en bonne santé.";
            UpdatePcScore(CriticalCount, WarningCount);
        SaveWeeklyScore(PcScore);
        LoadWeeklyScore();
        }
        catch (Exception ex) { DiagStatus = $"Erreur: {ex.Message}"; }
        finally { IsDiagBusy = false; }
    }

    [RelayCommand]
    private async Task RefreshDiagnosticsAsync() => await RunDiagnosticsAsync();

    [RelayCommand]
    private async Task FixIssueAsync(DiagnosticIssue? issue)
    {
        if (issue?.FixKey == null) return;
        switch (issue.FixKey)
        {
            case "high_perf":
                IsDiagBusy = true;
                await Task.Run(() => _optimizer.SetHighPerfPlan(true));
                TwHighPerfPlan = true;
                ShowToast?.Invoke("Problèmes réglés", "Plan Haute Performance activé ✓");
                await RunDiagnosticsAsync();
                break;
            case "game_dvr":
                IsDiagBusy = true;
                await Task.Run(() => _optimizer.SetGameDvr(true));
                TwGameDvr = true;
                ShowToast?.Invoke("Problèmes réglés", "Xbox Game DVR désactivé ✓");
                await RunDiagnosticsAsync();
                break;
            case "net_throttle":
                IsDiagBusy = true;
                await Task.Run(() => _optimizer.SetNetworkThrottling(true));
                TwNetThrottle = true;
                ShowToast?.Invoke("Problèmes réglés", "Network Throttling désactivé ✓");
                await RunDiagnosticsAsync();
                break;
            case "windows_update":
            case "reboot":
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:windowsupdate") { UseShellExecute = true });
                break;
            case "clean":
                SelectedTab = 1;
                break;
            case "startup":
                SelectedTab = 2;
                break;
            case "windows_security":
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:windowsdefender") { UseShellExecute = true });
                break;
            case "drivers":
                SelectedTab = 5;
                break;
            case "task_manager":
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
                break;
            case "event_viewer":
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("eventvwr.msc") { UseShellExecute = true });
                break;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // TAB 9 — PROFILS RAPIDES
    // ══════════════════════════════════════════════════════════════════════════
    [ObservableProperty] private string _profileStatus  = "Aucun profil appliqué — choisissez un profil ci-dessous.";
    [ObservableProperty] private string _activeProfile  = "";
    [ObservableProperty] private string _pcScoreAdvice  = "";

    public bool IsGamingProfileActive     => ActiveProfile == "gaming";
    public bool IsBureauProfileActive     => ActiveProfile == "bureau";
    public bool IsMultitacheProfileActive => ActiveProfile == "multitache";
    public bool IsPrivacyProfileActive    => ActiveProfile == "privacy";

    partial void OnActiveProfileChanged(string value)
    {
        OnPropertyChanged(nameof(IsGamingProfileActive));
        OnPropertyChanged(nameof(IsBureauProfileActive));
        OnPropertyChanged(nameof(IsMultitacheProfileActive));
        OnPropertyChanged(nameof(IsPrivacyProfileActive));
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(AppRegKey);
            k.SetValue("ActiveProfile",  value);
            k.SetValue("ProfileStatus",  ProfileStatus);
        }
        catch { }
    }

    private void RestorePersistedProfile()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(AppRegKey);
            if (k == null) return;
            var profile = k.GetValue("ActiveProfile")?.ToString() ?? "";
            var status  = k.GetValue("ProfileStatus")?.ToString() ?? "";
            if (!string.IsNullOrEmpty(profile))
            {
                ActiveProfile = profile;
                if (!string.IsNullOrEmpty(status)) ProfileStatus = status;
            }
        }
        catch { }
    }

    [RelayCommand]
    private async Task ApplyGamingProfileAsync()
    {
        ProfileStatus = "Application du profil Gaming…";
        await Task.Run(() => { _optimizer.ApplyGamingProfile(); });
        LoadTweakStates();
        ActiveProfile = "gaming";
        ProfileStatus = "✓ Profil Gaming actif — Game DVR off, GPU priority, haute performance, réseau optimisé.";
        LogAction("Optimisation", "Profil Gaming", "8 tweaks appliqués");
        ShowToast?.Invoke("Profil Gaming", "8 optimisations appliquées ✓");
        ScheduleDiagRefresh();
    }

    [RelayCommand]
    private async Task ApplyBureautiqueProfileAsync()
    {
        ProfileStatus = "Application du profil Bureautique…";
        await Task.Run(() => { _optimizer.ApplyBureautiqueProfile(); });
        LoadTweakStates();
        ActiveProfile = "bureau";
        ProfileStatus = "✓ Profil Bureautique actif — télémétrie off, effets visuels réduits, plan équilibré.";
        ShowToast?.Invoke("Profil Bureautique", "Optimisations bureau appliquées ✓");
        ScheduleDiagRefresh();
    }

    [RelayCommand]
    private async Task ApplyMultitacheProfileAsync()
    {
        ProfileStatus = "Application du profil Multitâche…";
        await Task.Run(() => { _optimizer.ApplyMultitacheProfile(); });
        LoadTweakStates();
        ActiveProfile = "multitache";
        ProfileStatus = "✓ Profil Multitâche actif — réactivité max, réseau sans limite, haute performance.";
        ShowToast?.Invoke("Profil Multitâche", "Optimisations multitâche appliquées ✓");
        ScheduleDiagRefresh();
    }

    [RelayCommand]
    private async Task ApplyPrivacyProfileAsync()
    {
        ProfileStatus = "Application du profil Confidentialité…";
        await Task.Run(() => { _optimizer.ApplyPrivacyProfile(); });
        LoadTweakStates();
        ActiveProfile = "privacy";
        ProfileStatus = "✓ Profil Confidentialité actif — télémétrie off, ID pub off, localisation off, Cortana off.";
        ShowToast?.Invoke("Profil Confidentialité", "Paramètres vie privée appliqués ✓");
        ScheduleDiagRefresh();
    }

    [RelayCommand]
    private async Task ApplyWindowsDefaultProfileAsync()
    {
        ProfileStatus = "Remise aux paramètres Windows par défaut…";
        await Task.Run(() =>
        {
            _optimizer.SetGameDvr(false);
            _optimizer.SetFullscreenOptim(false);
            _optimizer.SetGpuPriority(false);
            _optimizer.SetSystemResponsiveness(false);
            _optimizer.SetWin32Priority(false);
            _optimizer.SetMousePrecision(false);
            _optimizer.SetHighPerfPlan(false);
            _optimizer.SetNetworkThrottling(false);
            _optimizer.SetTelemetry(false);
            _optimizer.SetAdvertisingId(false);
            _optimizer.SetLocation(false);
            _optimizer.SetCortana(false);
            _optimizer.SetVisualEffects(false);
        });
        LoadTweakStates();
        ActiveProfile = "";
        ProfileStatus = "✓ Paramètres Windows restaurés — tous les tweaks désactivés. Votre PC est dans l'état Windows d'origine.";
        ShowToast?.Invoke("Défaut Windows", "Tous les tweaks ont été désactivés ✓");
        ScheduleDiagRefresh();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // TAB 10 — GAMING
    // ══════════════════════════════════════════════════════════════════════════
    [ObservableProperty] private bool _twGameDvr         = false;
    [ObservableProperty] private bool _twFullscreenOptim = false;
    [ObservableProperty] private bool _twGpuPriority     = false;
    [ObservableProperty] private bool _twSystemResp      = false;
    [ObservableProperty] private bool _twWin32Priority   = false;
    [ObservableProperty] private bool _twMousePrecision  = false;
    [ObservableProperty] private bool _twHighPerfPlan    = false;
    [ObservableProperty] private bool _twNetThrottle     = false;

    public ObservableCollection<DetectedGame> Games { get; } = [];

    private void RefreshGames()
    {
        Task.Run(() =>
        {
            var games = _gameDetect.DetectGames();
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                Games.Clear();
                foreach (var g in games) Games.Add(g);
            });
        });
    }

    [RelayCommand] private void ToggleGameDvr()         { _optimizer.SetGameDvr(!TwGameDvr);                  TwGameDvr         = !TwGameDvr;         Toast("Game DVR");                ScheduleDiagRefresh(); }
    [RelayCommand] private void ToggleFullscreenOptim() { _optimizer.SetFullscreenOptim(!TwFullscreenOptim);  TwFullscreenOptim = !TwFullscreenOptim; Toast("Optimisations plein écran"); ScheduleDiagRefresh(); }
    [RelayCommand] private void ToggleGpuPriority()     { _optimizer.SetGpuPriority(!TwGpuPriority);          TwGpuPriority     = !TwGpuPriority;     Toast("Priorité GPU/CPU");         ScheduleDiagRefresh(); }
    [RelayCommand] private void ToggleSystemResp()      { _optimizer.SetSystemResponsiveness(!TwSystemResp);  TwSystemResp      = !TwSystemResp;      Toast("Réactivité système");        ScheduleDiagRefresh(); }
    [RelayCommand] private void ToggleWin32Priority()   { _optimizer.SetWin32Priority(!TwWin32Priority);      TwWin32Priority   = !TwWin32Priority;   Toast("Priorité premier plan");     ScheduleDiagRefresh(); }
    [RelayCommand] private void ToggleMousePrecision()  { _optimizer.SetMousePrecision(!TwMousePrecision);    TwMousePrecision  = !TwMousePrecision;  Toast("Accélération souris");       ScheduleDiagRefresh(); }
    [RelayCommand] private void ToggleHighPerfPlan()    { _optimizer.SetHighPerfPlan(!TwHighPerfPlan);        TwHighPerfPlan    = !TwHighPerfPlan;    Toast("Plan hautes performances");  ScheduleDiagRefresh(); }
    [RelayCommand] private void ToggleNetThrottle()     { _optimizer.SetNetworkThrottling(!TwNetThrottle);    TwNetThrottle     = !TwNetThrottle;     Toast("Network Throttling");        ScheduleDiagRefresh(); }

    // ── Game Booster ─────────────────────────────────────────────────────────
    [ObservableProperty] private bool   _isBoostActive      = false;
    [ObservableProperty] private string _boostStatus        = "Inactif — active le boost pour détecter automatiquement tes jeux.";
    [ObservableProperty] private string _boostedGameName    = "";
    [ObservableProperty] private string _ramBoostStatus     = "";
    [ObservableProperty] private string _overlayStatus      = "";
    [ObservableProperty] private bool   _isRamBusy          = false;
    public ObservableCollection<string> RunningOverlays { get; } = [];

    [RelayCommand]
    private void ToggleGameBoost()
    {
        if (!IsBoostActive)
        {
            // Sauvegarder le plan d'alimentation actuel et passer en Ultimate Performance
            _previousPowerPlanGuid = _optimizer.GetActivePowerPlanGuid();
            _optimizer.ApplyGamingProfile();
            _optimizer.SetUltimatePerfPlan(true);
            LoadTweakStates();

            // Démarrer surveillance
            _booster.GameDetected    += OnGameDetected;
            _booster.GameEnded       += OnGameEnded;
            _booster.SessionCompleted += OnSessionCompleted;
            _booster.StartWatching();

            // Boost immédiat si un jeu tourne déjà
            var results = _booster.BoostAllRunningGames();
            var found   = results.Where(r => r.Success).ToList();

            IsBoostActive = true;
            BoostStatus   = found.Count > 0
                ? $"ACTIF — {found.Count} jeu(x) boostés: {string.Join(", ", found.Select(r => r.GameName))}"
                : "ACTIF — En attente d'un jeu…";
            ShowToast?.Invoke("Game Booster", "Ultimate Performance activé — surveillance en cours ✓");
        }
        else
        {
            _booster.GameDetected    -= OnGameDetected;
            _booster.GameEnded       -= OnGameEnded;
            _booster.SessionCompleted -= OnSessionCompleted;
            _booster.StopWatching();

            // Restaurer le plan d'alimentation précédent
            if (!string.IsNullOrEmpty(_previousPowerPlanGuid))
                _optimizer.SetPowerPlan(_previousPowerPlanGuid);

            IsBoostActive   = false;
            BoostedGameName = "";
            BoostStatus     = "Inactif — active le boost pour détecter automatiquement tes jeux.";
            ShowToast?.Invoke("Game Booster", "Mode boost désactivé — plan d'alimentation restauré");
        }
    }

    private void OnGameDetected(string name)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            BoostedGameName = name;
            BoostStatus     = $"ACTIF — Jeu détecté et boosté: {name}";
            ShowToast?.Invoke("Game Booster", $"{name} détecté — priorité élevée appliquée ✓");
        });
    }

    private void OnGameEnded(string name)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            BoostedGameName = "";
            BoostStatus     = "ACTIF — Jeu fermé. En attente du prochain…";
        });
    }

    private void OnSessionCompleted(GameSession session)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            GameSessions.Insert(0, session);
            while (GameSessions.Count > 20) GameSessions.RemoveAt(GameSessions.Count - 1);
        });
    }

    public ObservableCollection<GameSession> GameSessions { get; } = [];

    // ── Mode Turbo One-Click ──────────────────────────────────────────────────
    [ObservableProperty] private bool   _isTurboActive = false;
    [ObservableProperty] private string _turboSummary  = "Désactivé — lance le Mode Turbo pour un boost maximal en un clic.";

    [RelayCommand]
    private async Task ToggleTurboAsync()
    {
        if (!IsTurboActive)
        {
            TurboSummary = "Activation…";
            await Task.Run(() =>
            {
                _optimizer.ApplyGamingProfile();
                _optimizer.SetUltimatePerfPlan(true);
                _serviceOptimizer.OptimizeForGaming();
            });
            LoadTweakStates();
            if (!IsBoostActive) ToggleGameBoost();
            IsTurboActive = true;
            TurboSummary  = "TURBO ACTIF — Plan Ultimate, services optimisés, Game Booster ON, profil Gaming.";
            ShowToast?.Invoke("Mode Turbo", "Performances maximales activées ✓");
        }
        else
        {
            TurboSummary = "Désactivation…";
            await Task.Run(() => _serviceOptimizer.RestoreServices());
            if (IsBoostActive) ToggleGameBoost();
            IsTurboActive = false;
            TurboSummary  = "Désactivé — services et plan d'alimentation restaurés.";
            ShowToast?.Invoke("Mode Turbo", "Mode Turbo désactivé — paramètres restaurés");
        }
    }

    [RelayCommand]
    private async Task OptimizeFiveMAsync()
    {
        BoostStatus = "Optimisation FiveM en cours…";
        await Task.Run(() =>
        {
            _optimizer.SetGameDvr(true);
            _optimizer.SetFullscreenOptim(true);
            _optimizer.SetGpuPriority(true);
            _optimizer.SetNetworkThrottling(true);
            _optimizer.OptimizeTcp();
        });
        LoadTweakStates();
        BoostStatus = "✓ FiveM optimisé — Game DVR off, GPU priority, TCP optimisé.";
        ShowToast?.Invoke("FiveM", "Optimisations FiveM appliquées ✓");
    }

    [RelayCommand]
    private async Task OptimizeRamAsync()
    {
        IsRamBusy    = true;
        RamBoostStatus = "Libération de la mémoire en cours…";
        var (freed, msg) = await Task.Run(() => _ramOptimizer.OptimizeRam());
        RamBoostStatus   = msg;
        ShowToast?.Invoke("RAM", msg);
        IsRamBusy = false;
    }

    [RelayCommand]
    private void ScanOverlays()
    {
        RunningOverlays.Clear();
        var overlays = _booster.GetRunningOverlays();
        foreach (var o in overlays) RunningOverlays.Add(o);
        OverlayStatus = overlays.Count > 0
            ? $"{overlays.Count} overlay(s) actif(s) détecté(s)."
            : "Aucun overlay intrusif détecté.";
    }

    [RelayCommand]
    private void KillOverlays()
    {
        var (killed, names) = _booster.KillOverlays();
        ScanOverlays();
        OverlayStatus = killed > 0
            ? $"{killed} overlay(s) fermé(s): {string.Join(", ", names)}"
            : "Aucun overlay à fermer.";
        if (killed > 0) ShowToast?.Invoke("Overlays", $"{killed} overlay(s) supprimés ✓");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // TAB 11 — CONFIDENTIALITÉ
    // ══════════════════════════════════════════════════════════════════════════
    [ObservableProperty] private bool _twTelemetry     = false;
    [ObservableProperty] private bool _twAdvertisingId = false;
    [ObservableProperty] private bool _twLocation      = false;
    [ObservableProperty] private bool _twCortana       = false;
    [ObservableProperty] private bool _twVisualEffects = false;

    [RelayCommand] private void ToggleTelemetry()     { _optimizer.SetTelemetry(!TwTelemetry);              TwTelemetry     = !TwTelemetry;     Toast("Télémétrie");    }
    [RelayCommand] private void ToggleAdvertisingId() { _optimizer.SetAdvertisingId(!TwAdvertisingId);      TwAdvertisingId = !TwAdvertisingId; Toast("ID publicitaire"); }
    [RelayCommand] private void ToggleLocation()      { _optimizer.SetLocation(!TwLocation);                TwLocation      = !TwLocation;      Toast("Localisation");  }
    [RelayCommand] private void ToggleCortana()       { _optimizer.SetCortana(!TwCortana);                  TwCortana       = !TwCortana;       Toast("Cortana");       }
    [RelayCommand] private void ToggleVisualEffects() { _optimizer.SetVisualEffects(!TwVisualEffects);      TwVisualEffects = !TwVisualEffects; Toast("Effets visuels"); }

    // ══════════════════════════════════════════════════════════════════════════
    // TAB 12 — SÉCURITÉ & RESTAURATION
    // ══════════════════════════════════════════════════════════════════════════
    public ObservableCollection<RestorePointEntry> RestorePoints { get; } = [];
    [ObservableProperty] private string _securityStatus   = "";
    [ObservableProperty] private bool   _isSecurityBusy   = false;

    private async Task LoadRestorePointsAsync()
    {
        IsSecurityBusy  = true;
        SecurityStatus  = "Chargement des points de restauration…";
        try
        {
            var points = await Task.Run(() => _restore.GetRestorePoints());
            RestorePoints.Clear();
            foreach (var p in points) RestorePoints.Add(p);
            SecurityStatus = $"{points.Count} point(s) de restauration disponible(s).";
        }
        catch (Exception ex) { SecurityStatus = $"Erreur: {ex.Message}"; }
        finally { IsSecurityBusy = false; }
    }

    [RelayCommand]
    private async Task DeleteRestorePointAsync(RestorePointEntry? entry)
    {
        if (entry == null) return;
        IsSecurityBusy = true;
        SecurityStatus = $"Suppression du point #{entry.SequenceNumber}…";
        bool ok = await _restore.DeleteRestorePointAsync(entry.SequenceNumber);

        if (ok)
        {
            // Retrait immédiat de l'UI — Windows a un délai VSS avant de mettre à jour WMI
            RestorePoints.Remove(entry);
            SecurityStatus = $"Point #{entry.SequenceNumber} supprimé ✓ — {RestorePoints.Count} point(s) restant(s).";
            ShowToast?.Invoke("Sécurité", $"Point #{entry.SequenceNumber} supprimé ✓");
            // Délai VSS : Windows met ~3s à nettoyer la shadow copy supprimée
            await Task.Delay(3000);
            await LoadRestorePointsAsync();
        }
        else
        {
            SecurityStatus = $"Impossible de supprimer le point #{entry.SequenceNumber} — droits admin requis.";
        }

        IsSecurityBusy = false;
    }

    [RelayCommand]
    private async Task CreateRestorePointAsync()
    {
        IsSecurityBusy = true;
        SecurityStatus = "Vérification de la protection système…";
        try
        {
            var result = await _restore.CreateRestorePointAsync("EKIPPP-OPTIMIZER — sauvegarde avant optimisation");
            SecurityStatus = result.Message;
            if (result.Success)
            {
                ShowToast?.Invoke("Sécurité", "Point de restauration créé ✓");
                // Windows met ~2s à enregistrer le point dans VSS avant qu'il soit visible
                await Task.Delay(2500);
                await LoadRestorePointsAsync();
            }
        }
        catch (Exception ex) { SecurityStatus = $"Erreur: {ex.Message}"; }
        finally { IsSecurityBusy = false; }
    }

    [RelayCommand]
    private void LaunchSystemRestore()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("rstrui.exe")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex) { SecurityStatus = $"Impossible d'ouvrir la restauration: {ex.Message}"; }
    }

    // ── BSOD Analyzer ────────────────────────────────────────────────────────
    public ObservableCollection<BsodEntry> BsodEvents { get; } = [];
    [ObservableProperty] private string _bsodStatus = "Cliquez sur 'Analyser' pour scanner les crashs.";

    [RelayCommand]
    private void RefreshBsod()
    {
        BsodStatus = "Analyse des crashs système…";
        Task.Run(() =>
        {
            var entries = _bsodAnalyzer.GetRecentCrashes(15);
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                BsodEvents.Clear();
                foreach (var e in entries) BsodEvents.Add(e);
                BsodStatus = entries.Count > 0
                    ? $"{entries.Count} crash(s) ou arrêt(s) inattendu(s) trouvé(s)."
                    : "Aucun BSOD récent détecté. Excellent !";
            });
        });
    }

    // ══════════════════════════════════════════════════════════════════════════
    // TAB 13 — AUTOMATISATION
    // ══════════════════════════════════════════════════════════════════════════
    public ObservableCollection<string> ScheduledTasks { get; } = [];
    [ObservableProperty] private string _automationStatus     = "";
    [ObservableProperty] private bool   _isAutoBusy           = false;
    [ObservableProperty] private bool   _isCleanScheduled     = false;
    [ObservableProperty] private bool   _isAnalysisScheduled  = false;
    [ObservableProperty] private bool   _isAutoStartEnabled   = false;

    [RelayCommand]
    private void ToggleAutoStart()
    {
        try
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
            if (IsAutoStartEnabled)
            {
                k.DeleteValue("EKIPPP-OPTIMIZER", throwOnMissingValue: false);
                IsAutoStartEnabled = false;
                AutomationStatus   = "Démarrage automatique désactivé.";
                ShowToast?.Invoke("Démarrage auto", "EKIPPP-OPTIMIZER ne se lancera plus au démarrage");
            }
            else
            {
                k.SetValue("EKIPPP-OPTIMIZER", $"\"{exePath}\"");
                IsAutoStartEnabled = true;
                AutomationStatus   = "EKIPPP-OPTIMIZER se lancera automatiquement au démarrage Windows ✓";
                ShowToast?.Invoke("Démarrage auto", "Démarrage automatique activé ✓");
            }
        }
        catch { }
    }

    private void LoadAutoStartState()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
            IsAutoStartEnabled = k?.GetValue("EKIPPP-OPTIMIZER") != null;
        }
        catch { }
    }

    [RelayCommand]
    private async Task ToggleDailyCleanupAsync()
    {
        IsAutoBusy = true;
        if (_isCleanScheduled)
        {
            AutomationStatus = "Suppression du nettoyage quotidien…";
            bool ok = await _scheduler.DeleteTaskAsync("DailyCleanup");
            AutomationStatus = ok ? "Nettoyage quotidien annulé ✓" : "Erreur lors de la suppression.";
            ShowToast?.Invoke("Automatisation", ok ? "Nettoyage quotidien désactivé" : "Erreur");
        }
        else
        {
            AutomationStatus = "Planification du nettoyage quotidien…";
            bool ok = await _scheduler.CreateDailyCleanupAsync();
            AutomationStatus = ok ? "Nettoyage planifié chaque jour à 02h00 ✓" : "Erreur lors de la création.";
            ShowToast?.Invoke("Automatisation", ok ? "Nettoyage quotidien planifié ✓" : "Erreur");
        }
        await RefreshScheduledTasksAsync();
        IsAutoBusy = false;
    }

    [RelayCommand]
    private async Task ToggleWeeklyAnalysisAsync()
    {
        IsAutoBusy = true;
        if (_isAnalysisScheduled)
        {
            AutomationStatus = "Suppression de l'analyse hebdomadaire…";
            bool ok = await _scheduler.DeleteTaskAsync("WeeklyAnalysis");
            AutomationStatus = ok ? "Analyse hebdomadaire annulée ✓" : "Erreur lors de la suppression.";
            ShowToast?.Invoke("Automatisation", ok ? "Analyse hebdomadaire désactivée" : "Erreur");
        }
        else
        {
            AutomationStatus = "Planification de l'analyse hebdomadaire…";
            bool ok = await _scheduler.CreateWeeklyAnalysisAsync();
            AutomationStatus = ok ? "Analyse planifiée chaque dimanche à 03h00 ✓" : "Erreur lors de la création.";
            ShowToast?.Invoke("Automatisation", ok ? "Analyse hebdomadaire planifiée ✓" : "Erreur");
        }
        await RefreshScheduledTasksAsync();
        IsAutoBusy = false;
    }

    internal async Task RefreshScheduledTasksAsync()
    {
        IsCleanScheduled    = await _scheduler.IsTaskRegisteredAsync("DailyCleanup");
        IsAnalysisScheduled = await _scheduler.IsTaskRegisteredAsync("WeeklyAnalysis");
        var tasks = await _scheduler.GetRegisteredTasksAsync();
        ScheduledTasks.Clear();
        foreach (var t in tasks) ScheduledTasks.Add(t);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // NAVIGATEURS — nettoyage dédié (cache, cookies, historique)
    // ══════════════════════════════════════════════════════════════════════════
    public ObservableCollection<BrowserProfile> Browsers { get; } = [];
    [ObservableProperty] private string _browserStatus  = "Scannez pour voir vos navigateurs et l'espace récupérable.";
    [ObservableProperty] private bool   _isBrowserBusy  = false;

    [RelayCommand]
    private async Task ScanBrowsersAsync()
    {
        IsBrowserBusy = true;
        BrowserStatus = "Détection des navigateurs…";
        Browsers.Clear();
        try
        {
            // Détection instantanée (< 10 ms) — aucun calcul de taille
            var profiles = await Task.Run(() => _browserCleaner.ScanAll());

            if (profiles.Count == 0)
            {
                BrowserStatus = "Aucun navigateur détecté.";
                return;
            }

            foreach (var p in profiles) Browsers.Add(p);
            BrowserStatus = $"{profiles.Count} navigateur(s) détecté(s) — calcul des tailles en cours…";

            // Calcul des tailles en arrière-plan, sans bloquer l'UI
            _ = Task.Run(() =>
            {
                long total = 0;
                for (int i = 0; i < profiles.Count; i++)
                {
                    var (cache, cookies, history) = _browserCleaner.MeasureSizes(profiles[i]);
                    total += cache + cookies + history;
                    var updated = profiles[i] with { CacheBytes = cache, CookiesBytes = cookies, HistoryBytes = history };
                    int idx = i;
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        if (idx < Browsers.Count) Browsers[idx] = updated;
                    });
                }
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                    BrowserStatus = $"{profiles.Count} navigateur(s) · {FormatSize(total)} récupérables");
            });
        }
        catch (Exception ex) { BrowserStatus = $"Erreur : {ex.Message}"; }
        finally { IsBrowserBusy = false; }
    }

    [RelayCommand]
    private async Task CleanBrowserCacheAsync(BrowserProfile? profile)
    {
        if (profile == null) return;
        IsBrowserBusy = true;
        BrowserStatus = $"Nettoyage cache {profile.Browser}…";
        try
        {
            var prog   = new Progress<string>(msg => BrowserStatus = msg);
            long freed = await _browserCleaner.CleanCacheAsync(profile, prog);
            BrowserStatus = freed > 0
                ? $"{profile.Browser} — {FormatSize(freed)} libérés (cache) ✓"
                : $"{profile.Browser} : cache déjà vide.";
            if (freed > 0) LogAction("Nettoyage", $"Cache {profile.Browser}", FormatSize(freed));
            ShowToast?.Invoke("Navigateurs", $"{profile.Browser}: {FormatSize(freed)} libérés ✓");
            await ScanBrowsersAsync();
        }
        catch (Exception ex) { BrowserStatus = $"Erreur: {ex.Message}"; }
        finally { IsBrowserBusy = false; }
    }

    [RelayCommand]
    private async Task CleanBrowserAllAsync(BrowserProfile? profile)
    {
        if (profile == null) return;
        IsBrowserBusy = true;
        BrowserStatus = $"Nettoyage complet {profile.Browser} (cache + cookies + historique)…";
        try
        {
            var prog   = new Progress<string>(msg => BrowserStatus = msg);
            long freed = await _browserCleaner.CleanAllAsync(profile, prog);
            BrowserStatus = freed > 0
                ? $"{profile.Browser} — {FormatSize(freed)} libérés (cache + cookies + historique) ✓"
                : $"{profile.Browser} : rien à nettoyer.";
            if (freed > 0) LogAction("Nettoyage", $"Complet {profile.Browser}", FormatSize(freed));
            ShowToast?.Invoke("Navigateurs", $"{profile.Browser} nettoyé — {FormatSize(freed)} libérés ✓");
            await ScanBrowsersAsync();
        }
        catch (Exception ex) { BrowserStatus = $"Erreur: {ex.Message}"; }
        finally { IsBrowserBusy = false; }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // TAB 12 — DÉSINSTALLEUR
    // ══════════════════════════════════════════════════════════════════════════
    public ObservableCollection<InstalledApp> InstalledApps { get; } = [];
    public ObservableCollection<InstalledApp> FilteredApps  { get; } = [];

    [ObservableProperty] private string _appFilter          = "";
    [ObservableProperty] private string _uninstallerStatus  = "Chargez la liste des applications installées.";
    [ObservableProperty] private bool   _isUninstallerBusy  = false;

    partial void OnAppFilterChanged(string value) => RefreshAppFilter();

    private void RefreshAppFilter()
    {
        FilteredApps.Clear();
        var filter = AppFilter.Trim();
        var source = string.IsNullOrEmpty(filter)
            ? InstalledApps
            : InstalledApps.Where(a =>
                a.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                a.Publisher.Contains(filter, StringComparison.OrdinalIgnoreCase));
        foreach (var a in source) FilteredApps.Add(a);
    }

    [RelayCommand]
    private async Task RefreshAppsAsync()
    {
        IsUninstallerBusy = true;
        UninstallerStatus = "Chargement des applications…";
        try
        {
            var apps = await Task.Run(() => _uninstaller.GetInstalledApps());
            InstalledApps.Clear();
            foreach (var a in apps) InstalledApps.Add(a);
            RefreshAppFilter();
            UninstallerStatus = $"{apps.Count} application(s) installée(s).";
        }
        catch (Exception ex) { UninstallerStatus = $"Erreur: {ex.Message}"; }
        finally { IsUninstallerBusy = false; }
    }

    [RelayCommand]
    private async Task UninstallAppAsync(InstalledApp? app)
    {
        if (app == null) return;
        IsUninstallerBusy = true;
        UninstallerStatus = $"Désinstallation de {app.Name}…";
        bool ok = await _uninstaller.UninstallAsync(app);
        UninstallerStatus = ok
            ? $"{app.Name} désinstallé ✓ — actualisez la liste."
            : $"Impossible de désinstaller {app.Name} — essayez manuellement.";
        if (ok)
        {
            LogAction("Système", $"Désinstallation {app.Name}", "Succès");
            ShowToast?.Invoke("Désinstalleur", $"{app.Name} désinstallé ✓");
            await RefreshAppsAsync();
        }
        IsUninstallerBusy = false;
    }

    [RelayCommand]
    private async Task CleanResidualsAsync(InstalledApp? app)
    {
        if (app == null) return;
        IsUninstallerBusy = true;
        UninstallerStatus = $"Nettoyage des résidus de {app.Name}…";
        var progress = new Progress<string>(msg => UninstallerStatus = msg);
        long freed = await _uninstaller.CleanResidualsAsync(app, progress);
        UninstallerStatus = freed > 0
            ? $"Résidus supprimés — {FormatSize(freed)} libérés ✓"
            : "Aucun résidu trouvé.";
        if (freed > 0) LogAction("Système", $"Résidus {app.Name}", FormatSize(freed));
        IsUninstallerBusy = false;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // MISE À JOUR & RAPPORT
    // ══════════════════════════════════════════════════════════════════════════
    private const string AppVersion = "1.0.0";
    public  string VersionDisplay   => $"v{AppVersion}";
    private const string UpdateUrl  = "https://ekippp.fr/optimizer/version.json";

    [ObservableProperty] private string _updateBanner = "";
    [ObservableProperty] private bool   _hasUpdate    = false;

    private async Task CheckForUpdateAsync()
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            var json       = await http.GetStringAsync(UpdateUrl);
            var m          = System.Text.RegularExpressions.Regex.Match(
                                 json, @"""version""\s*:\s*""([^""]+)""");
            if (!m.Success) return;
            var latest = m.Groups[1].Value;
            if (string.CompareOrdinal(latest, AppVersion) > 0)
            {
                UpdateBanner = $"Mise à jour disponible : v{latest} — téléchargez sur ekippp.fr";
                HasUpdate    = true;
                ShowToast?.Invoke("Mise à jour", $"EKIPPP Optimizer v{latest} est disponible !");
            }
        }
        catch { }
    }

    [RelayCommand]
    private async Task ExportReportAsync()
    {
        try
        {
            var score      = PcScore;
            var scoreColor = score >= 80 ? "#22C55E" : score >= 60 ? "#F59E0B" : "#EF4444";
            var issues     = Issues.Select(i => $@"
                <tr>
                    <td style='color:{(i.Severity == IssueSeverity.Critical ? "#EF4444" : "#F59E0B")};font-weight:600'>{i.Severity}</td>
                    <td>{System.Net.WebUtility.HtmlEncode(i.Title)}</td>
                    <td style='color:#A78BFA'>{i.Description}</td>
                </tr>").ToList();
            var history    = History.Take(20).Select(h => $@"
                <tr>
                    <td style='color:#A78BFA'>{h.TimeLabel}</td>
                    <td style='color:#D4C6F0'>{h.Category}</td>
                    <td>{System.Net.WebUtility.HtmlEncode(h.Action)}</td>
                    <td style='color:#22C55E'>{System.Net.WebUtility.HtmlEncode(h.Result)}</td>
                </tr>").ToList();

            var html = $@"<!DOCTYPE html>
<html lang='fr'>
<head><meta charset='UTF-8'><title>Rapport EKIPPP Optimizer</title>
<style>
*{{box-sizing:border-box;margin:0;padding:0}}
body{{font-family:'Segoe UI',Arial,sans-serif;background:#0F0A18;color:#D4C6F0;padding:32px}}
h1{{color:#A78BFA;font-size:28px;margin-bottom:4px}}
.sub{{color:#7C6A9C;font-size:14px;margin-bottom:32px}}
.card{{background:#1A1028;border:1px solid #2D1F45;border-radius:12px;padding:20px;margin-bottom:16px}}
h2{{color:#A78BFA;font-size:13px;font-weight:700;letter-spacing:1px;margin-bottom:14px;text-transform:uppercase}}
.score-big{{font-size:64px;font-weight:900;color:{scoreColor};line-height:1}}
.grid{{display:grid;grid-template-columns:1fr 1fr;gap:16px}}
table{{width:100%;border-collapse:collapse}}
th{{text-align:left;color:#7C6A9C;font-size:11px;padding:4px 8px;border-bottom:1px solid #2D1F45;text-transform:uppercase}}
td{{padding:6px 8px;font-size:13px;border-bottom:1px solid #1E1232;color:#C2B5E0}}
.ok{{color:#22C55E}}
</style></head>
<body>
<h1>EKIPPP OPTIMIZER — Rapport Système</h1>
<p class='sub'>Généré le {DateTime.Now:dd/MM/yyyy à HH:mm:ss}</p>
<div class='grid'>
  <div class='card'>
    <h2>Score PC Global</h2>
    <div class='score-big'>{score}</div>
    <div style='color:#7C6A9C;margin-top:8px'>/100 — {HealthLabel}</div>
    <div style='color:#A78BFA;font-size:13px;margin-top:8px'>{ScoreDeltaLabel}</div>
  </div>
  <div class='card'>
    <h2>Diagnostics</h2>
    <p style='color:#EF4444;font-size:22px;font-weight:700'>{CriticalCount} critique(s)</p>
    <p style='color:#F59E0B;font-size:18px;margin-top:4px'>{WarningCount} avertissement(s)</p>
    <p style='color:#A78BFA;font-size:13px;margin-top:8px'>{PcScoreAdvice}</p>
  </div>
</div>
<div class='card'>
  <h2>Matériel</h2>
  <table>
    <tr><th>Composant</th><th>Détails</th></tr>
    <tr><td>CPU</td><td>{System.Net.WebUtility.HtmlEncode(CpuSummary)}</td></tr>
    <tr><td>GPU</td><td>{System.Net.WebUtility.HtmlEncode(GpuSummary)}</td></tr>
    <tr><td>RAM</td><td>{System.Net.WebUtility.HtmlEncode(RamSummary)}</td></tr>
    <tr><td>Disques</td><td>{System.Net.WebUtility.HtmlEncode(DiskSummary)}</td></tr>
    <tr><td>Carte mère</td><td>{System.Net.WebUtility.HtmlEncode(BoardSummary)}</td></tr>
    <tr><td>Système</td><td>{System.Net.WebUtility.HtmlEncode(OsSummary)}</td></tr>
    <tr><td>CPU Temp</td><td>{MonCpuTempLabel}  ·  Fan: {MonCpuFanLabel}</td></tr>
    <tr><td>GPU Temp</td><td>{MonGpuTempLabel}  ·  Fan: {MonGpuFanLabel}  ·  VRAM: {MonGpuVramLabel}</td></tr>
  </table>
</div>
<div class='card'>
  <h2>Problèmes détectés</h2>
  {(Issues.Count == 0 ? "<p class='ok'>Aucun problème détecté — PC en excellente santé.</p>" : $@"
  <table><tr><th>Sévérité</th><th>Problème</th><th>Description</th></tr>{string.Join("", issues)}</table>")}
</div>
<div class='card'>
  <h2>Historique des actions ({History.Count} entrées)</h2>
  {(History.Count == 0 ? "<p style='color:#7C6A9C'>Aucune action enregistrée dans cette session.</p>" : $@"
  <table><tr><th>Date</th><th>Catégorie</th><th>Action</th><th>Résultat</th></tr>{string.Join("", history)}</table>")}
</div>
<div style='text-align:center;color:#2D1F45;font-size:11px;margin-top:24px'>EKIPPP OPTIMIZER — ekippp.fr</div>
</body></html>";

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var path    = Path.Combine(desktop, $"EKIPPP-Rapport-{DateTime.Now:yyyyMMdd-HHmm}.html");
            await File.WriteAllTextAsync(path, html, System.Text.Encoding.UTF8);
            OpenHtmlInBrowser(path);
            ShowToast?.Invoke("Rapport", "Rapport HTML exporté sur le Bureau ✓");
        }
        catch (Exception ex) { ShowToast?.Invoke("Rapport", $"Erreur: {ex.Message}"); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // INIT
    // ══════════════════════════════════════════════════════════════════════════
    public MainViewModel()
    {
        LoadTweakStates();
        LoadPersistedSnapshot();
        RestorePersistedProfile();
        LoadAutoStartState();
        AppliedDns = _dnsTest.GetCurrentDnsName(); // lit depuis l'adaptateur réseau, pas depuis un cache
        LoadHistory();
        LoadWeeklyScore();
        _ = RunAnalysisAsync();
        _ = RunDiagnosticsAsync();
        _ = CheckForUpdateAsync();
        RefreshTopProcesses();
        StartMonitoring();
    }

    private void LoadTweakStates()
    {
        TwGameDvr         = _optimizer.GetGameDvr()              == TweakState.On;
        TwFullscreenOptim = _optimizer.GetFullscreenOptim()      == TweakState.On;
        TwGpuPriority     = _optimizer.GetGpuPriority()          == TweakState.On;
        TwSystemResp      = _optimizer.GetSystemResponsiveness() == TweakState.On;
        TwWin32Priority   = _optimizer.GetWin32Priority()        == TweakState.On;
        TwMousePrecision  = _optimizer.GetMousePrecision()       == TweakState.On;
        TwHighPerfPlan    = _optimizer.GetHighPerfPlan()         == TweakState.On;
        TwNetThrottle     = _optimizer.GetNetworkThrottling()    == TweakState.On;
        TwTelemetry       = _optimizer.GetTelemetry()            == TweakState.On;
        TwAdvertisingId   = _optimizer.GetAdvertisingId()        == TweakState.On;
        TwLocation        = _optimizer.GetLocation()             == TweakState.On;
        TwCortana         = _optimizer.GetCortana()              == TweakState.On;
        TwVisualEffects   = _optimizer.GetVisualEffects()        == TweakState.On;
    }

    private static void OpenHtmlInBrowser(string filePath)
    {
        // Edge est pré-installé sur tout Windows 10/11 — priorité maximale
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var pf   = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        string[] candidates =
        [
            Path.Combine(pf86, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(pf,   "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(pf86, "Google",    "Chrome",   "Application", "chrome.exe"),
            Path.Combine(pf,   "Google",    "Chrome",   "Application", "chrome.exe"),
            Path.Combine(pf,   "Mozilla Firefox", "firefox.exe"),
        ];

        foreach (var browser in candidates)
        {
            if (!File.Exists(browser)) continue;
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(browser, $"\"{filePath}\"")
                    { UseShellExecute = true });
                return;
            }
            catch { }
        }

        // Dernier recours : association de fichier Windows (peut ouvrir un éditeur si mal configuré)
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch { }
    }

    private void Toast(string name) => ShowToast?.Invoke("EKIPPP Optimizer", $"{name} mis à jour ✓");

    private static string FormatSize(long bytes) => SizeFormatter.Format(bytes);
}

// ── Helpers ───────────────────────────────────────────────────────────────────
internal static class SizeFormatter
{
    public static string Format(long bytes)
    {
        if (bytes >= 1L << 30) return $"{bytes / (1024.0 * 1024 * 1024):F1} Go";
        if (bytes >= 1L << 20) return $"{bytes / (1024.0 * 1024):F1} Mo";
        if (bytes >= 1L << 10) return $"{bytes / 1024.0:F1} Ko";
        return $"{bytes} o";
    }
}

// ── Support types ─────────────────────────────────────────────────────────────
public record ProcessStat(string Name, long RamMB, int Pid);

public class CoreUsageItem(int index) : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public int Index => index;
    private double _percent;
    public double Percent
    {
        get => _percent;
        set => SetProperty(ref _percent, value);
    }
}

public class CleanCategoryVm(CleanCategory category) : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isSelected = true;
    public CleanCategory Category { get; } = category;
    public string Name        => Category.Name;
    public string Description => Category.Description;
    public string SizeLabel   => SizeFormatter.Format(Category.SizeBytes);
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected))); }
    }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
