using System.Runtime.InteropServices;

namespace FusionAPI.Epic;

public static class DllTools
{
    public static IntPtr LoadLibrary(string lpLibFileName)
    {
        NativeLibrary.TryLoad(lpLibFileName, out IntPtr handle);
        return handle;
    }

    public static bool FreeLibrary(IntPtr hModule)
    {
        if (hModule != IntPtr.Zero)
        {
            NativeLibrary.Free(hModule);
            return true;
        }
        return false;
    }

    public static uint GetLastError()
    {
        return (uint)Marshal.GetLastPInvokeError();
    }
}