using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TensileNeW;

internal sealed class RingInspectHostWindow : Window
{
    private readonly IntPtr _targetHandle;
    private readonly RingInspectChildHost _childHost;
    private readonly Window _owner;
    private bool _allowClose;

    public bool IsEmbedded => _childHost.IsAttached;

    public RingInspectHostWindow(Window owner, Process process, IntPtr targetHandle)
    {
        _targetHandle = targetHandle;
        _owner = owner;
        string title = RingInspectNative.ReadWindowTitle(targetHandle);
        Title = string.IsNullOrWhiteSpace(title) ? "识别" : title;
        Owner = owner;
        Width = 1280;
        Height = 800;
        MinWidth = 640;
        MinHeight = 420;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;
        _childHost = new RingInspectChildHost(targetHandle);

        Grid layout = new();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Border chrome = new()
        {
            Background = new SolidColorBrush(Color.FromRgb(247, 248, 250)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 223, 228)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        Grid header = new();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        StackPanel titlePanel = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        ImageSource? icon = RingInspectNative.ReadWindowIcon(targetHandle, process.StartInfo.FileName);
        if (icon is not null) titlePanel.Children.Add(new Image { Source = icon, Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0) });
        titlePanel.Children.Add(new TextBlock { Text = Title, VerticalAlignment = VerticalAlignment.Center, FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(42, 47, 55)) });
        header.Children.Add(titlePanel);
        StackPanel buttons = new() { Orientation = Orientation.Horizontal };
        Button minimize = CreateChromeButton("—");
        minimize.Click += (_, _) => HideHost();
        Button maximize = CreateChromeButton("□");
        maximize.Click += (_, _) => ToggleMaximize();
        buttons.Children.Add(minimize);
        buttons.Children.Add(maximize);
        Grid.SetColumn(buttons, 1);
        header.Children.Add(buttons);
        chrome.Child = header;
        layout.Children.Add(chrome);
        Grid.SetRow(_childHost, 1);
        layout.Children.Add(_childHost);
        Content = layout;
        chrome.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) ToggleMaximize();
            else if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove();
        };
        Closing += (_, e) =>
        {
            if (_allowClose) return;
            e.Cancel = true;
            HideHost();
        };
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized) HideHost();
        };
    }

    public bool ShowOrActivate()
    {
        if (!_childHost.IsAttached) return false;
        if (!IsVisible)
        {
            WindowState = WindowState.Normal;
            Left = _owner.Left;
            Top = _owner.Top;
            WindowState = WindowState.Maximized;
        }
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        _childHost.ResizeTarget();
        RingInspectNative.ShowWindowAsync(_targetHandle, 5);
        Activate();
        return IsVisible && _childHost.IsAttached;
    }

    public void PrepareHidden()
    {
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = _owner.Left;
        Top = _owner.Top;
        Opacity = 0;
        WindowState = WindowState.Normal;
        Show();
        WindowState = WindowState.Maximized;
        HideHost();
        Opacity = 1;
        ShowActivated = true;
    }

    public void ShutdownHost()
    {
        _allowClose = true;
        _childHost.Detach();
        if (IsLoaded) Close();
    }

    private void HideHost()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Hide();
        RingInspectNative.HideWindow(_targetHandle);
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private static Button CreateChromeButton(string text) => new()
    {
        Content = text,
        Width = 46,
        Height = 38,
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        Foreground = new SolidColorBrush(Color.FromRgb(55, 60, 68)),
        FontSize = 16,
        Cursor = System.Windows.Input.Cursors.Hand
    };
}

internal sealed class RingInspectChildHost : HwndHost
{
    private const int GwlStyle = -16;
    private const int GwlExtendedStyle = -20;
    private const long WsExAppWindow = 0x00040000L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsChild = 0x40000000L;
    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;
    private const long WsSysMenu = 0x00080000L;
    private const long WsMinimizeBox = 0x00020000L;
    private const long WsMaximizeBox = 0x00010000L;
    private const long WsPopup = unchecked((long)0x80000000);
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpAsyncWindowPos = 0x4000;
    private const int SwHide = 0;
    private readonly IntPtr _targetHandle;
    private IntPtr _containerHandle;
    private long _originalStyle;
    private long _originalExtendedStyle;

    public bool IsAttached => _containerHandle != IntPtr.Zero && RingInspectNative.GetParent(_targetHandle) == _containerHandle;

    public RingInspectChildHost(IntPtr targetHandle) => _targetHandle = targetHandle;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _containerHandle = RingInspectNative.CreateWindowEx(0, "STATIC", null, RingInspectNative.WsChild | RingInspectNative.WsVisible | RingInspectNative.WsClipChildren,
            0, 0, 1, 1, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_containerHandle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        _originalStyle = RingInspectNative.GetWindowLongPtr(_targetHandle, GwlStyle).ToInt64();
        _originalExtendedStyle = RingInspectNative.GetWindowLongPtr(_targetHandle, GwlExtendedStyle).ToInt64();
        long childStyle = (_originalStyle & ~(WsPopup | WsCaption | WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox)) | WsChild;
        RingInspectNative.SetWindowLongPtr(_targetHandle, GwlStyle, new IntPtr(childStyle));
        long childExtendedStyle = (_originalExtendedStyle & ~WsExAppWindow) | WsExToolWindow;
        RingInspectNative.SetWindowLongPtr(_targetHandle, GwlExtendedStyle, new IntPtr(childExtendedStyle));
        RingInspectNative.SetParent(_targetHandle, _containerHandle);
        if (!IsAttached)
        {
            int error = Marshal.GetLastWin32Error();
            RingInspectNative.SetWindowLongPtr(_targetHandle, GwlStyle, new IntPtr(_originalStyle));
            RingInspectNative.SetWindowLongPtr(_targetHandle, GwlExtendedStyle, new IntPtr(_originalExtendedStyle));
            RingInspectNative.DestroyWindow(_containerHandle);
            _containerHandle = IntPtr.Zero;
            throw new Win32Exception(error, "识别程序窗口嵌入失败。");
        }
        RingInspectNative.SetWindowPos(_targetHandle, IntPtr.Zero, 0, 0, 1, 1, SwpNoZOrder | SwpNoActivate | SwpFrameChanged | SwpAsyncWindowPos);
        RingInspectNative.ShowWindowAsync(_targetHandle, SwHide);
        return new HandleRef(this, _containerHandle);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        Detach();
        if (_containerHandle != IntPtr.Zero) RingInspectNative.DestroyWindow(_containerHandle);
        _containerHandle = IntPtr.Zero;
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        ResizeTarget();
    }

