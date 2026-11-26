// The second notch's window (and the side panel's: this file is compiled twice, see island.cpp,
// with kSide false for the second notch and true for the side panel). Its own globals, thread and
// window procedure; the drawing is the Renderer's.
constexpr const wchar_t* kClassName = kSide ? L"Nyatch.Side" : L"Nyatch.Deck";
// Sizes in DIPs, the same as the app's pages.
constexpr float kClosedHeight = 36.0f;
constexpr float kOpenWidth = 448.0f;
constexpr float kOpenHeight = 336.0f;
constexpr float kPad = 28.0f;      // room around the outline for its shadow
constexpr float kEdgeGap = 8.0f;   // outline to screen edge (0 for the notch shape)
constexpr float kSideGap = 8.0f;   // outline to the side, at a left or right position
// Nyatch: the side panel: its open width, and its closed tab on the screen's edge.
constexpr float kSideOpenWidth = 440.0f;
constexpr float kSideTabWidth = 30.0f;
constexpr float kSideTabHeight = 120.0f;

enum : int { kOpened = 1, kMenu = 2, kAltHidden = 3, kClosed = 4, kGoTo = 5, kWheel = 6, kAction = 7, kDropOver = 8, kEdit = 9, kHeight = 10 };

std::mutex g_mutex;          // g_text, g_openRect
std::wstring g_text;
RECT g_openRect = {};
bool g_openRectValid = false;
std::atomic<bool> g_textDirty{true};
std::atomic<int> g_request{-1};   // from the app: 1 open, 0 close
std::atomic<bool> g_hold{false};   // from the app: keep it open (something is being typed in it)
std::atomic<float> g_openness{0.0f};  // how far open the outline is, 0 to 1, for the app's pages to follow
std::atomic<HWND> g_pagesWindow{nullptr};  // the app's window with the pages: the outline stays just under it
std::atomic<bool> g_clicked{false};
std::atomic<LPARAM> g_clickAt{0};    // where, in window px (as WM_LBUTTONUP gives it)
std::atomic<bool> g_pressed{false};     // the left button is down on the window
std::atomic<LPARAM> g_pressAt{0};
std::atomic<WPARAM> g_pressKeys{0};     // MK_CONTROL / MK_SHIFT with the press, and with the click
std::atomic<WPARAM> g_clickKeys{0};
std::atomic<bool> g_rightClicked{false};
std::atomic<LPARAM> g_rightClickAt{0};
std::atomic<bool> g_onPage{false};   // a native page is open: right-clicks are for it
// The text boxes (see Renderer::DeckEdit): the renderer, which holds the one being typed in (only
// touched on this thread); its text and what Enter sent, for the app (guarded by g_mutex); and the
// app's requests: a box to give the keyboard to, and text to put in the one being typed in.
Renderer* g_deck = nullptr;
std::wstring g_editText, g_editSubmitted;
int g_wantField = 0;
double g_wantFieldAt = 0.0;
std::vector<std::pair<int, std::wstring>> g_setField;
bool g_fieldPress = false;           // the button went down on a box: dragging selects
int g_lastFieldClick = 0;
ULONGLONG g_lastFieldClickAt = 0;
POINT g_lastFieldClickPt = {};
std::atomic<int> g_hwheel{0};        // sideways wheel (a touchpad swipe)
bool g_chatOn = false;               // a chat page is on show
bool g_keysOn = false;               // the page picker is on show: a letter picks a page, Esc closes
// Opened from the keyboard (the hotkey): it has the keyboard, Esc closes it, and so does going to
// another app; the pointer does not have to come onto it first.
std::atomic<bool> g_keyboardOpen{false};
bool g_chatPress = false;            // the button went down on its conversation: dragging selects
// Nyatch: the side panel's handle, on the open panel's edge: dragged, it sets how tall the panel is.
bool g_gripPress = false;            // the button went down on the handle (this window's thread only)
RECT g_grip = {};                    // the handle, window px; empty while closed

// The window takes the keyboard only while something is typed in it; otherwise it never takes the focus.
void SetTyping(HWND hwnd, bool typing) {
    LONG_PTR style = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
    style = typing ? (style & ~static_cast<LONG_PTR>(WS_EX_NOACTIVATE)) : (style | WS_EX_NOACTIVATE);
    SetWindowLongPtrW(hwnd, GWL_EXSTYLE, style);
    if (typing) {
        SetForegroundWindow(hwnd);
        SetFocus(hwnd);
    }
}
std::atomic<int> g_wheel{0};         // wheel turned since the last frame (WHEEL_DELTA steps)
void Notify(int what, int x, int y);
// The page the app is showing: drawn here when it is one of ours ("notifications"), else empty.
std::wstring g_page;                 // guarded by g_mutex
std::wstring g_pageData;             // what the app gives the page to show; guarded by g_mutex
std::atomic<int> g_pageIndex{0}, g_pageCount{0};
std::atomic<bool> g_pageDirty{true};
void(__stdcall* g_event)(int, int, int) = nullptr;
HANDLE g_thread = nullptr;

void Notify(int what, int x = 0, int y = 0) {
    if (auto handler = g_event) handler(what, x, y);
}

// Nyatch: a short code for the page a click was made on, sent with it, so the app can drop a click meant for
// a page that is no longer on show (it would be taken as one of the new page's buttons). Never 0 (0: no page).
// The app works out the same code (DeckWindow.PageCode).
int PageCode(const std::wstring& page) {
    uint32_t h = 17;
    for (const wchar_t c : page) h = h * 31u + static_cast<uint32_t>(c);
    return static_cast<int>(h & 0x3FFFFFFFu) | 1;
}

// Whether this window runs and the app listens to it (the top notch's Focus Timer items go to its Focus page).
bool FocusHost() { return g_event != nullptr && g_thread != nullptr; }

// Whether a box is on a page: the Focus page's, the Notes page's, the terminal's, the Online page's,
// or the reply box on each chat page.
bool ChatPage(const std::wstring& page) { return page == L"claude" || page == L"chatgpt" || page == L"gemini" || page == L"deepseek" || page == L"mistral" || page == L"grok" || page == L"groq" || page == L"perplexity" || page == L"openrouter"; }
bool FieldOnPage(int id, const std::wstring& page) {
    if (id == 40) return ChatPage(page);
    const wchar_t* on = id == 1 ? L"focus" : id >= 10 && id < 20 ? L"notes" : id >= 20 && id < 30 ? L"terminal" : id == 30 ? L"online" : L"";
    return page == on;
}

// The box under a point (window px), or 0: a button drawn over it wins.
int FieldAt(POINT p) {
    if (!g_deck || !g_onPage.load()) return 0;
    for (auto it = g_deck->deckHits_.rbegin(); it != g_deck->deckHits_.rend(); ++it) {
        if (Renderer::Inside(it->rect, p)) return it->action == Renderer::kDeckField ? static_cast<int>(it->data) : 0;
    }
    return 0;
}

void PublishEdit(int id) {
    {
        std::lock_guard lock(g_mutex);
        g_editText = g_deck->edit_.text;
    }
    Notify(kEdit, id, Renderer::kEditChanged);
}

void EndEdit(HWND hwnd) {
    if (!g_deck || !g_deck->edit_.on) return;
    const int id = g_deck->edit_.id;
    g_deck->edit_.on = false;
    g_fieldPress = false;
    SetTyping(hwnd, false);
    Notify(kEdit, id, Renderer::kEditDone);
}

void StartEdit(HWND hwnd, int id, bool selectAll) {
    if (!g_deck) return;
    if (g_deck->edit_.on && g_deck->edit_.id == id) {
        SetFocus(hwnd);
        return;
    }
    EndEdit(hwnd);
    g_deck->EditStart(id, NowSeconds());
    if (selectAll) g_deck->edit_.anchor = 0;
    {
        std::lock_guard lock(g_mutex);
        g_editText = g_deck->edit_.text;
    }
    SetTyping(hwnd, true);
}

std::wstring ClipboardUnicode(HWND hwnd) {
    std::wstring text;
    if (!IsClipboardFormatAvailable(CF_UNICODETEXT) || !OpenClipboard(hwnd)) return text;
    if (HANDLE data = GetClipboardData(CF_UNICODETEXT)) {
        if (const wchar_t* chars = static_cast<const wchar_t*>(GlobalLock(data))) {
            text = chars;
            GlobalUnlock(data);
        }
    }
    CloseClipboard();
    return text;
}

// What a key did, told to the app; Enter in some boxes also empties it (a new to-do, a command) or ends typing.
void EditOutcome(HWND hwnd, int outcome) {
    Renderer::DeckEdit& e = g_deck->edit_;
    const int id = e.id;
    switch (outcome) {
        case Renderer::kEditIgnored:
        case Renderer::kEditMoved:
            return;
        case Renderer::kEditChanged:
            PublishEdit(id);
            return;
        case Renderer::kEditSubmit:
            {
                std::lock_guard lock(g_mutex);
                g_editSubmitted = e.text;
            }
            Notify(kEdit, id, Renderer::kEditSubmit);
            // A question sent: the conversation goes to its end, where the question and its answer come.
            if (id == 40) g_deck->chatToEnd_ = true;
            if (id == 11 || id == 20 || id == 40) {
                g_deck->Remember(false, NowSeconds());
                e.text.clear();
                e.caret = e.anchor = 0;
                e.follow = true;
                PublishEdit(id);
            }
            if (id == 1 || id == 12) EndEdit(hwnd);
            return;
        case Renderer::kEditCancel:
            Notify(kEdit, id, Renderer::kEditCancel);
            EndEdit(hwnd);
            return;
        default:
            Notify(kEdit, id, outcome);
            return;
    }
}

