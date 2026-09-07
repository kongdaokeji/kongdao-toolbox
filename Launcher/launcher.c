#ifndef UNICODE
#define UNICODE
#endif

#include <windows.h>
#include <shlwapi.h>

#pragma comment(lib, "shlwapi.lib")

static BOOL IsSupportedOS(void)
{
    OSVERSIONINFOW vi = { sizeof(vi) };
    if (!GetVersionExW(&vi)) return FALSE;
    return vi.dwMajorVersion >= 10;
}

static BOOL LaunchExe(const WCHAR *exe, const WCHAR *workDir, DWORD *exitCode)
{
    SHELLEXECUTEINFOW sei = { sizeof(sei) };
    sei.fMask = SEE_MASK_NOASYNC | SEE_MASK_NOCLOSEPROCESS;
    sei.lpVerb = L"open";
    sei.lpFile = exe;
    sei.lpDirectory = workDir;
    sei.nShow = SW_SHOWNORMAL;

    if (!ShellExecuteExW(&sei)) return FALSE;

    if (sei.hProcess) {
        WaitForSingleObject(sei.hProcess, 15000);
        if (exitCode) GetExitCodeProcess(sei.hProcess, exitCode);
        CloseHandle(sei.hProcess);
    }
    return TRUE;
}

int WINAPI wWinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, PWSTR lpCmdLine, int nCmdShow)
{
    WCHAR exePath[MAX_PATH];
    GetModuleFileNameW(NULL, exePath, MAX_PATH);

    WCHAR dir[MAX_PATH];
    wcscpy_s(dir, _countof(dir), exePath);
    PathRemoveFileSpecW(dir);

    WCHAR mainExe[MAX_PATH];
    PathCombineW(mainExe, dir, L"src\\TubaWinUi3.exe");

    WCHAR compatExe[MAX_PATH];
    PathCombineW(compatExe, dir, L"\u56FE\u5427\u5DE5\u5177\u7BB1Winui3\u517C\u5BB9\u7248.exe");

    if (!IsSupportedOS()) {
        if (GetFileAttributesW(compatExe) != INVALID_FILE_ATTRIBUTES) {
            LaunchExe(compatExe, dir, NULL);
            return 0;
        }
        MessageBoxW(NULL,
            L"当前操作系统不支持 WinUI 3 版本（需要 Windows 10 及以上）。\n"
            L"兼容版程序也未找到，无法启动。",
            L"图吧工具箱WinUI3", MB_OK | MB_ICONERROR);
        return 1;
    }

    if (GetFileAttributesW(mainExe) == INVALID_FILE_ATTRIBUTES) {
        if (GetFileAttributesW(compatExe) != INVALID_FILE_ATTRIBUTES) {
            LaunchExe(compatExe, dir, NULL);
            return 0;
        }
        WCHAR msg[MAX_PATH + 64];
        wsprintfW(msg, L"找不到程序文件：\n%s", mainExe);
        MessageBoxW(NULL, msg, L"图吧工具箱WinUI3", MB_OK | MB_ICONERROR);
        return 1;
    }

    DWORD exitCode = 0;
    if (!LaunchExe(mainExe, dir, &exitCode)) {
        DWORD err = GetLastError();
        if (GetFileAttributesW(compatExe) != INVALID_FILE_ATTRIBUTES) {
            LaunchExe(compatExe, dir, NULL);
            return 0;
        }
        WCHAR msg[128];
        wsprintfW(msg, L"启动失败，错误代码：%lu", err);
        MessageBoxW(NULL, msg, L"图吧工具箱WinUI3", MB_OK | MB_ICONERROR);
        return 1;
    }

    if (exitCode != 0 && exitCode != STILL_ACTIVE) {
        if (GetFileAttributesW(compatExe) != INVALID_FILE_ATTRIBUTES) {
            LaunchExe(compatExe, dir, NULL);
            return 0;
        }
    }

    return 0;
}