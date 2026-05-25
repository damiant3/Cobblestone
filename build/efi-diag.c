/* Standalone UEFI diagnostic shell for Codex.
   Compiled with: cl /nologo /GS- /c efi-diag.c
   Linked with:   link /nologo /SUBSYSTEM:EFI_APPLICATION /ENTRY:EfiMain /OUT:BOOTX64.EFI efi-diag.obj
*/

typedef unsigned long long UINT64;
typedef unsigned int       UINT32;
typedef unsigned short     UINT16;
typedef unsigned char      UINT8;
typedef long long          INT64;
typedef UINT64             UINTN;
typedef UINT16             CHAR16;
typedef void               VOID;
typedef UINTN              EFI_STATUS;
typedef VOID*              EFI_HANDLE;

#define IN
#define OUT
#define EFIAPI __cdecl

typedef struct { UINT64 Signature; UINT32 Revision; UINT32 HeaderSize; UINT32 CRC32; UINT32 Reserved; } EFI_TABLE_HEADER;
typedef struct { UINT32 Data1; UINT16 Data2; UINT16 Data3; UINT8 Data4[8]; } EFI_GUID;

typedef struct {
    UINT16 ScanCode;
    CHAR16 UnicodeChar;
} EFI_INPUT_KEY;

typedef struct _EFI_SIMPLE_TEXT_INPUT_PROTOCOL EFI_SIMPLE_TEXT_INPUT_PROTOCOL;
struct _EFI_SIMPLE_TEXT_INPUT_PROTOCOL {
    EFI_STATUS (EFIAPI *Reset)(EFI_SIMPLE_TEXT_INPUT_PROTOCOL *This, UINT8 ExtendedVerification);
    EFI_STATUS (EFIAPI *ReadKeyStroke)(EFI_SIMPLE_TEXT_INPUT_PROTOCOL *This, EFI_INPUT_KEY *Key);
    VOID *WaitForKey;
};

typedef struct _EFI_SIMPLE_TEXT_OUTPUT_PROTOCOL EFI_SIMPLE_TEXT_OUTPUT_PROTOCOL;
struct _EFI_SIMPLE_TEXT_OUTPUT_PROTOCOL {
    EFI_STATUS (EFIAPI *Reset)(EFI_SIMPLE_TEXT_OUTPUT_PROTOCOL *This, UINT8 ExtendedVerification);
    EFI_STATUS (EFIAPI *OutputString)(EFI_SIMPLE_TEXT_OUTPUT_PROTOCOL *This, CHAR16 *String);
    VOID *TestString;
    VOID *QueryMode;
    VOID *SetMode;
    EFI_STATUS (EFIAPI *SetAttribute)(EFI_SIMPLE_TEXT_OUTPUT_PROTOCOL *This, UINTN Attribute);
    EFI_STATUS (EFIAPI *ClearScreen)(EFI_SIMPLE_TEXT_OUTPUT_PROTOCOL *This);
    VOID *SetCursorPosition;
    VOID *EnableCursor;
    VOID *Mode;
};

typedef struct {
    EFI_TABLE_HEADER Hdr;
    VOID *RaiseTPL; VOID *RestoreTPL; VOID *AllocatePages; VOID *FreePages;
    VOID *GetMemoryMap; VOID *AllocatePool; VOID *FreePool;
    VOID *CreateEvent;  VOID *SetTimer;
    EFI_STATUS (EFIAPI *WaitForEvent)(UINTN NumberOfEvents, VOID **Event, UINTN *Index);
    VOID *SignalEvent; VOID *CloseEvent; VOID *CheckEvent;
    VOID *InstallProtocolInterface; VOID *ReinstallProtocolInterface;
    VOID *UninstallProtocolInterface; VOID *HandleProtocol; VOID *Reserved2;
    VOID *RegisterProtocolNotify; VOID *LocateHandle; VOID *LocateDevicePath;
    VOID *InstallConfigurationTable;
    VOID *LoadImage; VOID *StartImage; VOID *Exit; VOID *UnloadImage;
    VOID *ExitBootServices;
    VOID *GetNextMonotonicCount; VOID *Stall; VOID *SetWatchdogTimer;
    VOID *ConnectController; VOID *DisconnectController;
    VOID *OpenProtocol; VOID *CloseProtocol; VOID *OpenProtocolInformation;
    VOID *ProtocolsPerHandle; VOID *LocateHandleBuffer; VOID *LocateProtocol;
    VOID *InstallMultipleProtocolInterfaces; VOID *UninstallMultipleProtocolInterfaces;
    VOID *CalculateCrc32; VOID *CopyMem; VOID *SetMem;
    VOID *CreateEventEx;
} EFI_BOOT_SERVICES;