void EditCopy(HWND hwnd, bool cut) {
    Renderer::DeckEdit& e = g_deck->edit_;
    if (e.caret == e.anchor) return;
    CopyTextToClipboard(hwnd, g_deck->Selected());
    if (cut && g_deck->Replace(L"", false, NowSeconds())) PublishEdit(e.id);
}

void EditPaste(HWND hwnd) {
    const std::wstring text = ClipboardUnicode(hwnd);
    if (text.empty()) return;
    // Pasted on the terminal's output: it goes to the command line.
    if (g_deck->edit_.readOnly) {
        if (g_deck->edit_.id != 21) return;
        StartEdit(hwnd, 20, false);
    }
    if (g_deck->Replace(text, false, NowSeconds())) PublishEdit(g_deck->edit_.id);
}

void EditKeyDown(HWND hwnd, WPARAM vk) {
    const bool ctrl = GetKeyState(VK_CONTROL) < 0, shift = GetKeyState(VK_SHIFT) < 0;
    Renderer::DeckEdit& e = g_deck->edit_;
    if ((ctrl && vk == 'C') || (ctrl && vk == VK_INSERT)) {
        if (e.caret != e.anchor) EditCopy(hwnd, false);
        else if (vk == 'C') Notify(kEdit, e.id, Renderer::kEditInterrupt);  // the terminal: stop what runs
        return;
    }
    if ((ctrl && vk == 'X') || (shift && vk == VK_DELETE)) {
        EditCopy(hwnd, true);
        return;
    }
    if ((ctrl && vk == 'V') || (shift && vk == VK_INSERT)) {
        EditPaste(hwnd);
        return;
    }
    EditOutcome(hwnd, g_deck->EditKey(static_cast<UINT>(vk), ctrl, shift, NowSeconds()));
}

void EditChar(HWND hwnd, wchar_t c) {
    if (c < 0x20 || c == 0x7F) return;  // Enter, Backspace, Tab, Esc and Ctrl+letters come as keys
    if (g_deck->edit_.readOnly) {
        if (g_deck->edit_.id != 21) return;
        StartEdit(hwnd, 20, false);  // typing on the output goes to the command line
    }
    if (g_deck->Replace(std::wstring(1, c), true, NowSeconds())) PublishEdit(g_deck->edit_.id);
}

// Right-click on a box: Cut, Copy, Paste, Select all.
void FieldMenu(HWND hwnd, int id) {
    if (!(g_deck->edit_.on && g_deck->edit_.id == id)) StartEdit(hwnd, id, false);
    Renderer::DeckEdit& e = g_deck->edit_;
    const bool picked = e.caret != e.anchor;
    HMENU menu = CreatePopupMenu();
    AppendMenuW(menu, MF_STRING | (!picked || e.readOnly ? MF_GRAYED : 0), 1, L"Cut");
    AppendMenuW(menu, MF_STRING | (!picked ? MF_GRAYED : 0), 2, L"Copy");
    AppendMenuW(menu, MF_STRING | (e.readOnly && id != 21 ? MF_GRAYED : 0), 3, L"Paste");
    AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(menu, MF_STRING, 4, L"Select all");
    POINT at = {};
    GetCursorPos(&at);
    SetForegroundWindow(hwnd);
    const int chosen = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, at.x, at.y, 0, hwnd, nullptr);
    PostMessageW(hwnd, WM_NULL, 0, 0);
    DestroyMenu(menu);
    if (!g_deck->edit_.on) return;
    switch (chosen) {
        case 1: EditCopy(hwnd, true); break;
        case 2: EditCopy(hwnd, false); break;
        case 3: EditPaste(hwnd); break;
        case 4:
            g_deck->edit_.anchor = 0;
            g_deck->edit_.caret = g_deck->edit_.text.size();
            break;
    }
}

LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam) {
    switch (msg) {
        case WM_MOUSEACTIVATE: {
            // A click on a text box takes the keyboard; anywhere else the window never takes the focus.
            POINT p = {};
            GetCursorPos(&p);
            ScreenToClient(hwnd, &p);
            const bool onChat = g_deck && g_chatOn && FieldAt(p) == 0 && Renderer::Inside(g_deck->chatRect_, p);
            if ((g_deck && g_deck->edit_.on) || FieldAt(p) != 0 || onChat) {
                SetWindowLongPtrW(hwnd, GWL_EXSTYLE, GetWindowLongPtrW(hwnd, GWL_EXSTYLE) & ~static_cast<LONG_PTR>(WS_EX_NOACTIVATE));
                return MA_ACTIVATE;
            }
            return MA_NOACTIVATE;
        }
        case WM_KEYDOWN:
            if (wParam == VK_ESCAPE && g_keyboardOpen.load() && !g_keysOn) {
                if (g_deck && g_deck->edit_.on) EndEdit(hwnd);
                g_request = 0;
                return 0;
            }
            if (g_deck && g_deck->edit_.on) {
                EditKeyDown(hwnd, wParam);
                return 0;
            }
            if (g_keysOn && wParam == VK_ESCAPE) {
                Notify(kAction, 59999);
                return 0;
            }
            // In a conversation: Ctrl+C copies what is selected, Ctrl+A selects it all.
            if (g_deck && g_chatOn && GetKeyState(VK_CONTROL) < 0) {
                if (wParam == 'C' || wParam == VK_INSERT) {
                    if (const std::wstring picked = g_deck->ChatSelected(); !picked.empty()) CopyTextToClipboard(hwnd, picked);
                    return 0;
                }
                if (wParam == 'A' && !g_deck->chatPieces_.empty()) {
                    g_deck->chatSelOn_ = true;
                    g_deck->chatSelA_ = {0, 0};
                    g_deck->chatSelB_ = {g_deck->chatPieces_.size() - 1, g_deck->chatPieces_.back().text.size()};
                    return 0;
                }
            }
            break;
        case WM_CHAR:
            if (g_deck && g_deck->edit_.on) {
                EditChar(hwnd, static_cast<wchar_t>(wParam));
                return 0;
            }
            if (g_keysOn && wParam > 0x20 && wParam != 0x7F) {
                Notify(kAction, 60000 + static_cast<int>(towupper(static_cast<wchar_t>(wParam))));
                return 0;
            }
            break;
        case WM_KILLFOCUS:
            if (g_deck && g_deck->edit_.on) EndEdit(hwnd);
            else if (g_keysOn) Notify(kAction, 59999);  // the picker closes when something else is chosen
            break;
        case WM_LBUTTONDOWN:
        case WM_LBUTTONDBLCLK: {
            g_pressAt = lParam;
            g_pressKeys = wParam;
            g_pressed = true;
            SetCapture(hwnd);  // a selection box can be dragged past the edge
            // On a text box: the caret goes there (Shift: the selection grows to it; twice: a word).
            const POINT p = {GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam)};
            if (kSide && PtInRect(&g_grip, p)) {
                g_gripPress = true;
                return 0;
            }
            if (const int id = FieldAt(p); id != 0) {
                const bool same = g_deck->edit_.on && g_deck->edit_.id == id;
                StartEdit(hwnd, id, false);
                const size_t pos = g_deck->FieldPos(id, p);
                const ULONGLONG tick = GetTickCount64();
                const bool twice = g_lastFieldClick == id && tick - g_lastFieldClickAt <= GetDoubleClickTime() &&
                                   std::abs(p.x - g_lastFieldClickPt.x) <= 4 && std::abs(p.y - g_lastFieldClickPt.y) <= 4;
                g_lastFieldClick = id;
                g_lastFieldClickAt = twice ? 0 : tick;
                g_lastFieldClickPt = p;
                if (twice) g_deck->SelectWordAt(pos);
                else g_deck->MoveTo(pos, same && (wParam & MK_SHIFT), NowSeconds());
                g_fieldPress = !twice;
            } else if (g_deck && g_chatOn && Renderer::Inside(g_deck->chatRect_, p)) {
                // On the conversation: a drag from here selects (a plain click leaves the selection off).
                g_chatPress = true;
                g_deck->chatSelA_ = g_deck->chatSelB_ = g_deck->ChatPos(p);
                g_deck->chatSelOn_ = false;
                SetFocus(hwnd);
            }
            return 0;
        }
        case WM_MOUSEHWHEEL:
            g_hwheel += GET_WHEEL_DELTA_WPARAM(wParam);
            return 0;
        case WM_LBUTTONUP:
            if (GetCapture() == hwnd) ReleaseCapture();
            if (g_gripPress) {
                // The end of a drag of the handle: no click.
                g_gripPress = false;
                g_pressed = false;
                return 0;
            }
            g_clickKeys = wParam;
            g_pressed = false;
            g_clickAt = lParam;
            g_clicked = true;
            return 0;
        case WM_MOUSEWHEEL:
            g_wheel += GET_WHEEL_DELTA_WPARAM(wParam);
            return 0;
        case WM_RBUTTONUP: {
            if (g_onPage.load()) {
                g_rightClickAt = lParam;
                g_rightClicked = true;
                return 0;
            }
            POINT at = {};
            GetCursorPos(&at);
            Notify(kMenu, at.x, at.y);
            return 0;
        }
        case WM_SETCURSOR: {
            POINT p = {};
            GetCursorPos(&p);
            ScreenToClient(hwnd, &p);
            SetCursor(LoadCursorW(nullptr, (kSide && (g_gripPress || PtInRect(&g_grip, p))) ? IDC_SIZENS : FieldAt(p) != 0 ? IDC_IBEAM : IDC_ARROW));
            return TRUE;
        }
    }
    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

