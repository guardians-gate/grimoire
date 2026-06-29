#if MACCATALYST
using Foundation;
using ObjCRuntime;
using CoreGraphics;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;
using UIKit;

namespace Grimoire.Ui;

/// <summary>
/// Configures Mac Catalyst window chrome and backdrop behavior for the application.
/// </summary>
public partial class App
{
    /// <summary>
    /// View tag used to identify the installed UIKit material backdrop.
    /// </summary>
    private const int MaterialBackdropTag = 197801;
    /// <summary>
    /// Name of the notification raised when UIKit creates a scene window.
    /// </summary>
    private const string SceneWindowCreatedNotificationName = "UISBHSDidCreateWindowForSceneNotification";
    /// <summary>
    /// Notification user-info key that contains the host scene identifier.
    /// </summary>
    private const string SceneIdentifierUserInfoKey = "SceneIdentifier";
    /// <summary>
    /// AppKit material value for the under-window background visual effect.
    /// </summary>
    private const nint NSVisualEffectMaterialUnderWindowBackground = 21;
    /// <summary>
    /// AppKit blending mode value that renders behind the host window.
    /// </summary>
    private const nint NSVisualEffectBlendingModeBehindWindow = 0;
    /// <summary>
    /// AppKit visual effect state value representing the active state.
    /// </summary>
    private const nint NSVisualEffectStateActive = 1;
    /// <summary>
    /// AppKit autoresizing flag for width resizing.
    /// </summary>
    private const nint NSViewWidthSizable = 2;
    /// <summary>
    /// AppKit autoresizing flag for height resizing.
    /// </summary>
    private const nint NSViewHeightSizable = 16;
    /// <summary>
    /// Notification token for scene window creation events.
    /// </summary>
    private static readonly NSString SceneWindowCreatedNotification = new(SceneWindowCreatedNotificationName);
    /// <summary>
    /// User-info key token for the scene identifier payload.
    /// </summary>
    private static readonly NSString SceneIdentifierUserInfo = new(SceneIdentifierUserInfoKey);
    /// <summary>
    /// Selector handle for <c>sharedApplication</c>.
    /// </summary>
    private static readonly IntPtr SharedApplicationSelector = Selector.GetHandle("sharedApplication");
    /// <summary>
    /// Selector handle for <c>delegate</c>.
    /// </summary>
    private static readonly IntPtr DelegateSelector = Selector.GetHandle("delegate");
    /// <summary>
    /// Selector handle for <c>keyWindow</c>.
    /// </summary>
    private static readonly IntPtr KeyWindowSelector = Selector.GetHandle("keyWindow");
    /// <summary>
    /// Selector handle for <c>contentView</c>.
    /// </summary>
    private static readonly IntPtr ContentViewSelector = Selector.GetHandle("contentView");
    /// <summary>
    /// Selector handle for <c>setOpaque:</c>.
    /// </summary>
    private static readonly IntPtr SetOpaqueSelector = Selector.GetHandle("setOpaque:");
    /// <summary>
    /// Selector handle for <c>setBackgroundColor:</c>.
    /// </summary>
    private static readonly IntPtr SetBackgroundColorSelector = Selector.GetHandle("setBackgroundColor:");
    /// <summary>
    /// Selector handle for <c>subviews</c>.
    /// </summary>
    private static readonly IntPtr SubviewsSelector = Selector.GetHandle("subviews");
    /// <summary>
    /// Selector handle for <c>bounds</c>.
    /// </summary>
    private static readonly IntPtr BoundsSelector = Selector.GetHandle("bounds");
    /// <summary>
    /// Selector handle for <c>setFrame:</c>.
    /// </summary>
    private static readonly IntPtr SetFrameSelector = Selector.GetHandle("setFrame:");
    /// <summary>
    /// Selector handle for <c>setMaterial:</c>.
    /// </summary>
    private static readonly IntPtr SetMaterialSelector = Selector.GetHandle("setMaterial:");
    /// <summary>
    /// Selector handle for <c>setBlendingMode:</c>.
    /// </summary>
    private static readonly IntPtr SetBlendingModeSelector = Selector.GetHandle("setBlendingMode:");
    /// <summary>
    /// Selector handle for <c>setState:</c>.
    /// </summary>
    private static readonly IntPtr SetStateSelector = Selector.GetHandle("setState:");
    /// <summary>
    /// Selector handle for <c>setAutoresizingMask:</c>.
    /// </summary>
    private static readonly IntPtr SetAutoresizingMaskSelector = Selector.GetHandle("setAutoresizingMask:");
    /// <summary>
    /// Selector handle for <c>respondsToSelector:</c>.
    /// </summary>
    private static readonly IntPtr RespondsToSelectorSelector = Selector.GetHandle("respondsToSelector:");
    /// <summary>
    /// Selector handle for <c>hostWindowForSceneIdentifier:</c>.
    /// </summary>
    private static readonly IntPtr HostWindowForSceneSelector = Selector.GetHandle("hostWindowForSceneIdentifier:");
    /// <summary>
    /// Selector handle for <c>clearColor</c>.
    /// </summary>
    private static readonly IntPtr ClearColorSelector = Selector.GetHandle("clearColor");
    /// <summary>
    /// Selector handle for <c>alloc</c>.
    /// </summary>
    private static readonly IntPtr AllocSelector = Selector.GetHandle("alloc");
    /// <summary>
    /// Selector handle for <c>init</c>.
    /// </summary>
    private static readonly IntPtr InitSelector = Selector.GetHandle("init");
    /// <summary>
    /// Selector handle for <c>addSubview:positioned:relativeTo:</c>.
    /// </summary>
    private static readonly IntPtr AddSubviewPositionedRelativeSelector = Selector.GetHandle("addSubview:positioned:relativeTo:");
    /// <summary>
    /// Tracks created UIKit windows by scene identifier.
    /// </summary>
    private readonly Dictionary<string, WeakReference<UIWindow>> _sceneWindows = new(StringComparer.Ordinal);
    /// <summary>
    /// Synchronizes one-time registration of the scene window observer.
    /// </summary>
    private readonly Lock _sceneObserverLock = new();
    /// <summary>
    /// Holds the active scene window observer subscription.
    /// </summary>
    private NSObject? _sceneWindowObserver;

