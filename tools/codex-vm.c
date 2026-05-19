/*
 * codex-vm: Minimal WHP-based VM for Codex bare-metal binaries.
 * Replaces QEMU for development. Serial on TCP sockets, IDE from raw file.
 *
 * Usage: codex-vm.exe -kernel file.cdx [-disk file.img] [-mem 2048]
 *        [-data-port 12345] [-ctrl-port 12346]
 *        [-watch 0x1a6f7c5] [-watch-size 8]
 *        [-headless]
 */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <WinHvPlatform.h>
#include <winsock2.h>
#include <ws2tcpip.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#pragma comment(lib, "WinHvPlatform.lib")
#pragma comment(lib, "ws2_32.lib")
#pragma comment(lib, "gdi32.lib")
#pragma comment(lib, "user32.lib")

/* Forward declarations (defined later in the file) */
static void *guest_mem;
static size_t guest_mem_size;
static WHV_PARTITION_HANDLE partition;

/* VGA constants (used by both VGA display and UEFI emulation) */
#define VGA_BASE     0xB8000
#define VGA_COLS     80
#define VGA_ROWS     25

/* ══ PS/2 Keyboard Queue ══ */
#define KBD_QUEUE_SIZE 64
static unsigned char kbd_queue[KBD_QUEUE_SIZE];
static int kbd_head = 0, kbd_count = 0;
static volatile int kbd_irq_pending = 0;

static void kbd_enqueue(unsigned char scancode) {
    if (kbd_count < KBD_QUEUE_SIZE) {
        kbd_queue[(kbd_head + kbd_count) % KBD_QUEUE_SIZE] = scancode;
        kbd_count++;
    }
}

static int kbd_dequeue(void) {
    if (kbd_count <= 0) return -1;
    unsigned char sc = kbd_queue[kbd_head];
    kbd_head = (kbd_head + 1) % KBD_QUEUE_SIZE;
    kbd_count--;
    return sc;
}

/* Win32 VK → PS/2 Set 1 scancode (common keys) */
static unsigned char vk_to_scancode(int vk) {
    switch (vk) {
    case VK_ESCAPE: return 0x01;
    case '1': return 0x02; case '2': return 0x03; case '3': return 0x04;
    case '4': return 0x05; case '5': return 0x06; case '6': return 0x07;
    case '7': return 0x08; case '8': return 0x09; case '9': return 0x0A;
    case '0': return 0x0B; case VK_OEM_MINUS: return 0x0C;
    case VK_OEM_PLUS: return 0x0D; case VK_BACK: return 0x0E;
    case VK_TAB: return 0x0F;
    case 'Q': return 0x10; case 'W': return 0x11; case 'E': return 0x12;
    case 'R': return 0x13; case 'T': return 0x14; case 'Y': return 0x15;
    case 'U': return 0x16; case 'I': return 0x17; case 'O': return 0x18;
    case 'P': return 0x19; case VK_OEM_4: return 0x1A; case VK_OEM_6: return 0x1B;
    case VK_RETURN: return 0x1C; case VK_LCONTROL: case VK_CONTROL: return 0x1D;
    case 'A': return 0x1E; case 'S': return 0x1F; case 'D': return 0x20;
    case 'F': return 0x21; case 'G': return 0x22; case 'H': return 0x23;
    case 'J': return 0x24; case 'K': return 0x25; case 'L': return 0x26;
    case VK_OEM_1: return 0x27; case VK_OEM_7: return 0x28;
    case VK_OEM_3: return 0x29; case VK_LSHIFT: case VK_SHIFT: return 0x2A;
    case VK_OEM_5: return 0x2B;
    case 'Z': return 0x2C; case 'X': return 0x2D; case 'C': return 0x2E;
    case 'V': return 0x2F; case 'B': return 0x30; case 'N': return 0x31;
    case 'M': return 0x32; case VK_OEM_COMMA: return 0x33;
    case VK_OEM_PERIOD: return 0x34; case VK_OEM_2: return 0x35;
    case VK_RSHIFT: return 0x36; case VK_SPACE: return 0x39;
    case VK_F1: return 0x3B; case VK_F2: return 0x3C; case VK_F3: return 0x3D;
    case VK_F4: return 0x3E; case VK_F5: return 0x3F; case VK_F6: return 0x40;
    case VK_F7: return 0x41; case VK_F8: return 0x42; case VK_F9: return 0x43;
    case VK_F10: return 0x44; case VK_F11: return 0x57; case VK_F12: return 0x58;
    case VK_UP: return 0x48; case VK_DOWN: return 0x50;
    case VK_LEFT: return 0x4B; case VK_RIGHT: return 0x4D;
    case VK_HOME: return 0x47; case VK_END: return 0x4F;
    case VK_PRIOR: return 0x49; case VK_NEXT: return 0x51;
    case VK_INSERT: return 0x52; case VK_DELETE: return 0x53;
    default: return 0;
    }
}

/* ══ Mouse State ══ */
#define MOUSE_BUF_ADDR 28684
static int mouse_captured = 0;

/* ══ UEFI Emulation ══ */
#define UEFI_TABLE_PAGE   0xF0000   /* fake SystemTable + protocols */
#define UEFI_TRAP_PAGE    0xF1000   /* filled with HLT opcodes — halt-based dispatch */
#define UEFI_SYSTABLE_PTR 0x8000    /* guest reads SystemTable from here */

/* Trap addresses (offsets from UEFI_TRAP_PAGE, each 8 bytes apart) */
#define UEFI_TRAP_CONIN_RESET       0
#define UEFI_TRAP_CONIN_READKEY     1
#define UEFI_TRAP_CONOUT_RESET      4
#define UEFI_TRAP_CONOUT_PRINT      5
#define UEFI_TRAP_CONOUT_TESTSTR    6
#define UEFI_TRAP_CONOUT_QUERYMODE  7
#define UEFI_TRAP_CONOUT_SETMODE    8
#define UEFI_TRAP_CONOUT_SETATTR    9
#define UEFI_TRAP_CONOUT_CLEARSCR   10
#define UEFI_TRAP_CONOUT_SETCURSOR  11
#define UEFI_TRAP_CONOUT_ENABLECUR  12
#define UEFI_TRAP_CONIN_READKEYEX   20
#define UEFI_TRAP_BOOT_ALLOC_PAGES  30
#define UEFI_TRAP_BOOT_FREE_PAGES   31
#define UEFI_TRAP_BOOT_GET_MEMMAP   32
#define UEFI_TRAP_BOOT_ALLOC_POOL   33
#define UEFI_TRAP_BOOT_FREE_POOL    34
#define UEFI_TRAP_BOOT_EXIT_BOOTSVC 35
#define UEFI_TRAP_BOOT_STALL        36
#define UEFI_TRAP_BOOT_SETWATCHDOG  37
#define UEFI_TRAP_BOOT_HANDLEPROTO  38
#define UEFI_TRAP_BOOT_LOCHANDLE    39
#define UEFI_TRAP_BOOT_STUB         40
#define UEFI_TRAP_GOP_QUERYMODE     41
#define UEFI_TRAP_GOP_SETMODE       42
#define UEFI_TRAP_GOP_BLT           43
#define UEFI_TRAP_GOP_GETMODE       44

#define EFI_SUCCESS       0ULL
#define EFI_NOT_READY     0x8000000000000006ULL

/* GOP (Graphics Output Protocol) state */
#define GOP_FB_ADDR       0x40000000ULL  /* guest physical address of framebuffer (1 GB) */
#define GOP_MAX_W         1024
#define GOP_MAX_H         768
#define GOP_FB_SIZE       (GOP_MAX_W * GOP_MAX_H * 4)  /* 3 MB max */
static int gop_active = 0;
static int gop_width = 640;
static int gop_height = 480;
static int gop_stride = 640;
static unsigned char *gop_fb = NULL;  /* host-side framebuffer copy for rendering */
static HWND vga_hwnd;  /* forward decl — defined in VGA section */

static int uefi_mode = 0;          /* 1 when running a UEFI app */
static int uefi_cursor_row = 0;
static int uefi_cursor_col = 0;
static unsigned char uefi_attr = 0x07; /* white on black */
static unsigned long long uefi_alloc_next = 0x10000000; /* next free page for AllocatePages */

static void uefi_setup_tables(void *mem) {
    unsigned char *base = (unsigned char *)mem + UEFI_TABLE_PAGE;
    memset(base, 0, 4096);

    /* Helper: write a 64-bit value at offset */
    #define W64(off, val) do { unsigned long long v = (val); memcpy(base + (off), &v, 8); } while(0)

    /* SystemTable at 0x70000 */
    /* +48 = ConIn protocol pointer */
    W64(48, UEFI_TABLE_PAGE + 0x100);
    /* +64 = ConOut protocol pointer */
    W64(64, UEFI_TABLE_PAGE + 0x200);

    /* ConIn protocol at 0x70100 */
    #define TRAP(id) (UEFI_TRAP_PAGE + (id) * 8)
    W64(0x100 + 0,  TRAP(UEFI_TRAP_CONIN_RESET));
    W64(0x100 + 8,  TRAP(UEFI_TRAP_CONIN_READKEY));

    /* RuntimeServices at 0x70400 — stub all to traps */
    W64(88, UEFI_TABLE_PAGE + 0x400);

    /* BootServices at 0x70500 */
    W64(96, UEFI_TABLE_PAGE + 0x500);
    /* BootServices function table (24-byte header, then functions) */
    W64(0x500 + 40,  TRAP(UEFI_TRAP_BOOT_ALLOC_PAGES));
    W64(0x500 + 48,  TRAP(UEFI_TRAP_BOOT_FREE_PAGES));
    W64(0x500 + 56,  TRAP(UEFI_TRAP_BOOT_GET_MEMMAP));
    W64(0x500 + 64,  TRAP(UEFI_TRAP_BOOT_ALLOC_POOL));
    W64(0x500 + 72,  TRAP(UEFI_TRAP_BOOT_FREE_POOL));
    W64(0x500 + 232, TRAP(UEFI_TRAP_BOOT_EXIT_BOOTSVC));
    W64(0x500 + 248, TRAP(UEFI_TRAP_BOOT_STALL));
    W64(0x500 + 256, TRAP(UEFI_TRAP_BOOT_SETWATCHDOG));
    W64(0x500 + 152, TRAP(UEFI_TRAP_BOOT_HANDLEPROTO));
    W64(0x500 + 176, TRAP(UEFI_TRAP_BOOT_LOCHANDLE));
    /* Fill ALL empty protocol slots with the generic stub trap */
    for (int off = 8; off < 0x400; off += 8) {
        unsigned long long *slot = (unsigned long long *)(base + off);
        if (*slot == 0) *slot = TRAP(UEFI_TRAP_BOOT_STUB);
    }
    /* SystemTable sub-tables */
    for (int tbl = 0x100; tbl <= 0x500; tbl += 0x100) {
        for (int off = 0; off < 0x100; off += 8) {
            unsigned long long *slot = (unsigned long long *)(base + tbl + off);
            if (*slot == 0) *slot = TRAP(UEFI_TRAP_BOOT_STUB);
        }
    }

    /* ConInEx at 0x70300 — for ReadKeyStrokeEx */
    W64(0x300 + 248, TRAP(UEFI_TRAP_CONIN_READKEYEX));

    /* ConOut protocol at 0xF0200 */
    W64(0x200 + 0,   TRAP(UEFI_TRAP_CONOUT_RESET));
    W64(0x200 + 8,   TRAP(UEFI_TRAP_CONOUT_PRINT));
    W64(0x200 + 16,  TRAP(UEFI_TRAP_CONOUT_TESTSTR));
    W64(0x200 + 24,  TRAP(UEFI_TRAP_CONOUT_QUERYMODE));
    W64(0x200 + 32,  TRAP(UEFI_TRAP_CONOUT_SETMODE));
    W64(0x200 + 40,  TRAP(UEFI_TRAP_CONOUT_SETATTR));
    W64(0x200 + 48,  TRAP(UEFI_TRAP_CONOUT_CLEARSCR));
    W64(0x200 + 56,  TRAP(UEFI_TRAP_CONOUT_SETCURSOR));
    W64(0x200 + 64,  TRAP(UEFI_TRAP_CONOUT_ENABLECUR));
    /* +72 = Mode pointer → SIMPLE_TEXT_OUTPUT_MODE at 0xF0280 */
    W64(0x200 + 72,  UEFI_TABLE_PAGE + 0x280);
    /* Mode structure at 0xF0280 */
    /* MaxMode (INT32) */ *(int *)(base + 0x280) = 1;
    /* Mode (INT32) */    *(int *)(base + 0x284) = 0;
    /* Attribute (INT32) */ *(int *)(base + 0x288) = 0x07; /* white on black */
    /* CursorColumn (INT32) */ *(int *)(base + 0x28C) = 0;
    /* CursorRow (INT32) */    *(int *)(base + 0x290) = 0;
    /* CursorVisible (BOOL) */ *(int *)(base + 0x294) = 1;

    /* ConIn.WaitForKey event handle at ConIn+16 — just a non-null value */
    W64(0x100 + 16, 0xDEAD0001);

    /* GOP (Graphics Output Protocol) at 0xF0600
       The guest discovers this via LocateProtocol or a known SystemTable offset.
       We store the pointer at SystemTable+112 (non-standard, Codex convention). */
    W64(112, UEFI_TABLE_PAGE + 0x600);
    /* GOP protocol functions */
    W64(0x600 + 0,   TRAP(UEFI_TRAP_GOP_QUERYMODE));   /* QueryMode */
    W64(0x600 + 8,   TRAP(UEFI_TRAP_GOP_SETMODE));     /* SetMode */
    W64(0x600 + 16,  TRAP(UEFI_TRAP_GOP_BLT));         /* Blt */
    /* +24 = Mode pointer → GOP_MODE at 0xF0680 */
    W64(0x600 + 24,  UEFI_TABLE_PAGE + 0x680);
    /* GOP_MODE structure at 0xF0680 */
    /* MaxMode (UINT32) */          *(int *)(base + 0x680) = 3;
    /* Mode (UINT32) */             *(int *)(base + 0x684) = 0;
    /* +8 = Info pointer → 0xF06C0 */
    W64(0x688, UEFI_TABLE_PAGE + 0x6C0);
    /* SizeOfInfo (UINTN) */        W64(0x690, 36);
    /* FrameBufferBase (EFI_PHYSICAL_ADDRESS) */ W64(0x698, GOP_FB_ADDR);
    /* FrameBufferSize (UINTN) */   W64(0x6A0, GOP_FB_SIZE);
    /* GOP_MODE_INFO at 0xF06C0 */
    /* Version (UINT32) */          *(int *)(base + 0x6C0) = 0;
    /* HorizontalResolution */      *(int *)(base + 0x6C4) = 640;
    /* VerticalResolution */        *(int *)(base + 0x6C8) = 480;
    /* PixelFormat (0=RGB, 1=BGR) */ *(int *)(base + 0x6CC) = 1; /* BGR for standard UEFI */
    /* PixelsPerScanLine */         *(int *)(base + 0x6D4) = 640;

    #undef W64
    #undef TRAP

    /* Store SystemTable pointer at 0x8000 */
    unsigned long long st_ptr = UEFI_TABLE_PAGE;
    memcpy((unsigned char *)mem + UEFI_SYSTABLE_PTR, &st_ptr, 8);
}

/* Handle a UEFI trap — guest called a protocol function that faulted
   into the trap page. Returns 1 if handled, 0 if not a UEFI trap. */