// The few settings whose change needs a repaint.
struct LookPrint {
    float opacity = -1.0f;
    bool w11 = false, notch = false, shadow = false, depth = false;
    int material = -1, accentMode = -1;
    D2D1_COLOR_F accent = {};
    D2D1_COLOR_F background = {};
    std::wstring font, emoji;
    bool operator==(const LookPrint& o) const {
        return opacity == o.opacity && w11 == o.w11 && notch == o.notch && shadow == o.shadow && depth == o.depth &&
               material == o.material && accentMode == o.accentMode && font == o.font && emoji == o.emoji &&
               std::memcmp(&accent, &o.accent, sizeof(accent)) == 0 &&
               std::memcmp(&background, &o.background, sizeof(background)) == 0;
    }
};

LookPrint PrintOf(const Settings& s) {
    LookPrint p;
    p.opacity = s.pillOpacity;
    p.w11 = s.w11Style;
    p.notch = s.notchStyle;
    p.shadow = s.dropShadow;
    p.depth = s.materialDepth;
    p.material = static_cast<int>(s.backdropMaterial);
    p.accentMode = static_cast<int>(s.accentMode);
    p.accent = s.customAccent;
    p.background = s.pillBgColor;
    p.font = s.fontFamily;
    p.emoji = s.deckEmoji + L"|" + (s.characters ? s.characterPack + L"|" + s.characterStamp : std::wstring());
    return p;
}

