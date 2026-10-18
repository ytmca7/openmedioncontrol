using System.Runtime.InteropServices;

namespace OpenMedionControl.Hardware;

public enum LightZone : byte { Keyboard = 0, LeftSide = 1, RightSide = 2, Logo = 3 }

public enum LightEffect : byte
{
    Static = 0,
    Clock = 1,
    Wind = 2,
    Wave = 3,
    Breath = 4,
    Jump = 5,
    Rainbow = 6,
    Flow = 7,
}

public static class Lighting
{
    const byte NoDirection = 2;
    const byte LightCount = 7;

    [DllImport("mcucontrol.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern void OpenLight(byte whichLight);

    [DllImport("mcucontrol.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern void CloseLight(byte whichLight);

    [DllImport("mcucontrol.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern void SetLightRGB(byte whichLight, byte effect, byte brightness, byte speed, byte orientation,
        byte r, byte g, byte b, byte br, byte bg, byte bb);

    [DllImport("mcucontrol.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "WinLock")]
    static extern void WinLockNative(byte flag);

    public static bool UsesDirection(LightEffect e) => e is LightEffect.Wave;

    public static bool UsesSpeed(LightEffect e) => e is not LightEffect.Static;

    public static void Apply(LightZone zone, LightEffect effect, byte brightness, byte speed, bool reverse,
        byte r, byte g, byte b)
    {
        byte direction = UsesDirection(effect) ? (reverse ? (byte)1 : (byte)0) : NoDirection;
        SetLightRGB((byte)zone, (byte)effect, brightness, speed, direction, r, g, b, 0, 0, 0);
    }

    public static void SetAllOn(bool on)
    {
        for (byte i = 0; i < LightCount; i++)
        {
            if (on) OpenLight(i); else CloseLight(i);
        }
    }

    public static void SetWinKeyLock(bool locked) => WinLockNative(locked ? (byte)1 : (byte)0);
}