static int uefi_handle_trap(WHV_RUN_VP_EXIT_CONTEXT *ctx) {
    unsigned long long rip = ctx->VpContext.Rip;
    /* RIP points AFTER the HLT instruction (1 byte), so subtract 1 to get the trap address */
    unsigned long long trap_addr = rip - 1;
    if (trap_addr < UEFI_TRAP_PAGE || trap_addr >= UEFI_TRAP_PAGE + 0x1000) return 0;

    int func_id = (int)((trap_addr - UEFI_TRAP_PAGE) / 8);
    unsigned long long rax_result = EFI_SUCCESS;

    /* Read guest registers for arguments (MS x64 ABI: RCX, RDX, R8, R9) */
    WHV_REGISTER_NAME arg_names[5] = {
        WHvX64RegisterRcx, WHvX64RegisterRdx, WHvX64RegisterR8,
        WHvX64RegisterR9, WHvX64RegisterRsp
    };
    WHV_REGISTER_VALUE arg_vals[5];
    WHvGetVirtualProcessorRegisters(partition, 0, arg_names, 5, arg_vals);
    unsigned long long rcx = arg_vals[0].Reg64;
    unsigned long long rdx = arg_vals[1].Reg64;
    unsigned long long r8  = arg_vals[2].Reg64;
    unsigned long long rsp = arg_vals[4].Reg64;

    switch (func_id) {
    case UEFI_TRAP_CONOUT_PRINT: {
        /* OutputString(This, String) — RDX = UTF-16LE string in guest mem */
        if (rdx > 0 && rdx + 2 < guest_mem_size) {
            unsigned char *str = (unsigned char *)guest_mem + rdx;
            unsigned char *vga = (unsigned char *)guest_mem + VGA_BASE;
            for (int i = 0; i < 8192; i += 2) {
                unsigned short ch = str[i] | (str[i + 1] << 8);
                if (ch == 0) break;
                if (ch == '\n') {
                    uefi_cursor_col = 0;
                    uefi_cursor_row++;
                    if (uefi_cursor_row >= VGA_ROWS) {
                        /* Scroll up */
                        memmove(vga, vga + VGA_COLS * 2, (VGA_ROWS - 1) * VGA_COLS * 2);
                        memset(vga + (VGA_ROWS - 1) * VGA_COLS * 2, 0, VGA_COLS * 2);
                        uefi_cursor_row = VGA_ROWS - 1;
                    }
                } else if (ch == '\r') {
                    uefi_cursor_col = 0;
                } else {
                    if (uefi_cursor_col >= VGA_COLS) {
                        uefi_cursor_col = 0;
                        uefi_cursor_row++;
                        if (uefi_cursor_row >= VGA_ROWS) {
                            memmove(vga, vga + VGA_COLS * 2, (VGA_ROWS - 1) * VGA_COLS * 2);
                            memset(vga + (VGA_ROWS - 1) * VGA_COLS * 2, 0, VGA_COLS * 2);
                            uefi_cursor_row = VGA_ROWS - 1;
                        }
                    }
                    int off = (uefi_cursor_row * VGA_COLS + uefi_cursor_col) * 2;
                    vga[off] = (ch < 128) ? (unsigned char)ch : '?';
                    vga[off + 1] = uefi_attr;
                    uefi_cursor_col++;
                }
            }
        }
        break;
    }
    case UEFI_TRAP_CONOUT_SETATTR:
        /* SetAttribute(This, Attr) — RDX = attribute */
        uefi_attr = (unsigned char)(rdx & 0xFF);
        *(int *)((unsigned char *)guest_mem + UEFI_TABLE_PAGE + 0x288) = (int)rdx;
        break;

    case UEFI_TRAP_CONOUT_CLEARSCR: {
        unsigned char *vga = (unsigned char *)guest_mem + VGA_BASE;
        for (int i = 0; i < VGA_ROWS * VGA_COLS; i++) {
            vga[i * 2] = ' ';
            vga[i * 2 + 1] = uefi_attr;
        }
        uefi_cursor_row = 0;
        uefi_cursor_col = 0;
        break;
    }
    case UEFI_TRAP_CONOUT_SETCURSOR:
        /* SetCursorPosition(This, Col, Row) — RDX=col, R8=row */
        uefi_cursor_col = (int)(rdx & 0xFF);
        uefi_cursor_row = (int)(r8 & 0xFF);
        *(int *)((unsigned char *)guest_mem + UEFI_TABLE_PAGE + 0x28C) = uefi_cursor_col;
        *(int *)((unsigned char *)guest_mem + UEFI_TABLE_PAGE + 0x290) = uefi_cursor_row;
        break;

    case UEFI_TRAP_CONOUT_ENABLECUR:
        /* EnableCursor(This, Visible) — ignore */
        break;

    case UEFI_TRAP_CONIN_READKEY: {
        /* ReadKeyStroke(This, Key) — RDX = pointer to EFI_INPUT_KEY (4 bytes)
           Block until a key arrives to prevent guest busy-polling stack overflow. */
        int sc = kbd_dequeue();
        while (sc < 0 || (sc & 0x80)) { Sleep(10); sc = kbd_dequeue(); }
        if (sc < 0) {
            rax_result = EFI_NOT_READY;
        } else {
            unsigned char ascii = 0;
            unsigned short scan = 0;
            /* Map PS/2 scancode to UEFI scan code + ASCII */
            unsigned char ps2 = (unsigned char)(sc & 0x7F);
            if (sc & 0x80) { /* key up — ignore for ReadKeyStroke */ rax_result = EFI_NOT_READY; break; }
            ascii = vk_to_scancode(0); /* placeholder — need ps2 to ascii */
            /* Direct PS/2-to-ASCII for common keys */
            switch (ps2) {
            case 0x1C: ascii = 13; break; /* Enter */
            case 0x0E: ascii = 8; break;  /* Backspace */
            case 0x0F: ascii = 9; break;  /* Tab */
            case 0x39: ascii = 32; break; /* Space */
            case 0x01: scan = 23; break;  /* Escape */
            case 0x48: scan = 1; break;   /* Up */
            case 0x50: scan = 2; break;   /* Down */
            case 0x4D: scan = 3; break;   /* Right */
            case 0x4B: scan = 4; break;   /* Left */
            case 0x47: scan = 5; break;   /* Home */
            case 0x4F: scan = 6; break;   /* End */
            case 0x49: scan = 9; break;   /* Page Up */
            case 0x51: scan = 10; break;  /* Page Down */
            case 0x52: scan = 7; break;   /* Insert */
            case 0x53: scan = 8; break;   /* Delete */
            case 0x3B: scan = 11; break;  /* F1 */
            case 0x3C: scan = 12; break;  /* F2 */
            case 0x3D: scan = 13; break;  /* F3 */
            case 0x3E: scan = 14; break;  /* F4 */
            case 0x3F: scan = 15; break;  /* F5 */
            case 0x44: scan = 20; break;  /* F10 */
            case 0x57: scan = 21; break;  /* F11 */
            case 0x58: scan = 22; break;  /* F12 */
            default:
                /* Letter/number keys — convert PS/2 to ASCII */
                if (ps2 >= 0x10 && ps2 <= 0x19) { /* Q-P */
                    static const char qwerty_top[] = "qwertyuiop";
                    ascii = qwerty_top[ps2 - 0x10];
                } else if (ps2 >= 0x1E && ps2 <= 0x26) { /* A-L */
                    static const char qwerty_mid[] = "asdfghjkl";
                    ascii = qwerty_mid[ps2 - 0x1E];
                } else if (ps2 >= 0x2C && ps2 <= 0x32) { /* Z-M */
                    static const char qwerty_bot[] = "zxcvbnm";
                    ascii = qwerty_bot[ps2 - 0x2C];
                } else if (ps2 >= 0x02 && ps2 <= 0x0B) { /* 1-0 */
                    static const char digits[] = "1234567890";
                    ascii = digits[ps2 - 0x02];
                }
                break;
            }
            if (rdx > 0 && rdx + 4 <= guest_mem_size) {
                unsigned char *key = (unsigned char *)guest_mem + rdx;
                key[0] = scan & 0xFF; key[1] = (scan >> 8) & 0xFF;
                key[2] = ascii; key[3] = 0;
            }
        }
        break;
    }
    case UEFI_TRAP_CONIN_READKEYEX: {
        /* ReadKeyStrokeEx — block until key press */
        int sc = kbd_dequeue();
        while (sc < 0 || (sc & 0x80)) { Sleep(10); sc = kbd_dequeue(); }
        if (sc < 0) {
            rax_result = EFI_NOT_READY;
        } else {
            /* For now, delegate to the same logic but write extended struct */
            /* EFI_KEY_DATA: 4 bytes InputKey + 4 bytes ShiftState + ... */
            /* The guest reads scan_code (u16) + char (u16) + shift_state (u32) */
            unsigned char ps2 = (unsigned char)(sc & 0x7F);
            unsigned char ascii = 0;
            unsigned short scan = 0;
            /* Same mapping as above */
            switch (ps2) {
            case 0x1C: ascii = 13; break;
            case 0x0E: ascii = 8; break;
            case 0x0F: ascii = 9; break;
            case 0x39: ascii = 32; break;
            case 0x01: scan = 23; break;
            case 0x48: scan = 1; break;
            case 0x50: scan = 2; break;
            case 0x4D: scan = 3; break;
            case 0x4B: scan = 4; break;
            case 0x47: scan = 5; break;
            case 0x4F: scan = 6; break;
            case 0x49: scan = 9; break;
            case 0x51: scan = 10; break;
            case 0x3B: scan = 11; break;
            case 0x57: scan = 21; break;
            case 0x58: scan = 22; break;
            default:
                if (ps2 >= 0x10 && ps2 <= 0x19) { static const char t[] = "qwertyuiop"; ascii = t[ps2 - 0x10]; }
                else if (ps2 >= 0x1E && ps2 <= 0x26) { static const char t[] = "asdfghjkl"; ascii = t[ps2 - 0x1E]; }
                else if (ps2 >= 0x2C && ps2 <= 0x32) { static const char t[] = "zxcvbnm"; ascii = t[ps2 - 0x2C]; }
                else if (ps2 >= 0x02 && ps2 <= 0x0B) { static const char t[] = "1234567890"; ascii = t[ps2 - 0x02]; }
                break;
            }
            if (rdx > 0 && rdx + 8 <= guest_mem_size) {
                unsigned char *key = (unsigned char *)guest_mem + rdx;
                key[0] = scan & 0xFF; key[1] = (scan >> 8) & 0xFF;
                key[2] = ascii; key[3] = 0;
                /* ShiftState at offset 4: set "valid" bit */
                unsigned int shift = 0x80000000; /* valid flag */
                memcpy(key + 4, &shift, 4);
            }
        }
        break;
    }
    case UEFI_TRAP_BOOT_ALLOC_PAGES: {
        /* AllocatePages(Type, MemType, Pages, Memory*) — RCX=type, RDX=memtype, R8=pages, R9=&addr
           Type: 0=AllocateAnyPages, 1=AllocateMaxAddress, 2=AllocateAddress
           For AllocateAddress (type=2), *R9 contains the requested address — just return success.
           For other types, return from our allocator. */
        if (rcx == 2) {
            /* AllocateAddress: the caller already set *R9 to the desired address. No-op. */
        } else {
            unsigned long long pages = r8;
            unsigned long long addr = uefi_alloc_next;
            uefi_alloc_next += pages * 4096;
            if (arg_vals[3].Reg64 > 0 && arg_vals[3].Reg64 + 8 <= guest_mem_size) {
                memcpy((unsigned char *)guest_mem + arg_vals[3].Reg64, &addr, 8);
            }
        }
        break;
    }
    case UEFI_TRAP_BOOT_FREE_PAGES:
    case UEFI_TRAP_BOOT_FREE_POOL:
        break; /* no-op */

    case UEFI_TRAP_BOOT_GET_MEMMAP: {
        /* GetMemoryMap — return a minimal map, mostly to provide MapKey */
        /* RCX=&MapSize, RDX=MemoryMap, R8=&MapKey, R9=&DescSize */
        if (rcx > 0 && rcx + 8 <= guest_mem_size) {
            unsigned long long map_size = 48;
            memcpy((unsigned char *)guest_mem + rcx, &map_size, 8);
        }
        if (r8 > 0 && r8 + 8 <= guest_mem_size) {
            unsigned long long map_key = 0x1234;
            memcpy((unsigned char *)guest_mem + r8, &map_key, 8);
        }
        if (arg_vals[3].Reg64 > 0 && arg_vals[3].Reg64 + 8 <= guest_mem_size) {
            unsigned long long desc_size = 48;
            memcpy((unsigned char *)guest_mem + arg_vals[3].Reg64, &desc_size, 8);
        }
        break;
    }
    case UEFI_TRAP_BOOT_ALLOC_POOL: {
        /* AllocatePool(PoolType, Size, Buffer*) — RDX=size, R8=&buffer */
        unsigned long long size = rdx;
        unsigned long long addr = uefi_alloc_next;
        uefi_alloc_next = (uefi_alloc_next + size + 4095) & ~4095ULL;
        if (r8 > 0 && r8 + 8 <= guest_mem_size) {
            memcpy((unsigned char *)guest_mem + r8, &addr, 8);
        }
        break;
    }
    case UEFI_TRAP_BOOT_EXIT_BOOTSVC:
    case UEFI_TRAP_BOOT_STALL:
        break; /* stubs */

    case UEFI_TRAP_BOOT_HANDLEPROTO:
    case UEFI_TRAP_BOOT_LOCHANDLE:
        rax_result = 0x800000000000000EULL; /* EFI_NOT_FOUND */
        break;

    case UEFI_TRAP_CONOUT_QUERYMODE:
        /* QueryMode(This, ModeNumber, Columns, Rows) — R8=&Cols, R9=&Rows */
        if (r8 > 0 && r8 + 8 <= guest_mem_size) {
            unsigned long long cols = VGA_COLS;
            memcpy((unsigned char *)guest_mem + r8, &cols, 8);
        }
        if (arg_vals[3].Reg64 > 0 && arg_vals[3].Reg64 + 8 <= guest_mem_size) {
            unsigned long long rows = VGA_ROWS;
            memcpy((unsigned char *)guest_mem + arg_vals[3].Reg64, &rows, 8);
        }
        break;

    case UEFI_TRAP_GOP_QUERYMODE: {
        /* QueryMode(This, ModeNumber, SizeOfInfo, Info) — RDX=mode, R8=&size, R9=&info_ptr */
        int mode_num = (int)rdx;
        int w = 640, h = 480;
        if (mode_num == 1) { w = 800; h = 600; }
        else if (mode_num == 2) { w = 1024; h = 768; }
        /* Write SizeOfInfo */
        if (r8 > 0 && r8 + 8 <= guest_mem_size) {
            unsigned long long sz = 36;
            memcpy((unsigned char *)guest_mem + r8, &sz, 8);
        }
        /* Write Info pointer to our static info block, and update it */
        if (arg_vals[3].Reg64 > 0 && arg_vals[3].Reg64 + 8 <= guest_mem_size) {
            unsigned long long info_addr = UEFI_TABLE_PAGE + 0x6C0;
            memcpy((unsigned char *)guest_mem + arg_vals[3].Reg64, &info_addr, 8);
        }
        /* Update info block */
        unsigned char *info = (unsigned char *)guest_mem + UEFI_TABLE_PAGE + 0x6C0;
        *(int *)(info + 4) = w;
        *(int *)(info + 8) = h;
        *(int *)(info + 20) = w; /* PixelsPerScanLine */
        break;
    }
    case UEFI_TRAP_GOP_SETMODE: {
        /* SetMode(This, ModeNumber) — RDX = mode number */
        int mode_num = (int)rdx;
        gop_width = 640; gop_height = 480;
        if (mode_num == 1) { gop_width = 800; gop_height = 600; }
        else if (mode_num == 2) { gop_width = 1024; gop_height = 768; }
        gop_stride = gop_width;
        gop_active = 1;
        /* Allocate host-side framebuffer if needed */
        if (!gop_fb) gop_fb = (unsigned char *)calloc(1, GOP_FB_SIZE);
        /* Update GOP Mode structure in guest memory */
        unsigned char *gm = (unsigned char *)guest_mem + UEFI_TABLE_PAGE;
        *(int *)(gm + 0x684) = mode_num;
        *(int *)(gm + 0x6C4) = gop_width;
        *(int *)(gm + 0x6C8) = gop_height;
        *(int *)(gm + 0x6D4) = gop_stride;
        /* Map the framebuffer region in guest memory if address is within range */
        if (GOP_FB_ADDR + (unsigned long long)(gop_width * gop_height * 4) <= guest_mem_size) {
            memset((unsigned char *)guest_mem + GOP_FB_ADDR, 0, gop_width * gop_height * 4);
        }
        /* Resize the display window */
        if (vga_hwnd) {
            RECT r = {0, 0, gop_width, gop_height};
            AdjustWindowRect(&r, WS_OVERLAPPEDWINDOW, FALSE);
            SetWindowPos(vga_hwnd, NULL, 0, 0, r.right - r.left, r.bottom - r.top, SWP_NOMOVE | SWP_NOZORDER);
            char title[64];
            sprintf(title, "Codex VM — %dx%d", gop_width, gop_height);
            SetWindowTextA(vga_hwnd, title);
        }
        fprintf(stderr, "GOP: SetMode %d → %dx%d fb=0x%llx\n", mode_num, gop_width, gop_height, GOP_FB_ADDR);
        break;
    }
    case UEFI_TRAP_GOP_BLT:
        /* Blt — not yet implemented, guest writes directly to framebuffer */
        break;

    default:
        if (func_id >= 500) { fprintf(stderr, "UEFI app exited cleanly.\n"); return 2; /* signal clean exit */ }
        fprintf(stderr, "UEFI: unhandled trap %d (RIP=0x%llx)\n", func_id, rip);
        rax_result = 3; /* EFI_UNSUPPORTED */
        break;
    }

    /* Set RAX = result, pop return address from stack into RIP */
    unsigned long long ret_addr = 0;
    if (rsp > 0 && rsp + 8 <= guest_mem_size) {
        memcpy(&ret_addr, (unsigned char *)guest_mem + rsp, 8);
    }
    if (ret_addr == 0 || ret_addr > guest_mem_size) {
        fprintf(stderr, "UEFI trap %d: bad return addr 0x%llx (RSP=0x%llx)\n", func_id, ret_addr, rsp);
    }
    WHV_REGISTER_NAME ret_names[3] = { WHvX64RegisterRax, WHvX64RegisterRip, WHvX64RegisterRsp };
    WHV_REGISTER_VALUE ret_vals[3];
    ret_vals[0].Reg64 = rax_result;
    ret_vals[1].Reg64 = ret_addr;
    ret_vals[2].Reg64 = rsp + 8;
    WHvSetVirtualProcessorRegisters(partition, 0, ret_names, 3, ret_vals);

    return 1;
}

