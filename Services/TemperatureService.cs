using LibreHardwareMonitor.Hardware;

namespace EkipppOptimizer.Services;

public record ThermalSnapshot(
    double CpuTemp,
    double GpuTemp,
    double CpuFanRpm,
    double GpuFanRpm,
    double GpuLoad,
    double GpuVramUsedMB,
    double GpuVramTotalMB);

public class TemperatureService : IDisposable
{
    private readonly Computer _computer;
    private bool _initialized;

    public TemperatureService()
    {
        _computer = new Computer
        {
            IsCpuEnabled     = true,
            IsGpuEnabled     = true,
            IsMemoryEnabled  = false,
            IsMotherboardEnabled = false,
            IsStorageEnabled = false,
        };
    }

    public void Initialize()
    {
        if (_initialized) return;
        try { _computer.Open(); _initialized = true; } catch { }
    }

    public ThermalSnapshot Sample()
    {
        if (!_initialized) return new(0, 0, 0, 0, 0, 0, 0);

        double cpuTemp = 0, gpuTemp = 0, cpuFan = 0, gpuFan = 0;
        double gpuLoad = 0, gpuVramUsed = 0, gpuVramTotal = 0;

        try
        {
            foreach (var hw in _computer.Hardware)
            {
                hw.Update();

                if (hw.HardwareType == HardwareType.Cpu)
                {
                    foreach (var s in hw.Sensors)
                    {
                        if (s.SensorType == SensorType.Temperature && s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
                            cpuTemp = s.Value ?? 0;
                        if (s.SensorType == SensorType.Fan)
                            cpuFan = Math.Max(cpuFan, s.Value ?? 0);
                    }
                    if (cpuTemp == 0)
                        foreach (var s in hw.Sensors.Where(s => s.SensorType == SensorType.Temperature))
                            cpuTemp = Math.Max(cpuTemp, s.Value ?? 0);
                }
                else if (hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                {
                    foreach (var s in hw.Sensors)
                    {
                        if (s.SensorType == SensorType.Temperature) gpuTemp = Math.Max(gpuTemp, s.Value ?? 0);
                        if (s.SensorType == SensorType.Fan)         gpuFan  = Math.Max(gpuFan,  s.Value ?? 0);
                        if (s.SensorType == SensorType.Load && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                            gpuLoad = s.Value ?? 0;
                        if (s.SensorType == SensorType.SmallData && s.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase))
                            gpuVramUsed = s.Value ?? 0;
                        if (s.SensorType == SensorType.SmallData && s.Name.Contains("Memory Total", StringComparison.OrdinalIgnoreCase))
                            gpuVramTotal = s.Value ?? 0;
                    }
                }
            }
        }
        catch { }

        return new(
            Math.Round(cpuTemp, 1),
            Math.Round(gpuTemp, 1),
            Math.Round(cpuFan),
            Math.Round(gpuFan),
            Math.Round(gpuLoad, 1),
            Math.Round(gpuVramUsed),
            Math.Round(gpuVramTotal));
    }

    public void Dispose()
    {
        try { if (_initialized) _computer.Close(); } catch { }
    }
}
