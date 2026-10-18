using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using OpenMedionControl.Hardware;

namespace OpenMedionControl;

public partial class App : Application
{
    const string InstanceName = "OpenMedionControl.SingleInstance";
    const string ShowEventName = "OpenMedionControl.Show";

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetDllDirectory(string path);

    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);

    Mutex? _instance;
    EventWaitHandle? _showEvent;
    TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var settings = AppSettings.Load();

        if (e.Args.Length > 0 && e.Args[0] is "--install-driver" or "--uninstall-driver")
        {
            RunDriverSetup(e.Args, settings);
            Shutdown();
            return;
        }

        string dllDir;
        try
        {
            dllDir = DriverManager.EnsureInstalled(settings);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "The hardware driver could not be installed.\n\n" + ex.Message + "\n\n" +
                $"Make sure the \"driver\" folder next to OpenMedionControl.exe contains " +
                $"{string.Join(", ", DriverManager.RequiredFiles)}.",
                "OpenMedionControl", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        SetDllDirectory(dllDir);

        if (Cli.Handles(e.Args))
        {
            AttachConsole(-1);
            Shutdown(Cli.Run(e.Args));
            return;
        }

        _instance = new Mutex(true, InstanceName, out bool isFirst);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!isFirst)
        {
            _showEvent.Set();
            Shutdown();
            return;
        }

        RestoreLighting(settings);

        var window = new MainWindow(settings);
        _tray = new TrayIcon(window);
        window.Icon = TrayIcon.CreateImageSource();

        var listener = new Thread(() =>
        {
            while (_showEvent.WaitOne())
                Dispatcher.Invoke(window.ShowFromTray);
        }) { IsBackground = true };
        listener.Start();

        if (!e.Args.Contains("--minimized"))
            window.Show();
    }

    static void RunDriverSetup(string[] args, AppSettings settings)
    {
        using (var instance = new Mutex(false, InstanceName))
        {
            bool acquired;
            try { acquired = instance.WaitOne(15000); }
            catch (AbandonedMutexException) { acquired = true; }

            try
            {
                if (args[0] == "--install-driver")
                {
                    string source = args.Length > 1 ? args[1]
                        : DriverManager.FindSource(settings) ?? DriverManager.BundledDir;
                    bool reboot = DriverManager.Install(source);
                    MessageBox.Show(
                        reboot
                            ? "Driver installed. The Medion driver is still loaded; OpenMedionControl's own driver takes over after a reboot.\n\n" +
                              "Until then everything keeps working as before."
                            : "Driver installed and running.\n\nThe Medion software can now be uninstalled.",
                        "OpenMedionControl", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    DriverManager.Uninstall(settings.VendorPath);
                    MessageBox.Show("Driver removed.", "OpenMedionControl",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "OpenMedionControl – Driver", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (acquired) instance.ReleaseMutex();
            }
        }

        bool relaunch = args[0] == "--install-driver" || DriverManager.GetState(settings) == DriverManager.State.Vendor;
        if (relaunch && Environment.ProcessPath is { } exe)
            System.Diagnostics.Process.Start(exe);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }

    static void RestoreLighting(AppSettings settings)
    {
        try
        {
            Lighting.SetAllOn(settings.LightsOn);
            if (settings.LightsOn)
            {
                foreach (var (zone, z) in settings.Zones)
                    z.ApplyTo(zone);
            }
            if (settings.WinKeyLocked)
                Lighting.SetWinKeyLock(true);
        }
        catch (Exception) { }
    }
}