/* ══ VGA Display Window ══ */
#define CHAR_W       8
#define CHAR_H       16
#define VGA_WIN_W    (VGA_COLS * CHAR_W)
#define VGA_WIN_H    (VGA_ROWS * CHAR_H)
#define VGA_TIMER_ID 1

/* vga_hwnd is forward-declared near GOP state above */
static HFONT vga_font;
static int vga_headless = 0;
static volatile int vga_running = 1;

static const COLORREF vga_palette[16] = {
    RGB(0,0,0),       RGB(0,0,170),     RGB(0,170,0),     RGB(0,170,170),
    RGB(170,0,0),     RGB(170,0,170),   RGB(170,85,0),    RGB(170,170,170),
    RGB(85,85,85),    RGB(85,85,255),   RGB(85,255,85),   RGB(85,255,255),
    RGB(255,85,85),   RGB(255,85,255),  RGB(255,255,85),  RGB(255,255,255)
};

static void vga_paint(HWND hwnd) {
    if (!guest_mem) return;
    PAINTSTRUCT ps;
    HDC hdc = BeginPaint(hwnd, &ps);

    if (gop_active && GOP_FB_ADDR + (unsigned long long)(gop_width * gop_height * 4) <= guest_mem_size) {
        /* GOP framebuffer mode — render pixels from guest memory */
        BITMAPINFO bmi;
        memset(&bmi, 0, sizeof(bmi));
        bmi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
        bmi.bmiHeader.biWidth = gop_stride;
        bmi.bmiHeader.biHeight = -gop_height;  /* top-down */
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = BI_RGB;
        unsigned char *fb = (unsigned char *)guest_mem + GOP_FB_ADDR;
        StretchDIBits(hdc, 0, 0, gop_width, gop_height,
                      0, 0, gop_stride, gop_height,
                      fb, &bmi, DIB_RGB_COLORS, SRCCOPY);
    } else {
        /* Text mode — render VGA character buffer */
        HFONT old = (HFONT)SelectObject(hdc, vga_font);
        SetBkMode(hdc, OPAQUE);
        unsigned char *vga = (unsigned char *)guest_mem + VGA_BASE;
        char ch[2] = {0, 0};
        for (int row = 0; row < VGA_ROWS; row++) {
            for (int col = 0; col < VGA_COLS; col++) {
                int off = (row * VGA_COLS + col) * 2;
                unsigned char c = vga[off];
                unsigned char attr = vga[off + 1];
                int fg = attr & 0x0F;
                int bg = (attr >> 4) & 0x0F;
                SetTextColor(hdc, vga_palette[fg]);
                SetBkColor(hdc, vga_palette[bg]);
                ch[0] = c ? c : ' ';
                TextOutA(hdc, col * CHAR_W, row * CHAR_H, ch, 1);
            }
        }
        SelectObject(hdc, old);
    }

    EndPaint(hwnd, &ps);
}

static LRESULT CALLBACK vga_wndproc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    switch (msg) {
    case WM_PAINT:
        vga_paint(hwnd);
        return 0;
    case WM_TIMER:
        if (wp == VGA_TIMER_ID) InvalidateRect(hwnd, NULL, FALSE);
        return 0;
    case WM_KEYDOWN: {
        unsigned char sc = vk_to_scancode((int)wp);
        if (sc) {
            kbd_enqueue(sc); kbd_irq_pending = 1;
            /* Also write scancode to guest memory at key-buffer-addr (28680)
               so bare-metal Codex apps can read it via peek-byte */
            if (guest_mem && 28680 < guest_mem_size) {
                ((unsigned char *)guest_mem)[28680] = sc;
            }
        }
        return 0;
    }
    case WM_KEYUP: {
        unsigned char sc = vk_to_scancode((int)wp);
        if (sc) { kbd_enqueue(sc | 0x80); kbd_irq_pending = 1; }
        return 0;
    }
    case WM_LBUTTONDOWN: case WM_RBUTTONDOWN: case WM_MBUTTONDOWN:
    case WM_LBUTTONUP: case WM_RBUTTONUP: case WM_MBUTTONUP:
    case WM_MOUSEMOVE:
        if (guest_mem && (msg == WM_LBUTTONDOWN || mouse_captured)) {
            if (!mouse_captured && msg == WM_LBUTTONDOWN) {
                SetCapture(hwnd); mouse_captured = 1;
            }
            int mx = (short)LOWORD(lp);
            int my = (short)HIWORD(lp);
            static int last_mx = -1, last_my = -1;
            int dx = (last_mx >= 0) ? mx - last_mx : 0;
            int dy = (last_my >= 0) ? my - last_my : 0;
            last_mx = mx; last_my = my;
            unsigned char flags = 0x08;
            if (wp & MK_LBUTTON) flags |= 1;
            if (wp & MK_RBUTTON) flags |= 2;
            if (wp & MK_MBUTTON) flags |= 4;
            if (dx < 0) flags |= 0x10;
            if (dy < 0) flags |= 0x20;
            unsigned char *mbuf = (unsigned char *)guest_mem + MOUSE_BUF_ADDR;
            mbuf[0] = flags;
            mbuf[1] = (unsigned char)(dx & 0xFF);
            mbuf[2] = (unsigned char)(dy & 0xFF);
        }
        if (msg == WM_RBUTTONDOWN && mouse_captured) {
            ReleaseCapture(); mouse_captured = 0;
        }
        return 0;
    case WM_CLOSE:
        vga_running = 0;
        DestroyWindow(hwnd);
        return 0;
    case WM_DESTROY:
        vga_running = 0;
        return 0;
    default:
        return DefWindowProcA(hwnd, msg, wp, lp);
    }
}

static DWORD WINAPI vga_thread(LPVOID param) {
    (void)param;
    WNDCLASSA wc = {0};
    wc.lpfnWndProc = vga_wndproc;
    wc.hInstance = GetModuleHandleA(NULL);
    wc.lpszClassName = "CodexVmVGA";
    wc.hCursor = LoadCursorA(NULL, IDC_ARROW);
    wc.hbrBackground = (HBRUSH)GetStockObject(BLACK_BRUSH);
    RegisterClassA(&wc);
    int win_w = gop_active ? gop_width : VGA_WIN_W;
    int win_h = gop_active ? gop_height : VGA_WIN_H;
    const char *title = gop_active ? "Codex Spark" : "Codex VM";
    RECT r = {0, 0, win_w, win_h};
    AdjustWindowRect(&r, WS_OVERLAPPEDWINDOW, FALSE);
    vga_hwnd = CreateWindowA("CodexVmVGA", title,
        WS_OVERLAPPEDWINDOW | WS_VISIBLE,
        CW_USEDEFAULT, CW_USEDEFAULT,
        r.right - r.left, r.bottom - r.top,
        NULL, NULL, wc.hInstance, NULL);
    vga_font = CreateFontA(CHAR_H, CHAR_W, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
        OEM_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
        NONANTIALIASED_QUALITY, FIXED_PITCH | FF_MODERN, "Consolas");
    SetTimer(vga_hwnd, VGA_TIMER_ID, 50, NULL);  /* refresh 20Hz */
    MSG msg;
    while (vga_running && GetMessageA(&msg, NULL, 0, 0)) {
        TranslateMessage(&msg);
        DispatchMessageA(&msg);
    }
    if (vga_font) DeleteObject(vga_font);
    return 0;
}

static void vga_start(void) {
    if (vga_headless) return;
    CreateThread(NULL, 0, vga_thread, NULL, 0, NULL);
}

#define GUEST_MEM_BASE  0
#define LOAD_ADDR       0x100000
#define STACK_TOP       0x7FFE00
#define PAGE_TABLE_ADDR 0xC00000
#define MAX_MEM         (2ULL*1024*1024*1024)

/* Guest serial ring buffer limits (write_pos at 0x7008, read_pos at 0x700C, 1MB ring) */
#define SERIAL_RING_WPOS_ADDR 28704
#define SERIAL_RING_RPOS_ADDR 28712
#define SERIAL_RING_CAPACITY  1048576
#define SERIAL_RING_HEADROOM  148576  /* stop draining when < this much room left */

/* Serial state */
typedef struct {
    SOCKET sock;
    SOCKET client;
    int port;
    int connected;
    int dlab;
    unsigned char last_rx;  /* last received byte — returned on reads when FIFO empty */
} SerialPort;

/* IDE state */
typedef struct {
    unsigned char *data;
    size_t size;
    int sect_count, lba_lo, lba_mid, lba_hi, drive_head;
    int status, error;
    size_t buf_off;
    int buf_remaining;
    int sectors_left;
} IdeState;

/* PIC state */
typedef struct {
    int icw_step;       /* 0=idle, 1=waiting ICW2, 2=ICW3, 3=ICW4 */
    int vector_base;
    int mask;
    int isr;            /* in-service register */
    int irr;            /* interrupt request register */
} PicState;

/* Forward declarations for NAT (defined after NE2K) */
static void nat_handle_tx(unsigned char *frame, int len);
static void nat_poll_rx(void);
static void ne2k_inject_rx(void);

/* NE2000 NIC state */
#define NE2K_BASE 0x300
#define NE2K_MEM_SIZE 32768
typedef struct {
    unsigned char mem[NE2K_MEM_SIZE];
    unsigned char cr;
    unsigned char isr, imr;
    unsigned char dcr, tcr, rcr;
    unsigned char tpsr;
    int tbcr;
    unsigned char pstart, pstop, bnry, curr;
    int rsar, rbcr;
    unsigned char par[6];
    unsigned char mar[8];
    int page;          /* 0, 1, or 2 — from CR bits 7:6 */
    int started;       /* STA bit */
    int word_mode;     /* DCR bit 0: 1=word transfers */
} Ne2kState;

static Ne2kState ne2k;

static void ne2k_reset(void) {
    memset(&ne2k, 0, sizeof(ne2k));
    ne2k.isr = 0x80; /* RST bit set after reset */
    ne2k.par[0] = 0x52; ne2k.par[1] = 0x54; ne2k.par[2] = 0x00;
    ne2k.par[3] = 0x12; ne2k.par[4] = 0x34; ne2k.par[5] = 0x56;
    /* PROM: MAC bytes doubled (NE2000 convention) at mem[0..31] */
    for (int i = 0; i < 6; i++) {
        ne2k.mem[i * 2] = ne2k.par[i];
        ne2k.mem[i * 2 + 1] = ne2k.par[i];
    }
    for (int i = 12; i < 32; i++) ne2k.mem[i] = 0xFF;
}

static void ne2k_handle_out(int port, int val, int io_size) {
    int off = port - NE2K_BASE;
    if (off == 0x1F) { ne2k_reset(); return; }  /* reset port */
    if (off == 0x10) {
        /* DATA port write — remote DMA write into NIC memory */
        if (ne2k.rbcr > 0 && ne2k.rsar < NE2K_MEM_SIZE) {
            ne2k.mem[ne2k.rsar] = val & 0xFF;
            ne2k.rsar++; ne2k.rbcr--;
            if (io_size >= 2 && ne2k.rbcr > 0 && ne2k.rsar < NE2K_MEM_SIZE) {
                ne2k.mem[ne2k.rsar] = (val >> 8) & 0xFF;
                ne2k.rsar++; ne2k.rbcr--;
            }
        }
        if (ne2k.rbcr <= 0) ne2k.isr |= 0x40; /* RDC */
        return;
    }
    if (off == 0x00) {
        /* CR — command register */
        ne2k.page = (val >> 6) & 3;
        ne2k.started = (val & 2) ? 1 : 0;
        if (val & 4) {
            /* TXP — transmit packet */
            int tx_addr = ne2k.tpsr * 256;
            if (tx_addr + ne2k.tbcr <= NE2K_MEM_SIZE && ne2k.tbcr > 0)
                nat_handle_tx(ne2k.mem + tx_addr, ne2k.tbcr);
            ne2k.isr |= 0x02; /* TX complete */
            ne2k_inject_rx(); /* inject any reply frames immediately */
        }
        return;
    }
    /* Page-dependent registers */
    if (ne2k.page == 0) {
        switch (off) {
        case 0x01: ne2k.pstart = val; break;
        case 0x02: ne2k.pstop = val; break;
        case 0x03: ne2k.bnry = val; break;
        case 0x04: ne2k.tpsr = val; break;
        case 0x05: ne2k.tbcr = (ne2k.tbcr & 0xFF00) | (val & 0xFF); break;
        case 0x06: ne2k.tbcr = (ne2k.tbcr & 0x00FF) | ((val & 0xFF) << 8); break;
        case 0x07: ne2k.isr &= ~val; break;  /* write-1-to-clear */
        case 0x08: ne2k.rsar = (ne2k.rsar & 0xFF00) | (val & 0xFF); break;
        case 0x09: ne2k.rsar = (ne2k.rsar & 0x00FF) | ((val & 0xFF) << 8); break;
        case 0x0A: ne2k.rbcr = (ne2k.rbcr & 0xFF00) | (val & 0xFF); break;
        case 0x0B: ne2k.rbcr = (ne2k.rbcr & 0x00FF) | ((val & 0xFF) << 8); break;
        case 0x0C: ne2k.rcr = val; break;
        case 0x0D: ne2k.tcr = val; break;
        case 0x0E: ne2k.dcr = val; ne2k.word_mode = val & 1; break;
        case 0x0F: ne2k.imr = val; break;
        }
    } else if (ne2k.page == 1) {
        if (off >= 0x01 && off <= 0x06) ne2k.par[off - 1] = val;
        else if (off == 0x07) ne2k.curr = val;
        else if (off >= 0x08 && off <= 0x0F) ne2k.mar[off - 8] = val;
    }
}

static int ne2k_handle_in(int port, int io_size) {
    int off = port - NE2K_BASE;
    if (off == 0x1F) { ne2k_reset(); return ne2k.isr; }
    if (off == 0x10) {
        /* DATA port read — remote DMA read from NIC memory */
        int val = 0;
        if (ne2k.rbcr > 0 && ne2k.rsar < NE2K_MEM_SIZE) {
            val = ne2k.mem[ne2k.rsar];
            ne2k.rsar++; ne2k.rbcr--;
            if (io_size >= 2 && ne2k.rbcr > 0 && ne2k.rsar < NE2K_MEM_SIZE) {
                val |= ne2k.mem[ne2k.rsar] << 8;
                ne2k.rsar++; ne2k.rbcr--;
            }
        }
        if (ne2k.rbcr <= 0) ne2k.isr |= 0x40; /* RDC */
        return val;
    }
    if (off == 0x00) return ne2k.cr | (ne2k.page << 6) | (ne2k.started ? 2 : 0);
    if (ne2k.page == 0) {
        switch (off) {
        case 0x01: return 0;  /* CLDA0 */
        case 0x02: return 0;  /* CLDA1 */
        case 0x03: return ne2k.bnry;
        case 0x04: return 0x01; /* TSR: TX ok */
        case 0x07: return ne2k.isr;
        case 0x0C: return 0;  /* RSR */
        default: return 0;
        }
    } else if (ne2k.page == 1) {
        if (off >= 0x01 && off <= 0x06) return ne2k.par[off - 1];
        if (off == 0x07) return ne2k.curr;
        if (off >= 0x08 && off <= 0x0F) return ne2k.mar[off - 8];
    }
    return 0;
}

