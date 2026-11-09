/* GPL-3.0-or-later: independent Win32/Wine capability probe, no game files. */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <string.h>

static int get_stamp(HANDLE h, FILE_BASIC_INFO *basic, FILE_ID_INFO *id) {
    return GetFileInformationByHandleEx(h, FileBasicInfo, basic, sizeof(*basic))
        && GetFileInformationByHandleEx(h, FileIdInfo, id, sizeof(*id))
        && basic->ChangeTime.QuadPart != 0;
}

int main(void) {
    const char *name = "eml-wine-stamp-scratch.tmp";
    HANDLE h = CreateFileA(name, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ,
                           NULL, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, NULL);
    if (h == INVALID_HANDLE_VALUE) {
        fprintf(stderr, "Failed to create scratch Win32 file: %lu\n", GetLastError());
        return 2;
    }
    DWORD n = 0;
    FILE_BASIC_INFO a = {0}, b = {0}, c = {0};
    FILE_ID_INFO ia = {0}, ib = {0}, ic = {0};
    FILETIME mtime;
    int ok = 0;
    do {
        if (!WriteFile(h, "first", 5, &n, NULL) || n != 5 || !FlushFileBuffers(h)) break;
        if (!GetFileTime(h, NULL, NULL, &mtime)) break;
        if (!get_stamp(h, &a, &ia)) {
            puts("WINE_CHANGE_STAMP=UNSUPPORTED: full CRC fallback required");
            ok = 1; break;
        }
        Sleep(50);
        if (SetFilePointer(h, 0, NULL, FILE_BEGIN) == INVALID_SET_FILE_POINTER) break;
        if (!WriteFile(h, "other", 5, &n, NULL) || n != 5 || !FlushFileBuffers(h)) break;
        if (!SetFileTime(h, NULL, NULL, &mtime)) break;
        if (!get_stamp(h, &b, &ib)) {
            puts("WINE_CHANGE_STAMP=UNSUPPORTED after edit: full CRC fallback required");
            ok = 1; break;
        }
        if (a.ChangeTime.QuadPart == b.ChangeTime.QuadPart
            || ia.VolumeSerialNumber != ib.VolumeSerialNumber
            || memcmp(&ia.FileId, &ib.FileId, sizeof(ia.FileId)) != 0) {
            puts("WINE_CHANGE_STAMP=UNRELIABLE: full CRC fallback required");
            ok = 1; break;
        }
        CloseHandle(h);
        h = CreateFileA(name, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING,
                        FILE_ATTRIBUTE_NORMAL, NULL);
        if (h == INVALID_HANDLE_VALUE) break;
        if (!get_stamp(h, &c, &ic)
            || c.ChangeTime.QuadPart != b.ChangeTime.QuadPart
            || ic.VolumeSerialNumber != ib.VolumeSerialNumber
            || memcmp(&ic.FileId, &ib.FileId, sizeof(ib.FileId)) != 0) {
            puts("WINE_CHANGE_STAMP=UNRELIABLE after reopen: full CRC fallback required");
            ok = 1; break;
        }
        puts("WINE_CHANGE_STAMP=VERIFIED: stable file identity; preserved-mtime edit advanced change time");
        ok = 1;
    } while (0);
    CloseHandle(h);
    DeleteFileA(name);
    if (!ok) fprintf(stderr, "Probe failed unexpectedly: %lu\n", GetLastError());
    return ok ? 0 : 3;
}
