using System;
using System.Runtime.InteropServices;

namespace BaconTracker.App;

public class Win32Window : IOverlayWindow
{
    public void Initialize()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException("Win32Window is only supported on Windows.");
        }
        
        // Windows specific initialization (e.g., GLFW window creation, DX11 context)
    }

    public void Run()
    {
        // Windows specific render loop
    }

    public void SetClickThrough(bool clickThrough)
    {
        // Windows specific click-through toggles using SetWindowLongPtr WS_EX_TRANSPARENT
    }

    public void Close()
    {
    }

    public void Dispose()
    {
    }
}