/* NAT connection table */
#define NAT_MAX_CONN 64
#define NAT_GW_IP0 10
#define NAT_GW_IP1 0
#define NAT_GW_IP2 2
#define NAT_GW_IP3 2
#define NAT_GUEST_IP0 10
#define NAT_GUEST_IP1 0
#define NAT_GUEST_IP2 2
#define NAT_GUEST_IP3 15

static const unsigned char nat_gw_mac[6] = {0x52, 0x55, 0x0A, 0x00, 0x02, 0x02};

typedef struct {
    int active;
    SOCKET sock;
    unsigned char dst_ip[4];
    unsigned short guest_port;
    unsigned short dst_port;
    unsigned long seq_offset;  /* guest seq → host stream offset */
    unsigned long ack_offset;
    int state;  /* 0=unused, 1=connecting, 2=established, 3=fin_wait */
} NatConn;

static NatConn nat_conns[NAT_MAX_CONN];

static NatConn *nat_find(unsigned short guest_port, unsigned short dst_port, unsigned char *dst_ip) {
    for (int i = 0; i < NAT_MAX_CONN; i++) {
        NatConn *c = &nat_conns[i];
        if (c->active && c->guest_port == guest_port && c->dst_port == dst_port &&
            memcmp(c->dst_ip, dst_ip, 4) == 0) return c;
    }
    return NULL;
}

static NatConn *nat_alloc(void) {
    for (int i = 0; i < NAT_MAX_CONN; i++)
        if (!nat_conns[i].active) { memset(&nat_conns[i], 0, sizeof(NatConn)); return &nat_conns[i]; }
    return NULL;
}

/* Pending RX frames for the guest */
#define RX_QUEUE_SIZE 32
#define RX_FRAME_MAX 1536
typedef struct {
    unsigned char data[RX_FRAME_MAX];
    int len;
} RxFrame;

static RxFrame rx_queue[RX_QUEUE_SIZE];
static int rx_queue_head = 0, rx_queue_count = 0;

static void rx_enqueue(unsigned char *data, int len) {
    if (rx_queue_count >= RX_QUEUE_SIZE || len > RX_FRAME_MAX) return;
    int idx = (rx_queue_head + rx_queue_count) % RX_QUEUE_SIZE;
    memcpy(rx_queue[idx].data, data, len);
    rx_queue[idx].len = len;
    rx_queue_count++;
}

/* IP checksum helper */
static unsigned short ip_checksum(unsigned char *data, int len) {
    unsigned long sum = 0;
    for (int i = 0; i < len - 1; i += 2) sum += (data[i] << 8) | data[i + 1];
    if (len & 1) sum += data[len - 1] << 8;
    while (sum >> 16) sum = (sum & 0xFFFF) + (sum >> 16);
    return (unsigned short)(~sum & 0xFFFF);
}

/* Build and enqueue an ARP reply */
static void nat_arp_reply(unsigned char *request, int len) {
    if (len < 42) return;
    unsigned char reply[42];
    /* Ethernet header */
    memcpy(reply, request + 6, 6);       /* dst = sender's MAC */
    memcpy(reply + 6, nat_gw_mac, 6);    /* src = gateway MAC */
    reply[12] = 0x08; reply[13] = 0x06;  /* ARP */
    /* ARP payload */
    reply[14] = 0; reply[15] = 1;        /* HTYPE: Ethernet */
    reply[16] = 0x08; reply[17] = 0;     /* PTYPE: IPv4 */
    reply[18] = 6; reply[19] = 4;        /* HLEN, PLEN */
    reply[20] = 0; reply[21] = 2;        /* OPER: reply */
    memcpy(reply + 22, nat_gw_mac, 6);   /* sender MAC = gateway */
    memcpy(reply + 28, request + 38, 4); /* sender IP = target IP from request */
    memcpy(reply + 32, request + 6, 6);  /* target MAC = requester's MAC */
    memcpy(reply + 38, request + 28, 4); /* target IP = requester's IP */
    rx_enqueue(reply, 42);
}

/* Build an IP/TCP response frame and enqueue it */
static void nat_build_tcp_frame(unsigned char *dst_mac, unsigned char *src_ip, unsigned char *dst_ip,
                                 unsigned short src_port, unsigned short dst_port,
                                 unsigned long seq, unsigned long ack,
                                 int flags, unsigned char *payload, int payload_len) {
    unsigned char frame[1536];
    int ip_len = 20 + 20 + payload_len;  /* IP header + TCP header + payload */
    int total = 14 + ip_len;             /* Ethernet + IP */
    if (total > 1536) return;

    /* Ethernet */
    memcpy(frame, dst_mac, 6);
    memcpy(frame + 6, nat_gw_mac, 6);
    frame[12] = 0x08; frame[13] = 0x00; /* IPv4 */

    /* IP header */
    unsigned char *ip = frame + 14;
    ip[0] = 0x45; ip[1] = 0; ip[2] = ip_len >> 8; ip[3] = ip_len & 0xFF;
    ip[4] = 0; ip[5] = 0; ip[6] = 0x40; ip[7] = 0; /* Don't Fragment */
    ip[8] = 64; ip[9] = 6; /* TTL=64, proto=TCP */
    ip[10] = 0; ip[11] = 0; /* checksum placeholder */
    memcpy(ip + 12, src_ip, 4);
    memcpy(ip + 16, dst_ip, 4);
    unsigned short ipcsum = ip_checksum(ip, 20);
    ip[10] = ipcsum >> 8; ip[11] = ipcsum & 0xFF;

    /* TCP header */
    unsigned char *tcp = ip + 20;
    tcp[0] = src_port >> 8; tcp[1] = src_port & 0xFF;
    tcp[2] = dst_port >> 8; tcp[3] = dst_port & 0xFF;
    tcp[4] = seq >> 24; tcp[5] = (seq >> 16) & 0xFF; tcp[6] = (seq >> 8) & 0xFF; tcp[7] = seq & 0xFF;
    tcp[8] = ack >> 24; tcp[9] = (ack >> 16) & 0xFF; tcp[10] = (ack >> 8) & 0xFF; tcp[11] = ack & 0xFF;
    tcp[12] = 0x50; /* data offset = 5 words */
    tcp[13] = flags & 0xFF;
    tcp[14] = 0xFF; tcp[15] = 0xFF; /* window = 65535 */
    tcp[16] = 0; tcp[17] = 0; /* checksum placeholder */
    tcp[18] = 0; tcp[19] = 0; /* urgent pointer */
    if (payload_len > 0) memcpy(tcp + 20, payload, payload_len);

    /* TCP checksum (pseudo-header + TCP) */
    unsigned long tcpsum = 0;
    /* pseudo-header */
    for (int i = 0; i < 4; i += 2) tcpsum += (src_ip[i] << 8) | src_ip[i + 1];
    for (int i = 0; i < 4; i += 2) tcpsum += (dst_ip[i] << 8) | dst_ip[i + 1];
    tcpsum += 6; /* protocol TCP */
    int tcp_len = 20 + payload_len;
    tcpsum += tcp_len;
    for (int i = 0; i < tcp_len - 1; i += 2) tcpsum += (tcp[i] << 8) | tcp[i + 1];
    if (tcp_len & 1) tcpsum += tcp[tcp_len - 1] << 8;
    while (tcpsum >> 16) tcpsum = (tcpsum & 0xFFFF) + (tcpsum >> 16);
    unsigned short tcpcsum = (unsigned short)(~tcpsum & 0xFFFF);
    tcp[16] = tcpcsum >> 8; tcp[17] = tcpcsum & 0xFF;

    rx_enqueue(frame, total);
}

/* Handle a TX frame from the guest */
static void nat_handle_tx(unsigned char *frame, int len) {
    if (len < 14) return;
    unsigned short ethertype = (frame[12] << 8) | frame[13];

    if (ethertype == 0x0806) {
        /* ARP */
        nat_arp_reply(frame, len);
        return;
    }
    if (ethertype != 0x0800 || len < 34) return; /* IPv4 only */

    unsigned char *ip = frame + 14;
    int ip_hdr_len = (ip[0] & 0x0F) * 4;
    int ip_proto = ip[9];
    unsigned char *src_ip = ip + 12;
    unsigned char *dst_ip = ip + 16;

    if (ip_proto == 6 && len >= 14 + ip_hdr_len + 20) {
        /* TCP */
        unsigned char *tcp = ip + ip_hdr_len;
        unsigned short sport = (tcp[0] << 8) | tcp[1];
        unsigned short dport = (tcp[2] << 8) | tcp[3];
        unsigned long seq = ((unsigned long)tcp[4] << 24) | (tcp[5] << 16) | (tcp[6] << 8) | tcp[7];
        unsigned long ack = ((unsigned long)tcp[8] << 24) | (tcp[9] << 16) | (tcp[10] << 8) | tcp[11];
        int tcp_hdr_len = ((tcp[12] >> 4) & 0xF) * 4;
        int flags = tcp[13];
        int payload_len = len - 14 - ip_hdr_len - tcp_hdr_len;
        unsigned char *payload = tcp + tcp_hdr_len;

        unsigned char guest_ip[4] = {NAT_GUEST_IP0, NAT_GUEST_IP1, NAT_GUEST_IP2, NAT_GUEST_IP3};

        if (flags & 0x02) {
            /* SYN — new connection */
            NatConn *c = nat_find(sport, dport, dst_ip);
            if (!c) c = nat_alloc();
            if (!c) return;
            c->active = 1;
            c->guest_port = sport;
            c->dst_port = dport;
            memcpy(c->dst_ip, dst_ip, 4);
            c->seq_offset = seq;
            c->ack_offset = 1000000;
            c->state = 1;

            /* Connect host socket */
            c->sock = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
            if (c->sock == INVALID_SOCKET) { c->active = 0; return; }
            u_long nb = 1;
            ioctlsocket(c->sock, FIONBIO, &nb);
            struct sockaddr_in addr;
            memset(&addr, 0, sizeof(addr));
            addr.sin_family = AF_INET;
            addr.sin_port = htons(dport);
            memcpy(&addr.sin_addr, dst_ip, 4);
            connect(c->sock, (struct sockaddr*)&addr, sizeof(addr));
            /* Non-blocking connect — will complete later */

            /* Send SYN-ACK back to guest */
            nat_build_tcp_frame(ne2k.par, dst_ip, guest_ip,
                                dport, sport,
                                c->ack_offset, seq + 1,
                                0x12, /* SYN+ACK */
                                NULL, 0);
            c->state = 2;
        }
        else if ((flags & 0x01) && !(flags & 0x02)) {
            /* FIN */
            NatConn *c = nat_find(sport, dport, dst_ip);
            if (c) {
                nat_build_tcp_frame(ne2k.par, dst_ip, guest_ip,
                                    dport, sport,
                                    ack, seq + 1,
                                    0x11, /* FIN+ACK */
                                    NULL, 0);
                closesocket(c->sock);
                c->active = 0;
            }
        }
        else if (flags & 0x10) {
            /* ACK (possibly with data) */
            NatConn *c = nat_find(sport, dport, dst_ip);
            if (c && payload_len > 0 && c->state == 2) {
                /* Forward data to host */
                send(c->sock, (char*)payload, payload_len, 0);
                /* ACK the data */
                nat_build_tcp_frame(ne2k.par, dst_ip, guest_ip,
                                    dport, sport,
                                    ack, seq + payload_len,
                                    0x10, /* ACK */
                                    NULL, 0);
            }
        }

        if (flags & 0x04) {
            /* RST */
            NatConn *c = nat_find(sport, dport, dst_ip);
            if (c) { closesocket(c->sock); c->active = 0; }
        }
    }
    else if (ip_proto == 17 && len >= 14 + ip_hdr_len + 8) {
        /* UDP — minimal DNS forwarding could go here */
    }
}

/* Poll host sockets for incoming data and build RX frames */
static void nat_poll_rx(void) {
    unsigned char guest_ip[4] = {NAT_GUEST_IP0, NAT_GUEST_IP1, NAT_GUEST_IP2, NAT_GUEST_IP3};
    for (int i = 0; i < NAT_MAX_CONN; i++) {
        NatConn *c = &nat_conns[i];
        if (!c->active || c->state != 2) continue;
        unsigned char buf[1400];
        int n = recv(c->sock, (char*)buf, sizeof(buf), 0);
        if (n > 0) {
            c->ack_offset++;
            nat_build_tcp_frame(ne2k.par, c->dst_ip, guest_ip,
                                c->dst_port, c->guest_port,
                                c->ack_offset, c->seq_offset + 1,
                                0x10, /* ACK with data */
                                buf, n);
            c->ack_offset += n - 1;
        } else if (n == 0) {
            /* Connection closed by remote */
            nat_build_tcp_frame(ne2k.par, c->dst_ip, guest_ip,
                                c->dst_port, c->guest_port,
                                c->ack_offset + 1, c->seq_offset + 1,
                                0x11, /* FIN+ACK */
                                NULL, 0);
            closesocket(c->sock);
            c->state = 3;
        }
    }
}

/* Inject queued RX frames into the NE2000 ring buffer */
static void ne2k_inject_rx(void) {
    while (rx_queue_count > 0) {
        RxFrame *f = &rx_queue[rx_queue_head];
        /* Write NE2000 packet header + frame into ring buffer at curr page */
        int total = f->len + 4; /* 4-byte NE2000 header */
        int pages_needed = (total + 255) / 256;
        int next_page = ne2k.curr + pages_needed;
        if (next_page >= ne2k.pstop) next_page = ne2k.pstart + (next_page - ne2k.pstop);

        /* Check for ring buffer full (next would equal bnry) */
        if (next_page == ne2k.bnry) break;

        int addr = ne2k.curr * 256;
        if (addr + total > NE2K_MEM_SIZE) break;

        /* NE2000 RX header: status, next_page, length_lo, length_hi */
        ne2k.mem[addr] = 0x01;  /* RXOK */
        ne2k.mem[addr + 1] = next_page & 0xFF;
        ne2k.mem[addr + 2] = f->len & 0xFF;
        ne2k.mem[addr + 3] = (f->len >> 8) & 0xFF;
        memcpy(ne2k.mem + addr + 4, f->data, f->len);

        ne2k.curr = next_page;
        ne2k.isr |= 0x01; /* PRX — packet received */

        rx_queue_head = (rx_queue_head + 1) % RX_QUEUE_SIZE;
        rx_queue_count--;
    }
}


/* VGA Attribute Controller — minimal emulation for port 0x3C0/0x3C1 */
static int vga_attr_index = 0;    /* current attribute register index */
static int vga_attr_flipflop = 0; /* 0=next write is index, 1=next write is data */

static SerialPort com1, com2;
static IdeState ide;
static PicState pic_master, pic_slave;
static int debug_exit_code = -1;
static volatile int serial_irq_pending = 0;
static int no_timer = 0;  /* set via CODEX_VM_NO_TIMER=1 to suppress timer IRQ */

/* PIT state */
static int pit_vector = 32;
static LARGE_INTEGER perf_freq;
static LARGE_INTEGER last_tick;

/* Watchpoint */
static unsigned long long watch_addr = 0;
static int watch_size = 8;
static unsigned char watch_prev[64];
static int watch_active = 0;

static void die(const char *msg) { fprintf(stderr, "FATAL: %s\n", msg); exit(1); }

/* ── Serial ────────────────────────────────────────────────────────── */

static void serial_init(SerialPort *sp, int port) {
    sp->port = port;
    sp->client = INVALID_SOCKET;
    sp->connected = 0;
    sp->sock = socket(AF_INET, SOCK_STREAM, 0);
    if (sp->sock == INVALID_SOCKET) die("socket");
    int opt = 1;
    setsockopt(sp->sock, SOL_SOCKET, SO_REUSEADDR, (char*)&opt, sizeof(opt));
    struct sockaddr_in addr = {0};
    addr.sin_family = AF_INET;
    addr.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    addr.sin_port = htons((u_short)port);
    if (bind(sp->sock, (struct sockaddr*)&addr, sizeof(addr)) < 0) die("bind");
    listen(sp->sock, 1);
}