typedef struct {
    EFI_TABLE_HEADER Hdr;
    VOID *GetTime; VOID *SetTime; VOID *GetWakeupTime; VOID *SetWakeupTime;
    VOID *SetVirtualAddressMap; VOID *ConvertPointer;
    VOID *GetVariable; VOID *GetNextVariableName; VOID *SetVariable;
    VOID *GetNextHighMonotonicCount;
    EFI_STATUS (EFIAPI *ResetSystem)(UINT32 ResetType, EFI_STATUS ResetStatus, UINTN DataSize, VOID *ResetData);
} EFI_RUNTIME_SERVICES;

typedef struct {
    EFI_TABLE_HEADER Hdr;
    CHAR16 *FirmwareVendor;
    UINT32 FirmwareRevision;
    EFI_HANDLE ConsoleInHandle;
    EFI_SIMPLE_TEXT_INPUT_PROTOCOL *ConIn;
    EFI_HANDLE ConsoleOutHandle;
    EFI_SIMPLE_TEXT_OUTPUT_PROTOCOL *ConOut;
    EFI_HANDLE StandardErrorHandle;
    EFI_SIMPLE_TEXT_OUTPUT_PROTOCOL *StdErr;
    EFI_RUNTIME_SERVICES *RuntimeServices;
    EFI_BOOT_SERVICES *BootServices;
} EFI_SYSTEM_TABLE;

/* --- Globals --- */
static EFI_SYSTEM_TABLE *gST;
static EFI_SIMPLE_TEXT_OUTPUT_PROTOCOL *gOut;
static EFI_SIMPLE_TEXT_INPUT_PROTOCOL *gIn;
static EFI_BOOT_SERVICES *gBS;

static void print(CHAR16 *s) { gOut->OutputString(gOut, s); }

static CHAR16 hexbuf[4] = {0,0,0,0};
static CHAR16 hex_char(UINT8 n) { return n < 10 ? L'0' + n : L'A' + n - 10; }
static void print_hex8(UINT8 v) {
    hexbuf[0] = hex_char(v >> 4); hexbuf[1] = hex_char(v & 0xF); hexbuf[2] = 0;
    print(hexbuf);
}
static void print_hex32(UINT32 v) {
    print_hex8((UINT8)(v >> 24)); print_hex8((UINT8)(v >> 16));
    print_hex8((UINT8)(v >> 8));  print_hex8((UINT8)v);
}

static CHAR16 linebuf[128];
static UINT8  asciibuf[128];

static int read_line(void) {
    int pos = 0;
    CHAR16 echo[2] = {0, 0};
    for (;;) {
        UINTN idx;
        gBS->WaitForEvent(1, &gIn->WaitForKey, &idx);
        EFI_INPUT_KEY key;
        if (gIn->ReadKeyStroke(gIn, &key) != 0) continue;
        CHAR16 ch = key.UnicodeChar;
        if (ch == L'\r' || ch == L'\n') { print(L"\r\n"); asciibuf[pos] = 0; return pos; }
        if (ch == L'\b' || ch == 8) {
            if (pos > 0) { pos--; print(L"\b \b"); }
            continue;
        }
        if (ch >= 32 && pos < 126) {
            asciibuf[pos++] = (UINT8)ch;
            echo[0] = ch; print(echo);
        }
    }
}