    /// <summary>
    /// Initializes platform-specific chrome behavior for Mac Catalyst windows.
    /// </summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Platform chrome initialization is an instance partial hook shared across MAUI targets.")]
    partial void InitializePlatformChrome()
    {
        RegisterSceneWindowObserver();
    }

    /// <summary>
    /// Configures the Catalyst and AppKit backdrops for a created MAUI window.
    /// </summary>
    /// <param name="window">The MAUI window being configured.</param>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Platform chrome is an instance partial hook shared across MAUI targets.")]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The native window owns the inserted material view for the lifetime of the window.")]
    partial void ConfigurePlatformChrome(Window window)
    {
        if (window.Handler?.PlatformView is not UIWindow nativeWindow)
        {
            return;
        }

        RegisterSceneWindowObserver();
        TrackSceneWindow(nativeWindow);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ApplyCatalystBackdrop(nativeWindow);
            string? sceneIdentifier = nativeWindow.WindowScene?.Session.PersistentIdentifier;
            if (!string.IsNullOrWhiteSpace(sceneIdentifier))
            {
                ApplyAppKitBackdropForScene(sceneIdentifier);
            }

            ApplyAppKitBackdropForKeyWindow();
        });
    }

    /// <summary>
    /// Registers a notification observer that reacts to newly created scene windows.
    /// </summary>
    private void RegisterSceneWindowObserver()
    {
        lock (_sceneObserverLock)
        {
            if (_sceneWindowObserver is not null)
            {
                return;
            }

            _sceneWindowObserver = NSNotificationCenter.DefaultCenter.AddObserver(
                SceneWindowCreatedNotification,
                note =>
                {
                    string? hostSceneIdentifier = note.UserInfo?[SceneIdentifierUserInfo]?.ToString();
                    if (string.IsNullOrWhiteSpace(hostSceneIdentifier))
                    {
                        return;
                    }

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        ApplyAppKitBackdropForScene(hostSceneIdentifier);
                        ApplyAppKitBackdropForKeyWindow();
                        UIWindow? window = FindTrackedWindowForHostSceneIdentifier(hostSceneIdentifier);
                        if (window is not null)
                        {
                            ApplyCatalystBackdrop(window);
                        }
                    });
                });
        }
    }

    /// <summary>
    /// Stores the native window by its scene identifier for later lookup.
    /// </summary>
    /// <param name="nativeWindow">The native UIKit window to track.</param>
    private void TrackSceneWindow(UIWindow nativeWindow)
    {
        string? sceneIdentifier = nativeWindow.WindowScene?.Session.PersistentIdentifier;
        if (string.IsNullOrWhiteSpace(sceneIdentifier))
        {
            return;
        }

        _sceneWindows[sceneIdentifier] = new WeakReference<UIWindow>(nativeWindow);
    }

    /// <summary>
    /// Finds the tracked UIKit window that corresponds to a host scene identifier.
    /// </summary>
    /// <param name="hostSceneIdentifier">The host scene identifier reported by AppKit.</param>
    /// <returns>The matching tracked window, or <see langword="null"/> when not found.</returns>
    private UIWindow? FindTrackedWindowForHostSceneIdentifier(string hostSceneIdentifier)
    {
        foreach ((string trackedSceneIdentifier, WeakReference<UIWindow> weakWindow) in _sceneWindows)
        {
            if (!hostSceneIdentifier.EndsWith(trackedSceneIdentifier, StringComparison.Ordinal))
            {
                continue;
            }

            if (weakWindow.TryGetTarget(out UIWindow? window))
            {
                return window;
            }
        }

        return null;
    }

    /// <summary>
    /// Applies transparent Catalyst styling and material backdrop insertion.
    /// </summary>
    /// <param name="nativeWindow">The native window to update.</param>
    private static void ApplyCatalystBackdrop(UIWindow nativeWindow)
    {
        nativeWindow.Opaque = false;
        nativeWindow.BackgroundColor = UIColor.Clear;

        UIView rootHost = nativeWindow.RootViewController?.View ?? nativeWindow;
        MakeTransparent(rootHost);
        MakeAncestorViewsTransparent(rootHost);

        UISplitViewController? splitViewController = FindSplitViewController(nativeWindow.RootViewController);
        if (splitViewController is not null)
        {
            splitViewController.PrimaryBackgroundStyle = UISplitViewControllerBackgroundStyle.Sidebar;
            if (splitViewController.View is not null)
            {
                MakeTransparent(splitViewController.View);
            }

            foreach (UIViewController controller in splitViewController.ViewControllers)
            {
                if (controller.View is not null)
                {
                    MakeTransparent(controller.View);
                }
            }

            if (splitViewController.View is not null)
            {
                MakeAncestorViewsTransparent(splitViewController.View);
                InstallMaterialBackdrop(splitViewController.View);
            }

            return;
        }

        InstallMaterialBackdrop(rootHost);
    }

    /// <summary>
    /// Makes a UIKit view transparent.
    /// </summary>
    /// <param name="view">The view to update.</param>
    private static void MakeTransparent(UIView view)
    {
        view.Opaque = false;
        view.BackgroundColor = UIColor.Clear;
    }

    /// <summary>
    /// Makes all superviews of the specified view transparent.
    /// </summary>
    /// <param name="view">The starting child view.</param>
    private static void MakeAncestorViewsTransparent(UIView view)
    {
        UIView? current = view.Superview;
        while (current is not null)
        {
            MakeTransparent(current);
            current = current.Superview;
        }
    }

    /// <summary>
    /// Installs a UIKit material effect view behind the specified host view.
    /// </summary>
    /// <param name="hostView">The host view that should receive a backdrop.</param>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The native host view owns the material effect view and its effect lifecycle.")]
    private static void InstallMaterialBackdrop(UIView hostView)
    {
        if (hostView.ViewWithTag(MaterialBackdropTag) is UIVisualEffectView)
        {
            return;
        }

        UIVisualEffectView materialView = new(UIBlurEffect.FromStyle(UIBlurEffectStyle.SystemMaterial))
        {
            Tag = MaterialBackdropTag,
            UserInteractionEnabled = false,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        hostView.InsertSubview(materialView, 0);
        NSLayoutConstraint.ActivateConstraints(
        [
            materialView.LeadingAnchor.ConstraintEqualTo(hostView.LeadingAnchor),
            materialView.TrailingAnchor.ConstraintEqualTo(hostView.TrailingAnchor),
            materialView.TopAnchor.ConstraintEqualTo(hostView.TopAnchor),
            materialView.BottomAnchor.ConstraintEqualTo(hostView.BottomAnchor),
        ]);
    }

    /// <summary>
    /// Applies an AppKit visual-effect backdrop for the specified scene identifier.
    /// </summary>
    /// <param name="sceneIdentifier">The AppKit host scene identifier.</param>
    private static void ApplyAppKitBackdropForScene(string sceneIdentifier)
    {
        IntPtr nsAppClass = Class.GetHandle("NSApplication");
        if (nsAppClass == IntPtr.Zero)
        {
            return;
        }

        IntPtr nsApp = IntPtr_objc_msgSend(nsAppClass, SharedApplicationSelector);
        if (nsApp == IntPtr.Zero)
        {
            return;
        }

        IntPtr appDelegate = IntPtr_objc_msgSend(nsApp, DelegateSelector);
        if (appDelegate == IntPtr.Zero ||
            !bool_objc_msgSend_IntPtr(appDelegate, RespondsToSelectorSelector, HostWindowForSceneSelector))
        {
            return;
        }

        using NSString hostSceneIdentifier = new(sceneIdentifier);
        IntPtr hostWindowHandle = IntPtr_objc_msgSend_IntPtr(appDelegate, HostWindowForSceneSelector, hostSceneIdentifier.Handle);
        if (hostWindowHandle == IntPtr.Zero)
        {
            return;
        }

        ApplyAppKitBackdropToWindow(hostWindowHandle);
    }

    /// <summary>
    /// Applies an AppKit visual-effect backdrop for the current key window.
    /// </summary>
    private static void ApplyAppKitBackdropForKeyWindow()
    {
        IntPtr nsAppClass = Class.GetHandle("NSApplication");
        if (nsAppClass == IntPtr.Zero)
        {
            return;
        }

        IntPtr nsApp = IntPtr_objc_msgSend(nsAppClass, SharedApplicationSelector);
        if (nsApp == IntPtr.Zero)
        {
            return;
        }

        IntPtr keyWindowHandle = IntPtr_objc_msgSend(nsApp, KeyWindowSelector);
        if (keyWindowHandle == IntPtr.Zero)
        {
            return;
        }

        ApplyAppKitBackdropToWindow(keyWindowHandle);
    }

    /// <summary>
    /// Applies an AppKit visual-effect backdrop to the provided host window handle.
    /// </summary>
    /// <param name="hostWindowHandle">The AppKit window handle.</param>
    private static void ApplyAppKitBackdropToWindow(IntPtr hostWindowHandle)
    {
        IntPtr nsColorClass = Class.GetHandle("NSColor");
        IntPtr nsVisualEffectViewClass = Class.GetHandle("NSVisualEffectView");
        if (nsColorClass == IntPtr.Zero || nsVisualEffectViewClass == IntPtr.Zero)
        {
            return;
        }

        NSObject? hostWindow = Runtime.GetNSObject<NSObject>(hostWindowHandle);
        if (hostWindow is null)
        {
            return;
        }

        if (!RespondsToSelector(hostWindow.Handle, ContentViewSelector))
        {
            return;
        }

        IntPtr contentViewHandle = IntPtr_objc_msgSend(hostWindow.Handle, ContentViewSelector);
        if (contentViewHandle == IntPtr.Zero)
        {
            return;
        }

        if (RespondsToSelector(hostWindow.Handle, SetOpaqueSelector))
        {
            void_objc_msgSend_bool(hostWindow.Handle, SetOpaqueSelector, false);
        }

        IntPtr clearColorHandle = IntPtr_objc_msgSend(nsColorClass, ClearColorSelector);
        if (clearColorHandle != IntPtr.Zero && RespondsToSelector(hostWindow.Handle, SetBackgroundColorSelector))
        {
            void_objc_msgSend_IntPtr(hostWindow.Handle, SetBackgroundColorSelector, clearColorHandle);
        }

        if (HasVisualEffectSubview(contentViewHandle))
        {
            return;
        }

        IntPtr visualEffectHandle = IntPtr_objc_msgSend(
            IntPtr_objc_msgSend(nsVisualEffectViewClass, AllocSelector),
            InitSelector);
        if (visualEffectHandle == IntPtr.Zero)
        {
            return;
        }

        NSObject? visualEffect = Runtime.GetNSObject<NSObject>(visualEffectHandle);
        if (visualEffect is null)
        {
            return;
        }

        if (RespondsToSelector(visualEffect.Handle, SetMaterialSelector))
        {
            void_objc_msgSend_nint(visualEffect.Handle, SetMaterialSelector, NSVisualEffectMaterialUnderWindowBackground);
        }

        if (RespondsToSelector(visualEffect.Handle, SetBlendingModeSelector))
        {
            void_objc_msgSend_nint(visualEffect.Handle, SetBlendingModeSelector, NSVisualEffectBlendingModeBehindWindow);
        }

        if (RespondsToSelector(visualEffect.Handle, SetStateSelector))
        {
            void_objc_msgSend_nint(visualEffect.Handle, SetStateSelector, NSVisualEffectStateActive);
        }

        if (RespondsToSelector(visualEffect.Handle, SetAutoresizingMaskSelector))
        {
            void_objc_msgSend_nint(visualEffect.Handle, SetAutoresizingMaskSelector, NSViewWidthSizable | NSViewHeightSizable);
        }

        if (RespondsToSelector(contentViewHandle, BoundsSelector) &&
            RespondsToSelector(visualEffect.Handle, SetFrameSelector))
        {
            CGRect contentBounds = CGRect_objc_msgSend(contentViewHandle, BoundsSelector);
            void_objc_msgSend_CGRect(visualEffect.Handle, SetFrameSelector, contentBounds);
        }

        void_objc_msgSend_IntPtr_nint_IntPtr(
            contentViewHandle,
            AddSubviewPositionedRelativeSelector,
            visualEffect.Handle,
            -1,
            IntPtr.Zero);
    }

    /// <summary>
    /// Determines whether a content view already has an AppKit visual-effect subview.
    /// </summary>
    /// <param name="contentViewHandle">The AppKit content view handle.</param>
    /// <returns><see langword="true"/> when a visual-effect subview exists; otherwise, <see langword="false"/>.</returns>
    private static bool HasVisualEffectSubview(IntPtr contentViewHandle)
    {
        IntPtr visualEffectClass = Class.GetHandle("NSVisualEffectView");
        if (visualEffectClass == IntPtr.Zero)
        {
            return false;
        }

        if (!RespondsToSelector(contentViewHandle, SubviewsSelector))
        {
            return false;
        }

        IntPtr subviewsHandle = IntPtr_objc_msgSend(contentViewHandle, SubviewsSelector);
        NSArray? subviews = Runtime.GetNSObject<NSArray>(subviewsHandle);
        if (subviews is null)
        {
            return false;
        }

        for (nuint index = 0; index < subviews.Count; index++)
        {
            if (subviews.GetItem<NSObject>(index)?.ClassHandle == visualEffectClass)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether an Objective-C receiver responds to a selector.
    /// </summary>
    /// <param name="receiver">The Objective-C receiver handle.</param>
    /// <param name="selector">The selector handle to test.</param>
    /// <returns><see langword="true"/> when the selector is supported; otherwise, <see langword="false"/>.</returns>
    private static bool RespondsToSelector(IntPtr receiver, IntPtr selector)
    {
        if (receiver == IntPtr.Zero || selector == IntPtr.Zero)
        {
            return false;
        }

        return bool_objc_msgSend_IntPtr(receiver, RespondsToSelectorSelector, selector);
    }

    /// <summary>
    /// Sends an Objective-C message that returns a pointer and takes no extra arguments.
    /// </summary>
    /// <param name="receiver">The Objective-C receiver handle.</param>
    /// <param name="selector">The selector to invoke.</param>
    /// <returns>The returned Objective-C object handle.</returns>
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector);

    /// <summary>
    /// Sends an Objective-C message that returns a pointer and takes one pointer argument.
    /// </summary>
    /// <param name="receiver">The Objective-C receiver handle.</param>
    /// <param name="selector">The selector to invoke.</param>
    /// <param name="arg1">The first pointer argument.</param>
    /// <returns>The returned Objective-C object handle.</returns>
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr IntPtr_objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

    /// <summary>
    /// Sends an Objective-C message that returns a Boolean and takes one pointer argument.
    /// </summary>
    /// <param name="receiver">The Objective-C receiver handle.</param>
    /// <param name="selector">The selector to invoke.</param>
    /// <param name="arg1">The first pointer argument.</param>
    /// <returns><see langword="true"/> when the message result is true; otherwise, <see langword="false"/>.</returns>
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool bool_objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

    /// <summary>
    /// Sends an Objective-C message that takes one Boolean argument and returns no value.
    /// </summary>
    /// <param name="receiver">The Objective-C receiver handle.</param>
    /// <param name="selector">The selector to invoke.</param>
    /// <param name="arg1">The Boolean argument.</param>
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void void_objc_msgSend_bool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool arg1);

    /// <summary>
    /// Sends an Objective-C message that takes one pointer argument and returns no value.
    /// </summary>
    /// <param name="receiver">The Objective-C receiver handle.</param>
    /// <param name="selector">The selector to invoke.</param>
    /// <param name="arg1">The pointer argument.</param>
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void void_objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

    /// <summary>
    /// Sends an Objective-C message that takes one native integer argument and returns no value.
    /// </summary>
    /// <param name="receiver">The Objective-C receiver handle.</param>
    /// <param name="selector">The selector to invoke.</param>
    /// <param name="arg1">The native integer argument.</param>
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void void_objc_msgSend_nint(IntPtr receiver, IntPtr selector, nint arg1);

    /// <summary>
    /// Sends an Objective-C message that returns a Core Graphics rectangle.
    /// </summary>
    /// <param name="receiver">The Objective-C receiver handle.</param>
    /// <param name="selector">The selector to invoke.</param>
    /// <returns>The returned rectangle value.</returns>
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern CGRect CGRect_objc_msgSend(IntPtr receiver, IntPtr selector);

    /// <summary>
    /// Sends an Objective-C message that takes one Core Graphics rectangle argument and returns no value.
    /// </summary>
    /// <param name="receiver">The Objective-C receiver handle.</param>
    /// <param name="selector">The selector to invoke.</param>
    /// <param name="arg1">The rectangle argument.</param>
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void void_objc_msgSend_CGRect(IntPtr receiver, IntPtr selector, CGRect arg1);

    /// <summary>
    /// Sends an Objective-C message that takes pointer, native integer, and pointer arguments.
    /// </summary>
    /// <param name="receiver">The Objective-C receiver handle.</param>
    /// <param name="selector">The selector to invoke.</param>
    /// <param name="arg1">The first pointer argument.</param>
    /// <param name="arg2">The native integer argument.</param>
    /// <param name="arg3">The second pointer argument.</param>
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void void_objc_msgSend_IntPtr_nint_IntPtr(
        IntPtr receiver,
        IntPtr selector,
        IntPtr arg1,
        nint arg2,
        IntPtr arg3);

    /// <summary>
    /// Finds the first split view controller in the supplied controller hierarchy.
    /// </summary>
    /// <param name="controller">The root controller to inspect.</param>
    /// <returns>The located split view controller, or <see langword="null"/> when none exists.</returns>
    private static UISplitViewController? FindSplitViewController(UIViewController? controller)
    {
        if (controller is null)
        {
            return null;
        }

        if (controller is UISplitViewController splitViewController)
        {
            return splitViewController;
        }

        foreach (UIViewController child in controller.ChildViewControllers)
        {
            UISplitViewController? nested = FindSplitViewController(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return controller.PresentedViewController is null
            ? null
            : FindSplitViewController(controller.PresentedViewController);
    }
}
#endif