static void serial_accept(SerialPort *sp) {
    u_long nonblock = 0;
    ioctlsocket(sp->sock, FIONBIO, &nonblock);
    sp->client = accept(sp->sock, NULL, NULL);
    if (sp->client != INVALID_SOCKET) {
        sp->connected = 1;
        int opt = 1;
        setsockopt(sp->client, IPPROTO_TCP, TCP_NODELAY, (char*)&opt, sizeof(opt));
    }
}

static void serial_send(SerialPort *sp, unsigned char byte) {
    if (sp->connected) send(sp->client, (char*)&byte, 1, 0);
}

static int serial_has_data(SerialPort *sp) {
    if (!sp->connected) return 0;
    u_long avail = 0;
    ioctlsocket(sp->client, FIONREAD, &avail);
    return avail > 0;
}

static int guest_ring_has_room(void) {
    unsigned long long wpos = *(unsigned long long*)((char*)guest_mem + SERIAL_RING_WPOS_ADDR);
    unsigned long long rpos = *(unsigned long long*)((char*)guest_mem + SERIAL_RING_RPOS_ADDR);
    return (wpos - rpos) < (SERIAL_RING_CAPACITY - SERIAL_RING_HEADROOM);
}

static int serial_recv(SerialPort *sp) {
    if (!sp->connected) return -1;
    unsigned char b;
    u_long avail = 0;
    ioctlsocket(sp->client, FIONREAD, &avail);
    if (avail == 0) return -1;
    int n = recv(sp->client, (char*)&b, 1, 0);
    if (n == 1) { sp->last_rx = b; return b; }
    return -1;
}

static void serial_close(SerialPort *sp) {
    if (sp->client != INVALID_SOCKET) closesocket(sp->client);
    if (sp->sock != INVALID_SOCKET) closesocket(sp->sock);
}

/* ── IDE ───────────────────────────────────────────────────────────── */

static void ide_init(IdeState *d, const char *path) {
    memset(d, 0, sizeof(*d));
    d->status = 0x50; /* DRDY */
    if (!path) return;
    FILE *f = fopen(path, "rb");
    if (!f) { fprintf(stderr, "WARN: cannot open disk %s\n", path); return; }
    fseek(f, 0, SEEK_END);
    d->size = ftell(f);
    fseek(f, 0, SEEK_SET);
    d->data = malloc(d->size);
    fread(d->data, 1, d->size, f);
    fclose(f);
    fprintf(stderr, "IDE: %s (%zu bytes, %zu sectors)\n", path, d->size, d->size/512);
}

static unsigned int ide_get_lba(IdeState *d) {
    return d->lba_lo | (d->lba_mid << 8) | (d->lba_hi << 16) | ((d->drive_head & 0xF) << 24);
}

static void ide_start_read(IdeState *d) {
    unsigned int lba = ide_get_lba(d);
    int count = d->sect_count ? d->sect_count : 256;
    if ((size_t)lba * 512 >= d->size) { d->status = 0x51; d->error = 0x10; return; }
    d->buf_off = (size_t)lba * 512;
    d->buf_remaining = 512;
    d->sectors_left = count - 1;
    d->status = 0x58; /* DRDY|DRQ */
    d->error = 0;
}

static void ide_advance(IdeState *d) {
    if (d->sectors_left <= 0) { d->status = 0x50; d->buf_remaining = 0; return; }
    d->buf_off += 512;
    d->buf_remaining = 512;
    d->sectors_left--;
    d->status = 0x58;
}

static int ide_read_data(IdeState *d) {
    if (d->buf_remaining <= 0) return 0;
    int lo = (d->buf_off < d->size) ? d->data[d->buf_off] : 0;
    int hi = (d->buf_off+1 < d->size) ? d->data[d->buf_off+1] : 0;
    d->buf_off += 2;
    d->buf_remaining -= 2;
    if (d->buf_remaining <= 0) ide_advance(d);
    return lo | (hi << 8);
}

static void ide_handle_out(IdeState *d, int port, int val) {
    int reg = port - 0x1F0;
    if (reg == 2) d->sect_count = val & 0xFF;
    else if (reg == 3) d->lba_lo = val & 0xFF;
    else if (reg == 4) d->lba_mid = val & 0xFF;
    else if (reg == 5) d->lba_hi = val & 0xFF;
    else if (reg == 6) d->drive_head = val & 0xFF;
    else if (reg == 7) { if (val == 0x20) ide_start_read(d); else d->status = 0x50; }
}

static int ide_handle_in(IdeState *d, int port) {
    if (port == 0x3F6) return d->status;
    int reg = port - 0x1F0;
    if (reg == 7) return d->status;
    if (reg == 1) return d->error;
    if (reg == 0) return ide_read_data(d);
    return 0xFF;
}

/* ── PIC ──────────────────────────────────────────────────────────── */

static void pic_init(PicState *p) {
    memset(p, 0, sizeof(*p));
    p->mask = 0xFF;
}

static void pic_handle_out(PicState *p, int port_is_data, int val) {
    if (!port_is_data) {
        /* Command port */
        if (val & 0x10) {
            /* ICW1 */
            p->icw_step = 1;
            p->isr = 0;
            p->irr = 0;
        } else if (val == 0x20) {
            /* EOI */
            p->isr = 0;
            serial_irq_pending = 0;
        }
    } else {
        /* Data port */
        switch (p->icw_step) {
        case 1: p->vector_base = val & 0xF8; p->icw_step = 2; break;
        case 2: p->icw_step = 3; break; /* ICW3: cascade config, just consume */
        case 3: p->icw_step = 0; break; /* ICW4: mode, just consume */
        default: p->mask = val; break;  /* OCW1: interrupt mask */
        }
    }
}

static int pic_handle_in(PicState *p, int port_is_data) {
    if (port_is_data) return p->mask;
    return p->isr;
}

/* ── Page tables ───────────────────────────────────────────────────── */

static void setup_gdt(void) {
    /* Write a minimal GDT at 0xA000 + TSS at 0xA100 */
    unsigned char *gdt_base = (unsigned char *)guest_mem + 0xA000;
    memset(gdt_base, 0, 0x200);
    unsigned long long *gdt = (unsigned long long *)gdt_base;
    gdt[0] = 0;                          /* null descriptor */
    gdt[1] = 0x00AF9B000000FFFFULL;      /* 0x08: 64-bit code, DPL0 */
    gdt[2] = 0x00CF93000000FFFFULL;      /* 0x10: data, DPL0 */
    /* 0x18: 64-bit TSS descriptor (16 bytes: base=0xA100, limit=0x67, type=9 available) */
    unsigned long long tss_base = 0xA100;
    gdt[3] = 0x0000890000000067ULL | ((tss_base & 0xFFFF) << 16) | ((tss_base & 0xFF0000) << 16) | ((tss_base & 0xFF000000) << 32);
    gdt[4] = tss_base >> 32;
    /* Zero the TSS at 0xA100 */
    memset((unsigned char *)guest_mem + 0xA100, 0, 0x68);
}

static void setup_page_tables(void) {
    unsigned char *pt = (unsigned char*)guest_mem + PAGE_TABLE_ADDR;
    /* PML4 + PDPT + 2 PDs = 4 pages for 2GB identity map */
    memset(pt, 0, 4 * 4096);
    /* PML4[0] -> PDPT */
    *(unsigned long long*)(pt) = (PAGE_TABLE_ADDR + 4096) | 3;
    /* PDPT[0] -> PD0, PDPT[1] -> PD1 */
    *(unsigned long long*)(pt + 4096) = (PAGE_TABLE_ADDR + 8192) | 3;
    *(unsigned long long*)(pt + 4096 + 8) = (PAGE_TABLE_ADDR + 8192 + 4096) | 3;
    /* PD0: 512 x 2MB huge pages = first 1GB */
    for (int i = 0; i < 512; i++)
        *(unsigned long long*)(pt + 8192 + i*8) = ((unsigned long long)i * 0x200000) | 0x83;
    /* PD1: 512 x 2MB huge pages = second 1GB */
    for (int i = 0; i < 512; i++)
        *(unsigned long long*)(pt + 8192 + 4096 + i*8) = ((unsigned long long)(512 + i) * 0x200000) | 0x83;
}

/* ── WHP setup ─────────────────────────────────────────────────────── */

static void create_vm(size_t mem_mb) {
    HRESULT hr;
    guest_mem_size = mem_mb * 1024 * 1024;
    if (guest_mem_size > MAX_MEM) guest_mem_size = MAX_MEM;

    hr = WHvCreatePartition(&partition);
    if (FAILED(hr)) { fprintf(stderr, "WHvCreatePartition: 0x%lx\n", hr); exit(1); }

    WHV_PARTITION_PROPERTY prop;
    memset(&prop, 0, sizeof(prop));
    prop.ProcessorCount = 1;
    WHvSetPartitionProperty(partition, WHvPartitionPropertyCodeProcessorCount, &prop, sizeof(prop));

    /* Enable I/O port and CPUID exits. MSR exits handled selectively. */
    memset(&prop, 0, sizeof(prop));
    prop.ExtendedVmExits.X64CpuidExit = 1;
    prop.ExtendedVmExits.X64MsrExit = 1;
    WHvSetPartitionProperty(partition, WHvPartitionPropertyCodeExtendedVmExits, &prop, sizeof(prop));

    hr = WHvSetupPartition(partition);
    if (FAILED(hr)) { fprintf(stderr, "WHvSetupPartition: 0x%lx\n", hr); exit(1); }

    /* Enable exception exit for vector 1 (debug trap / single step) */
    memset(&prop, 0, sizeof(prop));
    prop.ExceptionExitBitmap = (1ULL << 1) | (1ULL << 6) | (1ULL << 13) | (1ULL << 14);  /* #DB, #UD, #GP, #PF */
    WHvSetPartitionProperty(partition, WHvPartitionPropertyCodeExceptionExitBitmap, &prop, sizeof(prop.ExceptionExitBitmap));

    guest_mem = VirtualAlloc(NULL, guest_mem_size, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!guest_mem) die("VirtualAlloc");
    memset(guest_mem, 0, guest_mem_size);

    hr = WHvMapGpaRange(partition, guest_mem, 0, guest_mem_size,
        WHvMapGpaRangeFlagRead | WHvMapGpaRangeFlagWrite | WHvMapGpaRangeFlagExecute);
    if (FAILED(hr)) { fprintf(stderr, "WHvMapGpaRange: 0x%lx\n", hr); exit(1); }

    hr = WHvCreateVirtualProcessor(partition, 0, 0);
    if (FAILED(hr)) { fprintf(stderr, "WHvCreateVirtualProcessor: 0x%lx\n", hr); exit(1); }

    if (uefi_mode) {
        uefi_setup_tables(guest_mem);
        /* Fill trap page with HLT (0xF4) opcodes — each UEFI function is at a known offset.
           When the guest CALLs a function, it executes HLT. The VM checks RIP on halt. */
        memset((unsigned char *)guest_mem + UEFI_TRAP_PAGE, 0xF4, 4096);
        fprintf(stderr, "UEFI mode: tables at 0x%x, traps at 0x%x\n", UEFI_TABLE_PAGE, UEFI_TRAP_PAGE);
    }
    /* Auto-activate GOP framebuffer if requested. The guest writes pixels
       directly to GOP_FB_ADDR using poke-32; the VM renders them.
       Window resize happens in vga_thread after creation (checks gop_active). */
    if (gop_active) {
        if (!gop_fb) gop_fb = (unsigned char *)calloc(1, GOP_FB_SIZE);
        memset((unsigned char *)guest_mem + GOP_FB_ADDR, 0, (size_t)gop_width * gop_height * 4);
        fprintf(stderr, "GOP: %dx%d framebuffer at 0x%llx\n", gop_width, gop_height, (unsigned long long)GOP_FB_ADDR);
    }
}

static void load_kernel(const char *path) {
    FILE *f = fopen(path, "rb");
    if (!f) { fprintf(stderr, "cannot open %s\n", path); exit(1); }
    fseek(f, 0, SEEK_END);
    size_t sz = ftell(f);
    fseek(f, 0, SEEK_SET);
    unsigned char *buf = malloc(sz);
    fread(buf, 1, sz, f);
    fclose(f);

    /* Detect CDX header (magic "CDX1") and skip it */
    size_t skip = 0;
    if (sz > 224 && buf[0] == 'C' && buf[1] == 'D' && buf[2] == 'X') {
        skip = 224;
        fprintf(stderr, "CDX header detected, skipping %zu bytes\n", skip);
    }
    size_t payload = sz - skip;
    memcpy((unsigned char*)guest_mem + LOAD_ADDR, buf + skip, payload);

    /* Check for PE/EFI binary (MZ header) */
    if (uefi_mode && sz > 64 && buf[0] == 'M' && buf[1] == 'Z') {
        unsigned int pe_off = *(unsigned int*)(buf + 60);
        if (pe_off + 4 < sz && buf[pe_off] == 'P' && buf[pe_off + 1] == 'E') {
            /* PE64 header */
            unsigned short machine = *(unsigned short*)(buf + pe_off + 4);
            unsigned short num_sections = *(unsigned short*)(buf + pe_off + 6);
            /* PE32+ optional header starts at pe_off + 24 */
            unsigned int entry_rva = *(unsigned int*)(buf + pe_off + 24 + 16);
            unsigned long long image_base = *(unsigned long long*)(buf + pe_off + 24 + 24);
            unsigned int hdr_size = *(unsigned int*)(buf + pe_off + 24 + 60);

            fprintf(stderr, "PE: machine=0x%x sections=%d entry_rva=0x%x image_base=0x%llx\n",
                machine, num_sections, entry_rva, image_base);

            /* Load PE above 2MB so the stub's zero of 0x1000-0x6FFF doesn't destroy it.
               The stub copies .text to 0x100000 then uses that copy going forward.
               Absolute addresses (like the trampoline target 0x10xxxx) refer to the
               copy at 0x100000, not the original load address. */
            unsigned long long load_base = 0x1000000; /* 16MB */

            /* Load PE headers */
            if (load_base + hdr_size < guest_mem_size) {
                memcpy((unsigned char*)guest_mem + load_base, buf, hdr_size < sz ? hdr_size : sz);
            }

            /* Load sections */
            unsigned short opt_hdr_size = *(unsigned short*)(buf + pe_off + 20);
            unsigned char *sec = buf + pe_off + 24 + opt_hdr_size;
            for (int i = 0; i < num_sections && (sec - buf) + 40 <= (ptrdiff_t)sz; i++, sec += 40) {
                unsigned int virt_addr = *(unsigned int*)(sec + 12);
                unsigned int raw_size = *(unsigned int*)(sec + 16);
                unsigned int raw_off = *(unsigned int*)(sec + 20);
                unsigned long long dest = load_base + virt_addr;
                if (dest + raw_size <= guest_mem_size && raw_off + raw_size <= sz) {
                    memcpy((unsigned char*)guest_mem + dest, buf + raw_off, raw_size);
                    fprintf(stderr, "  section %.8s: VA=0x%llx size=0x%x\n", sec, dest, raw_size);
                }
            }

            /* Set entry point for 64-bit long mode */
            unsigned long long entry64 = load_base + entry_rva;
            *(unsigned long long*)((unsigned char*)guest_mem + 0x500) = entry64;
            *(unsigned int*)((unsigned char*)guest_mem + 0x508) = 1; /* flag: PE64 mode */
            fprintf(stderr, "PE entry: 0x%llx\n", entry64);
        }
        free(buf);
        fprintf(stderr, "Loaded PE %s (%zu bytes)\n", path, sz);
        return;
    }

    /* Parse multiboot header for entry point */
    unsigned char *mb = buf + skip;
    if (payload > 32 && *(unsigned int*)mb == 0x1BADB002) {
        unsigned int flags = *(unsigned int*)(mb + 4);
        if (flags & 0x10000) {
            unsigned int entry = *(unsigned int*)(mb + 28);
            fprintf(stderr, "Multiboot entry: 0x%x\n", entry);
            *(unsigned int*)((unsigned char*)guest_mem + 0x500) = entry;
        }
    }
    free(buf);
    fprintf(stderr, "Loaded %s at 0x%x (%zu bytes, payload %zu)\n", path, LOAD_ADDR, sz, payload);
}