class DropTarget final : public IDropTarget {
   public:
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override {
        if (!ppv) return E_POINTER;
        if (IsEqualIID(riid, __uuidof(IUnknown)) || IsEqualIID(riid, __uuidof(IDropTarget))) {
            *ppv = static_cast<IDropTarget*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return static_cast<ULONG>(++refs_); }
    STDMETHODIMP_(ULONG) Release() override {
        const LONG r = --refs_;
        if (r == 0) delete this;
        return static_cast<ULONG>(r);
    }
    STDMETHODIMP DragEnter(IDataObject* data, DWORD, POINTL, DWORD* effect) override {
        // The Tray page is the second notch's own: it takes files whether or not the top notch has a tray,
        // and (Nyatch) what a browser gives: a picture, a link to a file, a piece of text.
        accept_ = !g_trayInternalDrag.load() && !kSide && TrayCanTake(data);
        if (accept_) {
            g_hold = true;          // open while the files are over it
            Notify(kDropOver);      // the app turns to the Tray page
        }
        if (effect) *effect = accept_ ? PickTrayDropEffect(*effect) : DROPEFFECT_NONE;
        return S_OK;
    }
    STDMETHODIMP DragOver(DWORD, POINTL, DWORD* effect) override {
        if (effect) *effect = accept_ ? PickTrayDropEffect(*effect) : DROPEFFECT_NONE;
        return S_OK;
    }
    STDMETHODIMP DragLeave() override {
        accept_ = false;
        g_hold = false;
        return S_OK;
    }
    STDMETHODIMP Drop(IDataObject* data, DWORD, POINTL, DWORD* effect) override {
        std::vector<std::wstring> paths;
        if (accept_ && ExtractDropPaths(data, &paths)) {
            AddPathsToFileTray(paths);
            if (effect) *effect = PickTrayDropEffect(*effect);
        } else if (accept_ && TakeDropIntoTray(data)) {
            if (effect) *effect = PickTrayDropEffect(*effect);
        } else if (effect) {
            *effect = DROPEFFECT_NONE;
        }
        accept_ = false;
        g_hold = false;
        return S_OK;
    }

   private:
    std::atomic<LONG> refs_{1};
    bool accept_ = false;
};

DWORD WINAPI ThreadProc(LPVOID) {
    // OLE, for files dropped on the notch.
    const HRESULT co = OleInitialize(nullptr);
    DwmBackdrop::g_owner = kSide ? 2 : 1;

    WNDCLASSEXW wc = {};
    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = WndProc;
    wc.hInstance = GetModuleHandleW(nullptr);
    wc.lpszClassName = kClassName;
    wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    RegisterClassExW(&wc);
    HWND hwnd = CreateWindowExW(WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST,
                                kClassName, kSide ? L"Nyatch side panel" : L"Nyatch second notch", WS_POPUP, 0, 0, 16, 16,
                                nullptr, nullptr, wc.hInstance, nullptr);
    Renderer renderer;
    if (!hwnd || !renderer.Initialize(hwnd)) {
        if (hwnd) DestroyWindow(hwnd);
        UnregisterClassW(kClassName, wc.hInstance);
        if (SUCCEEDED(co)) OleUninitialize();
        return 0;
    }
    {
        ComPtr<IDropTarget> drop;
        drop.Attach(new DropTarget());
        RegisterDragDrop(hwnd, drop.Get());
    }
    g_deck = &renderer;

    SpringValue width, height;
    bool started = false, open = false, hidden = false, shown = false, wasOver = false, raised = false;
    std::wstring page;             // the native page on show, or empty
    std::wstring pageData, paintedData;
    // The tray's selection box and clicks.
    bool marquee = false, swallowClick = false;
    D2D1_POINT_2F marqueeFrom = {};
    std::set<std::wstring> marqueeBase;
    int trayAnchor = -1;
    std::wstring lastTrayClickPath;
    ULONGLONG lastTrayClickAt = 0;
    uint64_t paintedEditSig = ~0ULL;
    ULONGLONG lastAppClickAt = 0;
    D2D1_RECT_F lastAppClickRect = {};
    int pageIndex = 0, pageCount = 0;
    float scroll = 0.0f;           // the page's list, scrolled
    float scrollGoal = 0.0f;       // where the wheel is taking it: scroll glides there (see below)
    double scrollAt = 0.0;
    double lastWheelTurn = -1e9, lastWheelScroll = -1e9;
    POINT paintedHover = {-2, -2};
    float paintedScroll = -1.0f, paintedContent = -1.0f;
    uint64_t paintedNotices = ~0ULL;
    bool paintedDnd = false;
    std::wstring paintedPage;
    int paintedIndex = -1, paintedCount = -1;
    bool asked = false;            // opened by the app or a click: open until the pointer has been on it and left
    double lastOpenAt = -10.0;     // Nyatch: when it was last open (a hidden one goes once it has closed)
    bool askedSeen = false;        // ...and the pointer has been on it since
    bool quietUntilLeft = false;   // closed by the app with the pointer on it: no hover-open until it leaves
    bool keyboardTaken = false;    // opened from the keyboard, and the keyboard has come to it
    Settings settings = GetSettingsCopy();
    double settingsAt = -1.0, lastPaint = -1e9;
    std::wstring text, measuredText, measuredEmoji;
    float measuredUnit = 0.0f, closedWidth = 168.0f;
    LookPrint painted;
    D2D1_RECT_F paintedPill = {};
    POINT paintedOrigin = {-1, -1};
    float paintedShown = -1.0f;
    std::wstring paintedText;  // what the closed notch last showed: a new text (a ticking timer) repaints
    bool topmostNow = false, topmostKnown = false;
    bool wasFull = false;      // the top notch thought a full-screen app was in front, last frame
    double lastTopCheck = 0.0;  // when it last made sure it is on top
    auto previous = std::chrono::steady_clock::now();

    while (true) {
        MSG msg;
        while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE)) {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
        if (WaitForSingleObject(g_stopEvent, 0) == WAIT_OBJECT_0) break;

        const auto nowPoint = std::chrono::steady_clock::now();
        const float dt = std::clamp(std::chrono::duration<float>(nowPoint - previous).count(), 0.001f, 0.05f);
        previous = nowPoint;
        const double now = NowSeconds();
        if (now - settingsAt > 0.3) {
            settings = GetSettingsCopy();
            settingsAt = now;
        }

        // Nyatch: hidden (turned off, with its hotkey still on): not on the screen until the hotkey opens it,
        // and gone again a moment after it closes.
        if (open || asked || g_request.load() == 1) lastOpenAt = now;
        const bool hiddenAtRest = kSide ? settings.sideHiddenAtRest : settings.deckHiddenAtRest;
        // Nyatch: and out of the way of a full-screen app (a film, a game) while it is closed: the side panel's tab
        // is always on top, so it stayed over them. Its hotkey still opens it there, and it is back when full
        // screen ends.
        const bool fullscreenNow = g_fullscreenInFront.load(std::memory_order_relaxed);
        if (!(kSide ? settings.sideEnabled : settings.deckEnabled) || (hiddenAtRest && now - lastOpenAt > 0.7) ||
            (fullscreenNow && now - lastOpenAt > 0.7)) {
            if (shown) {
                ShowWindow(hwnd, SW_HIDE);
                DwmBackdrop::HideAll();
                shown = false;
            }
            open = false;
            started = false;
            // Painted afresh before it shows again: else it waited for something on it to change (the LLM tab came
            // back late), or showed the frame it had before it was hidden for a moment (the second notch flashed).
            paintedShown = -1.0f;
            // Looked at often while a full-screen app has it hidden, so it is back as soon as the top notch is.
            MsgWaitForMultipleObjects(1, &g_stopEvent, FALSE, fullscreenNow ? 60 : 250, QS_ALLINPUT);
            continue;
        }

        // Where it goes: the main display's work area, as the app's pages expect.
        const HMONITOR monitor = MonitorFromPoint(POINT{0, 0}, MONITOR_DEFAULTTOPRIMARY);
        MONITORINFO info = {};
        info.cbSize = sizeof(info);
        GetMonitorInfoW(monitor, &info);
        const RECT work = info.rcWork;
        const float unit = static_cast<float>(GetDpiForWindow(hwnd)) / 96.0f;
        const bool atTop = !IsBottomPosition(settings.deckPosition);
        const Position where = settings.deckPosition;
        const bool atLeft = where == Position::TopLeft || where == Position::BottomLeft;
        const bool atRight = where == Position::TopRight || where == Position::BottomRight;
        const float gap = settings.notchStyle ? 0.0f : kEdgeGap * unit;
        const float pad = kPad * unit;

        if (g_textDirty.exchange(false)) {
            std::lock_guard lock(g_mutex);
            text = g_text;
        }
        // Nyatch: the side panel, closed, is a notch that says what it holds.
        if constexpr (kSide) text = L"LLM";
        if (text != measuredText || unit != measuredUnit || settings.deckEmoji != measuredEmoji) {
            const float textW = renderer.MeasureDeckText(text, unit, settings.fontFamily) / unit;
            const float mark = settings.deckEmoji == L"none" ? 0.0f : 13.0f + 8.0f;
            closedWidth = std::clamp(std::ceil(textW) + mark + 36.0f, 100.0f, 340.0f);
            measuredText = text;
            measuredUnit = unit;
            measuredEmoji = settings.deckEmoji;
        }

        // What the app asked for: open (its menu's Go to), or close (Esc, or a click elsewhere).
        const int request = g_request.exchange(-1);

        // Nyatch: the side panel is as tall as its share of the screen (less a gap at each end).
        static float sideDragged = -1.0f;  // Nyatch: the share of the screen the handle set, until the setting has it
        const float sideRoom = static_cast<float>(work.bottom - work.top) - 2.0f * kSideGap * unit;
        const float sideOpenH = std::max(240.0f * unit, sideRoom * (sideDragged >= 0.0f ? sideDragged : settings.sideHeight));
        const int winW = static_cast<int>(std::ceil(((kSide ? kSideOpenWidth : kOpenWidth) + kPad * 2.0f) * unit));
        const int winH = static_cast<int>(std::ceil(kSide ? sideOpenH + kPad * 2.0f * unit : (kOpenHeight + kPad * 2.0f) * unit));
        POINT origin;
        const float side = kSideGap * unit;
        if constexpr (kSide) {
            // On the right or left edge, at the top, the middle or the bottom.
            // Against the edge itself, as a notch is against the top of the screen.
            origin.x = settings.sideRight ? static_cast<LONG>(work.right + pad - winW) : static_cast<LONG>(work.left - pad);
            // At the top or the bottom, against that edge too.
            origin.y = settings.sideAnchor == 0 ? static_cast<LONG>(work.top - pad)
                     : settings.sideAnchor == 2 ? static_cast<LONG>(work.bottom + pad - winH)
                     : static_cast<LONG>(work.top + ((work.bottom - work.top) - winH) / 2);
        } else {
            origin.x = atLeft ? static_cast<LONG>(work.left + side - pad)
                     : atRight ? static_cast<LONG>(work.right - side + pad - winW)
                     : static_cast<LONG>(work.left + ((work.right - work.left) - winW) / 2);
            origin.y = atTop ? static_cast<LONG>(work.top + gap - pad) : static_cast<LONG>(work.bottom - gap + pad - winH);
            origin.x += static_cast<LONG>(std::lround(settings.deckOffsetX * unit));
            origin.y += static_cast<LONG>(std::lround(settings.deckOffsetY * unit));
        }

        // Nyatch: the side panel, closed, stands upright against its edge: as wide as a notch is tall, as tall as its words.
        // At the top or the bottom it lies flat, as a notch does; in the middle of the edge it stands upright.
        const bool sideUpright = kSide && settings.sideAnchor == 1;
        const float closedW = (sideUpright ? kClosedHeight : closedWidth) * unit, closedH = (sideUpright ? closedWidth : kClosedHeight) * unit;
        const float openW = (kSide ? kSideOpenWidth : kOpenWidth) * unit, openH = kSide ? sideOpenH : kOpenHeight * unit;
        if (!started) {
            width.Reset(closedW);
            height.Reset(kSide ? 0.0f : closedH);  // Nyatch: for the side panel, how far it has slid (0 to 1000)
            started = true;
        }

        auto pillAt = [&](float w, float h) {
            if constexpr (kSide) {
                // Grows out of the edge it sits on, and along it from its anchor.
                const float left = settings.sideRight ? winW - pad - w : pad;
                const float top = settings.sideAnchor == 0 ? pad : settings.sideAnchor == 2 ? winH - pad - h : (winH - h) * 0.5f;
                return D2D1::RectF(left, top, left + w, top + h);
            } else {
                const float left = atLeft ? pad : atRight ? winW - pad - w : (winW - w) * 0.5f;
                const float top = atTop ? pad : winH - pad - h;
                return D2D1::RectF(left, top, left + w, top + h);
            }
        };
        // Nyatch: the side panel slides rather than grows. Opening, its tab slides out over the edge, then the
        // panel, at its full size, slides in from beyond it; closing, the other way round.
        float sideSlide = 0.0f;  // 0 closed, 1 open
        const D2D1_RECT_F pill = [&] {
            if constexpr (kSide) {
                sideSlide = std::max(0.0f, height.value / 1000.0f);
                // Out over the edge it rests on: the side when upright, else the top or the bottom.
                const float outward = sideUpright ? (settings.sideRight ? 1.0f : -1.0f) : (settings.sideAnchor == 2 ? 1.0f : -1.0f);
                auto shifted = [&](D2D1_RECT_F r, float d) {
                    if (sideUpright) {
                        r.left += d;
                        r.right += d;
                    } else {
                        r.top += d;
                        r.bottom += d;
                    }
                    return r;
                };
                if (sideSlide < 0.5f) {
                    const float across = sideUpright ? closedW : closedH;
                    return shifted(pillAt(closedW, closedH), outward * std::clamp(sideSlide * 2.0f, 0.0f, 1.0f) * (across + pad));
                }
                const float across = sideUpright ? openW : openH;
                return shifted(pillAt(openW, openH), outward * (1.0f - (sideSlide * 2.0f - 1.0f)) * (across + pad));
            } else {
                return pillAt(std::max(width.value, closedH), std::max(height.value, closedH));
            }
        }();
        {
            const D2D1_RECT_F full = pillAt(openW, openH);
            std::lock_guard lock(g_mutex);
            g_openRect = RECT{origin.x + static_cast<LONG>(std::lround(full.left)), origin.y + static_cast<LONG>(std::lround(full.top)),
                              origin.x + static_cast<LONG>(std::lround(full.right)), origin.y + static_cast<LONG>(std::lround(full.bottom))};
            g_openRectValid = true;
        }

        // The strip between the outline and the screen edge, only where a hidden taskbar could be.
        D2D1_RECT_F guard = {};
        if (kSide) {
        } else if (!atTop && work.bottom >= info.rcMonitor.bottom) guard = D2D1::RectF(pill.left, pill.bottom, pill.right, static_cast<float>(winH));
        if (atTop && work.top <= info.rcMonitor.top) guard = D2D1::RectF(pill.left, 0.0f, pill.right, pill.top);

        POINT cursor = {};
        GetCursorPos(&cursor);
        auto onScreen = [&](const D2D1_RECT_F& r) {
            return RECT{origin.x + static_cast<LONG>(r.left), origin.y + static_cast<LONG>(r.top),
                        origin.x + static_cast<LONG>(std::ceil(r.right)), origin.y + static_cast<LONG>(std::ceil(r.bottom))};
        };
        const RECT pillScreen = onScreen(pill);
        const RECT guardScreen = onScreen(guard);
        // Nyatch: the side panel is "under the pointer" where it rests (its tab, or the open panel), not where it
        // is while sliding: the tab slides away from under a click that opens it, and that is not leaving it.
        const RECT restScreen = onScreen(open ? pillAt(openW, openH) : pillAt(closedW, closedH));
        const bool over = kSide ? PtInRect(&restScreen, cursor) != FALSE
                                : PtInRect(&pillScreen, cursor) || (guard.right > guard.left && PtInRect(&guardScreen, cursor));

        // Nyatch: the side panel's handle. Dragged, the panel follows the pointer; let go, it settles on
        // a quarter, a half, three quarters or all of the screen, and the app keeps that as the setting.
        if constexpr (kSide) {
            static bool dragging = false;
            static float fromH = 0.0f;
            static LONG fromY = 0;
            if (g_gripPress && open && !dragging) {
                dragging = true;
                fromH = openH;
                fromY = cursor.y;
                g_hold = true;
            }
            if (dragging) {
                float delta = static_cast<float>(fromY - cursor.y);  // up: taller, for a panel at the bottom
                if (settings.sideAnchor == 0) delta = -delta;           // at the top it grows down
                else if (settings.sideAnchor == 1) delta *= 2.0f;       // in the middle, both ways
                const float least = std::min(1.0f, 240.0f * unit / std::max(1.0f, sideRoom));
                sideDragged = std::clamp((fromH + delta) / std::max(1.0f, sideRoom), least, 1.0f);
                if (!g_gripPress) {
                    dragging = false;
                    g_hold = false;
                    float best = 0.25f;
                    for (const float stop : {0.25f, 0.5f, 0.75f, 1.0f}) {
                        if (std::fabs(stop - sideDragged) < std::fabs(best - sideDragged)) best = stop;
                    }
                    sideDragged = best;
                    Notify(kHeight, static_cast<int>(std::lround(best * 100.0f)));
                }
            } else if (sideDragged >= 0.0f && std::fabs(settings.sideHeight - sideDragged) < 0.001f) {
                sideDragged = -1.0f;  // the setting has it now
            }
            // The handle: a strip on the open panel's edge away from its anchor.
            const float gripW = 96.0f * unit, gripH = 14.0f * unit;
            const float mid = (pill.left + pill.right) * 0.5f;
            const bool atBottomEdge = settings.sideAnchor == 0;
            g_grip = open && sideSlide > 0.97f
                         ? RECT{static_cast<LONG>(mid - gripW * 0.5f), static_cast<LONG>(atBottomEdge ? pill.bottom - gripH : pill.top),
                                static_cast<LONG>(mid + gripW * 0.5f), static_cast<LONG>(atBottomEdge ? pill.bottom : pill.top + gripH)}
                         : RECT{};
        }

        // Alt held with the pointer on it: hidden until Alt is let go, so what is behind can be clicked.
        // Alt on its own: Ctrl+Alt (the second notch's hotkey) or Win+Alt does not hide it.
        const bool altHeld = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0 && (GetAsyncKeyState(VK_CONTROL) & 0x8000) == 0 &&
                             (GetAsyncKeyState(VK_LWIN) & 0x8000) == 0 && (GetAsyncKeyState(VK_RWIN) & 0x8000) == 0 &&
                             !g_keyboardOpen.load();
        const bool wantHidden = altHeld && (hidden || over);
        if (wantHidden != hidden) {
            hidden = wantHidden;
            if (hidden) {
                ShowWindow(hwnd, SW_HIDE);
                DwmBackdrop::HideAll();
                shown = false;
                open = false;
                asked = false;
                width.Reset(closedW);
                height.Reset(kSide ? 0.0f : closedH);
                Notify(kAltHidden);
            }
            paintedShown = -1.0f;  // repaint on the way back
        }

        // The top notch's rule: open while the pointer is on the outline as it is now (so, once open,
        // anywhere on the open notch), closed as soon as it leaves; the outline then shrinks away from
        // the pointer, and it opens again only by coming back onto the closed notch. Besides that it
        // stays open while the app holds it (typing), or after the app or a click asked for it until the
        // pointer has been on it and left. Closed by the app while the pointer is on it, it stays closed
        // until the pointer leaves, so it does not spring open again at once.
        if (g_pageDirty.exchange(false)) {
            std::lock_guard lock(g_mutex);
            if (page != g_page) {
                scroll = 0.0f;
                scrollGoal = 0.0f;
                renderer.deckTraySel_.clear();
                renderer.regionScroll_.clear();
                renderer.regionMaxPrev_.clear();
                renderer.chatSelOn_ = false;
            }
            page = g_page;
            pageData = g_pageData;
            pageIndex = g_pageIndex.load();
            pageCount = g_pageCount.load();
        }
        if (g_pressed.load() && page == L"tray" && open) {
            const LPARAM at = g_pressAt.load();
            const POINT from = {GET_X_LPARAM(at), GET_Y_LPARAM(at)};
            const POINT now2 = {cursor.x - origin.x, cursor.y - origin.y};
            if (!marquee && (std::abs(now2.x - from.x) > 6 || std::abs(now2.y - from.y) > 6)) {
                bool onRow = false;
                for (const auto& hit : renderer.deckHits_) {
                    if (hit.action == Renderer::kDeckTrayOpen && Renderer::Inside(hit.rect, from) &&
                        static_cast<size_t>(hit.data) < renderer.deckTrayPaths_.size()) {
                        onRow = true;
                        const size_t index = static_cast<size_t>(hit.data);
                        const std::wstring path = renderer.deckTrayPaths_[index];
                        // A selected file carries the whole selection; another one, just itself.
                        if (!renderer.deckTraySel_.count(path)) {
                            renderer.deckTraySel_ = {path};
                            trayAnchor = static_cast<int>(index);
                        }
                        std::vector<std::wstring> carried;
                        for (size_t i = 0; i < renderer.deckTrayPaths_.size(); ++i) {
                            if (renderer.deckTraySel_.count(renderer.deckTrayPaths_[i]) && !renderer.deckTrayIsText_[i]) {
                                carried.push_back(renderer.deckTrayPaths_[i]);
                            }
                        }
                        g_pressed = false;
                        if (GetCapture() == hwnd) ReleaseCapture();
                        g_hold = true;  // stays open while the files are carried
                        StartFileTrayDrag(carried);
                        g_hold = false;
                        g_clicked = false;
                        paintedHover = {-2, -2};
                        break;
                    }
                }
                // Pressed on empty space in the list: a selection box (Ctrl adds to what is selected).
                if (!onRow && Renderer::Inside(renderer.deckListRect_, from)) {
                    marquee = true;
                    marqueeBase = (g_pressKeys.load() & MK_CONTROL) ? renderer.deckTraySel_ : std::set<std::wstring>{};
                    marqueeFrom = D2D1::Point2F(static_cast<float>(from.x), from.y + scroll);
                    g_hold = true;
                } else if (!onRow) {
                    g_pressed = false;
                }
            }
            if (marquee) {
                // Past the top or bottom, the list scrolls along.
                const D2D1_RECT_F list = renderer.deckListRect_;
                if (now2.y > list.bottom) scroll = std::min(renderer.deckScrollMax_, scroll + std::min(24.0f, (now2.y - list.bottom) * 0.4f));
                if (now2.y < list.top) scroll = std::max(0.0f, scroll - std::min(24.0f, (list.top - now2.y) * 0.4f));
                scrollGoal = scroll;
                const float x0 = std::min(marqueeFrom.x, static_cast<float>(now2.x)), x1 = std::max(marqueeFrom.x, static_cast<float>(now2.x));
                const float y0 = std::min(marqueeFrom.y, now2.y + scroll), y1 = std::max(marqueeFrom.y, now2.y + scroll);
                std::set<std::wstring> picked = marqueeBase;
                for (size_t i = 0; i < renderer.deckTrayRows_.size() && i < renderer.deckTrayPaths_.size(); ++i) {
                    const D2D1_RECT_F r = renderer.deckTrayRows_[i];
                    if (r.left < x1 && r.right > x0 && r.top < y1 && r.bottom > y0) picked.insert(renderer.deckTrayPaths_[i]);
                }
                renderer.deckTraySel_ = std::move(picked);
                renderer.deckMarqueeOn_ = true;
                renderer.deckMarquee_ = D2D1::RectF(std::max(x0, list.left), std::max(y0 - scroll, list.top), std::min(x1, list.right),
                                                    std::min(y1 - scroll, list.bottom));
                paintedHover = {-2, -2};
            }
        }
        if (marquee && !g_pressed.load()) {
            marquee = false;
            renderer.deckMarqueeOn_ = false;
            g_hold = false;
            swallowClick = true;  // the button coming up ends the box; it is not a click
            paintedHover = {-2, -2};
        }
        g_chatOn = ChatPage(page) && open;
        if (const bool keysNow = page == L"keys" && open; keysNow != g_keysOn) {
            g_keysOn = keysNow;
            if (keysNow) SetTyping(hwnd, true);
            else if (!renderer.edit_.on) SetTyping(hwnd, false);
        }
        if (g_chatPress) {
            const POINT p = {cursor.x - origin.x, cursor.y - origin.y};
            if (g_pressed.load()) {
                const LPARAM at = g_pressAt.load();
                if (std::abs(p.x - GET_X_LPARAM(at)) > 4 || std::abs(p.y - GET_Y_LPARAM(at)) > 4) renderer.chatSelOn_ = true;
                if (renderer.chatSelOn_) {
                    // Past the top or bottom, the conversation scrolls along.
                    const D2D1_RECT_F body = renderer.chatRect_;
                    float& sc = renderer.regionScroll_[4];
                    if (auto r = renderer.regions_.find(4); r != renderer.regions_.end()) {
                        if (p.y > body.bottom) sc = std::min(r->second.max, sc + std::min(24.0f, (p.y - body.bottom) * 0.4f));
                        if (p.y < body.top) sc = std::max(0.0f, sc - std::min(24.0f, (body.top - p.y) * 0.4f));
                    }
                    renderer.chatSelB_ = renderer.ChatPos(p);
                }
            } else {
                g_chatPress = false;
                if (renderer.chatSelOn_ && renderer.chatSelA_ == renderer.chatSelB_) renderer.chatSelOn_ = false;
                if (renderer.chatSelOn_) swallowClick = true;  // the end of a drag is not a click on a link
            }
        }
        bool clicked = g_clicked.exchange(false);
        if (clicked && swallowClick) {
            clicked = false;
            swallowClick = false;
        }
        const LPARAM clickAt = g_clickAt.load();
        const bool onPage = open && !page.empty() && g_openness.load(std::memory_order_relaxed) > 0.6f;
        g_onPage = onPage;
        if (g_rightClicked.exchange(false)) {
            const LPARAM at = g_rightClickAt.load();
            const POINT where = {GET_X_LPARAM(at), GET_Y_LPARAM(at)};
            bool used = false;
            if (const int id = onPage ? FieldAt(where) : 0; id != 0) {
                FieldMenu(hwnd, id);
                used = true;
            } else if (onPage && g_chatOn && Renderer::Inside(renderer.chatRect_, where)) {
                const std::wstring picked = renderer.ChatSelected();
                HMENU menu = CreatePopupMenu();
                AppendMenuW(menu, MF_STRING | (picked.empty() ? MF_GRAYED : 0), 1, L"Copy");
                AppendMenuW(menu, MF_STRING, 2, L"Select all");
                POINT screen = {};
                GetCursorPos(&screen);
                SetForegroundWindow(hwnd);
                const int chosen = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, screen.x, screen.y, 0, hwnd, nullptr);
                PostMessageW(hwnd, WM_NULL, 0, 0);
                DestroyMenu(menu);
                if (chosen == 1) CopyTextToClipboard(hwnd, picked);
                if (chosen == 2 && !renderer.chatPieces_.empty()) {
                    renderer.chatSelOn_ = true;
                    renderer.chatSelA_ = {0, 0};
                    renderer.chatSelB_ = {renderer.chatPieces_.size() - 1, renderer.chatPieces_.back().text.size()};
                }
                used = true;
            }
            for (auto it = renderer.deckHits_.rbegin(); it != renderer.deckHits_.rend() && onPage && !used; ++it) {
                if (it->action == Renderer::kDeckApp && it->data >= 0 && Renderer::Inside(it->rect, where)) {
                    Notify(kAction, static_cast<int>(it->data) + 10000, PageCode(page));
                    used = true;
                    break;
                }
            }
            if (!used) {
                POINT screen = {};
                GetCursorPos(&screen);
                Notify(kMenu, screen.x, screen.y);
            }
        }
        if (clicked && onPage) {
            clicked = false;
            const POINT at = {GET_X_LPARAM(clickAt), GET_Y_LPARAM(clickAt)};
            Renderer::DeckHit hit = {{}, Renderer::kDeckNone, 0};
            for (auto it = renderer.deckHits_.rbegin(); it != renderer.deckHits_.rend(); ++it) {
                if (Renderer::Inside(it->rect, at)) {
                    hit = *it;
                    break;
                }
            }
            if (renderer.chatSelOn_ && hit.action != Renderer::kDeckApp) renderer.chatSelOn_ = false;
            // A click off the box being typed in ends typing (the terminal's command line stays).
            if (hit.action != Renderer::kDeckField && renderer.edit_.on && !(page == L"terminal" && hit.action != Renderer::kDeckNone)) EndEdit(hwnd);
            switch (hit.action) {
                case Renderer::kDeckDnd:
                    SetDoNotDisturb(!g_isDnDActive.load());
                    break;
                case Renderer::kDeckClearAll:
                    NyatchNotificationClearAll();
                    break;
                case Renderer::kDeckRemoveNotice:
                    NyatchNotificationClearOne(hit.data);
                    break;
                case Renderer::kDeckOpenNotice: {
                    std::wstring appId;
                    int64_t arrived = 0;
                    {
                        std::lock_guard lock(g_notificationListMutex);
                        for (const NotificationEntry& e : g_notificationList) {
                            if (static_cast<int64_t>(e.id) == hit.data) {
                                appId = e.appId;
                                arrived = e.fileTime;
                            }
                        }
                    }
                    if (!appId.empty()) OpenNotificationSender(appId, static_cast<ULONGLONG>(arrived));
                    // Closed, as the app closes it: the opened app comes forward.
                    open = false;
                    asked = false;
                    quietUntilLeft = true;
                    Notify(kClosed);
                    break;
                }
                case Renderer::kDeckGoTo:
                    Notify(kGoTo, static_cast<int>(hit.data));
                    break;
                case Renderer::kDeckApp: {
                    // A second click on the same spot soon after: its double-click action, if it has one.
                    const ULONGLONG tick = GetTickCount64();
                    const bool twice = tick - lastAppClickAt <= GetDoubleClickTime() && std::abs(hit.rect.top - lastAppClickRect.top) < 2.0f &&
                                       std::abs(hit.rect.left - lastAppClickRect.left) < 2.0f;
                    lastAppClickAt = twice ? 0 : tick;
                    lastAppClickRect = hit.rect;
                    const auto dbl = renderer.deckDoubles_.find(hit.data);
                    if (twice && dbl != renderer.deckDoubles_.end() && dbl->second >= 0) Notify(kAction, static_cast<int>(dbl->second), PageCode(page));
                    else if (hit.data >= 0) Notify(kAction, static_cast<int>(hit.data), PageCode(page));
                    break;
                }
                case Renderer::kDeckField:
                    break;
                case Renderer::kDeckRegionStep: {
                    const int region = static_cast<int>(hit.data / 2);
                    auto r = renderer.regions_.find(region);
                    if (r == renderer.regions_.end()) break;
                    float& sc = renderer.regionScroll_[region];
                    sc = std::clamp(sc + (hit.data % 2 ? 140.0f : -140.0f) * unit, 0.0f, r->second.max);
                    break;
                }
                case Renderer::kDeckNone:
                    // A plain click on empty space in the tray clears the selection.
                    if (page == L"tray" && !(g_clickKeys.load() & (MK_CONTROL | MK_SHIFT)) && !renderer.deckTraySel_.empty()) {
                        renderer.deckTraySel_.clear();
                        paintedHover = {-2, -2};
                    }
                    break;
                case Renderer::kDeckTrayRetry:
                    RetryTrayDownload(static_cast<int>(hit.data));
                    break;
                case Renderer::kDeckTrayCancel:
                    CancelTrayDownload(static_cast<int>(hit.data));
                    break;
                case Renderer::kDeckTrayOpen:
                case Renderer::kDeckTrayRemove: {
                    const size_t index = static_cast<size_t>(hit.data);
                    if (index >= renderer.deckTrayPaths_.size()) break;
                    const std::wstring path = renderer.deckTrayPaths_[index];
                    auto& sel = renderer.deckTraySel_;
                    paintedHover = {-2, -2};
                    if (hit.action == Renderer::kDeckTrayRemove) {
                        // The x on a selected row removes the whole selection.
                        if (sel.count(path) && sel.size() > 1) {
                            for (const std::wstring& one : std::vector<std::wstring>(sel.begin(), sel.end())) RemoveFileTrayItemByPath(one);
                            sel.clear();
                        } else {
                            RemoveFileTrayItemByPath(path);
                            sel.erase(path);
                        }
                        g_layoutDirty = true;
                        break;
                    }
                    const WPARAM keys = g_clickKeys.load();
                    if ((keys & MK_SHIFT) && trayAnchor >= 0 && static_cast<size_t>(trayAnchor) < renderer.deckTrayPaths_.size()) {
                        if (!(keys & MK_CONTROL)) sel.clear();
                        const size_t a = std::min<size_t>(trayAnchor, index), b = std::max<size_t>(trayAnchor, index);
                        for (size_t i = a; i <= b; ++i) sel.insert(renderer.deckTrayPaths_[i]);
                        break;
                    }
                    if (keys & (MK_CONTROL | MK_SHIFT)) {
                        if (!sel.erase(path)) sel.insert(path);
                        trayAnchor = static_cast<int>(index);
                        break;
                    }
                    // A plain click selects it; a second click on it soon after opens it.
                    const ULONGLONG tick = GetTickCount64();
                    const bool twice = lastTrayClickPath == path && tick - lastTrayClickAt <= GetDoubleClickTime();
                    lastTrayClickPath = twice ? L"" : path;
                    lastTrayClickAt = tick;
                    sel = {path};
                    trayAnchor = static_cast<int>(index);
                    if (!twice) break;
                    // A file opens; a text snippet is copied.
                    std::wstring text;
                    bool isText = false;
                    {
                        std::lock_guard lock(g_stateMutex);
                        for (const FileTrayItem& item : g_state.fileTrayItems) {
                            if (item.path == path) {
                                isText = item.isText;
                                text = item.text;
                            }
                        }
                    }
                    if (isText) {
                        CopyTextToClipboard(hwnd, text);
                    } else {
                        ShellExecuteW(nullptr, L"open", path.c_str(), nullptr, nullptr, SW_SHOWNORMAL);
                        open = false;
                        asked = false;
                        quietUntilLeft = true;
                        Notify(kClosed);
                    }
                    break;
                }
                case Renderer::kDeckTrayPaste:
                    PasteIntoFileTray(hwnd);
                    break;
                case Renderer::kDeckTrayClear:
                    {
                        std::lock_guard lock(g_stateMutex);
                        g_state.fileTrayItems.clear();
                    }
                    SaveFileTray();
                    g_layoutDirty = true;
                    break;
                default:
                    break;
            }
            paintedHover = {-2, -2};  // repaint
        }

