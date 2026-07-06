// Native Win32 system-tray helper for the DesktopBridge package.
//
// Responsibilities (kept deliberately small and lightweight):
//   - Show a notification-area (tray) icon, swapping between a light/dark icon with the system theme.
//   - Localize the "Open" / "Exit" menu to the current Windows UI language.
//   - "Open"  -> activate the main UWP app (by its AUMID).
//   - "Exit"  -> close the main UWP app (and its full-trust WPF process) and remove the icon.
//
// It is launched by TrayLauncherService (via its AppExecutionAlias), so it runs WITH package
// identity and can resolve its own package family name.

#include <windows.h>
#include <shellapi.h>
#include <appmodel.h>
#include <tlhelp32.h>
#include <shlobj.h>
#include <string>
#include <cwctype>
#include <cstdlib>
#include <cstdio>

#include "resource.h"

#pragma comment(lib, "shell32.lib")

namespace
{
    constexpr wchar_t kWindowClass[] = L"DesktopBridgeTrayHelperWnd";
    constexpr wchar_t kMutexName[]   = L"DesktopBridgeTrayHelper_SingleInstance";
    constexpr wchar_t kAppId[]       = L"App"; // UWP <Application Id="App"> in Package.appxmanifest

    constexpr UINT WM_TRAYICON = WM_APP + 1;
    constexpr UINT IDM_OPEN    = 1001;
    constexpr UINT IDM_EXIT    = 1002;
    constexpr UINT kTrayId     = 1;

    NOTIFYICONDATAW g_nid{};
    HINSTANCE g_instance   = nullptr;
    HWND  g_hwnd           = nullptr;
    HICON g_icon           = nullptr;
    UINT  g_taskbarCreated = 0;
    std::wstring g_packageFamilyName;

    // ============================ Localization ============================================
    // Menu / tooltip strings per language. TO ADD A LANGUAGE: add a row with its two-letter
    // ISO-639 code (lowercase) and the translations. The first row (English) is the fallback
    // used when the current Windows UI language isn't listed here.
    struct UiStrings { const wchar_t* open; const wchar_t* exit; const wchar_t* tooltip; };
    struct LangRow   { const wchar_t* code; UiStrings strings; };

    const LangRow kLanguages[] =
    {
        { L"en", { L"Open DesktopBridge",       L"Exit",    L"DesktopBridge" } }, // English (fallback)
        { L"es", { L"Abrir DesktopBridge",      L"Salir",   L"DesktopBridge" } }, // Spanish
        { L"fr", { L"Ouvrir DesktopBridge",     L"Quitter", L"DesktopBridge" } }, // French
        { L"de", { L"DesktopBridge öffnen", L"Beenden", L"DesktopBridge" } }, // German (ö = 'ö')
        // { L"bn", { L"...", L"...", L"..." } },   // <-- future language goes here
    };

    // Two-letter code of the current Windows UI language, e.g. "en".
    std::wstring CurrentUiLanguage()
    {
        wchar_t name[LOCALE_NAME_MAX_LENGTH] = {};
        if (LCIDToLocaleName(GetUserDefaultUILanguage(), name, LOCALE_NAME_MAX_LENGTH, 0) == 0)
        {
            return L"en";
        }
        std::wstring locale = name; // e.g. "en-US"
        const size_t dash = locale.find(L'-');
        if (dash != std::wstring::npos)
        {
            locale.resize(dash);
        }
        for (wchar_t& c : locale) c = static_cast<wchar_t>(towlower(c));
        return locale;
    }

    const UiStrings& CurrentStrings()
    {
        const std::wstring lang = CurrentUiLanguage();
        for (const LangRow& row : kLanguages)
        {
            if (lang == row.code) return row.strings;
        }
        return kLanguages[0].strings; // English fallback
    }

