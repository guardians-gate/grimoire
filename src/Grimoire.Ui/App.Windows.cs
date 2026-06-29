#if WINDOWS
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace Grimoire.Ui;

/// <summary>
/// Represents Windows-specific window chrome customization for the MAUI host window.
/// </summary>
public partial class App
{
    /// <summary>
    /// Applies Mica and title-bar styling for Windows app windows.
    /// </summary>
    /// <param name="window">The MAUI window representing the native WinUI window to customize.</param>
    partial void ConfigurePlatformChrome(Window window)
    {
        if (window.Handler?.PlatformView is not MauiWinUIWindow nativeWindow)
        {
            return;
        }

        nativeWindow.SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        nativeWindow.ExtendsContentIntoTitleBar = true;

        IntPtr handle = WindowNative.GetWindowHandle(nativeWindow);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(handle);
        AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(48, 217, 183, 110);
        appWindow.TitleBar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(80, 217, 183, 110);
        appWindow.TitleBar.ButtonForegroundColor = Colors.White;
        appWindow.TitleBar.ButtonInactiveForegroundColor = ColorHelper.FromArgb(176, 255, 255, 255);
    }
}
#endif
