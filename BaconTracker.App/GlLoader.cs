using System;
using System.Runtime.InteropServices;

namespace BaconTracker.App;

public static class GlLoader
{
    private static IntPtr _libGl = IntPtr.Zero;
    private static IntPtr _libEgl = IntPtr.Zero;

    [DllImport("libEGL.so.1", EntryPoint = "eglGetProcAddress", CharSet = CharSet.Ansi)]
    private static extern IntPtr EglGetProcAddress(string name);

    [DllImport("libGL.so.1", EntryPoint = "glXGetProcAddress", CharSet = CharSet.Ansi)]
    private static extern IntPtr GlxGetProcAddress(string name);

    public static IntPtr GetProcAddress(string name)
    {
        // 1. Try eglGetProcAddress (Wayland default)
        try
        {
            IntPtr ptr = EglGetProcAddress(name);
            if (ptr != IntPtr.Zero) return ptr;
        }
        catch { }

        // 2. Try glXGetProcAddress (X11 fallback)
        try
        {
            IntPtr ptr = GlxGetProcAddress(name);
            if (ptr != IntPtr.Zero) return ptr;
        }
        catch { }

        // 3. Try direct export from libGL.so.1
        if (_libGl == IntPtr.Zero)
        {
            try { _libGl = NativeLibrary.Load("libGL.so.1"); } catch { }
        }
        if (_libGl != IntPtr.Zero && NativeLibrary.TryGetExport(_libGl, name, out IntPtr addr))
        {
            return addr;
        }

        // 4. Try direct export from libEGL.so.1
        if (_libEgl == IntPtr.Zero)
        {
            try { _libEgl = NativeLibrary.Load("libEGL.so.1"); } catch { }
        }
        if (_libEgl != IntPtr.Zero && NativeLibrary.TryGetExport(_libEgl, name, out IntPtr addr2))
        {
            return addr2;
        }

        return IntPtr.Zero;
    }
}