static void set_initial_regs(void) {
    setup_page_tables();

    WHV_REGISTER_NAME names[] = {
        WHvX64RegisterRip, WHvX64RegisterRsp, WHvX64RegisterRflags,
        WHvX64RegisterCr0, WHvX64RegisterCr3, WHvX64RegisterCr4, WHvX64RegisterEfer,
        WHvX64RegisterCs, WHvX64RegisterDs, WHvX64RegisterEs,
        WHvX64RegisterSs, WHvX64RegisterFs, WHvX64RegisterGs, WHvX64RegisterTr,
        WHvX64RegisterRax, WHvX64RegisterRbx
    };
    WHV_REGISTER_VALUE vals[16];
    memset(vals, 0, sizeof(vals));

    int pe64 = *(unsigned int*)((unsigned char*)guest_mem + 0x508);

    if (pe64 && uefi_mode) {
        /* PE64/UEFI: start in 64-bit long mode with paging enabled */
        unsigned long long entry64 = *(unsigned long long*)((unsigned char*)guest_mem + 0x500);
        vals[0].Reg64 = entry64;           /* RIP */
        vals[1].Reg64 = STACK_TOP;         /* RSP */
        vals[2].Reg64 = 0x2;              /* RFLAGS */

        /* CR0: PE + PG + ET */
        vals[3].Reg64 = 0x80000011;
        /* CR3: page tables */
        vals[4].Reg64 = PAGE_TABLE_ADDR;
        /* CR4: PAE + OSFXSR + OSXMMEXCPT */
        vals[5].Reg64 = 0x620;
        /* EFER: LME + LMA + SCE */
        vals[6].Reg64 = 0xD01;

        /* CS: 64-bit code segment */
        vals[7].Segment.Base = 0; vals[7].Segment.Limit = 0xFFFFFFFF;
        vals[7].Segment.Selector = 0x08;
        vals[7].Segment.Attributes = 0xA09B;  /* L=1, D=0 for 64-bit */

        /* DS, ES, SS, FS, GS: 64-bit data */
        for (int i = 8; i <= 12; i++) {
            vals[i].Segment.Base = 0; vals[i].Segment.Limit = 0xFFFFFFFF;
            vals[i].Segment.Selector = 0x10;
            vals[i].Segment.Attributes = 0xC093;
        }

        /* TR: 64-bit TSS at 0xA100 */
        vals[13].Segment.Base = 0xA100; vals[13].Segment.Limit = 0x67;
        vals[13].Segment.Selector = 0x18; vals[13].Segment.Attributes = 0x8B;

        /* Push a return address that will HLT cleanly when the UEFI app returns */
        {
            unsigned long long ret_addr = UEFI_TRAP_PAGE + 0xFF0; /* HLT near end of trap page */
            unsigned long long rsp = STACK_TOP - 8;
            memcpy((unsigned char *)guest_mem + rsp, &ret_addr, 8);
            vals[1].Reg64 = rsp; /* adjusted RSP */
        }

        vals[14].Reg64 = 0;                 /* RCX (ImageHandle) → RAX register slot reused */
        vals[15].Reg64 = 0;                 /* RBX */

        /* Set RCX, RDX, GDTR, IDTR separately */
        setup_gdt();
        WHV_REGISTER_NAME uefi_names[5] = { WHvX64RegisterRcx, WHvX64RegisterRdx, WHvX64RegisterGdtr, WHvX64RegisterIdtr, WHvX64RegisterR10 };
        WHV_REGISTER_VALUE uefi_vals[5];
        memset(uefi_vals, 0, sizeof(uefi_vals));
        uefi_vals[0].Reg64 = 0;            /* ImageHandle */
        uefi_vals[1].Reg64 = UEFI_TABLE_PAGE; /* SystemTable */
        uefi_vals[2].Table.Base = 0xA000;   /* GDT base */
        uefi_vals[2].Table.Limit = 39;      /* 5 entries * 8 - 1 (TSS is 16 bytes) */
        uefi_vals[3].Table.Base = 0xB000;   /* IDT base (empty) */
        uefi_vals[3].Table.Limit = 0xFFF;
        uefi_vals[4].Reg64 = 0x600000;      /* R10 = heap base */
        WHvSetVirtualProcessorRegisters(partition, 0, uefi_names, 5, uefi_vals);

        /* Also set GS base to 0 explicitly */
        WHV_REGISTER_NAME gs_name = WHvX64RegisterGs;
        WHV_REGISTER_VALUE gs_val;
        memset(&gs_val, 0, sizeof(gs_val));
        gs_val.Segment.Base = 0; gs_val.Segment.Limit = 0xFFFFFFFF;
        gs_val.Segment.Selector = 0x10; gs_val.Segment.Attributes = 0xC093;
        WHvSetVirtualProcessorRegisters(partition, 0, &gs_name, 1, &gs_val);

        fprintf(stderr, "UEFI boot: RIP=0x%llx RSP=0x%llx RDX(SystemTable)=0x%x\n",
            entry64, (unsigned long long)STACK_TOP, UEFI_TABLE_PAGE);
    } else {
        /* Multiboot/CDX: start in 32-bit protected mode */
        unsigned int mb_entry = *(unsigned int*)((unsigned char*)guest_mem + 0x500);
        vals[0].Reg64 = mb_entry ? mb_entry : LOAD_ADDR;
        vals[1].Reg64 = STACK_TOP;
        vals[2].Reg64 = 0x2;

        vals[3].Reg64 = 0x11;  /* CR0: PE + ET */
        vals[4].Reg64 = 0;     /* CR3 */
        vals[5].Reg64 = 0;     /* CR4 */
        vals[6].Reg64 = 0;     /* EFER */

        /* CS: 32-bit code segment */
        vals[7].Segment.Base = 0; vals[7].Segment.Limit = 0xFFFFFFFF;
        vals[7].Segment.Selector = 0x08;
        vals[7].Segment.Attributes = 0xC09B;

        for (int i = 8; i <= 11; i++) {
            vals[i].Segment.Base = 0; vals[i].Segment.Limit = 0xFFFFFFFF;
            vals[i].Segment.Selector = 0x10;
            vals[i].Segment.Attributes = 0xC093;
        }
        vals[12].Segment.Base = 0; vals[12].Segment.Limit = 0xFFFFFFFF;
        vals[12].Segment.Selector = 0x10; vals[12].Segment.Attributes = 0xC093;

        vals[13].Segment.Base = 0; vals[13].Segment.Limit = 0x67;
        vals[13].Segment.Selector = 0x18; vals[13].Segment.Attributes = 0x8B;

        vals[14].Reg64 = 0x2BADB002;  /* EAX = multiboot magic */
        vals[15].Reg64 = 0;           /* EBX */
    }

    HRESULT hr = WHvSetVirtualProcessorRegisters(partition, 0, names, 16, vals);
    if (FAILED(hr)) { fprintf(stderr, "SetRegs: 0x%lx\n", hr); exit(1); }
}

/* ── Watchpoint via page protection ──────────────────────────────── */

static unsigned long long watch_page_base = 0;  /* 4KB-aligned GPA of watched page */
static int watch_hit_count = 0;

static void watch_init(void) {
    if (!watch_addr || watch_addr >= guest_mem_size) return;
    watch_page_base = watch_addr & ~0xFFFULL;
    memcpy(watch_prev, (unsigned char*)guest_mem + watch_addr, watch_size);
    watch_active = 1;
    fprintf(stderr, "WATCH: 0x%llx (%d bytes), page 0x%llx\n", watch_addr, watch_size, watch_page_base);
    fprintf(stderr, "WATCH: initial value=");
    for (int i = 0; i < watch_size; i++) fprintf(stderr, "%02x", watch_prev[i]);
    fprintf(stderr, "\n");

    /* Remap the watched page as read+execute only (no write) */
    HRESULT hr = WHvUnmapGpaRange(partition, watch_page_base, 4096);
    if (FAILED(hr)) { fprintf(stderr, "WHvUnmapGpaRange: 0x%lx\n", hr); return; }
    hr = WHvMapGpaRange(partition, (unsigned char*)guest_mem + watch_page_base,
        watch_page_base, 4096, WHvMapGpaRangeFlagRead | WHvMapGpaRangeFlagExecute);
    if (FAILED(hr)) { fprintf(stderr, "WHvMapGpaRange(RX): 0x%lx\n", hr); return; }
    fprintf(stderr, "WATCH: page 0x%llx set to READ-ONLY (write traps enabled)\n", watch_page_base);
}

static void dump_guest_regs(const char *reason, unsigned long long gpa) {
    WHV_REGISTER_NAME names[] = {
        WHvX64RegisterRip, WHvX64RegisterRsp, WHvX64RegisterRax,
        WHvX64RegisterRbx, WHvX64RegisterRcx, WHvX64RegisterRdx,
        WHvX64RegisterRsi, WHvX64RegisterRdi, WHvX64RegisterRbp,
        WHvX64RegisterR8, WHvX64RegisterR9, WHvX64RegisterR10,
        WHvX64RegisterR11, WHvX64RegisterR12, WHvX64RegisterR13,
        WHvX64RegisterR14, WHvX64RegisterR15, WHvX64RegisterCr2
    };
    WHV_REGISTER_VALUE vals[18];
    WHvGetVirtualProcessorRegisters(partition, 0, names, 18, vals);
    fprintf(stderr, "\n=== WATCHPOINT HIT #%d: %s (GPA=0x%llx) ===\n", watch_hit_count, reason, gpa);
    fprintf(stderr, "RIP=%016llx RSP=%016llx\n", vals[0].Reg64, vals[1].Reg64);
    fprintf(stderr, "RAX=%016llx RBX=%016llx RCX=%016llx RDX=%016llx\n",
        vals[2].Reg64, vals[3].Reg64, vals[4].Reg64, vals[5].Reg64);
    fprintf(stderr, "RSI=%016llx RDI=%016llx RBP=%016llx\n",
        vals[6].Reg64, vals[7].Reg64, vals[8].Reg64);
    fprintf(stderr, "R8 =%016llx R9 =%016llx R10=%016llx R11=%016llx\n",
        vals[9].Reg64, vals[10].Reg64, vals[11].Reg64, vals[12].Reg64);
    fprintf(stderr, "R12=%016llx R13=%016llx R14=%016llx R15=%016llx\n",
        vals[13].Reg64, vals[14].Reg64, vals[15].Reg64, vals[16].Reg64);
    fprintf(stderr, "CR2=%016llx\n", vals[17].Reg64);

    /* Walk the stack for return addresses */
    unsigned long long rsp = vals[1].Reg64;
    fprintf(stderr, "Stack (code-range return addrs):\n");
    for (int i = 0; i < 32; i++) {
        unsigned long long saddr = rsp + i * 8;
        if (saddr + 8 > guest_mem_size) break;
        unsigned long long v = *(unsigned long long*)((unsigned char*)guest_mem + saddr);
        if (v >= LOAD_ADDR && v < LOAD_ADDR + 0x300000)
            fprintf(stderr, "  [RSP+0x%02x] = 0x%llx\n", i*8, v);
    }
    fprintf(stderr, "=== END WATCHPOINT ===\n\n");
}

static void handle_io(WHV_RUN_VP_EXIT_CONTEXT *ctx);  /* forward decl */

static int handle_watch_write(WHV_RUN_VP_EXIT_CONTEXT *ctx) {
    unsigned long long gpa = ctx->MemoryAccess.Gpa;

    if (ctx->MemoryAccess.AccessInfo.AccessType != 1) return 0;
    if (gpa < watch_page_base || gpa >= watch_page_base + 4096) return 0;

    watch_hit_count++;

    /* Check if this write overlaps our specific watched bytes */
    int targets_watch = (gpa + 8 > watch_addr && gpa < watch_addr + (unsigned long long)watch_size);
    if (targets_watch) {
        dump_guest_regs("WRITE TO WATCHED ADDRESS", gpa);
        fprintf(stderr, "WATCH: instruction bytes (%d): ", ctx->MemoryAccess.InstructionByteCount);
        for (int i = 0; i < ctx->MemoryAccess.InstructionByteCount && i < 16; i++)
            fprintf(stderr, "%02x ", ctx->MemoryAccess.InstructionBytes[i]);
        fprintf(stderr, "\n");
        return 2;
    }

    /* Non-target write on the same page. Emulate by temporarily unprotecting,
       executing JUST this one instruction via the WHP instruction emulator
       approach: unprotect → run → immediately cancel → re-protect.

       But since cancel has latency, instead: unprotect, re-protect immediately
       (the TLB might still have the writable entry), then resume. The CPU will
       re-walk page tables on the next access and trap again if needed.

       Actually simplest: just make writable, run until next exit, re-protect.
       But we proved that loses the corruption. So instead: make writable,
       immediately set the page writable in guest_mem, advance RIP past the
       instruction, and don't run the VP at all. */

    /* Perform the store ourselves: read instruction length, advance RIP */
    unsigned int ilen = ctx->VpContext.InstructionLength;
    if (ilen == 0) ilen = 1;  /* safety */

    /* Make page writable momentarily so guest memory is coherent */
    WHvUnmapGpaRange(partition, watch_page_base, 4096);
    WHvMapGpaRange(partition, (unsigned char*)guest_mem + watch_page_base,
        watch_page_base, 4096, WHvMapGpaRangeFlagRead | WHvMapGpaRangeFlagWrite | WHvMapGpaRangeFlagExecute);

    /* Execute just this one instruction by running with immediate cancel */
    /* Actually: use a simpler trick — request an interrupt window exit,
       which fires after the very next instruction */
    WHV_REGISTER_NAME names[2];
    WHV_REGISTER_VALUE vals[2];
    memset(vals, 0, sizeof(vals));
    names[0] = WHvRegisterDeliverabilityNotifications;
    vals[0].DeliverabilityNotifications.InterruptNotification = 1;
    names[1] = WHvRegisterInternalActivityState;
    WHvSetVirtualProcessorRegisters(partition, 0, names, 2, vals);

    WHV_RUN_VP_EXIT_CONTEXT step_ctx;
    WHvRunVirtualProcessor(partition, 0, &step_ctx, sizeof(step_ctx));

    /* Check if target changed */
    unsigned char *cur = (unsigned char*)guest_mem + watch_addr;
    if (memcmp(cur, watch_prev, watch_size) != 0) {
        fprintf(stderr, "\nWATCH: TARGET MODIFIED after emulating hit #%d!\n", watch_hit_count);
        fprintf(stderr, "Emulated instruction was at RIP=0x%llx\n", ctx->VpContext.Rip);
        dump_guest_regs("state after target modification", gpa);
        fprintf(stderr, "MEM[0x%llx]=", watch_addr);
        for (int i = 0; i < watch_size; i++) fprintf(stderr, "%02x", cur[i]);
        fprintf(stderr, " (prev=");
        for (int i = 0; i < watch_size; i++) fprintf(stderr, "%02x", watch_prev[i]);
        fprintf(stderr, ")\n");
        memcpy(watch_prev, cur, watch_size);
        return 2;
    }

    /* Handle the step exit if it was an I/O or other event */
    if (step_ctx.ExitReason == WHvRunVpExitReasonX64IoPortAccess) {
        handle_io(&step_ctx);
    }

    /* Re-protect */
    WHvUnmapGpaRange(partition, watch_page_base, 4096);
    WHvMapGpaRange(partition, (unsigned char*)guest_mem + watch_page_base,
        watch_page_base, 4096, WHvMapGpaRangeFlagRead | WHvMapGpaRangeFlagExecute);

    return 1;
}

/* ── I/O dispatch ──────────────────────────────────────────────────── */

