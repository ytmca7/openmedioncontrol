using System;
using System.Diagnostics;

namespace OpenMedionControl;

public static class Autostart
{
    const string TaskName = "OpenMedionControl";

    public static bool IsEnabled() => Schtasks($"/Query /TN \"{TaskName}\"") == 0;

    public static bool Set(bool enabled)
    {
        if (!enabled) return Schtasks($"/Delete /TN \"{TaskName}\" /F") == 0;

        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown process path.");
        string action = $"\\\"{exe}\\\" --minimized";
        return Schtasks($"/Create /TN \"{TaskName}\" /TR \"{action}\" /SC ONLOGON /RL HIGHEST /F") == 0;
    }

    static int Schtasks(string args)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit(10000);
        return p.ExitCode;
    }
}
