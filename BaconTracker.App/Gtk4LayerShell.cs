using System;
using System.Runtime.InteropServices;

namespace BaconTracker.App;

public static class Gtk4LayerShell
{
    private const string LibraryName = "libgtk4-layer-shell.so.0";

    [DllImport(LibraryName, EntryPoint = "gtk4_layer_is_supported")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsSupported();

    [DllImport(LibraryName, EntryPoint = "gtk4_layer_init_for_toplevel")]
    public static extern void InitForToplevel(IntPtr window);

    [DllImport(LibraryName, EntryPoint = "gtk4_layer_set_layer")]
    public static extern void SetLayer(IntPtr window, int layer);

    [DllImport(LibraryName, EntryPoint = "gtk4_layer_set_namespace")]
    public static extern void SetNamespace(IntPtr window, [MarshalAs(UnmanagedType.LPStr)] string nameSpace);

    [DllImport(LibraryName, EntryPoint = "gtk4_layer_set_anchor")]
    public static extern void SetAnchor(IntPtr window, int edge, [MarshalAs(UnmanagedType.Bool)] bool anchor);

    [DllImport(LibraryName, EntryPoint = "gtk4_layer_set_keyboard_mode")]
    public static extern void SetKeyboardMode(IntPtr window, int mode);

    [DllImport(LibraryName, EntryPoint = "gtk4_layer_set_margin")]
    public static extern void SetMargin(IntPtr window, int edge, int margin);

    [DllImport(LibraryName, EntryPoint = "gtk4_layer_set_exclusive_zone")]
    public static extern void SetExclusiveZone(IntPtr window, int exclusiveZone);

    public enum Layer
    {
        Background = 0,
        Bottom = 1,
        Top = 2,
        Overlay = 3
    }

    public enum Edge
    {
        Left = 0,
        Right = 1,
        Top = 2,
        Bottom = 3
    }

    public enum KeyboardMode
    {
        None = 0,
        Exclusive = 1,
        OnDemand = 2
    }
}