static void handle_io(WHV_RUN_VP_EXIT_CONTEXT *ctx) {
    int port = ctx->IoPortAccess.PortNumber;
    int is_out = (ctx->IoPortAccess.AccessInfo.IsWrite != 0);
    int size = ctx->IoPortAccess.AccessInfo.AccessSize;
    int val = 0;
    if (is_out) val = (int)ctx->IoPortAccess.Rax;

    if (is_out) {
        /* Serial COM1 */
        if (port >= 0x3F8 && port <= 0x3FF) {
            if (port == 0x3F8 && !com1.dlab) serial_send(&com1, (unsigned char)val);
            else if (port == 0x3FB) com1.dlab = (val & 0x80) ? 1 : 0;
        }
        /* Serial COM2 */
        else if (port >= 0x2F8 && port <= 0x2FF) {
            if (port == 0x2F8 && !com2.dlab) serial_send(&com2, (unsigned char)val);
            else if (port == 0x2FB) com2.dlab = (val & 0x80) ? 1 : 0;
        }
        /* PIC master */
        else if (port == 0x20 || port == 0x21) {
            pic_handle_out(&pic_master, port == 0x21, val);
        }
        /* PIC slave */
        else if (port == 0xA0 || port == 0xA1) {
            pic_handle_out(&pic_slave, port == 0xA1, val);
        }
        /* PIT */
        else if (port >= 0x40 && port <= 0x43) {
            /* Accept PIT programming silently; timer is host-driven */
        }
        /* IDE */
        else if ((port >= 0x1F0 && port <= 0x1F7) || port == 0x3F6) {
            ide_handle_out(&ide, port, val);
        }
        /* Debug exit */
        else if (port == 0xF4) {
            debug_exit_code = val;
        }
        /* NE2000 NIC (0x300-0x31F) */
        else if (port >= NE2K_BASE && port < NE2K_BASE + 0x20) {
            ne2k_handle_out(port, val, size);
        }
        /* Keyboard controller (0x60/0x64) — accept silently */
        else if (port == 0x60 || port == 0x64) {
            /* guest disables keyboard; ignore */
        }
        /* VGA Attribute Controller (0x3C0) */
        else if (port == 0x3C0) {
            if (!vga_attr_flipflop) { vga_attr_index = val & 0x3F; }
            vga_attr_flipflop ^= 1;
        }
        /* VGA misc/sequencer/DAC — accept silently */
        else if (port >= 0x3C2 && port <= 0x3CF) { }
        else if (port >= 0x3D4 && port <= 0x3D5) { }
        /* LAPIC disable via MSR is handled in handle_msr; ignore port 0xFEE00xx */
    } else {
        int result = 0xFF;
        /* Serial COM1 */
        if (port >= 0x3F8 && port <= 0x3FF) {
            if (port == 0x3F8 && !com1.dlab) { int b = serial_recv(&com1); result = (b >= 0) ? b : com1.last_rx; }
            else if (port == 0x3FA) result = 1;  /* IIR: no interrupt pending */
            else if (port == 0x3FD) result = 0x60 | ((serial_has_data(&com1) && guest_ring_has_room()) ? 1 : 0);
            else if (port == 0x3FE) result = 0xB0; /* MSR */
            else result = 0;
        }
        /* Serial COM2 */
        else if (port >= 0x2F8 && port <= 0x2FF) {
            if (port == 0x2F8 && !com2.dlab) { int b = serial_recv(&com2); result = (b >= 0) ? b : com2.last_rx; }
            else if (port == 0x2FA) result = 1;
            else if (port == 0x2FD) result = 0x60 | (serial_has_data(&com2) ? 1 : 0);
            else if (port == 0x2FE) result = 0xB0;
            else result = 0;
        }
        /* PIC master */
        else if (port == 0x20 || port == 0x21) {
            result = pic_handle_in(&pic_master, port == 0x21);
        }
        /* PIC slave */
        else if (port == 0xA0 || port == 0xA1) {
            result = pic_handle_in(&pic_slave, port == 0xA1);
        }
        /* PIT */
        else if (port >= 0x40 && port <= 0x43) {
            result = 0;
        }
        /* IDE */
        else if ((port >= 0x1F0 && port <= 0x1F7) || port == 0x3F6) {
            result = ide_handle_in(&ide, port);
        }
        /* NE2000 NIC */
        else if (port >= NE2K_BASE && port < NE2K_BASE + 0x20) {
            result = ne2k_handle_in(port, size);
        }
        /* VGA Attribute Controller */
        else if (port == 0x3C0) {
            result = vga_attr_index | 0x20;  /* PAS bit always set on read */
            vga_attr_flipflop = 0;
        }
        else if (port == 0x3C1) { result = 0; }
        else if (port == 0x3DA) { vga_attr_flipflop = 0; result = 0; }
        else if (port >= 0x3C2 && port <= 0x3CF) { result = 0; }
        else if (port >= 0x3D4 && port <= 0x3D5) { result = 0; }
        /* Keyboard controller */
        else if (port == 0x60) {
            int sc = kbd_dequeue();
            result = (sc >= 0) ? sc : 0;
        }
        else if (port == 0x64) {
            result = (kbd_count > 0) ? 1 : 0; /* bit 0 = OBF (output buffer full) */
        }

        /* Inject result into RAX */
        WHV_REGISTER_NAME rax_name = WHvX64RegisterRax;
        WHV_REGISTER_VALUE rax_val;
        WHvGetVirtualProcessorRegisters(partition, 0, &rax_name, 1, &rax_val);
        if (size == 1) rax_val.Reg64 = (rax_val.Reg64 & ~0xFFULL) | (result & 0xFF);
        else if (size == 2) rax_val.Reg64 = (rax_val.Reg64 & ~0xFFFFULL) | (result & 0xFFFF);
        else rax_val.Reg64 = result;
        WHvSetVirtualProcessorRegisters(partition, 0, &rax_name, 1, &rax_val);
    }

    /* Advance RIP past the I/O instruction */
    WHV_REGISTER_NAME rip_name = WHvX64RegisterRip;
    WHV_REGISTER_VALUE rip_val;
    rip_val.Reg64 = ctx->VpContext.Rip + ctx->VpContext.InstructionLength;
    WHvSetVirtualProcessorRegisters(partition, 0, &rip_name, 1, &rip_val);
}

static void handle_cpuid(WHV_RUN_VP_EXIT_CONTEXT *ctx) {
    unsigned long long leaf = ctx->CpuidAccess.Rax;
    WHV_REGISTER_NAME names[] = { WHvX64RegisterRax, WHvX64RegisterRbx, WHvX64RegisterRcx, WHvX64RegisterRdx, WHvX64RegisterRip };
    WHV_REGISTER_VALUE vals[5];
    memset(vals, 0, sizeof(vals));
    if (leaf == 0) { vals[0].Reg64 = 1; vals[1].Reg64 = 0x756E6547; vals[2].Reg64 = 0x6C65746E; vals[3].Reg64 = 0x49656E69; }
    else if (leaf == 1) { vals[0].Reg64 = 0x000306C3; vals[2].Reg64 = 0; vals[3].Reg64 = 0x078BFBFF; }
    else if (leaf == 0x80000000) { vals[0].Reg64 = 0x80000001; }
    else if (leaf == 0x80000001) { vals[3].Reg64 = (1 << 29) | (1 << 20); } /* LM + NX */
    vals[4].Reg64 = ctx->VpContext.Rip + ctx->VpContext.InstructionLength;
    WHvSetVirtualProcessorRegisters(partition, 0, names, 5, vals);
}

/* MSR storage for guest-visible MSRs */
static unsigned long long msr_efer = 0;
static unsigned long long msr_star = 0;
static unsigned long long msr_lstar = 0;
static unsigned long long msr_cstar = 0;
static unsigned long long msr_sfmask = 0;
static unsigned long long msr_kernel_gs_base = 0;
static unsigned long long msr_apic_base = 0xFEE00900ULL; /* default: enabled, BSP */

static void handle_msr(WHV_RUN_VP_EXIT_CONTEXT *ctx, int is_write) {
    unsigned int msr_id = ctx->MsrAccess.MsrNumber;
    unsigned long long write_val = ((unsigned long long)ctx->MsrAccess.Rdx << 32) | (ctx->MsrAccess.Rax & 0xFFFFFFFF);

    if (is_write) {
        /* Apply the MSR write to the virtual processor where WHP needs it */
        switch (msr_id) {
        case 0xC0000080: { /* EFER */
            msr_efer = write_val;
            WHV_REGISTER_NAME n = WHvX64RegisterEfer;
            WHV_REGISTER_VALUE v; v.Reg64 = write_val;
            WHvSetVirtualProcessorRegisters(partition, 0, &n, 1, &v);
            break;
        }
        case 0xC0000081: msr_star = write_val; {
            WHV_REGISTER_NAME n = WHvX64RegisterStar;
            WHV_REGISTER_VALUE v; v.Reg64 = write_val;
            WHvSetVirtualProcessorRegisters(partition, 0, &n, 1, &v);
            break;
        }
        case 0xC0000082: msr_lstar = write_val; {
            WHV_REGISTER_NAME n = WHvX64RegisterLstar;
            WHV_REGISTER_VALUE v; v.Reg64 = write_val;
            WHvSetVirtualProcessorRegisters(partition, 0, &n, 1, &v);
            break;
        }
        case 0xC0000083: msr_cstar = write_val; {
            WHV_REGISTER_NAME n = WHvX64RegisterCstar;
            WHV_REGISTER_VALUE v; v.Reg64 = write_val;
            WHvSetVirtualProcessorRegisters(partition, 0, &n, 1, &v);
            break;
        }
        case 0xC0000084: msr_sfmask = write_val; {
            WHV_REGISTER_NAME n = WHvX64RegisterSfmask;
            WHV_REGISTER_VALUE v; v.Reg64 = write_val;
            WHvSetVirtualProcessorRegisters(partition, 0, &n, 1, &v);
            break;
        }
        case 0xC0000102: msr_kernel_gs_base = write_val; {
            WHV_REGISTER_NAME n = WHvX64RegisterKernelGsBase;
            WHV_REGISTER_VALUE v; v.Reg64 = write_val;
            WHvSetVirtualProcessorRegisters(partition, 0, &n, 1, &v);
            break;
        }
        case 0x1B: msr_apic_base = write_val; break; /* IA32_APIC_BASE — just store */
        default: break;
        }
    } else {
        /* MSR read: return stored value */
        unsigned long long read_val = 0;
        switch (msr_id) {
        case 0xC0000080: read_val = msr_efer; break;
        case 0xC0000081: read_val = msr_star; break;
        case 0xC0000082: read_val = msr_lstar; break;
        case 0xC0000083: read_val = msr_cstar; break;
        case 0xC0000084: read_val = msr_sfmask; break;
        case 0xC0000102: read_val = msr_kernel_gs_base; break;
        case 0x1B: read_val = msr_apic_base; break;
        default: break;
        }
        WHV_REGISTER_NAME names[] = { WHvX64RegisterRax, WHvX64RegisterRdx };
        WHV_REGISTER_VALUE vals[2];
        vals[0].Reg64 = read_val & 0xFFFFFFFF;
        vals[1].Reg64 = (read_val >> 32) & 0xFFFFFFFF;
        WHvSetVirtualProcessorRegisters(partition, 0, names, 2, vals);
    }

    /* Advance RIP */
    WHV_REGISTER_NAME rip_name = WHvX64RegisterRip;
    WHV_REGISTER_VALUE rip_val;
    rip_val.Reg64 = ctx->VpContext.Rip + ctx->VpContext.InstructionLength;
    WHvSetVirtualProcessorRegisters(partition, 0, &rip_name, 1, &rip_val);
}

static void inject_interrupt(int vector) {
    WHV_REGISTER_NAME names[2];
    WHV_REGISTER_VALUE vals[2];
    memset(vals, 0, sizeof(vals));
    names[0] = WHvRegisterInternalActivityState;
    names[1] = WHvRegisterPendingInterruption;
    vals[1].PendingInterruption.InterruptionPending = 1;
    vals[1].PendingInterruption.InterruptionType = 0;
    vals[1].PendingInterruption.InterruptionVector = vector;
    WHvSetVirtualProcessorRegisters(partition, 0, names, 2, vals);
}

static void inject_timer_interrupt(void) {
    int vec = pic_master.vector_base ? pic_master.vector_base : 32;
    inject_interrupt(vec);  /* IRQ0 = vector_base + 0 */
}

static int try_inject_serial_interrupt(void) {
    /* IRQ4 = serial COM1 RX. Only inject if PIC is programmed, IRQ4 unmasked, and data pending. */
    if (!pic_master.vector_base) return 0;       /* PIC not yet programmed */
    if (pic_master.mask & (1 << 4)) return 0;    /* IRQ4 masked */
    if (!serial_has_data(&com1)) return 0;
    if (serial_irq_pending) return 0;            /* wait for guest to read previous byte */
    serial_irq_pending = 1;
    inject_interrupt(pic_master.vector_base + 4); /* IRQ4 */
    return 1;
}

/* (monitor thread removed — page protection provides precise trapping) */

/* ── Main loop ─────────────────────────────────────────────────────── */

