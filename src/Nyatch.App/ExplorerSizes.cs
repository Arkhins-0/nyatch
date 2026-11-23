using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
using Nyatch.Core;

namespace Nyatch.App;

/// <summary>
/// File Explorer folder sizes and size units (Nyatch.Explorer.dll, the "Better file sizes in Explorer
/// details" code). While it is switched on, the DLL is loaded into each explorer.exe of this session and
/// started there; it is stopped and unloaded when switched off, when its settings change (then started
/// again) and when Nyatch quits. The private Windows functions it needs are looked up here first, from
/// Microsoft's public symbols, so Explorer never downloads anything.
/// </summary>
internal sealed class ExplorerSizes
{
    private const string DllName = "Nyatch.Explorer.dll";

    private readonly SettingsStore _settings;
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromSeconds(5) };
    // Explorer process -> the settings it was started with.
    private readonly Dictionary<int, string> _running = new();
    // When each was started, so an Explorer that closes right after is noticed.
    private readonly Dictionary<int, DateTime> _startedAt = new();
    // Set when an Explorer closed soon after starting this: not started again with the same settings.
    private string? _gaveUpOn;
    private string? _copy;          // the DLL copy Explorer loads (never the installed file)
    private bool _prepared;
    private string _lastExplorers = "";
    private DateTime _lastFullCheck = DateTime.MinValue;
    private bool _busy;

    public ExplorerSizes(SettingsStore settings)
    {
        _settings = settings;
        // Cheap on the timer: only when Explorer processes come or go (or once a minute, so turning off
        // Windhawk's copy is noticed) is every Explorer looked into again.
        _watch.Tick += async (_, _) =>
        {
            var explorers = string.Join(",", Explorers().Select(p => { using (p) return p.Id; }).OrderBy(id => id));
            if (explorers == _lastExplorers && DateTime.UtcNow - _lastFullCheck < TimeSpan.FromMinutes(1)) return;
            await ApplyAsync();
        };
    }

    /// <summary>Why it is not working, when it is not; empty otherwise.</summary>
    public string Problem { get; private set; } = "";

    /// <summary>How many File Explorer processes it is working in now.</summary>
    public int RunningIn
    {
        get
        {
            lock (_running) return _running.Count;
        }
    }

    /// <summary>Raised (on any thread) after a start, stop or problem, for the settings page.</summary>
    public event Action? Changed;

    private bool Enabled => _settings.Get("Explorer.Enabled") == "true";

    private string Signature => string.Join("|", _settings.Schema.All.Where(s => s.FullName.StartsWith("Explorer.", StringComparison.Ordinal)).Select(s => _settings.Get(s.FullName)));

    /// <summary>Bring every Explorer in line with the settings: started, restarted or stopped.</summary>
    public async Task ApplyAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (!Enabled)
            {
                _watch.Stop();
                await Task.Run(StopAll);
                Problem = "";
                return;
            }
            _watch.Start();
            if (!_prepared)
            {
                var (ok, message) = await Task.Run(Prepare);
                if (!ok)
                {
                    Problem = message;
                    return;
                }
                _prepared = true;
            }
            var signature = Signature;
            if (_gaveUpOn == signature) return;
            _gaveUpOn = null;
            Problem = "";
            _lastFullCheck = DateTime.UtcNow;
            _lastExplorers = string.Join(",", Explorers().Select(p => { using (p) return p.Id; }).OrderBy(id => id));
            await Task.Run(() => StartAll(signature));
        }
        catch (Exception e) when (e is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            Problem = e.Message;
        }
        finally
        {
            _busy = false;
            Changed?.Invoke();
        }
    }

    /// <summary>Take it out of every Explorer, at quit.</summary>
    public void StopAll()
    {
        foreach (var process in Explorers())
        {
            using (process) Stop(process.Id);
        }
        lock (_running) _running.Clear();
    }

    // ---------- Steps ----------

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int PrepareFunction([Out] char[] message, int chars);

    private (bool, string) Prepare()
    {
        var path = LocalCopy();
        var library = NativeLibrary.Load(path);
        var prepare = Marshal.GetDelegateForFunctionPointer<PrepareFunction>(NativeLibrary.GetExport(library, "NyatchExplorerPrepare"));
        var message = new char[512];
        var ok = prepare(message, message.Length) != 0;
        return (ok, ok ? "" : new string(message).TrimEnd('\0'));
    }

    private void StartAll(string signature)
    {
        var path = LocalCopy();
        var (startRva, stopRva) = Exports(path);
        var live = Explorers().Select(p => { using (p) return p.Id; }).ToHashSet();
        lock (_running)
        {
            // An Explorer this was started in that has closed within a minute and a half: most likely it
            // crashed, so it is not started again (the new Explorer would only crash the same way).
            if (_startedAt.Any(started => !live.Contains(started.Key) && DateTime.UtcNow - started.Value < TimeSpan.FromSeconds(90)))
            {
                _startedAt.Clear();
                _running.Clear();
                _gaveUpOn = signature;
                Problem = "File Explorer closed right after folder sizes started, so they were turned off. Change a setting here to try again.";
                return;
            }
            foreach (var gone in _startedAt.Keys.Where(id => !live.Contains(id)).ToList()) _startedAt.Remove(gone);
        }
        foreach (var process in Explorers())
        {
            using (process)
            {
                // The Windhawk mod this came from, still on: it already does all this, so Nyatch stays out.
                if (HasModule(process.Id, "explorer-details-better-file-sizes"))
                {
                    Stop(process.Id);
                    Problem = "Windhawk's \"Better file sizes in Explorer details\" mod is on and already does this. Turn it off in Windhawk and Nyatch takes over.";
                    continue;
                }
                string? had;
                lock (_running) _running.TryGetValue(process.Id, out had);
                if (had == signature && ModuleBase(process.Id, path) != IntPtr.Zero) continue;
                Stop(process.Id);  // old settings, or a copy left from before: out first
                var moduleBase = Load(process.Id, path);
                if (moduleBase == IntPtr.Zero) continue;
                if (RunRemote(process.Id, moduleBase + startRva, 15000) == 1)
                {
                    lock (_running)
                    {
                        _running[process.Id] = signature;
                        _startedAt[process.Id] = DateTime.UtcNow;
                    }
                }
                else
                {
                    RunRemote(process.Id, moduleBase + stopRva, 15000);
                    Problem = "Explorer folder sizes could not start in this Windows version.";
                }
            }
        }
        lock (_running)
        {
            foreach (var gone in _running.Keys.Where(id => !live.Contains(id)).ToList()) _running.Remove(gone);
        }
    }

    private void Stop(int pid)
    {
        var path = LocalCopy();
        var moduleBase = ModuleBase(pid, path);
        if (moduleBase != IntPtr.Zero)
        {
            var (_, stopRva) = Exports(path);
            RunRemote(pid, moduleBase + stopRva, 15000);
        }
        lock (_running) _running.Remove(pid);
    }

    /// <summary>
    /// The DLL Explorer loads: a copy under %LOCALAPPDATA%\Nyatch\Explorer named by its hash, so the
    /// installed file is never held open by Explorer and an update can always replace it.
    /// </summary>
    private string LocalCopy()
    {
        if (_copy is not null && File.Exists(_copy)) return _copy;
        var source = Path.Combine(AppContext.BaseDirectory, DllName);
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)))[..16];
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nyatch", "Explorer");
        Directory.CreateDirectory(folder);
        var copy = Path.Combine(folder, $"Nyatch.Explorer-{hash}.dll");
        if (!File.Exists(copy))
        {
            File.Copy(source, copy + ".part", overwrite: true);
            File.Move(copy + ".part", copy, overwrite: true);
        }
        // Older copies that no Explorer holds any more.
        foreach (var old in Directory.EnumerateFiles(folder, "Nyatch.Explorer-*.dll").Where(f => !string.Equals(f, copy, StringComparison.OrdinalIgnoreCase)))
        {
            try { File.Delete(old); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return _copy = copy;
    }

    private static (IntPtr start, IntPtr stop) Exports(string path)
    {
        var library = NativeLibrary.Load(path);
        return (NativeLibrary.GetExport(library, "NyatchExplorerStart") - library, NativeLibrary.GetExport(library, "NyatchExplorerStop") - library);
    }

    private static IEnumerable<Process> Explorers()
    {
        var session = Process.GetCurrentProcess().SessionId;
        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            if (process.SessionId == session) yield return process;
            else process.Dispose();
        }
    }

    // ---------- Into another process ----------

    private const uint ProcessAccess = 0x0002 | 0x0008 | 0x0010 | 0x0020 | 0x0400; // create thread, VM operation/read/write, query
    private const uint MemCommitReserve = 0x3000, MemRelease = 0x8000, PageReadWrite = 0x04;

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size, uint type, uint protect);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, UIntPtr size, uint type);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr written);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateRemoteThread(IntPtr process, IntPtr attributes, UIntPtr stack, IntPtr start, IntPtr parameter, uint flags, out uint threadId);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")] private static extern bool GetExitCodeThread(IntPtr thread, out uint code);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool K32EnumProcessModulesEx(IntPtr process, [Out] IntPtr[] modules, uint size, out uint needed, uint filter);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint K32GetModuleFileNameExW(IntPtr process, IntPtr module, StringBuilder name, uint size);

    /// <summary>Load the DLL into the process (LoadLibraryW on a thread of its own there). Its base address, or zero.</summary>
    private static IntPtr Load(int pid, string path)
    {
        var process = OpenProcess(ProcessAccess, false, pid);
        if (process == IntPtr.Zero) return IntPtr.Zero;
        try
        {
            var bytes = Encoding.Unicode.GetBytes(path + "\0");
            var remote = VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)bytes.Length, MemCommitReserve, PageReadWrite);
            if (remote == IntPtr.Zero) return IntPtr.Zero;
            try
            {
                if (!WriteProcessMemory(process, remote, bytes, (UIntPtr)bytes.Length, out _)) return IntPtr.Zero;
                // kernel32 is at the same address in every process of this boot.
                var loadLibrary = GetProcAddress(GetModuleHandleW("kernel32.dll"), "LoadLibraryW");
                var thread = CreateRemoteThread(process, IntPtr.Zero, UIntPtr.Zero, loadLibrary, remote, 0, out _);
                if (thread == IntPtr.Zero) return IntPtr.Zero;
                WaitForSingleObject(thread, 15000);
                CloseHandle(thread);
            }
            finally
            {
                VirtualFreeEx(process, remote, UIntPtr.Zero, MemRelease);
            }
            return FindModule(process, path);
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static IntPtr ModuleBase(int pid, string path)
    {
        var process = OpenProcess(ProcessAccess, false, pid);
        if (process == IntPtr.Zero) return IntPtr.Zero;
        try
        {
            return FindModule(process, path);
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static bool HasModule(int pid, string nameContains)
    {
        var process = OpenProcess(ProcessAccess, false, pid);
        if (process == IntPtr.Zero) return false;
        try
        {
            return FindModule(process, nameContains, contains: true) != IntPtr.Zero;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static IntPtr FindModule(IntPtr process, string path, bool contains = false)
    {
        var modules = new IntPtr[1024];
        if (!K32EnumProcessModulesEx(process, modules, (uint)(IntPtr.Size * modules.Length), out var needed, 0x03)) return IntPtr.Zero;
        var count = Math.Min(modules.Length, (int)(needed / IntPtr.Size));
        var name = new StringBuilder(1024);
        for (var i = 0; i < count; i++)
        {
            name.Clear();
            if (K32GetModuleFileNameExW(process, modules[i], name, (uint)name.Capacity) > 0 &&
                (contains ? name.ToString().Contains(path, StringComparison.OrdinalIgnoreCase)
                          : string.Equals(name.ToString(), path, StringComparison.OrdinalIgnoreCase)))
            {
                return modules[i];
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>Run one of the DLL's exports as a thread in the process. Its exit code, or uint.MaxValue.</summary>
    private static uint RunRemote(int pid, IntPtr start, uint timeout)
    {
        var process = OpenProcess(ProcessAccess, false, pid);
        if (process == IntPtr.Zero) return uint.MaxValue;
        try
        {
            var thread = CreateRemoteThread(process, IntPtr.Zero, UIntPtr.Zero, start, IntPtr.Zero, 0, out _);
            if (thread == IntPtr.Zero) return uint.MaxValue;
            try
            {
                if (WaitForSingleObject(thread, timeout) != 0) return uint.MaxValue;
                return GetExitCodeThread(thread, out var code) ? code : uint.MaxValue;
            }
            finally
            {
                CloseHandle(thread);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }
}