        // Text boxes: the app's requests, typing ending with the page, and a press dragged to select.
        {
            std::vector<std::pair<int, std::wstring>> sets;
            int want = 0;
            double wantAt = 0.0;
            {
                std::lock_guard lock(g_mutex);
                sets.swap(g_setField);
                want = g_wantField;
                wantAt = g_wantFieldAt;
            }
            for (const auto& [id, value] : sets) {
                Renderer::DeckEdit& e = renderer.edit_;
                if (!e.on || e.id != id || e.text == value) continue;
                renderer.Remember(false, now);
                e.text = value;
                e.caret = e.anchor = value.size();
                e.follow = true;
                std::lock_guard lock(g_mutex);
                g_editText = value;
            }
            if (want != 0) {
                const bool drawn = std::find(renderer.fieldsDrawn_.begin(), renderer.fieldsDrawn_.end(), want) != renderer.fieldsDrawn_.end();
                if (onPage && drawn && FieldOnPage(want, page)) StartEdit(hwnd, want, want == 12);
                if ((onPage && drawn) || now - wantAt > 1.5) {
                    std::lock_guard lock(g_mutex);
                    if (g_wantField == want) g_wantField = 0;
                }
            }
        }
        if (renderer.edit_.on && (!onPage || !FieldOnPage(renderer.edit_.id, page))) EndEdit(hwnd);
        if (g_fieldPress) {
            if (g_pressed.load() && renderer.edit_.on) {
                const POINT p = {cursor.x - origin.x, cursor.y - origin.y};
                const size_t pos = renderer.FieldPos(renderer.edit_.id, p);
                if (pos != renderer.edit_.caret) {
                    renderer.edit_.caret = pos;
                    renderer.edit_.follow = true;
                }
            } else {
                g_fieldPress = false;
            }
        }

