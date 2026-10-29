using System.Runtime.InteropServices;
using System.Windows.Interop;
using PawnAppraiser.Core;

namespace PawnAppraiser.Desktop;

public sealed class HotkeyService : IDisposable
{
    private readonly nint handle;
    private readonly HwndSource source;
    private readonly DiagnosticLog log;
    private readonly HashSet<int> registered = [];
    public event Action? Collapse;
    public event Action? Visibility;
    public HotkeyService(nint handle, DiagnosticLog log)
    { this.handle = handle; this.log = log; source = HwndSource.FromHwnd(handle)!; source.AddHook(Hook); }
    public string[] Bind(Hotkey collapse, Hotkey visibility)
    {
        Release(); var errors = new List<string>();
        void Register(int id, string label, Hotkey key)
        {
            if (NativeMethods.RegisterHotKey(handle, id, key.Modifiers | 0x4000, key.VirtualKey))
            { registered.Add(id); log.Write($"HOTKEY registered {id} VK={key.VirtualKey:X} modifiers={key.Modifiers:X}"); }
            else { var error = Marshal.GetLastWin32Error(); errors.Add($"{label}快捷键 {key.Display} 注册失败（{error}）；请在设置中改键"); log.Write(errors[^1]); }
        }
        Register(1, "展开/收起", collapse); Register(2, "显示/隐藏", visibility);
        return errors.ToArray();
    }
    private nint Hook(nint hwnd, int msg, nint w, nint l, ref bool handled)
    {
        if (msg == 0x312 && registered.Contains((int)w))
        { handled = true; log.Write($"HOTKEY received {w}"); if (w == 1) Collapse?.Invoke(); else if (w == 2) Visibility?.Invoke(); }
        return 0;
    }
    private void Release()
    {
        foreach (var id in registered) log.Write($"HOTKEY unregister {id}: {NativeMethods.UnregisterHotKey(handle, id)}");
        registered.Clear();
    }
    public void Dispose() { Release(); source.RemoveHook(Hook); }
}

public sealed class TrayService : IDisposable
{
    private const uint Callback = 0x8000 + 20;
    private readonly nint handle;
    private readonly HwndSource source;
    private readonly uint taskbarCreated;
    private NativeMethods.NotifyIconData data;
    private readonly Action<int> action;
    private readonly DiagnosticLog log;
    public bool Available { get; private set; }
    public event Action<bool>? AvailabilityChanged;
    public TrayService(nint handle, Action<int> action, DiagnosticLog log)
    {
        this.handle = handle; this.action = action; this.log = log;
        source = HwndSource.FromHwnd(handle)!; source.AddHook(Hook);
        taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        data = new() { Size = (uint)Marshal.SizeOf<NativeMethods.NotifyIconData>(), Window = handle, Id = 1,
            Flags = 1 | 2 | 4, Callback = Callback, Icon = NativeMethods.LoadIcon(0, (nint)32512),
            Tip = "深空当铺鉴定助手 · 右键菜单", Info = "", InfoTitle = "" };
        Add();
    }
    private void Add()
    {
        Available = NativeMethods.Shell_NotifyIcon(0, ref data);
        log.Write($"TRAY add: {Available}"); AvailabilityChanged?.Invoke(Available);
    }
    private nint Hook(nint hwnd, int msg, nint w, nint l, ref bool handled)
    {
        if ((uint)msg == taskbarCreated) Add();
        if ((uint)msg == Callback)
        {
            if ((int)l == 0x203) action(1); // Double left click: show/hide
            else if ((int)l == 0x205 || (int)l == 0x7B) ShowMenu();
            handled = true;
        }
        return 0;
    }
    private void ShowMenu()
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == 0) return;
        try
        {
            NativeMethods.AppendMenu(menu, 0, 1, "显示 / 隐藏");
            NativeMethods.AppendMenu(menu, 0, 2, "展开 / 收起");
            NativeMethods.AppendMenu(menu, 0, 3, "设置…");
            NativeMethods.AppendMenu(menu, 0x800, 0, "");
            NativeMethods.AppendMenu(menu, 0, 4, "退出程序");
            NativeMethods.GetCursorPos(out var point); NativeMethods.SetForegroundWindow(handle);
            var command = NativeMethods.TrackPopupMenu(menu, 0x100 | 0x80 | 0x2, point.X, point.Y, 0, handle, 0);
            NativeMethods.PostMessage(handle, 0, 0, 0);
            if (command != 0) action((int)command);
        }
        finally { NativeMethods.DestroyMenu(menu); }
    }
    public void Dispose()
    {
        log.Write($"TRAY delete: {NativeMethods.Shell_NotifyIcon(2, ref data)}");
        Available = false; source.RemoveHook(Hook);
    }
}

public sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex = new(false, @"Local\PawnAppraiser_v1_Mutex");
    private readonly EventWaitHandle signal = new(false, EventResetMode.AutoReset, @"Local\PawnAppraiser_v1_Show");
    private RegisteredWaitHandle? wait;
    public bool IsOwner { get; }
    public SingleInstance()
    {
        try { IsOwner = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsOwner = true; }
        if (!IsOwner) signal.Set();
    }
    public void Listen(Action show) => wait = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => show(), null, Timeout.Infinite, false);
    public void Dispose()
    { wait?.Unregister(null); if (IsOwner) mutex.ReleaseMutex(); signal.Dispose(); mutex.Dispose(); }
}