    public void ResizeTarget()
    {
        if (!IsAttached) return;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        RingInspectNative.SetWindowPos(_targetHandle, IntPtr.Zero, 0, 0,
            Math.Max(1, (int)Math.Round(ActualWidth * scale)),
            Math.Max(1, (int)Math.Round(ActualHeight * scale)), SwpNoZOrder | SwpNoActivate | SwpAsyncWindowPos);
    }

    public void Detach()
    {
        if (!IsAttached || !RingInspectNative.IsWindow(_targetHandle)) return;
        RingInspectNative.HideWindow(_targetHandle);
        RingInspectNative.SetWindowLongPtr(_targetHandle, GwlStyle, new IntPtr(_originalStyle));
        RingInspectNative.SetWindowLongPtr(_targetHandle, GwlExtendedStyle, new IntPtr(_originalExtendedStyle));
        RingInspectNative.SetParent(_targetHandle, IntPtr.Zero);
    }
}

internal static class RingInspectNative
{
    internal const uint WsChild = 0x40000000;
    internal const uint WsVisible = 0x10000000;
    internal const uint WsClipChildren = 0x02000000;
    private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr parameter);
    private const uint WmGetIcon = 0x007F;
    private const int IconSmall = 0;
    private const int IconBig = 1;
    private const int GclpHIcon = -14;
    private const int GclpHIconSm = -34;
    private const uint GwOwner = 4;

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr handle, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string? windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool DestroyWindow(IntPtr handle);
    [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(IntPtr handle, int command);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr handle);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern IntPtr GetWindowLongPtr64(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr64(IntPtr handle, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)] private static extern IntPtr GetWindowLong32(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern IntPtr SetWindowLong32(IntPtr handle, int index, IntPtr value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr handle, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] private static extern IntPtr GetClassLongPtr64(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "GetClassLongW")] private static extern uint GetClassLong32(IntPtr handle, int index);

    internal static IntPtr GetWindowLongPtr(IntPtr handle, int index) => IntPtr.Size == 8 ? GetWindowLongPtr64(handle, index) : GetWindowLong32(handle, index);
    internal static IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value) => IntPtr.Size == 8 ? SetWindowLongPtr64(handle, index, value) : SetWindowLong32(handle, index, value);
    internal static bool ActivateHostedWindow(IntPtr handle)
    {
        uint message = RegisterWindowMessage("RingInspect.Wpf.Host.Activate.v1");
        return message != 0 && PostMessage(handle, message, IntPtr.Zero, IntPtr.Zero);
    }

    internal static IntPtr FindMainWindow(int processId)
    {
        IntPtr result = IntPtr.Zero;
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out uint ownerProcessId);
            if (ownerProcessId != (uint)processId || GetWindow(handle, GwOwner) != IntPtr.Zero ||
                string.IsNullOrWhiteSpace(ReadWindowTitle(handle))) return true;
            result = handle;
            return false;
        }, IntPtr.Zero);
        return result;
    }
    internal static string ReadWindowTitle(IntPtr handle)
    {
        StringBuilder text = new(512);
        _ = GetWindowText(handle, text, text.Capacity);
        return text.ToString();
    }

    internal static ImageSource? ReadWindowIcon(IntPtr handle, string executablePath)
    {
        IntPtr icon = SendMessage(handle, WmGetIcon, new IntPtr(IconBig), IntPtr.Zero);
        if (icon == IntPtr.Zero) icon = SendMessage(handle, WmGetIcon, new IntPtr(IconSmall), IntPtr.Zero);
        if (icon == IntPtr.Zero) icon = IntPtr.Size == 8 ? GetClassLongPtr64(handle, GclpHIcon) : new IntPtr(unchecked((int)GetClassLong32(handle, GclpHIcon)));
        if (icon == IntPtr.Zero) icon = IntPtr.Size == 8 ? GetClassLongPtr64(handle, GclpHIconSm) : new IntPtr(unchecked((int)GetClassLong32(handle, GclpHIconSm)));
        if (icon == IntPtr.Zero)
        {
            try
            {
                using System.Drawing.Icon? executableIcon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
                if (executableIcon is null) return null;
                ImageSource executableImage = Imaging.CreateBitmapSourceFromHIcon(executableIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                executableImage.Freeze();
                return executableImage;
            }
            catch
            {
                return null;
            }
        }
        ImageSource image = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        image.Freeze();
        return image;
    }

    internal static void HideWindow(IntPtr handle) => ShowWindowAsync(handle, 0);
}
