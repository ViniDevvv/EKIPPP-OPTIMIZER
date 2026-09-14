using System.Runtime.InteropServices;

namespace EkipppOptimizer.Services;

// Retire le bridage EcoQoS ("mode Efficacité") que Windows peut imposer à un process — fréquence
// plafonnée et, sur CPU hybride (Intel 12e génération et suivantes), planification biaisée vers
// les cœurs Efficacité. C'est un mécanisme DISTINCT de ProcessPriorityClass (Game Booster) et de
// l'affinité cœurs (jamais touchée dans cette app, voir CorePinningService) : un process en
// priorité Haute peut quand même être bridé par EcoQoS si Windows l'a mis en arrière-plan à un
// moment donné. API stable depuis Windows 10 1709, aucune structure incertaine — un seul flag à
// effacer. Le process cible doit déjà tourner sous nos droits (l'app est admin), pas d'élévation
// supplémentaire nécessaire.
public class PowerThrottlingService
{
    private const int ProcessPowerThrottlingClass = 4; // PROCESS_INFORMATION_CLASS.ProcessPowerThrottling
    private const uint CurrentVersion = 1;              // PROCESS_POWER_THROTTLING_CURRENT_VERSION
    private const uint ExecutionSpeedMask = 0x1;         // PROCESS_POWER_THROTTLING_EXECUTION_SPEED

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(
        IntPtr hProcess, int processInformationClass,
        ref PROCESS_POWER_THROTTLING_STATE processInformation, uint processInformationSize);

    public bool ClearEfficiencyMode(IntPtr processHandle)
    {
        try
        {
            var state = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = CurrentVersion,
                ControlMask = ExecutionSpeedMask,
                StateMask = 0, // 0 = throttling forcé désactivé (pas "laisser Windows décider")
            };
            return SetProcessInformation(processHandle, ProcessPowerThrottlingClass,
                ref state, (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
        }
        catch { return false; }
    }
}