    // ============================ Diagnostics =============================================
    // Log to <LOCALAPPDATA>\Packages\<PFN>\LocalCache\TrayHelper.log when running with package
    // identity, otherwise to %TEMP%\TrayHelper.log.
    std::wstring LogPath()
    {
        std::wstring dir;
        wchar_t* localAppData = nullptr;
        size_t len = 0;
        if (_wdupenv_s(&localAppData, &len, L"LOCALAPPDATA") == 0 && localAppData != nullptr)
        {
            dir = localAppData;
            free(localAppData);
        }
        if (!dir.empty() && !g_packageFamilyName.empty())
        {
            dir += L"\\Packages\\" + g_packageFamilyName + L"\\LocalCache";
        }
        else
        {
            wchar_t temp[MAX_PATH] = {};
            GetTempPathW(MAX_PATH, temp);
            dir = temp;
        }
        return dir + L"\\TrayHelper.log";
    }

    void Log(const std::wstring& message)
    {
        HANDLE file = CreateFileW(LogPath().c_str(), FILE_APPEND_DATA,
            FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE)
        {
            return;
        }
        SYSTEMTIME st{};
        GetLocalTime(&st);
        wchar_t stamp[32] = {};
        swprintf_s(stamp, L"%04d-%02d-%02d %02d:%02d:%02d ",
            st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond);
        const std::wstring line = stamp + message + L"\r\n";

        int bytes = WideCharToMultiByte(CP_UTF8, 0, line.c_str(), -1, nullptr, 0, nullptr, nullptr);
        if (bytes > 1)
        {
            std::string utf8(static_cast<size_t>(bytes - 1), '\0');
            WideCharToMultiByte(CP_UTF8, 0, line.c_str(), -1, utf8.data(), bytes, nullptr, nullptr);
            DWORD written = 0;
            WriteFile(file, utf8.data(), static_cast<DWORD>(utf8.size()), &written, nullptr);
        }
        CloseHandle(file);
    }

    // ============================ Theme ===================================================
    // The taskbar/system theme lives in this per-user value: 1 = light, 0 = dark.
    bool IsLightTheme()
    {
        DWORD value = 1; // assume light if the value is missing
        DWORD size = sizeof(value);
        DWORD type = 0;
        HKEY key = nullptr;
        if (RegOpenKeyExW(HKEY_CURRENT_USER,
                L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize",
                0, KEY_READ, &key) == ERROR_SUCCESS)
        {
            RegQueryValueExW(key, L"SystemUsesLightTheme", nullptr, &type,
                reinterpret_cast<LPBYTE>(&value), &size);
            RegCloseKey(key);
        }
        return value != 0;
    }

