using System.Runtime.InteropServices;

namespace Icono.App;

internal static class NativeMethods
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwcpDoNotRound = 1;
    private const int DwmwcpRound = 2;
    private const int DwmsbtMainWindow = 2; // Mica

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    /// <summary>The glass theme lets the blurred desktop show through the window (Windows 11 "acrylic"); every other theme keeps Mica behind an
    /// opaque window. Returns false when the system cannot do it.</summary>
    public static bool SetGlassBackdrop(IntPtr hwnd, bool glass)
    {
        var margins = glass ? new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 } : default;
        var extended = DwmExtendFrameIntoClientArea(hwnd, ref margins) == 0;
        var backdropType = glass ? 3 : DwmsbtMainWindow; // acrylic or Mica
        var set = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdropType, Marshal.SizeOf<int>()) == 0;
        return extended && set;
    }

    public static void EnableDarkTitleBar(IntPtr hwnd)
    {
        var enabled = 1;
        var size = Marshal.SizeOf<int>();

        if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref enabled, size) != 0)
        {
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeLegacy, ref enabled, size);
        }
    }

    public static void EnableRoundedCorners(IntPtr hwnd) => SetCornerPreference(hwnd, round: true);

    /// <summary>Windows 11 rounds the corners of a window; a maximized window must stay square.</summary>
    public static void SetCornerPreference(IntPtr hwnd, bool round)
    {
        var preference = round ? DwmwcpRound : DwmwcpDoNotRound;
        DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, Marshal.SizeOf<int>());
    }

    public static bool TryEnableMicaBackdrop(IntPtr hwnd)
    {
        var backdropType = DwmsbtMainWindow;
        var hr = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdropType, Marshal.SizeOf<int>());
        return hr == 0;
    }
}