static UINT64 parse_hex(int *idx) {
    UINT64 v = 0;
    int i = *idx;
    while (asciibuf[i] == ' ') i++;
    if (asciibuf[i] == '0' && asciibuf[i+1] == 'x') i += 2;
    for (;;) {
        UINT8 c = asciibuf[i];
        if (c >= '0' && c <= '9') { v = v * 16 + (c - '0'); i++; }
        else if (c >= 'a' && c <= 'f') { v = v * 16 + (c - 'a' + 10); i++; }
        else if (c >= 'A' && c <= 'F') { v = v * 16 + (c - 'A' + 10); i++; }
        else break;
    }
    *idx = i;
    return v;
}

static int streq(UINT8 *a, const char *b) {
    while (*b) { if (*a != (UINT8)*b) return 0; a++; b++; }
    return (*a == 0 || *a == ' ');
}

UINT8 __inbyte(UINT16 Port);
void __outbyte(UINT16 Port, UINT8 Data);
#pragma intrinsic(__inbyte, __outbyte)

static UINT8 port_in_byte(UINT16 port) { return __inbyte(port); }
static void port_out_byte(UINT16 port, UINT8 val) { __outbyte(port, val); }

EFI_STATUS EFIAPI EfiMain(EFI_HANDLE ImageHandle, EFI_SYSTEM_TABLE *SystemTable) {
    gST = SystemTable;
    gOut = SystemTable->ConOut;
    gIn = SystemTable->ConIn;
    gBS = SystemTable->BootServices;

    gOut->ClearScreen(gOut);
    gOut->SetAttribute(gOut, 0x0F); /* bright white on black */
    print(L"=== Codex Diagnostic Shell (UEFI) ===\r\n");
    print(L"Type 'help' for commands.\r\n\r\n");

    for (;;) {
        gOut->SetAttribute(gOut, 0x0A); /* green */
        print(L"diag> ");
        gOut->SetAttribute(gOut, 0x0F); /* white */
        int len = read_line();
        if (len == 0) continue;

        if (streq(asciibuf, "help")) {
            print(L"Commands:\r\n");
            print(L"  help              show this\r\n");
            print(L"  peek <hex>        read 8 bytes at address\r\n");
            print(L"  poke <hex> <hex>  write byte at address\r\n");
            print(L"  in <hex>          read I/O port byte\r\n");
            print(L"  out <hex> <hex>   write I/O port byte\r\n");
            print(L"  echo <text>       echo text\r\n");
            print(L"  reboot            reset system\r\n");
        }
        else if (streq(asciibuf, "peek")) {
            int idx = 5;
            UINT64 addr = parse_hex(&idx);
            print_hex32((UINT32)(addr >> 32)); print_hex32((UINT32)addr);
            print(L": ");
            volatile UINT8 *p = (volatile UINT8 *)addr;
            for (int i = 0; i < 8; i++) {
                print_hex8(p[i]);
                if (i < 7) print(L" ");
            }
            print(L"\r\n");
        }
        else if (streq(asciibuf, "poke")) {
            int idx = 5;
            UINT64 addr = parse_hex(&idx);
            UINT64 val = parse_hex(&idx);
            volatile UINT8 *p = (volatile UINT8 *)addr;
            *p = (UINT8)val;
            print(L"OK\r\n");
        }
        else if (streq(asciibuf, "in")) {
            int idx = 3;
            UINT64 port = parse_hex(&idx);
            UINT8 val = port_in_byte((UINT16)port);
            print(L"0x"); print_hex8(val); print(L"\r\n");
        }
        else if (streq(asciibuf, "out")) {
            int idx = 4;
            UINT64 port = parse_hex(&idx);
            UINT64 val = parse_hex(&idx);
            port_out_byte((UINT16)port, (UINT8)val);
            print(L"OK\r\n");
        }
        else if (streq(asciibuf, "echo")) {
            int i = 5;
            while (asciibuf[i] == ' ') i++;
            CHAR16 tmp[2] = {0,0};
            while (asciibuf[i]) { tmp[0] = asciibuf[i++]; print(tmp); }
            print(L"\r\n");
        }
        else if (streq(asciibuf, "reboot")) {
            print(L"Rebooting...\r\n");
            gST->RuntimeServices->ResetSystem(0, 0, 0, 0);
        }
        else {
            print(L"? (type help)\r\n");
        }
    }
}