    HICON LoadThemeIcon()
    {
        // Mapping (per request): light theme -> light icon, dark theme -> dark icon.
        // Swap these two IDs if you prefer the icon to contrast with the taskbar instead.
        const int id = IsLightTheme() ? IDI_APPICON_LIGHT : IDI_APPICON_DARK;
        HICON icon = static_cast<HICON>(LoadImageW(
            g_instance, MAKEINTRESOURCEW(id), IMAGE_ICON,
            GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON), LR_DEFAULTCOLOR));
        return icon ? icon : LoadIconW(nullptr, IDI_APPLICATION);
    }

    // ============================ Packaging helpers =======================================
    std::wstring CurrentPackageFamilyName()
    {
        UINT32 length = 0;
        if (GetCurrentPackageFamilyName(&length, nullptr) != ERROR_INSUFFICIENT_BUFFER || length == 0)
        {
            return L"";
        }
        std::wstring buffer(length, L'\0');
        if (GetCurrentPackageFamilyName(&length, buffer.data()) != ERROR_SUCCESS)
        {
            return L"";
        }
        if (!buffer.empty() && buffer.back() == L'\0')
        {
            buffer.pop_back();
        }
        return buffer;
    }

    std::wstring ProcessPackageFamilyName(HANDLE process)
    {
        UINT32 length = 0;
        if (GetPackageFamilyName(process, &length, nullptr) != ERROR_INSUFFICIENT_BUFFER || length == 0)
        {
            return L"";
        }
        std::wstring buffer(length, L'\0');
        if (GetPackageFamilyName(process, &length, buffer.data()) != ERROR_SUCCESS)
        {
            return L"";
        }
        if (!buffer.empty() && buffer.back() == L'\0')
        {
            buffer.pop_back();
        }
        return buffer;
    }

    // Activate the packaged UWP app via the shell's AppsFolder using its AUMID.
    void OpenMainApp()
    {
        if (g_packageFamilyName.empty())
        {
            return;
        }
        const std::wstring target = L"shell:AppsFolder\\" + g_packageFamilyName + L"!" + kAppId;

        SHELLEXECUTEINFOW sei{};
        sei.cbSize = sizeof(sei);
        sei.fMask  = SEE_MASK_NOASYNC;
        sei.lpVerb = L"open";
        sei.lpFile = target.c_str();
        sei.nShow  = SW_SHOWNORMAL;
        BOOL ok = ShellExecuteExW(&sei);
        Log(L"OpenMainApp '" + target + (ok ? L"' ok." : L"' FAILED (gle=" + std::to_wstring(GetLastError()) + L")."));
    }

    // Terminate every process in our package family except this helper - i.e. the UWP app and
    // its full-trust WPF process. Best effort.
    void ExitMainApp()
    {
        if (g_packageFamilyName.empty())
        {
            return;
        }
        const DWORD self = GetCurrentProcessId();

        HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == INVALID_HANDLE_VALUE)
        {
            return;
        }

        PROCESSENTRY32W entry{};
        entry.dwSize = sizeof(entry);
        if (Process32FirstW(snapshot, &entry))
        {
            do
            {
                if (entry.th32ProcessID == self)
                {
                    continue;
                }
                HANDLE process = OpenProcess(
                    PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_TERMINATE, FALSE, entry.th32ProcessID);
                if (process == nullptr)
                {
                    continue;
                }
                if (_wcsicmp(ProcessPackageFamilyName(process).c_str(), g_packageFamilyName.c_str()) == 0)
                {
                    TerminateProcess(process, 0);
                }
                CloseHandle(process);
            } while (Process32NextW(snapshot, &entry));
        }
        CloseHandle(snapshot);
    }

    // ============================ Tray icon ===============================================
    void AddTrayIcon()
    {
        g_nid = {};
        g_nid.cbSize           = sizeof(g_nid);
        g_nid.hWnd             = g_hwnd;
        g_nid.uID              = kTrayId;
        g_nid.uFlags           = NIF_ICON | NIF_MESSAGE | NIF_TIP;
        g_nid.uCallbackMessage = WM_TRAYICON;
        g_nid.hIcon            = g_icon;
        wcscpy_s(g_nid.szTip, CurrentStrings().tooltip);
        BOOL ok = Shell_NotifyIconW(NIM_ADD, &g_nid);
        Log(ok ? L"Shell_NotifyIcon NIM_ADD succeeded."
               : L"Shell_NotifyIcon NIM_ADD FAILED (gle=" + std::to_wstring(GetLastError()) + L").");
    }

    // Reload the icon for the current theme and update the tray in place.
    void ApplyThemeIcon()
    {
        HICON updated = LoadThemeIcon();
        HICON previous = g_icon;
        g_icon = updated;
        g_nid.hIcon = g_icon;
        Shell_NotifyIconW(NIM_MODIFY, &g_nid);
        if (previous != nullptr && previous != g_icon)
        {
            DestroyIcon(previous);
        }
        Log(std::wstring(L"Theme changed; using ") + (IsLightTheme() ? L"light" : L"dark") + L" icon.");
    }

    void RefreshTooltipForLanguage()
    {
        wcscpy_s(g_nid.szTip, CurrentStrings().tooltip);
        Shell_NotifyIconW(NIM_MODIFY, &g_nid);
    }

    void ShowContextMenu()
    {
        POINT cursor{};
        GetCursorPos(&cursor);

        // The menu is rebuilt on every right-click, so it always reflects the current language.
        const UiStrings& strings = CurrentStrings();

        HMENU menu = CreatePopupMenu();
        AppendMenuW(menu, MF_STRING, IDM_OPEN, strings.open);
        AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
        AppendMenuW(menu, MF_STRING, IDM_EXIT, strings.exit);

        // Required so the menu dismisses correctly when the user clicks elsewhere.
        SetForegroundWindow(g_hwnd);
        TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, cursor.x, cursor.y, 0, g_hwnd, nullptr);
        DestroyMenu(menu);
    }

    LRESULT CALLBACK WndProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
    {
        if (message == g_taskbarCreated)
        {
            // Explorer restarted - re-add the icon.
            AddTrayIcon();
            return 0;
        }

        switch (message)
        {
        case WM_TRAYICON:
            switch (LOWORD(lParam))
            {
            case WM_LBUTTONDBLCLK:
                OpenMainApp();
                break;
            case WM_RBUTTONUP:
            case WM_CONTEXTMENU:
                ShowContextMenu();
                break;
            }
            return 0;

        case WM_SETTINGCHANGE:
            // Broadcast when system settings change; lParam names the affected area.
            if (lParam != 0)
            {
                const wchar_t* area = reinterpret_cast<const wchar_t*>(lParam);
                if (wcscmp(area, L"ImmersiveColorSet") == 0)
                {
                    ApplyThemeIcon();          // light/dark theme switched
                }
                else if (wcscmp(area, L"intl") == 0)
                {
                    RefreshTooltipForLanguage(); // UI language / region changed
                }
            }
            return 0;

        case WM_COMMAND:
            switch (LOWORD(wParam))
            {
            case IDM_OPEN:
                OpenMainApp();
                break;
            case IDM_EXIT:
                ExitMainApp();
                DestroyWindow(hwnd);
                break;
            }
            return 0;

        case WM_DESTROY:
            Shell_NotifyIconW(NIM_DELETE, &g_nid);
            PostQuitMessage(0);
            return 0;
        }

        return DefWindowProcW(hwnd, message, wParam, lParam);
    }
}