        // The wheel on a native page: the list scrolls while it can, then the page turns.
        if (const int sideways = g_hwheel.exchange(0); sideways != 0 && onPage) {
            const POINT p = {cursor.x - origin.x, cursor.y - origin.y};
            for (auto& [id, region] : renderer.regions_) {
                if (region.sideways && Renderer::Inside(region.rect, p)) {
                    float& sc = renderer.regionScroll_[id];
                    sc = std::clamp(sc + 0.5f * sideways * unit, 0.0f, region.max);
                }
            }
        }
        // Nyatch: the page's list glides to where the wheel sent it: quick at first, easing in at the end.
        {
            const float dt = static_cast<float>(std::clamp(now - scrollAt, 0.0, 0.05));
            scrollAt = now;
            scrollGoal = std::clamp(scrollGoal, 0.0f, std::max(0.0f, renderer.deckScrollMax_));
            if (scroll != scrollGoal) {
                scroll += (scrollGoal - scroll) * (1.0f - std::exp(-dt * 16.0f));
                if (std::fabs(scrollGoal - scroll) < 0.4f) scroll = scrollGoal;
            }
        }
        int wheelNow = g_wheel.exchange(0);
        if constexpr (kSide) {
            // Nyatch: on the side panel the wheel changes the chat only over its button (the logo and the name).
            const POINT p = {cursor.x - origin.x, cursor.y - origin.y};
            if (wheelNow != 0 && onPage && Renderer::Inside(renderer.chatPickerRect_, p)) {
                if (now - lastWheelTurn > 0.16) {
                    lastWheelTurn = now;
                    Notify(kWheel, wheelNow < 0 ? 1 : -1);
                }
                wheelNow = 0;
            }
        }
        if (wheelNow != 0 && onPage) {
            const POINT p = {cursor.x - origin.x, cursor.y - origin.y};
            const float step = 48.0f * unit * (static_cast<float>(-wheelNow) / WHEEL_DELTA);
            float* target = nullptr;
            float max = 0.0f;
            for (int id : renderer.fieldsDrawn_) {
                const Renderer::FieldGeo& g = renderer.fields_[id];
                if (g.multiline && Renderer::Inside(g.rect, p)) {
                    target = &renderer.fieldScroll_[id];
                    max = g.max;
                }
            }
            for (auto& [id, region] : renderer.regions_) {
                if (!target && Renderer::Inside(region.rect, p)) {
                    target = &renderer.regionScroll_[id];
                    max = region.max;
                }
            }
            if (target) {
                const float next = std::clamp(*target + step, 0.0f, max);
                if (next != *target) {
                    *target = next;
                    lastWheelScroll = now;
                    wheelNow = 0;
                }
            }
        }
        if (const int wheel = wheelNow; wheel != 0 && onPage) {
            const float step = 48.0f * unit * (static_cast<float>(-wheel) / WHEEL_DELTA);
            const float max = renderer.deckScrollMax_;
            const bool canScroll = (step > 0 && scrollGoal < max - 0.5f) || (step < 0 && scrollGoal > 0.5f);
            if (canScroll) {
                // Nyatch: the wheel moves the goal; the list glides there (each notch adds to where it is going).
                scrollGoal = std::clamp(scrollGoal + step, 0.0f, max);
                lastWheelScroll = now;
            } else if (!kSide && now - lastWheelScroll > 0.45 && now - lastWheelTurn > 0.16) {
                // A moment after scrolling, so reaching the end does not turn the page by accident.
                // (Not on the side panel: there the wheel past the end does nothing; its chat button changes the chat.)
                lastWheelTurn = now;
                Notify(kWheel, wheel < 0 ? 1 : -1);
            }
        }
        if (request == 1 || clicked) {
            asked = true;
            askedSeen = over;  // the pointer may be elsewhere (the menu): it has to come onto it first
        }
        // Opened from the keyboard: it keeps the keyboard, and another app coming to the front (a click
        // elsewhere, Alt+Tab) closes it. Nyatch's own windows (a menu on a page) do not.
        if (g_keyboardOpen.load()) {
            if (open && !hidden) {
                if (GetFocus() != hwnd && GetForegroundWindow() != hwnd && !keyboardTaken) {
                    SetTyping(hwnd, true);
                    keyboardTaken = true;
                }
                if (GetFocus() == hwnd || GetForegroundWindow() == hwnd) keyboardTaken = true;
                DWORD frontPid = 0;
                if (HWND front = GetForegroundWindow()) GetWindowThreadProcessId(front, &frontPid);
                if (keyboardTaken && frontPid != 0 && frontPid != GetCurrentProcessId() && request != 1) {
                    asked = false;
                    g_keyboardOpen = false;
                    keyboardTaken = false;
                    Notify(kClosed);
                }
            }
        } else {
            keyboardTaken = false;
        }
        if (request == 0) {
            asked = false;
            quietUntilLeft = over;
        }
        if (asked && over) askedSeen = true;
        if (wasOver && !over) {
            // (The side panel does not close when the pointer leaves it: a click somewhere else closes it.)
            if (askedSeen && !kSide) asked = false;
            quietUntilLeft = false;
        }
        if constexpr (kSide) {
            // Nyatch: a click anywhere off the open side panel closes it (not while its menu is up).
            static bool buttonWasDown = false;
            const bool down = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0 || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;
            if (down && !buttonWasDown && open && !over && !g_hold.load()) {
                asked = false;
                if (renderer.edit_.on) EndEdit(hwnd);
            }
            buttonWasDown = down;
        }
        // (Closed by the app with the pointer still on it, it really closes: the pages are gone, and an
        // open outline without them would be an empty panel.)
        const bool want = !hidden && (g_hold.load() || (renderer.edit_.on && !renderer.edit_.readOnly) || asked ||
                                      (over && !quietUntilLeft && ((!kSide && settings.deckExpandOnHover) || open)));
        if (want != open) {
            open = want;
            Notify(open ? kOpened : kClosed);
            // Closed: the next opening is the pointer's again, and the keyboard goes back.
            if (!open && g_keyboardOpen.exchange(false)) {
                keyboardTaken = false;
                if (!renderer.edit_.on) SetTyping(hwnd, false);
            }
        }
        wasOver = over;