int main(int argc, char **argv) {
    const char *kernel = NULL, *disk = NULL;
    int mem_mb = 2048, data_port = 12345, ctrl_port = 12346;

    for (int i = 1; i < argc; i++) {
        if (!strcmp(argv[i], "-kernel") && i+1 < argc) kernel = argv[++i];
        else if (!strcmp(argv[i], "-disk") && i+1 < argc) disk = argv[++i];
        else if (!strcmp(argv[i], "-mem") && i+1 < argc) mem_mb = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-data-port") && i+1 < argc) data_port = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-ctrl-port") && i+1 < argc) ctrl_port = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-watch") && i+1 < argc) watch_addr = strtoull(argv[++i], NULL, 0);
        else if (!strcmp(argv[i], "-watch-size") && i+1 < argc) watch_size = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-headless")) vga_headless = 1;
        else if (!strcmp(argv[i], "-uefi")) uefi_mode = 1;
        else if (!strcmp(argv[i], "-gop")) { gop_active = 1; }
        else if (!strcmp(argv[i], "-gop-width") && i+1 < argc) { gop_width = atoi(argv[++i]); gop_stride = gop_width; gop_active = 1; }
        else if (!strcmp(argv[i], "-gop-height") && i+1 < argc) { gop_height = atoi(argv[++i]); gop_active = 1; }
    }
    if (!kernel) {
        fprintf(stderr, "Usage: codex-vm -kernel file.cdx [-disk file.img] [-mem MB]\n"
                        "       [-data-port N] [-ctrl-port N]\n"
                        "       [-watch 0xADDR] [-watch-size N] [-headless] [-uefi]\n"
                        "       [-gop] [-gop-width N] [-gop-height N]\n");
        return 1;
    }
    if (watch_size > 64) watch_size = 64;

    if (getenv("CODEX_VM_NO_TIMER")) { no_timer = 1; fprintf(stderr, "TIMER INTERRUPTS DISABLED\n"); }

    WSADATA wsa; WSAStartup(MAKEWORD(2,2), &wsa);

    serial_init(&com1, data_port);
    serial_init(&com2, ctrl_port);
    ide_init(&ide, disk);
    pic_init(&pic_master);
    pic_init(&pic_slave);
    ne2k_reset();

    create_vm(mem_mb);
    load_kernel(kernel);
    set_initial_regs();

    if (!uefi_mode) {
        fprintf(stderr, "VM ready. Waiting for connections on ports %d/%d...\n", data_port, ctrl_port);
        serial_accept(&com1);
        serial_accept(&com2);
        fprintf(stderr, "Connected. Running guest (mem=%dMB)...\n", mem_mb);
    } else {
        fprintf(stderr, "UEFI VM starting (mem=%dMB)...\n", mem_mb);
    }
    vga_start();

    QueryPerformanceFrequency(&perf_freq);
    QueryPerformanceCounter(&last_tick);

    if (watch_addr) watch_init();

    if (uefi_mode) {
        /* Verify guest memory at entry point */
        unsigned char *ep = (unsigned char *)guest_mem + 0x1000;
        fprintf(stderr, "Guest mem at 0x1000: %02x %02x %02x %02x %02x\n", ep[0], ep[1], ep[2], ep[3], ep[4]);
        /* Verify page tables */
        unsigned long long *pml4 = (unsigned long long *)((unsigned char *)guest_mem + PAGE_TABLE_ADDR);
        unsigned long long *pdpt = (unsigned long long *)((unsigned char *)guest_mem + PAGE_TABLE_ADDR + 4096);
        unsigned long long *pd = (unsigned long long *)((unsigned char *)guest_mem + PAGE_TABLE_ADDR + 8192);
        fprintf(stderr, "PML4[0]=0x%llx PDPT[0]=0x%llx PD[0]=0x%llx\n", pml4[0], pdpt[0], pd[0]);
    }

    WHV_RUN_VP_EXIT_CONTEXT ctx;
    unsigned long long exits = 0;
    int watch_hits = 0;
    int pending_irq = -1;      /* next interrupt vector to deliver, or -1 */
    int halted = 0;
    int window_registered = 0;

    /* Shadow register file — workaround for WHP corrupting GPRs across VM exits.
     * We snapshot all 16 GPRs after every exit, then force-write them back before
     * the next VP run. Exit handlers (handle_io, handle_cpuid, handle_msr) modify
     * shadow_gprs[] directly instead of calling WHvSetVirtualProcessorRegisters. */
    static const WHV_REGISTER_NAME shadow_names[16] = {
        WHvX64RegisterRax, WHvX64RegisterRcx, WHvX64RegisterRdx, WHvX64RegisterRbx,
        WHvX64RegisterRsp, WHvX64RegisterRbp, WHvX64RegisterRsi, WHvX64RegisterRdi,
        WHvX64RegisterR8,  WHvX64RegisterR9,  WHvX64RegisterR10, WHvX64RegisterR11,
        WHvX64RegisterR12, WHvX64RegisterR13, WHvX64RegisterR14, WHvX64RegisterR15
    };
    WHV_REGISTER_VALUE shadow_gprs[16];
    int shadow_valid = 0;

    for (;;) {
        /* ── Pre-run: inject pending interrupt if guest is ready ── */
        if (pending_irq >= 0) {
            /* Read current execution state to check interruptability */
            WHV_REGISTER_NAME es_name = WHvX64RegisterEs;  /* dummy, we use ctx */
            int can_inject = (ctx.VpContext.Rflags & 0x200) != 0;  /* IF=1 */

            if (can_inject && exits > 0) {
                WHV_REGISTER_NAME inj_names[2];
                WHV_REGISTER_VALUE inj_vals[2];
                memset(inj_vals, 0, sizeof(inj_vals));
                inj_names[0] = WHvRegisterPendingInterruption;
                inj_vals[0].PendingInterruption.InterruptionPending = 1;
                inj_vals[0].PendingInterruption.InterruptionType = 0; /* WHvX64PendingInterrupt */
                inj_vals[0].PendingInterruption.InterruptionVector = pending_irq;
                inj_names[1] = WHvRegisterInternalActivityState;
                /* inj_vals[1] = zero (clear any activity block) */
                WHvSetVirtualProcessorRegisters(partition, 0, inj_names, 2, inj_vals);
                pending_irq = -1;
                halted = 0;
                window_registered = 0;
            } else if (!can_inject && !window_registered && exits > 0) {
                /* Guest has IF=0, request interrupt window */
                WHV_REGISTER_NAME iw_name = WHvRegisterDeliverabilityNotifications;
                WHV_REGISTER_VALUE iw_val;
                memset(&iw_val, 0, sizeof(iw_val));
                iw_val.DeliverabilityNotifications.InterruptNotification = 1;
                WHvSetVirtualProcessorRegisters(partition, 0, &iw_name, 1, &iw_val);
                window_registered = 1;
            }
        }

        /* ── Writeback shadow GPRs before run (WHP corruption workaround) ── */
        if (shadow_valid) {
            WHvSetVirtualProcessorRegisters(partition, 0, shadow_names, 16, shadow_gprs);
        }

        /* ── Run VP ── */
        HRESULT hr = WHvRunVirtualProcessor(partition, 0, &ctx, sizeof(ctx));
        if (FAILED(hr)) { fprintf(stderr, "WHvRunVirtualProcessor: 0x%lx\n", hr); break; }
        exits++;
        if (uefi_mode && exits <= 20) {
            fprintf(stderr, "  exit #%llu: reason=%d RIP=0x%llx\n", exits, ctx.ExitReason, ctx.VpContext.Rip);
        }

        /* (shadow snapshot moved to after exit handling) */

        /* ── Handle exit ── */
        switch (ctx.ExitReason) {
        case WHvRunVpExitReasonCanceled:
            break;
        case WHvRunVpExitReasonX64IoPortAccess:
            handle_io(&ctx);
            if (debug_exit_code >= 0) goto done;
            break;
        case WHvRunVpExitReasonX64Cpuid:
            handle_cpuid(&ctx);
            break;
        case WHvRunVpExitReasonX64MsrAccess:
            handle_msr(&ctx, ctx.MsrAccess.AccessInfo.IsWrite);
            break;
        case WHvRunVpExitReasonX64Halt:
            if (uefi_mode && ctx.VpContext.Rip >= UEFI_TRAP_PAGE && ctx.VpContext.Rip < UEFI_TRAP_PAGE + 0x1000) {
                int r = uefi_handle_trap(&ctx);
                if (r == 2) goto done;
                if (r) break;
            }
            if (ctx.VpContext.Rflags & 0x200) {
                halted = 1;
            } else {
                fprintf(stderr, "Guest halted with IF=0 after %llu exits\n", exits);
                dump_guest_regs("halt with IF=0", 0);
                /* Dump bytes at the faulting RIP from guest memory */
                {
                    WHV_REGISTER_NAME rn = WHvX64RegisterRip;
                    WHV_REGISTER_VALUE rv;
                    WHvGetVirtualProcessorRegisters(partition, 0, &rn, 1, &rv);
                    unsigned long long rip = rv.Reg64;
                    if (rip > 0 && rip + 32 < guest_mem_size) {
                        unsigned char *p = (unsigned char*)guest_mem + rip;
                        fprintf(stderr, "Guest mem at RIP 0x%llx: ", rip);
                        for (int i = -8; i < 24; i++)
                            fprintf(stderr, "%s%02x", (i==0) ? "[" : " ", p[i]);
                        fprintf(stderr, "]\n");
                    }
                }
                goto done;
            }
            break;
        case WHvRunVpExitReasonX64InterruptWindow:
            window_registered = 0;
            /* Fall through to post-exit logic which will inject */
            break;
        case WHvRunVpExitReasonException:
            fprintf(stderr, "Exception vector=%d at RIP=0x%llx\n",
                ctx.VpException.ExceptionType, ctx.VpContext.Rip);
            goto done;
        case WHvRunVpExitReasonMemoryAccess:
            if (uefi_mode && ctx.MemoryAccess.AccessInfo.AccessType == 2 && uefi_handle_trap(&ctx)) {
                break;  /* UEFI protocol call handled */
            }
            if (watch_active) {
                int wr = handle_watch_write(&ctx);
                if (wr == 2) goto done;  /* target hit or crash */
                if (wr == 1) {
                    if (watch_hit_count >= 5000) { fprintf(stderr, "WATCH: 5000 page hits.\n"); goto done; }
                    break;
                }
            }
            fprintf(stderr, "MMIO at GPA=0x%llx (RIP=0x%llx, access=%s) after %llu exits\n",
                ctx.MemoryAccess.Gpa, ctx.VpContext.Rip,
                ctx.MemoryAccess.AccessInfo.AccessType == 0 ? "read" :
                ctx.MemoryAccess.AccessInfo.AccessType == 1 ? "write" : "exec",
                exits);
            goto done;
        default:
            /* Check for UEFI HLT trap on any unhandled exit reason */
            if (uefi_mode && ctx.VpContext.Rip > UEFI_TRAP_PAGE && ctx.VpContext.Rip <= UEFI_TRAP_PAGE + 0x1000) {
                int r = uefi_handle_trap(&ctx);
                if (r == 2) goto done;  /* clean UEFI exit */
                if (r == 1) break;
            }
            /* Work around PeWriter trampoline bug: the stub writes target at 0x700A
               (off by 2), clobbering the MOV RAX opcode. At exit reason 4 near 0x700x,
               extract the target and set RIP directly. CR3 was already loaded. */
            if (uefi_mode && ctx.ExitReason == 4 && exits <= 10) {
                /* Dump trampoline and page tables for debugging */
                unsigned char *t = (unsigned char *)guest_mem + 0x70000;
                fprintf(stderr, "  tramp@0x70000: %02x %02x %02x %02x %02x %02x %02x %02x %02x %02x %02x %02x %02x %02x %02x %02x %02x %02x %02x %02x %02x %02x\n",
                    t[0],t[1],t[2],t[3],t[4],t[5],t[6],t[7],t[8],t[9],t[10],t[11],t[12],t[13],t[14],t[15],t[16],t[17],t[18],t[19],t[20],t[21]);
                unsigned long long *pml4 = (unsigned long long *)((unsigned char *)guest_mem + 0x1000);
                unsigned long long *pdpt = (unsigned long long *)((unsigned char *)guest_mem + 0x2000);
                unsigned long long *pd = (unsigned long long *)((unsigned char *)guest_mem + 0x3000);
                fprintf(stderr, "  PML4[0]=0x%llx PDPT[0]=0x%llx PD[0]=0x%llx PD[1]=0x%llx\n", pml4[0], pdpt[0], pd[0], pd[1]);
            }
            if (uefi_mode && ctx.ExitReason == 4) {
                /* UEFI trampoline fixup: the PE stub wrote target at 0x700A,
                   loaded CR3=0x1000 (its own page tables), but the MOV RAX was
                   mangled. Fix up: read target, set RIP, and re-validate VP state. */
                unsigned long long target = *(unsigned long long *)((unsigned char *)guest_mem + 0x700A);
                if (target > 0x1000 && target < guest_mem_size) {
                    fprintf(stderr, "UEFI trampoline fixup: target=0x%llx CR3=0x%llx\n", target, ctx.VpContext.Cr8);
                    /* Re-set full VP state for 64-bit mode with stub's page tables */
                    WHV_REGISTER_NAME fix_names[] = {
                        WHvX64RegisterRip, WHvX64RegisterCr0, WHvX64RegisterCr3,
                        WHvX64RegisterCr4, WHvX64RegisterEfer,
                        WHvX64RegisterCs, WHvX64RegisterSs, WHvX64RegisterDs,
                        WHvX64RegisterEs, WHvX64RegisterFs, WHvX64RegisterGs,
                        WHvX64RegisterTr, WHvX64RegisterGdtr
                    };
                    WHV_REGISTER_VALUE fix_vals[13];
                    memset(fix_vals, 0, sizeof(fix_vals));
                    fix_vals[0].Reg64 = target;          /* RIP */
                    fix_vals[1].Reg64 = 0x80000011;      /* CR0: PE+PG+ET */
                    fix_vals[2].Reg64 = 0x1000;          /* CR3: stub's page tables */
                    fix_vals[3].Reg64 = 0x620;           /* CR4: PAE+OSFXSR+OSXMMEXCPT */
                    fix_vals[4].Reg64 = 0xD01;           /* EFER: SCE+LME+LMA+NXE */
                    /* CS: 64-bit code */
                    fix_vals[5].Segment.Selector = 0x08;
                    fix_vals[5].Segment.Base = 0; fix_vals[5].Segment.Limit = 0xFFFFFFFF;
                    fix_vals[5].Segment.Attributes = 0xA09B;
                    /* SS, DS, ES, FS, GS: data */
                    for (int s = 6; s <= 10; s++) {
                        fix_vals[s].Segment.Selector = 0x10;
                        fix_vals[s].Segment.Base = 0; fix_vals[s].Segment.Limit = 0xFFFFFFFF;
                        fix_vals[s].Segment.Attributes = 0xC093;
                    }
                    /* TR: TSS */
                    fix_vals[11].Segment.Selector = 0x18;
                    fix_vals[11].Segment.Base = 0xA100; fix_vals[11].Segment.Limit = 0x67;
                    fix_vals[11].Segment.Attributes = 0x8B;
                    /* GDTR */
                    fix_vals[12].Table.Base = 0xA000; fix_vals[12].Table.Limit = 39;
                    WHvSetVirtualProcessorRegisters(partition, 0, fix_names, 13, fix_vals);
                    break;
                }
            }
            fprintf(stderr, "Unhandled exit reason %d (RIP=0x%llx) after %llu exits\n",
                ctx.ExitReason, ctx.VpContext.Rip, exits);
            if (ctx.ExitReason == 4) {
                WHV_REGISTER_NAME dbg_names[] = {WHvX64RegisterCr0, WHvX64RegisterCr3, WHvX64RegisterCr4, WHvX64RegisterEfer, WHvX64RegisterCs, WHvX64RegisterSs, WHvX64RegisterTr};
                WHV_REGISTER_VALUE dbg_vals[7];
                WHvGetVirtualProcessorRegisters(partition, 0, dbg_names, 7, dbg_vals);
                fprintf(stderr, "  CR0=0x%llx CR3=0x%llx CR4=0x%llx EFER=0x%llx\n", dbg_vals[0].Reg64, dbg_vals[1].Reg64, dbg_vals[2].Reg64, dbg_vals[3].Reg64);
                fprintf(stderr, "  CS: sel=0x%x attr=0x%x base=0x%llx limit=0x%x\n", dbg_vals[4].Segment.Selector, dbg_vals[4].Segment.Attributes, dbg_vals[4].Segment.Base, dbg_vals[4].Segment.Limit);
                fprintf(stderr, "  SS: sel=0x%x attr=0x%x\n", dbg_vals[5].Segment.Selector, dbg_vals[5].Segment.Attributes);
                fprintf(stderr, "  TR: sel=0x%x attr=0x%x base=0x%llx limit=0x%x\n", dbg_vals[6].Segment.Selector, dbg_vals[6].Segment.Attributes, dbg_vals[6].Segment.Base, dbg_vals[6].Segment.Limit);
            }
            goto done;
        }

        /* ── Snapshot GPRs after exit handling (captures handler's RAX/RIP changes) ── */
        WHvGetVirtualProcessorRegisters(partition, 0, shadow_names, 16, shadow_gprs);
        shadow_valid = 1;

        /* ── Post-exit: decide what interrupt to queue ── */
        if (pending_irq < 0) {
            int vec = pic_master.vector_base ? pic_master.vector_base : 32;
            /* Serial IRQ4 takes priority over timer — but only if previous serial
               IRQ was acknowledged (EOI). Without this guard, the guest can get
               nested serial interrupts during large serial transfers. */
            if (pic_master.vector_base && serial_has_data(&com1) && !(pic_master.mask & (1 << 4)) && !serial_irq_pending && guest_ring_has_room()) {
                serial_irq_pending = 1;
                pending_irq = vec + 4;
            } else if (kbd_irq_pending && kbd_count > 0 && pic_master.vector_base && !(pic_master.mask & (1 << 1))) {
                kbd_irq_pending = 0;
                pending_irq = vec + 1;  /* IRQ 1 = keyboard */
            } else if (halted) {
                /* Halted waiting for interrupt — check timer or sleep */
                LARGE_INTEGER now;
                QueryPerformanceCounter(&now);
                double elapsed = (double)(now.QuadPart - last_tick.QuadPart) / perf_freq.QuadPart;
                if (!no_timer && elapsed >= 0.055) {
                    QueryPerformanceCounter(&last_tick);
                    pending_irq = vec;  /* timer tick */
                } else if (no_timer && serial_has_data(&com1) && pic_master.vector_base && !(pic_master.mask & (1<<4)) && !serial_irq_pending && guest_ring_has_room()) {
                    serial_irq_pending = 1;
                    pending_irq = vec + 4;  /* no timer, but serial data wakes us */
                } else {
                    /* Sleep until next tick, but wake early if serial data arrives */
                    DWORD ms = (DWORD)((0.055 - elapsed) * 1000.0);
                    if (ms > 50) ms = 50;  /* cap sleep to check serial periodically */
                    if (ms > 0) Sleep(ms);
                    /* Re-check serial after sleep */
                    if (pic_master.vector_base && serial_has_data(&com1) && !(pic_master.mask & (1 << 4)) && !serial_irq_pending && guest_ring_has_room()) {
                        serial_irq_pending = 1;
                        pending_irq = vec + 4;
                    } else {
                        QueryPerformanceCounter(&now);
                        elapsed = (double)(now.QuadPart - last_tick.QuadPart) / perf_freq.QuadPart;
                        if (!no_timer && elapsed >= 0.055) {
                            QueryPerformanceCounter(&last_tick);
                            pending_irq = vec;
                        }
                    }
                }
            }
        }

        /* Poll NAT sockets for incoming data and inject into NE2000 ring buffer */
        if (exits % 10000 == 0) { nat_poll_rx(); ne2k_inject_rx(); }

        /* (page-protection watchpoint is handled inline in MemoryAccess case) */
    }
done:
    fprintf(stderr, "VM exited (code=%d, exits=%llu, watch_hits=%d)\n", debug_exit_code, exits, watch_hit_count);
    serial_close(&com1);
    serial_close(&com2);
    WHvDeleteVirtualProcessor(partition, 0);
    WHvDeletePartition(partition);
    if (ide.data) free(ide.data);
    VirtualFree(guest_mem, 0, MEM_RELEASE);
    WSACleanup();
    return (debug_exit_code >= 0) ? (debug_exit_code << 1) | 1 : 0;
}