int APIENTRY wWinMain(HINSTANCE instance, HINSTANCE, LPWSTR, int)
{
    // Single instance: if the tray helper is already running, just exit.
    HANDLE mutex = CreateMutexW(nullptr, TRUE, kMutexName);
    if (mutex != nullptr && GetLastError() == ERROR_ALREADY_EXISTS)
    {
        CloseHandle(mutex);
        return 0;
    }

    g_instance          = instance;
    g_packageFamilyName = CurrentPackageFamilyName();
    g_taskbarCreated    = RegisterWindowMessageW(L"TaskbarCreated");

    Log(L"TrayHelper starting. PackageFamilyName='" +
        (g_packageFamilyName.empty() ? std::wstring(L"<none / no package identity>") : g_packageFamilyName) +
        L"', lang='" + CurrentUiLanguage() + L"', theme=" + (IsLightTheme() ? L"light" : L"dark") + L".");

    WNDCLASSEXW wc{};
    wc.cbSize        = sizeof(wc);
    wc.lpfnWndProc   = WndProc;
    wc.hInstance     = instance;
    wc.lpszClassName = kWindowClass;
    RegisterClassExW(&wc);

    // A hidden tool-window (top-level so it still receives the broadcast "TaskbarCreated" and
    // "WM_SETTINGCHANGE" messages, but kept off the taskbar and Alt-Tab and never shown).
    g_hwnd = CreateWindowExW(
        WS_EX_TOOLWINDOW, kWindowClass, L"DesktopBridge Tray", WS_POPUP,
        0, 0, 0, 0, nullptr, nullptr, instance, nullptr);
    if (g_hwnd == nullptr)
    {
        Log(L"CreateWindowEx FAILED (gle=" + std::to_wstring(GetLastError()) + L").");
        return 1;
    }

    g_icon = LoadThemeIcon();
    AddTrayIcon();

    MSG msg{};
    while (GetMessageW(&msg, nullptr, 0, 0))
    {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }

    if (g_icon != nullptr)
    {
        DestroyIcon(g_icon);
    }
    if (mutex != nullptr)
    {
        ReleaseMutex(mutex);
        CloseHandle(mutex);
    }
    return 0;
}
