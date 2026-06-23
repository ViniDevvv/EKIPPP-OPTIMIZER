using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EkipppOptimizer.Services;

public class RamOptimizerService
{
    [DllImport("ntdll.dll")]
    private static extern uint NtSetSystemInformation(int InfoClass, ref int Info, int Length);

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, nint dwMinimumWorkingSetSize, nint dwMaximumWorkingSetSize);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES { public LUID Luid; public uint Attributes; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID_AND_ATTRIBUTES Privileges; }

    private const int    SystemMemoryListInformation = 80;
    private const int    MemoryPurgeLists            = 3;
    private const uint   TOKEN_ADJUST_PRIVILEGES     = 0x0020;
    private const uint   TOKEN_QUERY                 = 0x0008;
    private const uint   SE_PRIVILEGE_ENABLED        = 0x00000002;

    public long GetAvailableRamMB()
    {
        try
        {
            using var s = new System.Management.ManagementObjectSearcher(
                "SELECT FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (var o in s.Get())
                return Convert.ToInt64(o["FreePhysicalMemory"]) / 1024;
        }
        catch { }
        return 0;
    }

    public long GetStandbyRamMB()
    {
        try
        {
            using var counter = new PerformanceCounter("Memory", "Standby Cache Normal Priority Bytes", "", true);
            long v1 = (long)counter.NextValue();
            using var counter2 = new PerformanceCounter("Memory", "Standby Cache Reserve Bytes", "", true);
            long v2 = (long)counter2.NextValue();
            using var counter3 = new PerformanceCounter("Memory", "Standby Cache Core Bytes", "", true);
            long v3 = (long)counter3.NextValue();
            return (v1 + v2 + v3) / (1024 * 1024);
        }
        catch { return 0; }
    }

    /// <summary>Libère la mémoire en attente (standby list) — nécessite admin.</summary>
    public (long freedMB, string message) OptimizeRam()
    {
        long before = GetAvailableRamMB();
        long standby = GetStandbyRamMB();

        // Étape 1 : vider les working sets de tous les processus
        int wsFreed = EmptyWorkingSets();

        // Étape 2 : purger la standby list
        bool purged = PurgeStandbyList();

        long after  = GetAvailableRamMB();
        long freed  = Math.Max(0, after - before);

        string msg = purged
            ? $"{freed} Mo libérés ({wsFreed} processus compressés, standby purgée)"
            : $"{freed} Mo libérés ({wsFreed} processus compressés) — purge standby nécessite admin";

        return (freed, msg);
    }

    private int EmptyWorkingSets()
    {
        int count = 0;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == Process.GetCurrentProcess().Id) continue;
                SetProcessWorkingSetSize(p.Handle, -1, -1);
                count++;
            }
            catch { }
        }
        return count;
    }

    private bool PurgeStandbyList()
    {
        try
        {
            // Acquérir le privilège SeProfileSingleProcessPrivilege
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle,
                TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token)) return false;

            if (!LookupPrivilegeValue(null, "SeProfileSingleProcessPrivilege", out var luid))
            {
                CloseHandle(token);
                return false;
            }

            var tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Privileges = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = SE_PRIVILEGE_ENABLED }
            };
            AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            CloseHandle(token);

            int command = MemoryPurgeLists;
            uint result = NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int));
            return result == 0;
        }
        catch { return false; }
    }
}
