using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace OpenMedionControl.Hardware;

public enum PerformanceMode { Office = 1, Gaming = 2, Turbo = 3 }

public static class Ec
{
    const byte CmdPort = 0x6C;
    const byte DataPort = 0x68;

    const byte PageControl = 0xDE;
    const byte PageTemp = 0xDD;
    const byte PageFan = 0xD5;

    const int StepDelayMs = 10;
    const int MaxTries = 10;

    [DllImport("rwport.dll", EntryPoint = "Write")] static extern void PortWrite(byte port, byte data);
    [DllImport("rwport.dll", EntryPoint = "Read")] static extern byte PortRead(byte port);

    static T Locked<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, @"Global\IO_Mutex");
        bool acquired;
        try { acquired = mutex.WaitOne(3000); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new TimeoutException("EC is busy (IO_Mutex timeout).");
        try { return action(); }
        finally { mutex.ReleaseMutex(); }
    }

    static void Send(byte page, byte value)
    {
        PortWrite(CmdPort, page);
        Thread.Sleep(StepDelayMs);
        PortWrite(DataPort, value);
        Thread.Sleep(StepDelayMs);
    }

    static int Query(byte page, byte register)
    {
        Send(page, register);
        return PortRead(DataPort);
    }

    static int QueryValid(byte page, byte register, Func<int, bool> isValid) => Locked(() =>
    {
        for (int i = 0; i < MaxTries; i++)
        {
            int v = Query(page, register);
            if (isValid(v)) return v;
        }
        return -1;
    });

    static void SendTwice(byte page, byte value) => Locked(() =>
    {
        for (int i = 0; i < 2; i++) { Send(page, value); Thread.Sleep(StepDelayMs); }
        return 0;
    });

    public static PerformanceMode? GetMode()
    {
        int v = QueryValid(PageControl, 0x11, v => v is >= 1 and <= 3);
        return v < 0 ? null : (PerformanceMode)v;
    }

    public static bool SetMode(PerformanceMode mode)
    {
        for (int i = 0; i < MaxTries; i++)
        {
            Locked(() => { Send(PageControl, (byte)mode); return 0; });
            Thread.Sleep(30);
            if (GetMode() == mode) return true;
        }
        return false;
    }

    public static int GetPowerSource() => QueryValid(PageControl, 0x05, v => v is >= 0 and <= 2);

    public static bool TurboAvailable() => GetPowerSource() == 2;

    public static bool? GetFanBoost()
    {
        int v = QueryValid(PageControl, 0x10, v => v is 1 or 2);
        return v < 0 ? null : v == 1;
    }

    public static void SetFanBoost(bool on) => SendTwice(PageControl, on ? (byte)0x0E : (byte)0x0F);

    public static bool? GetUsbPowerWhenOff()
    {
        int v = QueryValid(PageControl, 0x0C, v => v is 16 or 17);
        return v < 0 ? null : v == 17;
    }

    public static void SetUsbPowerWhenOff(bool on) => SendTwice(PageControl, on ? (byte)0x0A : (byte)0x0B);

    public static int GetCpuTemp() => QueryValid(PageTemp, 0x20, v => v is >= 10 and <= 200);

    public static int GetGpuTemp() => QueryValid(PageTemp, 0x23, v => v is >= 10 and <= 200);

    public static int GetCpuFanRpm() => ReadWord(PageFan, 0x18);

    public static int GetGpuFanRpm() => ReadWord(PageFan, 0x16);

    static int ReadWord(byte page, byte lowRegister) => Locked(() =>
    {
        int lo = Query(page, lowRegister);
        Thread.Sleep(StepDelayMs);
        int hi = Query(page, (byte)(lowRegister + 1));
        return (hi << 8) | lo;
    });
}
