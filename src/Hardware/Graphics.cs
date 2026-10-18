using System.Runtime.InteropServices;

namespace OpenMedionControl.Hardware;

public static class Graphics
{
    [DllImport("SysInfo.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern int SetDisGraphics();

    [DllImport("SysInfo.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern int SetInteGraphics();

    public static int SetDedicatedOnly(bool dedicated) => dedicated ? SetDisGraphics() : SetInteGraphics();
}
