using System.Diagnostics;

namespace EkipppOptimizer.Services;

public class ScheduledTaskService
{
    private const string TaskFolder = "EKIPPP-OPTIMIZER";

    public async Task<bool> CreateDailyCleanupAsync()
    {
        var xml = BuildTaskXml(
            "Nettoyage quotidien",
            "Nettoyage automatique des fichiers temporaires",
            "PT1H",     // trigger: every day at 02:00
            "02:00:00",
            "--auto-clean");
        return await RegisterTaskAsync("DailyCleanup", xml);
    }

    public async Task<bool> CreateWeeklyAnalysisAsync()
    {
        var xml = BuildWeeklyTaskXml(
            "Analyse hebdomadaire",
            "Analyse complète du système chaque semaine",
            "--auto-analyze");
        return await RegisterTaskAsync("WeeklyAnalysis", xml);
    }

    public async Task<bool> DeleteTaskAsync(string taskName)
    {
        return await RunSchtasks($"/Delete /TN \"{TaskFolder}\\{taskName}\" /F");
    }

    public async Task<bool> IsTaskRegisteredAsync(string taskName)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks",
                $"/Query /TN \"{TaskFolder}\\{taskName}\"")
            {
                UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    public async Task<List<string>> GetRegisteredTasksAsync()
    {
        var list = new List<string>();
        var known = new[] { ("DailyCleanup", "Nettoyage quotidien — chaque jour à 02h00"),
                            ("WeeklyAnalysis", "Analyse hebdomadaire — chaque dimanche à 03h00") };
        foreach (var (id, label) in known)
            if (await IsTaskRegisteredAsync(id)) list.Add(label);
        return list;
    }

    private static string BuildTaskXml(string name, string desc, string interval, string startTime, string args)
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "EKIPPP-OPTIMIZER.exe";
        return $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Description>{desc}</Description>
          </RegistrationInfo>
          <Triggers>
            <CalendarTrigger>
              <StartBoundary>2024-01-01T{startTime}</StartBoundary>
              <ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>
            </CalendarTrigger>
          </Triggers>
          <Principals>
            <Principal id="Author">
              <RunLevel>HighestAvailable</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
          </Settings>
          <Actions>
            <Exec>
              <Command>{exePath}</Command>
              <Arguments>{args}</Arguments>
            </Exec>
          </Actions>
        </Task>
        """;
    }

    private static string BuildWeeklyTaskXml(string name, string desc, string args)
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "EKIPPP-OPTIMIZER.exe";
        return $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Description>{desc}</Description>
          </RegistrationInfo>
          <Triggers>
            <CalendarTrigger>
              <StartBoundary>2024-01-01T03:00:00</StartBoundary>
              <ScheduleByWeek>
                <WeeksInterval>1</WeeksInterval>
                <DaysOfWeek><Sunday/></DaysOfWeek>
              </ScheduleByWeek>
            </CalendarTrigger>
          </Triggers>
          <Principals>
            <Principal id="Author">
              <RunLevel>HighestAvailable</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
          </Settings>
          <Actions>
            <Exec>
              <Command>{exePath}</Command>
              <Arguments>{args}</Arguments>
            </Exec>
          </Actions>
        </Task>
        """;
    }

    private static async Task<bool> RegisterTaskAsync(string taskName, string xml)
    {
        try
        {
            var tmpFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ekippp_{taskName}.xml");
            await System.IO.File.WriteAllTextAsync(tmpFile, xml, System.Text.Encoding.Unicode);
            var result = await RunSchtasks($"/Create /TN \"{TaskFolder}\\{taskName}\" /XML \"{tmpFile}\" /F");
            System.IO.File.Delete(tmpFile);
            return result;
        }
        catch { return false; }
    }

    private static async Task<bool> RunSchtasks(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks", args)
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                CreateNoWindow         = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}
