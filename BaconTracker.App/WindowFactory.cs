using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;

namespace BaconTracker.App;

public static class WindowFactory
{
    public static IOverlayWindow CreateWindow()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Console.WriteLine("[WindowFactory] Creating Windows native overlay...");
            return Program.ServiceProvider.GetRequiredService<Win32Window>();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string? sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
            Console.WriteLine($"[WindowFactory] Detecting Linux subsystem. Session Type: {sessionType}");

            // Default to Gtk4 Wayland Overlay for Linux
            return Program.ServiceProvider.GetRequiredService<WaylandGtk4Window>();
        }
        
        throw new PlatformNotSupportedException("This operating system is not supported by BaconTracker.");
    }
}
