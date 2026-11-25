using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nyatch.App;

// The second notch's outline is drawn by the island (native, like the top notch): closed, it shows the
// date or a running timer; it opens on hover or click, hides while Alt is held, and keeps a hidden
// taskbar down. This window only shows the pages, placed exactly over the open outline. While the
// notch is closed it is empty and lets the pointer through.
internal sealed partial class DeckWindow
{
    private const int GwlExStyleNative = -20;
    private const long WsExTransparentNative = 0x20;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetStyleNative(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetStyleNative(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);

    // Alpha 1: invisible over the island's outline, but the pointer still lands on the pages.
    private static readonly SolidColorBrush Catcher = Freeze(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)));

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    /// <summary>The code the island sends with a click for the page it was made on (its PageCode, worked out alike).</summary>
    private static int PageCode(string page)
    {
        uint h = 17;
        foreach (var c in page) h = unchecked(h * 31u + c);
        return (int)(h & 0x3FFFFFFFu) | 1;
    }

    private void StartNativeOutline()
    {
        Island.DeckSetHost((what, x, y) => Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            switch (what)
            {
                case 1:
                    Expand();
                    break;
                case 2:
                    ShowMenu();
                    break;
                case 3:
                    Collapse();
                    HideOverNative();  // at once, so what is behind can be clicked
                    break;
                case 4:
                    Collapse();
                    break;
                case 5:
                    // A page dot on a page the island draws.
                    if (_expanded && x >= 0 && x < _turnCount) ShowTab(x, x > _tab ? 1 : -1);
                    break;
                case 7:
                    // A button on a page the island draws. A click made on a page that is no longer on show is dropped:
                    // its number would be taken as one of this page's buttons (a click on Notes starting the timer).
                    if (_tab < 0 || _tab >= _tabs.Count) break;
                    if (y != 0 && y != PageCode(_tabs[_tab].Native ?? "")) break;
                    if (_tabs[_tab].Native == "focus") FocusAction(x);
                    else if (_tabs[_tab].Native == "apps") AppsAction(x);
                    else if (_tabs[_tab].Native == "clipboard") ClipboardAction(x);
                    else if (_tabs[_tab].Native == "notes") NotesAction(x);
                    else if (_tabs[_tab].Native == "terminal") TerminalAction(x);
                    else if (_tabs[_tab].Native == "online") OnlineAction(x);
                    else if (_tabs[_tab].Native == "keys") KeysAction(x);
                    else if (DeckAi is not null) AiAction(x);
                    break;
                case 9:
                    // A text box on a page the island draws: x is the box, y what happened.
                    if (x == 1) FocusEdit(y);
                    else if (x is >= 10 and < 20) NotesEdit(x, y);
                    else if (x is >= 20 and < 30) TerminalEdit(x, y);
                    else if (x == 30) OnlineEdit(x, y);
                    else if (x == 40 && !SideOn) AiEdit(x, y);
                    break;
                case 8:
                    // Files dragged over the notch: open on the Tray page.
                    var tray = _tabs.FindIndex(t => t.Native == "tray");
                    if (tray < 0) break;
                    if (_expanded && _tab != tray) ShowTab(tray, tray > _tab ? 1 : -1);
                    else if (!_expanded)
                    {
                        _tab = tray;
                        Expand();
                    }
                    break;
                case 6:
                    // The wheel turned past the end of a page the island draws.
                    if (_expanded && _tabs.Count > 0) ShowTab(TurnFrom(_tab, x > 0), x > 0 ? 1 : -1);
                    break;
                case 11:
                    // The top notch's Focus Timer menu: it works the Focus page's timer.
                    FocusMenu(x);
                    break;
                case 12:
                    // The ringing timer's or alarm's buttons on the top notch: 1 again (or snooze), 2 stop.
                    _ringer?.Press(again: x == 1);
                    break;
            }
        }));
        SourceInitialized += (_, _) =>
        {
            SetPassThrough(true);
            Island.DeckSetPages(new WindowInteropHelper(this).Handle);
        };
        _pill.Visibility = Visibility.Hidden;
    }

    /// <summary>
    /// Typing in a text box (a note, a to-do, the terminal) keeps the notch open even with the pointer
    /// away; a click on a button only takes the keyboard, and does not.
    /// </summary>
    private void HoldWhileTyping()
    {
        var typing = _expanded && IsKeyboardFocusWithin && Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.PasswordBox;
        Island.DeckHold(typing);
    }

    /// <summary>Line the pages up with the island's open outline.</summary>
    private void PlaceOverNative()
    {
        if (Island.DeckOpenRect() is not { } rect) return;
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (scale <= 0) scale = 1;
        _pill.HorizontalAlignment = HorizontalAlignment.Left;
        _pill.VerticalAlignment = VerticalAlignment.Top;
        Left = rect.Left / scale - Margin_;
        Top = rect.Top / scale - Margin_;
    }

    private void ShowOverNative()
    {
        Island.DeckOpen(true);
        PlaceOverNative();
        // On a page the island draws, this window stays out of the way.
        var native = _tab >= 0 && _tab < _tabs.Count && _tabs[_tab].Native is not null;
        _pill.Visibility = native ? Visibility.Hidden : Visibility.Visible;
        SetPassThrough(native);
        // Above the island's outline.
        var handle = new WindowInteropHelper(this).Handle;
        Island.DeckSetPages(handle);
        if (handle != IntPtr.Zero) SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
    }

    /// <summary>
    /// Tell the island which page is on show. One it draws itself: this window hides its pages and lets
    /// the pointer through, so the island gets the clicks and the wheel. One of ours: back on top.
    /// </summary>
    private void ShowNativePage(Page page)
    {
        // The dots are for the pages on the notch: none is lit on a hotkey-only page, and the picker has none.
        Island.DeckShowPage(page.Native ?? "", _tab < _turnCount ? _tab : -1, page.Native == "keys" ? 0 : _turnCount);
        SendNativeData();
        if (!_expanded) return;
        var native = page.Native is not null;
        _pill.Visibility = native ? Visibility.Hidden : Visibility.Visible;
        SetPassThrough(native);
        if (native)
        {
            Keyboard.ClearFocus();
            Island.DeckHold(false);
        }
    }

    /// <summary>What the native page on show needs from here (the Focus page's clocks), while it is open.</summary>
    /// <param name="tick">From the twice-a-second clock: only pages that change on their own (Focus) are sent then.</param>
    private void SendNativeData(bool tick = false)
    {
        if (!_expanded || _tab < 0 || _tab >= _tabs.Count) return;
        switch (_tabs[_tab].Native)
        {
            case "focus":
                Island.DeckSetPageData(FocusData());
                break;
            case "apps" when !tick:
                Island.DeckSetPageData(AppsData());
                break;
            case "clipboard" when !tick || _clipSent != _clipVersion:
                Island.DeckSetPageData(ClipboardData());
                break;
            case "notes" when !tick:
                Island.DeckSetPageData(NotesData());
                break;
            case "terminal" when !tick || _termSent != _termVersion:
                Island.DeckSetPageData(TerminalData());
                break;
            case "online" when !tick:
                Island.DeckSetPageData(OnlineData());
                break;
            case "keys" when !tick:
                Island.DeckSetPageData(KeysData());
                break;
            case { } id when !tick && _aiPages.TryGetValue(id, out var chat):
                Island.DeckSetPageData(AiData(chat));
                break;
        }
    }

    private void HideOverNative()
    {
        _pill.Visibility = Visibility.Hidden;
        SetPassThrough(true);
    }

    /// <summary>The island draws the outline, so this window draws none: only the pages, clipped to its shape.</summary>
    private void PaintOverNative(double opened)
    {
        _pill.Width = OpenWidth;
        _pill.Height = OpenHeight;
        _pill.Background = Catcher;
        _pill.BorderThickness = new Thickness(0);
        _pill.Effect = null;
        _depth.Visibility = _bloom.Visibility = Visibility.Collapsed;
        _closed.Visibility = Visibility.Collapsed;
        // The island's corner radii (at 1 DIP per unit), square against the edge for the notch shape.
        var radius = _shape == "w11" ? 8 : _shape == "notch" ? 16 : 44;
        var corners = _shape == "notch" ? (_top ? new CornerRadius(0, 0, radius, radius) : new CornerRadius(radius, radius, 0, 0)) : new CornerRadius(radius);
        _pill.CornerRadius = corners;
        _pill.Clip = Rounded(OpenWidth, OpenHeight, corners);
        // The pages come in as the island's outline reaches full size, and go first when it closes,
        // following the outline itself so they are never shown without it behind them.
        _open.Opacity = Math.Clamp((Island.DeckOpenness() - 0.55) / 0.4, 0, 1);
    }

    private void SetPassThrough(bool on)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var style = (long)GetStyleNative(handle, GwlExStyleNative);
        style = on ? style | WsExTransparentNative : style & ~WsExTransparentNative;
        SetStyleNative(handle, GwlExStyleNative, (IntPtr)style);
    }
}
