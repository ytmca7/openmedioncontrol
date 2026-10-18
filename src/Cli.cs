using System;
using OpenMedionControl.Hardware;

namespace OpenMedionControl;

public static class Cli
{
    public static bool Handles(string[] args) => args.Length > 0 && args[0] is "--status" or "--mode" or "--fan";

    public static int Run(string[] args)
    {
        try
        {
            switch (args[0])
            {
                case "--status":
                    var s = HardwareStatus.Read();
                    if (s.Error != null) { Console.Error.WriteLine(s.Error); return 1; }
                    Console.WriteLine($"Mode:               {HardwareStatus.ModeName(s.Mode)}");
                    Console.WriteLine($"Turbo available:    {s.TurboAvailable}");
                    Console.WriteLine($"CPU:                {s.CpuTemp} C, {s.CpuFanRpm} RPM");
                    Console.WriteLine($"GPU:                {s.GpuTemp} C, {s.GpuFanRpm} RPM");
                    Console.WriteLine($"Max fan speed:      {s.FanBoost}");
                    Console.WriteLine($"USB power when off: {s.UsbPowerWhenOff}");
                    return 0;

                case "--mode" when args.Length > 1 && Enum.TryParse<PerformanceMode>(args[1], true, out var mode):
                    bool ok = Ec.SetMode(mode);
                    Console.WriteLine(ok ? $"Mode: {mode}" : "Could not set the mode.");
                    return ok ? 0 : 1;

                case "--fan" when args.Length > 1 && args[1] is "on" or "off":
                    Ec.SetFanBoost(args[1] == "on");
                    return 0;

                default:
                    Console.Error.WriteLine("Usage: OpenMedionControl --status | --mode office|gaming|turbo | --fan on|off");
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