        if constexpr (kSide) {
            // How far it has slid: a little quicker out than in, and without a bounce past the edge.
            height.target = open ? 1000.0f : 0.0f;
            height.Step(dt, open ? 260.0f : 300.0f, open ? 32.0f : 35.0f);
        } else {
            width.target = open ? openW : closedW;
            height.target = open ? openH : closedH;
            width.Step(dt, open ? 380.0f : 200.0f, open ? 26.0f : 28.0f);
            height.Step(dt, open ? 380.0f : 200.0f, open ? 26.0f : 28.0f);
        }
        const bool animating = width.value != width.target || height.value != height.target || scroll != scrollGoal || renderer.deckPageMoving_;

        // Above a hidden taskbar while the pointer is on it, even when it is not always on top.
        const bool wantTopmost = kSide || settings.deckAlwaysOnTop || over;
        if (!topmostKnown || wantTopmost != topmostNow || (over && !raised)) {
            SetWindowPos(hwnd, wantTopmost ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0,
                         SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
            // Never over the pages: just under the app's window, which draws them on this outline.
            if (HWND pages = g_pagesWindow.load(); pages && IsWindowVisible(pages)) {
                SetWindowPos(hwnd, pages, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
            }
            topmostNow = wantTopmost;
            topmostKnown = true;
        }
        raised = over;
        // Nyatch: a full-screen app (or anything else always on top) that came over it is not left there: once a
        // second, and at once when full screen ends (a click on another screen), it goes back on top, as the top
        // notch does (EnsureTopmost), then under the pages again.
        {
            const bool fullNow = g_isFullscreen.load(std::memory_order_relaxed);
            if (wantTopmost && !fullNow && (wasFull || now - lastTopCheck > 1.0)) {
                lastTopCheck = now;
                EnsureTopmost(hwnd);
                if (HWND pages = g_pagesWindow.load(); pages && IsWindowVisible(pages)) {
                    SetWindowPos(hwnd, pages, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
                }
            }
            wasFull = fullNow;
        }

        const float opened = kSide ? std::clamp(height.value / 500.0f - 1.0f, 0.0f, 1.0f)
                                   : std::clamp((height.value - closedH) / std::max(1.0f, openH - closedH), 0.0f, 1.0f);
        g_openness.store(hidden ? 0.0f : opened, std::memory_order_relaxed);
        // (The side panel's tab keeps its words as it slides; the panel is drawn once it is gone.)
        const float closedShown = kSide ? (height.value < 500.0f ? 1.0f : 0.0f) : std::clamp(1.0f - opened * 4.0f, 0.0f, 1.0f);
        const LookPrint look = PrintOf(settings);
        // A native page: repainted when what it shows, the pointer over it, or its scrolling changes.
        const float contentShown = page.empty() ? 0.0f : std::clamp((opened - 0.55f) / 0.4f, 0.0f, 1.0f);
        POINT hover = {-1, -1};
        if (contentShown > 0.0f && (PtInRect(&pillScreen, cursor))) hover = {cursor.x - origin.x, cursor.y - origin.y};
        uint64_t notices = g_notificationListGeneration.load();
        if (page == L"tray") {
            std::lock_guard lock(g_stateMutex);
            for (const FileTrayItem& item : g_state.fileTrayItems) notices = notices * 31 + std::hash<std::wstring>{}(item.path) + item.sizeBytes;
            notices = notices * 131 + g_trayDownloadsGeneration.load();  // downloads coming in
            notices += g_state.fileTrayItems.size();
        }
        const bool dndNow = g_isDnDActive.load();
        uint64_t editSig = 0;
        if (renderer.edit_.on) {
            const Renderer::DeckEdit& e = renderer.edit_;
            editSig = std::hash<std::wstring>{}(e.text) ^ (e.caret * 1315423911ULL) ^ (e.anchor * 2654435761ULL) ^ (static_cast<uint64_t>(e.id) << 48);
            if (!e.readOnly && std::fmod(std::max(0.0, now - e.blinkFrom), 1.06) < 0.6) editSig += 1;
            if (e.follow) editSig += 7;
        }
        for (const auto& [id, value] : renderer.fieldScroll_) editSig = editSig * 31 + static_cast<uint64_t>(value * 8.0f) + id;
        for (const auto& [id, value] : renderer.regionScroll_) editSig = editSig * 37 + static_cast<uint64_t>(value * 8.0f) + id;
        if (renderer.chatSelOn_) {
            editSig = editSig * 41 + renderer.chatSelA_.first * 7919 + renderer.chatSelA_.second * 104729 + renderer.chatSelB_.first * 1299709 +
                      renderer.chatSelB_.second * 15485863 + 3;
        }
        const bool pageChanged = page != paintedPage || pageIndex != paintedIndex || pageCount != paintedCount || pageData != paintedData ||
                                 editSig != paintedEditSig ||
                                 contentShown != paintedContent ||
                                 (contentShown > 0.0f && (hover.x != paintedHover.x || hover.y != paintedHover.y ||
                                                          scroll != paintedScroll || notices != paintedNotices || dndNow != paintedDnd ||
                                                          now - lastPaint > 30.0));
        const bool changed = !(look == painted) || origin.x != paintedOrigin.x || origin.y != paintedOrigin.y ||
                             std::memcmp(&pill, &paintedPill, sizeof(pill)) != 0 || closedShown != paintedShown ||
                             text != paintedText || pageChanged || now - lastPaint > 2.0 || renderer.deckPageMoving_ ||
                             (contentShown > 0.0f && renderer.characterDue_ > 0.0 && now >= renderer.characterDue_);
        if (!hidden && changed) {
            SharedState snapshot;
            {
                std::lock_guard lock(g_stateMutex);
                snapshot.media.art = g_state.media.art;  // the auto accent follows the playing album
            }
            Renderer::DeckFrame frame;
            frame.origin = origin;
            frame.width = winW;
            frame.height = winH;
            frame.pill = pill;
            frame.guard = guard;
            frame.unit = unit;
            frame.closedShown = closedShown;
            frame.flatBottom = !kSide && !atTop && settings.notchStyle;
            frame.side = kSide;
            frame.sideRight = settings.sideRight;
            frame.sideTurn = !kSide ? 0 : sideUpright ? (settings.sideRight ? 2 : 3) : settings.sideAnchor == 2 ? 1 : 0;
            frame.text = text;
            if constexpr (kSide) {
                frame.grip = D2D1::RectF(static_cast<float>(g_grip.left), static_cast<float>(g_grip.top), static_cast<float>(g_grip.right),
                                         static_cast<float>(g_grip.bottom));
                frame.gripHot = g_gripPress || PtInRect(&g_grip, POINT{cursor.x - origin.x, cursor.y - origin.y});
            }
            frame.page = page;
            frame.pageIndex = pageIndex;
            frame.pageCount = pageCount;
            frame.contentShown = contentShown;
            frame.hover = hover;
            frame.scroll = scroll;
            frame.data = pageData;
            frame.now = now;
            if (renderer.RenderDeck(hwnd, snapshot, settings, frame, now)) {
                scroll = std::clamp(scroll, 0.0f, renderer.deckScrollMax_);
                scrollGoal = std::clamp(scrollGoal, 0.0f, renderer.deckScrollMax_);
                paintedPage = page;
                paintedData = pageData;
                paintedEditSig = editSig;
                paintedIndex = pageIndex;
                paintedCount = pageCount;
                paintedContent = contentShown;
                paintedHover = hover;
                paintedScroll = scroll;
                paintedNotices = notices;
                paintedDnd = dndNow;
                painted = look;
                paintedOrigin = origin;
                paintedPill = pill;
                paintedShown = closedShown;
                paintedText = text;
                lastPaint = now;
                if (!shown) {
                    ShowWindow(hwnd, SW_SHOWNOACTIVATE);
                    shown = true;
                }
            }
        }

        // Quick while moving, slower when nothing is near.
        RECT nearRect = pillScreen;
        InflateRect(&nearRect, 140, 140);
        const DWORD wait = animating ? 7 : PtInRect(&nearRect, cursor) ? 16 : (kSide && open) ? 20 : 50;  // (open: quick to see a click elsewhere)
        MsgWaitForMultipleObjects(1, &g_stopEvent, FALSE, wait, QS_ALLINPUT);
    }

    RevokeDragDrop(hwnd);
    g_deck = nullptr;
    renderer.Shutdown();
    DwmBackdrop::DestroyAll();
    DestroyWindow(hwnd);
    UnregisterClassW(kClassName, wc.hInstance);
    if (SUCCEEDED(co)) OleUninitialize();
    return 0;
}
