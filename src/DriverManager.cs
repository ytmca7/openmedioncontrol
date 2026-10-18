using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace OpenMedionControl;

public static class DriverManager
{
    public const string ServiceName = "OpenMedionIoPort";
    const string VendorServiceName = "ioportdrv";
    const string DriverFile = "WTIOportDrv.sys";

    public static readonly string[] RequiredFiles = { DriverFile, "rwport.dll", "mcucontrol.dll", "SysInfo.dll" };

    public static readonly string InstallDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "OpenMedionControl", "driver");

    public static readonly string BundledDir = Path.Combine(AppContext.BaseDirectory, "driver");

    static readonly string[] VendorProcesses = { "MedionOSD", "Medion Control Center" };

    public enum State { Own, Vendor, Missing }

    public static bool HasAllFiles(string dir) => RequiredFiles.All(f => File.Exists(Path.Combine(dir, f)));

    public static bool IsOwnInstalled => HasAllFiles(InstallDir) && ServiceState(ServiceName) != null;

    static bool IsVendorInstalled(AppSettings settings) =>
        HasAllFiles(settings.VendorPath) && ServiceState(VendorServiceName) != null;

    public static State GetState(AppSettings settings) =>
        IsOwnInstalled ? State.Own
        : IsVendorInstalled(settings) ? State.Vendor
        : State.Missing;

    public static string? FindSource(AppSettings settings) =>
        HasAllFiles(BundledDir) ? BundledDir
        : HasAllFiles(settings.VendorPath) ? settings.VendorPath
        : null;

    public static string EnsureInstalled(AppSettings settings)
    {
        switch (GetState(settings))
        {
            case State.Own:
                if (ServiceState(ServiceName) == "STOPPED")
                    Sc($"start {ServiceName}");
                return InstallDir;

            case State.Vendor:
                return settings.VendorPath;

            default:
                string source = FindSource(settings) ?? throw new FileNotFoundException(
                    $"No driver found. Expected {string.Join(", ", RequiredFiles)} in {BundledDir}.");
                Install(source);
                return InstallDir;
        }
    }

    public static bool Install(string sourceDir)
    {
        var missing = RequiredFiles.Where(f => !File.Exists(Path.Combine(sourceDir, f))).ToArray();
        if (missing.Length > 0)
            throw new FileNotFoundException($"Missing in {sourceDir}: {string.Join(", ", missing)}");

        foreach (var name in VendorProcesses)
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { p.Kill(); p.WaitForExit(5000); } catch (Exception) { }
            }

        bool vendorStillLoaded = RemoveService(VendorServiceName);
        RemoveService(ServiceName);

        Directory.CreateDirectory(InstallDir);
        if (!string.Equals(Path.GetFullPath(sourceDir).TrimEnd('\\'), InstallDir, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var f in RequiredFiles)
                File.Copy(Path.Combine(sourceDir, f), Path.Combine(InstallDir, f), overwrite: true);
        }

        string sys = Path.Combine(InstallDir, DriverFile);
        var (code, output) = Sc($"create {ServiceName} type= kernel start= auto error= normal " +
                                $"binPath= \"{sys}\" DisplayName= \"OpenMedionControl I/O Port\"");
        if (code != 0) throw new InvalidOperationException("Could not create the driver service:\n" + output);

        if (vendorStillLoaded) return true;

        Sc($"start {ServiceName}");
        if (!WaitForState(ServiceName, "RUNNING"))
            throw new InvalidOperationException("Driver installed, but it does not start:\n" + Sc($"query {ServiceName}").Output);
        return false;
    }

    public static void Uninstall(string vendorPath)
    {
        RemoveService(ServiceName);
        try { Directory.Delete(InstallDir, recursive: true); }
        catch (Exception) { }

        string vendorSys = Path.Combine(vendorPath, DriverFile);
        if (File.Exists(vendorSys) && ServiceState(VendorServiceName) == null)
        {
            Sc($"create {VendorServiceName} type= kernel start= auto binPath= \"{vendorSys}\"");
            Sc($"start {VendorServiceName}");
        }
    }

    static bool RemoveService(string name)
    {
        if (ServiceState(name) == null) return false;
        bool stuck = false;
        if (ServiceState(name) != "STOPPED")
        {
            Sc($"stop {name}");
            stuck = !WaitForState(name, "STOPPED");
        }
        Sc($"delete {name}");
        return stuck;
    }

    public static string? ServiceState(string name)
    {
        var (code, output) = Sc($"query {name}");
        if (code == 1060) return null;
        var m = Regex.Match(output, @"\b\d\s+(STOPPED|START_PENDING|STOP_PENDING|RUNNING|CONTINUE_PENDING|PAUSE_PENDING|PAUSED)\b");
        return m.Success ? m.Groups[1].Value : "UNKNOWN";
    }

    static bool WaitForState(string name, string state)
    {
        for (int i = 0; i < 20; i++)
        {
            if (ServiceState(name) == state) return true;
            Thread.Sleep(250);
        }
        return false;
    }

    static (int Code, string Output) Sc(string args)
    {
        var psi = new ProcessStartInfo("sc.exe", args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(15000);
        return (p.ExitCode, output);
    }
}
