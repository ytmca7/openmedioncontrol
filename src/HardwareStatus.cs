using System;
using OpenMedionControl.Hardware;

namespace OpenMedionControl;

public sealed record HardwareStatus(
    PerformanceMode? Mode,
    bool TurboAvailable,
    int CpuTemp,
    int GpuTemp,
    int CpuFanRpm,
    int GpuFanRpm,
    bool? FanBoost,
    bool? UsbPowerWhenOff,
    string? Error)
{
    public static HardwareStatus Read()
    {
        try
        {
            return new HardwareStatus(
                Ec.GetMode(),
                Ec.TurboAvailable(),
                Ec.GetCpuTemp(),
                Ec.GetGpuTemp(),
                Ec.GetCpuFanRpm(),
                Ec.GetGpuFanRpm(),
                Ec.GetFanBoost(),
                Ec.GetUsbPowerWhenOff(),
                null);
        }
        catch (Exception ex)
        {
            return new HardwareStatus(null, false, -1, -1, -1, -1, null, null, ex.Message);
        }
    }

    public static string ModeName(PerformanceMode? mode) => mode switch
    {
        PerformanceMode.Office => "Office",
        PerformanceMode.Gaming => "Gaming",
        PerformanceMode.Turbo => "Turbo",
        _ => "?",
    };
}
