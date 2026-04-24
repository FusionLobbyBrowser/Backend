using System.Runtime.InteropServices;

namespace FusionAPI.Epic;

public static class DllTools
{
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr LoadLibrary(string lpLibFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern UInt32 GetLastError();
}