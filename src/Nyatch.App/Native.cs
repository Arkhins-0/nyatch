using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Nyatch.App;

/// <summary>An open window, as the Apps tab lists it.</summary>
internal sealed record OpenWindow(IntPtr Handle, string Title, string? Path);

/// <summary>
/// A running app and its windows, front one first.
/// Id tells apps apart (the program's file, or the app's own identity when several apps share one
/// program); it is null when the app cannot be told apart or started again from here.
/// Launch is the command that starts it when the Id alone will not, and IconSource is where its icon is read from.
/// </summary>
internal sealed record ActiveApp(string Name, string? Id, string? Launch, string? IconSource, List<OpenWindow> Windows);

/// <summary>The Windows calls the second pill's widgets need.</summary>
internal static class Native
{
    // ---------- Window styles ----------

    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x80, WsExNoActivate = 0x08000000, WsExAppWindow = 0x40000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    /// <summary>Keep a window out of Alt+Tab, and choose whether clicking it takes the keyboard.</summary>
    public static void SetToolWindow(Window window, bool takesKeyboard)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var style = (long)GetWindowLongPtr(handle, GwlExStyle) | WsExToolWindow;
        style = takesKeyboard ? style & ~WsExNoActivate : style | WsExNoActivate;
        SetWindowLongPtr(handle, GwlExStyle, (IntPtr)style);
    }

    // ---------- Open windows ----------

    private delegate bool EnumProc(IntPtr window, IntPtr state);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr state);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr window, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

    /// <summary>The windows Alt+Tab would show, front to back.</summary>
    public static List<OpenWindow> OpenWindows(int limit)
    {
        var found = new List<OpenWindow>();
        var own = (uint)Environment.ProcessId;
        EnumWindows((window, _) =>
        {
            if (found.Count >= limit) return false;
            if (!IsWindowVisible(window) || GetWindow(window, 4 /* owner */) != IntPtr.Zero) return true;
            var style = (long)GetWindowLongPtr(window, GwlExStyle);
            if ((style & WsExToolWindow) != 0 && (style & WsExAppWindow) == 0) return true;
            // Hidden Store-app shells report visible but are "cloaked".
            if (DwmGetWindowAttribute(window, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            var text = new StringBuilder(260);
            if (GetWindowTextW(window, text, text.Capacity) == 0) return true;
            GetWindowThreadProcessId(window, out var process);
            if (process == own) return true;
            found.Add(new OpenWindow(window, text.ToString(), ProcessPath(process)));
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase) { "msedge.exe", "chrome.exe", "brave.exe", "vivaldi.exe", "opera.exe", "chromium.exe", "firefox.exe" };

    private static bool IsBrowser(string? program) => program is not null && Browsers.Contains(System.IO.Path.GetFileName(program));

    /// <summary>The open windows gathered by app, in the order their front windows are stacked.</summary>
    public static List<ActiveApp> ActiveApps()
    {
        var apps = new List<ActiveApp>();
        foreach (var window in OpenWindows(64))
        {
            var app = AppOf(window);
            var known = app.Id is null ? null : apps.Find(a => string.Equals(a.Id, app.Id, StringComparison.OrdinalIgnoreCase));
            if (known is not null) known.Windows.Add(window);
            else apps.Add(app);
        }
        return apps;
    }

    /// <summary>Which app a window belongs to. Several apps can share one program: a website installed as an app runs inside the browser, and Store apps inside a Windows host. Those windows say who they are.</summary>
    private static ActiveApp AppOf(OpenWindow window)
    {
        var said = WindowSays(window.Handle);
        var host = window.Path is null || System.IO.Path.GetFileName(window.Path).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);
        var installedSite = said.Command.Contains("--app", StringComparison.OrdinalIgnoreCase);
        // A browser's own windows are the browser, one app, whatever page they show. Only a site installed
        // as an app is an app of its own: Chrome or Edge start those with "--app", and a Store web app
        // (Netflix, Crunchyroll) has a package identity ("Package_id!App").
        if (IsBrowser(window.Path) && !installedSite && !said.Id.Contains('!'))
        {
            return new ActiveApp(AppName(window.Path!), window.Path, null, window.Path, [window]);
        }

        if (said.Id.Length > 0)
        {
            // An app Windows lists under this identity: a Store app, or a website from the Store that runs
            // inside the browser (Netflix, Crunchyroll). Windows has its name, its icon and the way to start it.
            var item = @"shell:AppsFolder\" + said.Id;
            if (Icon(item) is not null) return new ActiveApp(ShellName(item) ?? window.Title, said.Id, item, item, [window]);

            // A website installed from the browser: the window itself carries its name, icon and command.
            if (said.Command.Length > 0)
            {
                var name = said.Name.Length > 0 && !said.Name.StartsWith('@') ? said.Name : window.Path is null ? window.Title : AppName(window.Path);
                return new ActiveApp(name, said.Id, said.Command, said.Icon.Length > 0 ? said.Icon : window.Path, [window]);
            }
        }
        if (host) return new ActiveApp(window.Title, null, null, null, [window]);
        return new ActiveApp(AppName(window.Path!), window.Path, null, window.Path, [window]);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr result);
        [PreserveSig] int GetParent(out IntPtr parent);
        [PreserveSig] int GetDisplayName(uint form, out IntPtr name);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHCreateItemFromParsingName")]
    private static extern int ShellItemFromName(string path, IntPtr context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItem? item);

    private static readonly Dictionary<string, string?> ShellNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The name Windows shows for a shell item, such as "Crunchyroll" for its entry among the installed apps.</summary>
    private static string? ShellName(string path)
    {
        if (ShellNames.TryGetValue(path, out var known)) return known;
        string? name = null;
        var iid = typeof(IShellItem).GUID;
        IShellItem? item = null;
        try
        {
            if (ShellItemFromName(path, IntPtr.Zero, ref iid, out item) == 0 && item is not null && item.GetDisplayName(0 /* as shown */, out var text) == 0 && text != IntPtr.Zero)
            {
                name = Marshal.PtrToStringUni(text);
                Marshal.FreeCoTaskMem(text);
            }
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
        }
        finally
        {
            if (item is not null) Marshal.ReleaseComObject(item);
        }
        return ShellNames[path] = string.IsNullOrWhiteSpace(name) ? null : name;
    }

    // ----- What a window says about its app -----

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid Format;
        public uint Id;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [DllImport("shell32.dll")] private static extern int SHGetPropertyStoreForWindow(IntPtr window, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore? store);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);

    private static readonly Guid AppModel = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    /// <summary>The identity, start command, icon and name a window has set for itself; empty strings where it has not.</summary>
    private static (string Id, string Command, string Icon, string Name) WindowSays(IntPtr window)
    {
        var iid = typeof(IPropertyStore).GUID;
        try
        {
            if (SHGetPropertyStoreForWindow(window, ref iid, out var store) != 0 || store is null) return ("", "", "", "");
            try
            {
                return (Text(store, 5), Text(store, 2), Text(store, 3), Text(store, 4));
            }
            finally
            {
                Marshal.ReleaseComObject(store);
            }
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
            return ("", "", "", "");
        }

        static string Text(IPropertyStore store, uint id)
        {
            var key = new PropertyKey { Format = AppModel, Id = id };
            if (store.GetValue(ref key, out var value) != 0) return "";
            try
            {
                return value.Type == 31 /* text */ && value.Pointer != IntPtr.Zero ? Marshal.PtrToStringUni(value.Pointer) ?? "" : "";
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
    }

    /// <summary>Start a program by its file, a shell item, or a whole command line with arguments.</summary>
    public static void Launch(string command)
    {
        command = command.Trim();
        if (command.Length == 0) return;
        if (command.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || File.Exists(command))
        {
            Open(command);
            return;
        }
        string program, arguments;
        if (command[0] == '"')
        {
            var close = command.IndexOf('"', 1);
            if (close < 0) return;
            program = command[1..close];
            arguments = command[(close + 1)..].Trim();
        }
        else
        {
            var space = command.IndexOf(' ');
            program = space < 0 ? command : command[..space];
            arguments = space < 0 ? "" : command[(space + 1)..].Trim();
        }
        try
        {
            Process.Start(new ProcessStartInfo(program, arguments) { UseShellExecute = false });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
        }
    }

    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The name a program gives itself, such as "Visual Studio Code"; its file name if it gives none.</summary>
    public static string AppName(string path)
    {
        if (Names.TryGetValue(path, out var known)) return known;
        var name = "";
        try
        {
            name = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim() ?? "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
        if (name.Length == 0) name = System.IO.Path.GetFileNameWithoutExtension(path);
        // Windows still calls File Explorer by its old name.
        if (name == "Windows Explorer") name = "File Explorer";
        return Names[path] = name;
    }

    public static void Focus(IntPtr window)
    {
        if (IsIconic(window)) ShowWindow(window, 9 /* restore */);
        SetForegroundWindow(window);
    }

    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder path, ref uint size);

    public static string? ProcessPath(uint process)
    {
        var handle = OpenProcess(0x1000 /* query limited information */, false, process);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var path = new StringBuilder(1024);
            var size = (uint)path.Capacity;
            return QueryFullProcessImageNameW(handle, 0, path, ref size) ? path.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    // ---------- Icons ----------

    private static readonly Dictionary<string, ImageSource?> Icons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The icon of a file, an icon file ("path" or "path,index"), or a shell item such as a Store app,
    /// remembered after the first time. Windows is asked for a large one so it stays sharp on a tile.
    /// </summary>
    public static ImageSource? Icon(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (Icons.TryGetValue(path, out var known)) return known;
        var file = path;
        // "C:\icons\app.ico,0": the part after the comma picks an icon inside the file.
        var comma = file.LastIndexOf(',');
        if (comma > 2 && !File.Exists(file) && File.Exists(file[..comma])) file = file[..comma];
        var image = ShellIcon(file, 48);
        if (image is null && File.Exists(file))
        {
            try
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(file);
                if (icon is not null)
                {
                    image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    image.Freeze();
                }
            }
            catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
            }
        }
        return Icons[path] = image;
    }

    /// <summary>A running app's icon: its own (a web app's, not the browser's), else the one its front window shows.</summary>
    public static ImageSource? AppIcon(ActiveApp app) => Icon(app.IconSource) ?? WindowIcon(app.Windows[0].Handle);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static extern IntPtr SendMessageTimeoutPlain(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] private static extern IntPtr GetClassLongPtr(IntPtr window, int index);

    /// <summary>The icon a window shows in its title bar and on the taskbar.</summary>
    public static ImageSource? WindowIcon(IntPtr window)
    {
        var icon = IntPtr.Zero;
        // Big icon first, then the small one, then the one its window class was given.
        foreach (var which in new[] { 1, 2, 0 })
        {
            if (SendMessageTimeoutPlain(window, 0x007F, new IntPtr(which), IntPtr.Zero, 2 /* give up if hung */, 100, out var answer) != IntPtr.Zero && answer != IntPtr.Zero)
            {
                icon = answer;
                break;
            }
        }
        if (icon == IntPtr.Zero) icon = GetClassLongPtr(window, -14);
        if (icon == IntPtr.Zero) return null;
        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is COMException or ArgumentException)
        {
            return null;
        }
    }

    // ----- Large icons from the shell -----

    [StructLayout(LayoutKind.Sequential)]
    private struct IconSize
    {
        public int Width, Height;
    }

    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(IconSize size, int flags, out IntPtr bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? item);

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public int Type, Width, Height, Stride;
        public ushort Planes, BitsPerPixel;
        public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DibSection
    {
        public BitmapInfo Bitmap;
        // The bitmap's header: only the height's sign matters here (positive means stored bottom row first).
        public uint HeaderSize;
        public int HeaderWidth, HeaderHeight;
        public ushort HeaderPlanes, HeaderBits;
        public uint Compression, SizeImage;
        public int PerMeterX, PerMeterY;
        public uint ColoursUsed, ColoursImportant;
        public uint Mask0, Mask1, Mask2;
        public IntPtr Section;
        public uint Offset;
    }

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")] private static extern int GetObject(IntPtr handle, int size, ref DibSection info);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

    /// <summary>Where Windows saves screenshots (Pictures\Screenshots, wherever you moved it). Null if unknown.</summary>
    public static string? ScreenshotsFolder()
    {
        var screenshots = new Guid("B7BEDE81-DF94-4682-A7D8-57A52620B86F");
        if (SHGetKnownFolderPath(screenshots, 0, IntPtr.Zero, out var raw) != 0 || raw == IntPtr.Zero) return null;
        try
        {
            return Marshal.PtrToStringUni(raw);
        }
        finally
        {
            Marshal.FreeCoTaskMem(raw);
        }
    }

    /// <summary>An installed app's icon, from its app id (as Start knows it).</summary>
    public static ImageSource? AppIdIcon(string appId, int size = 32) =>
        string.IsNullOrEmpty(appId) ? null : ShellIcon(@"shell:AppsFolder\" + appId, size);

    private static ImageSource? ShellIcon(string path, int size)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        IShellItemImageFactory? item = null;
        var bitmap = IntPtr.Zero;
        try
        {
            if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out item) != 0 || item is null) return null;
            // Icon only (never a preview of the file's contents); a larger one is fine.
            // Exactly this size: asking for 'bigger is fine' gets small icons padded onto a framed canvas.
            if (item.GetImage(new IconSize { Width = size, Height = size }, 0x4, out bitmap) != 0 || bitmap == IntPtr.Zero) return null;
            var info = new DibSection();
            var got = GetObject(bitmap, Marshal.SizeOf<DibSection>(), ref info);
            BitmapSource image;
            if (got >= Marshal.SizeOf<DibSection>() && info.Bitmap.BitsPerPixel == 32 && info.Bitmap.Bits != IntPtr.Zero)
            {
                // Read the pixels directly: the usual conversion throws the transparency away.
                // Windows hands some icons over premultiplied and some not. A colour brighter than its own
                // alpha can only be the second kind; reading those as premultiplied gives them a white rim.
                var length = info.Bitmap.Stride * info.Bitmap.Height;
                var pixels = new byte[length];
                Marshal.Copy(info.Bitmap.Bits, pixels, 0, length);
                var straight = false;
                for (var i = 0; i + 3 < length && !straight; i += 4)
                {
                    var a = pixels[i + 3];
                    straight = pixels[i] > a || pixels[i + 1] > a || pixels[i + 2] > a;
                }
                image = BitmapSource.Create(info.Bitmap.Width, info.Bitmap.Height, 96, 96, straight ? PixelFormats.Bgra32 : PixelFormats.Pbgra32, null, pixels, info.Bitmap.Stride);
                if (info.HeaderHeight > 0) image = new TransformedBitmap(image, new ScaleTransform(1, -1));
                // Copy, since the pixels belong to the bitmap that is deleted below.
                image = new WriteableBitmap(image);
            }
            else
            {
                image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is COMException or InvalidCastException or ArgumentException or IOException or UnauthorizedAccessException or OutOfMemoryException)
        {
            return null;
        }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (item is not null) Marshal.ReleaseComObject(item);
        }
    }

    // ---------- Pinned apps ----------

    /// <summary>The shortcuts pinned to the Windows taskbar: name and shortcut path.</summary>
    public static List<(string Name, string Shortcut)> PinnedApps(int limit)
    {
        var pinned = new List<(string, string)>();
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
        try
        {
            foreach (var shortcut in Directory.EnumerateFiles(folder, "*.lnk"))
            {
                if (pinned.Count >= limit) break;
                pinned.Add((Path.GetFileNameWithoutExtension(shortcut), shortcut));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return pinned;
    }

    public static void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
        }
    }

    // ---------- Desktop, recycle bin, theme, language ----------

    public static void ShowDesktop()
    {
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
            shell.ToggleDesktop();
        }
        catch (Exception)
        {
            // The shell would not answer; nothing to show for it.
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct RecycleBinInfo
    {
        public uint Size;
        public long Bytes;
        public long Items;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHQueryRecycleBinW(string? root, ref RecycleBinInfo info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHEmptyRecycleBinW(IntPtr owner, string? root, uint flags);

    /// <summary>How many items are in the recycle bin, or -1 if Windows will not say.</summary>
    public static long RecycleBinItems()
    {
        var info = new RecycleBinInfo { Size = (uint)Marshal.SizeOf<RecycleBinInfo>() };
        return SHQueryRecycleBinW(null, ref info) == 0 ? info.Items : -1;
    }

    /// <summary>Empty the recycle bin. Windows asks the user to confirm first.</summary>
    public static void EmptyRecycleBin() => SHEmptyRecycleBinW(IntPtr.Zero, null, 0);

    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Personalize);
        return key?.GetValue("AppsUseLightTheme") is int light && light != 0;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeoutW(IntPtr window, uint message, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

    /// <summary>Switch Windows and its apps between light and dark.</summary>
    public static void ToggleTheme()
    {
        var light = IsLightTheme() ? 0 : 1;
        using (var key = Registry.CurrentUser.CreateSubKey(Personalize))
        {
            key.SetValue("AppsUseLightTheme", light, RegistryValueKind.DWord);
            key.SetValue("SystemUsesLightTheme", light, RegistryValueKind.DWord);
        }
        // Tell running apps the colours changed.
        SendMessageTimeoutW(new IntPtr(0xFFFF), 0x001A, IntPtr.Zero, "ImmersiveColorSet", 2, 200, out _);
    }

    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);

    /// <summary>The typing language of the window in front, as a short code such as "EN".</summary>
    public static string InputLanguage()
    {
        var thread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var language = (int)((long)GetKeyboardLayout(thread) & 0xFFFF);
        try
        {
            return new System.Globalization.CultureInfo(language).TwoLetterISOLanguageName.ToUpperInvariant();
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            return "??";
        }
    }

    /// <summary>Ask the window in front to switch to the next typing language.</summary>
    public static void NextInputLanguage() => PostMessageW(GetForegroundWindow(), 0x0050, new IntPtr(2), IntPtr.Zero);

    // ---------- Clipboard ----------

    [DllImport("user32.dll", SetLastError = true)] private static extern bool AddClipboardFormatListener(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RemoveClipboardFormatListener(IntPtr window);

    /// <summary>Have Windows tell a window each time the clipboard changes (or stop).</summary>
    public static bool ListenToClipboard(IntPtr window, bool on) => window != IntPtr.Zero && (on ? AddClipboardFormatListener(window) : RemoveClipboardFormatListener(window));

    /// <summary>The window in front, if it belongs to another app; else zero.</summary>
    public static IntPtr ForeignForeground()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return IntPtr.Zero;
        GetWindowThreadProcessId(window, out var process);
        return process == (uint)Environment.ProcessId ? IntPtr.Zero : window;
    }

    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);

    /// <summary>Bring a window to the front and press Ctrl+V in it.</summary>
    public static async void PasteInto(IntPtr window)
    {
        if (window == IntPtr.Zero || !IsWindow(window)) return;
        if (IsIconic(window)) ShowWindow(window, 9 /* restore */);
        SetForegroundWindow(window);
        // Give the window a moment to take the keyboard.
        await System.Threading.Tasks.Task.Delay(90);
        if (GetForegroundWindow() != window) return;
        const byte control = 0x11, v = 0x56;
        const uint up = 0x2;
        keybd_event(control, 0, 0, UIntPtr.Zero);
        keybd_event(v, 0, 0, UIntPtr.Zero);
        keybd_event(v, 0, up, UIntPtr.Zero);
        keybd_event(control, 0, up, UIntPtr.Zero);
    }

    // ---------- Processes that end with the app ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct JobLimits
    {
        public long PerProcessTime, PerJobTime;
        public uint Flags;
        public UIntPtr MinimumSet, MaximumSet;
        public uint ActiveProcesses;
        public UIntPtr Affinity;
        public uint Priority, SchedulingClass;
        // The I/O counters and memory limits of the extended form.
        public ulong Reads, Writes, Others, ReadBytes, WriteBytes, OtherBytes;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObjectW(IntPtr security, string? name);
    [DllImport("kernel32.dll")] private static extern bool SetInformationJobObject(IntPtr job, int kind, ref JobLimits limits, uint size);
    [DllImport("kernel32.dll")] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    private static readonly Lazy<IntPtr> Job = new(() =>
    {
        var job = CreateJobObjectW(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return IntPtr.Zero;
        // Everything in the job ends when the app's handle to it closes, which happens however the app ends.
        var limits = new JobLimits { Flags = 0x2000 /* kill on job close */ };
        SetInformationJobObject(job, 9 /* extended limits */, ref limits, (uint)Marshal.SizeOf<JobLimits>());
        return job;
    });

    /// <summary>Make a process (and what it starts) end when this app ends.</summary>
    public static void KeepWithApp(Process process)
    {
        try
        {
            if (Job.Value != IntPtr.Zero) AssignProcessToJobObject(Job.Value, process.Handle);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    // ---------- Screen time ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInput
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput info);

    /// <summary>How long since the keyboard or mouse was last used.</summary>
    public static TimeSpan IdleTime()
    {
        var info = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
        return GetLastInputInfo(ref info) ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time)) : TimeSpan.Zero;
    }

    /// <summary>The name of the app in front, such as "Code", or null.</summary>
    public static string? ForegroundApp()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var process);
        if (process == 0 || process == (uint)Environment.ProcessId) return null;
        var path = ProcessPath(process);
        return path is null ? null : Path.GetFileNameWithoutExtension(path);
    }
}
