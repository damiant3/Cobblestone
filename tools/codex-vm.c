/*
 * codex-vm: Minimal WHP-based VM for Codex bare-metal binaries.
 * Replaces QEMU for development. Serial on TCP sockets, IDE from raw file.
 *
 * Usage: codex-vm.exe -kernel file.cdx [-disk file.img] [-disk2 file.img] [-mem 2048]
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
#include <math.h>

#pragma comment(lib, "WinHvPlatform.lib")
#pragma comment(lib, "ws2_32.lib")
#pragma comment(lib, "gdi32.lib")
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "winmm.lib")
#include <mmsystem.h>

/* Forward declarations (defined later in the file) */
static void *guest_mem;
static size_t guest_mem_size;
static WHV_PARTITION_HANDLE partition;
static int smp_cores;  /* 0 or 1 = single-core (default); 2-16 = multi-core */

/* MSR storage for guest-visible MSRs.
   Declared here rather than beside handle_msr because the boot paths that set a
   register on the VP directly must also set the shadow, and they run earlier in
   this file. A shadow that disagrees with the register is a diagnostic that
   lies: the guest reads one value and the CPU holds another. */
static unsigned long long msr_efer = 0;
static unsigned long long msr_star = 0;
static unsigned long long msr_lstar = 0;
static unsigned long long msr_cstar = 0;
static unsigned long long msr_sfmask = 0;
static unsigned long long msr_kernel_gs_base = 0;
static unsigned long long msr_apic_base = 0xFEE00900ULL; /* default: enabled, BSP */

/* System-wide mutex: serialize WHP partition create/destroy across all
   codex-vm instances.  vid.sys on Win11 26100 corrupts its kernel heap
   when multiple processes hit WHvCreatePartition / WHvDeletePartition /
   WHvMapGpaRange concurrently -- cascade of 20 event-viewer entries and
   bug checks 0xD1, 0x13A, 0x50.  Named mutex ensures one-at-a-time. */
static HANDLE whp_mutex;
static void whp_lock(void) {
    if (!whp_mutex) whp_mutex = CreateMutexA(NULL, FALSE, "Global\\CodexVmWhpMutex");
    if (!whp_mutex) { fprintf(stderr, "FATAL: CreateMutex failed (%lu)\n", GetLastError()); exit(99); }
    DWORD r = WaitForSingleObject(whp_mutex, INFINITE);
    if (r == WAIT_FAILED) { fprintf(stderr, "FATAL: WHP mutex wait failed (%lu)\n", GetLastError()); exit(99); }
}
static void whp_unlock(void) {
    if (whp_mutex) ReleaseMutex(whp_mutex);
}
static void cleanup_whp(void) {
    if (!partition) return;
    whp_lock();
    WHvDeleteVirtualProcessor(partition, 0);
    WHvDeletePartition(partition);
    partition = NULL;
    whp_unlock();
}

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
/* Pointer grab, Ctrl+Alt+G, the same chord QEMU uses.
   The guest's pointer is a RELATIVE boot mouse: the host sends
   clamp(host_position - guest_position) and the guest converges on it. Ungrabbed
   that works, but the host keeps its own cursor, so two pointers are on screen
   and they only line up once enough reports have closed the gap at 127 px a
   time. Grabbed, the host cursor is hidden and pinned to the middle of the
   client area and every move is measured from there, so the motion is unbounded
   and there is exactly one pointer to look at. */
static int mouse_grabbed = 0;
static int grab_warping = 0;   /* our own SetCursorPos raises WM_MOUSEMOVE too */
static volatile unsigned char pending_mouse[3] = {0};
static volatile int pending_mouse_valid = 0;
static volatile int pending_mouse_abs_x = 0, pending_mouse_abs_y = 0, pending_mouse_btn = 0;
/* Buttons pressed since the guest last read port 0xE1. A guest that polls
   slower than the user clicks would otherwise never see a press whose down
   and up both land between two polls: the level is back to 0 by the time it
   looks. The latch OR-accumulates presses and is consumed by the 0xE1 read. */
static volatile LONG pending_mouse_btn_latch = 0;
static volatile unsigned long long pending_kbd_scancode = 0;
static volatile int pending_kbd_valid = 0;

/* Forward declaration -- full definition + instance below serial/PIC/NE2K */
typedef struct IdeState_ {
    unsigned char *data;
    size_t size;
    int sect_count, lba_lo, lba_mid, lba_hi, drive_head;
    int status, error;
    size_t buf_off;
    int buf_remaining;
    int sectors_left;
    int writing;            /* 1 during a WRITE SECTORS (0x30) transfer */
    int identing;           /* 1 during an IDENTIFY DEVICE (0xEC) transfer */
    const char *path;       /* disk image path, for write-back */
    int present;            /* 0 = no medium behind this position on the bus */
    FILE *wfp;              /* write-back handle, held open across sectors */
    int wfp_failed;         /* the open was tried and refused; do not retry per sector */
} IdeState;
static IdeState ide;
/* The primary channel's slave, from -disk2. Two positions is what the guest's
   own drive manager assumes: dm-enumerate-drives probes four (master and slave
   on each of two channels) and dm-exec-install partitions whichever one it was
   given. With a single image behind every position, "install to drive 1"
   reformatted drive 0 -- the disk the guest booted from -- and nothing in the
   guest could see it, because every position answered identically. The
   secondary channel is still undecoded, which is the honest answer: an IN on an
   unclaimed port returns 0xFF, the floating bus, and the guest's own detect
   reads that as no drive. */
static IdeState ide_slave;
/* Bit 4 of the last drive/head write on the primary channel. The register also
   carries LBA bits 27:24, so the write is recorded on BOTH devices and only the
   selection bit picks which one the following accesses reach. */
static int ide_sel = 0;
static IdeState *ide_active(void) { return ide_sel ? &ide_slave : &ide; }
static void ide_flush(IdeState *d, size_t off, size_t len);

/* ══ UEFI Emulation ══ */
#define UEFI_TABLE_PAGE   0xF0000   /* fake SystemTable + protocols */
#define UEFI_TRAP_PAGE    0xF1000   /* filled with HLT opcodes -- halt-based dispatch */
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
#define UEFI_TRAP_BOOT_LOCATEPROTO  45
#define UEFI_TRAP_BLK_RESET         50
#define UEFI_TRAP_BLK_READBLOCKS    51
#define UEFI_TRAP_BLK_WRITEBLOCKS   52
#define UEFI_TRAP_BLK_FLUSH         53
#define UEFI_TRAP_RT_GETTIME        60
#define UEFI_TRAP_SFS_OPENVOLUME    70
#define UEFI_TRAP_FILE_OPEN         71
#define UEFI_TRAP_FILE_CLOSE        72
#define UEFI_TRAP_FILE_READ         73
#define UEFI_TRAP_FILE_GETINFO      74
#define UEFI_TRAP_FILE_SETPOS       75

#define EFI_SUCCESS       0ULL
#define EFI_NOT_READY     0x8000000000000006ULL
#define EFI_NOT_FOUND_S   0x800000000000000EULL
#define EFI_UNSUPPORTED_S 0x8000000000000003ULL
#define EFI_INVALID_PARAM 0x8000000000000002ULL
#define EFI_DEVICE_ERROR_S 0x8000000000000007ULL
#define UEFI_MAP_KEY      0x1234ULL

/* UEFI protocol GUIDs (in-memory layout: Data1 LE32, Data2 LE16, Data3 LE16, Data4 raw) */
static const unsigned char GUID_BLOCK_IO[16]     = {0x21,0x5B,0x4E,0x96, 0x59,0x64, 0xD2,0x11, 0x8E,0x39,0x00,0xA0,0xC9,0x69,0x72,0x3B};
static const unsigned char GUID_SFS[16]          = {0x22,0x5B,0x4E,0x96, 0x59,0x64, 0xD2,0x11, 0x8E,0x39,0x00,0xA0,0xC9,0x69,0x72,0x3B};
static const unsigned char GUID_LOADED_IMAGE[16] = {0xA1,0x31,0x1B,0x5B, 0x62,0x95, 0xD2,0x11, 0x8E,0x3F,0x00,0xA0,0xC9,0x69,0x72,0x3B};
static const unsigned char GUID_DEVICE_PATH[16]  = {0x91,0x6E,0x57,0x09, 0x3F,0x6D, 0xD2,0x11, 0x8E,0x39,0x00,0xA0,0xC9,0x69,0x72,0x3B};
static const unsigned char GUID_GOP[16]          = {0xDE,0xA9,0x42,0x90, 0xDC,0x23, 0x38,0x4A, 0x96,0xFB,0x7A,0xDE,0xD0,0x80,0x51,0x6A};
/* EFI_SIMPLE_TEXT_INPUT_EX_PROTOCOL. The ConInEx block at UEFI_TABLE_PAGE+0x300
   and its ReadKeyStrokeEx have existed here for a long time and were
   unreachable: LocateProtocol answered NOT_FOUND for this GUID, so the
   compiler's uefi-read-key-ex builtin -- which builds exactly these 16 bytes on
   the stack and calls LocateProtocol with them -- could never succeed under
   this emulator. That made the one input path that calls firmware untestable,
   while uefi-read-key (a bare read of the PS/2 cell at key-buffer-addr) was
   testable and became the only path anything used. On a board with no PS/2
   port that path cannot ever deliver a key. */
static const unsigned char GUID_INPUT_EX[16]     = {0x34,0x75,0x9E,0xDD, 0x62,0x77, 0x98,0x46, 0x8C,0x14,0xF5,0x85,0x17,0xA6,0x25,0xAA};

/* ══ Intel PCH USB2 port routing ══
   On these parts the USB2 pins of a shared port belong to the companion EHCI
   controller until software moves them. Four PCI-config registers on the xHCI
   function do it: XUSB2PR (0xD0, which ports are routed to xHCI), XUSB2PRM
   (0xD4, which ports CAN be), USB3PSSEN (0xD8, SuperSpeed enable) and USB3PRM
   (0xDC, its mask). Only bits set in the mask are writable in the routing
   register, and a port left with the companion presents nothing whatever to
   the xHCI -- no connect status, no port power, no device.

   That last clause is why this is modelled. From inside the guest it is
   indistinguishable from a device that enumerated and then delivered nothing.
   Until now this model presented a NEC/Renesas part, so the driver's routing
   path short-circuited on the vendor check and no emulated run had ever
   executed it.

   -xhci-intel presents a Lynx Point PCH with the routable ports still on the
   companion, so the guest must route them before it sees anything.
   -xhci-intel-lock additionally makes XUSB2PR read-only, which is the arm
   that says no: a correct driver still finds nothing, and that is what shows
   the gate is what decides. */
static int xhci_intel = 0;
static int xhci_intel_lock = 0;
static int xhci_pci_slot = -1;
static int xhci_pci_slot2 = -1;   /* the second controller, -xhci-two */
static unsigned int xhci_xusb2pr = 0;
static unsigned int xhci_usb3pssen = 0;
/* Ports 1..3 are the USB2 ones this model presents (full-speed keyboard,
   high-speed camera, high-speed hub); port 0 is the SuperSpeed disk. */
#define XHCI_XUSB2PRM 0xEu
#define XHCI_USB3PRM  0x1u

/* ══ PCI Configuration Space ══ */
#define PCI_MAX_DEVICES 10
static unsigned int pci_config_addr = 0;
/* bar_size is the window the device actually decodes; bar_probe records that
   the guest wrote all-ones to size the BAR and has not yet written a base
   back. Sizing used to be destructive and a lie in the same breath: the
   all-ones write REPLACED the base with 0xFFFF0000, so a driver that sized a
   BAR lost the address it had just read, and every device claimed 64 KB
   whatever it really decoded. Probing is a mode now, not a mutation. */
static struct {
    unsigned short vendor, device;
    unsigned char class_code, subclass, progif, header_type;
    unsigned int bar[6];
    unsigned int bar_size[6];
    unsigned char bar_probe[6];
    unsigned char irq_line;
    unsigned short command;
    /* Which (bus, slot) this device answers at. Every historical device is
       bus 0 and its slot equals its array index, which is why bus went
       unmodelled until the PCI-to-PCI bridge below needed a second bus. */
    unsigned char bus, slot;
    unsigned char sec_bus, sub_bus;   /* header_type 1 only: the buses it forwards to */
} pci_devices[PCI_MAX_DEVICES];
static int pci_device_count = 0;

/* -nic-bme-clear: the NIC's Bus Master Enable bit cannot be set. The bed
   otherwise reads 0x0007 on every run once firmware or the driver has
   enabled the device, so a stage reading the command register has never
   seen the BME-clear case and cannot tell it from a device that was never
   enabled. With this the bit reads back 0 however often it is written, and
   the device does no DMA while it is clear -- a bus master that DMAs with
   BME clear would be a bed lying about its own state, which is the defect
   the device id had.

   The PCI command register's layout and the rule that a master must not
   initiate transactions with BME clear are standard PCI, and no PCI
   document is in this tree: shape right, nothing here cites it. */
static int e1000_bme_clear = 0;
static int e1000_pci_slot  = -1;
/* -dmar: publish a DMAR table so a stage can tell "an IOMMU is described"
   from "it is not". Header only; see the table's own comment. */
static int acpi_dmar = 0;

/* Locate the array index of the device answering config cycles for
   (bus, slot). Returns -1 if nothing is there, which config reads render as
   the all-ones vendor a bus walk uses to skip an empty slot. */
static int pci_find(int bus, int slot) {
    for (int i = 0; i < pci_device_count; i++)
        if (pci_devices[i].bus == bus && pci_devices[i].slot == slot) return i;
    return -1;
}

/* The xHCI register window follows the BAR the guest most recently wrote,
   not a compile-time constant. Until 2026-07-31 this dispatch used a fixed
   XHCI_BAR of 0xFE800000, which sits INSIDE GopXhci's usable window
   [0xC0000000, 0x100000000), so xhci-init-relocated was never once taken
   under this model and a relocation could not be observed, let alone a
   collision between two controllers relocating to the same address. */
static unsigned int xhci_bar_initial = 0xFE800000u;


static int pci_add_device(unsigned short vendor, unsigned short device,
    unsigned char cls, unsigned char sub, unsigned char progif,
    unsigned int bar0, unsigned char irq) {
    if (pci_device_count >= PCI_MAX_DEVICES) return -1;
    int i = pci_device_count++;
    memset(&pci_devices[i], 0, sizeof(pci_devices[0]));
    pci_devices[i].vendor = vendor;
    pci_devices[i].device = device;
    pci_devices[i].class_code = cls;
    pci_devices[i].subclass = sub;
    pci_devices[i].progif = progif;
    pci_devices[i].header_type = 0;
    pci_devices[i].bar[0] = bar0;
    /* Default decode window. xHCI overrides this with its real register
       space right after registration; a BAR that decodes nothing keeps 0
       and sizes as absent rather than claiming 64 KB it does not have. */
    pci_devices[i].bar_size[0] = bar0 ? 0x10000 : 0;
    pci_devices[i].irq_line = irq;
    pci_devices[i].command = 0x0003; /* IO + MMIO enabled */
    pci_devices[i].bus = 0;
    pci_devices[i].slot = (unsigned char)i;
    return i;
}

static unsigned int pci_read_config(int bus, int slot, int func, int offset) {
    if (func != 0) return 0xFFFFFFFF;
    int dev = pci_find(bus, slot);
    if (dev < 0) return 0xFFFFFFFF;
    /* A PCI-to-PCI bridge (header type 1) lays out config differently from an
       endpoint: offset 0x18 is primary/secondary/subordinate bus numbers, not
       a BAR, and that register is the whole reason the bridge exists here --
       pci-scan-all reads its secondary bus to know which bus to descend to. */
    if (pci_devices[dev].header_type == 1) {
        switch (offset & 0xFC) {
        case 0x00: return pci_devices[dev].vendor | ((unsigned int)pci_devices[dev].device << 16);
        case 0x04: return pci_devices[dev].command;
        case 0x08: return ((unsigned int)pci_devices[dev].class_code << 24) |
                          ((unsigned int)pci_devices[dev].subclass << 16) |
                          ((unsigned int)pci_devices[dev].progif << 8);
        case 0x0C: return (unsigned int)pci_devices[dev].header_type << 16;
        case 0x18: return (unsigned int)pci_devices[dev].bus |
                          ((unsigned int)pci_devices[dev].sec_bus << 8) |
                          ((unsigned int)pci_devices[dev].sub_bus << 16);
        default: return 0;
        }
    }
    if (xhci_intel && dev == xhci_pci_slot) {
        switch (offset & 0xFC) {
        case 0xD0: return xhci_xusb2pr;
        case 0xD4: return XHCI_XUSB2PRM;
        case 0xD8: return xhci_usb3pssen;
        case 0xDC: return XHCI_USB3PRM;
        }
    }
    switch (offset & 0xFC) {
    case 0x00: return pci_devices[dev].vendor | ((unsigned int)pci_devices[dev].device << 16);
    case 0x04: return pci_devices[dev].command;
    case 0x08: return ((unsigned int)pci_devices[dev].class_code << 24) |
                      ((unsigned int)pci_devices[dev].subclass << 16) |
                      ((unsigned int)pci_devices[dev].progif << 8);
    case 0x0C: return (unsigned int)pci_devices[dev].header_type << 16;
    case 0x10: case 0x14: case 0x18: case 0x1C: case 0x20: case 0x24: {
        int bi = ((offset & 0xFC) - 0x10) / 4;
        if (!pci_devices[dev].bar_probe[bi]) return pci_devices[dev].bar[bi];
        /* Sizing read: all-ones in the address field, zeros below the
           window, and the low type bits preserved -- that is how a driver
           computes the length (~mask + 1). A device with no BAR reads 0. */
        unsigned int size = pci_devices[dev].bar_size[bi];
        if (size == 0) return 0;
        return (~(size - 1)) | (pci_devices[dev].bar[bi] & 0x0F);
    }
    case 0x3C: return pci_devices[dev].irq_line;
    default: return 0;
    }
}

static void pci_write_config(int bus, int slot, int func, int offset, unsigned int val) {
    if (func != 0) return;
    int dev = pci_find(bus, slot);
    if (dev < 0) return;
    if (pci_devices[dev].header_type == 1) {
        if ((offset & 0xFC) == 0x04) pci_devices[dev].command = (unsigned short)(val & 0xFFFF);
        /* The bus-number register is fixed by the model; a guest that
           re-numbers buses is not something this bed reproduces, and silently
           accepting the write would let a driver believe it had. Ignore it. */
        return;
    }
    if (xhci_intel && dev == xhci_pci_slot) {
        /* Unroutable bits are hardwired, so a mask read back from XUSB2PRM is
           the only value that moves everything movable. */
        if ((offset & 0xFC) == 0xD0) {
            unsigned int was = xhci_xusb2pr;
            if (!xhci_intel_lock) xhci_xusb2pr = val & XHCI_XUSB2PRM;
            fprintf(stderr, "xHCI: XUSB2PR write 0x%X -> routed 0x%X (was 0x%X, mask 0x%X)%s\n",
                    val, xhci_xusb2pr, was, XHCI_XUSB2PRM,
                    xhci_intel_lock ? " [locked: ignored]" : "");
            return;
        }
        if ((offset & 0xFC) == 0xD8) { xhci_usb3pssen = val & XHCI_USB3PRM; return; }
    }
    if ((offset & 0xFC) == 0x04) {
        unsigned short cmd = (unsigned short)(val & 0xFFFF);
        if (e1000_bme_clear && dev == e1000_pci_slot) cmd &= (unsigned short)~0x0004;
        pci_devices[dev].command = cmd;
    }
    else if ((offset & 0xFC) >= 0x10 && (offset & 0xFC) <= 0x24) {
        int bar_idx = ((offset & 0xFC) - 0x10) / 4;
        if ((dev == xhci_pci_slot || dev == xhci_pci_slot2) && bar_idx == 0 && val != 0xFFFFFFFF)
            fprintf(stderr, "xHCI ord%d: BAR0 write 0x%08X (was 0x%08X)\n", dev == xhci_pci_slot ? 0 : 1, val, pci_devices[dev].bar[0]);
        if (val == 0xFFFFFFFF) {
            pci_devices[dev].bar_probe[bar_idx] = 1;  /* size query; base kept */
        } else {
            pci_devices[dev].bar_probe[bar_idx] = 0;
            pci_devices[dev].bar[bar_idx] = val;
        }
    }
}

/* ══ Bochs VBE Display ══ */
#define VBE_INDEX_PORT 0x01CE
#define VBE_DATA_PORT  0x01CF
#define VBE_FB_ADDR    0xFD000000ULL
static unsigned short vbe_index = 0;
static unsigned short vbe_regs[16] = {0}; /* VBE_DISPI registers */
static int vbe_active = 0;

/* ══ PC Speaker ══ */
static int speaker_enabled = 0;
static unsigned int speaker_freq = 0;
static int speaker_freq_latch = 0; /* 0=expecting low byte, 1=expecting high byte */
static unsigned char pit_mode[3] = {0}; /* mode register per channel */
static unsigned char pit_access[3] = {0}; /* access mode per channel */

/* The programmed reload divisor per channel, and the half-written state
   of a two-byte load. These were missing entirely: channel-data writes
   were discarded and every counter read answered 0, so a guest could
   program a frequency and then watch a counter that never moved. A
   divisor of 0 means "not yet programmed"; the hardware reads 0 as
   65536, which is what pit_divisor() returns for it. */
static unsigned short pit_reload[3] = {0};
static unsigned char pit_load_hi[3] = {0};  /* 1 = low byte seen, high byte next */
static unsigned char pit_read_hi[3] = {0};  /* same latch on the read side */
/* The counter-latch command (0x43 with an access field of 00) freezes a
   channel's count so the two halves a guest reads belong to the same
   instant. Without it a 16-bit read straddles a decrement and can produce
   a value the counter never held -- which is the whole reason the command
   exists, and it was not modelled at all. */
static unsigned short pit_latched[3] = {0};
static unsigned char pit_latch_valid[3] = {0};
#define PIT_HZ 1193182.0

static unsigned int pit_divisor(int ch) {
    return pit_reload[ch] ? (unsigned int)pit_reload[ch] : 65536u;
}

static double now_ms_for_timer(void);   /* defined with the LAPIC timer */

/* Where the counter stands right now. Mode 3, the square-wave generator,
   decrements by two and reloads at half the divisor -- so it never shows an
   odd count, and modelling it as a plain countdown reports values that
   channel could not produce. Every other mode counts down by one. */
static unsigned int pit_current_count(int ch) {
    unsigned int div = pit_divisor(ch);
    unsigned long long elapsed =
        (unsigned long long)(now_ms_for_timer() * PIT_HZ / 1000.0);
    if (pit_mode[ch] == 3) {
        unsigned int half = div / 2;
        if (half == 0) return 0;
        return div - 2u * (unsigned int)(elapsed % half);
    }
    return div - (unsigned int)(elapsed % div);
}

/* ══ xHCI Controller Emulation ══ */
#define XHCI_BAR       0xFE800000ULL
#define XHCI_BAR_SIZE  0x4000       /* 16 KB register space */
#define XHCI_CAP_LEN   32           /* capability registers: 32 bytes */
/* Extended capabilities live at BAR+0x80. HCCPARAMS carries the offset in
   DWORDS, which is why this is 0x20 and the byte offset is 0x80. */
#define XHCI_XECP_DWORDS 0x20
#define XHCI_XECP_OFF    (XHCI_XECP_DWORDS * 4)
#define XHCI_MAX_SLOTS 32
/* Array bound, not the reported count. The four modelled devices live on
   ports 1-4; everything above them is a powered empty port and exists so
   the number of ROOT PORTS can be varied.

   That number is not cosmetic. Real controllers are wide -- the ASUS PCH
   reports 26 -- and a guest that indexes a fixed-size table by port
   number overruns whatever sits above it. GopXhci's diagnostic snapshot
   did exactly that, laying PORTSC over the handback flag that gates the
   keyboard pump, and no bed could show it: this model reported FOUR ports
   and QEMU reports EIGHT, which is precisely the last cell the snapshot
   owns. Both beds sat on or under the boundary. */
#define XHCI_MAX_PORTS 32
#define XHCI_MODELLED_PORTS 4
#define XHCI_HUB_TIERS 2

struct xhci_state {
    unsigned int usbcmd;
    unsigned int usbsts;
    unsigned int dnctrl;
    unsigned long long crcr;
    /* Exact command-ring walk position + consumer cycle state. CRCR's
       address field is bits 63:6, so parking the walk position in crcr
       and reloading it with & ~0x3F rounded DOWN to a 64-byte boundary --
       replaying up to three consumed command TRBs per doorbell. Harmless
       while ENABLE_SLOT was stateless; slot allocation made every replay
       mint a phantom slot. */
    unsigned long long cr_pos;
    int cr_ccs;
    unsigned long long dcbaap;
    unsigned int config;
    unsigned int portsc[XHCI_MAX_PORTS];
    /* Event ring */
    unsigned long long erstba;   /* event ring segment table base */
    unsigned short erdp_idx;     /* current event ring dequeue index */
    unsigned long long er_addr;  /* event ring base (from ERST entry 0) */
    unsigned short er_size;      /* event ring size (from ERST entry 0) */
    int er_ccs;                  /* consumer cycle state */
    unsigned long long guest_erdp; /* last ERDP the guest wrote back -- the
                                      consumer's position, which is what makes
                                      ring-full REAL: a producer that laps an
                                      unacknowledged consumer is corruption no
                                      hardware commits */
    int er_dropped;              /* events dropped on a full ring (reported once) */
    /* Root port (1-based) each slot was addressed against, latched from the
       ADDRESS_DEVICE input context. Device personality (storage / HID kbd /
       UVC) keys off the PORT, as on real hardware -- not the slot number,
       which is just allocation order. */
    int slot_port[XHCI_MAX_SLOTS + 1];
    /* The rest of the slot context's identity fields, latched from the same
       ADDRESS_DEVICE input context. Root port alone stops being enough the
       moment a hub is on the bus: two devices then share a root port and are
       told apart only by the route string. Speed and the transaction
       translator are latched for the periodic-schedule check further down --
       a full-speed device behind a high-speed hub is exactly the case that
       needs a TT, and a model that does not know the speed cannot notice. */
    int slot_route[XHCI_MAX_SLOTS + 1];
    int slot_speed[XHCI_MAX_SLOTS + 1];
    int slot_tt_hub[XHCI_MAX_SLOTS + 1];
    int slot_tt_port[XHCI_MAX_SLOTS + 1];
    /* Downstream port state of the modelled hubs on root port 4, one entry
       per tier. One downstream port each, because one is all it takes: tier
       0 is the high-speed hub that owns the transaction translator, and
       tier 1 (present only under -xhci-hub-tiers 2) is a full-speed hub
       that owns none and must pass tier 0's along to what hangs below it. */
    int hub_powered[XHCI_HUB_TIERS];
    int hub_enabled[XHCI_HUB_TIERS];
    int hub_c_reset[XHCI_HUB_TIERS];
    int hub_c_connection[XHCI_HUB_TIERS];
    /* USB Legacy Support ownership. Firmware owns the controller at reset;
       a driver claims it by setting the OS-owned bit and waiting for the
       BIOS-owned bit to clear. Modelling both halves is what makes the
       handoff observable instead of a constant. */
    int legacy_bios_owned;
    int legacy_os_owned;
    /* One latch per slot and DCI for the periodic parameter report below, so
       it says its piece on the first transfer and not on every one. */
    unsigned char periodic_reported[XHCI_MAX_SLOTS + 1][32];
    int next_slot;
    int scratch_checked;   /* -xhci-scratch: verdict printed once */
    int scratch_bad;       /* -xhci-scratch: array judged unusable */
};

/* TWO controllers. The ASUS carries an Intel PCH xHCI and an ASMedia one
   (its own ids are 8086:a12f and 1b21:1242; this model presents 1033:0194
   or, under -xhci-intel, 8086:8C31 as ordinal 0, and 1b21:1242 as ordinal
   1), and xhci-reloc-base is a
   single constant with no per-controller term, so if both relocate the
   second lands on top of the first. One controller cannot express that. */
static struct xhci_state xhci_ctl[2];
static struct xhci_state *xcur = &xhci_ctl[0];
static int xhci_two = 0;
/* IN THE MMIO HOLE, not in RAM. This was 0x91000000, which is 2.27 GB and
   sits inside the guest's own arena: the diag PCI stage answers BELOW3G for
   it and says in as many words "a device window sits inside our RAM arena
   ... do not expect the network or USB rows to be right". So every reading
   taken from the second controller under the default was one the machine had
   already declared unreliable, and nothing noticed because no arm had ever
   run this flag (measured 2026-08-21: zero uses of -xhci-two, -xhci-no-disk
   or -xhci-bar2 anywhere). 0xFE900000 is clear of the VGA at 0xFD000000, the
   HDA at 0xFE000000, the e1000 at 0xFE400000 and the first xHCI at
   0xFE800000, whose window is 16 KB.

   -xhci-bar2 still places it anywhere, which is how the collision above is
   reproduced deliberately rather than by default. */
static unsigned int xhci_bar2_initial = 0xFE900000u;

static unsigned long long xhci_decode_base_slot(int slot) {
    if (slot < 0) return 0;
    if (!(pci_devices[slot].command & 0x2)) return 0;   /* memory decode off */
    return (unsigned long long)(pci_devices[slot].bar[0] & 0xFFFFFFF0u);
}

/* Which controller answers this address. Both can decode the same window
   once both have relocated to the one xhci-reloc-base; real hardware gives
   no guarantee there, and this model lets the HIGHER ordinal answer, which
   is the case the hypothesis names: the keyboard's saved pointers then
   address the ASMedia rather than the Intel. */
static int xhci_sel_for(unsigned long long gpa) {
    int sel = -1;
    unsigned long long b0 = xhci_decode_base_slot(xhci_pci_slot);
    unsigned long long b1 = xhci_two ? xhci_decode_base_slot(xhci_pci_slot2) : 0;
    if (b0 && gpa >= b0 && gpa < b0 + XHCI_BAR_SIZE) sel = 0;
    if (b1 && gpa >= b1 && gpa < b1 + XHCI_BAR_SIZE) sel = 1;
    return sel;
}

/* -xhci-no-root-kbd: unplug the root-port HID keyboard, leaving the hub as
   the only route to one. The bus walk takes the first keyboard it finds and
   the root port comes first, so this is what lets a test drive the hub path
   with an unmodified guest binary -- the topology is the variable. */
static int xhci_no_root_kbd = 0;
/* -hid-nak: the HID keyboard never delivers -- every interrupt IN is NAKed,
   the pending TD stays in progress forever (USB2 8.5.6.4: NAK does not
   consume error budget, the xHC retries every ESIT per xHCI 4.14.3.1, the
   endpoint never halts). This is the ASUS flight signature measured
   2026-08-03: EPINT=0, dq parked, est=1, and Stop Endpoint answering with
   FSE code 26 (Stopped, TD in progress) -- the arm exists to reproduce
   that machine on the desk. */
static int xhci_hid_nak = 0;
/* -hid-idle-quirk: after the guest sends SET_IDLE with duration 0, the
   keyboard NAKs every interrupt IN until further notice -- the quirk class
   where "report only on change" is honored as "never report". HID 1.11
   F.3 requires a boot keyboard to report on EVERY poll by default and
   7.2.4 lets SET_IDLE silence it; a quirky device over-honors the
   silence. The arm exists to test the driver WITHOUT the request against
   a device that punishes the request. */
static int hid_idle_quirk = 0;
static int hid_idle_zeroed = 0;
/* -hid-root-silent: the root-port keyboard completes every interrupt IN
   with SUCCESS and eight zero bytes, and answers GET_REPORT with zeros,
   while the hub-attached keyboard carries the injected keys. This is the
   state the ASUS TUF measured on 2026-08-04 -- a Transfer Event, SUCCESS,
   on the driver's own endpoint, buffer all zeros with a key held -- which
   no arm could produce: -hid-nak models the OPPOSITE (no event at all).
   A keyboard-shaped interface that answers everything and never carries a
   key is what a wireless dongle with no paired keyboard, a composite
   device's boot interface, or a vendor HID node all look like from the
   bus, and the fix under test is binding EVERY boot keyboard rather than
   the first. */
static int xhci_hid_root_silent = 0;
/* -hid-keys: scripted keys (-keyt / -keys-file) feed ONLY the USB HID
   held-key set -- no PS/2 queue, no host-side write into the 28680 key
   cell. Without this, injected keys reach the guest through the PS/2
   emulation whatever the USB stack does, so no bed could ever prove a
   scancode travelled the interrupt-IN DMA path end to end. The ASUS has
   no PS/2 controller at all; this flag is that machine. */
static int hid_keys_only = 0;
/* Tracked HID device state, answered by the class GETs (HID 1.11 7.2).
   Protocol defaults to 1 (report) per 7.2.6; idle default 0 here rather
   than the recommended 500 ms so a guest reading GET_IDLE sees exactly
   what it (or nobody) set. */
static int hid_idle_rate = 0;
static int hid_protocol = 1;
static int hid_configuration = 0;

/* -xhci-no-disk: unplug the SuperSpeed mass storage on root port 1.

   THE RATIONALE THIS COMMENT CARRIED IS FALSE, measured 2026-08-21 on the
   diag image. It said the walk stops at controller 0 with a disk present, so
   the flag was needed to reach the second one at all. It is not: usb-found-all
   wants a keyboard AND a mouse AND a disk, and this bed HAS NO MOUSE MODEL, so
   the walk never short-circuits whatever else is found. Under -xhci-two both
   controllers come up running with the disk present and without it alike.

   The flag still does what its first sentence says and is still worth having.
   What no bed here can currently express is the walk STOPPING early, because
   that needs a mouse. */
static int xhci_no_disk = 0;
/* The bConfigurationValue the mass-storage model reports and REQUIRES.
   -usb-cfgval N. Default 1 keeps every existing test unchanged. */
static int usb_cfgval = 1;
/* Completion code the mass-storage model answers SET_CONFIGURATION with,
   regardless of the value sent. -usb-setcfg-fault N. 0 leaves it alone.
   6 is STALL, 4 is USB Transaction Error. This injects a SYMPTOM, not a
   cause: it is for developing and proving the host's handling of a
   refusal, and it can never tell you why a real device refused. */
static int usb_setcfg_fault = 0;
/* As above, but the fault applies to the FIRST SET_CONFIGURATION only and
   then clears. -usb-setcfg-fault-once N. A transient failure and a
   permanent one want opposite fixes in the host, and a bed that can only
   produce the permanent kind cannot show that a reading distinguishes
   them. This is the arm that makes "would a retry have worked?" a
   question the instrument can answer both ways. */
static int usb_setcfg_fault_once = 0;
/* Root ports the controller REPORTS in HCSPARAMS1. -xhci-ports N.
   Defaults to the four that carry devices, so every existing test is
   unchanged; raising it adds empty powered ports above them. */
static int xhci_num_ports = XHCI_MODELLED_PORTS;
/* Root port the mass-storage device sits on. -usb-disk-port N.

   It is 1 by default and every existing test depends on that. It exists
   because NO BED COULD PUT A CONNECTED DEVICE ABOVE ROOT PORT 7 -- this
   model had four ports, and QEMU refuses attachment above its eighth
   whatever HCSPARAMS1 claims. The ASUS answered with the boot stick on
   port 9, where the probe's eight PORTSC rows could not reach it and a
   count of connected ports named none of them. A device the reader cannot
   locate is the failure this flag exists to make reproducible. */
static int usb_disk_port = 1;
/* Present the power-on UNIT ATTENTION every conforming SCSI target
   presents. ON by default, because a target that does NOT do this is the
   unusual one and a model that only succeeds is not a test.
   -usb-no-unit-attention restores the old always-ready behaviour. */
static int usb_unit_attention = 1;

/* -xhci-hub-tiers N: how many hubs to stack on root port 4. One is a
   high-speed hub with a full-speed keyboard below it. Two puts a
   full-speed hub in between, which is the topology that tells apart a
   driver reading the translator off the immediate parent from one
   carrying the nearest high-speed ancestor's down the walk. A laptop
   with a monitor hub in front of a keyboard hub is this shape. */
static int xhci_hub_tiers = 1;

/* -xhci-calibrate-periodic: skew the EXPECTED Interval and Max ESIT Payload
   by one, so a correct driver is reported as wrong. The value checks below
   are the arms that make this bed able to say a driver is wrong rather than
   merely present; a check that has never said no is worth what no check is
   worth (L-FALSIF), and this flag is how the no gets run. */
static int xhci_calibrate_periodic = 0;

/* -xhci-psi: declare Protocol Speed ID dwords and report NON-DEFAULT Port
   Speed values in PORTSC.

   xHCI 1.2 Table 7-13's speed IDs (1 full, 2 low, 3 high, 4 super) are
   DEFAULTS, and 7.2.2.1.1 conditions them: they "shall be presented in the
   PORTSC Port Speed field only if no PSI Dwords are defined (PSIC = '0')". A
   controller that declares its own PSI dwords may report full speed as any
   value it defined, and a driver must read the Supported Protocol capability
   to learn the mapping rather than assume the table.

   No previous bed could present that, because the model agreed with the
   assumption: every arm reported 1 for full speed, so a driver that hardcoded
   the defaults and one that read the capability were indistinguishable. Under
   this flag they are not, and three things move at once for a driver that
   assumed -- endpoint zero's max packet size, which branch of the Interval
   encoding applies, and whether a device behind a hub is given a transaction
   translator at all. */
static int xhci_psi = 0;
#define XHCI_PSI_FULL  5
#define XHCI_PSI_LOW   6
#define XHCI_PSI_HIGH  7
#define XHCI_PSI_SUPER 8

/* -xhci-csz: advertise CSZ=1 (HCCPARAMS1 bit 2) and hold every context to
   the 64-byte stride that bit demands, the way Intel PCH silicon does. Every
   bed before this one reported CSZ=0, so the driver's 64-byte context path
   was compiled into every image and executed nowhere except the ASUS -- the
   exact machine where the interrupt endpoint is silent. Under this flag the
   input control context is still one entry at +0, the slot context moves to
   +64, DCI n to +(n+1)*64 on the input side and +n*64 in the device context.
   A driver that hardcodes 32 writes its slot context inside the input
   control context and its ring pointers where the controller will not look:
   ADDRESS_DEVICE latches a garbage port, the doorbell reads a zero dequeue
   pointer, and the existing schedule checks print why nothing moves. */
static int xhci_csz64 = 0;
#define XHCI_CTXSZ (xhci_csz64 ? 64 : 32)

/* -xhci-scratch <N>: declare N scratchpad buffers in HCSPARAMS2 (hi 25:21,
   lo 31:27) and REFUSE the first ENABLE_SLOT unless DCBAA[0] points at an
   array of N page-aligned, non-zero, in-RAM page pointers. QEMU and this
   model declared zero forever, so the driver's scratchpad path has never run
   in any bed; Intel parts demand real pages and DMA into them, and a missing
   or short array is silent corruption on metal. Refusal with completion code
   9 plus a stderr verdict is the observable stand-in for that corruption. */
static int xhci_scratch_bufs = 0;

/* -uefi-conout-remode: the first ConOut ClearScreen switches the GOP to
   1024x768/1024, modelling AMI Aptio V's GraphicsConsole activation. See the
   ClearScreen trap for the full account. */
static int uefi_conout_remode = 0;
/* Firmware tables a diagnostic reads passively. Present by default because
   every real board carries them; the -no-* switches are the arms that show a
   reader can say "none offered", and -edid-bad breaks the EDID checksum so
   the reader can be seen to refuse a corrupt block. */
static int uefi_mode;              /* defined with the UEFI state below; smbios_setup_tables reads it */
static int uefi_no_smbios = 0;
static int uefi_no_edid = 0;
static int uefi_edid_bad = 0;
static const unsigned char GUID_SMBIOS3[16] = {0x44,0x15,0xFD,0xF2, 0x94,0x97, 0x2C,0x4A, 0x99,0x2E,0xE5,0xBB,0xCF,0x20,0xE3,0x94};
static const unsigned char GUID_EDID_ACTIVE[16] = {0x56,0x10,0x8C,0xBD, 0x36,0x9F, 0xEC,0x44, 0x92,0xA8,0xA6,0x33,0x7F,0x81,0x79,0x86};
static const unsigned char GUID_EDID_DISCOVERED[16] = {0xF6,0x34,0x0C,0x1C, 0x80,0xD3, 0xFA,0x41, 0xA0,0x49,0x8A,0xD0,0x6C,0x1A,0x66,0xAA};

/* -xhci-evt-flood <N>: post N extra Port Status Change events at the Run
   transition. 64-TRB event ring: N >= 64 forces the producer through the
   wrap with the consumer far behind, and past it into the full condition --
   the two paths a 4-port bed can never reach and a 26-port Intel reaches
   before enumeration begins. */
static int xhci_evt_flood = 0;

/* The speed class actually on each root port, kept apart from the ID used to
   report it. Without the separation the model cannot tell a driver that read
   the mapping from one that guessed it, because both answers are the same
   number. */
static int xhci_port_true_speed[XHCI_MAX_PORTS];

/* Table 7-13 by default; the declared values when PSI dwords exist. */
static int xhci_speed_id(int true_speed) {
    if (!xhci_psi) return true_speed;
    switch (true_speed) {
    case 1: return XHCI_PSI_FULL;
    case 2: return XHCI_PSI_LOW;
    case 3: return XHCI_PSI_HIGH;
    case 4: return XHCI_PSI_SUPER;
    default: return true_speed;
    }
}

/* BOT (Bulk-Only Transport) state for the slot-1 mass-storage device.
   The CBW and any write data arrive on the bulk OUT ring (even DCI);
   read data and the CSW are served on the bulk IN ring (odd DCI).
   One command is in flight at a time, per the BOT spec. */
static struct {
    int active;              /* CBW latched, CSW not yet delivered */
    unsigned int tag;
    unsigned int xfer_len;
    int dir_in;
    unsigned char cb[16];
    unsigned int data_done;  /* bytes of the data phase consumed so far */
    int csw_status;          /* 0 = good, 1 = failed */
    /* Pending sense, as a real target keeps it. A SCSI device answers the
       FIRST command after a power-on or reset with CHECK CONDITION and
       sense key UNIT ATTENTION, and KEEPS answering it until the host
       reads the sense data. REQUEST SENSE is the one command the
       condition does not apply to, and reading it clears the condition.

       This model had none of it: csw_status was zeroed per CBW and
       REQUEST_SENSE returned eighteen zeros with no key, so every
       recognised command succeeded and a host that skipped the handshake
       passed here and failed on every conforming target. reek recorded
       that on 2026-07-29 and it stayed open until 2026-08-03. It is the
       rung-4-to-5 path of the MSC ladder -- the next rung the board will
       reach -- and nothing had ever executed it. */
    int sense_key;           /* 0 = none pending */
    int sense_asc;
    int sense_ascq;
} bot;

/* USB Mass Storage device descriptor (18 bytes) */
static const unsigned char usb_dev_desc[] = {
    18, 1,  /* bLength, bDescriptorType=DEVICE */
    0x00, 0x03, /* bcdUSB = 3.00 */
    0, 0, 0,    /* class/sub/proto = per-interface */
    9,          /* bMaxPacketSize0 = 512 (encoded as 2^9 for USB3) */
    0x81, 0x07, /* idVendor = 0x0781 (SanDisk) */
    0x56, 0x55, /* idProduct = 0x5556 */
    0x00, 0x01, /* bcdDevice = 1.00 */
    1, 2, 0,    /* iManufacturer, iProduct, iSerial */
    1           /* bNumConfigurations */
};

/* Config + Interface + 2 Bulk Endpoints (32 bytes total).

   NOT const: -usb-cfgval patches byte 5. bConfigurationValue is the value
   SET_CONFIGURATION must carry and it is NOT an index -- a device is free
   to number its single configuration anything non-zero. Every emulated
   storage device in reach reported 1 AND accepted any value sent, so a
   host that sent a hardcoded 1 passed on every bed and was refused on real
   hardware. Enforced at the SETUP stage below, because reporting a value
   the model does not then require is the stub-that-agrees-with-itself
   shape this catalog exists to refuse. */
static unsigned char usb_cfg_desc[] = {
    /* Config descriptor */
    9, 2,       /* bLength, bDescriptorType=CONFIG */
    32, 0,      /* wTotalLength = 32 */
    1,          /* bNumInterfaces */
    1,          /* bConfigurationValue -- patched by -usb-cfgval */
    0,          /* iConfiguration */
    0x80,       /* bmAttributes: bus-powered */
    50,         /* bMaxPower: 100mA */
    /* Interface descriptor */
    9, 4,       /* bLength, bDescriptorType=INTERFACE */
    0,          /* bInterfaceNumber */
    0,          /* bAlternateSetting */
    2,          /* bNumEndpoints */
    8,          /* bInterfaceClass = Mass Storage */
    6,          /* bInterfaceSubClass = SCSI */
    80,         /* bInterfaceProtocol = BBB (Bulk-Only) */
    0,          /* iInterface */
    /* Bulk IN endpoint */
    7, 5,       /* bLength, bDescriptorType=ENDPOINT */
    0x81,       /* bEndpointAddress = EP1 IN */
    2,          /* bmAttributes = Bulk */
    0x00, 0x04, /* wMaxPacketSize = 1024 */
    0,          /* bInterval */
    /* Bulk OUT endpoint */
    7, 5,       /* bLength, bDescriptorType=ENDPOINT */
    0x02,       /* bEndpointAddress = EP2 OUT */
    2,          /* bmAttributes = Bulk */
    0x00, 0x04, /* wMaxPacketSize = 1024 */
    0           /* bInterval */
};

/* USB HID Keyboard device descriptor (18 bytes) */
static const unsigned char usb_hid_dev_desc[] = {
    18, 1,      /* bLength, bDescriptorType=DEVICE */
    0x10, 0x02, /* bcdUSB = 2.10 */
    0, 0, 0,    /* class/sub/proto = per-interface */
    64,         /* bMaxPacketSize0 */
    0x3D, 0x04, /* idVendor = 0x043D (Lexmark -- generic) */
    0x01, 0x00, /* idProduct = 0x0001 */
    0x00, 0x01, /* bcdDevice = 1.00 */
    0, 0, 0,    /* iManufacturer, iProduct, iSerial */
    1           /* bNumConfigurations */
};

/* HID keyboard config: config(9) + interface(9) + HID(9) + endpoint(7) = 34 bytes */
static const unsigned char usb_hid_cfg_desc[] = {
    9, 2,       /* bLength, bDescriptorType=CONFIG */
    34, 0,      /* wTotalLength = 34 */
    1,          /* bNumInterfaces */
    1,          /* bConfigurationValue */
    0, 0x80, 50,/* iConfiguration, bmAttributes, bMaxPower */
    /* Interface */
    9, 4,       /* bLength, bDescriptorType=INTERFACE */
    0, 0,       /* bInterfaceNumber, bAlternateSetting */
    1,          /* bNumEndpoints */
    3,          /* bInterfaceClass = HID */
    1,          /* bInterfaceSubClass = Boot Interface */
    1,          /* bInterfaceProtocol = Keyboard */
    0,          /* iInterface */
    /* HID descriptor */
    9, 0x21,    /* bLength, bDescriptorType=HID */
    0x11, 0x01, /* bcdHID = 1.11 */
    0,          /* bCountryCode */
    1,          /* bNumDescriptors */
    0x22,       /* bDescriptorType = Report */
    63, 0,      /* wDescriptorLength = 63 (boot keyboard report) */
    /* Interrupt IN endpoint */
    7, 5,       /* bLength, bDescriptorType=ENDPOINT */
    0x81,       /* bEndpointAddress = EP1 IN */
    3,          /* bmAttributes = Interrupt */
    8, 0,       /* wMaxPacketSize = 8 */
    10          /* bInterval = 10ms */
};

/* -hid-combo: the HID device carries TWO interfaces -- boot keyboard on
   EP1 IN, boot mouse on EP2 IN -- the wireless-dongle topology, and the
   shape the ASUS answered on 2026-08-04 (four keyboard-shaped interfaces,
   keys on the fourth). A whole-device classifier hands the combo to the
   keyboard driver and the mouse half is unreachable; the per-interface
   walk binds both, with SET_CONFIGURATION sent exactly once. */
static int hid_combo = 0;
static const unsigned char usb_hid_combo_cfg_desc[] = {
    9, 2,       /* bLength, bDescriptorType=CONFIG */
    59, 0,      /* wTotalLength = 59 */
    2,          /* bNumInterfaces */
    1,          /* bConfigurationValue */
    0, 0x80, 50,/* iConfiguration, bmAttributes, bMaxPower */
    /* Interface 0: boot keyboard */
    9, 4, 0, 0, 1, 3, 1, 1, 0,
    9, 0x21, 0x11, 0x01, 0, 1, 0x22, 63, 0,
    7, 5, 0x81, 3, 8, 0, 10,   /* EP1 IN, interrupt, mps 8 */
    /* Interface 1: boot mouse */
    9, 4, 1, 0, 1, 3, 1, 2, 0,
    9, 0x21, 0x11, 0x01, 0, 1, 0x22, 50, 0,
    7, 5, 0x82, 3, 4, 0, 10    /* EP2 IN, interrupt, mps 4 */
};

/* USB 2.0 high-speed hub, single transaction translator.
   bDeviceProtocol = 1 is single-TT; that is the field a host controller
   driver reads to decide whether devices below this hub need TT fields
   filled in their slot contexts. */
static const unsigned char usb_hub_dev_desc[] = {
    18, 1,      /* bLength, bDescriptorType=DEVICE */
    0x00, 0x02, /* bcdUSB = 2.00 */
    9,          /* bDeviceClass = HUB */
    0,          /* bDeviceSubClass */
    1,          /* bDeviceProtocol = single TT */
    64,         /* bMaxPacketSize0 */
    0x09, 0x05, /* idVendor */
    0x00, 0x20, /* idProduct */
    0x00, 0x01, /* bcdDevice */
    0, 0, 0,    /* iManufacturer, iProduct, iSerialNumber */
    1           /* bNumConfigurations */
};

/* The same hub one tier down, at full speed. A full-speed hub has no
   transaction translator at all, which is why bDeviceProtocol is 0 here and
   why anything below it must be served by the high-speed hub above. */
static const unsigned char usb_hub_fs_dev_desc[] = {
    18, 1,      /* bLength, bDescriptorType=DEVICE */
    0x10, 0x01, /* bcdUSB = 1.10 */
    9,          /* bDeviceClass = HUB */
    0,          /* bDeviceSubClass */
    0,          /* bDeviceProtocol = full speed, no TT */
    64,         /* bMaxPacketSize0 */
    0x09, 0x05, /* idVendor */
    0x01, 0x20, /* idProduct */
    0x00, 0x01, /* bcdDevice */
    0, 0, 0,    /* iManufacturer, iProduct, iSerialNumber */
    1           /* bNumConfigurations */
};

static const unsigned char usb_hub_cfg_desc[] = {
    9, 2,       /* bLength, bDescriptorType=CONFIGURATION */
    25, 0,      /* wTotalLength = 9 + 9 + 7 */
    1,          /* bNumInterfaces */
    1,          /* bConfigurationValue */
    0,          /* iConfiguration */
    0xE0,       /* bmAttributes = self-powered */
    50,         /* bMaxPower */
    /* Hub interface */
    9, 4,       /* bLength, bDescriptorType=INTERFACE */
    0, 0,       /* bInterfaceNumber, bAlternateSetting */
    1,          /* bNumEndpoints */
    9,          /* bInterfaceClass = HUB */
    0, 0,       /* bInterfaceSubClass, bInterfaceProtocol */
    0,          /* iInterface */
    /* Status-change interrupt IN endpoint */
    7, 5,       /* bLength, bDescriptorType=ENDPOINT */
    0x81,       /* bEndpointAddress = EP1 IN */
    3,          /* bmAttributes = Interrupt */
    1, 0,       /* wMaxPacketSize = 1 */
    12          /* bInterval */
};

/* Hub class descriptor (type 0x29), USB 2.0 11.23.2.1. One downstream
   port, individual power switching, per-port overcurrent reporting. */
static const unsigned char usb_hub_class_desc[] = {
    9,          /* bDescLength */
    0x29,       /* bDescriptorType = HUB */
    1,          /* bNbrPorts */
    0x09, 0x00, /* wHubCharacteristics: individual power + individual OC */
    50,         /* bPwrOn2PwrGood (x2 ms) */
    100,        /* bHubContrCurrent */
    0x00,       /* DeviceRemovable */
    0xFF        /* PortPwrCtrlMask */
};

/* Standard HID boot keyboard report descriptor (63 bytes) */
static const unsigned char usb_hid_report_desc[] = {
    0x05, 0x01, 0x09, 0x06, 0xA1, 0x01,
    0x05, 0x07, 0x19, 0xE0, 0x29, 0xE7, 0x15, 0x00, 0x25, 0x01,
    0x75, 0x01, 0x95, 0x08, 0x81, 0x02,
    0x95, 0x01, 0x75, 0x08, 0x81, 0x01,
    0x95, 0x05, 0x75, 0x01, 0x05, 0x08, 0x19, 0x01, 0x29, 0x05,
    0x91, 0x02, 0x95, 0x01, 0x75, 0x03, 0x91, 0x01,
    0x95, 0x06, 0x75, 0x08, 0x15, 0x00, 0x25, 0x65,
    0x05, 0x07, 0x19, 0x00, 0x29, 0x65, 0x81, 0x00,
    0xC0
};

/* PS/2 Set 1 scancode → USB HID usage ID (common keys) */
static unsigned char ps2_to_hid[128] = {
    0,0x29,0x1E,0x1F,0x20,0x21,0x22,0x23, /* 00-07: none,ESC,1-6 */
    0x24,0x25,0x26,0x27,0x2D,0x2E,0x2A,0x2B, /* 08-0F: 7-0,-,=,BS,TAB */
    0x14,0x1A,0x08,0x15,0x17,0x1C,0x18,0x0C, /* 10-17: Q,W,E,R,T,Y,U,I */
    0x12,0x13,0x2F,0x30,0x28,0xE0,0x04,0x16, /* 18-1F: O,P,[,],ENTER,LCTRL,A,S */
    0x07,0x09,0x0A,0x0B,0x0D,0x0E,0x0F,0x33, /* 20-27: D,F,G,H,J,K,L,; */
    0x34,0x35,0xE1,0x31,0x1D,0x1B,0x06,0x19, /* 28-2F: ',`,LSHIFT,\,Z,X,C,V */
    0x05,0x11,0x10,0x36,0x37,0x38,0xE5,0x55, /* 30-37: B,N,M,comma,.,/,RSHIFT,KP* */
    0xE2,0x2C,0x39,0x3A,0x3B,0x3C,0x3D,0x3E, /* 38-3F: LALT,SPACE,CAPS,F1-F5 */
    0x3F,0x40,0x41,0x42,0x43,0x53,0x47,0x5F, /* 40-47: F6-F10,NUM,SCROLL,KP7 */
    0x52,0x61,0x56,0x50,0x5C,0x4F,0x59,0x51, /* 48-4F: UP,PGUP,KP-,LEFT,KP5,RIGHT,KP+,END */
    0x51,0x4E,0x49,0x4C,0,0,0,0x44,           /* 50-57: DOWN,PGDN,INS,DEL,,,,,F11 */
    0x45,0,0,0,0,0,0,0,                        /* 58-5F: F12 */
    0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,         /* 60-6F */
    0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0          /* 70-7F */
};

/* USB UVC Camera device descriptor (18 bytes) */
static const unsigned char usb_uvc_dev_desc[] = {
    18, 1,      /* bLength, bDescriptorType=DEVICE */
    0x00, 0x02, /* bcdUSB = 2.00 */
    0xEF, 0x02, 0x01, /* class=misc, sub=common, proto=IAD */
    64,         /* bMaxPacketSize0 */
    0x6D, 0x04, /* idVendor = 0x046D (Logitech) */
    0x25, 0x08, /* idProduct = 0x0825 */
    0x00, 0x01, /* bcdDevice = 1.00 */
    0, 0, 0,    /* iManufacturer, iProduct, iSerial */
    1           /* bNumConfigurations */
};

/* Minimal UVC config: IAD + VideoControl IF + VideoStreaming IF + isoc EP = 71 bytes */
static const unsigned char usb_uvc_cfg_desc[] = {
    /* Config descriptor */
    9, 2, 71, 0, 2, 1, 0, 0x80, 250, /* wTotalLength=71, 2 interfaces, 500mA */
    /* Interface Association Descriptor (IAD) */
    8, 11, 0, 2, 14, 3, 0, 0, /* bFirstInterface=0, bInterfaceCount=2, class=video */
    /* VideoControl Interface */
    9, 4, 0, 0, 0, 14, 1, 0, 0, /* IF0: video control, 0 endpoints */
    /* VC Header */
    13, 0x24, 1, 0x50, 0x01, 13+0, 0, 0, 0, 1, 1, 0, 0,
    /* VideoStreaming Interface alt 0 (zero bandwidth) */
    9, 4, 1, 0, 0, 14, 2, 0, 0,
    /* VideoStreaming Interface alt 1 (active) */
    9, 4, 1, 1, 1, 14, 2, 0, 0,
    /* Isochronous IN endpoint */
    7, 5, 0x82, 0x05, 0x00, 0x04, 1, /* EP2 IN, isoc, 1024 bytes, interval=1 */
};

/* Test pattern generator: produces a 160x120 YUYV frame (38400 bytes) */
#define UVC_FRAME_W 160
#define UVC_FRAME_H 120
#define UVC_FRAME_SIZE (UVC_FRAME_W * UVC_FRAME_H * 2)  /* YUYV = 2 bytes/pixel */
static unsigned int uvc_frame_counter = 0;

static void uvc_generate_test_frame(unsigned char *buf) {
    for (int y = 0; y < UVC_FRAME_H; y++) {
        for (int x = 0; x < UVC_FRAME_W; x++) {
            int off = (y * UVC_FRAME_W + x) * 2;
            int bar = (x * 8) / UVC_FRAME_W;
            unsigned char yval = (bar & 1) ? 235 : 16;
            unsigned char uv = (bar < 4) ? 128 + bar * 20 : 128 - (bar - 4) * 20;
            buf[off] = yval;
            buf[off + 1] = uv;
        }
    }
    uvc_frame_counter++;
}

/* HID keyboard held-key set. A boot report describes the keys currently
   HELD, not a queue of events -- the old builder scanned the PS/2 event
   queue, so a key looked held until the PS/2 consumer drained it and a
   pure-USB guest saw keys stuck down forever. Every host input path calls
   hid_key_event beside kbd_enqueue: make adds the usage, break removes it.

   One keystroke, one keyboard: the VM has a single virtual keyboard that
   feeds both the PS/2 port and the HID device model. A guest that has
   programmed the PIC with IRQ1 unmasked takes keys through its PS/2 ISR
   (the same predicate that suppresses the host's direct cell-28680 write);
   mirroring them into the HID set as well would double every keystroke
   for a payload that also pumps USB. When the PS/2 route is live the HID
   set stays empty -- and is cleared, so a make that landed before the
   guest unmasked IRQ1 cannot linger as a phantom held key. */
static unsigned char hid_held_mods = 0;
static unsigned char hid_held_keys[6] = {0};

/* Real interrupt endpoints NAK until they have news: a HID interrupt IN
   TRB completes only when its report would differ from the last one
   delivered on that endpoint (for the combo mouse: only when a sample
   arrived since the last report); otherwise the TD stays pending, and
   endpoints left NAKing are re-rung from the main loop when input state
   changes.

   DEFAULT SINCE 2026-08-06, with -hid-instant-complete as the opt-out.
   The old default completed every interrupt IN TRB at doorbell time with
   whatever the state was then, which consumes the guest's armed TD and
   leaves nothing for a later report to land in. Measured on
   usb-kbd-multi, three runs per cell, key hold width the only variable:
   1 ms and 2 ms holds gave got=0 under instant-complete and got=30 here;
   10 ms and the shipped 600 ms gave got=30 under both. A keystroke
   narrower than the guest's poll interval simply did not exist, and it
   failed silently -- the VM logs the key event and the report reaching
   the endpoint either way.

   -hid-instant-complete keeps the old model reachable because it is the
   one where completions are so plentiful that a driver defect fed by
   their SCARCITY -- one endpoint's waiter consuming a sibling's rare
   completion and starving it -- cannot be expressed. That is a real use
   (it is the negative arm usb-hid-steal was designed against), but it is
   a thing to ask for, not a thing to be handed. */
static int hid_nak_unchanged = 1;
/* Now that the NAK model is the default, its narration would be on every
   bed's stderr rather than only where it was asked for. CODEX_VM_HIDNAK_TRACE
   restores it, sampled once like the BOT trace. */
static int hid_nak_dbg = 0;
static int hid_nak_trace = -1;
static int hid_nak_tracing(void) {
    if (hid_nak_trace < 0) hid_nak_trace = getenv("CODEX_VM_HIDNAK_TRACE") ? 1 : 0;
    return hid_nak_trace;
}
static volatile int hid_input_changed = 0;
static long hid_reringe_calls = 0; /* input-changed re-ring passes */
static long hid_service_laps = 0;  /* HID service-thread laps */
/* The scripted-timeline zero. It was a local in main until the pointer
   timeline had to be applied from the service thread as well. */
static LARGE_INTEGER hid_timebase;
static volatile int hid_mouse_fresh = 0;
static unsigned char hid_nak_last[XHCI_MAX_SLOTS + 1][32][8];
static unsigned char hid_nak_seen[XHCI_MAX_SLOTS + 1][32];
static struct xhci_state *hid_nak_ctl = 0;

static int ps2_irq_route_live(void);

static int hid_first_key_logged = 0;
static void hid_key_event(unsigned char scancode) {
    hid_input_changed = 1;
    if (!hid_first_key_logged) {
        hid_first_key_logged = 1;
        fprintf(stderr, "HID: first key event sc=%02x keys_only=%d route_live=%d\n",
                scancode, hid_keys_only, ps2_irq_route_live());
    }
    /* The route-live suppression prevents double delivery for guests taking
       keys through their PS/2 ISR. Under -hid-keys nothing is enqueued to
       PS/2 at all, so the HID set is the only route and must fill. */
    if (!hid_keys_only && ps2_irq_route_live()) {
        hid_held_mods = 0;
        memset(hid_held_keys, 0, sizeof(hid_held_keys));
        return;
    }
    unsigned char hid = ps2_to_hid[scancode & 0x7F];
    if (hid == 0) return;
    int up = (scancode & 0x80) != 0;
    if (hid >= 0xE0 && hid <= 0xE7) {
        if (up) hid_held_mods &= (unsigned char)~(1 << (hid - 0xE0));
        else    hid_held_mods |= (unsigned char)(1 << (hid - 0xE0));
        return;
    }
    for (int i = 0; i < 6; i++) {
        if (up  && hid_held_keys[i] == hid) { hid_held_keys[i] = 0; return; }
    }
    if (up) return;
    for (int i = 0; i < 6; i++) if (hid_held_keys[i] == hid) return;
    for (int i = 0; i < 6; i++) {
        if (hid_held_keys[i] == 0) { hid_held_keys[i] = hid; return; }
    }
}

/* Build an 8-byte HID boot keyboard report from the held-key set */
static int hid_first_report_logged = 0;
static void build_hid_keyboard_report(unsigned char *report) {
    memset(report, 0, 8);
    report[0] = hid_held_mods;
    int slot = 2;
    for (int i = 0; i < 6; i++) {
        if (hid_held_keys[i]) report[slot++] = hid_held_keys[i];
    }
}

/* Which keyboard device the injected keys appear on. Exactly one carries
   them, because two devices mirroring one held-key set is not a machine
   anyone has: the root keyboard by default; the hub keyboard when the
   root one is unplugged (-xhci-no-root-kbd) or silenced
   (-hid-root-silent). The other keyboard reports zeros -- the idle
   heartbeat of a keyboard nobody is typing on. */
static int hid_kbd_carries_keys(int kind_hid_or_hub) {
    if (kind_hid_or_hub == 2 /* XHCI_KIND_HID */) return !xhci_hid_root_silent;
    return xhci_no_root_kbd || xhci_hid_root_silent;
}

/* Boot mouse report from the pointer the host already tracks: the delta
   since the last report, clamped to the signed byte the boot protocol
   carries, so a large jump arrives as several reports exactly as a real
   mouse delivers it. Buttons are the live state plus the press latch, the
   same edge guarantee the absolute port path gives. */
/* How often the guest actually COLLECTS a pointer report. The pointer is
   relative and converges at 127 px a report, so this rate, not the delta cap,
   is what decides whether the cursor tracks or hops: a loop that polls slowly
   makes the pointer jump the whole accumulated distance at once. */
static long hid_mouse_reports = 0;
static long xhci_mouse_doorbells = 0;
static int hid_mouse_last_x = 0, hid_mouse_last_y = 0;
static int hid_mouse_delta_clamp(int d) {
    if (d > 127) return 127;
    if (d < -127) return -127;
    return d;
}
static void build_hid_mouse_report(unsigned char *report) {
    int dx = hid_mouse_delta_clamp(pending_mouse_abs_x - hid_mouse_last_x);
    int dy = hid_mouse_delta_clamp(pending_mouse_abs_y - hid_mouse_last_y);
    hid_mouse_last_x += dx;
    hid_mouse_last_y += dy;
    LONG latched = InterlockedExchange(&pending_mouse_btn_latch, 0);
    report[0] = (unsigned char)((pending_mouse_btn | (int)latched) & 7);
    report[1] = (unsigned char)(dx & 0xFF);
    report[2] = (unsigned char)(dy & 0xFF);
    report[3] = 0;
}


/* Endpoint ID (control bits 20:16) is part of every Transfer Event (xHCI
   6.4.2.1). This model omitted it until 2026-08-03, so every bed transfer
   event carried epid=0 and any per-endpoint attribution done by a guest
   counted real completions as foreign -- the kbd probe's EPINT row read 0
   in beds where the endpoint was in fact delivering. Command Completion
   (6.4.2.2) and Port Status Change (6.4.2.3) events have no such field;
   callers pass 0. */
/* -usb-bot-drop N: drop the Nth transfer event on a BULK endpoint (ep_id >= 2,
   so EP0 enumeration is untouched). The data still moves; only the completion
   goes missing, which is the worksflight 2026-08-09 signature exactly -- a
   32 KB data phase answering msc-ce-no-event with no stall and a volume left
   clean. Without it no bed can reach a guest's fuel-exhaustion path or the
   Reset Recovery retry behind it, and an unexecuted recovery path is worth
   what no recovery path is worth. N counts from 1.
   -usb-bot-drops K: swallow K CONSECUTIVE events starting at N, default 1.
   One drop is always recovered -- the driver re-issues the transfer and the
   write completes -- so with K=1 no bed can produce a REFUSED write, which is
   the shape the 2026-08-19 metal sitting showed. K=2 is NOT enough (measured:
   both events swallowed, the driver still recovers); K=4 at index 500 is what
   refuses on diag.img, because the retry's own transfers have to fall inside
   the window too. */
static int usb_bot_drop = 0;
static int usb_bot_drops = 1;
static int usb_bot_xfer_seen = 0;
/* -usb-bot-drop-len N: swallow the transfer event for any BULK transfer while
   the BOT command in flight carries N bytes or more, and let everything
   smaller through. It is the SIZE-KEYED sibling of -usb-bot-drop, and it
   exists because an ORDINAL is not a property of the thing under test.
   -usb-bot-drop 500 names the 500th event since boot, so inserting a stage
   anywhere upstream moves the drop into a different phase without failing:
   measured 2026-08-20, adding the xhci stage at 8 moved the sink arm's drop
   out of the data phase into the MOUNT, and the arm reported mount-fail --
   a plausible word, not a nonsense one, which is what makes it dangerous.
   Keyed to dCBWDataTransferLength instead, the same flag means the same
   thing whatever else the guest does first, and it gives a bed a THRESHOLD
   to answer with: -usb-bot-drop-len 16384 refuses every command of 32
   sectors or more and completes everything below it, so an instrument that
   claims to measure the largest transfer a device accepts must answer 32.
   WRITES ONLY (dir_in == 0), and that is not a detail: the first version
   refused reads as well, and the mount reads more than 16 KB, so the volume
   never mounted and the stage under test reported no-medium instead of the
   threshold. A size lever aimed at a write path must leave the read path
   alone or it never reaches the thing it is aimed at.
   -usb-bot-drop-len-max M bounds it ABOVE, and it exists because the
   diagnostic bank writes 32,768-byte commands of its own: without an upper
   bound, every threshold small enough to refuse a rung also refuses the
   bank, the bank dies before the stage runs, and the row reads no-medium.
   Measured 2026-08-20. With N == M the lever refuses exactly one command
   size, which is what a control wants: the bed's answer is a number the
   instrument must reproduce, not a direction. 0 means no upper bound. */
static int usb_bot_drop_len = 0;
static int usb_bot_drop_len_max = 0;
/* -usb-bot-die-len N: the target stops answering FOREVER once it sees a write
   command carrying N bytes or more. Not one refused command -- the device is
   gone from that point on, which is what the metal target does.

   The sink's 2.7 MB write has killed the bank on the board three times
   (sittings 7, 8 and 9 lost the deferred sink and every stage after it), and
   no bed could produce that. -usb-bot-drop-len refuses a single command and
   the device keeps answering, so `sink-drop` reads sink=write-refused with
   bank=ok and the file whole: the refusal is handled and the run survives.
   That is a different event from the one metal keeps delivering, and reading
   one as evidence about the other is what left the deferral ordering without
   a falsifier.

   KEYED ON THE WRITE'S OWN LENGTH, like -usb-bot-drop-len and unlike
   -usb-bot-drop, because an ordinal counts transfer events since boot and is
   a property of the whole run: inserting a stage moved -usb-bot-drop's
   landing out of the sink's data phase once already and the arm reported a
   plausible wrong word.

   AND THE LENGTH DOES NOT AIM IT, which is the part to read before using
   this. "The sink writes 2.7 MB as one write" is true of the sink's own call
   and false at this layer: measured 2026-08-21, it goes out as 64-sector
   commands, 32768 bytes each (the stage's own row says chunk=64), and the
   BANK writes 32768-byte commands too. So no threshold separates them -- at
   1000000 the flag never fires at all, and at 32768 or below it dies on
   whichever bulk write comes first. What it gives is a target that survives
   until the first bulk write and then never answers again, which is a real
   fault model and is NOT the same as "dies during the sink".

   Measured on the diag image at 32768: the run reaches the deferred sink,
   the target dies, and the summary reads `bank=none write refused, write
   stage 13` with sink state=no-medium -- a bank death at the sink, but
   reported as none rather than the metal word `lost`. Aiming it at the sink
   specifically needs a key the sink owns and the bank does not; LBA range is
   the candidate and is not measured yet. Until then this is a lever without
   a sight, and no arm keys on it. */
/* -usb-bot-census: print every BOT read and write with its LBA and declared
   length. Off by default and stderr only, so no arm's output moves. */
static int usb_bot_census  = 0;
/* -census FILE: send every census line to FILE instead of stderr, and turn
   the BOT census on. The point is an ARTIFACT that outlives the run: a
   rehearsal keeps its bed trace beside the arm's own output, so the board's
   trace has something to be diffed against row for row when it arrives.
   stderr does not survive -- diag-arm redirects it per arm and the harness
   KILLS codex-vm the moment END appears.

   WHICH IS ALSO WHY EVERY LINE IS FLUSHED. A killed process does not drain
   its buffers, so a census written the ordinary way would be empty or cut
   at an arbitrary point, and a truncated trace and a complete one look the
   same when you are reading rows (L-SHORT). The cost is one flush per
   command on a path that is off by default. */
static FILE *census_fp = NULL;

static FILE *census_out(void) { return census_fp ? census_fp : stderr; }
static int usb_bot_die_len = 0;
/* The LBA at or above which -usb-bot-die-len fires. 0 means anywhere, which
   is the unaimed lever; see the census note at the die check. */
static int usb_bot_die_lba = 0;
static int usb_bot_dead    = 0;
/* -usb-bot-revive-on-reset: the dead target ANSWERS AGAIN after a Bulk-Only
   Mass Storage Reset. Default off, so -usb-bot-die-lba stays the latched
   death every existing arm is written against.

   It keys on the BOT class reset and not on a port reset because that is the
   one the driver actually issues. Measured 2026-08-21 off sink-dies' own
   stderr: after the death the guest sends four Mass Storage Resets and no
   port reset at all. A lever keyed on a reset the driver never performs is
   an arm that cannot fire, which is the failure this switch exists to let an
   arm avoid rather than commit.

   The control transfer carrying that reset rides ep0, which the death latch
   never gated -- it suppresses transfer events on ep 2 and above -- so the
   reset is seen while the target is dead, and reviving here is the only
   missing half. Without this, msc-cell-retry can reach msc-retry-failed (2,
   the reset ran and the retried write did not) and can never reach
   msc-retry-ok (3), so the driver's recovery has a bed for its failure and
   none for its success. */
static int usb_bot_revive  = 0;
/* -usb-bot-die-on-nic: the BOT target stops answering FOREVER at the first
   bulk WRITE issued after the e1000 model sees the first observable of NIC
   bring-up. Latched like -usb-bot-die-lba, and SPEC-FREE in the same way: it
   injects a symptom, not a mechanism. Nothing here claims to know why a part
   would behave this way, and no reading taken under it is evidence that it
   does.

   The symptom it exists for is sitting 11's, measured 2026-08-21. The bank
   ended at `stage=b3 step=rings-link`; the next two mid-stage notes (k1,
   calibrate) never landed, b3 nevertheless went on to complete a TCP
   exchange, and the glass read BANK LOST AT STAGE 15. So the medium stopped
   taking writes DURING e1000 bring-up -- ring setup, semaphore, link-up --
   and not at a large write and not at the sink. Sittings 7 to 10 lost the
   bank at the sink, which was also the first write after the NIC stages, so
   the two readings are one candidate and not two: the xHCI/MSC path dies
   when the I219 is brought up, both being on the PCH.

   WHY THIS IS NOT -usb-bot-die-lba WITH A DIFFERENT NUMBER. Both of the
   existing levers are keyed on properties of the WRITE (its length, its
   LBA), so both can only express "the medium dies at a certain kind of
   write". The candidate here is that the write is innocent and its TIMING is
   the whole of it: the same write survives before bring-up and dies after.
   No length and no LBA can separate those two, because they are the same
   command. This keys on the OTHER DEVICE, which is what makes the bed able
   to say no to the candidate rather than merely reproduce a bank death.

   WHICH observable arms it is measured rather than assumed, and the run says
   which. Either the CTRL write that sets SLU or the RCTL write that sets EN
   arms it, whichever the driver issues first, and the arming line names the
   register. Committing to one in advance would have been a guess about our
   own driver's order, and that order has already moved once (the quiesce
   rework put an RCTL write with EN clear ahead of the link-up). */
static int usb_bot_die_on_nic = 0;
static int usb_bot_nic_armed  = 0;

/* Called from e1000_write on the first bring-up observable. Idempotent: only
   the FIRST one arms, so a driver that writes CTRL.SLU repeatedly during a
   retry does not restate the reading. */
static void usb_bot_nic_arm(const char *what) {
    if (!usb_bot_die_on_nic || usb_bot_nic_armed) return;
    usb_bot_nic_armed = 1;
    fprintf(stderr, "xHCI: -usb-bot-die-on-nic: ARMED by %s; the target dies on the next bulk write\n", what);
}

static void xhci_post_event_ep_resid(int trb_type, int slot, int ep_id, int completion, unsigned long long trb_ptr, int resid) {
    if (xcur->er_addr == 0 || xcur->er_size == 0) return;
    if (usb_bot_drop > 0 && trb_type == 32 && ep_id >= 2) {
        usb_bot_xfer_seen++;
        if (usb_bot_xfer_seen >= usb_bot_drop && usb_bot_xfer_seen < usb_bot_drop + usb_bot_drops) {
            fprintf(stderr, "xHCI: -usb-bot-drop: swallowing transfer event %d (slot=%d ep=%d cc=%d)\n",
                    usb_bot_xfer_seen, slot, ep_id, completion);
            return;
        }
    }
    /* Already dead: nothing this target does answers again. Latched rather
       than re-tested so the run cannot recover by sending something small. */
    if (usb_bot_dead && trb_type == 32 && ep_id >= 2) return;
    if (usb_bot_die_len > 0 && trb_type == 32 && ep_id >= 2 &&
        bot.active && !bot.dir_in && bot.xfer_len >= (unsigned int)usb_bot_die_len) {
        unsigned int lba = ((unsigned int)bot.cb[2] << 24) | ((unsigned int)bot.cb[3] << 16) |
                           ((unsigned int)bot.cb[4] << 8) | bot.cb[5];
        /* -usb-bot-die-lba is what AIMS it. Length alone cannot: measured by
           census 2026-08-21, the bank issues 32768-byte writes too. It issues
           them at exactly two fixed LBAs, 2049 and 2153, and its file data
           goes out at 2560..3584 bytes around 3475..3541, while the sink is
           one contiguous burst of sixty 32768-byte writes at 3548..7324. So
           the two keys together separate them with about 1300 sectors of
           margin, where either key alone has none. */
        if (lba >= (unsigned int)usb_bot_die_lba) {
            fprintf(stderr, "xHCI: -usb-bot-die-len: target DIED on a %u-byte write at lba=%u (slot=%d ep=%d); nothing answers from here\n",
                    bot.xfer_len, lba, slot, ep_id);
            usb_bot_dead = 1;
            return;
        }
    }
    /* Keyed on the NIC, not on the write. Any bulk write will do once armed,
       which is the point: the candidate says the write is innocent. */
    if (usb_bot_die_on_nic && usb_bot_nic_armed && trb_type == 32 && ep_id >= 2 &&
        bot.active && !bot.dir_in) {
        unsigned int lba = ((unsigned int)bot.cb[2] << 24) | ((unsigned int)bot.cb[3] << 16) |
                           ((unsigned int)bot.cb[4] << 8) | bot.cb[5];
        fprintf(stderr, "xHCI: -usb-bot-die-on-nic: target DIED on the first %u-byte write at lba=%u after NIC bring-up (slot=%d ep=%d); nothing answers from here\n",
                bot.xfer_len, lba, slot, ep_id);
        usb_bot_dead = 1;
        return;
    }
    if (usb_bot_drop_len > 0 && trb_type == 32 && ep_id >= 2 &&
        bot.active && !bot.dir_in && bot.xfer_len >= (unsigned int)usb_bot_drop_len &&
        (usb_bot_drop_len_max == 0 || bot.xfer_len <= (unsigned int)usb_bot_drop_len_max)) {
        fprintf(stderr, "xHCI: -usb-bot-drop-len: swallowing transfer event for a %u-byte command (slot=%d ep=%d cc=%d)\n",
                bot.xfer_len, slot, ep_id, completion);
        return;
    }
    /* Ring full = advancing the producer would land on the slot the guest's
       last ERDP write-back names (xHCI 4.9.4, one-slot-open convention). Real
       silicon stops posting and drops; it never laps the consumer. Every bed
       before this check posted blindly, and none ever produced 64 events, so
       neither the wrap nor the full path had ever executed against the guest
       (the ASUS's 26-port controller crosses both). Report the first drop:
       a guest gone silent for this reason must be attributable. */
    if (xcur->guest_erdp >= xcur->er_addr &&
        xcur->guest_erdp < xcur->er_addr + (unsigned long long)xcur->er_size * 16) {
        int consumer = (int)((xcur->guest_erdp - xcur->er_addr) / 16);
        if ((xcur->erdp_idx + 1) % xcur->er_size == (unsigned)consumer) {
            if (!xcur->er_dropped)
                fprintf(stderr, "xHCI: EVENT RING FULL (size=%u, consumer at %d): dropping type=%d -- guest stopped acknowledging ERDP\n",
                        xcur->er_size, consumer, trb_type);
            xcur->er_dropped++;
            return;
        }
    }
    unsigned long long off = xcur->er_addr + (unsigned long long)xcur->erdp_idx * 16;
    if (off + 16 > guest_mem_size) return;
    unsigned char *ev = (unsigned char *)guest_mem + off;
    /* TRB pointer (param lo/hi) = address of command/transfer TRB that completed */
    *(unsigned int *)(ev + 0) = (unsigned int)(trb_ptr & 0xFFFFFFFF);
    *(unsigned int *)(ev + 4) = (unsigned int)(trb_ptr >> 32);
    /* Status: completion code in bits 31:24, residual in bits 23:0 */
    *(unsigned int *)(ev + 8) = ((completion & 0xFF) << 24) | (resid & 0xFFFFFF);
    /* Control: TRB type in bits 15:10, endpoint ID in 20:16, slot in 31:24 */
    *(unsigned int *)(ev + 12) = ((trb_type & 0x3F) << 10) | ((ep_id & 0x1F) << 16) | ((slot & 0xFF) << 24) | (xcur->er_ccs & 1);
    xcur->erdp_idx++;
    if (xcur->erdp_idx >= xcur->er_size) {
        xcur->erdp_idx = 0;
        xcur->er_ccs ^= 1;
    }
}

static void xhci_post_event_ep(int trb_type, int slot, int ep_id, int completion, unsigned long long trb_ptr) {
    xhci_post_event_ep_resid(trb_type, slot, ep_id, completion, trb_ptr, 0);
}

static void xhci_post_event(int trb_type, int slot, int completion, unsigned long long trb_ptr) {
    xhci_post_event_ep_resid(trb_type, slot, 0, completion, trb_ptr, 0);
}

/* Advance one TRB slot, following a link TRB (with toggle-cycle handling)
   if one sits at the new position. Rings are cycle-managed: a TRB belongs
   to the controller only while its cycle bit matches the consumer state. */
static unsigned long long xhci_next_trb(unsigned long long addr, int *ccs) {
    addr += 16;
    for (int hops = 0; hops < 4; hops++) {
        if (addr + 16 > guest_mem_size) return addr;
        unsigned char *t = (unsigned char *)guest_mem + addr;
        unsigned int c = *(unsigned int *)(t + 12);
        if (((c >> 10) & 0x3F) != 6 || (int)(c & 1) != *ccs) return addr;
        if (c & 2) *ccs ^= 1;
        addr = *(unsigned long long *)t & ~0xFULL;
    }
    return addr;
}

/* Copy the contexts an input context offers into the slot's output device
   context, honouring the Add Context flags (A0 = slot context, An = DCI n).
   This is the controller's job on real hardware: the driver owns the input
   context and never writes the output one. It is also what makes an
   endpoint reachable at all, because the transfer doorbell reads its TR
   dequeue pointer out of the DEVICE context -- so a slot whose contexts
   were never copied answers every doorbell with a dequeue pointer of zero
   and moves no bytes, while ADDRESS_DEVICE still reports success.

   Context stride is XHCI_CTXSZ: 32 when we advertise CSZ = 0, 64 under
   -xhci-csz. Input: control context at +0, slot context at +stride, DCI n
   at +(n+1)*stride. Output: slot context at +0, DCI n at +n*stride. So
   add-flag bit i copies input+(i+1)*stride to device+i*stride. */
static void xhci_copy_input_ctx(unsigned long long ictx, int slot) {
    int cs = XHCI_CTXSZ;
    if (xcur->dcbaap == 0 || ictx == 0) return;
    unsigned long long ent = xcur->dcbaap + (unsigned long long)slot * 8;
    if (ent + 8 > guest_mem_size) return;
    unsigned long long dctx =
        *(unsigned long long *)((unsigned char *)guest_mem + ent) & ~0x3FULL;
    if (dctx == 0) return;
    if (ictx + 8 > guest_mem_size) return;
    unsigned int add = *(unsigned int *)((unsigned char *)guest_mem + ictx + 4);
    for (int ci = 0; ci < 32; ci++) {
        if (!(add & (1u << ci))) continue;
        unsigned long long src = ictx + (unsigned long long)(ci + 1) * cs;
        unsigned long long dst = dctx + (unsigned long long)ci * cs;
        if (src + cs > guest_mem_size || dst + cs > guest_mem_size) continue;
        memcpy((unsigned char *)guest_mem + dst,
               (unsigned char *)guest_mem + src, cs);
        /* xHCI 4.6.5-6: the CONTROLLER owns EP State (output context dword 0
           bits 2:0) and sets a successfully addressed or configured endpoint
           to Running(1); the input context offers it as 0. Copying verbatim
           left est=0 in every bed while real silicon reads 1, so the one row
           built to discriminate silences disagreed with metal by default. */
        if (ci >= 1) {
            unsigned int *d0 = (unsigned int *)((unsigned char *)guest_mem + dst);
            *d0 = (*d0 & ~7u) | 1;
        }
    }
}

static int bot_db_trace = -1;
/* Device personality for a slot. Root port picks the device, except on the
   hub's root port, where the route string picks between the hub itself
   (route 0) and what hangs off its downstream port (route non-zero).
   Slots addressed before the port latch existed fall back to the historical
   slot-number binding. */
#define XHCI_KIND_MSC     1
#define XHCI_KIND_HID     2
#define XHCI_KIND_UVC     3
#define XHCI_KIND_HUB     4
#define XHCI_KIND_HUB_HID 5

/* Which tier of the hub stack a slot is, or -1 for anything that is not a
   hub. A hub is identified by the route string that reached it: tier 0 was
   addressed at route 0 (straight off root port 4), tier 1 at route 0x1 (down
   tier 0's port 1). Anything deeper is the keyboard. */
static int xhci_hub_tier(int slot) {
    if (xcur->slot_port[slot] != 4) return -1;
    if (xcur->slot_route[slot] == 0) return 0;
    if (xhci_hub_tiers >= 2 && xcur->slot_route[slot] == 0x1) return 1;
    return -1;
}

/* The speed class a slot's device really is, which is what the endpoint
   context's Interval encoding must be computed from. Everything this model
   puts below a hub is full speed -- xhci_hub_port_status sets neither the
   low- nor the high-speed bit -- including the full-speed hub at tier 1. */
static int xhci_slot_true_speed(int slot) {
    int port = xcur->slot_port[slot];
    if (port < 1 || port > XHCI_MAX_PORTS) return 0;
    if (xhci_hub_tier(slot) == 1) return 1;
    if (xhci_hub_tier(slot) < 0 && xcur->slot_route[slot] != 0) return 1;
    return xhci_port_true_speed[port - 1];
}

static int xhci_slot_kind(int slot) {
    int port = xcur->slot_port[slot];
    /* The relocated storage port is checked FIRST, so -usb-disk-port can
       carry the device to any root port without disturbing the identity
       the other three ports carry by their number. */
    if (port == usb_disk_port) return XHCI_KIND_MSC;
    if (port == 4) return xhci_hub_tier(slot) >= 0 ? XHCI_KIND_HUB : XHCI_KIND_HUB_HID;
    return port > 0 ? port : slot;
}

/* A hub's downstream port status, as one little-endian dword: status word
   low, change word high (USB 2.0 11.24.2.7). Whatever is behind it -- the
   next hub down or the keyboard -- is full speed, so neither the low-speed
   nor the high-speed bit is ever set, which is what makes everything below
   tier 0 need its transaction translator. */
static unsigned int xhci_hub_port_status(int tier) {
    unsigned int status = 0, change = 0;
    if (tier < 0 || tier >= XHCI_HUB_TIERS) return 0;
    if (xcur->hub_powered[tier]) status |= (1u << 0) | (1u << 8);  /* CONNECTION, POWER */
    if (xcur->hub_enabled[tier]) status |= (1u << 1);              /* ENABLE */
    if (xcur->hub_c_connection[tier]) change |= (1u << 0);         /* C_CONNECTION */
    if (xcur->hub_c_reset[tier]) change |= (1u << 4);              /* C_RESET */
    return status | (change << 16);
}

/* The endpoint descriptor is the last seven bytes of each of these
   configurations (USB 2.0 9.6.6: bLength, bDescriptorType, bEndpointAddress,
   bmAttributes, wMaxPacketSize, bInterval). Read the fields out of the bytes
   the model actually serves rather than restating them, so the expectation
   cannot drift from the descriptor it is an expectation about. */
static const unsigned char *xhci_periodic_ep_desc(int kind, int ep_idx) {
    if (kind == XHCI_KIND_HID && hid_combo)
        return ep_idx == 5 ? usb_hid_combo_cfg_desc + sizeof(usb_hid_combo_cfg_desc) - 7
                           : usb_hid_combo_cfg_desc + 27;
    if (kind == XHCI_KIND_HID || kind == XHCI_KIND_HUB_HID)
        return usb_hid_cfg_desc + sizeof(usb_hid_cfg_desc) - 7;
    if (kind == XHCI_KIND_HUB)
        return usb_hub_cfg_desc + sizeof(usb_hub_cfg_desc) - 7;
    return 0;
}

/* xHCI 1.2 Table 6-12. The Interval field of an interrupt endpoint context
   is a speed-dependent ENCODING of the descriptor's bInterval, never the
   bInterval itself. Full and low speed count whole frames, so the field is
   floor(log2(bInterval * 8)) clamped to 3..10; high and super speed already
   carry a 2^(bInterval-1) microframe exponent, so the field is bInterval - 1
   clamped to 0..15. A driver that takes the wrong branch writes a legal,
   non-zero, WRONG number. */
static int xhci_expected_interval(int speed, int b_interval) {
    int v;
    if (b_interval <= 0) return -1;
    if (speed == 1 || speed == 2) {          /* full, low */
        int frames = b_interval * 8;
        v = 0;
        while ((1 << (v + 1)) <= frames) v++;
        if (v < 3) v = 3;
        if (v > 10) v = 10;
    } else {                                 /* high, super */
        v = b_interval - 1;
        if (v > 15) v = 15;
    }
    return xhci_calibrate_periodic ? v + 1 : v;
}

/* xHCI 1.2 section 6.2.3.8. Max ESIT Payload is the most an endpoint can
   move in one service interval: Max Packet Size * (Max Burst + 1). Nothing
   this model presents bursts, so it is wMaxPacketSize exactly. */
static int xhci_expected_esit(int max_packet) {
    return xhci_calibrate_periodic ? max_packet + 1 : max_packet;
}

/* The transfer-ring walk now has two callers on two threads: the guest's
   doorbell write on the VP thread, and the HID service thread standing in for
   a controller polling a periodic endpoint. One lock over both, taken only
   around the walk itself. */
static CRITICAL_SECTION xhci_db_lock;
static int xhci_db_lock_ready = 0;
static void xhci_db_lock_enter(void) { if (xhci_db_lock_ready) EnterCriticalSection(&xhci_db_lock); }
static void xhci_db_lock_leave(void) { if (xhci_db_lock_ready) LeaveCriticalSection(&xhci_db_lock); }

static void xhci_handle_doorbell(int db, unsigned int val) {
    if (db == 0) {
        /* Command ring doorbell -- consume TRBs while their cycle bit
           matches the consumer cycle state. The walk resumes at the exact
           stored position; CRCR itself cannot hold it (its address field
           is 64-byte aligned, and rounding down replays consumed TRBs). */
        unsigned long long ring_addr = xcur->cr_pos;
        int ccs = xcur->cr_ccs;
        for (int safety = 0; safety < 64; safety++) {
            if (ring_addr + 16 > guest_mem_size) break;
            unsigned char *trb = (unsigned char *)guest_mem + ring_addr;
            unsigned int control = *(unsigned int *)(trb + 12);
            if ((int)(control & 1) != ccs) break;
            int trb_type = (control >> 10) & 0x3F;
            if (trb_type == 6) { /* LINK */
                if (control & 2) ccs ^= 1;
                ring_addr = *(unsigned long long *)trb & ~0xFULL;
                continue;
            }
            if (trb_type == 9) { /* ENABLE_SLOT */
                /* Slots allocate in order, one per enable -- a second device
                   gets slot 2, not slot 1 again. Completion code 9 is the
                   spec's No Slots Available Error.

                   Under -xhci-scratch, the first ENABLE_SLOT is where the
                   scratchpad array is judged: by then a correct driver has
                   written DCBAA[0] and the controller is running. Real
                   silicon given no scratchpad DMAs through a null pointer
                   and corrupts silently; this model refuses the slot and
                   says why, which is the same failure made observable. */
                int scratch_ok = 1;
                if (xhci_scratch_bufs > 0 && !xcur->scratch_checked) {
                    xcur->scratch_checked = 1;
                    unsigned long long arr = 0;
                    if (xcur->dcbaap != 0 && xcur->dcbaap + 8 <= guest_mem_size)
                        arr = *(unsigned long long *)((unsigned char *)guest_mem + xcur->dcbaap) & ~0x3FULL;
                    /* xHCI 6.1: the array itself needs 64-byte alignment;
                       only the BUFFER PAGES it points to need PAGESIZE. */
                    int bad = (arr == 0 || (arr & 0x3F) != 0 ||
                               arr + (unsigned long long)xhci_scratch_bufs * 8 > guest_mem_size);
                    for (int sb = 0; !bad && sb < xhci_scratch_bufs; sb++) {
                        unsigned long long pg = *(unsigned long long *)((unsigned char *)guest_mem + arr + sb * 8);
                        if (pg == 0 || (pg & 0xFFF) != 0 || pg + 4096 > guest_mem_size) bad = 1;
                    }
                    xcur->scratch_bad = bad;
                    fprintf(stderr, "xHCI: scratchpad check: declared %d, DCBAA[0]=0x%llx : %s\n",
                            xhci_scratch_bufs, arr, bad ? "BAD -- refusing ENABLE_SLOT" : "OK");
                }
                if (xhci_scratch_bufs > 0 && xcur->scratch_bad) scratch_ok = 0;
                int slot = (scratch_ok && xcur->next_slot <= XHCI_MAX_SLOTS) ? xcur->next_slot++ : 0;
                xhci_post_event(33, slot, slot ? 1 : 9, ring_addr);
            } else if (trb_type == 11 || trb_type == 12 || trb_type == 13) {
                /* ADDRESS_DEVICE / CONFIGURE_ENDPOINT / EVALUATE_CONTEXT:
                   succeed, echoing the slot the command named. Address
                   Device also latches the root port from the input
                   context's slot context (dword 1 bits 23:16), which is
                   what binds a slot to a device personality, and copies
                   the offered contexts into the output device context --
                   without which the slot is addressed but no endpoint of
                   it can carry a byte. CONFIGURE_ENDPOINT copies too, by
                   the same Add-flag rule, which is what populates a bulk
                   endpoint's context, and EVALUATE_CONTEXT by the same rule
                   again -- it is how a driver corrects endpoint zero's max
                   packet size once it has read the device descriptor and
                   knows what the device actually wanted. */
                int slot = (control >> 24) & 0xFF;
                if ((trb_type == 11 || trb_type == 12 || trb_type == 13) && slot >= 1 && slot <= XHCI_MAX_SLOTS) {
                    unsigned long long ictx = *(unsigned long long *)trb & ~0xFULL;
                    if (trb_type == 11 && ictx && ictx + (unsigned long long)XHCI_CTXSZ * 2 <= guest_mem_size) {
                        unsigned int sd0 = *(unsigned int *)((unsigned char *)guest_mem + ictx + XHCI_CTXSZ);
                        unsigned int sd1 = *(unsigned int *)((unsigned char *)guest_mem + ictx + XHCI_CTXSZ + 4);
                        unsigned int sd2 = *(unsigned int *)((unsigned char *)guest_mem + ictx + XHCI_CTXSZ + 8);
                        xcur->slot_port[slot]    = (sd1 >> 16) & 0xFF;
                        xcur->slot_route[slot]   = sd0 & 0xFFFFF;
                        xcur->slot_speed[slot]   = (sd0 >> 20) & 0xF;
                        xcur->slot_tt_hub[slot]  = sd2 & 0xFF;
                        xcur->slot_tt_port[slot] = (sd2 >> 8) & 0xFF;
                    }
                    xhci_copy_input_ctx(ictx, slot);
                }
                xhci_post_event(33, slot, 1, ring_addr);
            } else if (trb_type == 14 || trb_type == 15 || trb_type == 16) {
                /* RESET_ENDPOINT / STOP_ENDPOINT / SET_TR_DEQUEUE, xHCI
                   4.6.8-10: slot in control 31:24, endpoint DCI in 20:16.
                   Stop parks the state at Stopped(3) and writes the
                   controller's current dequeue into the output context --
                   the ONLY moment real silicon publishes its true position;
                   this model keeps the context dequeue current after every
                   walk, so the state change is the observable part. Set TR
                   Dequeue is legal only from Stopped(3) or Error(4), Reset
                   Endpoint only from Halted(2); anywhere else the answer is
                   Context State Error(19) and nothing moves, which is the
                   arm a driver that skipped the stop fails against. Until
                   2026-08-03 all three fell through with NO completion
                   event, so no recovery path could ever be bedded. */
                int slot = (control >> 24) & 0xFF;
                int dci = (control >> 16) & 0x1F;
                int code = 17; /* Parameter Error */
                if (slot >= 1 && slot <= XHCI_MAX_SLOTS && dci >= 1 &&
                    xcur->dcbaap && xcur->dcbaap + (unsigned long long)(slot + 1) * 8 <= guest_mem_size) {
                    unsigned long long dctx = *(unsigned long long *)((unsigned char *)guest_mem + xcur->dcbaap + (unsigned long long)slot * 8) & ~0x3FULL;
                    if (dctx && dctx + 32ULL * XHCI_CTXSZ <= guest_mem_size) {
                        unsigned int *ed0 = (unsigned int *)((unsigned char *)guest_mem + dctx + (unsigned long long)dci * XHCI_CTXSZ);
                        int est = (int)(*ed0 & 7);
                        if (trb_type == 15) {
                            /* 4.8.3 p.164: Stop received in Halted or Error
                               state has no effect -- Context State Error.
                               Otherwise 4.6.9 p.134: a Stopped Transfer
                               Event on the endpoint is MANDATORY before the
                               Command Completion Event, in every timing
                               scenario; an idle ring is the between-TDs
                               case, code Stopped - Length Invalid (27),
                               pointer = current dequeue, length 0. */
                            if (est == 2 || est == 4) code = 19;
                            else {
                                /* Between-TDs vs TD-in-progress (4.6.9
                                   p.134): a cycle-matching non-link TRB at
                                   the dequeue is a TD the endpoint has in
                                   hand -- FSE code Stopped(26) with the
                                   residual; an idle ring answers
                                   Stopped - Length Invalid(27), length 0.
                                   The ASUS emits 26 for the NAK-parked
                                   keyboard TD; beds must too. */
                                unsigned long long dq_full =
                                    *(unsigned long long *)((unsigned char *)ed0 + 8);
                                unsigned long long cur_dq = dq_full & ~0xFULL;
                                int fse_code = 27, fse_resid = 0;
                                if (cur_dq && cur_dq + 16 <= guest_mem_size) {
                                    unsigned int tctl = *(unsigned int *)((unsigned char *)guest_mem + cur_dq + 12);
                                    if ((int)(tctl & 1) == (int)(dq_full & 1) &&
                                        ((tctl >> 10) & 0x3F) != 6) {
                                        fse_code = 26;
                                        fse_resid = (int)(*(unsigned int *)((unsigned char *)guest_mem + cur_dq + 8) & 0x1FFFF);
                                    }
                                }
                                xhci_post_event_ep_resid(32, slot, dci, fse_code, cur_dq, fse_resid);
                                *ed0 = (*ed0 & ~7u) | 3;
                                code = 1;
                            }
                        } else if (trb_type == 14) {
                            if (est == 2) { *ed0 = (*ed0 & ~7u) | 3; code = 1; }
                            else code = 19;
                        } else {
                            if (est == 3 || est == 4) {
                                /* param bit 0 is DCS, bits 3:1 are the
                                   stream context type -- not part of the
                                   stored pointer. */
                                *(unsigned long long *)((unsigned char *)ed0 + 8) =
                                    *(unsigned long long *)trb & ~0xEULL;
                                code = 1;
                            } else code = 19;
                        }
                    }
                }
                xhci_post_event(33, slot, code, ring_addr);
            } else if (trb_type == 23) { /* NOOP */
                xhci_post_event(33, 0, 1, ring_addr);
            }
            ring_addr += 16;
        }
        xcur->cr_pos = ring_addr;
        xcur->cr_ccs = ccs;
    } else if (db >= 1 && db <= XHCI_MAX_SLOTS) {
        /* Transfer ring doorbell for a device slot -- process transfer TRBs */
        /* Counted to separate a guest that arms rarely from one that arms often
           into an endpoint that yields rarely.

           It does NOT count trips round the guest's loop, and reading it that
           way is what sent WORKS-26 to the repaint path for a day. The
           input-changed re-ring in the main loop calls this function on the
           guest's behalf, so a host re-ring lands in this counter exactly as a
           guest doorbell does. What the number actually measures is how often
           the endpoint was serviced, whoever asked. */
        if (db == 2 && val == 5) xhci_mouse_doorbells++;
        /* The guest wrote TRBs at the transfer ring address stored in the
           device context's endpoint context. For simplicity, we process
           control transfers (SETUP+DATA+STATUS) by pattern matching. */
        /* Read the device context array to find the endpoint's TR dequeue pointer.
           For now, use a simpler approach: the guest just called usb-control-transfer
           or usb-bulk-in/out, which wrote TRBs and rang this doorbell. We can
           find the TRBs by reading the endpoint context from DCBAAP. */
        if (xcur->dcbaap == 0 || xcur->dcbaap + (db + 1) * 8 > guest_mem_size) return;
        unsigned long long slot_ctx_addr = *(unsigned long long *)((unsigned char *)guest_mem + xcur->dcbaap + db * 8);
        if (slot_ctx_addr == 0 || slot_ctx_addr + 32 * XHCI_CTXSZ > guest_mem_size) return;
        /* Endpoint context for the target endpoint (val & 0xFF is the endpoint index) */
        int ep_idx = val & 0xFF;
        if (ep_idx == 0) ep_idx = 1; /* control endpoint = DCI 1 */
        unsigned char *ep_ctx = (unsigned char *)guest_mem + slot_ctx_addr + ep_idx * XHCI_CTXSZ;
        /* xHCI 4.8.3: a Halted(2) endpoint ignores its doorbell until Reset
           Endpoint; a Stopped(3) one restarts on it, resuming at the saved
           dequeue. Disabled(0) is walked anyway -- contexts written before
           the state field was modelled carry 0 and must keep working. */
        {
            unsigned int ed0 = *(unsigned int *)ep_ctx;
            if ((ed0 & 7) == 2) return;
            if ((ed0 & 7) == 3) *(unsigned int *)ep_ctx = (ed0 & ~7u) | 1;
        }
        unsigned long long dq_raw = *(unsigned long long *)(ep_ctx + 8);
        unsigned long long tr_dequeue = dq_raw & ~0xFULL;
        int ccs = (int)(dq_raw & 1);
        if (bot_db_trace < 0) bot_db_trace = getenv("CODEX_VM_BOT_TRACE") ? 1 : 0;
        if (bot_db_trace)
            fprintf(stderr, "DB: slot=%d dci=%d ctx=0x%llx dq=0x%llx ccs=%d\n",
                    db, ep_idx, (unsigned long long)slot_ctx_addr,
                    (unsigned long long)tr_dequeue, ccs);
        if (tr_dequeue == 0 || tr_dequeue + 16 > guest_mem_size) return;

        /* Device personality by the ROOT PORT the slot was addressed
           against: port 1 = mass storage, port 2 = HID keyboard, port 3 =
           UVC camera, port 4 = hub (matching portsc[] order), with the
           route string separating the hub from the device below it. */
        int kind = xhci_slot_kind(db);

        /* Walk the transfer ring cycle-aware: consume while the TRB cycle
           bit matches the consumer state, follow link TRBs (toggling on
           the TC flag), stop at the producer's edge. */
        for (int safety = 0; safety < 64; safety++) {
            if (tr_dequeue + 16 > guest_mem_size) break;
            unsigned char *trb = (unsigned char *)guest_mem + tr_dequeue;
            unsigned int ctrl = *(unsigned int *)(trb + 12);
            if ((int)(ctrl & 1) != ccs) break;
            int tt = (ctrl >> 10) & 0x3F;
            if (tt == 6) { /* LINK */
                if (ctrl & 2) ccs ^= 1;
                tr_dequeue = *(unsigned long long *)trb & ~0xFULL;
                continue;
            }

            if (tt == 2) { /* SETUP stage */
                unsigned char setup[8];
                memcpy(setup, trb, 8);
                int bmRequestType = setup[0];
                int bRequest = setup[1];
                int wValue = setup[2] | (setup[3] << 8);
                int wIndex = setup[4] | (setup[5] << 8);
                int wLength = setup[6] | (setup[7] << 8);

                /* Hub class requests carry no data stage for SET_FEATURE /
                   CLEAR_FEATURE, so they are acted on here rather than in
                   the data-stage block below. A hub that accepts
                   PORT_POWER and PORT_RESET without changing its port
                   status is a hub whose walk always finds nothing -- the
                   stub-that-agrees-with-itself shape. */
                /* SET_CONFIGURATION on the storage model, answered rather
                   than merely recorded. Completion code for the STATUS
                   stage: 1 success, 6 STALL, 4 USB Transaction Error.

                   A device REFUSES a configuration value it does not have
                   (USB 2.0 9.4.7: a value not matching a
                   bConfigurationValue is a request error, and a control
                   pipe reports a request error by stalling). Sending the
                   descriptor's value is therefore not a nicety, and a
                   model that accepts any value cannot say so. */
                int ctrl_cc = 1;
                if (kind == XHCI_KIND_MSC && bmRequestType == 0x00 && bRequest == 9) {
                    if (usb_setcfg_fault) ctrl_cc = usb_setcfg_fault;
                    else if (usb_setcfg_fault_once) {
                        ctrl_cc = usb_setcfg_fault_once;
                        usb_setcfg_fault_once = 0;   /* transient: the next one lands */
                    }
                    else if (wValue != usb_cfgval) ctrl_cc = 6;
                }

                int tier = xhci_hub_tier(db);
                if (kind == XHCI_KIND_HUB && tier >= 0 &&
                    bmRequestType == 0x23 && wIndex == 1) {
                    if (bRequest == 3) {  /* SET_FEATURE */
                        if (wValue == 8) {          /* PORT_POWER */
                            if (!xcur->hub_powered[tier]) xcur->hub_c_connection[tier] = 1;
                            xcur->hub_powered[tier] = 1;
                        } else if (wValue == 4) {   /* PORT_RESET */
                            xcur->hub_enabled[tier] = 1;
                            xcur->hub_c_reset[tier] = 1;
                        }
                    } else if (bRequest == 1) {  /* CLEAR_FEATURE */
                        if (wValue == 20) xcur->hub_c_reset[tier] = 0;       /* C_PORT_RESET */
                        else if (wValue == 16) xcur->hub_c_connection[tier] = 0; /* C_PORT_CONNECTION */
                    }
                }

                /* HID class SETs (HID 1.11 7.2) and SET_CONFIGURATION are
                   tracked so the class GETs answer the truth and the
                   -hid-idle-quirk arm can key off a zero idle duration. */
                if (kind == XHCI_KIND_HID || kind == XHCI_KIND_HUB_HID) {
                    if (bmRequestType == 0x21) {
                        if (bRequest == 10) {
                            hid_idle_rate = (wValue >> 8) & 0xFF;
                            if (hid_idle_rate == 0) hid_idle_zeroed = 1;
                        } else if (bRequest == 11) {
                            hid_protocol = wValue & 1;
                        }
                    } else if (bmRequestType == 0x00 && bRequest == 9) {
                        hid_configuration = wValue & 0xFF;
                    }
                }

                /* Bulk-Only Mass Storage Reset (USB MSC BOT 3.1): abandon the
                   command in flight and go back to expecting a CBW. Absent
                   from this model until 2026-08-09, so a guest that timed out
                   mid-command could not resynchronise it: bot.active stayed
                   set, and the retry's CBW was consumed as write data for the
                   command it was trying to replace. Reset Recovery therefore
                   had no bed at all. Sense is deliberately left alone -- a BOT
                   reset does not clear a pending UNIT ATTENTION. */
                if (kind == XHCI_KIND_MSC && bmRequestType == 0x21 && bRequest == 0xFF) {
                    fprintf(stderr, "xHCI: BOT Mass Storage Reset (active=%d, data_done=%u) -- abandoning command\n",
                            bot.active, bot.data_done);
                    bot.active = 0;
                    bot.data_done = 0;
                    if (usb_bot_revive && usb_bot_dead) {
                        /* The death is SPENT, not merely lifted. Measured
                           2026-08-21 without this: the target revives, the
                           driver retries the very write that killed it, the
                           length and LBA still match, and it dies again --
                           death, reset, death, forever, and the run never
                           ends. A revive that leaves the trigger armed cannot
                           produce a recovery that SUCCEEDS, which is the only
                           thing this switch exists to make reachable. */
                        fprintf(stderr, "xHCI: -usb-bot-revive-on-reset: the target ANSWERS AGAIN after Bulk-Only Mass Storage Reset; the die trigger is spent\n");
                        usb_bot_dead = 0;
                        usb_bot_die_len = 0;
                        /* Same reason, and it bites harder here: -usb-bot-die-on-nic
                           keys on no property of the write at all, so a revive
                           leaving it armed kills the retry unconditionally. */
                        usb_bot_die_on_nic = 0;
                    }
                }

                /* Optional DATA stage, then the STATUS stage. Both
                   advances follow links -- a TD may straddle the wrap. */
                tr_dequeue = xhci_next_trb(tr_dequeue, &ccs);
                if (tr_dequeue + 16 > guest_mem_size) break;
                unsigned char *data_trb = (unsigned char *)guest_mem + tr_dequeue;
                unsigned int data_ctrl = *(unsigned int *)(data_trb + 12);
                int data_tt = (data_ctrl >> 10) & 0x3F;
                unsigned long long data_buf = *(unsigned long long *)data_trb;
                int data_len = *(unsigned int *)(data_trb + 8) & 0x1FFFF;

                if (data_tt == 3 && data_buf > 0 && data_buf + data_len <= guest_mem_size) {
                    if (bRequest == 6) { /* GET_DESCRIPTOR */
                        int desc_type = (wValue >> 8) & 0xFF;
                        const unsigned char *dev_d = usb_dev_desc;
                        int dev_d_sz = (int)sizeof(usb_dev_desc);
                        const unsigned char *cfg_d = usb_cfg_desc;
                        int cfg_d_sz = (int)sizeof(usb_cfg_desc);
                        if (kind == XHCI_KIND_HID || kind == XHCI_KIND_HUB_HID) { dev_d = usb_hid_dev_desc; dev_d_sz = (int)sizeof(usb_hid_dev_desc); cfg_d = usb_hid_cfg_desc; cfg_d_sz = (int)sizeof(usb_hid_cfg_desc); }
                        if (kind == XHCI_KIND_HID && hid_combo) { cfg_d = usb_hid_combo_cfg_desc; cfg_d_sz = (int)sizeof(usb_hid_combo_cfg_desc); }
                        if (kind == XHCI_KIND_UVC) { dev_d = usb_uvc_dev_desc; dev_d_sz = (int)sizeof(usb_uvc_dev_desc); cfg_d = usb_uvc_cfg_desc; cfg_d_sz = (int)sizeof(usb_uvc_cfg_desc); }
                        if (kind == XHCI_KIND_HUB) { dev_d = tier == 0 ? usb_hub_dev_desc : usb_hub_fs_dev_desc; dev_d_sz = 18; cfg_d = usb_hub_cfg_desc; cfg_d_sz = (int)sizeof(usb_hub_cfg_desc); }
                        if (desc_type == 1) { /* DEVICE */
                            int n = data_len < dev_d_sz ? data_len : dev_d_sz;
                            memcpy((unsigned char *)guest_mem + data_buf, dev_d, n);
                        } else if (desc_type == 2) { /* CONFIG */
                            int n = data_len < cfg_d_sz ? data_len : cfg_d_sz;
                            memcpy((unsigned char *)guest_mem + data_buf, cfg_d, n);
                        } else if (desc_type == 0x22 && (kind == XHCI_KIND_HID || kind == XHCI_KIND_HUB_HID)) { /* HID REPORT */
                            int n = data_len < (int)sizeof(usb_hid_report_desc) ? data_len : (int)sizeof(usb_hid_report_desc);
                            memcpy((unsigned char *)guest_mem + data_buf, usb_hid_report_desc, n);
                        } else if (desc_type == 0x29 && kind == XHCI_KIND_HUB) { /* HUB class */
                            int n = data_len < (int)sizeof(usb_hub_class_desc) ? data_len : (int)sizeof(usb_hub_class_desc);
                            memcpy((unsigned char *)guest_mem + data_buf, usb_hub_class_desc, n);
                        }
                    } else if (bRequest == 0 && bmRequestType == 0xA3 &&
                               kind == XHCI_KIND_HUB && wIndex == 1 && data_len >= 4) {
                        /* GET_PORT_STATUS */
                        unsigned int st = xhci_hub_port_status(xhci_hub_tier(db));
                        memcpy((unsigned char *)guest_mem + data_buf, &st, 4);
                    } else if (bmRequestType == 0xA1 &&
                               (kind == XHCI_KIND_HID || kind == XHCI_KIND_HUB_HID)) {
                        /* HID class GETs (HID 1.11 7.2): GET_REPORT(1) is
                           MANDATORY and returns the live input report over
                           EP0 -- the pipe that keeps working when the
                           interrupt endpoint does not, and therefore the
                           existence proof for a polling fallback.
                           GET_IDLE(2) and GET_PROTOCOL(3) answer tracked
                           state. Absent from this model until 2026-08-03;
                           no device-state instrument was beddable. */
                        if (bRequest == 1 && data_len >= 1) {
                            unsigned char rep[8];
                            if (hid_kbd_carries_keys(kind)) build_hid_keyboard_report(rep);
                            else memset(rep, 0, 8);
                            int n = data_len < 8 ? data_len : 8;
                            memcpy((unsigned char *)guest_mem + data_buf, rep, n);
                        } else if (bRequest == 2 && data_len >= 1) {
                            *((unsigned char *)guest_mem + data_buf) = (unsigned char)hid_idle_rate;
                        } else if (bRequest == 3 && data_len >= 1) {
                            *((unsigned char *)guest_mem + data_buf) = (unsigned char)hid_protocol;
                        }
                    } else if (bmRequestType == 0x80 && bRequest == 8 && data_len >= 1 &&
                               (kind == XHCI_KIND_HID || kind == XHCI_KIND_HUB_HID)) {
                        /* GET_CONFIGURATION (USB2 9.4.2) */
                        *((unsigned char *)guest_mem + data_buf) = (unsigned char)hid_configuration;
                    }
                }
                if (data_tt == 3) tr_dequeue = xhci_next_trb(tr_dequeue, &ccs);
                /* STATUS stage: post the transfer event whether or not a
                   data stage preceded it (SET_CONFIGURATION has none). */
                if (tr_dequeue + 16 <= guest_mem_size) {
                    unsigned char *sts_trb = (unsigned char *)guest_mem + tr_dequeue;
                    unsigned int sts_ctrl = *(unsigned int *)(sts_trb + 12);
                    if (((sts_ctrl >> 10) & 0x3F) == 4 && (int)(sts_ctrl & 1) == ccs) {
                        xhci_post_event_ep(32, db, ep_idx, ctrl_cc, tr_dequeue); /* Transfer Event */
                        /* xHCI 4.8.3: a USB Transaction Error transitions the
                           endpoint to Halted(2), and 4.10.2.1 requires a Reset
                           Endpoint before it will accept a doorbell again. The
                           doorbell handler above already honours Halted(2) and
                           returns; injecting only the completion code left EP0
                           Running, so a bare retry was serviced here while on
                           silicon it gets no completion event at all. */
                        if (ctrl_cc == 4) {
                            unsigned int ed0 = *(unsigned int *)ep_ctx;
                            *(unsigned int *)ep_ctx = (ed0 & ~7u) | 2;
                        }
                        tr_dequeue = xhci_next_trb(tr_dequeue, &ccs);
                    }
                }
                (void)wLength;
                continue;
            } else if (tt == 1) { /* NORMAL (bulk or interrupt transfer) */
                /* xHCI 6.4.1.1: a Data Buffer Pointer has NO alignment
                   requirement. The ~0xF mask belongs on ring bases, input
                   contexts and dequeue pointers -- all 16-byte aligned by
                   spec -- and rounding a data buffer down by it silently
                   reads or writes up to 15 bytes displaced. */
                unsigned long long buf_addr = *(unsigned long long *)trb;
                int buf_len = *(unsigned int *)(trb + 8) & 0x1FFFF;

                /* A periodic endpoint has to be SCHEDULABLE, not merely
                   present. Real silicon places an interrupt endpoint in a
                   periodic schedule computed from its Interval and Max ESIT
                   Payload; leave either at zero and the endpoint is
                   configured, reports success, and is never serviced -- the
                   exact shape of a keyboard that enumerates and then goes
                   silent forever. This model used to deliver regardless,
                   which meant a driver could be wrong here and pass every
                   emulated test. Refuse, and say why: silence that explains
                   itself is worth more than silence that does not. */
                {
                    int ep_type = (*(unsigned int *)(ep_ctx + 4) >> 3) & 7;
                    if (ep_type == 3 || ep_type == 7) {  /* interrupt OUT / IN */
                        int interval = (*(unsigned int *)ep_ctx >> 16) & 0xFF;
                        int esit = (*(unsigned int *)(ep_ctx + 16) >> 16) & 0xFFFF;
                        if (interval == 0 || esit == 0) {
                            fprintf(stderr,
                                "xHCI: slot %d dci %d is an interrupt endpoint that "
                                "cannot be scheduled (Interval=%d, MaxESITPayload=%d); "
                                "not servicing it. Real hardware is silent here.\n",
                                db, ep_idx, interval, esit);
                            break;
                        }
                        /* Non-zero is not the same as right, and the two
                           fields above are the ones a driver most easily gets
                           legally wrong: Interval by taking the wrong speed
                           branch of Table 6-12, Max ESIT Payload by deriving
                           it from the transfer it wants rather than from
                           wMaxPacketSize. Compare both against the descriptor
                           this model serves. Real silicon still schedules a
                           wrong-but-legal value, at the wrong rate, so this
                           reports and does not refuse. */
                        {
                            const unsigned char *epd = xhci_periodic_ep_desc(kind, ep_idx);
                            if (epd && !xcur->periodic_reported[db][ep_idx & 31]) {
                                int b_interval = epd[6];
                                int max_packet = epd[4] | (epd[5] << 8);
                                /* The expectation is computed from what the
                                   device IS, not from the speed the guest
                                   wrote into the slot context. Taking the
                                   guest's word makes a driver that misread
                                   the speed agree with itself, which is the
                                   whole failure this check exists to catch. */
                                int truth = xhci_slot_true_speed(db);
                                int want_id = xhci_speed_id(truth);
                                int want_i = xhci_expected_interval(truth, b_interval);
                                int want_e = xhci_expected_esit(max_packet);
                                xcur->periodic_reported[db][ep_idx & 31] = 1;
                                fprintf(stderr,
                                    "xHCI: slot %d dci %d speed: PORTSC reports %d for a "
                                    "%s-speed device, slot context says %d : %s\n",
                                    db, ep_idx, want_id,
                                    truth == 1 ? "full" : truth == 2 ? "low" :
                                    truth == 3 ? "high" : truth == 4 ? "super" : "?",
                                    xcur->slot_speed[db],
                                    want_id == xcur->slot_speed[db] ? "MATCH" : "MISMATCH");
                                fprintf(stderr,
                                    "xHCI: slot %d dci %d periodic check: "
                                    "bInterval %d -> Interval want %d got %d : %s ; "
                                    "wMaxPacketSize %d -> MaxESITPayload want %d got %d : %s\n",
                                    db, ep_idx,
                                    b_interval, want_i, interval,
                                    want_i == interval ? "MATCH" : "MISMATCH",
                                    max_packet, want_e, esit,
                                    want_e == esit ? "MATCH" : "MISMATCH");
                            }
                        }
                        /* A full- or low-speed device below a high-speed hub
                           reaches the bus only through that hub's TRANSACTION
                           TRANSLATOR, and the controller is told which one by
                           the TT Hub Slot ID / TT Port Number in the slot
                           context. Leave them zero and there is no split
                           schedule to put the endpoint in: control transfers
                           to endpoint zero still work, because the hub
                           handles those itself, while the periodic endpoint
                           is never serviced. That asymmetry -- enumerates
                           perfectly, then silent forever -- is precisely the
                           reported shape of the keyboard on the ASUS. This
                           model used to deliver anyway, so the one hypothesis
                           left standing was the one it could not test. */
                        /* Judged on what the device IS. A driver that misread
                           the Port Speed ID and therefore skipped the TT is
                           exactly the case worth catching, and testing its
                           own claim would let it off. */
                        if ((xhci_slot_true_speed(db) == 1 || xhci_slot_true_speed(db) == 2) &&
                            xcur->slot_route[db] != 0 && xcur->slot_tt_hub[db] == 0) {
                            fprintf(stderr,
                                "xHCI: slot %d dci %d is a periodic endpoint on a "
                                "%s-speed device at route 0x%X with no transaction "
                                "translator (TT Hub Slot ID=0); not servicing it. "
                                "Real hardware is silent here.\n",
                                db, ep_idx,
                                xhci_slot_true_speed(db) == 1 ? "full" : "low",
                                xcur->slot_route[db]);
                            break;
                        }
                    }
                }

                /* HID keyboard interrupt IN -- return 8-byte boot report.
                   Real controllers raise a transfer event for any TRB that
                   asks (IOC); a driver awaiting the event instead of
                   re-reading the buffer must not hang here. */
                if ((kind == XHCI_KIND_HID || kind == XHCI_KIND_HUB_HID) &&
                    buf_addr > 0 && buf_addr + 8 <= guest_mem_size) {
                    /* NAK arm: no DMA, no event, no dequeue advance -- the
                       TD is in progress and stays there. Breaking the walk
                       leaves the context dequeue at this TRB, which is what
                       a stop's write-back then publishes, exactly as the
                       ASUS did. */
                    if (xhci_hid_nak || (hid_idle_quirk && hid_idle_zeroed)) break;
                    unsigned char report[8];
                    int is_combo_mouse = (kind == XHCI_KIND_HID && hid_combo && ep_idx == 5);
                    if (hid_nak_unchanged) {
                        if (hid_nak_tracing() && !hid_nak_seen[db][ep_idx & 31] && hid_nak_dbg < 8) {
                            hid_nak_dbg++;
                            fprintf(stderr, "HIDNAK: slot %d ep %d first service (kind=%d)\n", db, ep_idx, kind);
                        }
                        hid_nak_seen[db][ep_idx & 31] = 1;
                        hid_nak_ctl = xcur;
                        /* The mouse builder consumes the delta and the press
                           latch, so its NAK gate is the sample-freshness flag
                           checked BEFORE building, not a byte compare after. */
                        if (is_combo_mouse && !hid_mouse_fresh) break;
                    }
                    if (is_combo_mouse) {
                        memset(report, 0, 8);
                        build_hid_mouse_report(report);
                        hid_mouse_reports++;
                        /* A clamped delta leaves a remainder; stay fresh so
                           the next TRB drains it as silicon would. */
                        if (hid_nak_unchanged)
                            hid_mouse_fresh = (pending_mouse_abs_x != hid_mouse_last_x ||
                                               pending_mouse_abs_y != hid_mouse_last_y);
                    }
                    else if (hid_kbd_carries_keys(kind)) build_hid_keyboard_report(report);
                    else memset(report, 0, 8);
                    if (hid_nak_unchanged && !is_combo_mouse) {
                        if (!memcmp(report, hid_nak_last[db][ep_idx & 31], 8)) break;
                        memcpy(hid_nak_last[db][ep_idx & 31], report, 8);
                    }
                    if ((report[0] || report[2]) && hid_first_report_logged < 4) {
                        hid_first_report_logged++;
                        fprintf(stderr, "HID: nonzero report -> slot=%d ep=%d buf=0x%llx key=%02x\n",
                                db, ep_idx, (unsigned long long)buf_addr, report[2]);
                    }
                    int n = buf_len < 8 ? buf_len : 8;
                    memcpy((unsigned char *)guest_mem + buf_addr, report, n);
                    if (ctrl & 0x20) xhci_post_event_ep(32, db, ep_idx, 1, tr_dequeue);
                    tr_dequeue += 16;
                    continue;
                }

                /* CODEX_VM_BOT_TRACE=1 narrates the Bulk-Only Transport path.
                   A driver whose command block is rejected looks identical to
                   one whose endpoint was never configured -- both are silence
                   -- and this is what tells them apart. Sampled once, so the
                   trace costs a branch per transfer and not a getenv. */
                static int bot_trace = -1;
                if (bot_trace < 0) bot_trace = getenv("CODEX_VM_BOT_TRACE") ? 1 : 0;
                if (bot_trace)
                    fprintf(stderr, "BOT: slot=%d dci=%d kind=%d disk=%d buf=0x%llx len=%d active=%d\n",
                            db, ep_idx, kind, ide.data ? 1 : 0,
                            (unsigned long long)buf_addr, buf_len, bot.active);
                if (kind == 1 && ide.data && buf_addr > 0 &&
                    buf_addr + (unsigned long long)buf_len <= guest_mem_size) {
                    unsigned char *buf = (unsigned char *)guest_mem + buf_addr;
                    int ring_is_in = (ep_idx >= 2) && (ep_idx & 1); /* odd DCI = IN endpoint */
                    if (!ring_is_in) {
                        /* Bulk OUT ring: a CBW, or write data following one */
                        if (!bot.active && buf_len >= 31 && *(unsigned int *)buf == 0x43425355) {
                            bot.active = 1;
                            bot.tag = *(unsigned int *)(buf + 4);
                            bot.xfer_len = *(unsigned int *)(buf + 8);
                            bot.dir_in = (buf[12] & 0x80) != 0;
                            memcpy(bot.cb, buf + 15, 16);
                            bot.data_done = 0;
                            bot.csw_status = 0;
                            /* -usb-bot-census: one line per command, with the
                               LBA and the declared length. SITES, NOT TOTALS.
                               -usb-bot-die-len cannot be aimed at the sink
                               because the sink and the bank both issue
                               32768-byte writes, and no total says where
                               either of them lands. */
                            if (usb_bot_census) {
                                unsigned char op = bot.cb[0];
                                if (op == 0x2A || op == 0x28) {
                                    unsigned int lba = ((unsigned int)bot.cb[2] << 24) | ((unsigned int)bot.cb[3] << 16) |
                                                       ((unsigned int)bot.cb[4] << 8) | bot.cb[5];
                                    fprintf(census_out(), "BOT-CENSUS: %s lba=%u len=%u\n",
                                            op == 0x2A ? "write" : "read", lba, bot.xfer_len);
                                    fflush(census_out());
                                }
                            }
                            /* Unknown commands fail cleanly: report CHECK
                               CONDITION and consume the declared transfer. */
                            {
                                unsigned char op = bot.cb[0];
                                if (op != 0x00 && op != 0x03 && op != 0x12 &&
                                    op != 0x25 && op != 0x28 && op != 0x2A) {
                                    bot.csw_status = 1;
                                    bot.data_done = bot.xfer_len;
                                }
                                /* A pending UNIT ATTENTION refuses everything
                                   except REQUEST SENSE, which is how the host
                                   is meant to clear it. SPC: the condition
                                   persists until the sense data is read. */
                                else if (bot.sense_key && op != 0x03) {
                                    bot.csw_status = 1;
                                    bot.data_done = bot.xfer_len;
                                }
                            }
                        } else if (bot.active && !bot.dir_in && bot.data_done < bot.xfer_len) {
                            unsigned int n = (unsigned int)buf_len;
                            if (n > bot.xfer_len - bot.data_done) n = bot.xfer_len - bot.data_done;
                            if (bot.cb[0] == 0x2A) { /* WRITE_10 */
                                unsigned int lba = ((unsigned int)bot.cb[2] << 24) | ((unsigned int)bot.cb[3] << 16) |
                                                   ((unsigned int)bot.cb[4] << 8) | bot.cb[5];
                                unsigned long long disk_off = (unsigned long long)lba * 512 + bot.data_done;
                                if (disk_off + n <= ide.size) {
                                    memcpy(ide.data + disk_off, buf, n);
                                    ide_flush(&ide, (size_t)disk_off, n); /* durable, like IDE writes */
                                } else {
                                    bot.csw_status = 1;
                                    fprintf(stderr, "BOT: WRITE_10 out of range lba=%u done=%u n=%u\n", lba, bot.data_done, n);
                                }
                            } else {
                                bot.csw_status = 1; /* OUT data for a non-write command */
                                fprintf(stderr, "BOT: OUT data for op=%02x n=%u\n", bot.cb[0], n);
                            }
                            bot.data_done += n;
                        }
                    } else if (bot.active) {
                        /* Bulk IN ring: the data phase of an IN command, then the CSW */
                        if (bot.dir_in && bot.data_done < bot.xfer_len) {
                            unsigned char op = bot.cb[0];
                            unsigned int n = (unsigned int)buf_len;
                            if (n > bot.xfer_len - bot.data_done) n = bot.xfer_len - bot.data_done;
                            if (op == 0x28) { /* READ_10 */
                                unsigned int lba = ((unsigned int)bot.cb[2] << 24) | ((unsigned int)bot.cb[3] << 16) |
                                                   ((unsigned int)bot.cb[4] << 8) | bot.cb[5];
                                unsigned long long disk_off = (unsigned long long)lba * 512 + bot.data_done;
                                if (disk_off + n <= ide.size) memcpy(buf, ide.data + disk_off, n);
                                else bot.csw_status = 1;
                            } else if (op == 0x12) { /* INQUIRY */
                                unsigned char inq[36] = {0};
                                inq[0] = 0x00; /* direct access block device */
                                inq[1] = 0x80; /* removable */
                                inq[2] = 0x05; /* SPC-3 */
                                inq[4] = 31;   /* additional length */
                                memcpy(inq + 8, "Codex   ", 8);
                                memcpy(inq + 16, "Virtual Disk    ", 16);
                                memcpy(inq + 32, "1.0 ", 4);
                                memcpy(buf, inq, n < 36 ? n : 36);
                            } else if (op == 0x25) { /* READ_CAPACITY_10 */
                                unsigned int last_lba = (unsigned int)(ide.size / 512) - 1;
                                unsigned char cap[8];
                                cap[0] = (last_lba >> 24) & 0xFF;
                                cap[1] = (last_lba >> 16) & 0xFF;
                                cap[2] = (last_lba >> 8) & 0xFF;
                                cap[3] = last_lba & 0xFF;
                                cap[4] = 0; cap[5] = 0; cap[6] = 2; cap[7] = 0; /* 512 bytes */
                                memcpy(buf, cap, n < 8 ? n : 8);
                            } else if (op == 0x03) { /* REQUEST_SENSE */
                                unsigned char sense[18] = {0};
                                sense[0] = 0x70; /* current, fixed format */
                                sense[2] = (unsigned char)bot.sense_key;
                                sense[7] = 10;   /* additional length */
                                sense[12] = (unsigned char)bot.sense_asc;
                                sense[13] = (unsigned char)bot.sense_ascq;
                                memcpy(buf, sense, n < 18 ? n : 18);
                                /* Reading it is what clears it. A model that
                                   answered the key and kept the condition
                                   would loop the host forever. */
                                bot.sense_key = 0; bot.sense_asc = 0; bot.sense_ascq = 0;
                            }
                            bot.data_done += n;
                        } else if (buf_len >= 13) {
                            unsigned char csw[13] = {0};
                            *(unsigned int *)csw = 0x53425355; /* CSW signature */
                            *(unsigned int *)(csw + 4) = bot.tag;
                            *(unsigned int *)(csw + 8) = bot.xfer_len - bot.data_done; /* residue */
                            csw[12] = (unsigned char)bot.csw_status;
                            memcpy(buf, csw, 13);
                            bot.active = 0;
                        }
                    }
                    /* Real controllers raise a transfer event for every
                       TRB that asks for one (IOC, bit 5). The BOT driver
                       drives CBW, data, and CSW as three separate awaited
                       transfers, so each must complete with its own event. */
                    if (ctrl & 0x20) xhci_post_event_ep(32, db, ep_idx, 1, tr_dequeue);
                }
                tr_dequeue += 16;
            } else if (tt == 5) { /* ISOCH (isochronous transfer) */
                unsigned long long buf_addr = *(unsigned long long *)trb;
                int buf_len = *(unsigned int *)(trb + 8) & 0x1FFFF;
                int copied = 0;
                /* UVC camera: write test pattern frame data */
                if (kind == XHCI_KIND_UVC && buf_addr > 0 && buf_addr + buf_len <= guest_mem_size) {
                    static unsigned char uvc_frame[UVC_FRAME_SIZE];
                    static int uvc_frame_ready = 0;
                    if (!uvc_frame_ready) {
                        uvc_generate_test_frame(uvc_frame);
                        uvc_frame_ready = 1;
                    }
                    int n = buf_len < UVC_FRAME_SIZE ? buf_len : UVC_FRAME_SIZE;
                    memcpy((unsigned char *)guest_mem + buf_addr, uvc_frame, n);
                    copied = n;
                }
                /* An isochronous TRB that asks for an event gets one, exactly
                   as bulk and interrupt do. Without this the data lands and
                   nothing says so, and a driver that waits for its completion
                   waits forever -- the stub-that-agrees-with-itself shape,
                   which is why no isochronous driver could be written against
                   this model. Short packets report the residual in the event,
                   so a frame smaller than the buffer is distinguishable from
                   a full one. */
                if (ctrl & 0x20) {
                    int residual = buf_len - copied;
                    xhci_post_event_ep(32, db, ep_idx, residual > 0 ? 13 : 1, tr_dequeue);
                }
                tr_dequeue += 16;
            } else {
                tr_dequeue += 16;
            }
        }
        /* Update endpoint context TR dequeue pointer with the consumer
           cycle state, so the next doorbell resumes exactly here. */
        *(unsigned long long *)(ep_ctx + 8) = tr_dequeue | (unsigned long long)ccs;
    }
}

/* Reset ONE controller. HCRST reaches this, and a host-controller reset on
   the ASMedia must not touch the Intel: wiping both is what a single global
   state made look like a driver defect, and it is not one. Ordinal 0 carries
   every modelled device; ordinal 1 is the ASMedia with nothing on it. */
static void xhci_reset_ctl(struct xhci_state *x) {
    int ord = (int)(x - xhci_ctl);
    memset(x, 0, sizeof(*x));
    x->usbsts = 1;            /* HCH (halted) */
    x->next_slot = 1;
    x->legacy_bios_owned = 1; /* firmware owns it until a driver claims it */
    x->legacy_os_owned = 0;
    if (ord != 0) return;
    memset(&bot, 0, sizeof(bot));
    /* A controller reset raises the target's power-on condition, and this
       is the ONLY correct place to arm it: the guest issues HCRST during
       bring-up, so arming at init alone is wiped before the first command.
       ASC 0x29 says so in as many words -- "power on, RESET, or bus device
       reset occurred". Caught by a sabotage arm that should have failed
       and did not. */
    if (usb_unit_attention) { bot.sense_key = 0x06; bot.sense_asc = 0x29; bot.sense_ascq = 0x00; }
    {
    struct xhci_state *xcur = x;    /* XUSB2PR and USB3PSSEN are NOT reset here. They live in PCI config
       space, HCRST reaches this function, and a host-controller reset does
       not un-route a port on real silicon -- a driver that routes and then
       resets would lose the bus. They start at zero as statics, which is
       firmware leaving every routable port with the companion. */
    xcur->portsc[0] = 1 | (4 << 10); /* CCS=1 (connected), speed=4 (SuperSpeed) -- mass storage */
    /* The HID keyboard is FULL SPEED, because that is what a real boot
       keyboard is and what the machine this driver failed on reported.
       It was HighSpeed here for no reason but convenience, and that is
       exactly the difference that let a driver pass in emulation and go
       silent on metal: full speed selects a different endpoint Interval
       encoding and, behind a high-speed hub, needs a transaction
       translator the driver never names. */
    xcur->portsc[1] = 1 | (1 << 10); /* CCS=1 (connected), speed=1 (FullSpeed) -- HID keyboard */
    xcur->portsc[2] = 1 | (3 << 10); /* CCS=1 (connected), speed=3 (HighSpeed) -- UVC camera */
    /* A HIGH-SPEED HUB, with a FULL-SPEED keyboard behind it. This is the
       topology the driver has never been run against: everything else on
       this controller is root-attached, so the hub walk in
       apps/works/GopUsb.codex -- the code the real boot payload actually
       runs, unlike the root-only walk in GopUsbKbd -- had no model to
       execute against at all. A device here is reachable only through a
       route string, and being full speed under a high-speed parent, only
       through that hub's transaction translator. */
    xcur->portsc[3] = 1 | (3 << 10); /* CCS=1 (connected), speed=3 (HighSpeed) -- hub */
    /* The bus walk takes the first keyboard it finds, and root port 2 comes
       before the hub on port 4 -- so with both present the hub-attached
       keyboard is never reached. Unplugging the root one makes the hub the
       only route to a keyboard, which is what lets one test drive the hub
       path with the SAME guest binary. The topology is the variable. */
    if (xhci_no_root_kbd) xcur->portsc[1] = 0;
    if (xhci_no_disk) xcur->portsc[0] = 0;
    /* The truth behind the reported IDs, in the same order. */
    xhci_port_true_speed[0] = 4;
    xhci_port_true_speed[1] = xhci_no_root_kbd ? 0 : 1;
    xhci_port_true_speed[2] = 3;
    xhci_port_true_speed[3] = 3;
    /* Ports above the four modelled ones are empty and POWERED, which is
       what a real wide controller reports and what makes them dangerous
       to a guest that overruns a table: PP alone makes PORTSC non-zero,
       so the overrunning write lands a truthy value on whatever follows.
       Leaving these at zero would reproduce the overrun and hide its
       consequence, which is the failure mode this model exists to expose. */
    for (int p = XHCI_MODELLED_PORTS; p < XHCI_MAX_PORTS; p++) {
        xcur->portsc[p] = 1u << 9;   /* PP=1, CCS=0 */
        xhci_port_true_speed[p] = 0;
    }
    /* Carry the storage device to another root port. Its old port goes
       dark rather than staying connected, because a stick that answers on
       two ports at once is not a machine anyone has. */
    if (usb_disk_port != 1 && usb_disk_port >= 1 && usb_disk_port <= XHCI_MAX_PORTS) {
        unsigned int was = xcur->portsc[0];
        int wasspd = xhci_port_true_speed[0];
        xcur->portsc[0] = 1u << 9;
        xhci_port_true_speed[0] = 0;
        xcur->portsc[usb_disk_port - 1] = was;
        xhci_port_true_speed[usb_disk_port - 1] = wasspd;
    }
    }
}

static void xhci_init(void) {
    xhci_reset_ctl(&xhci_ctl[0]);
    xhci_reset_ctl(&xhci_ctl[1]);
    xcur = &xhci_ctl[0];
}
static unsigned long long hpet_raw(void);
static unsigned int xhci_read(unsigned long long offset) {
    if (offset < XHCI_CAP_LEN) {
        switch ((int)offset) {
        case 0:  return XHCI_CAP_LEN | (0x0100 << 16);  /* CAPLENGTH=32, HCIVERSION=1.0 */
        case 4:  return ((unsigned)xhci_num_ports << 24) | XHCI_MAX_SLOTS;  /* HCSPARAMS1 */
        case 8:  return 0x0F    /* HCSPARAMS2 */
                      | (((unsigned)(xhci_scratch_bufs >> 5) & 31) << 21)   /* Max Scratchpad Bufs Hi */
                      | (((unsigned)xhci_scratch_bufs & 31) << 27);         /* Max Scratchpad Bufs Lo */
        case 12: return 0;      /* HCSPARAMS3 */
        /* HCCPARAMS1. AC64 (bit 0) is now set and honest: every pointer this
           model reads out of a context or a TRB is taken as 64-bit already,
           so advertising 32-bit-only was a claim the code did not honour.
           Bits 31:16 are xECP, the extended-capability offset in DWORDS from
           the BAR base; zero used to say "no extended capabilities", which is
           what stops a driver from ever finding USB Legacy Support and taking
           ownership from firmware. It points at BAR+0x80 now. */
        case 16: return (XHCI_XECP_DWORDS << 16) | 0x21 | (xhci_csz64 ? 4u : 0u); /* +CSZ under -xhci-csz */
        case 20: return 0x800;  /* DBOFF: doorbell array at offset 2048 */
        case 24: return 0x1000; /* RTSOFF: runtime regs at offset 4096 --
                                   clear of the port array at op+0x400,
                                   which with CAPLENGTH=32 spans absolute
                                   0x420-0x460 and used to shadow the
                                   interrupter registers entirely */
        default: return 0;
        }
    }
    /* Extended capabilities, decoded before the operational block because
       they sit inside its address range. */
    if (offset >= XHCI_XECP_OFF && offset < XHCI_XECP_OFF + 8) {
        if (offset == XHCI_XECP_OFF)
            return 1                                        /* cap ID: USB Legacy Support */
                 | (4u << 8)                                /* next: +4 dwords, Supported Protocol */
                 | ((xcur->legacy_bios_owned ? 1u : 0u) << 16)
                 | ((xcur->legacy_os_owned ? 1u : 0u) << 24);
        return 0;  /* USBLEGCTLSTS: no SMI sources modelled */
    }
    /* Supported Protocol Capability (xHCI 7.2). Required on real silicon and
       absent here until now, which is why nothing could read a speed mapping
       rather than assume one. PSIC is zero unless -xhci-psi, and zero is the
       spec's own condition for Table 7-13's defaults applying. */
    if (offset >= XHCI_XECP_OFF + 16 && offset < XHCI_XECP_OFF + 16 + 4 * (4 + 4)) {
        int d = (int)(offset - (XHCI_XECP_OFF + 16)) / 4;
        int psic = xhci_psi ? 4 : 0;
        switch (d) {
        case 0: return 2u | (0u << 8) | (0u << 16) | (3u << 24); /* id 2, end of list, USB 3.0 */
        case 1: return 0x20425355u;                              /* name string "USB " */
        case 2: return 1u | ((unsigned)xhci_num_ports << 8) | ((unsigned)psic << 28);
        case 3: return 0;                                        /* protocol slot type 0 */
        }
        if (!psic) return 0;
        /* PSI dword: PSIV 3:0, PSIE 5:4 (2 = Mb/s, 1 = Kb/s, 3 = Gb/s),
           PSIM 31:16. The rates are the real ones for each class, so a driver
           that reads the mantissa rather than the id still lands correctly. */
        switch (d) {
        case 4: return (12u   << 16) | (2u << 4) | XHCI_PSI_FULL;   /* 12 Mb/s */
        case 5: return (1500u << 16) | (1u << 4) | XHCI_PSI_LOW;    /* 1500 Kb/s */
        case 6: return (480u  << 16) | (2u << 4) | XHCI_PSI_HIGH;   /* 480 Mb/s */
        case 7: return (5u    << 16) | (3u << 4) | XHCI_PSI_SUPER;  /* 5 Gb/s */
        }
        return 0;
    }
    unsigned long long op_off = offset - XHCI_CAP_LEN;
    if (op_off < 0x400) {
        switch ((int)op_off) {
        case 0:  return xcur->usbcmd;
        case 4:  return xcur->usbsts;
        case 8:  return 1;  /* PAGESIZE: 4KB */
        case 20: return xcur->dnctrl;
        case 24: return (unsigned int)(xcur->crcr & 0xFFFFFFFF);
        case 28: return (unsigned int)(xcur->crcr >> 32);
        case 48: return (unsigned int)(xcur->dcbaap & 0xFFFFFFFF);
        case 52: return (unsigned int)(xcur->dcbaap >> 32);
        case 56: return xcur->config;
        default: return 0;
        }
    }
    if (op_off >= 0x400 && op_off < 0x400 + xhci_num_ports * 16) {
        int port = (int)(op_off - 0x400) / 16;
        int preg = (int)(op_off - 0x400) % 16;
        /* A port still owned by the companion EHCI is dark to the xHCI: the
           whole PORTSC reads zero, not merely CCS. */
        if (xhci_intel && (XHCI_XUSB2PRM & (1u << port)) &&
            !(xhci_xusb2pr & (1u << port))) return 0;
        if (preg == 0) {
            unsigned int v = xcur->portsc[port];
            if (xhci_psi && xhci_port_true_speed[port])
                v = (v & ~(0xFu << 10)) |
                    ((unsigned)xhci_speed_id(xhci_port_true_speed[port]) << 10);
            return v;
        }
        return 0;
    }
    /* Runtime registers at offset 0x1000 (RTSOFF) */
    if (offset == 0x1000) {
        /* MFINDEX (5.5.1 p.423): bits 13:0, one count per 125 us microframe
           while the controller runs; it may stop only when every root port
           is Disconnected/Disabled/Training/Powered-off (4.14.2 p.260).
           Tied to host time so a guest sampling it twice across a real
           delay observes real advance -- the SCHEDX instrument that asks
           whether frames exist at all. Absent from this model until
           2026-08-03; it read 0 and a bed could not tell a dead frame
           counter from a live one. */
        if (!(xcur->usbcmd & 1)) return 0;
        return (unsigned int)(((hpet_raw() * 8000ULL) / 14318180ULL) & 0x3FFF);
    }
    /* Interrupter 0 at RTSOFF + 0x20 */
    if (offset >= 0x1020 && offset < 0x1040) {
        int ireg = (int)(offset - 0x1020);
        if (ireg == 0) return 0; /* IMAN */
        if (ireg == 4) return 0; /* IMOD */
        if (ireg == 8) return xcur->er_size; /* ERSTSZ */
        if (ireg == 16) return (unsigned int)(xcur->erstba & 0xFFFFFFFF); /* ERSTBA lo */
        if (ireg == 20) return (unsigned int)(xcur->erstba >> 32); /* ERSTBA hi */
        if (ireg == 24) return (unsigned int)(xcur->er_addr + xcur->erdp_idx * 16); /* ERDP lo */
        if (ireg == 28) return 0; /* ERDP hi */
    }
    return 0;
}

static void xhci_write(unsigned long long offset, unsigned int val) {
    if (offset < XHCI_CAP_LEN) return;
    /* The legacy-support handoff: a driver sets the OS-owned bit, and
       firmware answers by dropping its own. Both bits are real state, so a
       driver that polls for the release sees it happen. */
    if (offset >= XHCI_XECP_OFF && offset < XHCI_XECP_OFF + 8) {
        if (offset == XHCI_XECP_OFF && (val & (1u << 24))) {
            xcur->legacy_os_owned = 1;
            xcur->legacy_bios_owned = 0;
        }
        return;
    }
    unsigned long long op_off = offset - XHCI_CAP_LEN;
    if (op_off < 0x400) {
        switch ((int)op_off) {
        case 0: {
            if (val & 2) { xhci_reset_ctl(xcur); xcur->usbsts &= ~1; return; }
            int was_running = xcur->usbcmd & 1;
            xcur->usbcmd = val;
            if (val & 1) xcur->usbsts &= ~1; else xcur->usbsts |= 1;
            /* Run transition: a connect that predates Run posts its Port
               Status Change Event now, one per connected port -- 26 of them
               on the board this model chases. -xhci-evt-flood N stacks N
               more, which is what marches the producer through the ring
               WRAP and, unconsumed, into the FULL condition. */
            if (!was_running && (val & 1)) {
                for (int p = 0; p < xhci_num_ports; p++)
                    if (xcur->portsc[p] & 1)
                        xhci_post_event(34, 0, 1, (unsigned long long)(p + 1) << 24);
                for (int f = 0; f < xhci_evt_flood; f++)
                    xhci_post_event(34, 0, 1, 1ULL << 24);
            }
            break;
        }
        case 4:  xcur->usbsts &= ~val; break;
        case 20: xcur->dnctrl = val; break;
        case 24:
            xcur->crcr = (xcur->crcr & 0xFFFFFFFF00000000ULL) | val;
            xcur->cr_pos = xcur->crcr & ~0x3FULL;
            xcur->cr_ccs = (int)(xcur->crcr & 1);
            break;
        case 28:
            xcur->crcr = (xcur->crcr & 0xFFFFFFFFULL) | ((unsigned long long)val << 32);
            xcur->cr_pos = xcur->crcr & ~0x3FULL;
            xcur->cr_ccs = (int)(xcur->crcr & 1);
            break;
        case 48: xcur->dcbaap = (xcur->dcbaap & 0xFFFFFFFF00000000ULL) | val; break;
        case 52: xcur->dcbaap = (xcur->dcbaap & 0xFFFFFFFFULL) | ((unsigned long long)val << 32); break;
        case 56: xcur->config = val; break;
        }
        return;
    }
    if (op_off >= 0x400 && op_off < 0x400 + xhci_num_ports * 16) {
        int port = (int)(op_off - 0x400) / 16;
        int preg = (int)(op_off - 0x400) % 16;
        if (preg == 0) {
            /* PORTSC: change bits 17-23 are write-1-to-clear; a port
               reset (PR, bit 4) completes instantly -- the device is
               virtual -- leaving the port enabled with PRC latched.
               Writing PED=1 outside a reset disables the port (RW1C). */
            unsigned int old = xcur->portsc[port];
            old &= ~(val & 0x00FE0000u);
            if (val & 0x10u) old |= 0x2u | (1u << 21);
            else if (val & 0x2u) old &= ~0x2u;
            xcur->portsc[port] = old;
            /* A completed reset posts a Port Status Change Event (type 34,
               port id in parameter bits 31:24), the way silicon does. This
               model never posted one before, so the guest's consumption of
               unsolicited events -- the skip path in its waits, and the ring
               WRAP once enough of them accumulate -- had no bed at all. */
            if ((val & 0x10u) && (xcur->usbcmd & 1))
                xhci_post_event(34, 0, 1, (unsigned long long)(port + 1) << 24);
        }
        return;
    }
    /* Runtime / Interrupter 0 registers */
    if (offset >= 0x1020 && offset < 0x1040) {
        int ireg = (int)(offset - 0x1020);
        if (ireg == 8) xcur->er_size = (unsigned short)val; /* ERSTSZ */
        if (ireg == 16) { /* ERSTBA lo */
            xcur->erstba = (xcur->erstba & 0xFFFFFFFF00000000ULL) | val;
            /* Read event ring segment table entry 0 to get ring address+size */
            if (xcur->erstba > 0 && xcur->erstba + 16 <= guest_mem_size) {
                unsigned char *erst = (unsigned char *)guest_mem + xcur->erstba;
                xcur->er_addr = *(unsigned long long *)erst;
                xcur->er_size = *(unsigned short *)(erst + 8);
                xcur->erdp_idx = 0;
                xcur->er_ccs = 1;
            }
        }
        if (ireg == 20) xcur->erstba = (xcur->erstba & 0xFFFFFFFFULL) | ((unsigned long long)val << 32);
        if (ireg == 24) xcur->guest_erdp = (xcur->guest_erdp & 0xFFFFFFFF00000000ULL) | (val & ~0xFu);
        if (ireg == 28) xcur->guest_erdp = (xcur->guest_erdp & 0xFFFFFFFFULL) | ((unsigned long long)val << 32);
        return;
    }
    if (offset >= 0x800 && offset < 0xC00) {
        int db = (int)(offset - 0x800) / 4;
        /* Serialised against the HID service thread, which walks the same
           transfer rings on behalf of a periodic endpoint. */
        xhci_db_lock_enter();
        xhci_handle_doorbell(db, val);
        xhci_db_lock_leave();
    }
}

/* ══ Intel HDA Audio Controller ══ */
#define HDA_BAR       0xFE000000ULL
#define HDA_BAR_SIZE  0x4000

static struct {
    unsigned int gctl;
    unsigned short wakeen, statests;
    unsigned int intctl, intsts;
    unsigned int corblbase, corbubase;
    unsigned short corbwp, corbrp;
    unsigned char corbctl, corbsts, corbsize;
    unsigned int rirblbase, rirbubase;
    unsigned short rirbwp, rintcnt;
    unsigned char rirbctl, rirbsts, rirbsize;
    /* Stream descriptor 0 (output) */
    unsigned int sd0ctl;
    unsigned char sd0sts;
    unsigned int sd0lpib, sd0cbl;
    unsigned short sd0lvi, sd0fmt;
    unsigned int sd0bdpl, sd0bdpu;
    /* Stream descriptor 1 (input / microphone) at BAR offset 0xA0 */
    unsigned int sd1ctl;
    unsigned char sd1sts;
    unsigned int sd1lpib, sd1cbl;
    unsigned short sd1lvi, sd1fmt;
    unsigned int sd1bdpl, sd1bdpu;
} hda;

static unsigned int hda_codec_verb(unsigned int verb) {
    int nid = (verb >> 20) & 0x7F;
    int vid = (verb >> 8) & 0xFFF;
    int payload = verb & 0xFF;
    if (vid == 0xF00) { /* GET_PARAMETER */
        if (nid == 0) { /* root */
            if (payload == 0x00) return 0x10DE0001;  /* vendor ID */
            if (payload == 0x02) return 0x00100001;  /* revision */
            if (payload == 0x04) return 0x00010001;  /* sub-node: start=1, count=1 */
        }
        if (nid == 1) { /* audio function group */
            if (payload == 0x04) return 0x00020004;  /* sub-node: start=2, count=4 */
            if (payload == 0x05) return 0x00000001;  /* fn group type: audio */
            if (payload == 0x0A) return 0x0001;      /* PCM sizes: 16-bit */
            if (payload == 0x0B) return 0x0001;      /* PCM rates: 48kHz */
        }
        if (nid == 2) { /* DAC (audio output) */
            if (payload == 0x09) return 0x00000011;  /* widget cap: type=0 (output), stereo */
            if (payload == 0x0A) return 0x0001;
            if (payload == 0x0B) return 0x0001;
        }
        if (nid == 3) { /* ADC (audio input) */
            if (payload == 0x09) return 0x00100011;  /* widget cap: type=1 (input), stereo */
            if (payload == 0x0E) return 1;           /* connection list length: 1 */
        }
        if (nid == 4) { /* output pin (speaker) */
            if (payload == 0x09) return 0x00400001;  /* widget cap: type=4 (pin), out */
            if (payload == 0x0C) return 0x00000010;  /* pin cap: output capable */
            if (payload == 0x0E) return 1;           /* connection list: 1 entry */
        }
        if (nid == 5) { /* input pin (microphone) */
            if (payload == 0x09) return 0x00400001;  /* widget cap: type=4 (pin), in */
            if (payload == 0x0C) return 0x00000020;  /* pin cap: input capable */
        }
        return 0;
    }
    if (vid == 0xF02) { /* GET_CONNECTION_LIST_ENTRY */
        if (nid == 4) return 2;  /* output pin → DAC (nid 2) */
        if (nid == 3) return 5;  /* ADC → input pin (nid 5) */
        return 0;
    }
    if (vid == 0xF07) return 0x40;  /* GET_PIN_WIDGET_CONTROL: OUT enabled */
    return 0;
}

static void hda_process_corb(void) {
    if (!(hda.corbctl & 2)) return;  /* DMA not running */
    unsigned long long corb_addr = (unsigned long long)hda.corbubase << 32 | hda.corblbase;
    unsigned long long rirb_addr = (unsigned long long)hda.rirbubase << 32 | hda.rirblbase;
    while (hda.corbrp != hda.corbwp) {
        hda.corbrp = (hda.corbrp + 1) & 0xFF;
        unsigned long long cmd_off = corb_addr + (unsigned long long)hda.corbrp * 4;
        if (cmd_off + 4 > guest_mem_size) break;
        unsigned int cmd = *(unsigned int *)((unsigned char *)guest_mem + cmd_off);
        unsigned int resp = hda_codec_verb(cmd);
        /* Write response to RIRB */
        if (hda.rirbctl & 2) {
            hda.rirbwp = (hda.rirbwp + 1) & 0xFF;
            unsigned long long resp_off = rirb_addr + (unsigned long long)hda.rirbwp * 8;
            if (resp_off + 8 <= guest_mem_size) {
                *(unsigned int *)((unsigned char *)guest_mem + resp_off) = resp;
                *(unsigned int *)((unsigned char *)guest_mem + resp_off + 4) = 0; /* codec 0 */
            }
        }
    }
}

/* Cumulative count of PCM buffers handed to the DAC, readable by the guest at
   HDA BAR offset 0x180. A headless test cannot hear the stream, so it plays a
   known number of buffers and asserts this counter -- evidence the guest's PCM
   reached the drain, independent of whether the host has a working speaker. */
static unsigned int hda_drain_count = 0;

static unsigned int hda_read(unsigned long long offset) {
    switch ((int)offset) {
    case 0x00: return 0x1101;  /* GCAP: OSS=1, ISS=1, 64OK -- matches SD0 output + SD1 mic */
    case 0x02: return 0x0100;  /* 0x02=VMIN(0), 0x03=VMAJ(1): HDA 1.0 */
    case 0x08: return hda.gctl;
    case 0x0C: return hda.wakeen;
    case 0x0E: return hda.statests;
    case 0x20: return hda.intctl;
    case 0x24: return hda.intsts;
    case 0x40: return hda.corblbase;
    case 0x44: return hda.corbubase;
    case 0x48: return hda.corbwp;
    case 0x4A: return hda.corbrp;
    case 0x4C: return hda.corbctl;
    case 0x4D: return hda.corbsts;
    case 0x4E: return 0x42;  /* CORBSIZE: 256 entries supported+active */
    case 0x50: return hda.rirblbase;
    case 0x54: return hda.rirbubase;
    case 0x58: return hda.rirbwp;
    case 0x5A: return hda.rintcnt;
    case 0x5C: return hda.rirbctl;
    case 0x5D: return hda.rirbsts;
    case 0x5E: return 0x42;  /* RIRBSIZE: 256 entries */
    /* Stream descriptor 0 (output) at 0x80 */
    case 0x80: return hda.sd0ctl;
    case 0x83: return hda.sd0sts;
    case 0x84: return hda.sd0lpib;
    case 0x88: return hda.sd0cbl;
    case 0x8C: return hda.sd0lvi;
    case 0x92: return hda.sd0fmt;
    case 0x98: return hda.sd0bdpl;
    case 0x9C: return hda.sd0bdpu;
    /* Stream descriptor 1 (input / microphone) */
    case 0xA0: return hda.sd1ctl;
    case 0xA3: return hda.sd1sts;
    case 0xA4: return hda.sd1lpib;
    case 0xA8: return hda.sd1cbl;
    case 0xAC: return hda.sd1lvi;
    case 0xB2: return hda.sd1fmt;
    case 0xB8: return hda.sd1bdpl;
    case 0xBC: return hda.sd1bdpu;

    case 0x180: return hda_drain_count;  /* buffers drained to the DAC (streaming evidence) */
    default: return 0;
    }
}

/* ══ Audio Output (waveOut) ══ */
/* Eight ring buffers give a streaming guest (the C64 SID, one ~16 ms PCM
   buffer per emulated frame) enough queued audio to ride out a frame that
   renders slower than real time without an underrun click. audio_write blocks
   only when all eight are in flight, which is what paces the guest to the
   speaker. */
#define AUDIO_BUFS 8
#define AUDIO_BUF_SIZE 4096
static HWAVEOUT wave_out = NULL;
static WAVEHDR wave_hdrs[AUDIO_BUFS];
static unsigned char wave_data[AUDIO_BUFS][AUDIO_BUF_SIZE];
static int wave_next = 0;
static int wave_opened = 0;

static void audio_open(void) {
    if (wave_opened) return;
    WAVEFORMATEX wfx;
    memset(&wfx, 0, sizeof(wfx));
    wfx.wFormatTag = WAVE_FORMAT_PCM;
    wfx.nChannels = 2;
    wfx.nSamplesPerSec = 48000;
    wfx.wBitsPerSample = 16;
    wfx.nBlockAlign = wfx.nChannels * wfx.wBitsPerSample / 8;
    wfx.nAvgBytesPerSec = wfx.nSamplesPerSec * wfx.nBlockAlign;
    if (waveOutOpen(&wave_out, WAVE_MAPPER, &wfx, 0, 0, CALLBACK_NULL) == MMSYSERR_NOERROR) {
        wave_opened = 1;
        for (int i = 0; i < AUDIO_BUFS; i++) {
            memset(&wave_hdrs[i], 0, sizeof(WAVEHDR));
            wave_hdrs[i].lpData = (LPSTR)wave_data[i];
            wave_hdrs[i].dwBufferLength = AUDIO_BUF_SIZE;
        }
        fprintf(stderr, "AUDIO: waveOut opened (48kHz 16-bit stereo)\n");
    }
}

static void audio_write(unsigned char *pcm, int len) {
    if (!wave_opened) audio_open();
    if (!wave_opened) return;
    while (len > 0) {
        WAVEHDR *wh = &wave_hdrs[wave_next];
        if (wh->dwFlags & WHDR_PREPARED) {
            while (!(wh->dwFlags & WHDR_DONE)) Sleep(1);
            waveOutUnprepareHeader(wave_out, wh, sizeof(WAVEHDR));
        }
        int chunk = (len < AUDIO_BUF_SIZE) ? len : AUDIO_BUF_SIZE;
        memcpy(wave_data[wave_next], pcm, chunk);
        wh->dwBufferLength = chunk;
        waveOutPrepareHeader(wave_out, wh, sizeof(WAVEHDR));
        waveOutWrite(wave_out, wh, sizeof(WAVEHDR));
        pcm += chunk;
        len -= chunk;
        wave_next = (wave_next + 1) % AUDIO_BUFS;
    }
}

static int hda_drain_idx = 0; /* next BDL entry to drain */

static void hda_drain_stream(void) {
    if (!(hda.sd0ctl & 2)) return;
    unsigned long long bdl_addr = (unsigned long long)hda.sd0bdpu << 32 | hda.sd0bdpl;
    if (bdl_addr == 0) return;
    int lvi = hda.sd0lvi;
    if (hda_drain_idx > lvi) { hda_drain_idx = 0; return; }
    for (; hda_drain_idx <= lvi && hda_drain_idx < 256; hda_drain_idx++) {
        unsigned long long entry_addr = bdl_addr + (unsigned long long)hda_drain_idx * 16;
        if (entry_addr + 16 > guest_mem_size) break;
        unsigned char *entry = (unsigned char *)guest_mem + entry_addr;
        unsigned long long buf_addr = *(unsigned long long *)entry;
        unsigned int buf_len = *(unsigned int *)(entry + 8);
        if (buf_addr > 0 && buf_addr + buf_len <= guest_mem_size && buf_len > 0) {
            audio_write((unsigned char *)guest_mem + buf_addr, buf_len);
            hda.sd0lpib += buf_len;
            hda_drain_count++;   /* evidence: a buffer reached the DAC */
        }
        unsigned int ioc = *(unsigned int *)(entry + 12);
        if (ioc & 1) hda.sd0sts |= 4; /* IOC flag set -- raise BCIS */
    }
    if (hda_drain_idx > lvi) {
        /* Played the whole BDL once. Stop instead of looping: leaving RUN set made
           the main loop re-invoke this blocking drain on EVERY VM exit, wedging a
           guest that did not immediately hda-stop. Clear RUN so one RUN assertion
           plays the buffer exactly once. */
        hda_drain_idx = 0;
        hda.sd0lpib = 0;
        hda.sd0ctl &= ~2u;   /* clear RUN */
        hda.sd0sts |= 4;     /* BCIS: buffer completed */
    }
}

/* ══ Audio Input (waveIn / microphone) ══ */
#define AUDIO_IN_BUFS 4
#define AUDIO_IN_BUF_SIZE 4096
#define MIC_RING_SIZE 262144   /* ~1.3s of 48kHz 16-bit stereo */
static HWAVEIN wave_in = NULL;
static WAVEHDR wave_in_hdrs[AUDIO_IN_BUFS];
static unsigned char wave_in_data[AUDIO_IN_BUFS][AUDIO_IN_BUF_SIZE];
static unsigned char mic_ring[MIC_RING_SIZE];
static volatile int mic_ring_pos = 0;
static int wave_in_opened = 0;
static int wave_in_failed = 0;
static CRITICAL_SECTION mic_lock;
static int mic_lock_init = 0;

/* waveIn callback: append the recorded buffer into the ring, re-queue it.
   Runs on a system audio thread, so the ring is guarded by mic_lock. */
static void CALLBACK wave_in_cb(HWAVEIN h, UINT msg, DWORD_PTR inst,
                                DWORD_PTR p1, DWORD_PTR p2) {
    (void)inst; (void)p2;
    if (msg != WIM_DATA) return;
    WAVEHDR *wh = (WAVEHDR *)p1;
    EnterCriticalSection(&mic_lock);
    for (unsigned int i = 0; i < wh->dwBytesRecorded; i++) {
        mic_ring[mic_ring_pos] = ((unsigned char *)wh->lpData)[i];
        mic_ring_pos = (mic_ring_pos + 1) % MIC_RING_SIZE;
    }
    LeaveCriticalSection(&mic_lock);
    waveInAddBuffer(h, wh, sizeof(WAVEHDR));
}

static void audio_in_open(void) {
    if (wave_in_opened || wave_in_failed) return;
    if (!mic_lock_init) { InitializeCriticalSection(&mic_lock); mic_lock_init = 1; }
    WAVEFORMATEX wfx;
    memset(&wfx, 0, sizeof(wfx));
    wfx.wFormatTag = WAVE_FORMAT_PCM;
    wfx.nChannels = 2;
    wfx.nSamplesPerSec = 48000;
    wfx.wBitsPerSample = 16;
    wfx.nBlockAlign = 4;
    wfx.nAvgBytesPerSec = 48000 * 4;
    if (waveInOpen(&wave_in, WAVE_MAPPER, &wfx, (DWORD_PTR)wave_in_cb, 0,
                   CALLBACK_FUNCTION) != MMSYSERR_NOERROR) {
        fprintf(stderr, "AUDIO: waveIn open failed (no microphone?)\n");
        wave_in_failed = 1;
        return;
    }
    for (int i = 0; i < AUDIO_IN_BUFS; i++) {
        memset(&wave_in_hdrs[i], 0, sizeof(WAVEHDR));
        wave_in_hdrs[i].lpData = (LPSTR)wave_in_data[i];
        wave_in_hdrs[i].dwBufferLength = AUDIO_IN_BUF_SIZE;
        waveInPrepareHeader(wave_in, &wave_in_hdrs[i], sizeof(WAVEHDR));
        waveInAddBuffer(wave_in, &wave_in_hdrs[i], sizeof(WAVEHDR));
    }
    waveInStart(wave_in);
    wave_in_opened = 1;
    fprintf(stderr, "AUDIO: waveIn opened (microphone, 48kHz 16-bit stereo)\n");
}

/* Input stream 1: when the guest sets its RUN bit, copy the most recent
   captured microphone samples into the guest's BDL buffer, then clear RUN
   (one-shot capture). Called from the main loop. Opening the mic is lazy on
   the first run; with no microphone the buffer is filled with silence. */
static void hda_fill_input(void) {
    if (!(hda.sd1ctl & 2)) return;
    if (!wave_in_opened) audio_in_open();
    unsigned long long bdl_addr = (unsigned long long)hda.sd1bdpu << 32 | hda.sd1bdpl;
    if (bdl_addr == 0 || bdl_addr + 16 > guest_mem_size) { hda.sd1ctl &= ~2u; return; }
    unsigned char *entry = (unsigned char *)guest_mem + bdl_addr;
    unsigned long long buf_addr = *(unsigned long long *)entry;
    unsigned int buf_len = *(unsigned int *)(entry + 8);
    if (buf_addr == 0 || buf_addr + buf_len > guest_mem_size || buf_len == 0) {
        hda.sd1ctl &= ~2u; return;
    }
    unsigned int copy = buf_len > MIC_RING_SIZE ? MIC_RING_SIZE : buf_len;
    unsigned char *dst = (unsigned char *)guest_mem + buf_addr;
    if (wave_in_opened) {
        EnterCriticalSection(&mic_lock);
        int start = (((mic_ring_pos - (int)copy) % MIC_RING_SIZE) + MIC_RING_SIZE) % MIC_RING_SIZE;
        for (unsigned int i = 0; i < copy; i++) dst[i] = mic_ring[(start + i) % MIC_RING_SIZE];
        LeaveCriticalSection(&mic_lock);
    } else {
        for (unsigned int i = 0; i < copy; i++) dst[i] = 0;
    }
    hda.sd1lpib += copy;
    hda.sd1sts |= 4;      /* IOC */
    hda.sd1ctl &= ~2u;    /* one-shot: clear RUN so the main loop stops */
}

static void hda_write(unsigned long long offset, unsigned int val) {
    switch ((int)offset) {
    case 0x08: /* GCTL */
        hda.gctl = val;
        if (val & 1) hda.statests = 1;  /* codec 0 present after reset exit */
        break;
    case 0x0C: hda.wakeen = (unsigned short)val; break;
    case 0x0E: hda.statests &= ~(unsigned short)val; break;  /* W1C */
    case 0x20: hda.intctl = val; break;
    case 0x24: hda.intsts &= ~val; break;  /* W1C */
    case 0x40: hda.corblbase = val; break;
    case 0x44: hda.corbubase = val; break;
    case 0x48:
        hda.corbwp = (unsigned short)(val & 0xFF);
        hda_process_corb();
        break;
    case 0x4A:
        if (val & 0x8000) hda.corbrp = 0;  /* reset */
        break;
    case 0x4C: hda.corbctl = (unsigned char)val; break;
    case 0x4D: hda.corbsts &= ~(unsigned char)val; break;
    case 0x50: hda.rirblbase = val; break;
    case 0x54: hda.rirbubase = val; break;
    case 0x58:
        if (val & 0x8000) hda.rirbwp = 0xFFFF;  /* reset */
        break;
    case 0x5A: hda.rintcnt = (unsigned short)val; break;
    case 0x5C: hda.rirbctl = (unsigned char)val; break;
    case 0x5D: hda.rirbsts &= ~(unsigned char)val; break;
    /* Stream descriptor 0 */
    case 0x80:
        hda.sd0ctl = val;
        if (val & 2) hda_drain_stream(); /* DMA run bit set -- drain BDL to audio */
        break;
    case 0x83: hda.sd0sts &= ~(unsigned char)val; break;
    case 0x88: hda.sd0cbl = val; break;
    case 0x8C: hda.sd0lvi = (unsigned short)val; break;
    case 0x92: hda.sd0fmt = (unsigned short)val; break;
    case 0x98: hda.sd0bdpl = val; break;
    case 0x9C: hda.sd0bdpu = val; break;
    /* Stream descriptor 1 (input / microphone). RUN is polled by the main
       loop, which fills the guest buffer from the mic ring and clears RUN. */
    case 0xA0: hda.sd1ctl = val; break;
    case 0xA3: hda.sd1sts &= ~(unsigned char)val; break;
    case 0xA8: hda.sd1cbl = val; break;
    case 0xAC: hda.sd1lvi = (unsigned short)val; break;
    case 0xB2: hda.sd1fmt = (unsigned short)val; break;
    case 0xB8: hda.sd1bdpl = val; break;
    case 0xBC: hda.sd1bdpu = val; break;
    }
}

/* ══ HPET (High Precision Event Timer) ══ */
#define HPET_BAR      0xFED00000ULL
#define HPET_BAR_SIZE 0x1000

static struct {
    unsigned int config;       /* general config: bit 0 = enable */
    unsigned int int_status;
    unsigned long long t0_config;
    unsigned long long t0_comparator;
    int t0_armed;              /* comparator has been written since it last fired */
    unsigned long long frozen; /* counter value carried across a stop */
} hpet;

/* The point the running counter measures from. Rolled forward every time
   the counter is restarted, so a stop costs the guest no elapsed ticks. */
static LARGE_INTEGER hpet_epoch;

/* -no-hpet: the window is dead. Every register reads zero and every write is
   dropped, which is the box that offers no timer rather than one that offers a
   broken one.

   It is the capability period at offset 04 that decides: a guest derives its
   tick rate from it, and a period of zero is the machine saying nothing. The
   readers here (Hpet.codex, and E1000e and NicAsde above it) turn that into a
   rate of zero and fall back to counting reads, and those count fallbacks had
   no bed arm at all -- three diagnostic stages carry a `no-hpet` state that
   nothing could reach, so nothing could show they were right. The counter
   reads zero too, so a caller that ignores the rate and times off the counter
   gets a clock that never advances rather than one that races.

   The xHCI MFINDEX above still runs off hpet_raw. That is the model's own use
   of host time, not a register the guest was offered, and suppressing it would
   stop the frame counter for a reason that has nothing to do with the HPET. */
static int hpet_absent = 0;

/* -hpet-frozen: the window is there and UNDECODED, which is the other dead
   clock. Every register reads all-ones: the period at 04 is 0xFFFFFFFF, so a
   guest derives a rate of 232830 Hz, bogus but NONZERO, and takes its CLOCKED
   wait paths; the counter at F0/F4 is 0xFFFFFFFF and never moves, so every
   one of those waits is bounded only by its read fuel. That is the shape
   -no-hpet cannot express (a zero period sends every reader to the counting
   fallback) and the one blu asked this flag to carry: a rate nothing
   validates over a counter that is stuck. The diag ladder's b3 clock control
   reads it and refuses before bring-up. */
static int hpet_allones = 0;

/* The main counter must advance at the rate the capability register
   advertises. It used to return the raw QueryPerformanceCounter value
   while GCAP claimed a 69841279 fs period, so every elapsed-time
   calculation a guest did was wrong by the QPC/14.318MHz ratio -- on a
   10 MHz QPC that is a clock running 30% slow, which reads as a machine
   that is merely sluggish rather than as a broken timer.

   The counter is measured from VM start so it begins near zero, and the
   scaling is split into whole and fractional parts because delta *
   HPET_HZ overflows 64 bits on a long run. */
#define HPET_HZ 14318180ULL

/* Defined here rather than beside the RTC because hpet_now reads them; the
   comment explaining the flag is at the RTC device. */
static int rtc_fixed = 0;
static SYSTEMTIME rtc_fixed_st;

/* Raw elapsed count since the epoch below, in HPET ticks. */
static unsigned long long hpet_raw(void) {
    static LARGE_INTEGER freq;
    LARGE_INTEGER pc;
    unsigned long long delta;
    if (!freq.QuadPart) QueryPerformanceFrequency(&freq);
    QueryPerformanceCounter(&pc);
    delta = (unsigned long long)(pc.QuadPart - hpet_epoch.QuadPart);
    return (delta / (unsigned long long)freq.QuadPart) * HPET_HZ
         + ((delta % (unsigned long long)freq.QuadPart) * HPET_HZ)
           / (unsigned long long)freq.QuadPart;
}

/* ENABLE_CNF (general configuration bit 0) halts the main counter, and the
   counter must hold its value across the stop rather than resuming as if
   time had passed -- a guest that disables the HPET to read a consistent
   64-bit value off a 32-bit bus, which is the ordinary reason to do it,
   otherwise gets a counter that jumped while it was supposedly stopped.
   `frozen` is the value at the moment of the stop; the epoch is rolled
   forward on restart so the count continues from there. */
/* -rtc stops the HPET as well, and stops it at the second the caller named.
   The flag exists so a captured frame can be compared against a recorded one,
   and a guest that animates off a hardware counter is exactly as
   uncomparable as one that paints the wall clock. Pinning it to
   (minute*60 + second) * HPET_HZ rather than to zero keeps the two clocks
   telling the same time, so a caller who moves -rtc forward a second still
   moves the animation forward a second, which is what every existing capture
   recipe assumes.

   Nothing that reads the HPET runs under -rtc -- the readers are E1000e and
   NicAsde, and no test sidecar passes the flag -- so a counter that does not
   advance cannot strand a timeout here. */
static unsigned long long hpet_now(void) {
    if (rtc_fixed) {
        return ((unsigned long long)rtc_fixed_st.wMinute * 60ULL
              + (unsigned long long)rtc_fixed_st.wSecond) * (unsigned long long)HPET_HZ;
    }
    if (!(hpet.config & 1)) return hpet.frozen;
    return hpet.frozen + hpet_raw();
}

static unsigned int hpet_read(unsigned long long offset) {
    if (hpet_absent) return 0;
    if (hpet_allones) return 0xFFFFFFFFu;
    switch ((int)offset) {
    case 0x00: return 0x8086A201;  /* GCAP_ID: rev=1, num_timers=1, 64-bit, vendor=Intel */
    case 0x04: return 0x0429B17F;  /* period = 69841279 femtoseconds (~14.318 MHz) */
    case 0x10: return hpet.config;
    case 0x20: return hpet.int_status;
    case 0xF0: return (unsigned int)(hpet_now() & 0xFFFFFFFF);
    case 0xF4: return (unsigned int)(hpet_now() >> 32);
    case 0x100: return (unsigned int)(hpet.t0_config & 0xFFFFFFFF);
    case 0x104: return (unsigned int)(hpet.t0_config >> 32);
    case 0x108: return (unsigned int)(hpet.t0_comparator & 0xFFFFFFFF);
    case 0x10C: return (unsigned int)(hpet.t0_comparator >> 32);
    default: return 0;
    }
}

static void hpet_write(unsigned long long offset, unsigned int val) {
    if (hpet_absent) return;
    if (hpet_allones) return;
    switch ((int)offset) {
    case 0x10: {
        int was = hpet.config & 1, now = val & 1;
        if (was && !now) {
            hpet.frozen = hpet.frozen + hpet_raw();   /* stopping: bank it */
        } else if (!was && now) {
            QueryPerformanceCounter(&hpet_epoch);     /* starting: new epoch */
        }
        hpet.config = val;
        break;
    }
    case 0x20: hpet.int_status &= ~val; break; /* W1C */
    case 0x100: hpet.t0_config = (hpet.t0_config & 0xFFFFFFFF00000000ULL) | val; break;
    case 0x104: hpet.t0_config = (hpet.t0_config & 0xFFFFFFFFULL) | ((unsigned long long)val << 32); break;
    case 0x108: hpet.t0_comparator = (hpet.t0_comparator & 0xFFFFFFFF00000000ULL) | val;
                hpet.t0_armed = 1; break;
    case 0x10C: hpet.t0_comparator = (hpet.t0_comparator & 0xFFFFFFFFULL) | ((unsigned long long)val << 32);
                hpet.t0_armed = 1; break;
    }
}

/* ══ IOAPIC ══ */
#define IOAPIC_BAR      0xFEC00000ULL
#define IOAPIC_BAR_SIZE 0x1000
#define IOAPIC_MAX_REDIR 24

static struct {
    unsigned int ioregsel;
    unsigned int id;
    unsigned long long redir[IOAPIC_MAX_REDIR];
} ioapic;

static unsigned int ioapic_read(unsigned long long offset) {
    if (offset == 0x00) return ioapic.ioregsel;
    if (offset == 0x10) { /* IOWIN -- data register */
        unsigned int sel = ioapic.ioregsel;
        if (sel == 0x00) return ioapic.id; /* IOAPICID */
        if (sel == 0x01) return (IOAPIC_MAX_REDIR - 1) << 16 | 0x20; /* IOAPICVER: version=0x20 */
        if (sel == 0x02) return 0; /* IOAPICARB */
        if (sel >= 0x10 && sel < 0x10 + IOAPIC_MAX_REDIR * 2) {
            int entry = (sel - 0x10) / 2;
            int hi = (sel - 0x10) % 2;
            if (hi) return (unsigned int)(ioapic.redir[entry] >> 32);
            return (unsigned int)(ioapic.redir[entry] & 0xFFFFFFFF);
        }
    }
    return 0;
}

static void ioapic_write(unsigned long long offset, unsigned int val) {
    if (offset == 0x00) { ioapic.ioregsel = val; return; }
    if (offset == 0x10) {
        unsigned int sel = ioapic.ioregsel;
        if (sel == 0x00) { ioapic.id = val & 0x0F000000; return; }
        if (sel >= 0x10 && sel < 0x10 + IOAPIC_MAX_REDIR * 2) {
            int entry = (sel - 0x10) / 2;
            int hi = (sel - 0x10) % 2;
            if (hi) ioapic.redir[entry] = (ioapic.redir[entry] & 0xFFFFFFFFULL) | ((unsigned long long)val << 32);
            else ioapic.redir[entry] = (ioapic.redir[entry] & 0xFFFFFFFF00000000ULL) | val;
        }
    }
}

/* ── Device interrupt queue ────────────────────────────────────────────

   The redirection table used to be write-only storage: entries went in
   and nothing ever walked them, so a device that raised a line had no
   route to a vector and a guest that programmed the IOAPIC and waited
   got nothing back.

   A raised line resolves through its redirection entry to a vector,
   which lands here rather than being injected on the spot. The BSP run
   loop already owns one interrupt slot and the IF/interrupt-window
   dance around it; injecting from a device poll would race that and drop
   whichever vector lost. This queue lets the existing slot drain them in
   order, one per lap, so the PIT tick and a device line no longer
   overwrite each other. */
#define DEVIRQ_QUEUE 16
static struct {
    int vec[DEVIRQ_QUEUE];
    int head, tail;
} devirq;

static void devirq_push(int vector) {
    int next = (devirq.tail + 1) % DEVIRQ_QUEUE;
    if (next == devirq.head) return;   /* full: drop rather than wrap over unread */
    devirq.vec[devirq.tail] = vector;
    devirq.tail = next;
}

static int devirq_pop(void) {
    int v;
    if (devirq.head == devirq.tail) return -1;
    v = devirq.vec[devirq.head];
    devirq.head = (devirq.head + 1) % DEVIRQ_QUEUE;
    return v;
}

/* Raise an IOAPIC line. Masked entries (bit 16) and entries whose vector
   was never programmed are dropped -- a vector below 32 is a CPU
   exception number and delivering one would fake a fault. */
static void ioapic_raise(int irq) {
    unsigned long long e;
    int vector;
    if (irq < 0 || irq >= IOAPIC_MAX_REDIR) return;
    e = ioapic.redir[irq];
    if (e & (1ULL << 16)) return;              /* masked */
    vector = (int)(e & 0xFF);
    if (vector < 32) return;
    devirq_push(vector);
}

/* Comparator match. Called from the same poll the other devices use.
   Level of modelling: timer 0 only (GCAP advertises one), TN_INT_ENB_CNF
   (bit 2) gates delivery, TN_TYPE_CNF (bit 3) selects periodic, and
   TN_INT_ROUTE_CNF (bits 9-13) picks the IOAPIC line. A one-shot fires
   once per comparator write, which is what t0_armed tracks. */
static void hpet_poll(void) {
    int route, periodic;
    if (!(hpet.config & 1)) return;            /* ENABLE_CNF clear: counter halted */
    if (!hpet.t0_armed) return;
    if (hpet_now() < hpet.t0_comparator) return;

    hpet.int_status |= 1;
    periodic = (hpet.t0_config >> 3) & 1;
    route = (int)((hpet.t0_config >> 9) & 0x1F);
    if ((hpet.t0_config >> 2) & 1) ioapic_raise(route);

    if (periodic) {
        /* Re-arm one period on from the deadline that just passed. The
           period is the comparator's own value in periodic mode. */
        unsigned long long now = hpet_now();
        unsigned long long step = hpet.t0_comparator;
        if (!step) { hpet.t0_armed = 0; return; }
        while (hpet.t0_comparator <= now) hpet.t0_comparator += step;
    } else {
        hpet.t0_armed = 0;
    }
}

/* ══ LAPIC ══ */
#define LAPIC_BAR       0xFEE00000ULL
#define LAPIC_BAR_SIZE  0x1000
#define SMP_MAX_CORES   16

static struct {
    unsigned int id;
    unsigned int sivr;
    unsigned int icr_lo;
    unsigned int icr_hi;
    unsigned int eoi;
    int ap_count;
    unsigned long long ap_entry_addr;
    volatile int ap_running[SMP_MAX_CORES];
    HANDLE ap_threads[SMP_MAX_CORES];
} lapic_state;

/* The LAPIC timer is the one thing here that MUST be per-core: it is what
   preempts a process running on an application processor, and the point of
   it is that each core keeps its own count and takes its own interrupt. The
   ICR and SIVR above stay shared -- the BSP writes them at bring-up and
   nobody reads them afterwards -- but a timer shared between cores would
   deliver one core's tick to another, which is not a timer at all.

   LVT bit 16 masks, bit 17 selects periodic, and the vector is the low
   eight bits. An initial count of zero disarms the timer, per the SDM. */
typedef struct {
    unsigned int lvt_timer;    /* 0x320 */
    unsigned int init_count;   /* 0x380 */
    unsigned int div_cfg;      /* 0x3E0 */
    double next_fire_ms;
} lapic_timer_t;
static lapic_timer_t lapic_timers[SMP_MAX_CORES];

/* Set by a guest wake IPI (fixed-mode, all-but-self on the timer vector) to
   nudge a core parked on hlt in __idle_dispatch. A parked AP thread polls its
   flag while halted and, on seeing it, injects the timer vector to un-halt and
   rescan -- the guest's idle-stack guard drops the tick. Without this a parked
   core only rescans on its own timer period, so short-lived work finishes on
   the never-parking boot processor before any AP wakes to claim it. */
static volatile LONG ap_wake_pending[SMP_MAX_CORES];

/* The guest asks for a period in bus counts; what it is owed is a periodic
   interrupt, not a faithful bus-clock model. One tick per PIT period gives
   an AP the same preemption cadence the boot processor already has, which
   is the property the scheduler actually depends on. */
/* The timer period used to be this constant whatever the guest programmed,
   so Initial Count and the divide configuration were decoration: two guests
   asking for periods an order of magnitude apart got the same one. The
   period is derived now, and this survives only as the fallback for a timer
   armed before its count is known, and as the scale the Current Count
   register reads against. */
#define LAPIC_TIMER_PERIOD_MS 55.0

/* A modelled input frequency. Nothing here has a real bus clock to divide,
   so one has to be chosen and stated: 100 MHz is the APIC bus frequency on
   the hardware this layout imitates. It is a modelling choice, not a
   measurement -- what the guest can rely on is that DOUBLING the initial
   count doubles the period, which is what was untrue before. */
#define LAPIC_INPUT_HZ 100000000.0

/* A period is clamped into a range a VM can actually service. Below the
   floor a guest could ask for a tick faster than the host can deliver and
   spend the whole run in interrupt entry; above the ceiling a mistaken
   count would park preemption for minutes and read as a hang. */
#define LAPIC_PERIOD_MIN_MS 1.0
#define LAPIC_PERIOD_MAX_MS 5000.0

static double now_ms_for_timer(void);   /* defined below; Current Count needs it */

/* SDM: the divide configuration is bits [3,1:0], and the encoding is not
   sequential -- 0b1011 is divide-by-1, which sorts after 128. */
static unsigned int lapic_divisor(unsigned int dcr) {
    static const unsigned int tbl[8] = { 2, 4, 8, 16, 32, 64, 128, 1 };
    return tbl[(((dcr & 0x8) >> 1) | (dcr & 0x3)) & 7];
}

static double lapic_period_ms(int cpu) {
    double p;
    if (!lapic_timers[cpu].init_count) return LAPIC_TIMER_PERIOD_MS;
    p = (double)lapic_timers[cpu].init_count
      * (double)lapic_divisor(lapic_timers[cpu].div_cfg)
      * 1000.0 / LAPIC_INPUT_HZ;
    if (p < LAPIC_PERIOD_MIN_MS) p = LAPIC_PERIOD_MIN_MS;
    if (p > LAPIC_PERIOD_MAX_MS) p = LAPIC_PERIOD_MAX_MS;
    return p;
}

static unsigned int lapic_read_cpu(int cpu, unsigned long long offset) {
    if (offset == 0x20) return (unsigned int)cpu << 24;
    if (offset == 0x30) return 0x00050014;
    if (offset == 0xF0) return lapic_state.sivr;
    if (offset == 0x300) return lapic_state.icr_lo;
    if (offset == 0x310) return lapic_state.icr_hi;
    if (cpu >= 0 && cpu < SMP_MAX_CORES) {
        if (offset == 0x320) return lapic_timers[cpu].lvt_timer;
        if (offset == 0x380) return lapic_timers[cpu].init_count;
        /* Current Count. This returned the Initial Count verbatim, so it
           never fell: a guest spinning on it to wait out a period spun
           forever, and one sampling deltas to calibrate measured zero
           elapsed every time. Scale the remaining fraction of the period
           the timer is actually pacing against. */
        if (offset == 0x390) {
            double rem;
            if (!lapic_timers[cpu].init_count) return 0;
            double period = lapic_period_ms(cpu);
            rem = lapic_timers[cpu].next_fire_ms - now_ms_for_timer();
            if (rem < 0.0) rem = 0.0;
            if (rem > period) rem = period;
            return (unsigned int)((double)lapic_timers[cpu].init_count
                                  * rem / period);
        }
        if (offset == 0x3E0) return lapic_timers[cpu].div_cfg;
    }
    return 0;
}

static unsigned int lapic_read(unsigned long long offset) {
    return lapic_read_cpu(0, offset);
}

static void ap_thread_func(void *arg);

/* What an MMIO instruction is: how long, and which operand carries the
   value. Both come from the same walk over the bytes WHP hands us, because
   deriving them separately is how they drift apart. */
typedef struct {
    int len;            /* bytes; 0 = not decoded, and the caller must not step RIP */
    int reg;            /* GPR 0-15 from ModRM.reg, extended by REX.R; -1 = immediate source */
    unsigned int imm;   /* the source value when reg is -1 */
} mmio_insn_t;
static mmio_insn_t mmio_decode(const unsigned char *b, int n);
static int mmio_insn_len(const unsigned char *b, int n);

/* x86 numbers its GPRs in this order. The table is explicit rather than
   arithmetic on the enum: the WHV_REGISTER_NAME values are not ours, and
   an off-by-one here would read a plausible number out of the wrong
   register -- the failure this whole change exists to end. */
static const WHV_REGISTER_NAME mmio_gpr_names[16] = {
    WHvX64RegisterRax, WHvX64RegisterRcx, WHvX64RegisterRdx, WHvX64RegisterRbx,
    WHvX64RegisterRsp, WHvX64RegisterRbp, WHvX64RegisterRsi, WHvX64RegisterRdi,
    WHvX64RegisterR8,  WHvX64RegisterR9,  WHvX64RegisterR10, WHvX64RegisterR11,
    WHvX64RegisterR12, WHvX64RegisterR13, WHvX64RegisterR14, WHvX64RegisterR15,
};

static void output_buf_write(unsigned char b);

static void lapic_write(unsigned long long offset, unsigned int val);

/* Host milliseconds. The LAPIC timer only needs a monotonic clock to pace a
   period against; QPC is the one the rest of this program already trusts. */
static double now_ms_for_timer(void) {
    static LARGE_INTEGER freq;
    LARGE_INTEGER pc;
    if (!freq.QuadPart) QueryPerformanceFrequency(&freq);
    QueryPerformanceCounter(&pc);
    return (double)pc.QuadPart * 1000.0 / (double)freq.QuadPart;
}

static void lapic_write_cpu(int cpu, unsigned long long offset, unsigned int val) {
    if (cpu >= 0 && cpu < SMP_MAX_CORES) {
        if (offset == 0x320) { lapic_timers[cpu].lvt_timer = val; return; }
        if (offset == 0x3E0) { lapic_timers[cpu].div_cfg = val; return; }
        if (offset == 0x380) {
            lapic_timers[cpu].init_count = val;
            /* Arming the timer starts its first period from now. A zero
               count disarms it, and must not leave a fire time behind.
               The period comes from the count just written and the divide
               configuration, so it must be computed after the store. */
            lapic_timers[cpu].next_fire_ms = val
                ? now_ms_for_timer() + lapic_period_ms(cpu) : 0.0;
            return;
        }
    }
    lapic_write(offset, val);
}

/* The boot processor's own LAPIC writes have to take the same route its
   reads already do (lapic_read delegates to lapic_read_cpu(0)). They went
   straight to lapic_write instead, which knows nothing about the timer
   registers -- so arming the timer from the BSP set no initial count, and
   the whole per-core timer register file was reachable only from an
   application processor. Non-timer offsets still fall through to
   lapic_write, so EOI, SIVR and the SIPI path are unchanged. */
static void lapic_write_bsp(unsigned long long offset, unsigned int val) {
    lapic_write_cpu(0, offset, val);
}

/* Is this core's LAPIC timer armed and unmasked? Bit 16 of the LVT is the
   mask; an initial count of zero means disarmed. */
static int lapic_timer_armed(int cpu) {
    if (cpu < 0 || cpu >= SMP_MAX_CORES) return 0;
    if (lapic_timers[cpu].init_count == 0) return 0;
    if (lapic_timers[cpu].lvt_timer & (1u << 16)) return 0;
    return (lapic_timers[cpu].lvt_timer & 0xFF) >= 32;
}

/* Returns the vector to deliver if this core's timer period has elapsed,
   otherwise -1. Rearms for the next period on a periodic timer (bit 17) and
   disarms a one-shot, which is what the SDM says a one-shot does. */
static int lapic_timer_due(int cpu, double now_ms) {
    if (!lapic_timer_armed(cpu)) return -1;
    if (now_ms < lapic_timers[cpu].next_fire_ms) return -1;
    if (lapic_timers[cpu].lvt_timer & (1u << 17))
        lapic_timers[cpu].next_fire_ms = now_ms + lapic_period_ms(cpu);
    else
        lapic_timers[cpu].init_count = 0;
    return (int)(lapic_timers[cpu].lvt_timer & 0xFF);
}

static void lapic_write(unsigned long long offset, unsigned int val) {
    if (offset == 0xB0) { lapic_state.eoi = 0; return; }
    if (offset == 0xF0) { lapic_state.sivr = val; return; }
    if (offset == 0x310) { lapic_state.icr_hi = val; return; }
    if (offset == 0x300) {
        lapic_state.icr_lo = val;
        int deliv = (val >> 8) & 7;
        int dest = (val >> 18) & 3;
        int vector = val & 0xFF;
        if (deliv == 6 && dest == 3) {
            /* A start-up IPI carries a PAGE NUMBER, and the core it starts
               begins executing at the first byte of that page IN REAL MODE,
               with CS.selector = vector<<8, CS.base = vector<<12 and IP = 0.
               Nothing else is handed over: no stack, no page tables, no
               long mode, and no identity.

               This used to read a full 64-bit entry address out of guest
               memory at 0x1000 and drop the AP straight into long mode with
               CR3, EFER, a GDT and its core id in RDI all supplied by the
               host. That made SMP work here and guaranteed it could not work
               on a physical machine, because on silicon the vector field is
               the only channel there is. It also made the guest's own
               trampoline untestable, which is why there wasn't one.

               The guest now copies a real-mode trampoline into that page
               before it sends this IPI, so every SMP test exercises the code
               a physical machine would run. */
            unsigned long long tramp = (unsigned long long)vector << 12;
            for (int i = 1; i < SMP_MAX_CORES && i <= lapic_state.ap_count; i++) {
                if (lapic_state.ap_running[i]) continue;
                lapic_state.ap_running[i] = 1;
                HRESULT hr = WHvCreateVirtualProcessor(partition, i, 0);
                if (FAILED(hr)) {
                    fprintf(stderr, "WHvCreateVirtualProcessor(%d): 0x%lx\n", i, hr);
                    lapic_state.ap_running[i] = 0;
                    continue;
                }
                /* Reset state, per the SDM's "Processor State After Reset"
                   with the start-up vector applied to CS. Real mode means
                   CR0.PE = 0, so no CR3, no CR4.PAE, no EFER and no GDT --
                   the trampoline installs every one of them itself. RDI is
                   NOT set: a core that is told its identity by the host is a
                   core whose identity mechanism has never been tested, and
                   the trampoline takes an id from a guest cell instead. */
                WHV_REGISTER_NAME ap_names[] = {
                    WHvX64RegisterRip, WHvX64RegisterRsp, WHvX64RegisterRflags,
                    WHvX64RegisterCs, WHvX64RegisterDs, WHvX64RegisterEs,
                    WHvX64RegisterSs, WHvX64RegisterFs, WHvX64RegisterGs,
                    WHvX64RegisterCr0, WHvX64RegisterCr3, WHvX64RegisterCr4,
                    WHvX64RegisterEfer, WHvX64RegisterGdtr, WHvX64RegisterIdtr
                };
                WHV_REGISTER_VALUE ap_vals[15];
                memset(ap_vals, 0, sizeof(ap_vals));
                ap_vals[0].Reg64 = 0;
                ap_vals[1].Reg64 = 0;
                ap_vals[2].Reg64 = 2;
                ap_vals[3].Segment.Selector = (unsigned short)(vector << 8);
                ap_vals[3].Segment.Base = tramp;
                ap_vals[3].Segment.Limit = 0xFFFF;
                ap_vals[3].Segment.Attributes = 0x009B; /* 16-bit code, present */
                for (int s = 4; s <= 8; s++) {
                    ap_vals[s].Segment.Selector = 0;
                    ap_vals[s].Segment.Base = 0;
                    ap_vals[s].Segment.Limit = 0xFFFF;
                    ap_vals[s].Segment.Attributes = 0x0093; /* 16-bit data */
                }
                ap_vals[9].Reg64  = 0x10;        /* CR0:  ET only -- PE and PG clear */
                ap_vals[10].Reg64 = 0;           /* CR3                              */
                ap_vals[11].Reg64 = 0;           /* CR4                              */
                ap_vals[12].Reg64 = 0;           /* EFER                             */
                ap_vals[13].Table.Base = 0;      /* GDTR: the trampoline loads one   */
                ap_vals[13].Table.Limit = 0xFFFF;
                ap_vals[14].Table.Base = 0;      /* IDTR: real-mode IVT              */
                ap_vals[14].Table.Limit = 0xFFFF;
                HRESULT shr = WHvSetVirtualProcessorRegisters(partition, i, ap_names, 15, ap_vals);
                if (FAILED(shr))
                    fprintf(stderr, "SMP: AP %d real-mode SetRegs failed: 0x%lx\n", i, (unsigned long)shr);
                fprintf(stderr, "SMP: AP %d started in real mode at %04x:0000 (0x%llx)\n",
                    i, (unsigned int)(vector << 8), tramp);
                lapic_state.ap_threads[i] = CreateThread(NULL, 0,
                    (LPTHREAD_START_ROUTINE)ap_thread_func,
                    (void*)(intptr_t)i, 0, NULL);
            }
        }
        if (deliv == 0 && dest == 3) {
            /* Wake IPI (fixed-mode, all-but-self): a core published work and
               nudges every parked idle core to rescan. Flag every AP; a parked
               one injects its timer vector and falls through to its scan, the
               guest's idle-stack guard dropping the tick. */
            for (int i = 1; i < SMP_MAX_CORES; i++)
                InterlockedExchange(&ap_wake_pending[i], 1);
        }
        return;
    }
}

/* Deliver this core's LAPIC timer tick, if one is due and the core can take
   it. An AP starts with IF=0 and only raises it once its own timer is armed,
   so a core that has not asked to be preempted never is.

   The interrupt is what makes preemption on an application processor
   possible at all: before this, an AP took no interrupt of any kind, so a
   process that never yielded owned its core until it exited. */
static void ap_deliver_timer(int cpu_id) {
    int vec = lapic_timer_due(cpu_id, now_ms_for_timer());
    if (vec < 0) return;

    WHV_REGISTER_NAME fn = WHvX64RegisterRflags;
    WHV_REGISTER_VALUE fv;
    if (FAILED(WHvGetVirtualProcessorRegisters(partition, cpu_id, &fn, 1, &fv))) return;
    if (!(fv.Reg64 & 0x200)) return;   /* IF=0: the core is not accepting one */

    WHV_REGISTER_NAME names[2] = {
        WHvRegisterPendingInterruption, WHvRegisterInternalActivityState
    };
    WHV_REGISTER_VALUE vals[2];
    memset(vals, 0, sizeof(vals));
    vals[0].PendingInterruption.InterruptionPending = 1;
    vals[0].PendingInterruption.InterruptionType = 0;  /* WHvX64PendingInterrupt */
    vals[0].PendingInterruption.InterruptionVector = (unsigned int)vec;
    WHvSetVirtualProcessorRegisters(partition, cpu_id, names, 2, vals);
}

/* Un-halt a parked idle core by injecting its timer vector, whether or not
   the timer period is up: the core is on hlt in __idle_dispatch and only needs
   to resume past it and rescan. Called after a parked AP was nudged by a wake
   IPI or its wait timed out. No-op if the core has IF=0 (not accepting an
   interrupt, so not our idle core). */
static void ap_force_wake(int cpu_id) {
    WHV_REGISTER_NAME fn = WHvX64RegisterRflags;
    WHV_REGISTER_VALUE fv;
    if (FAILED(WHvGetVirtualProcessorRegisters(partition, cpu_id, &fn, 1, &fv))) return;
    if (!(fv.Reg64 & 0x200)) return;   /* IF=0 */
    int vec = lapic_timers[cpu_id].lvt_timer & 0xFF;
    if (vec < 32) vec = 48;            /* the timer/wake vector the guest uses */
    WHV_REGISTER_NAME names[2] = {
        WHvRegisterPendingInterruption, WHvRegisterInternalActivityState
    };
    WHV_REGISTER_VALUE vals[2];
    memset(vals, 0, sizeof(vals));
    vals[0].PendingInterruption.InterruptionPending = 1;
    vals[0].PendingInterruption.InterruptionType = 0;
    vals[0].PendingInterruption.InterruptionVector = (unsigned int)vec;
    WHvSetVirtualProcessorRegisters(partition, cpu_id, names, 2, vals);
}

static void ap_thread_func(void *arg) {
    int cpu_id = (int)(intptr_t)arg;
    WHV_RUN_VP_EXIT_CONTEXT ctx;
    fprintf(stderr, "SMP: AP %d thread started\n", cpu_id);
    for (;;) {
        ap_deliver_timer(cpu_id);
        HRESULT hr = WHvRunVirtualProcessor(partition, cpu_id, &ctx, sizeof(ctx));
        if (FAILED(hr)) {
            fprintf(stderr, "SMP: AP %d run failed: 0x%lx\n", cpu_id, hr);
            break;
        }
        switch (ctx.ExitReason) {
        case WHvRunVpExitReasonX64Halt:
            /* Parked on hlt in __idle_dispatch. Wake on a wake IPI (within a
               millisecond) or the timer period (fallback), then inject the
               timer vector to resume past the hlt and rescan. */
            for (int w = 0; w < (int)LAPIC_TIMER_PERIOD_MS; w++) {
                if (InterlockedExchange(&ap_wake_pending[cpu_id], 0)) break;
                Sleep(1);
            }
            ap_force_wake(cpu_id);
            break;
        case 0x1000: /* WHvRunVpExitReasonX64ApicInitSipiTrap -- ignore, AP already running */
            /* SIPI ignored -- AP already running */
            break;
        case WHvRunVpExitReasonMemoryAccess:
            if (ctx.MemoryAccess.Gpa >= LAPIC_BAR && ctx.MemoryAccess.Gpa < LAPIC_BAR + LAPIC_BAR_SIZE) {
                unsigned long long off = ctx.MemoryAccess.Gpa - LAPIC_BAR;
                /* Same rule as handle_device_mmio, and for the same reason:
                   decode the instruction, never guess it. A wrong length
                   resumes mid-instruction; a wrong register moves the wrong
                   value. This path used to assume RAX for both directions. */
                mmio_insn_t ap_insn = mmio_decode(ctx.MemoryAccess.InstructionBytes,
                                                  ctx.MemoryAccess.InstructionByteCount);
                int ap_ilen = ap_insn.len;
                if (ap_ilen == 0) {
                    fprintf(stderr, "SMP: AP %d cannot size instruction at RIP=0x%llx\n",
                        cpu_id, (unsigned long long)ctx.VpContext.Rip);
                    goto ap_done;
                }
                if (ctx.MemoryAccess.AccessInfo.AccessType == 0 && ap_insn.reg < 0) {
                    fprintf(stderr, "SMP: AP %d load with no register operand at RIP=0x%llx\n",
                        cpu_id, (unsigned long long)ctx.VpContext.Rip);
                    goto ap_done;
                }
                WHV_REGISTER_NAME rn[2];
                WHV_REGISTER_VALUE rv[2];
                int ap_nregs;
                if (ap_insn.reg >= 0) { rn[0] = mmio_gpr_names[ap_insn.reg]; rn[1] = WHvX64RegisterRip; ap_nregs = 2; }
                else                  { rn[0] = WHvX64RegisterRip; ap_nregs = 1; }
                WHvGetVirtualProcessorRegisters(partition, cpu_id, rn, ap_nregs, rv);
                if (ctx.MemoryAccess.AccessInfo.AccessType == 0) rv[0].Reg64 = lapic_read_cpu(cpu_id, off);
                else lapic_write_cpu(cpu_id, off, ap_insn.reg >= 0 ? (unsigned int)rv[0].Reg64 : ap_insn.imm);
                rv[ap_nregs - 1].Reg64 += ap_ilen;
                WHvSetVirtualProcessorRegisters(partition, cpu_id, rn, ap_nregs, rv);
            } else {
                /* Any other trapping access on an AP (HPET, IOAPIC, an unmapped
                   GPA, a device BAR). APs must not drive stateful devices off VP0,
                   but the old code fell through to break with RIP unchanged, so WHP
                   re-faulted the same instruction forever -- a latent hard hang.
                   Decode the length, read an unmapped device as 0, drop a write,
                   and step over it, mirroring the BSP's handle_device_mmio. */
                mmio_insn_t apx = mmio_decode(ctx.MemoryAccess.InstructionBytes,
                                              ctx.MemoryAccess.InstructionByteCount);
                if (apx.len == 0) {
                    fprintf(stderr, "SMP: AP %d unmapped MMIO GPA=0x%llx RIP=0x%llx, cannot size\n",
                        cpu_id, (unsigned long long)ctx.MemoryAccess.Gpa,
                        (unsigned long long)ctx.VpContext.Rip);
                    goto ap_done;
                }
                WHV_REGISTER_NAME xrn[2]; WHV_REGISTER_VALUE xrv[2]; int xnr;
                if (ctx.MemoryAccess.AccessInfo.AccessType == 0 && apx.reg >= 0) {
                    xrn[0] = mmio_gpr_names[apx.reg]; xrn[1] = WHvX64RegisterRip; xnr = 2;
                    WHvGetVirtualProcessorRegisters(partition, cpu_id, xrn, xnr, xrv);
                    xrv[0].Reg64 = 0;               /* unmapped device reads as 0 */
                    xrv[1].Reg64 += apx.len;
                } else {
                    xrn[0] = WHvX64RegisterRip; xnr = 1;
                    WHvGetVirtualProcessorRegisters(partition, cpu_id, xrn, xnr, xrv);
                    xrv[0].Reg64 += apx.len;        /* drop write, step over */
                }
                WHvSetVirtualProcessorRegisters(partition, cpu_id, xrn, xnr, xrv);
            }
            break;
        case WHvRunVpExitReasonX64IoPortAccess:
            {
                /* This used to advance RIP and discard the access. An AP that
                 * faulted therefore ran its exception handler, wrote the dump to
                 * the UART -- and the host threw every byte away. A fault on an
                 * application processor was invisible, which is not the same as
                 * it not happening. Serve COM1 so an AP can be heard.
                 *
                 * Only the serial port is served here. The full handle_io() is
                 * the boot processor's: it drives stateful devices (IDE, NIC,
                 * the GPU rasterizer) through a shadow register file for VP 0
                 * and is not safe to re-enter from another thread. An AP has no
                 * business touching those; it has business reporting that it
                 * died. */
                int port = ctx.IoPortAccess.PortNumber;
                int is_out = (ctx.IoPortAccess.AccessInfo.IsWrite != 0);
                WHV_REGISTER_NAME rn[2] = { WHvX64RegisterRax, WHvX64RegisterRip };
                WHV_REGISTER_VALUE rv[2];
                WHvGetVirtualProcessorRegisters(partition, cpu_id, rn, 2, rv);
                if (is_out) {
                    if (port == 0x3F8) output_buf_write((unsigned char)rv[0].Reg64);
                } else if (port == 0x3FD) {
                    rv[0].Reg64 = 0x60;   /* THR + TSR empty: transmit ready */
                } else {
                    rv[0].Reg64 = 0;
                }
                rv[1].Reg64 = ctx.VpContext.Rip +
                    (ctx.VpContext.InstructionLength ? ctx.VpContext.InstructionLength : 1);
                WHvSetVirtualProcessorRegisters(partition, cpu_id, rn, 2, rv);
            }
            break;
        case WHvRunVpExitReasonCanceled:
            break;
        default:
            fprintf(stderr, "SMP: AP %d exit reason %d at RIP=0x%llx\n",
                cpu_id, ctx.ExitReason, ctx.VpContext.Rip);
            goto ap_done;
        }
    }
ap_done:
    fprintf(stderr, "SMP: AP %d thread exiting\n", cpu_id);
}

/* ══ ACPI Tables ══ */
#define ACPI_BASE 0xE0000ULL  /* RSDP in the E-segment (ROM shadow area) */

static unsigned char acpi_checksum(unsigned char *data, int len) {
    unsigned char sum = 0;
    for (int i = 0; i < len; i++) sum += data[i];
    return (unsigned char)(0 - sum);
}

static void acpi_setup_tables(void *mem) {
    unsigned char *base = (unsigned char *)mem;
    unsigned int rsdt_addr = (unsigned int)(ACPI_BASE + 0x100);
    unsigned int fadt_addr = (unsigned int)(ACPI_BASE + 0x200);
    unsigned int xsdt_addr = (unsigned int)(ACPI_BASE + 0x300);
    unsigned int madt_addr = (unsigned int)(ACPI_BASE + 0x400);
    unsigned int dsdt_addr = (unsigned int)(ACPI_BASE + 0x600);
    /* DMAR sits past the DSDT rather than in the 0x500 gap: the MADT grows
       with the core count (44 + 8N + 12) and the DSDT with its AML, so the
       gaps between the fixed slots are not fixed. */
    unsigned int dmar_addr = (unsigned int)(ACPI_BASE + 0xA00);

    /* RSDP at ACPI_BASE. Revision 2 (ACPI 2.0+): 36 bytes, carrying BOTH the
       legacy 32-bit RSDT pointer and the 64-bit XSDT pointer, exactly as real
       firmware does. The first 20 bytes checksum to zero for 1.0 consumers;
       the full 36 checksum to zero via the extended byte at +32. */
    unsigned char *rsdp = base + ACPI_BASE;
    memset(rsdp, 0, 36);
    memcpy(rsdp, "RSD PTR ", 8);      /* signature */
    memcpy(rsdp + 9, "CODEX ", 6);    /* OEM ID */
    rsdp[15] = 2;                      /* revision = 2 (ACPI 2.0+) */
    *(unsigned int *)(rsdp + 16) = rsdt_addr;
    *(unsigned int *)(rsdp + 20) = 36;          /* Length */
    *(unsigned long long *)(rsdp + 24) = xsdt_addr;
    rsdp[8] = acpi_checksum(rsdp, 20);
    rsdp[32] = acpi_checksum(rsdp, 36);

    /* RSDT at +0x100 (header + 2 pointers: FADT, MADT, plus DMAR when it
       is present) */
    unsigned char *rsdt = base + rsdt_addr;
    int rsdt_len = 36 + 8 + (acpi_dmar ? 4 : 0);
    memset(rsdt, 0, rsdt_len);
    memcpy(rsdt, "RSDT", 4);
    *(unsigned int *)(rsdt + 4) = rsdt_len;
    rsdt[8] = 1; /* revision */
    memcpy(rsdt + 10, "CODEX ", 6);   /* OEM ID */
    memcpy(rsdt + 16, "CODEXVM ", 8); /* OEM table ID */
    *(unsigned int *)(rsdt + 24) = 1;  /* OEM revision */
    *(unsigned int *)(rsdt + 36) = fadt_addr;
    *(unsigned int *)(rsdt + 40) = madt_addr;
    if (acpi_dmar) *(unsigned int *)(rsdt + 44) = dmar_addr;
    rsdt[9] = acpi_checksum(rsdt, rsdt_len);

    /* XSDT at +0x300 (header + 2 sixty-four-bit pointers: FADT, MADT) */
    unsigned char *xsdt = base + xsdt_addr;
    int xsdt_len = 36 + 16 + (acpi_dmar ? 8 : 0);
    memset(xsdt, 0, xsdt_len);
    memcpy(xsdt, "XSDT", 4);
    *(unsigned int *)(xsdt + 4) = xsdt_len;
    xsdt[8] = 1; /* revision */
    memcpy(xsdt + 10, "CODEX ", 6);
    memcpy(xsdt + 16, "CODEXVM ", 8);
    *(unsigned int *)(xsdt + 24) = 1;  /* OEM revision */
    *(unsigned long long *)(xsdt + 36) = fadt_addr;
    *(unsigned long long *)(xsdt + 44) = madt_addr;
    if (acpi_dmar) *(unsigned long long *)(xsdt + 52) = dmar_addr;
    xsdt[9] = acpi_checksum(xsdt, xsdt_len);

    /* DMAR, for a stage that needs to tell "an IOMMU is described" from
       "it is not". Only the HEADER is modelled: signature, length, checksum,
       plus the two fields the table's own header carries, host address width
       and flags. There are NO remapping structures behind it, so a guest
       that walks its body finds an empty table rather than a description of
       units. That is enough for present-or-absent, which is what the
       pch-state stage asks, and it is not enough for anything else -- a
       reader must not take this as a modelled IOMMU.

       The ACPI table header layout is standard and no ACPI or VT-d document
       is in this tree, so this is the same status as the family-corroborated
       MAC offsets: the shape is right, nothing here cites it. */
    if (acpi_dmar) {
        unsigned char *dmar = base + dmar_addr;
        int dmar_len = 48;
        memset(dmar, 0, dmar_len);
        memcpy(dmar, "DMAR", 4);
        *(unsigned int *)(dmar + 4) = dmar_len;
        dmar[8] = 1;
        memcpy(dmar + 10, "CODEX ", 6);
        memcpy(dmar + 16, "CODEXVM ", 8);
        *(unsigned int *)(dmar + 24) = 1;
        dmar[36] = 38;   /* host address width, 39-bit reported as N-1 */
        dmar[37] = 0;    /* flags: INTR_REMAP clear */
        dmar[9] = acpi_checksum(dmar, dmar_len);
    }

    /* FADT at +0x200 (116 bytes, revision 1). Field offsets are the ACPI
       spec's, not an approximation: FIRMWARE_CTRL 36, DSDT 40, SCI_INT 46,
       SMI_CMD 48, PM1a_EVT_BLK 56, PM1b_EVT_BLK 60, PM1a_CNT_BLK 64,
       PM1b_CNT_BLK 68, PM2_CNT_BLK 72, PM_TMR_BLK 76. (Through 2026-07-09
       this table put DSDT at 36 and the PM1a event/control blocks at 64/72
       -- a spec-derived parser read the event block as the control block.
       No guest parsed ACPI, so nothing depended on the wrong layout.)
       The port values match QEMU's PIIX4: events at 0x600, control at
       0x604. */
    unsigned char *fadt = base + fadt_addr;
    memset(fadt, 0, 129);
    memcpy(fadt, "FACP", 4);
    *(unsigned int *)(fadt + 4) = 129;
    fadt[8] = 1; /* revision */
    memcpy(fadt + 10, "CODEX ", 6);
    memcpy(fadt + 16, "CODEXVM ", 8);
    *(unsigned int *)(fadt + 36) = 0;          /* FIRMWARE_CTRL (no FACS) */
    *(unsigned int *)(fadt + 40) = dsdt_addr;  /* DSDT address */
    fadt[45] = 1;                               /* preferred PM profile: desktop */
    *(unsigned short *)(fadt + 46) = 9;        /* SCI interrupt: a GSI, as
                                                  QEMU's PIIX4 publishes and
                                                  this table's 0x600/0x604
                                                  ports already imitate. Was
                                                  0x2000, which is not an
                                                  interrupt number at all --
                                                  the IOAPIC has 24 entries. */
    *(unsigned int *)(fadt + 48) = 0xB004;     /* SMI command port (Bochs compat) */
    fadt[52] = 0xF1;                            /* ACPI enable value */
    fadt[53] = 0xF0;                            /* ACPI disable value */
    *(unsigned int *)(fadt + 56) = 0x600;      /* PM1a event block */
    *(unsigned int *)(fadt + 60) = 0;          /* PM1b event block */
    *(unsigned int *)(fadt + 64) = 0x604;      /* PM1a control block */
    *(unsigned int *)(fadt + 68) = 0;          /* PM1b control block */
    fadt[88] = 4; /* PM1 event length */
    fadt[89] = 2; /* PM1 control length */
    *(unsigned short *)(fadt + 109) = (1 << 10) | (1 << 5); /* boot flags: 8042, no VGA */
    /* Reset register, as real chipsets publish it: flags bit 10 declares
       it, the GAS at 116 names I/O port 0xCF9, RESET_VALUE 0x06 pulses a
       full reset. Extends the FADT to the ACPI 2.0 length (129). */
    *(unsigned int *)(fadt + 112) = (1u << 10);   /* RESET_REG_SUP */
    fadt[116] = 1;                                 /* address space: system I/O */
    fadt[117] = 8;                                 /* register bit width */
    fadt[119] = 1;                                 /* access size: byte */
    *(unsigned long long *)(fadt + 120) = 0xCF9;
    fadt[128] = 0x06;                              /* reset value */
    fadt[9] = acpi_checksum(fadt, 129);

    /* DSDT at +0x600: header plus the one AML object an OS needs to power the
       machine off -- Name (_S5_, Package (4) { 0, 0, 0, 0 }).
         08            NameOp
         5F 53 35 5F   "_S5_"
         12            PackageOp
         06            PkgLength (1 byte: itself + 5 following)
         04            NumElements
         00 00 00 00   ZeroOp x4  (SLP_TYPa = 0, SLP_TYPb = 0, rsvd, rsvd)
       SLP_TYPa = 0 matches QEMU's PIIX4/q35, so a driver that reads _S5_ and
       writes (SLP_TYPa << 10) | SLP_EN to PM1a_CNT emits the same 0x2000 to
       port 0x604 here and on QEMU. */
    unsigned char *dsdt = base + dsdt_addr;
    static const unsigned char s5_aml[] = {
        0x08, 0x5F, 0x53, 0x35, 0x5F, 0x12, 0x06, 0x04, 0x00, 0x00, 0x00, 0x00
    };
    int dsdt_len = 36 + (int)sizeof(s5_aml);
    memset(dsdt, 0, dsdt_len);
    memcpy(dsdt, "DSDT", 4);
    *(unsigned int *)(dsdt + 4) = dsdt_len;
    dsdt[8] = 1;
    memcpy(dsdt + 10, "CODEX ", 6);
    memcpy(dsdt + 16, "CODEXVM ", 8);
    memcpy(dsdt + 36, s5_aml, sizeof(s5_aml));
    dsdt[9] = acpi_checksum(dsdt, dsdt_len);

    /* MADT at +0x400 */
    unsigned char *madt = base + madt_addr;
    int num_cpus = smp_cores > 1 ? smp_cores : 1;
    int madt_len = 44 + num_cpus * 8 + 12;
    memset(madt, 0, madt_len);
    memcpy(madt, "APIC", 4);
    *(unsigned int *)(madt + 4) = madt_len;
    madt[8] = 3; /* revision */
    memcpy(madt + 10, "CODEX ", 6);
    memcpy(madt + 16, "CODEXVM ", 8);
    *(unsigned int *)(madt + 36) = 0xFEE00000;  /* local APIC address */
    *(unsigned int *)(madt + 40) = 1;           /* flags: PCAT_COMPAT */
    for (int i = 0; i < num_cpus; i++) {
        int off = 44 + i * 8;
        madt[off] = 0; madt[off+1] = 8;
        madt[off+2] = (unsigned char)i; madt[off+3] = (unsigned char)i;
        *(unsigned int *)(madt + off + 4) = 1;
    }
    int io_off = 44 + num_cpus * 8;
    madt[io_off] = 1; madt[io_off+1] = 12; madt[io_off+2] = 0;
    *(unsigned int *)(madt + io_off + 4) = 0xFEC00000;
    *(unsigned int *)(madt + io_off + 8) = 0;
    madt[9] = acpi_checksum(madt, madt_len);

    fprintf(stderr, "ACPI: RSDP=0x%llx RSDT=0x%x XSDT=0x%x FADT=0x%x MADT=0x%x DSDT=0x%x\n",
        ACPI_BASE, rsdt_addr, xsdt_addr, fadt_addr, madt_addr, dsdt_addr);
}

/* ══ SMBIOS Tables ══ */
static void smbios_setup_tables(void *mem) {
    unsigned char *base = (unsigned char *)mem;
    unsigned int table_addr = 0xE0800; /* after ACPI tables */
    unsigned int entry_addr = 0xE0780;

    /* SMBIOS structures at table_addr */
    unsigned char *t = base + table_addr;
    int off = 0;

    /* Type 0: BIOS Information */
    t[off+0] = 0; t[off+1] = 24; /* type, length */
    *(unsigned short*)(t+off+2) = 0; /* handle */
    t[off+4] = 1; /* vendor string index */
    t[off+5] = 2; /* BIOS version string index */
    *(unsigned short*)(t+off+6) = 0xE800; /* BIOS start segment */
    t[off+8] = 3; /* release date string index */
    t[off+9] = 0; /* BIOS ROM size (64K*(n+1)) */
    off += 24;
    /* strings: vendor, version, date, then double-null */
    /* 21, not 22: the literal's own terminator is not part of the set, and a
       stray NUL after the set's double NUL reads as a zero-length structure
       to a spec walker, which stopped the diagnostic ladder at type 0 (2026-08-18). */
    memcpy(t+off, "Codex\0" "1.0\0" "05/23/2026\0", 21); off += 21;
    t[off++] = 0; /* end of strings */

    /* Type 1: System Information */
    int sys_off = off;
    t[off+0] = 1; t[off+1] = 27; /* type, length */
    *(unsigned short*)(t+off+2) = 1; /* handle */
    t[off+4] = 1; /* manufacturer string */
    t[off+5] = 2; /* product name string */
    t[off+6] = 3; /* version string */
    t[off+7] = 0; /* serial number */
    off += 27;
    memcpy(t+off, "Codex Project\0" "Codex VM\0" "1.0\0", 27); off += 27;
    t[off++] = 0;

    /* Type 2: Baseboard */
    int bb_off = off;
    t[off+0] = 2; t[off+1] = 15; /* type, length */
    *(unsigned short*)(t+off+2) = 2; /* handle */
    t[off+4] = 1; /* manufacturer */
    t[off+5] = 2; /* product */
    off += 15;
    memcpy(t+off, "Codex\0" "Virtual Board\0", 20); off += 20;
    t[off++] = 0;

    /* Type 17: Memory Device, one DIMM sized from -mem (size in MB at +12,
       0x7FFF meaning the extended u32 at +28 holds it; DDR4 at +18; the
       manufacturer string at +23). Added 2026-08-18 so a diagnostic's RAM
       row has something to sum. */
    {
        unsigned long long mb = guest_mem_size / (1024ULL * 1024ULL);
        t[off+0] = 17; t[off+1] = 40;
        *(unsigned short*)(t+off+2) = 3;
        t[off+4] = 0xFF; t[off+5] = 0xFF; t[off+6] = 0xFF; t[off+7] = 0xFF;
        *(unsigned short*)(t+off+8) = 64; *(unsigned short*)(t+off+10) = 64;
        if (mb < 0x7FFF) *(unsigned short*)(t+off+12) = (unsigned short)mb;
        else { *(unsigned short*)(t+off+12) = 0x7FFF; *(unsigned int*)(t+off+28) = (unsigned int)mb; }
        t[off+14] = 9; t[off+16] = 1; t[off+17] = 2; t[off+18] = 0x1A; t[off+23] = 3;
        off += 40;
        memcpy(t+off, "DIMM0\0" "BANK0\0" "Codex\0", 18); off += 18;
        t[off++] = 0;
    }

    /* Type 127: End of Table */
    t[off+0] = 127; t[off+1] = 4;
    *(unsigned short*)(t+off+2) = 0xFFFE;
    off += 4; t[off++] = 0; t[off++] = 0;

    /* SMBIOS Entry Point at entry_addr (32 bytes, SMBIOS 2.1) */
    unsigned char *ep = base + entry_addr;
    memset(ep, 0, 32);
    memcpy(ep, "_SM_", 4);
    ep[5] = 31; /* entry point length */
    ep[6] = 2; ep[7] = 1; /* SMBIOS version 2.1 */
    *(unsigned short*)(ep+8) = (unsigned short)off; /* max structure size */
    memcpy(ep+16, "_DMI_", 5);
    *(unsigned short*)(ep+22) = (unsigned short)off; /* structure table length */
    *(unsigned int*)(ep+24) = table_addr; /* structure table address */
    *(unsigned short*)(ep+28) = 5; /* number of structures */
    /* checksums */
    unsigned char sum = 0;
    for (int i = 16; i < 31; i++) sum += ep[i];
    ep[21] = (unsigned char)(0 - sum);
    sum = 0;
    for (int i = 0; i < 31; i++) sum += ep[i];
    ep[4] = (unsigned char)(0 - sum);

    fprintf(stderr, "SMBIOS: entry=0x%x tables=0x%x (%d bytes)\n", entry_addr, table_addr, off);

    /* The 3.0 entry point (SMBIOS spec 5.2.2: `_SM3_`, checksum at 5, length
       0x18, major/minor/docrev at 7-9, revision 1 at 10, structure table
       maximum size u32 at 12, address u64 at 16) at 0xF0B00 in the UEFI table
       page, published there by uefi_setup_tables. Only in UEFI mode: the page
       is the fake SystemTable's. */
    if (uefi_mode) {
        unsigned char *e3 = base + UEFI_TABLE_PAGE + 0xB00;
        memset(e3, 0, 0x18);
        memcpy(e3, "_SM3_", 5);
        e3[6] = 0x18; e3[7] = 3; e3[8] = 2; e3[9] = 0; e3[10] = 1;
        *(unsigned int*)(e3+12) = (unsigned int)off;
        *(unsigned long long*)(e3+16) = table_addr;
        unsigned char s3 = 0;
        for (int i = 0; i < 0x18; i++) s3 = (unsigned char)(s3 + e3[i]);
        e3[5] = (unsigned char)(0x100 - s3);
    }
}

/* Length of the instruction that took an MMIO exit, so RIP can be stepped past
 * it exactly.
 *
 * Neither field WHP offers is a length. VpContext.InstructionLength reads 0 on
 * these exits, and MemoryAccess.InstructionByteCount is the count of bytes
 * copied into the InstructionBytes window -- always 16, the size of the buffer.
 * Taking either as a length walks RIP into the middle of an instruction: the
 * old code assumed 2 and a 3-byte REX-prefixed device store resumed on its own
 * ModRM byte, raising #UD.
 *
 * The bytes themselves are real, so decode them. This covers the MOV forms a
 * device access can take (and the two-byte 0x0F escapes), which is the whole
 * vocabulary the guest uses to reach a register window. Returns 0 if the
 * instruction is not one we can size, and the caller says so rather than
 * guessing. */
/* Decode a MOV between a register and memory: its length, and which operand
   holds the value. The register is ModRM's reg field extended by REX.R --
   the same byte the length walk already has to read to find the
   displacement, which is why one function returns both.

   This replaces a guess. The write path used to take the value out of RAX
   for the LAPIC and RDX for every other device, and the read path always
   landed the result in RAX. Both fit the drivers they were written against
   and nothing else: the compiler's own SMP bring-up loads RAX
   (X86_64Boot's emit-lapic-write), while the MMIO builtin a guest driver
   uses emits `mov [rdi], edx`. A guest writing 0x1FF to the LAPIC's SIVR
   got back 0xFE -- whatever happened to be in RAX. EOI hid it, because its
   value is architecturally discarded, so the one register a guest could
   reach worked by accident. */
static mmio_insn_t mmio_decode(const unsigned char *b, int n) {
    mmio_insn_t r;
    r.len = 0; r.reg = -1; r.imm = 0;
    int i = 0;
    int opsize16 = 0;
    unsigned char rex = 0;
    if (n <= 0) return r;
    for (; i < n; i++) {
        unsigned char c = b[i];
        if (c == 0x66) { opsize16 = 1; continue; }
        if (c == 0x67 || c == 0xF0 || c == 0xF2 || c == 0xF3 ||
            c == 0x2E || c == 0x36 || c == 0x3E || c == 0x26 ||
            c == 0x64 || c == 0x65) continue;
        break;
    }
    if (i < n && (b[i] & 0xF0) == 0x40) { rex = b[i]; i++; }   /* REX */
    if (i >= n) return r;
    unsigned char op = b[i++];
    int two = 0;
    if (op == 0x0F) {
        if (i >= n) return r;
        op = b[i++];
        two = 1;
    }
    int immsz = 0;
    int reg_is_operand = 1;
    if (!two) {
        switch (op) {
            case 0x88: case 0x89: case 0x8A: case 0x8B: immsz = 0; break;
            /* MOV r/m, imm: ModRM.reg is an opcode extension, not a register. */
            case 0xC6: immsz = 1; reg_is_operand = 0; break;
            case 0xC7: immsz = opsize16 ? 2 : 4; reg_is_operand = 0; break;
            default: return r;
        }
    } else {
        switch (op) {
            case 0xB6: case 0xB7: case 0xBE: case 0xBF: immsz = 0; break;
            default: return r;
        }
    }
    if (i >= n) return r;
    unsigned char modrm = b[i++];
    int mod = (modrm >> 6) & 3;
    int rm  = modrm & 7;
    if (mod != 3) {
        if (rm == 4) {
            if (i >= n) return r;
            unsigned char sib = b[i++];
            if (mod == 0 && (sib & 7) == 5) i += 4;
        } else if (mod == 0 && rm == 5) {
            i += 4;                                    /* RIP-relative disp32 */
        }
        if (mod == 1) i += 1;
        else if (mod == 2) i += 4;
    }
    if (reg_is_operand) {
        r.reg = ((modrm >> 3) & 7) | ((rex & 0x04) ? 8 : 0);   /* REX.R */
    } else {
        if (i + immsz > n) return r;
        unsigned int v = 0;
        for (int k = 0; k < immsz; k++) v |= (unsigned int)b[i + k] << (8 * k);
        r.imm = v;
        r.reg = -1;
    }
    i += immsz;
    if (i > n) return r;
    r.len = i;
    return r;
}

static int mmio_insn_len(const unsigned char *b, int n) {
    return mmio_decode(b, n).len;
}

/* ══ Intel e1000e NIC model (Device Emulation Catalog entry 1) ══
   Register offsets and bit positions are from the 8254x/8257x family
   datasheet, the same reading E1000e.codex was written from -- which is
   exactly why this model is not an independent oracle. */
#define E1000_BAR       0xFE400000ULL
#define E1000_BAR_SIZE  0x20000

#define E1000_REG_CTRL     0x0000
#define E1000_REG_STATUS   0x0008
#define E1000_REG_MDIC     0x0020
#define E1000_REG_ICR      0x00C0
#define E1000_REG_RCTL     0x0100
#define E1000_REG_TCTL     0x0400
#define E1000_REG_RDBAL    0x2800
#define E1000_REG_RDBAH    0x2804
#define E1000_REG_RDLEN    0x2808
#define E1000_REG_RDH      0x2810
#define E1000_REG_RDT      0x2818
#define E1000_REG_TDBAL    0x3800
#define E1000_REG_TDBAH    0x3804
#define E1000_REG_TDLEN    0x3808
#define E1000_REG_TDH      0x3810
#define E1000_REG_TDT      0x3818
#define E1000_REG_RAL      0x5400
#define E1000_REG_RAH      0x5404

/* Statistics, and only the receive side this bed can honestly move. These are
   the counters that answer "did a frame ever reach the MAC" INDEPENDENTLY of
   the descriptor ring, which is the one question a driver cannot ask of its
   own ring: a ring that shows nothing looks the same whether the wire was
   quiet or the writeback went somewhere else.

   GPRC and RNBC are modelled: incremented at the two places a received frame
   can go, delivered or dropped for want of a descriptor. CRCERRS and MPC are
   defined here for the guest to read on METAL and are never incremented by
   this model -- nothing here corrupts a frame or overruns a FIFO -- so a bed
   reading 0 from them means "not modelled" and not "measured zero". Any arm
   that asserts on those two is asserting a stub.

   CLEAR ON READ, as the silicon does (8254x 13.4): a reader gets the count
   since the last read, so reading twice reports the second interval and not
   the total. */
#define E1000_REG_CRCERRS  0x4000
#define E1000_REG_MPC      0x4010
#define E1000_REG_GPRC     0x4074
#define E1000_REG_RNBC     0x40A0

#define E1000_CTRL_RST     0x04000000u
#define E1000_CTRL_SLU     0x00000040u
#define E1000_CTRL_ASDE    0x00000020u
#define E1000_STATUS_LU    0x00000002u

/* Speed reporting, which is the field B2 Finding 4 turns on.
   82583V STATUS (line 12590): SPEED 7:6, "Reflects speed setting of the MAC
   and/or link"; ASDV 9:8, "Auto-Speed Detection Value -- speed result sensed
   by the MAC auto-detection function". Both encode 00b=10, 01b=100,
   10b/11b=1000. CTRL.SPEED is 9:8, and line 12640 says STATUS.SPEED reflects
   "(CTRL.SPEED) or MAC auto-speed detection used". */
#define E1000_CTRL_SPEED_SH   8
#define E1000_STATUS_SPEED_SH 6
#define E1000_STATUS_ASDV_SH  8
#define E1000_SPEED_10     0u
#define E1000_SPEED_1000   2u

/* ASDV is diagnostic only (12648): "The ASDV bits are provided for diagnostics
   purposes only. Even if the MAC speed configuration is not set using this
   function (ASDE=0b), the ASD calculation can be initiated by software writing
   a logic one to the CTRL_EXT.ASDCHK bit." Recorded, not modelled -- see the
   -e1000-asde block for why. */
#define E1000_RCTL_EN      0x00000002u
#define E1000_RAH_AV       0x80000000u
#define E1000_RXD_STAT_DD  0x01
#define E1000_RXD_STAT_EOP 0x02
#define E1000_TXD_STAT_DD  0x01

/* MDIC, the management interface to the PHY. On the 8254x the PHY is a
   discrete part on an MDIO bus; on the I219 the MAC and PHY are both inside
   the PCH and the same register reaches it. Either way this is the ONLY way
   to read or write a PHY register, and until now the model had no PHY at
   all, so every PHY-shaped defect in the driver was invisible here by
   construction.

   Layout, from the same family datasheet as the rest of this model:
   bits 15:0 DATA, 20:16 REGADD, 25:21 PHYADD, 27:26 OP, 28 R (ready),
   29 I (interrupt enable), 30 E (error). OP is 01 write, 10 read. Software
   writes the register with R clear and polls until the MAC sets R. */
#define E1000_MDIC_DATA    0x0000FFFFu
#define E1000_MDIC_REG_SH  16
#define E1000_MDIC_PHY_SH  21
#define E1000_MDIC_OP_SH   26
#define E1000_MDIC_OP_MASK 0x0C000000u
#define E1000_MDIC_OP_WR   0x04000000u
#define E1000_MDIC_OP_RD   0x08000000u
#define E1000_MDIC_R       0x10000000u
#define E1000_MDIC_E       0x40000000u

/* The PHY answers at one address. A read of any other address is an error
   rather than a zero, because that is what silicon does when nothing is
   listening, and a driver that scans addresses must be able to tell the
   difference. */
#define E1000_PHY_ADDR     1

#define E1000_PHY_BMCR     0
#define E1000_PHY_BMSR     1
#define E1000_PHY_ID1      2
#define E1000_PHY_ID2      3

/* == I219 (8086:15b8), and ONLY what the I219 datasheet states ==

   The part on Damian's ASUS is a PCH device: the MAC is in the chipset and
   the I219 is the PHY. This model adds the one requirement with a named
   failure mode, K1 at 1 Gbps, so a bed run can tell a driver that
   configures it from one that does not. Everything else in the eight-row
   table (ULP, SWFLAG, SMBus/LANPHYPC, LCD reload, LTR) is still absent and
   an arm must not read this model's silence as agreement.

   CITED, I219 datasheet rev 2.02 section 9.5.5.2, "PCIe Power Management
   Control PHY Address 01, Page 770, Register 17":
     bit 13 Giga_K1_disable, RW, default 0b -- "When set, the I219 does not
            enter K1 while link speed at 1000 Mb/s."
     bit 14 K1 enable,       RW, default 0b -- "Enable K1 Power Save Mode."
   Section 9.3 gives the addressing: register 31 at PHY address 01 is the
   page register and belongs to no page, which is the path this model
   already uses to reach page 769.

   MODELLED, NOT CITED, and the distinction is the point: the datasheet
   documents the CONTROL, not the consequence. That K1 left enabled at
   1 Gbps stalls the MAC is the vendor driver's behaviour
   (e1000_configure_k1_ich8lan), and it is the proposition the campaign
   exists to test. The model makes it EXPRESSIBLE; it does not assert that
   silicon does this. A green arm here is evidence about the driver's
   configuration step and about nothing else.

   The power-on value is the platform's NVM setting, which we do not have
   for this board, so it is a knob rather than an invention: -i219-k1-nvm 1
   (the default) powers up with K1 enabled and Giga_K1_disable clear, which
   is the condition the campaign is about; 0 powers up with K1 off. */
#define I219_DEVICE_ID     0x15B8
#define I219_PCIE_PM_PAGE  770
#define I219_PCIE_PM_REG   17
#define I219_K1_GIGA_DIS   0x2000u
#define I219_K1_ENABLE     0x4000u

/* The MDIO/NVM semaphore, the second requirement with a specified
   mechanism. CITED in two halves with DIFFERENT strengths, and the
   difference is recorded rather than smoothed over:

   The PROTOCOL is cited exactly, 82583V rev 2.6 section 4.5.2: a request is
   registered by writing 1b to your own ownership bit; the requester is
   granted access only when that same bit READS BACK 1b, which it does only
   while the other two are 0b; at most one bit is set at any time; the owner
   writes 0b when done. SW and HW clear on reset, MNG clears only on
   LAN_PWR_GOOD or by firmware.

   The OFFSET is corroborated by FAMILY and not cited for the part: 0x00F00
   with SW ownership at bit 5, HW at 6, MNG at 7 is 82583V section 9.2.2.15,
   and the I219's MAC is in the PCH, whose CSR map is in no document we
   hold. A model built on it inherits that gap.

   Enforcement is its own flag, -i219-swflag, rather than riding -i219. Two
   requirements enforced by one switch cannot be told apart by an arm: a
   driver that fails would fail for either reason and the pair would prove
   nothing about which. */
#define I219_EXTCNF_CTRL   0x00F00
#define I219_EXTCNF_SW     0x0020u
#define I219_EXTCNF_HW     0x0040u
#define I219_EXTCNF_MNG    0x0080u

/* ULP Configuration 1, PHY page 779 register 16, I219 datasheet 9.5.7.1.
   Field table cited in full: START 0, SW_ACCESS 1, ULP_IND 2, STICKY_ULP 4
   (enter ULP on link disconnect), INBAND_EXIT 5, WOL_HOST 6, WOL_ME 7,
   RESER_TO_SMBUS 8, EN_1G_SMBUS 9, EN_ULP_LANPHYPC 10 (enter ULP on LAN
   disable), reserved 13:11, FORCE_ULP 14, RESET_ULP_IND 15. The documented
   default of every one of those fields is 0b. Register 17 on the same page
   is ULP Configuration 2 (9.5.7.2, MESHADOW 4:0) and is not modelled.

   THE POWER-ON VALUE IS MEASURED AND IS NOT THE DOCUMENTED DEFAULT. The
   board answers 0x0800 -- bit 11, inside the reserved 13:11 field -- and did
   so on two independent flights, sittings 8 and 9, each banking
   `ulp 779.16=0800` in its own DIAG.TXT. Powering up at the documented
   0x0000 would disagree with the only part anyone has read.

   Both ULP-ENTRY BITS ARE CLEAR ON THAT BOARD, so a driver's entry-disable
   write has nothing to change and no arm can see it happen. That is what
   -i219-ulp-armed is for: it powers the register up with STICKY_ULP and
   EN_ULP_LANPHYPC SET so the write has something to clear, which is what
   separates the reaching arm from the skipping one. Without it both arms are
   the same run and the pair proves nothing.

   A PHY reset does NOT clear this register here. The datasheet does not say
   what a PHY reset does to it, so that is a modelling choice and not a
   citation; it is the choice that keeps an armed value alive across a driver
   which resets before writing, and an arm whose subject a reset silently
   wipes could never fire. */
#define I219_ULP_PAGE         779
#define I219_ULP_CFG1_REG     16
#define I219_ULP_STICKY       0x0010u  /* bit 4 */
#define I219_ULP_EN_LANPHYPC  0x0400u  /* bit 10 */
#define I219_ULP_CFG1_BOARD   0x0800u  /* measured, sittings 8 and 9 */

#define E1000_BMCR_RESET   0x8000
#define E1000_BMCR_ANEG_EN 0x1000
#define E1000_BMCR_ANEG_RST 0x0200
#define E1000_BMSR_ANEG_DONE 0x0020
#define E1000_BMSR_LINK    0x0004

static int e1000_present = 0;
/* Every one of these exists so the model can REFUSE. A model that can
   only succeed is not a test of the driver's failure handling. */
static int e1000_fault_no_reset = 0;
static int e1000_fault_no_link  = 0;
static int e1000_fault_no_mac   = 0;
static int e1000_fault_no_tx_dd = 0;
/* -e1000-rdh-ro: RDH ignores writes, the way CTRL does on the I219-V. This
   exists for the discriminator NIC-4 rests on. DiagNicRing writes RDH=7,
   reads it back and prints rdh-writable=y/n to tell "frames are moving" from
   "RDH is not ours to write"; without this switch every bed run answers y,
   the n branch has never executed anywhere, and the sitting would be reading
   a field nothing has shown can say no. */
static int e1000_fault_rdh_ro   = 0;
/* -i219: present 8086:15b8 and model the K1 requirement above. OFF by
   default, so the 82540EM every existing arm runs against is unchanged. */
static int i219_present         = 0;
static int i219_k1_nvm          = 1;
static unsigned short i219_k1_reg = 0;
static unsigned short i219_ulp_cfg1 = I219_ULP_CFG1_BOARD;
/* -i219-swflag: MDIO is refused unless the caller holds SW ownership.
   -i219-mng-holds: firmware is holding the MNG bit, so an acquisition
   attempt is refused however correct the driver's protocol is, which is the
   case a driver that assumes it always wins cannot survive. */
static int i219_swflag_enforce  = 0;
static int i219_mng_holds       = 0;
static unsigned int i219_extcnf = 0;
static int e1000_inject_want    = 0;
static int e1000_nat            = 0;
static int e1000_strict_filter  = 0;
/* -pci-bridge: place a PCI-to-PCI bridge on bus 0 forwarding to bus 1, with a
   device behind it, so pci-scan-all's descent branch (header type 1, read the
   secondary bus, recurse) actually executes. OFF by default (L-FALLBACK): the
   bridge is a new device on bus 0, so turning it on unconditionally would move
   every existing pci-scan-bus-0 count. Found by blu: pci_add_device hardcoded
   header type 0, so no bridge existed and the descent could not run here. */
static int pci_bridge           = 0;
/* -pci-bridge-deep: a second bridge behind the first, so the descent recurses
   past one level. See where the devices are built for why it is its own flag
   rather than a change to -pci-bridge. */
static int pci_bridge_deep      = 0;
/* -pci-bridge-levels N: N chained bridges, bus 0 -> 1 -> ... -> N, each with
   an endpoint on the bus it forwards to. 1 is what -pci-bridge builds and 2 is
   -pci-bridge-deep, so the three are one mechanism and the older flags keep
   their exact topologies.

   THE POINT IS THE LEVELS THE GUEST REFUSES, not the ones it walks.
   pci-scan-max-depth is 3 and pci-collect's `depth > max` arm has never
   executed anywhere, because no bed has ever presented a fourth level. A fuel
   cap that has never been shown to say no is not a cap, it is an assertion
   (L-FALSIF), and this project has found a defect behind almost every guard of
   that shape. -pci-bridge-levels 4 is what makes it fire.

   -pci-bridge-backward points the deepest bridge at a bus number at or below
   its own, which is the other untravelled branch: pci-bridge-one descends only
   when sec > bus, and nothing has ever handed it a bridge that fails that
   test. A real one does exist -- an unconfigured bridge reads 0 there. */
static int pci_bridge_levels    = 0;
static int pci_bridge_backward  = 0;
/* -e1000-inject-armed: hold the canned frames until the guest has READ GPRC,
   so they arrive after a stage's own opening reading and inside its window.

   THE INJECTOR EMPTIES ITS WHOLE BUDGET AT RCTL.EN, and that is one stage too
   early for anything the diagnostic image wants to ask. DiagNicRing reads GPRC
   twice on purpose -- once before its attach, banked as `pre`, and once at the
   end as `gp` -- so that a frame counted in an EARLIER stage's window cannot be
   read as one that arrived while this stage was looking (DiagNicRing.codex,
   "GPRC was read TWICE"). Measured 2026-08-21 on the 18799 image: with
   -e1000-inject 1 the reading is pre=1 gp=0, which is the counter working
   exactly as designed and the frame arriving in nicinit. So sitting 9's row --
   gp above zero with no descriptor writeback -- was not expressible here
   whatever the FAULT flags said, and the reason was WHEN the frame arrives
   rather than what the MAC does with it.

   KEYED ON THE GPRC READ AND NOT ON AN ORDINAL, deliberately. Counting RDT
   writes also works and was built first: measured, `-e1000-inject-late 2` puts
   the frame in the nicring window and 1 puts it in nicinit, because bring-up
   writes the tail once and the listener recycles once. But that is a property
   of the whole run rather than of the thing under test, and this is the exact
   defect `diag-arm.ps1` documents at length for `-usb-bot-drop N`: inserting a
   stage moved that arm's drop into a different phase and it reported a
   plausible wrong word. A band one value wide would have rotted the first time
   anyone touched the bring-up.

   The GPRC read is the stage's OWN action, it is what separates `pre` from
   `gp`, and it cannot be moved by anything upstream. */
static int e1000_inject_armed   = 0;
static int e1000_gprc_reads     = 0;
/* -e1000-no-phy: MDIC never reports ready, which is a PHY that is not
   answering. -e1000-phy-err: it reports the error bit instead. The two are
   different failures and a driver can confuse them, which is the reason
   both exist rather than one. */
static int e1000_fault_no_phy   = 0;
static int e1000_fault_phy_err  = 0;
/* -e1000-ctrl-ro: CTRL is READ-ONLY to the driver. Writes are discarded
   whole and the register keeps the value firmware left in it.

   THIS IS MEASURED, not invented. Damian's ASUS, Intel I219-V at 00:1f.6,
   2026-08-13: the driver cleared CTRL.SLU and read CTRL straight back, four
   rows across two flights, and it answered 0x180240 -- the firmware value,
   SLU still set -- every time. The same flights showed CTRL.ASDE refusing
   to set. STATUS reads, MDIC reads AND MDIC WRITES all work on that part,
   so this is CTRL specifically and NOT the CSR write path: e1000-phy-write
   writes MDIC at 0x0020 and polls it ready, and it succeeds every arm.

   The I219 is the PHY; the MAC is inside the PCH and its link configuration
   is owned by the integrated LAN controller and the ME, which is the split
   the datasheet describes (Table 5-1, and 9.2's note that the LAN
   controller configures the LCD registers). Nothing public in this tree
   says CTRL is read-only to the host, so this flag models the OBSERVATION
   and does not claim the mechanism.

   Why it earns a flag: with CTRL writes discarded, e1000-reset never sets
   RST, so e1000-await-reset sees it clear on its first read and answers
   settled=1. A reset that never happened is indistinguishable from one that
   completed, and on 2026-08-13 that read as "the reset works on a warm
   part". OFF by default (L-FALLBACK): every existing green keeps the
   writable-CTRL floor it was measured on. */
static int e1000_ctrl_ro        = 0;
#define E1000_CTRL_FIRMWARE_VALUE 0x180240u
/* -e1000-preconfigured: the part arrives with the RECEIVER ALREADY RUNNING,
   which is the state firmware and the ME leave a PCH LAN device in when the
   driver cannot reset it.

   This exists because -e1000-ctrl-ro made a second assumption visible.
   e1000-reset is discarded on that part, so e1000-init proceeds on a device
   nobody quiesced, and e1000-setup-rx programs RDBAL/RDBAH/RDLEN/RDH/RDT and
   only THEN sets RCTL.EN. That order is correct after a reset, when the
   receiver is already off, and it is undefined while the receiver is live:
   the ring is being reprogrammed underneath a device that may be DMAing into
   it.

   The model refuses in a way the driver can notice. A ring register written
   while RCTL.EN is set POISONS the ring and no frame is delivered into it.
   Writing RCTL with EN clear is a real quiesce and clears the poison, so a
   driver that disables the receiver, programs the ring and then enables
   works, and one that programs under a live receiver gets silence.

   OFF by default (L-FALLBACK): every existing green keeps the
   freshly-reset-device floor it was measured on. */
static int e1000_preconfigured  = 0;
static int e1000_ring_poisoned  = 0;
/* -e1000-phy-link: STATUS.LU requires the PHY to have finished
   auto-negotiation, not merely CTRL.SLU. OFF by default, deliberately: the
   default keeps the SLU-only behaviour every existing green was measured
   against, so this flag ADDS an arm rather than moving the floor
   (L-FALLBACK). It is the arm that catches a driver which never touches the
   PHY at all -- which is every version of ours before this change, and
   which on a PCH-integrated part is exactly the silent failure. */
static int e1000_phy_link       = 0;

/* -e1000-mdio-window: MDIC answers nothing until 10 ms have passed since the
   guest wrote CTRL.RST. Intel I219 datasheet rev 2.02, section 9.2 "MDIO
   Access", page 88: "After LCD reset to the I219 a delay of 10 ms is required
   before attempting to access MDIO registers."

   A closed window answers with neither R nor E, which is deliberately the
   same shape -e1000-no-phy produces. Silicon that is not listening yet and
   silicon that is not there cannot be told apart by the driver, and a model
   that made them distinguishable would let a driver pass by reading a
   difference the real part does not offer.

   OFF by default (L-FALLBACK): every existing green was measured with no
   window at all, so this ADDS an arm rather than moving the floor.

   THE ASSUMPTION, and it is not ours to settle: the datasheet says the window
   opens at an LCD reset, which is a reset of the LAN Connected Device. This
   arm opens it at CTRL.RST, which the 82583V calls a reset of the MAC
   function and which that datasheet distinguishes from CTRL.PHY_RST. Nothing
   public establishes that our CTRL.RST is an LCD reset. See
   docs/Reference/E1000_ServiceModel_Notes.md, finding 1. */
static int e1000_mdio_window = 0;
#define E1000_MDIO_WINDOW_MS 10

/* -e1000-mdio-slow: MDIO READS answer E until reduced-frequency mode is on.
   I219 datasheet 9.2: "Access using MDIO should be done only when bit 10 in
   page 769 register 16 is set", and 9.5.3.1 defines that bit as "reduced MDIO
   frequency access (required for read during cable disconnect)". Its reset
   value is 0b, so the condition is unmet on a part nobody has configured,
   which is the part we get.

   TWO MODELLING DECISIONS THAT ARE OURS AND NOT THE DATASHEET'S, because an
   arm that hides its own inventions is not evidence:

   1. Reads are refused and WRITES are not, and the page register and 769.16
      itself are exempt. Read strictly, 9.2 forbids the very transactions that
      would satisfy it -- setting the bit is itself MDIO access -- so a literal
      arm would make the requirement unsatisfiable. The exemption is the
      smallest bootstrap that leaves the requirement testable.
   2. The failure shape is E. The datasheet does not say what an access at the
      wrong frequency does, and a floating bus reading 0xFFFF would be equally
      arguable. E was chosen because it cannot be confused with a legitimate
      register value, which 0xFFFF can.

   What the arm therefore tests is that the driver SETS slow mode, which is
   the citable part. It does not claim to reproduce the electrical failure.

   OFF by default (L-FALLBACK). */
static int e1000_mdio_slow = 0;

/* -e1000-asde: STATUS answers SPEED and ASDV, and CTRL.ASDE decides which
   source SPEED is taken from. Until this flag existed the model had no ASDE
   bit and no speed fields at all, so `na-line` printed SPEED and ASDV off a
   register nothing ever wrote: both read 10 Mb/s on every arm, forever, which
   is an instrument that cannot fail (L-FALSIF). The ASDE finding could not be
   exercised in the bed because the bed could not express it.

   What is CITED (82583V 12349): with ASDE set "the MAC ignores the speed
   indicated by the PHY and attempts to automatically detect the resolved speed
   of the link". With it clear, STATUS.SPEED follows the link. And 12646: "If
   Auto-Speed Detection is enabled, the device's speed is configured only once
   after the link signal is asserted by the PHY", which is why the value latches
   rather than being recomputed per read.

   WHAT IS OURS, stated here rather than left for a reader to find. The
   datasheet says ASDE "must be set to 0b" and does NOT say what a part does
   when software disobeys. This model does not invent a failure for that: it
   models only the behaviour the sentence describes, and the MAC's own
   detection resolves to 10 Mb/s because this bed's MAC has no wire to sense
   and nothing to detect. So a driver that leaves ASDE set reads SPEED=10 while
   the PHY negotiated 1000, and a driver that clears it reads 1000. That is the
   cited DIRECTION with a magnitude of our choosing.

   In particular this arm does NOT reproduce the metal wedge and must not be
   read as evidence about it. Nothing in either datasheet in this tree explains
   why the ASUS hangs, and a bed that hung here would be manufacturing a cause.

   Two things the datasheet describes are deliberately NOT modelled, because
   in this bed they have no observable state. 12646 says the speed is
   "configured only once after the link signal is asserted", and 12648 says
   ASDCHK can run the detection with ASDE clear -- but our detection has one
   possible outcome, so a latch and a trigger would both be invisible whether
   they worked or not. Modelling them would add exactly the kind of field this
   flag exists to remove.

   OFF by default (L-FALLBACK): every existing green was measured against a
   STATUS with no speed fields, and `e1000-asde-arms` asserts the two arms are
   INDISTINGUISHABLE in the bed, which is precisely the blindness this lifts. */
static int e1000_asde = 0;
static const unsigned int e1000_asd_value = E1000_SPEED_10;

/* Page selected by the last write to register 31. Registers 0-15 are the IEEE
   set and are identical in every page (9.3), so only 16-31 consult this. */
static unsigned int e1000_phy_page = 0;
#define E1000_PHY_PAGE_REG   31
#define E1000_PHY_PORT_PAGE  769
#define E1000_PHY_CUSTOM_MODE 16
#define E1000_PHY_MDIO_SLOW  0x0400u   /* 769.16 bit 10 */

/* Page 769 register 16, Custom Mode Control. Reset value from the field table
   at 9.5.3.1: reserved 9:0 = 0x180, bit 10 = 0b, reserved 15:11 = 0x04. */
#define E1000_PHY_CUSTOM_MODE_RESET 0x2180u
static unsigned short e1000_phy_custom_mode = E1000_PHY_CUSTOM_MODE_RESET;
static LARGE_INTEGER e1000_reset_at;   /* zero until the guest writes CTRL.RST */

static int e1000_mdio_window_open(void) {
    static LARGE_INTEGER freq;
    LARGE_INTEGER now;
    if (!e1000_mdio_window) return 1;
    /* No reset seen yet, so no window has opened and none is being violated.
       Refusing here would make the arm fire on a driver that never resets,
       which is a different defect and not this one. */
    if (!e1000_reset_at.QuadPart) return 1;
    if (!freq.QuadPart) QueryPerformanceFrequency(&freq);
    QueryPerformanceCounter(&now);
    return ((now.QuadPart - e1000_reset_at.QuadPart) * 1000)
             / freq.QuadPart >= E1000_MDIO_WINDOW_MS;
}

/* Reset value of the PHY register file. ID1/ID2 are the model's own
   identifiers and are NOT measured off Damian's I219: nothing here has read
   that part's PHY. They exist so a driver can prove it reached A phy and
   read back what it wrote, not so it can recognise a real one. */
static unsigned short e1000_phy_regs[32];
static void e1000_phy_reset_regs(void) {
    memset(e1000_phy_regs, 0, sizeof(e1000_phy_regs));
    e1000_phy_regs[E1000_PHY_BMCR] = E1000_BMCR_ANEG_EN;
    e1000_phy_regs[E1000_PHY_BMSR] = 0;
    e1000_phy_regs[E1000_PHY_ID1]  = 0x0154;
    e1000_phy_regs[E1000_PHY_ID2]  = 0x0C00;
    /* A PHY reset takes the paged state with it, slow mode included. That
       makes ORDER matter to the driver: set slow mode before resetting the
       PHY and it is gone by the time the reads that need it happen. Without
       this the model would accept either order and the arm would be testing
       that the write occurred rather than that it holds. */
    e1000_phy_page = 0;
    e1000_phy_custom_mode = E1000_PHY_CUSTOM_MODE_RESET;
    /* 770.17 comes back at its NVM value after a PHY reset, which is why
       the vendor driver reconfigures K1 after every reset rather than once
       at bring-up. A model that kept the driver's value across a reset
       would let a driver that configures K1 exactly once pass. */
    i219_k1_reg = (unsigned short)(i219_k1_nvm ? I219_K1_ENABLE : 0);
}

/* 4.5.2: a request is granted only if nobody else holds it, and the caller
   learns the answer by reading the bit back rather than from the write. So
   the write either takes the bit or leaves it clear, and never reports. */

/* THIS MODEL IS MORE FORGIVING THAN THE SPEC, AND THE COUNTERS ARE HERE
   BECAUSE OF IT. 4.5.2 gives each agent ONE bit, says at most one is 1b at
   any time, and says the owner writes 0b when done. A software write that
   asserts MNG or HW is therefore a protocol violation. `keep = mng` below
   preserves firmware's bit whatever software writes, so the violation has no
   consequence here and a driver that read-modify-writes the whole register
   behaves IDENTICALLY to one that writes only its own bit. Every swflag arm
   passed either way, which is why the registered acquire-loop defect had no
   falsifier at all (L-FALSIF: an instrument that cannot fail is not
   evidence; L-GAP: ask what the suite cannot express before reading its
   silence as agreement).

   These counters change no behaviour. The model still tolerates the
   violation, because whether the real PCH tolerates it is exactly the
   proposition the campaign is testing and inventing an answer here would be
   a bed built from our own beliefs. They only make the difference VISIBLE,
   so an arm can say no. `foreign` is the discriminating one: it counts
   writes that assert a bit the writer does not own. `cfgbits` counts writes
   carrying nonzero extended-configuration bits, which on the board is what a
   read-modify-write puts back (`extcnf 0x00F00=002c0089`, sitting 8). */
static unsigned long long i219_extcnf_writes  = 0;
static unsigned long long i219_extcnf_foreign = 0;
static unsigned long long i219_extcnf_cfgbits = 0;

/* -i219-extcnf-strict: a part that ENFORCES 4.5.2's "at most one bit is 1b"
   rather than tolerating a violation. A software write asserting MNG or HW
   latches the violation, and while it is latched SW ownership is never
   granted. This is the flag that gives the acquire-loop defect a falsifier:
   the counters above make it visible to a host census, but a battery arm
   compares GUEST serial output and cannot read a host counter, so without an
   effect the guest can see there is no runner and the fix is a one-time
   measurement (L-BODY: if it is gated only by prose it is not gated).

   IT IS OFF BY DEFAULT AND MUST STAY OFF. Whether the real PCH enforces this
   is not cited anywhere we hold -- 4.5.2 says the mechanism "does not block
   software accesses" -- so enforcement is a PROPOSITION, and a bed that
   enforced it by default would be asserting the campaign's open question as
   an answer. The flag exists to express the failure mode, not to claim the
   part has it. */
static int i219_extcnf_strict   = 0;
static int i219_extcnf_violated = 0;

/* -i219-mng-release-after N: firmware clears its OWN MNG bit after N software
   writes to EXTCNF_CTRL. 4.5.2 says that bit clears on LAN_PWR_GOOD or when
   firmware clears it, so this is the ordinary case the driver's poll loop
   exists for: the ME finishes and lets go.

   WITHOUT IT THE STRICT ARM IS VACUOUS, and that was measured rather than
   reasoned. With MNG never held the register starts at zero, so a
   read-modify-writing driver reads zero and writes only its own bit -- the
   defect cannot appear and a sabotage of the fix changes nothing (foreign=0).
   With MNG held forever, no acquire can succeed however correct the protocol
   is, so both drivers answer 0. Only a release makes the two answers differ:
   a driver that never asserts a foreign bit is granted the moment firmware
   lets go, and one that does is latched out by -i219-extcnf-strict. */
static unsigned int i219_mng_release_after = 0;

static void i219_extcnf_write(unsigned int val) {
    i219_extcnf_writes++;
    if (i219_mng_release_after && i219_extcnf_writes >= i219_mng_release_after)
        i219_extcnf &= ~I219_EXTCNF_MNG;
    unsigned int mng = i219_extcnf & I219_EXTCNF_MNG;
    unsigned int want_sw = val & I219_EXTCNF_SW;
    unsigned int keep = mng;
    if (val & (I219_EXTCNF_MNG | I219_EXTCNF_HW)) {
        i219_extcnf_foreign++;
        if (i219_extcnf_strict) i219_extcnf_violated = 1;
    }
    if (val & ~(I219_EXTCNF_SW | I219_EXTCNF_HW | I219_EXTCNF_MNG)) i219_extcnf_cfgbits++;
    if (want_sw && !(mng | (i219_extcnf & I219_EXTCNF_HW))
        && !(i219_extcnf_strict && i219_extcnf_violated))
        keep |= I219_EXTCNF_SW;
    i219_extcnf = (val & ~(I219_EXTCNF_SW | I219_EXTCNF_HW | I219_EXTCNF_MNG)) | keep;
}


static unsigned int e1000_regs[E1000_BAR_SIZE / 4];

/* Link is up and the resolved speed is 1000: in this model auto-negotiation
   completing IS the 1 Gbps case (e1000_read derives STATUS.SPEED from the
   same bit), so the two conditions are one test. */
/* No DMA while Bus Master Enable is clear. Checked at the two places the
   device would touch guest memory, the same two the K1 stall gates, so a
   bed run can produce a device that is mapped and answering registers and
   still moves nothing. */
static int e1000_dma_blocked(void) {
    if (!e1000_bme_clear || e1000_pci_slot < 0) return 0;
    return (pci_devices[e1000_pci_slot].command & 0x0004) ? 0 : 1;
}

/* STATUS.LU, as e1000_read derives it, in one place so the readers of the
   link cannot drift apart. -e1000-phy-link is what makes aneg-done part of
   the condition; without it the link rides on CTRL.SLU alone, which is the
   floor every existing green was measured on. */
static int e1000_link_up(void) {
    if (e1000_fault_no_link) return 0;
    if (!(e1000_regs[E1000_REG_CTRL / 4] & E1000_CTRL_SLU)) return 0;
    return !e1000_phy_link ||
           (e1000_phy_regs[E1000_PHY_BMSR] & E1000_BMSR_ANEG_DONE) != 0;
}

/* THE STALL RIDES ON STATUS.LU, NOT ON BMSR. It required aneg-done outright
   until 2026-08-21, and ANEG DOES NOT COMPLETE ON THE PART: measured on the
   ASUS 2026-08-15, e1000-await-aneg ran its full million and returned 0 while
   STATUS.LU came up and the part negotiated 1000 Mb/s, which E1000e.codex:444
   documents and which sitting 9's nicinit row read again (s9 ret=0
   us=3000419). So the bed could only stall in a state the board is never in,
   and the arm built on it was proving something about a condition that does
   not occur out there. Same predicate as the STATUS register now, by
   construction rather than by two copies agreeing. */
static int i219_mac_stalled(void) {
    if (!i219_present) return 0;
    if (!e1000_link_up()) return 0;
    if (i219_k1_reg & I219_K1_GIGA_DIS) return 0;
    return (i219_k1_reg & I219_K1_ENABLE) ? 1 : 0;
}

static int e1000_rx_cursor = 0;
static int e1000_injected = 0;
static int e1000_tx_frames = 0;
static unsigned int e1000_tx_bytes = 0;
static unsigned int e1000_tx_sum = 0;

/* The station address the model answers RAL/RAH with, and the address its
   receive filter accepts on. One array so the two cannot drift: a filter
   keyed on a different address than the register reports is a bed that
   passes a driver reading either. */
static const unsigned char e1000_station_mac[6] = {0x52,0x54,0x00,0xAB,0xCD,0xEF};

/* Where the NAT sends e1000-bound frames. Learned from the source address
   of the guest's own transmits, which is what a peer on real wire has to
   do -- nothing tells the other end an address it has not seen used. */
static unsigned char e1000_peer_mac[6];
static int e1000_peer_known = 0;
static int e1000_rx_dropped = 0;

static void nat_handle_tx(unsigned char *frame, int len);
static void e1000_nat_rx(void);

/* The canned inbound frame. Broadcast destination, a recognisable source
   and an unassigned EtherType, so a driver that reports receiving it
   cannot be reporting something the host stack put on the wire. */
static const unsigned char e1000_canned[60] = {
    0xFF,0xFF,0xFF,0xFF,0xFF,0xFF, 0x52,0x54,0x00,0xAB,0xCD,0xEF, 0x88,0xB5,
    0xC0,0xDE,0x00,0x01,0x02,0x03,0x04,0x05,0x06,0x07,0x08,0x09,0x0A,0x0B,
    0x0C,0x0D,0x0E,0x0F,0x10,0x11,0x12,0x13,0x14,0x15,0x16,0x17,0x18,0x19,
    0x1A,0x1B,0x1C,0x1D,0x1E,0x1F,0x20,0x21,0x22,0x23,0x24,0x25,0x26,0x27,
    0x28,0x29,0x2A,0x2B
};

static unsigned long long e1000_ring_base(int lo_reg, int hi_reg) {
    return (unsigned long long)e1000_regs[lo_reg / 4] |
           ((unsigned long long)e1000_regs[hi_reg / 4] << 32);
}

/* Place pending frames into the receive ring the way the silicon would:
   fetch the descriptor, DMA into the buffer it names, write back the
   length and set DD|EOP. Stops at the tail, because the descriptors
   between head and tail belong to the driver and not to the device. */
/* GPRC COUNTS A GOOD FRAME AT THE MAC, BEFORE ANY DESCRIPTOR. It used to be
   incremented inside the delivery loop just after DD was written, so every
   way of stopping a receive returned above it and the bed could only ever
   read gprc=0 ddset=0 -- DiagNicRing's "nothing arrived" (:122). Sitting 9
   read gp=1 ddset=0 off the ASUS on 2026-08-21, which is the row BELOW it,
   "frames arrived and we cannot see them" (:124), and no flag or fault here
   could produce that row at all. A row the bed cannot make say "invisible"
   is a row whose instrument cannot fail (L-FALSIF), and it is the row a
   flight gets read against.

   82583V stats, and DiagNicRing:119: GPRC is a MAC counter and RNBC is the
   no-descriptor counter. The frame is accepted and counted here; whether a
   descriptor is ever written back is the next question and not this one.
   RNBC stays where it is, at the ring-full test, because turned away for
   want of a descriptor is a different row again (:127) and a driver acts on
   it differently. */
static void e1000_deliver_rx(void) {
    /* Not yet: the frames are being held for a stage that has not started
       polling. Nothing is counted here either, because a frame the wire has
       not delivered has not reached the MAC. */
    if (e1000_inject_armed && e1000_gprc_reads < 1) return;
    /* The three ways this device stops moving frames. The MAC still takes
       them and still counts them: the ring is left exactly as the driver
       programmed it, RDH does not advance and no descriptor gets DD, which
       is the shape a flight reads off the board. */
    if (e1000_ring_poisoned || i219_mac_stalled() || e1000_dma_blocked()) {
        while (e1000_injected < e1000_inject_want) {
            e1000_regs[E1000_REG_GPRC / 4]++;
            e1000_injected++;
        }
        return;
    }
    unsigned long long ring = e1000_ring_base(E1000_REG_RDBAL, E1000_REG_RDBAH);
    unsigned int len = e1000_regs[E1000_REG_RDLEN / 4];
    if (!ring || !len) return;
    unsigned int count = len / 16;
    if (!count) return;
    while (e1000_injected < e1000_inject_want) {
        unsigned int idx = (unsigned int)e1000_rx_cursor % count;
        if (idx == e1000_regs[E1000_REG_RDT / 4]) {          /* ring full */
            e1000_regs[E1000_REG_RNBC / 4]++;
            return;
        }
        unsigned long long desc = ring + (unsigned long long)idx * 16;
        if (desc + 16 > guest_mem_size) return;
        unsigned char *d = (unsigned char *)guest_mem + desc;
        unsigned long long buf = *(unsigned long long *)d;
        if (!buf || buf + sizeof(e1000_canned) > guest_mem_size) return;
        memcpy((unsigned char *)guest_mem + buf, e1000_canned, sizeof(e1000_canned));
        *(unsigned short *)(d + 8) = (unsigned short)sizeof(e1000_canned);
        d[12] = E1000_RXD_STAT_DD | E1000_RXD_STAT_EOP;
        e1000_rx_cursor = (int)((idx + 1) % count);
        e1000_regs[E1000_REG_RDH / 4] = (idx + 1) % count;
        e1000_regs[E1000_REG_GPRC / 4]++;
        e1000_injected++;
    }
}

/* Consume everything the driver has queued up to the tail. The frame is
   summed rather than retransmitted: what is being tested is that the
   driver built a descriptor the device can follow to the right bytes. */
static void e1000_consume_tx(unsigned int tail) {
    /* Stalled: the tail write is accepted and nothing is consumed, so TDH
       stays put and no descriptor is written back with DD. */
    if (i219_mac_stalled()) return;
    if (e1000_dma_blocked()) return;
    unsigned long long ring = e1000_ring_base(E1000_REG_TDBAL, E1000_REG_TDBAH);
    unsigned int len = e1000_regs[E1000_REG_TDLEN / 4];
    if (!ring || !len) { e1000_regs[E1000_REG_TDH / 4] = tail; return; }
    unsigned int count = len / 16;
    if (!count) { e1000_regs[E1000_REG_TDH / 4] = tail; return; }
    unsigned int head = e1000_regs[E1000_REG_TDH / 4] % count;
    while (head != (tail % count)) {
        unsigned long long desc = ring + (unsigned long long)head * 16;
        if (desc + 16 > guest_mem_size) break;
        unsigned char *d = (unsigned char *)guest_mem + desc;
        unsigned long long buf = *(unsigned long long *)d;
        unsigned short dlen = *(unsigned short *)(d + 8);
        if (buf && dlen && buf + dlen <= guest_mem_size) {
            unsigned char *p = (unsigned char *)guest_mem + buf;
            for (unsigned short k = 0; k < dlen; k++) e1000_tx_sum += p[k];
            e1000_tx_bytes += dlen;
            e1000_tx_frames++;
            if (e1000_nat && dlen >= 14) {
                memcpy(e1000_peer_mac, p + 6, 6);
                e1000_peer_known = 1;
                nat_handle_tx(p, dlen);
            }
        }
        if (!e1000_fault_no_tx_dd) d[12] = E1000_TXD_STAT_DD;
        head = (head + 1) % count;
    }
    e1000_regs[E1000_REG_TDH / 4] = head;
    if (e1000_nat) e1000_nat_rx();   /* deliver replies immediately, as the ne2k TX path does */
}

/* One MDIC transaction, completed synchronously. Real silicon takes tens of
   microseconds and the driver polls for R; completing immediately still
   exercises the poll loop, because the driver cannot know it did not wait.
   What it does NOT model is a PHY slower than the driver's fuel cap, which
   is what -e1000-no-phy is for. */
static unsigned int e1000_mdic_exec(unsigned int val) {
    unsigned int reg = (val >> E1000_MDIC_REG_SH) & 0x1F;
    unsigned int phy = (val >> E1000_MDIC_PHY_SH) & 0x1F;
    unsigned int op  = val & E1000_MDIC_OP_MASK;

    /* The semaphore gates the MDIO door itself, so a driver that never
       acquires it cannot reach the PHY at all. Refused as an ERROR rather
       than a never-ready, because the two are different failures and a
       driver can tell them apart (-e1000-no-phy is the other one). */
    if (i219_swflag_enforce && !(i219_extcnf & I219_EXTCNF_SW))
        return (val & ~E1000_MDIC_R) | E1000_MDIC_E;
    if (!e1000_mdio_window_open()) return val & ~(E1000_MDIC_R | E1000_MDIC_E);
    if (e1000_fault_no_phy) return val & ~(E1000_MDIC_R | E1000_MDIC_E);
    if (e1000_fault_phy_err) return (val & ~E1000_MDIC_R) | E1000_MDIC_E;
    if (phy != E1000_PHY_ADDR) return (val & ~E1000_MDIC_R) | E1000_MDIC_E;

    if (op == E1000_MDIC_OP_WR) {
        unsigned short data = (unsigned short)(val & E1000_MDIC_DATA);
        if (reg == E1000_PHY_BMCR) {
            /* Both of these are self-clearing on silicon, and a driver that
               waits for them to clear hangs forever if the model latches
               them. Auto-negotiation completing is the observable effect. */
            if (data & E1000_BMCR_RESET) {
                e1000_phy_reset_regs();
                data &= (unsigned short)~E1000_BMCR_RESET;
            }
            if (data & E1000_BMCR_ANEG_RST) {
                data &= (unsigned short)~E1000_BMCR_ANEG_RST;
                if (!e1000_fault_no_link)
                    e1000_phy_regs[E1000_PHY_BMSR] |=
                        (unsigned short)(E1000_BMSR_ANEG_DONE | E1000_BMSR_LINK);
            }
        }
        /* Register 31 is the page register in every page of PHY address 01,
           and only its 11 MSBs define the page: software writes page x 32
           and the five LSBs are ignored (I219 9.3). */
        if (reg == E1000_PHY_PAGE_REG) {
            e1000_phy_page = data >> 5;
            return (val & ~E1000_MDIC_DATA) | E1000_MDIC_R | data;
        }
        if (reg >= 16 && e1000_phy_page != 0) {
            if (e1000_phy_page == E1000_PHY_PORT_PAGE &&
                reg == E1000_PHY_CUSTOM_MODE) {
                e1000_phy_custom_mode = data;
                return (val & ~E1000_MDIC_DATA) | E1000_MDIC_R | data;
            }
            if (i219_present && e1000_phy_page == I219_PCIE_PM_PAGE &&
                reg == I219_PCIE_PM_REG) {
                i219_k1_reg = data;
                return (val & ~E1000_MDIC_DATA) | E1000_MDIC_R | data;
            }
            if (i219_present && e1000_phy_page == I219_ULP_PAGE &&
                reg == I219_ULP_CFG1_REG) {
                i219_ulp_cfg1 = data;
                return (val & ~E1000_MDIC_DATA) | E1000_MDIC_R | data;
            }
            /* Only the one paged register this model has a citation for
               exists. Anything else on a non-zero page is not modelled and
               must not answer as though it were. */
            return (val & ~E1000_MDIC_R) | E1000_MDIC_E;
        }
        if (reg != E1000_PHY_BMSR) e1000_phy_regs[reg] = data;
        return (val & ~E1000_MDIC_DATA) | E1000_MDIC_R | data;
    }
    if (op == E1000_MDIC_OP_RD) {
        if (reg == E1000_PHY_PAGE_REG)
            return (val & ~E1000_MDIC_DATA) | E1000_MDIC_R |
                   ((e1000_phy_page << 5) & E1000_MDIC_DATA);
        if (reg >= 16 && e1000_phy_page != 0) {
            if (e1000_phy_page == E1000_PHY_PORT_PAGE &&
                reg == E1000_PHY_CUSTOM_MODE)
                return (val & ~E1000_MDIC_DATA) | E1000_MDIC_R |
                       e1000_phy_custom_mode;
            if (i219_present && e1000_phy_page == I219_PCIE_PM_PAGE &&
                reg == I219_PCIE_PM_REG)
                return (val & ~E1000_MDIC_DATA) | E1000_MDIC_R | i219_k1_reg;
            if (i219_present && e1000_phy_page == I219_ULP_PAGE &&
                reg == I219_ULP_CFG1_REG)
                return (val & ~E1000_MDIC_DATA) | E1000_MDIC_R | i219_ulp_cfg1;
            return (val & ~E1000_MDIC_R) | E1000_MDIC_E;
        }
        /* The slow-mode gate applies to ordinary register reads only. The
           page register and 769.16 returned above are the bootstrap, and the
           reasoning for exempting them is at the flag's declaration. */
        if (e1000_mdio_slow &&
            !(e1000_phy_custom_mode & E1000_PHY_MDIO_SLOW))
            return (val & ~E1000_MDIC_R) | E1000_MDIC_E;
        return (val & ~E1000_MDIC_DATA) | E1000_MDIC_R | e1000_phy_regs[reg];
    }

    /* Neither read nor write is a malformed transaction, not a silent zero. */
    return (val & ~E1000_MDIC_R) | E1000_MDIC_E;
}

static unsigned int e1000_read(unsigned long long off) {
    if (off + 4 > E1000_BAR_SIZE) return 0;
    switch ((unsigned int)off) {
    case E1000_REG_GPRC:
    case E1000_REG_RNBC:
    case E1000_REG_MPC:
    case E1000_REG_CRCERRS: {
        unsigned int v = e1000_regs[off / 4];
        e1000_regs[off / 4] = 0;        /* clear on read */
        /* The stage's own opening reading. -e1000-inject-armed holds the
           frames until it has happened, so what arrives is inside this
           stage's window by construction rather than by an ordinal. */
        if ((unsigned int)off == E1000_REG_GPRC) e1000_gprc_reads++;
        return v;
    }
    case E1000_REG_STATUS: {
        unsigned int v = e1000_regs[E1000_REG_STATUS / 4];
        unsigned int ctrl = e1000_regs[E1000_REG_CTRL / 4];
        if (e1000_link_up()) v |= E1000_STATUS_LU;
        if (e1000_asde) {
            /* The PHY's own resolved speed. It has a speed to report only once
               auto-negotiation has finished, which is the same condition the
               link itself rides on. */
            unsigned int phy_speed =
                (e1000_phy_regs[E1000_PHY_BMSR] & E1000_BMSR_ANEG_DONE)
                    ? E1000_SPEED_1000 : E1000_SPEED_10;
            v &= ~((3u << E1000_STATUS_SPEED_SH) | (3u << E1000_STATUS_ASDV_SH));
            v |= e1000_asd_value << E1000_STATUS_ASDV_SH;
            v |= ((ctrl & E1000_CTRL_ASDE) ? e1000_asd_value : phy_speed)
                     << E1000_STATUS_SPEED_SH;
        }
        return v;
    }
    case I219_EXTCNF_CTRL:
        if (i219_present) return i219_extcnf;
        return e1000_regs[off / 4];
    case E1000_REG_RAL:
        return e1000_fault_no_mac ? 0 :
            ((unsigned int)e1000_station_mac[0] | ((unsigned int)e1000_station_mac[1] << 8) |
             ((unsigned int)e1000_station_mac[2] << 16) | ((unsigned int)e1000_station_mac[3] << 24));
    case E1000_REG_RAH:
        return e1000_fault_no_mac ? 0 : (E1000_RAH_AV |
            (unsigned int)e1000_station_mac[4] | ((unsigned int)e1000_station_mac[5] << 8));
    case E1000_REG_RDH:
        e1000_deliver_rx();
        if (e1000_nat) e1000_nat_rx();
        return e1000_regs[E1000_REG_RDH / 4];
    default:
        return e1000_regs[off / 4];
    }
}

static void e1000_write(unsigned long long off, unsigned int val) {
    if (off + 4 > E1000_BAR_SIZE) return;
    switch ((unsigned int)off) {
    case E1000_REG_CTRL:
        /* Discarded whole: no store, no PHY reset, no MDIO window. A driver
           that writes RST here and then polls for it sees it already clear,
           which is what the real part does. */
        if (e1000_ctrl_ro) return;
        /* RST is self-clearing on real silicon. Holding it set is the
           "device wedged in reset" refusal. */
        if (val & E1000_CTRL_RST) {
            /* A device reset drops the link, so the PHY is no longer
               negotiated. Without this the negotiated state survives every
               later reset, and a test's link arm passes on a bring-up that
               skipped the PHY entirely -- which is the exact defect the PHY
               path exists to catch. */
            e1000_phy_regs[E1000_PHY_BMSR] &=
                (unsigned short)~(E1000_BMSR_ANEG_DONE | E1000_BMSR_LINK);
            QueryPerformanceCounter(&e1000_reset_at);   /* opens the MDIO window */
            if (!e1000_fault_no_reset) val &= ~E1000_CTRL_RST;
        }
        /* Link-up is one of the two candidate first observables of bring-up.
           Read AFTER the RST handling above, so a reset that happens to carry
           SLU does not arm on a write whose purpose was the reset. */
        if (val & E1000_CTRL_SLU) usb_bot_nic_arm("CTRL.SLU");
        e1000_regs[E1000_REG_CTRL / 4] = val;
        return;
    case E1000_REG_RCTL:
        /* Disabling the receiver IS the quiesce, and it is what makes the
           ring safe to reprogram. Clearing the poison here is the whole
           reward for doing it in the right order. */
        if (!(val & E1000_RCTL_EN)) e1000_ring_poisoned = 0;
        /* The other candidate. Only EN set counts: the quiesce writes RCTL
           with EN CLEAR, and arming on that would fire one stage early, on
           the write whose whole purpose is to stop the receiver. */
        if (val & E1000_RCTL_EN) usb_bot_nic_arm("RCTL.EN");
        e1000_regs[E1000_REG_RCTL / 4] = val;
        if (val & E1000_RCTL_EN) { e1000_deliver_rx(); if (e1000_nat) e1000_nat_rx(); }
        return;
    /* The ring's ADDRESS and LENGTH, not its tail: RDT is written on every
       descriptor recycle during normal receive, so poisoning on it would
       break the working path rather than the broken one. */
    case E1000_REG_RDBAL:
    case E1000_REG_RDBAH:
    case E1000_REG_RDLEN:
        if (e1000_regs[E1000_REG_RCTL / 4] & E1000_RCTL_EN) e1000_ring_poisoned = 1;
        e1000_regs[off / 4] = val;
        /* The ring moved, so the old receive cursor names a descriptor in a
           ring that no longer exists. See the RDH case below for what leaving
           it stale costs. */
        e1000_rx_cursor = 0;
        return;
    /* RDH IS DEVICE STATE THE DRIVER INITIALISES, and until 2026-08-20 a write
       to it fell through to the default store: e1000_rx_cursor, which is what
       both delivery paths actually use as the head, was never reset by
       anything. It only ever advanced.

       That is invisible on the first bring-up and fatal on a later one. Our
       e1000-setup-rx programs RDH=0 and RDT=count-1 with count 16, and both
       nat and inject delivery declare "ring full: leave it queued" when the
       cursor equals RDT. So a guest that brings the receiver up a SECOND time
       after consuming frames -- which the diagnostic ladder does, once per NIC
       stage -- had one chance in sixteen per bring-up of resuming with the
       cursor already sitting on 15, and from there NOTHING is ever delivered
       again: RNBC counts up, the host queue grows without bound, and the guest
       polls a ring the model refuses to fill. Measured on the b3 stage, where
       every ARP reply the NAT built (138 of them) sat in rx_queue while the
       stage reported no-arp.

       The guest cannot see the difference between that and a dead wire, which
       is the same "arrived but invisible" shape NIC-4 chased on metal -- and a
       bed that can produce it for a reason the metal does not have is worse
       than one that cannot. */
    case E1000_REG_RDH: {
        unsigned int rdlen = e1000_regs[E1000_REG_RDLEN / 4];
        unsigned int count = rdlen / 16;
        /* The part owns the head and the driver's store is dropped on the
           floor: no register update and no cursor move, so a read-back returns
           whatever the device already had. */
        if (e1000_fault_rdh_ro) return;
        e1000_regs[E1000_REG_RDH / 4] = val;
        e1000_rx_cursor = count ? (int)(val % count) : 0;
        return;
    }
    case E1000_REG_RDT:
        e1000_regs[E1000_REG_RDT / 4] = val;
        e1000_deliver_rx();
        if (e1000_nat) e1000_nat_rx();
        return;
    case E1000_REG_TDT:
        e1000_regs[E1000_REG_TDT / 4] = val;
        e1000_consume_tx(val);
        return;
    case E1000_REG_MDIC:
        e1000_regs[E1000_REG_MDIC / 4] = e1000_mdic_exec(val);
        return;
    case I219_EXTCNF_CTRL:
        if (i219_present) { i219_extcnf_write(val); return; }
        e1000_regs[off / 4] = val;
        return;
    case E1000_REG_ICR:
        e1000_regs[E1000_REG_ICR / 4] = 0;
        return;
    default:
        e1000_regs[off / 4] = val;
        return;
    }
}

static int handle_device_mmio(WHV_RUN_VP_EXIT_CONTEXT *ctx) {
    unsigned long long gpa = ctx->MemoryAccess.Gpa;
    int access_type = ctx->MemoryAccess.AccessInfo.AccessType;

    /* Resolve the device BEFORE decoding. Most memory-access exits are not
       device accesses at all: the guest heap is demand-paged, so the first
       touch of every 2 MB page arrives here on its way to the demand-commit
       in the run loop. Decoding first meant each of those printed a
       cannot-size complaint naming a device fault that never happened -- one
       line per page, thousands over a long compile, and reading exactly like
       a livelock to anyone diagnosing a hang. A gpa outside every BAR is not
       this function's business, so leave before spending the decode on it. */

    unsigned long long offset = 0;
    unsigned int (*read_fn)(unsigned long long) = NULL;
    void (*write_fn)(unsigned long long, unsigned int) = NULL;

    if (e1000_present && gpa >= E1000_BAR && gpa < E1000_BAR + E1000_BAR_SIZE) {
        offset = gpa - E1000_BAR;
        read_fn = e1000_read; write_fn = e1000_write;
    } else if (xhci_sel_for(gpa) >= 0) {
        int xsel = xhci_sel_for(gpa);
        xcur = &xhci_ctl[xsel];
        offset = gpa - xhci_decode_base_slot(xsel ? xhci_pci_slot2 : xhci_pci_slot);
        read_fn = xhci_read; write_fn = xhci_write;
    } else if (gpa >= HDA_BAR && gpa < HDA_BAR + HDA_BAR_SIZE) {
        offset = gpa - HDA_BAR;
        read_fn = hda_read; write_fn = hda_write;
    } else if (gpa >= HPET_BAR && gpa < HPET_BAR + HPET_BAR_SIZE) {
        offset = gpa - HPET_BAR;
        read_fn = hpet_read; write_fn = hpet_write;
    } else if (gpa >= IOAPIC_BAR && gpa < IOAPIC_BAR + IOAPIC_BAR_SIZE) {
        offset = gpa - IOAPIC_BAR;
        read_fn = ioapic_read; write_fn = ioapic_write;
    } else if (gpa >= LAPIC_BAR && gpa < LAPIC_BAR + LAPIC_BAR_SIZE) {
        offset = gpa - LAPIC_BAR;
        read_fn = lapic_read; write_fn = lapic_write_bsp;
    }

    if (read_fn) {
        mmio_insn_t insn = mmio_decode(ctx->MemoryAccess.InstructionBytes,
                                       ctx->MemoryAccess.InstructionByteCount);
        if (insn.len == 0) {
            /* WHP's instruction bytes are best-effort and stop at the page
               the exit was taken on: an instruction that STRADDLES a page
               boundary arrives truncated (or, when the exit races an
               interrupt window, empty), and a truncated prefix cannot be
               sized. Refetch through the guest's own translations, one page
               at a time, so a straddling instruction is assembled from both
               pages instead of guessed at.

               This is not a corner case, it is a lottery every build enters.
               Measured 2026-08-03: a 68-byte code-size change moved one
               LAPIC store to RIP=0x112fff -- last byte of a page -- and the
               startup IPI that brings up every application processor was
               reported as unmapped MMIO. No AP ever started, and the guest
               looked like a broken SMP scheduler while the defect was here.
               A previous version of this fallback only ran when WHP supplied
               ZERO bytes, so the truncated case fell straight through. */
            unsigned char ib[16] = {0};
            unsigned long long rip = ctx->VpContext.Rip;
            int got = 0;
            for (int page = 0; page < 2 && got < 16; page++) {
                WHV_TRANSLATE_GVA_RESULT tr;
                WHV_GUEST_PHYSICAL_ADDRESS ig = 0;
                unsigned long long gva = rip + (unsigned)got;
                if (FAILED(WHvTranslateGva(partition, 0, gva,
                        WHvTranslateGvaFlagValidateExecute, &tr, &ig)) ||
                    tr.ResultCode != WHvTranslateGvaResultSuccess) break;
                unsigned long long page_left = 4096 - (gva & 0xFFF);
                unsigned long long want = (unsigned long long)(16 - got);
                if (want > page_left) want = page_left;
                if (ig + want > guest_mem_size) break;
                memcpy(ib + got, (unsigned char *)guest_mem + ig, (size_t)want);
                got += (int)want;
            }
            if (got > 0) insn = mmio_decode(ib, got);
        }
        int ilen = insn.len;
        if (ilen == 0) {
            fprintf(stderr, "MMIO: cannot size instruction at RIP=0x%llx (gpa=0x%llx), "
                            "not stepping\n",
                (unsigned long long)ctx->VpContext.Rip, gpa);
            return 0;
        }
        /* A load must name a destination register; only a store can carry an
           immediate. RIP is fetched rather than taken from the exit context,
           and it is always the last name so the step below does not care
           whether a GPR came with it. */
        if (access_type == 0 && insn.reg < 0) {
            fprintf(stderr, "MMIO: load with no register operand at RIP=0x%llx "
                            "(gpa=0x%llx), not stepping\n",
                (unsigned long long)ctx->VpContext.Rip, gpa);
            return 0;
        }
        WHV_REGISTER_NAME names[2];
        WHV_REGISTER_VALUE vals[2];
        int nregs;
        if (insn.reg >= 0) { names[0] = mmio_gpr_names[insn.reg]; names[1] = WHvX64RegisterRip; nregs = 2; }
        else               { names[0] = WHvX64RegisterRip; nregs = 1; }
        WHvGetVirtualProcessorRegisters(partition, 0, names, nregs, vals);
        if (access_type == 0) {
            vals[0].Reg64 = read_fn(offset);
        } else {
            write_fn(offset, insn.reg >= 0 ? (unsigned int)vals[0].Reg64 : insn.imm);
        }
        vals[nregs - 1].Reg64 += ilen;
        WHvSetVirtualProcessorRegisters(partition, 0, names, nregs, vals);
        return 1;
    }
    return 0;
}

/* GOP (Graphics Output Protocol) state */
#define GOP_FB_ADDR       0xBF000000ULL  /* guest physical address of framebuffer (3GB - 16MB, in RAM -- fast writes, no MMIO trap) */
#define GOP_MAX_W         1920
#define GOP_MAX_H         1200
#define GOP_MAX_STRIDE    2048
/* Sized by STRIDE x HEIGHT, and the height is the one that was wrong. This was
   2048 x 768 x 4 = exactly 6 MB while GOP_MAX_STRIDE was already 2048 for the
   ASUS TUF panel named in the -gop-stride comment below. That panel is
   1920x1080, which needs 2048 x 1080 x 4 = 8.4 MB, so every attempt to model it
   memset past the end of this buffer and codex-vm died host-side with an access
   violation before drawing a pixel. The bed built for padded scanlines could
   not be run at the panel it was built for, so every probe anyone ever verified
   under OVMF was necessarily verified at a geometry too small to show the bug.
   9.4 MB now, and GOP_FB_ADDR leaves 16 MB below the 3 GB line. */
#define GOP_FB_SIZE       (GOP_MAX_STRIDE * GOP_MAX_H * 4)  /* 9.4 MB */
static int gop_active = 0;
static int gop_width = 640;
static int gop_height = 480;
static int gop_stride = 640;
/* -gop-stride pads each scanline past the visible width, which is what real
   panels do: the ASUS TUF sitting reported 1920x1080 with a stride of 2048.
   Every bed we own had stride equal to width, so a renderer stepping rows by
   the visible width could not fail here. 0 means "follow the width", the
   behaviour before this option existed. */
static int gop_stride_opt = 0;
/* The GOP mode table the guest can QueryMode/SetMode. Modes 0..2 are fixed
   (640x480, 800x600, 1024x768); mode 3 is the CLI-selected -gop-width x
   -gop-height when it matches none of them, so a bed run at 1600x900 is a
   firmware whose largest mode is what the display supports (real firmware
   boots in the panel's native mode and lists it). Without mode 3 a stub that
   SetModes the largest enumerated mode would shrink every -gop-width run to
   1024x768 while doing the right thing on a board. -gop-max-mode N caps
   MaxMode (1 = a firmware with nothing to enumerate: the fallback arm). */
static int gop_cli_w = 0, gop_cli_h = 0;
static int gop_max_mode_opt = 0;
static int gop_mode_count(void) {
    int n = 3;
    if (gop_cli_w && !((gop_cli_w == 640 && gop_cli_h == 480) ||
                       (gop_cli_w == 800 && gop_cli_h == 600) ||
                       (gop_cli_w == 1024 && gop_cli_h == 768))) n = 4;
    if (gop_max_mode_opt > 0 && gop_max_mode_opt < n) n = gop_max_mode_opt;
    return n;
}
static int gop_mode_dims(int mode, int *w, int *h) {
    if (mode == 0) { *w = 640;  *h = 480; return 1; }
    if (mode == 1) { *w = 800;  *h = 600; return 1; }
    if (mode == 2) { *w = 1024; *h = 768; return 1; }
    if (mode == 3 && gop_mode_count() > 3) { *w = gop_cli_w; *h = gop_cli_h; return 1; }
    return 0;
}
static int gop_mode_index(int w, int h) {
    int i, mw, mh;
    for (i = 0; i < gop_mode_count(); i++)
        if (gop_mode_dims(i, &mw, &mh) && mw == w && mh == h) return i;
    return 0;
}
static unsigned char *gop_fb = NULL;  /* host-side framebuffer copy for rendering */
static HWND vga_hwnd;  /* forward decl -- defined in VGA section */

/* The title carries the geometry AND the grab state, from one place, because
   they used to be written from two: a GOP SetMode rewrote the title with the
   new resolution and silently wiped whatever the grab had put there. The guest
   sets its mode after boot, so the hint vanished before it could be read. */
static int vga_title_w = 0, vga_title_h = 0;

static void vga_update_title(void) {
    char t[160];
    const char *hint = mouse_grabbed
        ? "mouse GRABBED, Ctrl+Alt+G releases"
        : "Ctrl+Alt+G grabs the mouse";
    if (!vga_hwnd) return;
    if (vga_title_w > 0) sprintf(t, "Codex VM - %dx%d  |  %s", vga_title_w, vga_title_h, hint);
    else                 sprintf(t, "Codex VM  |  %s", hint);
    SetWindowTextA(vga_hwnd, t);
}

/* The host-side triangle rasterizer and its post passes address the
   framebuffer densely, row pitch = visible width, in the band workers, the
   glow distance field and the bloom composite. Under a padded stride every
   one of those walks off the end of a row, so the host would paint garbage
   geometry -- and a sheared HOST renderer gets read as the guest's defect,
   which is the opposite of what this bed is for. So the host GPU declines a
   padded stride and says so once. What the bed serves is GUEST-side row
   stepping, which is where the stride is a payload's responsibility. */
static int gop_host_gpu_refuses(void) {
    if (gop_stride == gop_width) return 0;
    static int warned = 0;
    if (!warned) {
        fprintf(stderr, "GPU: host rasterizer REFUSES a padded stride (%d visible, %d scanline). "
                        "It addresses rows by width. Nothing is drawn by the host this run; "
                        "-gop-stride is a bed for GUEST-side row stepping.\n",
                gop_width, gop_stride);
        warned = 1;
    }
    return 1;
}

/* Board register apertures (-board-mmio).
 *
 * Three of the nine IoT board drivers put their register windows above the
 * 3 GB RAM ceiling, in the range x86 reserves for PCI MMIO: the RP2040's SIO
 * at 0xD0000000, the Cortex-M System Control Block at 0xE000ED00, and the
 * BCM2711 peripheral block at 0xFE000000. Nothing backs those GPAs, so once
 * the HAL started issuing real loads and stores instead of returning zero,
 * those three drivers page-faulted and their tests were skipped.
 *
 * With -board-mmio we commit host RAM at each window and map it, so a board
 * driver's register access lands on memory and reads back what it wrote. That
 * is the same fidelity the other six boards already get by falling inside
 * guest RAM: it exercises the address arithmetic, the access width, and the
 * read-modify-write logic. It does NOT model peripheral behaviour, and no
 * silicon has been in the loop. Only Renode does that.
 *
 * The Pi4 window overlaps the emulated Intel HDA BAR (0xFE000000) and the
 * xHCI BAR (0xFE800000). Mapping RAM there means those GPAs no longer trap to
 * the device models, so audio and USB are dead while the flag is on. That is
 * why this is opt-in: a board test wants neither.
 */
#define BOARD_MMIO_REGIONS 3
static int board_mmio = 0;
static void *board_mmio_host[BOARD_MMIO_REGIONS];
static const struct { unsigned long long base; size_t size; const char *what; }
board_mmio_map[BOARD_MMIO_REGIONS] = {
    { 0xD0000000ULL, 0x10000,  "RP2040 SIO" },
    { 0xE0000000ULL, 0x10000,  "Cortex-M PPB/SCB" },
    { 0xFE000000ULL, 0x900000, "BCM2711 peripherals" },
};

static int uefi_mode = 0;          /* 1 when running a UEFI app (declared above for smbios_setup_tables) */
static int uefi_strict = 0;        /* 1 = model real firmware: honor the memory map,
                                      fault on writes to firmware-owned low memory the
                                      app never allocated. Turns the hardware-only
                                      fixed-address boot bug into a VM-reproducible #PF. */
static unsigned long long uefi_image_base = 0; /* where load_kernel placed the PE */
static unsigned long long uefi_image_size = 0; /* loaded PE size (BootServicesCode) */
static int cmos_index = 0;        /* CMOS register selected via port 0x70 */
static int rtc_lenient = 0;       /* -rtc-lenient: the old always-valid RTC */
static unsigned int rtc_noise = 0x1234567u;
/* -rtc <stamp>: the clock stands still. Declared up beside the HPET, which
   stops with it. */

/* A guest that displays the time cannot be compared against a recorded frame,
   because the frame carries the host's clock and the host's clock never
   repeats. That is not a property of the guest -- it is machine state we
   decline to control, and it was the stated reason GuiOS could have no golden.
   -rtc takes it back: the emulated MC146818 answers a time the caller chose,
   so a frame is a function of the program and the flags and nothing else.

   The stamp is YYYY-MM-DDTHH:MM:SS (the T may be a space). Day-of-week is
   COMPUTED from the date rather than accepted, because register 6 is readable
   and a machine that answers a Tuesday for a Sunday is lying about something
   nobody asked it to invent.

   While it is set the update-in-progress simulation is OFF, and that is a real
   loss, not an oversight: UIP is what makes an unguarded RTC read fail here
   the way it fails on metal, and a frozen clock cannot express it. So -rtc is
   for frames, never for testing the RTC itself -- anything asserting on clock
   behaviour must run without it. */
static int rtc_day_of_week(int y, int m, int d) {
    /* Sakamoto. Valid for the Gregorian calendar; 0 = Sunday. */
    static const int t[] = { 0, 3, 2, 5, 0, 3, 5, 1, 4, 6, 2, 4 };
    if (m < 3) y -= 1;
    return (y + y / 4 - y / 100 + y / 400 + t[m - 1] + d) % 7;
}
static int rtc_parse_fixed(const char *s) {
    int y, mo, d, h, mi, se;
    if (sscanf(s, "%d-%d-%dT%d:%d:%d", &y, &mo, &d, &h, &mi, &se) != 6 &&
        sscanf(s, "%d-%d-%d %d:%d:%d", &y, &mo, &d, &h, &mi, &se) != 6) return 0;
    if (mo < 1 || mo > 12 || d < 1 || d > 31) return 0;
    if (h < 0 || h > 23 || mi < 0 || mi > 59 || se < 0 || se > 59) return 0;
    memset(&rtc_fixed_st, 0, sizeof(rtc_fixed_st));
    rtc_fixed_st.wYear = (WORD)y;   rtc_fixed_st.wMonth  = (WORD)mo;
    rtc_fixed_st.wDay  = (WORD)d;   rtc_fixed_st.wHour   = (WORD)h;
    rtc_fixed_st.wMinute = (WORD)mi; rtc_fixed_st.wSecond = (WORD)se;
    rtc_fixed_st.wMilliseconds = 500;   /* mid-second: never update-in-progress */
    rtc_fixed_st.wDayOfWeek = (WORD)rtc_day_of_week(y, mo, d);
    return 1;
}

/* The MC146818 spends part of every second updating its time registers. For
   that window it raises Update-In-Progress (Status A bit 7) and the time
   registers read as GARBAGE -- the spec says their contents are undefined,
   and real silicon duly returns junk. A guest that polls the seconds without
   checking UIP therefore sees the value change many times a second.

   codex-vm used to answer every CMOS read with a clean value from the host
   clock and swear, in Status A, that no update was ever in progress. It was
   incapable of reproducing this, so a guest that read the RTC unguarded
   passed here and on QEMU and then counted thirty "seconds" in microseconds
   on a real board -- which is exactly what happened, at the cost of many
   flash-boot-walk cycles that this emulator should have made unnecessary.

   An oracle that cannot fail is not an oracle. This one now updates. */
static int rtc_updating(SYSTEMTIME *st) {
    if (rtc_lenient) return 0;
    return st->wMilliseconds < 2;   /* ~2ms of every second, as the part does */
}
static unsigned char rtc_junk(void) {
    rtc_noise = rtc_noise * 1103515245u + 12345u;
    return (unsigned char)((rtc_noise >> 16) & 0xFF);
}
static int uefi_cursor_row = 0;
static int uefi_cursor_col = 0;
static unsigned char uefi_attr = 0x07; /* white on black */
static unsigned long long uefi_alloc_hi;   /* high-water mark, allocates downward (set in setup) */
static unsigned long long uefi_alloc_pool = 0x10000000; /* bump allocator for AllocatePool */

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

    /* RuntimeServices at 0x70400 -- stub all to traps */
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

    /* ConInEx at 0x70300 -- for ReadKeyStrokeEx */
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

    /* ConIn.WaitForKey event handle at ConIn+16 -- just a non-null value */
    W64(0x100 + 16, 0xDEAD0001);

    /* LocateProtocol at BootServices+320 (0x140) = base+0x640 */
    W64(0x500 + 320, TRAP(UEFI_TRAP_BOOT_LOCATEPROTO));
    /* Fill extended BootServices range (offsets 0x100-0x17F) with stubs */
    for (int off = 0x100; off < 0x180; off += 8) {
        unsigned long long *slot = (unsigned long long *)(base + 0x500 + off);
        if (*slot == 0) *slot = TRAP(UEFI_TRAP_BOOT_STUB);
    }

    /* RuntimeServices.GetTime at offset +24 (after 24-byte header) */
    W64(0x400 + 24, TRAP(UEFI_TRAP_RT_GETTIME));

    /* EFI_CONFIGURATION_TABLE. Spec layout: NumberOfTableEntries at
       SystemTable+104, ConfigurationTable pointer at +112. This is how a
       UEFI application finds the ACPI RSDP -- LocateProtocol cannot. (Until
       2026-07-09 slot 112 held the GOP interface pointer, which is a
       SystemTable field the spec assigns to ConfigurationTable; nothing read
       it, since GOP is reached through LocateProtocol, so the slot is now
       what the spec says it is.)
       Array at 0xF0A00: { EFI_GUID VendorGuid; VOID *VendorTable; } x N,
       24 bytes each. Entry 0 is EFI_ACPI_20_TABLE_GUID, entry 1 the legacy
       ACPI_TABLE_GUID; both point at the one RSDP, which carries an RSDT and
       an XSDT, so a guest exercises whichever path it prefers. */
    static const unsigned char GUID_ACPI20[16] = {
        0x71,0xe8,0x68,0x88, 0xf1,0xe4, 0xd3,0x11,
        0xbc,0x22,0x00,0x80,0xc7,0x3c,0x88,0x81
    };
    static const unsigned char GUID_ACPI10[16] = {
        0x30,0x2d,0x9d,0xeb, 0x88,0x2d, 0xd3,0x11,
        0x9a,0x16,0x00,0x90,0x27,0x3f,0xc1,0x4d
    };
    memcpy(base + 0xA00, GUID_ACPI20, 16);
    W64(0xA00 + 16, ACPI_BASE);
    memcpy(base + 0xA18, GUID_ACPI10, 16);
    W64(0xA18 + 16, ACPI_BASE);
    W64(104, 2);                            /* NumberOfTableEntries */
    W64(112, UEFI_TABLE_PAGE + 0xA00);      /* ConfigurationTable */

    /* SMBIOS in the ConfigurationTable: entry 2 is SMBIOS3_TABLE_GUID pointing
       at the 3.0 entry point smbios_setup_tables builds at 0xF0B00, entry 3 the
       2.x SMBIOS_TABLE_GUID pointing at the 2.1 entry at 0xE0780; both name the
       one structure table at 0xE0800. Passive: nothing in the guest reads it
       unless it goes looking, and the diagnostic ladder does. -no-smbios leaves
       both entries out, which is the arm that shows the reader can say
       "none offered". */
    if (!uefi_no_smbios) {
        static const unsigned char GUID_SMBIOS2[16] = {0x31,0x2D,0x9D,0xEB, 0x88,0x2D, 0xD3,0x11, 0x9A,0x16,0x00,0x90,0x27,0x3F,0xC1,0x4D};
        memcpy(base + 0xA30, GUID_SMBIOS3, 16);
        W64(0xA30 + 16, UEFI_TABLE_PAGE + 0xB00);
        memcpy(base + 0xA48, GUID_SMBIOS2, 16);
        W64(0xA48 + 16, 0xE0780);
        W64(104, 4);                        /* NumberOfTableEntries */
    }
    /* EDID: EFI_EDID_ACTIVE_PROTOCOL { UINT32 SizeOfEdid; UINT8 *Edid; } at
       0xF0B40 with a 128-byte EDID 1.4 block at 0xF0B80 (VESA E-EDID 1.4):
       manufacturer CDX, product 1, one detailed timing of 1920x1080@148.5 MHz,
       a monitor-name descriptor "codex-vm dsp". Reached through LocateProtocol
       for both the ACTIVE and DISCOVERED GUIDs. -edid-bad breaks the checksum. */
    if (!uefi_no_edid) {
        unsigned char *ed = base + 0xB80;
        memset(ed, 0, 128);
        ed[0] = 0x00; for (int k = 1; k < 7; k++) ed[k] = 0xFF; ed[7] = 0x00;
        ed[8] = 0x0C; ed[9] = 0x98;                 /* CDX: (3<<10)|(4<<5)|24 */
        ed[10] = 0x01; ed[11] = 0x00;               /* product code 1 */
        ed[12] = 0x01;                              /* serial 1 */
        ed[16] = 0; ed[17] = 36;                    /* week 0, year 2026 */
        ed[18] = 1; ed[19] = 4;                     /* EDID 1.4 */
        ed[20] = 0x80; ed[21] = 60; ed[22] = 34; ed[23] = 120; ed[24] = 0x0A;
        for (int k = 38; k < 54; k++) ed[k] = 0x01; /* no standard timings */
        ed[54] = 0x02; ed[55] = 0x3A;               /* 14850 * 10 kHz */
        ed[56] = 0x80; ed[57] = 0x18; ed[58] = 0x71; /* h 1920, hblank 280 */
        ed[59] = 0x38; ed[60] = 0x2D; ed[61] = 0x40; /* v 1080, vblank 45 */
        ed[66] = 0x50; ed[67] = 0x1D; ed[68] = 0x00; /* image size 600x340 mm */
        ed[72] = 0; ed[73] = 0; ed[74] = 0; ed[75] = 0xFC; ed[76] = 0;
        memcpy(ed + 77, "codex-vm dsp\n", 13);
        ed[90] = 0; ed[91] = 0; ed[92] = 0; ed[93] = 0x10; ed[94] = 0;
        ed[108] = 0; ed[109] = 0; ed[110] = 0; ed[111] = 0x10; ed[112] = 0;
        ed[126] = 0;
        {
            unsigned char sum = 0;
            for (int k = 0; k < 127; k++) sum = (unsigned char)(sum + ed[k]);
            ed[127] = (unsigned char)(0x100 - sum);
            if (uefi_edid_bad) ed[127] = (unsigned char)(ed[127] + 1);
        }
        *(unsigned int *)(base + 0xB40) = 128;
        W64(0xB48, UEFI_TABLE_PAGE + 0xB80);
    }

    /* GOP (Graphics Output Protocol) at 0xF0700, reached via LocateProtocol */
    W64(0x700 + 0,   TRAP(UEFI_TRAP_GOP_QUERYMODE));
    W64(0x700 + 8,   TRAP(UEFI_TRAP_GOP_SETMODE));
    W64(0x700 + 16,  TRAP(UEFI_TRAP_GOP_BLT));
    W64(0x700 + 24,  UEFI_TABLE_PAGE + 0x780);
    /* GOP_MODE at 0xF0780 */
    gop_cli_w = gop_width; gop_cli_h = gop_height;
    *(int *)(base + 0x780) = gop_mode_count();   /* MaxMode */
    *(int *)(base + 0x784) = gop_mode_index(gop_width, gop_height);   /* Mode */
    W64(0x788, UEFI_TABLE_PAGE + 0x7C0);  /* Info pointer */
    W64(0x790, 36);               /* SizeOfInfo */
    W64(0x798, GOP_FB_ADDR);      /* FrameBufferBase */
    W64(0x7A0, GOP_FB_SIZE);      /* FrameBufferSize */
    /* GOP_MODE_INFO at 0xF07C0. The current mode reports the CLI-selected
       resolution (-gop-width/-gop-height, default 640x480) -- real firmware
       boots in the panel's native mode, and a UEFI app that only reads the
       current mode (the Option A stub) must see the size the display will
       actually scan out. Previously hardcoded 640x480, which garbled any
       -gop-width run: the guest drew 640-wide rows into a wider display. */
    *(int *)(base + 0x7C0) = 0;   /* Version */
    *(int *)(base + 0x7C4) = gop_width;  /* HorizontalResolution */
    *(int *)(base + 0x7C8) = gop_height; /* VerticalResolution */
    *(int *)(base + 0x7CC) = 1;   /* PixelFormat (BGR) */
    /* PixelsPerScanLine at the STANDARD info offset +32 (base+0x7E0). It was
       previously at +20 (0x7D4), which is inside the PixelInformation field --
       a real UEFI app reads +32, so this makes the stride correct on hardware. */
    *(int *)(base + 0x7E0) = gop_stride; /* PixelsPerScanLine */

    /* Block I/O Protocol at 0xF0800 (only when disk is loaded) */
    if (ide.data && ide.size > 0) {
        W64(0x800 + 0,  0x00020001ULL);                  /* Revision */
        W64(0x800 + 8,  UEFI_TABLE_PAGE + 0x840);       /* Media pointer */
        W64(0x800 + 16, TRAP(UEFI_TRAP_BLK_RESET));     /* Reset */
        W64(0x800 + 24, TRAP(UEFI_TRAP_BLK_READBLOCKS));/* ReadBlocks */
        W64(0x800 + 32, TRAP(UEFI_TRAP_BLK_WRITEBLOCKS));
        W64(0x800 + 40, TRAP(UEFI_TRAP_BLK_FLUSH));
        /* Block I/O Media at 0xF0840 */
        *(int *)(base + 0x840) = 1;    /* MediaId (UINT32) */
        base[0x844] = 1;               /* RemovableMedia (BOOLEAN) */
        base[0x845] = 1;               /* MediaPresent (BOOLEAN) */
        base[0x846] = 0;               /* LogicalPartition (BOOLEAN) */
        base[0x847] = 0;               /* ReadOnly (BOOLEAN) */
        base[0x848] = 0;               /* WriteCaching (BOOLEAN) */
        /* 3 bytes padding to align BlockSize */
        *(int *)(base + 0x84C) = 512;  /* BlockSize (UINT32) */
        *(int *)(base + 0x850) = 0;    /* IoAlign (UINT32) */
        {
            unsigned long long last_blk = (ide.size / 512) - 1;
            memcpy(base + 0x858, &last_blk, 8); /* LastBlock */
        }
    }

    /* Loaded Image Protocol at 0xF0880 */
    *(int *)(base + 0x880) = 0x1000;         /* Revision */
    W64(0x880 + 8,  0);                      /* ParentHandle */
    W64(0x880 + 16, UEFI_TABLE_PAGE);        /* SystemTable */
    W64(0x880 + 24, 1);                      /* DeviceHandle (handle #1 = boot disk) */
    W64(0x880 + 32, UEFI_TABLE_PAGE + 0x900);/* FilePath (Device Path) */

    /* Device Path at 0xF0900 -- EndEntire node */
    base[0x900] = 0x7F;  /* Type: End */
    base[0x901] = 0xFF;  /* SubType: EndEntire */
    base[0x902] = 4;     /* Length[0] */
    base[0x903] = 0;     /* Length[1] */

    /* Firmware Vendor string at 0xF0940 (UCS-2) -- "Codex VM" */
    {
        const char *vendor = "Codex VM";
        for (int i = 0; vendor[i]; i++) {
            base[0x940 + i*2] = vendor[i];
            base[0x940 + i*2 + 1] = 0;
        }
    }
    W64(16, UEFI_TABLE_PAGE + 0x940);  /* SystemTable.FirmwareVendor */
    *(int *)(base + 24) = 0x00010000;  /* SystemTable.FirmwareRevision = 1.0 */

    #undef W64
    #undef TRAP

    /* Store SystemTable pointer at 0x8000 */
    unsigned long long st_ptr = UEFI_TABLE_PAGE;
    memcpy((unsigned char *)mem + UEFI_SYSTABLE_PTR, &st_ptr, 8);
}

/* Commit + map the host pages backing a guest region so BOTH the guest CPU
   and host-side trap handlers can touch it. guest_mem is MEM_RESERVE with lazy
   commit: a page is committed when the GUEST first faults on it. Any host-side
   access to a region the guest has never touched must therefore commit it here
   first, or it lands in reserved address space.

   The two ways that bites are worth knowing apart. A host WRITE through the
   CRT (fread into guest RAM) does not crash: ReadFile reports the fault as an
   error, so the read comes back short and, if nobody checks the return, looks
   like it worked. A host READ or a direct memcpy out of the same region does
   crash, and takes the VM process with it. So the symptom of the missing
   commit shows up nowhere near the code that missed it.

   Callers: UEFI AllocatePages/AllocatePool (host writes descriptor arrays into
   the memory it hands out), and the GPU asset loader / texture upload (host
   reads and writes guest RAM the guest never touched). */
static void guest_commit_range(unsigned long long base, unsigned long long size) {
    if (base >= guest_mem_size) return;
    unsigned long long start = base & ~0x1FFFFFULL;
    unsigned long long end = (base + size + 0x1FFFFFULL) & ~0x1FFFFFULL;
    if (end > guest_mem_size) end = guest_mem_size;
    for (unsigned long long a = start; a < end; a += 0x200000) {
        size_t len = 0x200000;
        if (a + len > guest_mem_size) len = (size_t)(guest_mem_size - a);
        if (VirtualAlloc((unsigned char *)guest_mem + a, len, MEM_COMMIT, PAGE_READWRITE)) {
            /* Errors (e.g. range already mapped) are harmless -- the mapping stays. */
            WHvMapGpaRange(partition, (unsigned char *)guest_mem + a, a, len,
                WHvMapGpaRangeFlagRead | WHvMapGpaRangeFlagWrite | WHvMapGpaRangeFlagExecute);
        }
    }
}

/* The run loop's demand-commit: back the 2 MB chunk holding gpa and map it.
   Returns 1 on success. On failure the caller MUST report it as a host
   condition and not as a guest fault: a VirtualAlloc(MEM_COMMIT) refusal is
   the box's commit charge (several agents' VMs each reserve 3 GB and commit
   what they touch), a WHvMapGpaRange refusal is the hypervisor's, and either
   used to fall through to the "Unmapped MMIO" crash report as if the guest
   had wandered off the map. err is GetLastError() from VirtualAlloc when it
   refused, hr the WHvMapGpaRange result when the commit succeeded and the
   map did not. Answers -1 when the chunk was ALREADY committed before this
   call and the map refused: that is not a host failure, the fault the caller
   is handling is the guest's own, and it must fall through to the report it
   would have made anyway. */
static int demand_commit_chunk(unsigned long long gpa, DWORD *err, HRESULT *hr) {
    size_t chunk = 2ULL * 1024 * 1024;
    size_t base = (size_t)((gpa / chunk) * chunk);
    size_t len = chunk;
    int was_committed = 0;
    MEMORY_BASIC_INFORMATION mbi;
    *err = 0; *hr = S_OK;
    if (gpa >= guest_mem_size) return 0;
    if (base + len > guest_mem_size) len = guest_mem_size - base;
    if (VirtualQuery((unsigned char *)guest_mem + gpa, &mbi, sizeof(mbi)) == sizeof(mbi))
        was_committed = (mbi.State == MEM_COMMIT);
    if (!VirtualAlloc((unsigned char *)guest_mem + base, len, MEM_COMMIT, PAGE_READWRITE)) {
        *err = GetLastError();
        return 0;
    }
    *hr = WHvMapGpaRange(partition, (unsigned char *)guest_mem + base, base, len,
        WHvMapGpaRangeFlagRead | WHvMapGpaRangeFlagWrite | WHvMapGpaRangeFlagExecute);
    if (SUCCEEDED(*hr)) return 1;
    return was_committed ? -1 : 0;
}

static void report_commit_failure(unsigned long long gpa, DWORD err, HRESULT hr) {
    if (err)
        fprintf(stderr, "HOST: cannot back guest RAM at GPA 0x%llx: VirtualAlloc(MEM_COMMIT) failed, err=%lu%s -- the host is out of commit charge, this is not a guest fault\n",
                gpa, (unsigned long)err, err == ERROR_COMMITMENT_LIMIT ? " (ERROR_COMMITMENT_LIMIT)" : "");
    else
        fprintf(stderr, "HOST: cannot back guest RAM at GPA 0x%llx: WHvMapGpaRange failed, hr=0x%08lX -- a hypervisor refusal, this is not a guest fault\n",
                gpa, (unsigned long)hr);
}

/* Handle a UEFI trap -- guest called a protocol function that faulted
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
        /* OutputString(This, String) -- RDX = UTF-16LE string in guest mem */
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
                /* Echo to stderr as well. ConOut used to land ONLY in the VGA
                   text buffer, where scrolling discards a line for good, so a
                   -headless UEFI payload was mute and its diagnostics were
                   unreadable at exactly the point they were needed. */
                if (ch == '\n') fputc('\n', stderr);
                else if (ch != '\r') fputc((ch < 128) ? (int)ch : '?', stderr);
            }
            fflush(stderr);
        }
        break;
    }
    case UEFI_TRAP_CONOUT_SETATTR:
        /* SetAttribute(This, Attr) -- RDX = attribute */
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
        /* -uefi-conout-remode: model AMI's GraphicsConsole. On that firmware
           the first real ConOut use activates the text console, which SETS
           ITS OWN GRAPHICS MODE (typically 1024x768) -- so a stub that read
           the GOP geometry BEFORE calling ClearScreen hands its payload a
           1920x1080/2048 handoff for a scanout that is now 1024 pixels per
           row. OVMF and this model never re-moded on ClearScreen, which is
           why no bed could express the ASUS's stretched-and-interleaved
           screen. The mode-info table is updated the way real firmware
           updates it: a guest that re-reads Mode->Info AFTER this call sees
           the truth; only a stale earlier copy is wrong. */
        if (uefi_conout_remode && gop_active) {
            gop_width = 1024; gop_height = 768; gop_stride = 1024;
            unsigned char *gm = (unsigned char *)guest_mem + UEFI_TABLE_PAGE;
            *(int *)(gm + 0x784) = 2;   /* Mode: the console's own 1024x768 is mode 2 */
            *(int *)(gm + 0x7C4) = gop_width;
            *(int *)(gm + 0x7C8) = gop_height;
            *(int *)(gm + 0x7E0) = gop_stride;
            fprintf(stderr, "UEFI: ClearScreen re-moded GOP to 1024x768 stride 1024 (AMI GraphicsConsole model)\n");
        }
        break;
    }
    case UEFI_TRAP_CONOUT_SETCURSOR:
        /* SetCursorPosition(This, Col, Row) -- RDX=col, R8=row */
        uefi_cursor_col = (int)(rdx & 0xFF);
        uefi_cursor_row = (int)(r8 & 0xFF);
        *(int *)((unsigned char *)guest_mem + UEFI_TABLE_PAGE + 0x28C) = uefi_cursor_col;
        *(int *)((unsigned char *)guest_mem + UEFI_TABLE_PAGE + 0x290) = uefi_cursor_row;
        break;

    case UEFI_TRAP_CONOUT_ENABLECUR:
        /* EnableCursor(This, Visible) -- ignore */
        break;

    case UEFI_TRAP_CONIN_READKEY: {
        /* ReadKeyStroke(This, Key) -- RDX = pointer to EFI_INPUT_KEY (4 bytes)
           Non-blocking: returns EFI_NOT_READY if no key available.
           Handles PS/2 extended scancodes (0xE0 prefix) in one call. */
        int sc = kbd_dequeue();
        /* Skip key-up events */
        while (sc >= 0 && (sc & 0x80)) sc = kbd_dequeue();
        /* Handle 0xE0 extended prefix: consume it and read the real scancode */
        if (sc == 0xE0) sc = kbd_dequeue();
        if (sc < 0 || (sc & 0x80)) sc = -1;
        if (sc < 0) {
            rax_result = EFI_NOT_READY;
        } else {
            unsigned char ascii = 0;
            unsigned short scan = 0;
            /* Map PS/2 scancode to UEFI scan code + ASCII */
            unsigned char ps2 = (unsigned char)(sc & 0x7F);
            if (sc & 0x80) { /* key up -- ignore for ReadKeyStroke */ rax_result = EFI_NOT_READY; break; }
            ascii = vk_to_scancode(0); /* placeholder -- need ps2 to ascii */
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
                /* Letter/number keys -- convert PS/2 to ASCII */
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
        /* ReadKeyStrokeEx -- block until key press */
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
        /* AllocatePages(Type, MemType, Pages, Memory*) -- RCX=type, RDX=memtype, R8=pages, R9=&addr
           Type: 0=AllocateAnyPages, 1=AllocateMaxAddress, 2=AllocateAddress
           AMI Aptio V allocates top-down from conventional memory. */
        unsigned long long pages = r8;
        unsigned long long alloc_size = pages * 4096;
        /* The value the guest had in *R9 BEFORE the call. For AllocateMaxAddress
           that is the ceiling it is asking for, and reading it back here is the
           only way to see whether a stub's ceiling store landed at the address
           the call actually reads. Captured before any branch overwrites it. */
        unsigned long long alloc_in = 0;
        if (arg_vals[3].Reg64 > 0 && arg_vals[3].Reg64 + 8 <= guest_mem_size)
            memcpy(&alloc_in, (unsigned char *)guest_mem + arg_vals[3].Reg64, 8);
        if (rcx == 2) {
            /* AllocateAddress: caller set *R9 to exact address.
               Permissive mode just succeeds. Strict mode honors the memory
               map like real firmware: a fixed-address request that overlaps
               firmware-owned low memory or the running image's own load
               region (BootServicesCode) fails with EFI_NOT_FOUND. This is
               exactly what bricks a stub that hardcodes 0x100000 / 0x1000000
               on a real board whose map differs. */
            if (uefi_strict) {
                unsigned long long want = 0;
                if (arg_vals[3].Reg64 > 0 && arg_vals[3].Reg64 + 8 <= guest_mem_size)
                    memcpy(&want, (unsigned char *)guest_mem + arg_vals[3].Reg64, 8);
                int occupied = (want < 0x200000); /* first 2MB: firmware-owned */
                if (uefi_image_size &&
                    want < uefi_image_base + uefi_image_size &&
                    want + alloc_size > uefi_image_base)
                    occupied = 1;                 /* overlaps the loaded image */
                if (occupied) {
                    rax_result = EFI_NOT_FOUND_S;
                    fprintf(stderr, "UEFI-strict: AllocateAddress(0x%llx, %llu pages) "
                        "-> EFI_NOT_FOUND (firmware-owned / image region)\n", want, pages);
                }
            }
        } else if (rcx == 1) {
            /* AllocateMaxAddress: allocate at or below the address in *R9 */
            unsigned long long max_addr = 0;
            if (arg_vals[3].Reg64 > 0 && arg_vals[3].Reg64 + 8 <= guest_mem_size)
                memcpy(&max_addr, (unsigned char *)guest_mem + arg_vals[3].Reg64, 8);
            unsigned long long ceiling = (max_addr < uefi_alloc_hi) ? max_addr : uefi_alloc_hi;
            if (ceiling >= alloc_size + 0x100000) {
                unsigned long long addr = (ceiling - alloc_size) & ~0xFFFULL;
                if (addr >= 0x1000) {
                    uefi_alloc_hi = addr;
                    memcpy((unsigned char *)guest_mem + arg_vals[3].Reg64, &addr, 8);
                } else {
                    rax_result = 0x8000000000000009ULL; /* EFI_OUT_OF_RESOURCES */
                }
            } else {
                rax_result = 0x8000000000000009ULL;
            }
        } else {
            /* AllocateAnyPages: top-down from high memory */
            if (uefi_alloc_hi >= alloc_size + 0x100000) {
                unsigned long long addr = (uefi_alloc_hi - alloc_size) & ~0xFFFULL;
                uefi_alloc_hi = addr;
                if (arg_vals[3].Reg64 > 0 && arg_vals[3].Reg64 + 8 <= guest_mem_size)
                    memcpy((unsigned char *)guest_mem + arg_vals[3].Reg64, &addr, 8);
            } else {
                rax_result = 0x8000000000000009ULL;
            }
        }
        if (rax_result == EFI_SUCCESS) {
            /* Commit whatever we handed back so host-side handlers can write it. */
            unsigned long long got = 0;
            if (arg_vals[3].Reg64 > 0 && arg_vals[3].Reg64 + 8 <= guest_mem_size)
                memcpy(&got, (unsigned char *)guest_mem + arg_vals[3].Reg64, 8);
            if (got) guest_commit_range(got, alloc_size);
        }
        /* CODEX_VM_ALLOC_TRACE=1: one line per AllocatePages. Lets a guest that
           faults on a returned address be told apart from one that asked for the
           wrong thing, which reasoning about the stub's byte encoding could not
           settle. */
        if (getenv("CODEX_VM_ALLOC_TRACE")) {
            unsigned long long shown = 0;
            if (arg_vals[3].Reg64 > 0 && arg_vals[3].Reg64 + 8 <= guest_mem_size)
                memcpy(&shown, (unsigned char *)guest_mem + arg_vals[3].Reg64, 8);
            fprintf(stderr, "UEFI-ALLOC: type=%llu pages=%llu in=0x%llx -> addr=0x%llx "
                    "status=0x%llx alloc_hi=0x%llx\n",
                    (unsigned long long)rcx, pages, alloc_in, shown,
                    (unsigned long long)rax_result, uefi_alloc_hi);
        }
        break;
    }
    case UEFI_TRAP_BOOT_FREE_PAGES:
    case UEFI_TRAP_BOOT_FREE_POOL:
        break; /* no-op */

    case UEFI_TRAP_BOOT_GET_MEMMAP: {
        /* GetMemoryMap -- ASUS TUF (AMI Aptio V) compatible memory map
           RCX=&MapSize, RDX=MemoryMap, R8=&MapKey, R9=&DescSize
           Stack: [RSP+40]=&DescVersion
           For >3GB guests, splits RAM around the PCI MMIO hole
           (0xC0000000-0xFFFFFFFF) and reports high RAM above 4GB. */
        struct { unsigned int type; unsigned long long phys; unsigned long long virt; unsigned long long pages; unsigned long long attr; } entries[8];
        int num_entries = 0;
        #define MMAP_ENT(t,p,pg) do { entries[num_entries].type=(t); entries[num_entries].phys=(p); \
            entries[num_entries].virt=(p); entries[num_entries].pages=(pg); \
            entries[num_entries].attr=0; num_entries++; } while(0)
        MMAP_ENT(0, 0x00000, 1);            /* Reserved: IVT+BDA */
        MMAP_ENT(7, 0x01000, 0x9E);         /* Conventional: low 631 KB */
        MMAP_ENT(0, 0x9F000, 1);            /* Reserved: EBDA */
        MMAP_ENT(0, 0xA0000, 0x20);         /* Reserved: VGA */
        MMAP_ENT(0, 0xC0000, 0x40);         /* Reserved: ROM shadow */
        if (guest_mem_size <= 0xC0000000ULL) {
            MMAP_ENT(7, 0x100000, (unsigned int)((guest_mem_size - 0x100000) / 4096));
        } else {
            MMAP_ENT(7, 0x100000, (unsigned int)((0xC0000000ULL - 0x100000) / 4096));
            if (guest_mem_size > 0x100000000ULL) {
                MMAP_ENT(7, 0x100000000ULL, (unsigned int)((guest_mem_size - 0x100000000ULL) / 4096));
            }
        }
        #undef MMAP_ENT
        unsigned long long desc_size = 48;
        unsigned long long map_size = (unsigned long long)num_entries * desc_size;
        if (rcx > 0 && rcx + 8 <= guest_mem_size)
            memcpy((unsigned char *)guest_mem + rcx, &map_size, 8);
        if (r8 > 0 && r8 + 8 <= guest_mem_size) {
            unsigned long long map_key = UEFI_MAP_KEY;
            memcpy((unsigned char *)guest_mem + r8, &map_key, 8);
        }
        if (arg_vals[3].Reg64 > 0 && arg_vals[3].Reg64 + 8 <= guest_mem_size)
            memcpy((unsigned char *)guest_mem + arg_vals[3].Reg64, &desc_size, 8);
        /* Write descriptor version */
        unsigned long long desc_ver_addr = 0;
        if (rsp + 40 + 8 <= guest_mem_size)
            memcpy(&desc_ver_addr, (unsigned char *)guest_mem + rsp + 40, 8);
        if (desc_ver_addr > 0 && desc_ver_addr + 4 <= guest_mem_size)
            *(int *)((unsigned char *)guest_mem + desc_ver_addr) = 1;
        /* Write map entries to guest buffer */
        if (rdx > 0 && rdx + map_size <= guest_mem_size) {
            for (int i = 0; i < num_entries; i++) {
                unsigned char *d = (unsigned char *)guest_mem + rdx + i * 48;
                memset(d, 0, 48);
                *(unsigned int *)(d + 0)  = entries[i].type;
                memcpy(d + 8,  &entries[i].phys, 8);
                memcpy(d + 16, &entries[i].virt, 8);
                memcpy(d + 24, &entries[i].pages, 8);
                memcpy(d + 32, &entries[i].attr, 8);
            }
        }
        break;
    }
    case UEFI_TRAP_BOOT_ALLOC_POOL: {
        /* AllocatePool(PoolType, Size, Buffer*) -- RDX=size, R8=&buffer */
        unsigned long long size = rdx;
        unsigned long long addr = uefi_alloc_pool;
        uefi_alloc_pool = (uefi_alloc_pool + size + 4095) & ~4095ULL;
        if (r8 > 0 && r8 + 8 <= guest_mem_size) {
            memcpy((unsigned char *)guest_mem + r8, &addr, 8);
        }
        guest_commit_range(addr, size);
        break;
    }
    case UEFI_TRAP_BOOT_EXIT_BOOTSVC: {
        /* ExitBootServices(ImageHandle=RCX, MapKey=RDX). This used to share the
           Stall case: it read RCX (the image handle) as a microsecond count, slept
           a nonsense interval, and returned EFI_SUCCESS without ever checking
           MapKey -- the "codex-vm said the boot was fine" failure class. The memory
           map here never changes between calls, so the key GetMemoryMap handed out
           stays valid; a guest passing a different key never called GetMemoryMap
           and must be refused, as real firmware does. */
        if (rdx != UEFI_MAP_KEY) {
            rax_result = EFI_INVALID_PARAM;
        }
        break;
    }
    case UEFI_TRAP_BOOT_STALL: {
        /* Stall(Microseconds) -- RCX = microseconds to pause.
           Cap at 16ms for UI responsiveness (compiled-in read-key
           helper requests 50ms which is too sluggish for menus). */
        unsigned long long us = rcx;
        if (us > 0) {
            DWORD ms = (DWORD)((us + 999) / 1000);
            if (ms > 8) ms = 8;
            Sleep(ms);
        }
        break;
    }
    case UEFI_TRAP_BOOT_SETWATCHDOG:
        break;

    case UEFI_TRAP_BOOT_HANDLEPROTO: {
        /* HandleProtocol(Handle, Protocol*, Interface**) -- RCX=handle, RDX=&GUID, R8=&interface */
        unsigned char guid[16];
        if (rdx > 0 && rdx + 16 <= guest_mem_size)
            memcpy(guid, (unsigned char *)guest_mem + rdx, 16);
        else { rax_result = EFI_NOT_FOUND_S; break; }
        unsigned long long iface = 0;
        if (memcmp(guid, GUID_BLOCK_IO, 16) == 0 && ide.data)
            iface = UEFI_TABLE_PAGE + 0x800;
        else if (memcmp(guid, GUID_LOADED_IMAGE, 16) == 0)
            iface = UEFI_TABLE_PAGE + 0x880;
        else if (memcmp(guid, GUID_DEVICE_PATH, 16) == 0)
            iface = UEFI_TABLE_PAGE + 0x900;
        else if (memcmp(guid, GUID_GOP, 16) == 0)
            iface = UEFI_TABLE_PAGE + 0x700;
        if (iface && r8 > 0 && r8 + 8 <= guest_mem_size)
            memcpy((unsigned char *)guest_mem + r8, &iface, 8);
        else
            rax_result = EFI_NOT_FOUND_S;
        break;
    }
    case UEFI_TRAP_BOOT_LOCHANDLE:
        rax_result = EFI_NOT_FOUND_S;
        break;

    case UEFI_TRAP_BOOT_LOCATEPROTO: {
        /* LocateProtocol(Protocol*, Registration, Interface**) -- RCX=&GUID, RDX=reg, R8=&interface */
        unsigned char guid[16];
        if (rcx > 0 && rcx + 16 <= guest_mem_size)
            memcpy(guid, (unsigned char *)guest_mem + rcx, 16);
        else { rax_result = EFI_NOT_FOUND_S; break; }
        unsigned long long iface = 0;
        if (memcmp(guid, GUID_BLOCK_IO, 16) == 0 && ide.data) {
            iface = UEFI_TABLE_PAGE + 0x800;
        } else if (memcmp(guid, GUID_SFS, 16) == 0) {
            /* Simple File System -- not yet implemented */
        } else if (memcmp(guid, GUID_GOP, 16) == 0) {
            iface = UEFI_TABLE_PAGE + 0x700;
        } else if (memcmp(guid, GUID_LOADED_IMAGE, 16) == 0) {
            iface = UEFI_TABLE_PAGE + 0x880;
        } else if (memcmp(guid, GUID_INPUT_EX, 16) == 0) {
            iface = UEFI_TABLE_PAGE + 0x300;   /* ConInEx, built above */
        } else if (memcmp(guid, GUID_EDID_ACTIVE, 16) == 0 || memcmp(guid, GUID_EDID_DISCOVERED, 16) == 0) {
            if (!uefi_no_edid) iface = UEFI_TABLE_PAGE + 0xB40;   /* { SizeOfEdid, Edid* }, built in uefi_setup_tables */
        } else {
            fprintf(stderr, "UEFI: LocateProtocol(unknown %02x%02x%02x%02x) → NOT_FOUND\n",
                guid[3],guid[2],guid[1],guid[0]);
        }
        if (iface && r8 > 0 && r8 + 8 <= guest_mem_size)
            memcpy((unsigned char *)guest_mem + r8, &iface, 8);
        else
            rax_result = EFI_NOT_FOUND_S;
        break;
    }

    case UEFI_TRAP_BLK_READBLOCKS: {
        /* ReadBlocks(This, MediaId, LBA, BufferSize, Buffer)
           MS x64 ABI: RCX=This, RDX=MediaId, R8=LBA, R9=BufferSize, [RSP+40]=Buffer
           (RSP+40 because CALL pushed return addr, so caller's [RSP+32] is now [RSP+40]) */
        unsigned long long lba = r8;
        unsigned long long buf_size = arg_vals[3].Reg64;
        unsigned long long buf_addr = 0;
        if (rsp + 40 + 8 <= guest_mem_size)
            memcpy(&buf_addr, (unsigned char *)guest_mem + rsp + 40, 8);
        unsigned long long disk_off = lba * 512;
        if (!ide.data || disk_off + buf_size > ide.size) {
            rax_result = EFI_DEVICE_ERROR_S; /* EFI_DEVICE_ERROR (bit 63 set) */
            break;
        }
        if (buf_addr > 0 && buf_addr + buf_size <= guest_mem_size) {
            memcpy((unsigned char *)guest_mem + buf_addr, ide.data + disk_off, (size_t)buf_size);
        } else {
            rax_result = EFI_INVALID_PARAM; /* EFI_INVALID_PARAMETER (bit 63 set) */
        }
        break;
    }
    case UEFI_TRAP_BLK_WRITEBLOCKS: {
        /* WriteBlocks(This, MediaId, LBA, BufferSize, Buffer) -- same ABI as
           ReadBlocks above. This fell through to the no-op below until
           2026-08-09, so every guest write returned EFI_SUCCESS and left the
           image byte-identical: a correct writer and a missing one produced the
           same picture, and no bed could tell them apart. */
        unsigned long long lba = r8;
        unsigned long long buf_size = arg_vals[3].Reg64;
        unsigned long long buf_addr = 0;
        if (rsp + 40 + 8 <= guest_mem_size)
            memcpy(&buf_addr, (unsigned char *)guest_mem + rsp + 40, 8);
        unsigned long long disk_off = lba * 512;
        if (!ide.data || disk_off + buf_size > ide.size) {
            rax_result = EFI_DEVICE_ERROR_S;
            break;
        }
        if (buf_addr > 0 && buf_addr + buf_size <= guest_mem_size) {
            memcpy(ide.data + disk_off, (unsigned char *)guest_mem + buf_addr, (size_t)buf_size);
            /* Media advertises WriteCaching = 0 (0xF0848), so a write is
               through to the backing file and FlushBlocks has nothing left. */
            ide_flush(&ide, (size_t)disk_off, (size_t)buf_size);
        } else {
            rax_result = EFI_INVALID_PARAM;
        }
        break;
    }
    case UEFI_TRAP_BLK_RESET:
    case UEFI_TRAP_BLK_FLUSH:
        break;

    case UEFI_TRAP_RT_GETTIME: {
        /* GetTime(Time*, Capabilities*) -- RCX=&EFI_TIME, RDX=&caps (optional) */
        if (rcx > 0 && rcx + 16 <= guest_mem_size) {
            SYSTEMTIME st;
            GetLocalTime(&st);
            unsigned char *t = (unsigned char *)guest_mem + rcx;
            memset(t, 0, 16);
            *(unsigned short *)(t + 0) = st.wYear;
            t[2] = (unsigned char)st.wMonth;
            t[3] = (unsigned char)st.wDay;
            t[4] = (unsigned char)st.wHour;
            t[5] = (unsigned char)st.wMinute;
            t[6] = (unsigned char)st.wSecond;
        }
        break;
    }

    case UEFI_TRAP_CONOUT_QUERYMODE:
        /* QueryMode(This, ModeNumber, Columns, Rows) -- R8=&Cols, R9=&Rows */
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
        /* QueryMode(This, ModeNumber, SizeOfInfo*, Info**). The info block
           it hands back is a SCRATCH copy at +0x2C0, not the current mode's
           block at +0x7C0: it used to write the queried geometry over the
           current mode's Info, so a stub that enumerated modes and then
           re-read Mode->Info without a SetMode saw the LAST QUERIED mode, not
           the one on the glass. A mode past MaxMode is EFI_INVALID_PARAMETER,
           which is what a stub's "on any failure fall through" has to see. */
        int mode_num = (int)rdx;
        int w, h;
        if (mode_num < 0 || mode_num >= gop_mode_count() || !gop_mode_dims(mode_num, &w, &h)) {
            rax_result = 0x8000000000000002ULL; /* EFI_INVALID_PARAMETER */
            break;
        }
        if (r8 > 0 && r8 + 8 <= guest_mem_size) {
            unsigned long long sz = 36;
            memcpy((unsigned char *)guest_mem + r8, &sz, 8);
        }
        if (arg_vals[3].Reg64 > 0 && arg_vals[3].Reg64 + 8 <= guest_mem_size) {
            unsigned long long info_addr = UEFI_TABLE_PAGE + 0x2C0;
            memcpy((unsigned char *)guest_mem + arg_vals[3].Reg64, &info_addr, 8);
        }
        unsigned char *info = (unsigned char *)guest_mem + UEFI_TABLE_PAGE + 0x2C0;
        memset(info, 0, 36);
        *(int *)(info + 4) = w;
        *(int *)(info + 8) = h;
        *(int *)(info + 12) = 1;   /* PixelFormat (BGR), as the current block reports */
        *(int *)(info + 32) = gop_stride_opt > w ? gop_stride_opt : w;   /* PixelsPerScanLine */
        break;
    }
    case UEFI_TRAP_GOP_SETMODE: {
        int mode_num = (int)rdx;
        int w, h;
        if (mode_num < 0 || mode_num >= gop_mode_count() || !gop_mode_dims(mode_num, &w, &h)) {
            rax_result = 0x8000000000000002ULL; /* EFI_UNSUPPORTED would also do; the stub only tests non-zero */
            break;
        }
        gop_width = w; gop_height = h;
        gop_stride = gop_stride_opt > gop_width ? gop_stride_opt : gop_width;
        gop_active = 1;
        if (!gop_fb) gop_fb = (unsigned char *)calloc(1, GOP_FB_SIZE);
        /* Same defect as the VBE mode set (main 14494) and the oversized-disk
           write: the startup path commits the framebuffer region only when
           gop_active was set on the command line and only to the initial
           mode's extent, so a SetMode from a guest that booted headless (or
           into a smaller mode) memset reserved uncommitted address space and
           the HOST faulted (0xC0000005) inside this trap. Measured
           2026-08-15 with the stub's mode selection: -gop-width 800 and the
           default bed both crashed the host at SetMode 2, while a bed already
           at the target mode did not. Commit before the first write. */
        guest_commit_range(0xBE000000ULL,
            (GOP_FB_ADDR + (unsigned long long)((size_t)gop_stride * gop_height * 4)) - 0xBE000000ULL);
        unsigned char *gm = (unsigned char *)guest_mem + UEFI_TABLE_PAGE;
        *(int *)(gm + 0x784) = mode_num;
        *(int *)(gm + 0x7C4) = gop_width;
        *(int *)(gm + 0x7C8) = gop_height;
        *(int *)(gm + 0x7E0) = gop_stride;  /* PixelsPerScanLine at standard info offset +32 */
        if (GOP_FB_ADDR + (unsigned long long)(gop_stride * gop_height * 4) <= guest_mem_size) {
            memset((unsigned char *)guest_mem + GOP_FB_ADDR, 0, gop_stride * gop_height * 4);
        }
        if (vga_hwnd) {
            RECT r = {0, 0, gop_width, gop_height};
            AdjustWindowRect(&r, WS_OVERLAPPEDWINDOW, FALSE);
            SetWindowPos(vga_hwnd, NULL, 0, 0, r.right - r.left, r.bottom - r.top, SWP_NOMOVE | SWP_NOZORDER);
            vga_title_w = gop_width;
            vga_title_h = gop_height;
            vga_update_title();
        }
        fprintf(stderr, "GOP: SetMode %d → %dx%d fb=0x%llx\n", mode_num, gop_width, gop_height, GOP_FB_ADDR);
        break;
    }
    case UEFI_TRAP_GOP_BLT:
        /* Blt -- not yet implemented, guest writes directly to framebuffer */
        break;

    default:
        if (func_id >= 500) { fprintf(stderr, "UEFI app exited cleanly.\n"); return 2; /* signal clean exit */ }
        fprintf(stderr, "UEFI: unhandled trap %d (RIP=0x%llx)\n", func_id, rip);
        rax_result = EFI_UNSUPPORTED_S; /* EFI_UNSUPPORTED (bit 63 set) */
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

/* Scripted keyboard injection for automated interactive tests (-keys).
   A comma-separated list of Set-1 scancodes fed to the guest one at a time
   on a timer, exactly as if typed at the window -- so a GOP menu can be
   driven and screenshotted without a human at the keyboard. */
static unsigned char inject_keys[256];
static int inject_key_count = 0;
static int inject_key_idx = 0;
static double inject_key_start_ms = 1200.0;
static double inject_key_interval_ms = 350.0;

/* Scripted pointer injection for automated interactive tests (-mouse,
   -mouse-file). A timeline of absolute samples 't:x,y,btn' applied on the
   same main-loop clock as -keys. Each sample is written through exactly the
   path the window proc uses -- including the 0xE1 press latch -- so the guest
   cannot tell a scripted pointer from a hand on the mouse. No host cursor is
   moved and no window focus is taken, so a UI test runs headless and does not
   fight the operator (or another agent) for the physical mouse. */
/* Timeline keyboard (-keys-file): 't:scancode' per line, same clock as the
   pointer timeline. The legacy -keys flag (fixed start + interval) still
   works; a timeline lets a UI script interleave typing with clicks. */
#define MAX_INJECT_KEYT 1024
typedef struct { double t_ms; unsigned char sc; } InjectKeyT;
static InjectKeyT inject_keyt[MAX_INJECT_KEYT];
static int inject_keyt_count = 0;
static int inject_keyt_idx = 0;

static void inject_keyt_parse(const char *s) {
    while (*s && inject_keyt_count < MAX_INJECT_KEYT) {
        while (*s == ';' || *s == '\n' || *s == '\r' || *s == ' ' || *s == '\t') s++;
        if (*s == '#') { while (*s && *s != '\n') s++; continue; }
        if (!*s) break;
        double t; int sc;
        if (sscanf(s, "%lf:%d", &t, &sc) == 2) {
            inject_keyt[inject_keyt_count].t_ms = t;
            inject_keyt[inject_keyt_count].sc = (unsigned char)sc;
            inject_keyt_count++;
        } else {
            fprintf(stderr, "-keys-file: cannot parse event near '%.20s'\n", s);
        }
        while (*s && *s != ';' && *s != '\n') s++;
    }
}

#define MAX_INJECT_MOUSE 4096
typedef struct { double t_ms; int x, y, btn; } InjectMouse;
static InjectMouse inject_mouse[MAX_INJECT_MOUSE];
static int inject_mouse_count = 0;
static int inject_mouse_idx = 0;

/* One event: 't:x,y,btn'. Separators between events: ';' or newline. */
static void inject_mouse_parse(const char *s) {
    while (*s && inject_mouse_count < MAX_INJECT_MOUSE) {
        while (*s == ';' || *s == '\n' || *s == '\r' || *s == ' ' || *s == '\t') s++;
        if (*s == '#') { while (*s && *s != '\n') s++; continue; }  /* comment line */
        if (!*s) break;
        double t; int x, y, b;
        if (sscanf(s, "%lf:%d,%d,%d", &t, &x, &y, &b) == 4) {
            inject_mouse[inject_mouse_count].t_ms = t;
            inject_mouse[inject_mouse_count].x = x;
            inject_mouse[inject_mouse_count].y = y;
            inject_mouse[inject_mouse_count].btn = b;
            inject_mouse_count++;
        } else {
            fprintf(stderr, "-mouse: cannot parse event near '%.20s'\n", s);
        }
        while (*s && *s != ';' && *s != '\n') s++;
    }
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

static const char *screenshot_path = NULL;
static int screenshot_delay_ms = 3000;
static int gpu_frame_count = 0;
/* Single-pixel rasterizer probe, off unless CODEX_GPU_PROBE=x,y[,frame] is set. */
static int gpu_probe_on = 0, gpu_probe_x = -1, gpu_probe_y = -1, gpu_probe_frame = 8;
static volatile long gpu_probe_lines = 0;
static void gpu_probe_init(void) {
    const char *e = getenv("CODEX_GPU_PROBE");
    if (!e) return;
    int f = 8;
    if (sscanf(e, "%d,%d,%d", &gpu_probe_x, &gpu_probe_y, &f) >= 2) { gpu_probe_on = 1; gpu_probe_frame = f; }
}
static volatile int gpu_frame_ready = 0;
static int gpu_cine = 0;               /* cinematic mode: additive sparks + bloom + grade (opt-in via port 0x410) */
static long long gpu_cine_last = 0;    /* QPC of previous cine frame, for 60fps pacing */
static void gpu_cinematic_post(void);
static void gpu_cine_pace(void);
static void gpu_composite_band(unsigned int *fb, int w, int h, int y0, int y1);
static void sync_shadow_buffers(void);
static int gpu_last_tri_count = 0;
/* GPU viewport (scissor) rect, inclusive; see gpu_clip_rect for the ports. */
static int gpu_vp_x0 = 0, gpu_vp_y0 = 0, gpu_vp_x1 = 0, gpu_vp_y1 = 0;
static int gpu_vp_active = 0;
/* Shadow map: a light-space depth buffer the main pass samples. Host-side,
   because nothing in the guest ever reads it. */
static unsigned int *gpu_shadow_buf = NULL;
static int gpu_shadow_size = 0;
static int gpu_shadow_pending = 0;
/* Set in the count word at port 0x400 to mean "rasterize into the shadow map
   instead of the screen". Counts never approach this. */
#define GPU_SHADOW_FLAG 0x40000000u
/* r3d-shadow-bias and r3d-shadow-slope in Renderer3D.codex. Matched so the two
   renderers put the acne threshold in the same place. The base covers depth
   quantisation on a surface square to the light; the slope term covers one seen
   at a grazing angle, where a single map texel spans a lot of depth. A flat
   base large enough to cover the grazing case on its own detaches a shadow from
   the object casting it -- measured at 62 per cent of the cast shadow lost. */
#define GPU_SHADOW_BIAS 500
/* Swept against the desk scene, measuring acne on the SPHERE as the difference
   from the same frame with shadows off, and the cast shadow on the ground the
   same way. 6 (the old value) left 350 acne pixels on the sphere; 16 is where
   they reach zero, and it costs 596 of 132,287 ground-shadow pixels, under half
   a per cent. 24 and 40 also read zero acne and take more of the shadow, so 16
   is the knee rather than the largest value that works. The old 6 was swept on
   a CUBE FACE, where flat geometry hides what a curved surface shows: the
   software renderer reads zero acne here at any setting, so this was the host
   path alone being off parity. */
#define GPU_SHADOW_SLOPE 16
/* Per-triangle light-space data for the shadow compare, parallel to the
   command buffer rather than inside it: the 72-byte triangle record is
   written by every rasterizer client in the tree, and six of its words are
   already spoken for as UV (a non-zero UV is what selects the texture path).
   Ten ints per triangle -- (lx,ly,ld) per vertex, then the colour a shadowed
   fragment takes. lx and ly are shadow-map pixels scaled by 1000 and ld is on
   the same 0..1e6 scale as the depth buffer, which is the fixed point the
   software renderer's r3d-project-clip-sw already produces. */
#define GPU_LIGHT_ADDR 0xBE500000ULL
#define GPU_LIGHT_STRIDE 40
static void gpu_shadow_begin(int size);
static void gpu_shadow_render(int count);
static float gpu_light[3] = {0, 0, -1};
static float gpu_eye[3] = {0, 0, -1};
static unsigned long long gpu_tex_guest_addr = 0;
static int gpu_tex_upload_w = 0, gpu_tex_upload_h = 0;
static unsigned long long asset_path_addr = 0;
static unsigned long long asset_dest_addr = 0;
static unsigned long long asset_last_size = 0;
static unsigned char *earth_tex_data = NULL;
static int earth_tex_w = 0, earth_tex_h = 0;
/* 0 = globe shading (GlobeDemo, which uploads nothing and samples the
   procedural earth), 1 = plain modulate. The commit port picks it. */
static int gpu_tex_mode = 0;

/* Shadow buffers: the VGA thread reads ONLY from these, never from guest_mem.
   The main loop copies guest_mem -> shadow between VP exits.  This avoids
   concurrent host/guest access to WHP-mapped pages (triggers vid.sys 0xD1). */
static unsigned char shadow_vga[VGA_COLS * VGA_ROWS * 2];
static unsigned char *shadow_gop = NULL;
static volatile int shadow_gop_w = 0, shadow_gop_h = 0, shadow_gop_stride = 0;

static const COLORREF vga_palette[16] = {
    RGB(0,0,0),       RGB(0,0,170),     RGB(0,170,0),     RGB(0,170,170),
    RGB(170,0,0),     RGB(170,0,170),   RGB(170,85,0),    RGB(170,170,170),
    RGB(85,85,85),    RGB(85,85,255),   RGB(85,255,85),   RGB(85,255,255),
    RGB(255,85,85),   RGB(255,85,255),  RGB(255,255,85),  RGB(255,255,255)
};

static void save_screenshot_bmp(const char *path) {
    if (!shadow_gop || shadow_gop_w <= 0 || shadow_gop_h <= 0) {
        fprintf(stderr, "Screenshot: no GOP framebuffer active\n");
        return;
    }
    int w = shadow_gop_w, h = shadow_gop_h, stride = shadow_gop_stride;
    int row_bytes = w * 3;
    int pad = (4 - (row_bytes % 4)) % 4;
    int padded_row = row_bytes + pad;
    int pixel_size = padded_row * h;
    int file_size = 54 + pixel_size;
    FILE *f = fopen(path, "wb");
    if (!f) { fprintf(stderr, "Screenshot: cannot open %s\n", path); return; }
    unsigned char hdr[54];
    memset(hdr, 0, sizeof(hdr));
    hdr[0] = 'B'; hdr[1] = 'M';
    *(int*)(hdr+2) = file_size;
    *(int*)(hdr+10) = 54;
    *(int*)(hdr+14) = 40;
    *(int*)(hdr+18) = w;
    *(int*)(hdr+22) = h;
    *(short*)(hdr+26) = 1;
    *(short*)(hdr+28) = 24;
    *(int*)(hdr+34) = pixel_size;
    fwrite(hdr, 1, 54, f);
    unsigned char padding[4] = {0,0,0,0};
    for (int y = h - 1; y >= 0; y--) {
        unsigned int *row = (unsigned int*)(shadow_gop + y * stride * 4);
        for (int x = 0; x < w; x++) {
            unsigned int px = row[x];
            unsigned char bgr[3] = { px & 0xFF, (px >> 8) & 0xFF, (px >> 16) & 0xFF };
            fwrite(bgr, 1, 3, f);
        }
        if (pad > 0) fwrite(padding, 1, pad, f);
    }
    fclose(f);
    fprintf(stderr, "Screenshot saved: %s (%dx%d)\n", path, w, h);
}

static void vga_paint(HWND hwnd) {
    PAINTSTRUCT ps;
    HDC hdc = BeginPaint(hwnd, &ps);

    if (shadow_gop && shadow_gop_w > 0 && shadow_gop_h > 0) {
        /* GOP framebuffer mode -- render from shadow (never guest_mem) */
        BITMAPINFO bmi;
        memset(&bmi, 0, sizeof(bmi));
        bmi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
        bmi.bmiHeader.biWidth = shadow_gop_stride;
        bmi.bmiHeader.biHeight = -shadow_gop_h;  /* top-down */
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = BI_RGB;
        /* The SOURCE rectangle is the visible width, not the stride. biWidth
           above carries the stride, which is what makes rows step correctly;
           passing the stride here as well scaled the off-screen pad columns
           into the window. Harmless while every stride equalled a width, and
           reachable the moment -gop-stride exists. Headless runs never call
           this, so no test can see it. */
        StretchDIBits(hdc, 0, 0, shadow_gop_w, shadow_gop_h,
                      0, 0, shadow_gop_w, shadow_gop_h,
                      shadow_gop, &bmi, DIB_RGB_COLORS, SRCCOPY);
    } else {
        /* Text mode -- render from shadow VGA buffer (never guest_mem) */
        HFONT old = (HFONT)SelectObject(hdc, vga_font);
        SetBkMode(hdc, OPAQUE);
        char ch[2] = {0, 0};
        for (int row = 0; row < VGA_ROWS; row++) {
            for (int col = 0; col < VGA_COLS; col++) {
                int off = (row * VGA_COLS + col) * 2;
                unsigned char c = shadow_vga[off];
                unsigned char attr = shadow_vga[off + 1];
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

/* Centre of the client area, in screen coordinates: where the host cursor is
   parked while grabbed and what every delta is measured against. */
static void grab_centre(HWND hwnd, POINT *pt) {
    RECT rc;
    GetClientRect(hwnd, &rc);
    pt->x = (rc.right - rc.left) / 2;
    pt->y = (rc.bottom - rc.top) / 2;
    ClientToScreen(hwnd, pt);
}

static void grab_set(HWND hwnd, int on) {
    if (on == mouse_grabbed) return;
    mouse_grabbed = on;
    if (on) {
        RECT rc;
        POINT c;
        GetClientRect(hwnd, &rc);
        MapWindowPoints(hwnd, NULL, (POINT *)&rc, 2);
        ClipCursor(&rc);
        SetCapture(hwnd);
        ShowCursor(FALSE);
        grab_centre(hwnd, &c);
        grab_warping = 1;
        SetCursorPos(c.x, c.y);
        vga_update_title();
    } else {
        ClipCursor(NULL);
        ReleaseCapture();
        ShowCursor(TRUE);
        vga_update_title();
    }
}

static LRESULT CALLBACK vga_wndproc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    switch (msg) {
    case WM_KILLFOCUS:
        /* A grab that outlives focus takes the pointer away from whatever the
           operator switched to, which is the one behaviour nobody forgives. */
        grab_set(hwnd, 0);
        return 0;
    case WM_PAINT:
        vga_paint(hwnd);
        return 0;
    case WM_TIMER:
        if (wp == VGA_TIMER_ID) {
            InvalidateRect(hwnd, NULL, FALSE);
            /* HDA drain moved to main loop -- must not touch guest_mem here */
        }
        return 0;
    case WM_KEYDOWN: {
        /* Ctrl+Alt+G is the emulator's, not the guest's: swallowed here so the
           guest never sees a G it did not earn. */
        if ((int)wp == 'G' && (GetKeyState(VK_CONTROL) & 0x8000) &&
            (GetKeyState(VK_MENU) & 0x8000)) {
            grab_set(hwnd, !mouse_grabbed);
            return 0;
        }
        unsigned char sc = vk_to_scancode((int)wp);
        if (sc) {
            kbd_enqueue(sc); kbd_irq_pending = 1;
            hid_key_event(sc);
            pending_kbd_scancode = (unsigned long long)sc;
            pending_kbd_valid = 1;
        }
        return 0;
    }
    case WM_KEYUP: {
        unsigned char sc = vk_to_scancode((int)wp);
        if (sc) { kbd_enqueue(sc | 0x80); kbd_irq_pending = 1; hid_key_event(sc | 0x80); }
        return 0;
    }
    case WM_LBUTTONDOWN: case WM_RBUTTONDOWN: case WM_MBUTTONDOWN:
    case WM_LBUTTONUP: case WM_RBUTTONUP: case WM_MBUTTONUP:
    case WM_MOUSEMOVE: {
        if (msg == WM_LBUTTONDOWN && !mouse_captured) {
            SetCapture(hwnd); mouse_captured = 1;
        }
        if (msg == WM_LBUTTONUP && mouse_captured) {
            ReleaseCapture(); mouse_captured = 0;
        }
        if (mouse_grabbed) {
            POINT c;
            int cx, cy, dx, dy;
            if (grab_warping) { grab_warping = 0; return 0; }
            grab_centre(hwnd, &c);
            {
                POINT here = c;
                ScreenToClient(hwnd, &here);
                cx = here.x; cy = here.y;
            }
            dx = (short)LOWORD(lp) - cx;
            dy = (short)HIWORD(lp) - cy;
            if (msg == WM_MOUSEMOVE && (dx || dy)) {
                int nx = pending_mouse_abs_x + dx;
                int ny = pending_mouse_abs_y + dy;
                if (nx < 0) nx = 0; if (ny < 0) ny = 0;
                if (nx > gop_width - 1) nx = gop_width - 1;
                if (ny > gop_height - 1) ny = gop_height - 1;
                pending_mouse_abs_x = nx;
                pending_mouse_abs_y = ny;
                grab_warping = 1;
                SetCursorPos(c.x, c.y);
            }
        } else {
            pending_mouse_abs_x = (short)LOWORD(lp);
            pending_mouse_abs_y = (short)HIWORD(lp);
        }
        int prev_btn = pending_mouse_btn;
        int new_btn = 0;
        if (wp & MK_LBUTTON) new_btn |= 1;
        if (wp & MK_RBUTTON) new_btn |= 2;
        if (wp & MK_MBUTTON) new_btn |= 4;
        LONG pressed = (LONG)(new_btn & ~prev_btn);
        if (pressed) InterlockedOr(&pending_mouse_btn_latch, pressed);
        pending_mouse_btn = new_btn;
        pending_mouse_valid = 1;
        hid_mouse_fresh = 1;
        hid_input_changed = 1;
        return 0;
    }
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
    const char *title = gop_active ? "Codex VM - GOP" : "Codex VM";
    RECT r = {0, 0, win_w, win_h};
    AdjustWindowRect(&r, WS_OVERLAPPEDWINDOW, FALSE);
    vga_hwnd = CreateWindowA("CodexVmVGA", title,
        WS_OVERLAPPEDWINDOW | WS_VISIBLE,
        CW_USEDEFAULT, CW_USEDEFAULT,
        r.right - r.left, r.bottom - r.top,
        NULL, NULL, wc.hInstance, NULL);
    vga_font = CreateFontA(CHAR_H, CHAR_W, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
        OEM_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
        NONANTIALIASED_QUALITY, FIXED_PITCH | FF_MODERN, "Courier New");
    vga_update_title();   /* the hint is there before the guest sets a mode */
    SetTimer(vga_hwnd, VGA_TIMER_ID, 16, NULL);  /* refresh ~60Hz */
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
    /* Single-instance guard for DISPLAY windows only. Headless VMs (compiles,
       the -Jobs N test harness) stay unrestricted -- the whp_mutex already
       serializes their partition ops. But two on-screen WHP display VMs can
       hang or bug-check the host, so refuse a second window. */
    HANDLE inst = CreateMutexA(NULL, TRUE, "Global\\CodexVmDisplayWindow");
    if (inst && GetLastError() == ERROR_ALREADY_EXISTS) {
        fprintf(stderr, "ERROR: another codex-vm display window is already running.\n");
        fprintf(stderr, "Close it first -- running two on-screen WHP VMs can hang the host.\n");
        exit(1);
    }
    CreateThread(NULL, 0, vga_thread, NULL, 0, NULL);
}

#define GUEST_MEM_BASE  0
#define LOAD_ADDR       0x100000
#define STACK_TOP       0x7FFE00
#define PAGE_TABLE_ADDR 0xC00000
/* Ceiling on the UEFI identity map, in GB. 64 PDs cost 264 KB of guest RAM
   at PAGE_TABLE_ADDR and cover more than any bed needs; past it the map
   prints that it is short rather than faulting the guest silently. */
#define UEFI_MAP_MAX_GB 64
#define MAX_MEM         (16ULL*1024*1024*1024)

/* Memory-mapped I/O.
   Input: pre-loaded at 0x500000 (2 MB). Output: guest writes to ring
   buffer at 0x700000 (2 MB) via mmio; VM drains on doorbell or exit.
   Legacy serial (COM1 OUT) is also captured for old-seed compat.

   Layout:  0x100000  Code (4 MB)
            0x500000  Input ring buffer (2 MB)
            0x700000  Output ring buffer (2 MB)
            0x900000  Heap                                             */
#define INPUT_BUF_ADDR        0x500000
/* The input ceiling is host-side policy, not a guest constraint: only the
   first GUEST_RING_SIZE bytes land in guest RAM, the rest sits in the
   malloc'd drip-feed overflow. 16 MB refused a wide-citation program's IR
   outright (ui-orchestrator-test streams 48 MB, measured 2026-07-28). */
#define INPUT_BUF_MAX         0x10000000  /* 256 MB */
#define GUEST_RING_SIZE       0x100000  /* 1 MB -- must match seed's serial-ring-buf-size */
#define GUEST_RING_MASK       0x0FFFFF

/* Drip-feed state: host-side overflow buffer for input > GUEST_RING_SIZE */
static unsigned char *input_overflow = NULL;
static size_t input_overflow_len = 0;
static size_t input_overflow_pos = 0;
static unsigned long long input_total_written = 0;
/* The legacy output ring (0x700000, write position at guest cell 36152)
   is retired: that GPA is live heap in every current guest, no seed
   lineage still writes the ring, and the drain misread any guest value
   parked at 36152 as a byte count (it turned the first spawn-pool
   cursor into gigabytes of zero output). Output leaves via the blit
   doorbell or the per-byte COM1 path only. Cell 36152 stays reserved:
   do not hand it to new guest metadata. */
#define DOORBELL_PORT         0x510
#define DOORBELL_DATA_READY   0x01
#define DOORBELL_COMPILE_DONE 0x02
#define DOORBELL_BLIT         0x03
#define DOORBELL_FATAL        0xFF

/* Bulk output blit: on OUT 0x510, 0x03 the VM appends guest RAM
   [addr, addr+len) directly to the output buffer, where addr/len are
   qwords the guest stored at the two cells below (free space in the
   PML4 page above try-fail-flag 36128, below the PDPT at 36864).
   One VM exit replaces two per byte (LSR poll + THR write).
   IN 0x511 returns BLIT_PROBE_MAGIC so the guest can detect support;
   older VMs and real hardware float 0xFF and the guest falls back to
   the per-byte COM1 loop. Port 0x510 IN is deliberately untouched:
   emit-lapic-disable probes it expecting the 0xFF default. */
#define BLIT_ADDR_CELL        36160
#define BLIT_LEN_CELL         36168
#define BLIT_PROBE_PORT       0x511
#define BLIT_PROBE_MAGIC      0xB7

/* IDE state -- typedef is above (forward-declared for UEFI emulation) */

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
    int page;          /* 0, 1, or 2 -- from CR bits 7:6 */
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

static int ne2k_out_count = 0;
static void ne2k_handle_out(int port, int val, int io_size) {
    int off = port - NE2K_BASE;
    ne2k_out_count++;
    if (off == 0x1F) { ne2k_reset(); return; }  /* reset port */
    if (off == 0x10) {
        /* DATA port write -- remote DMA write into NIC memory */
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
        /* CR -- command register */
        ne2k.page = (val >> 6) & 3;
        ne2k.started = (val & 2) ? 1 : 0;
        if (val & 4) {
            /* TXP -- transmit packet */
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
        case 0x03: ne2k.bnry = val; ne2k_inject_rx(); break;
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
        /* DATA port read -- remote DMA read from NIC memory, wrapping at ring boundary */
        int val = 0;
        int ring_end = ne2k.pstop * 256;
        int ring_start = ne2k.pstart * 256;
        if (ne2k.rbcr > 0 && ne2k.rsar < NE2K_MEM_SIZE) {
            val = ne2k.mem[ne2k.rsar];
            ne2k.rsar++;
            if (ne2k.rsar >= ring_end && ring_end > ring_start) ne2k.rsar = ring_start;
            ne2k.rbcr--;
            if (io_size >= 2 && ne2k.rbcr > 0 && ne2k.rsar < NE2K_MEM_SIZE) {
                val |= ne2k.mem[ne2k.rsar] << 8;
                ne2k.rsar++;
                if (ne2k.rsar >= ring_end && ring_end > ring_start) ne2k.rsar = ring_start;
                ne2k.rbcr--;
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

/* Port forwarding: host listens, guest accepts */
#define PORTFWD_MAX 8
#define PF_TCP 0
#define PF_UDP 1
typedef struct {
    int active;
    int proto;              /* PF_TCP or PF_UDP */
    SOCKET listen_sock;
    unsigned short host_port;
    unsigned short guest_port;
} PortFwd;
static PortFwd portfwds[PORTFWD_MAX];
static int portfwd_count = 0;

/* Outbound destination-port remap, the opposite direction to -portfwd above.
   A guest dials a port compiled into it -- every transpiler plug carries one
   fixed port from build/plug-ports.ps1 -- and the NAT connects to the host on
   exactly that port, so N copies of one plug cannot run at once: they all need
   the same host listener. This table lets each VM be told "when the guest asks
   for G, connect to H instead", so N workers each own a private host port
   while running the same unmodified plug binary. TCP only; the UDP path is
   deliberately not remapped because nothing needs it and untested code here
   would be worse than the asymmetry. */
#define NATMAP_MAX 16
typedef struct {
    unsigned short guest_dport;
    unsigned short host_port;
} NatMap;
static NatMap natmaps[NATMAP_MAX];
static int natmap_count = 0;

static unsigned short natmap_host_port(unsigned short dport) {
    for (int i = 0; i < natmap_count; i++) {
        if (natmaps[i].guest_dport == dport) return natmaps[i].host_port;
    }
    return dport;
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

/* The lease this NAT offers, in seconds. An hour is the sensible default
   and it is useless as a bed: a client renews at half its lease, so
   nothing observes a renewal inside a test. -dhcp-lease <n> is what makes
   the renewal reachable in a run that lasts seconds. */
static unsigned int nat_dhcp_lease = 3600;

typedef struct {
    int active;
    SOCKET sock;
    unsigned char dst_ip[4];
    unsigned short guest_port;
    unsigned short dst_port;
    /* One convention, both directions: seq_offset lives in the GUEST's
       sequence space and ack_offset in OURS. nat_poll_rx is the shared
       emitter for every connection kind and has always read them that
       way -- it sends ack_offset as its seq and seq_offset + 1 as its
       ack. The port-forward path used to assign them the other way
       round, so an inbound connection's data frames carried a sequence
       number drawn from the guest's own space. It worked only because
       the guest's stack does not validate the receive window. */
    unsigned long seq_offset;  /* guest sequence space */
    unsigned long ack_offset;  /* our sequence space */
    int state;  /* 0=unused, 1=connecting, 2=established, 3=guest sent FIN, 4=host sent FIN */
    int forwarded; /* 1 = inbound port-forwarded connection */
    unsigned long guest_ack;  /* last ACK from guest (for forwarded conns) */
    unsigned long guest_isn;  /* guest's SYN sequence, for the deferred SYN-ACK */
    /* Bytes the guest handed us that the host socket has not taken yet:
       either the connect has not completed or send() said WOULDBLOCK.
       Dropping these is what made an outbound request vanish against any
       peer that was not instantaneous. */
    unsigned char *txbuf;
    int txlen;
    int txcap;
    /* Proper TCP toward the guest (the direction codex-vm is the SENDER
       for): a forwarded SYN and every injected data byte are held until
       the guest ACKs them, and retransmitted on a timer if it does not.
       Without this the injection was one-shot -- a SYN that arrived while
       a single-threaded guest server was busy on another connection, or a
       data frame the guest was not yet in a recv loop to read, was dropped
       and never resent, so the request never arrived. rtxbuf holds the
       un-ACKed host->guest bytes starting at our-sequence rtx_base;
       rtx_acked is the highest of our sequence space the guest has
       acknowledged; last_tx_ms and rtx_count drive and bound the timer. */
    unsigned char *rtxbuf;
    int rtxlen;
    int rtxcap;
    unsigned long rtx_base;
    unsigned long rtx_acked;
    double last_tx_ms;
    int rtx_count;
    /* When this connection became half-closed. Freeing on the closing
       handshake alone is not enough: whichever side closes SECOND may
       simply never be heard from -- a host client that disconnects
       leaves the connection in state 4 waiting on a guest FIN that a
       server with nothing more to say has no reason to send. A real
       stack does not wait forever either; it times the state out. */
    double closed_at_ms;
    /* The guest has sent FIN but bytes it handed us are still queued here,
       so the host send side must NOT go down yet. shutdown(SD_SEND) closes
       the socket's send direction and the kernel has never seen these
       bytes: they sit in txbuf, which is ours. */
    int shutdown_pending;
    int free_pending;
} NatConn;

/* Idle time, not total time: activity on a half-closed connection pushes
   the deadline out (see the ACK path), so this only reaps silence. */
#define NAT_HALF_CLOSED_TIMEOUT_MS 5000.0

/* How long VM exit will wait for a slow reader to take what the guest
   already handed us. It is generous because the alternative is to discard
   the guest's output: a 16 MB send whose reader stalls near the end left
   half a megabyte in this buffer, and 2 s of draining was not enough for
   it. A residual after this bound is reported by the byte census rather
   than dropped in silence. */
#define NAT_EXIT_DRAIN_MS 30000.0

/* Retransmit interval and cap for host->guest SYN/data. The guest polls
   its NIC fast, so a short interval delivers within its wait; the cap
   bounds a connection whose guest never answers (it is freed and the host
   client, which has its own timeout, retries). */
#define NAT_RTO_MS 50.0
#define NAT_MAX_RTX 240

static NatConn nat_conns[NAT_MAX_CONN];
static void portfwd_handle_synack(NatConn *c, unsigned long seq, unsigned long ack);

static NatConn *nat_find(unsigned short guest_port, unsigned short dst_port, unsigned char *dst_ip) {
    for (int i = 0; i < NAT_MAX_CONN; i++) {
        NatConn *c = &nat_conns[i];
        if (c->active && c->guest_port == guest_port && c->dst_port == dst_port &&
            memcmp(c->dst_ip, dst_ip, 4) == 0) return c;
    }
    return NULL;
}

static void nat_conn_free(NatConn *c);

/* Guest-to-host byte census. Every site on this path that can discard a
   byte increments one of these, and they are printed at exit. A send that
   arrives short is otherwise invisible from either end: the guest's own
   accounting says complete, and net-send-raw returns the frame length
   unconditionally after kicking TXP, so the guest cannot report a transmit
   failure even in principle. */
static unsigned long long nat_seg_bytes = 0;      /* payload in guest data segments */
static unsigned long long nat_seg_noconn = 0;     /* dropped: no NatConn matched */
static unsigned long long nat_seg_badstate = 0;   /* dropped: connection not writable */
static unsigned long long nat_queued = 0;         /* appended to the pending buffer */
static unsigned long long nat_queue_oom = 0;      /* dropped: realloc failed */
static unsigned long long nat_sock_sent = 0;      /* taken by send() */
static unsigned long long nat_freed_unsent = 0;   /* dropped: buffer freed still holding bytes */
static unsigned long long nat_freed_reap = 0;     /* ...of which: the half-closed reaper */
static unsigned long long nat_freed_exit = 0;     /* ...of which: VM exit gave up draining */

static NatConn *nat_alloc(void) {
    int oldest = -1;
    for (int i = 0; i < NAT_MAX_CONN; i++)
        if (!nat_conns[i].active) {
            /* The slot may still own a buffer from its previous tenant --
               memset alone would leak it and hand the next connection a
               dangling pointer. */
            if (nat_conns[i].txlen > 0) nat_freed_unsent += (unsigned long long)nat_conns[i].txlen;
            if (nat_conns[i].txbuf) free(nat_conns[i].txbuf);
            if (nat_conns[i].rtxbuf) free(nat_conns[i].rtxbuf);
            memset(&nat_conns[i], 0, sizeof(NatConn));
            return &nat_conns[i];
        }
    /* Nothing free. Rather than refuse the connection in silence, take
       the longest-standing half-closed slot: it is a connection one side
       has already finished with, and holding it is worth less than
       serving the one being asked for now. */
    for (int i = 0; i < NAT_MAX_CONN; i++) {
        if (nat_conns[i].state != 3 && nat_conns[i].state != 4) continue;
        if (oldest < 0 || nat_conns[i].closed_at_ms < nat_conns[oldest].closed_at_ms)
            oldest = i;
    }
    if (oldest >= 0) {
        nat_conn_free(&nat_conns[oldest]);
        memset(&nat_conns[oldest], 0, sizeof(NatConn));
        return &nat_conns[oldest];
    }
    fprintf(stderr, "NAT: connection table full (%d live) -- dropping\n", NAT_MAX_CONN);
    return NULL;
}

/* Release a connection: close the socket, drop any unsent bytes, and make
   the slot reusable. Nothing did this for a connection the guest had
   closed -- the slot stayed active with its socket open, so 64 ordinary
   closes exhausted the table and every connection after that was dropped
   in silence. */
static void nat_conn_free(NatConn *c) {
    if (c->sock != INVALID_SOCKET && c->sock != 0) closesocket(c->sock);
    c->sock = INVALID_SOCKET;
    if (c->txlen > 0) nat_freed_unsent += (unsigned long long)c->txlen;
    if (c->txbuf) { free(c->txbuf); c->txbuf = NULL; }
    c->txlen = 0;
    c->txcap = 0;
    if (c->rtxbuf) { free(c->rtxbuf); c->rtxbuf = NULL; }
    c->rtxlen = 0;
    c->rtxcap = 0;
    c->active = 0;
    c->state = 0;
    c->shutdown_pending = 0;
    c->free_pending = 0;
}

/* Append host->guest bytes to the retransmit buffer, tagged with the
   our-sequence of the first byte, so they can be resent until the guest
   ACKs them. Bytes are contiguous, so rtx_base marks the first un-ACKed
   byte and the buffer grows behind it. */
static void nat_rtx_append(NatConn *c, unsigned long seq, const unsigned char *p, int n) {
    if (n <= 0) return;
    if (c->rtxlen == 0) c->rtx_base = seq;
    if (c->rtxlen + n > c->rtxcap) {
        int cap = c->rtxcap ? c->rtxcap : 4096;
        while (cap < c->rtxlen + n) cap *= 2;
        unsigned char *nb = (unsigned char *)realloc(c->rtxbuf, cap);
        if (!nb) return;
        c->rtxbuf = nb;
        c->rtxcap = cap;
    }
    memcpy(c->rtxbuf + c->rtxlen, p, n);
    c->rtxlen += n;
}

/* The guest ACKed up to `ack` in our sequence space: drop everything the
   buffer holds below it. */
static void nat_rtx_ack(NatConn *c, unsigned long ack) {
    if ((long)(ack - c->rtx_acked) > 0) c->rtx_acked = ack;
    if (c->rtxlen <= 0) return;
    long drop = (long)(ack - c->rtx_base);
    if (drop <= 0) return;
    if (drop >= c->rtxlen) { c->rtxlen = 0; return; }
    memmove(c->rtxbuf, c->rtxbuf + drop, (size_t)(c->rtxlen - drop));
    c->rtxlen -= drop;
    c->rtx_base = ack;
}

/* Queue bytes the host socket could not take yet. */
static void nat_tx_queue(NatConn *c, const unsigned char *p, int n) {
    if (n <= 0) return;
    if (c->txlen + n > c->txcap) {
        int cap = c->txcap ? c->txcap : 4096;
        unsigned char *nb;
        while (cap < c->txlen + n) cap *= 2;
        nb = (unsigned char *)realloc(c->txbuf, (size_t)cap);
        if (!nb) { nat_queue_oom += (unsigned long long)n; return; }  /* drop, do not corrupt */
        c->txbuf = nb;
        c->txcap = cap;
    }
    memcpy(c->txbuf + c->txlen, p, (size_t)n);
    c->txlen += n;
    nat_queued += (unsigned long long)n;
}

/* Push as much of the pending buffer as the socket will take. A partial
   send or WSAEWOULDBLOCK leaves the remainder queued for the next poll
   rather than discarding it, which is what used to happen. */
static void nat_tx_flush(NatConn *c) {
    int sent;
    if (c->txlen <= 0) return;
    sent = send(c->sock, (const char *)c->txbuf, c->txlen, 0);
    if (sent <= 0) return;          /* WOULDBLOCK or error: keep it all */
    nat_sock_sent += (unsigned long long)sent;
    if (sent < c->txlen) memmove(c->txbuf, c->txbuf + sent, (size_t)(c->txlen - sent));
    c->txlen -= sent;
}

/* Pending RX frames for the guest */
#define RX_QUEUE_SIZE 256
#define RX_FRAME_MAX 1536
typedef struct {
    unsigned char data[RX_FRAME_MAX];
    int len;
} RxFrame;

static RxFrame rx_queue[RX_QUEUE_SIZE];
static int rx_queue_head = 0, rx_queue_count = 0;

/* The address guest-bound frames are sent TO. The NE2000 path takes it from
   PAR, which the driver programs; the e1000 path has no such register write
   to watch, so it uses what the guest's own transmits carried. Returning
   PAR whenever the e1000 NAT is off is what keeps every existing run
   byte-identical. */
static unsigned char *nat_guest_mac(void) {
    if (!e1000_nat) return ne2k.par;
    /* Before the guest has transmitted there is nothing to have learned, so
       the card is addressed at the address it answers RAL/RAH with. A peer
       reaching this machine cold has the same two choices and takes the
       same one. */
    return e1000_peer_known ? e1000_peer_mac : (unsigned char *)e1000_station_mac;
}

static void rx_enqueue(unsigned char *data, int len) {
    if (rx_queue_count >= RX_QUEUE_SIZE || len > RX_FRAME_MAX) {
        fprintf(stderr, "rx_enqueue DROP: q=%d len=%d max=%d\n", rx_queue_count, len, RX_FRAME_MAX);
        return;
    }
    int idx = (rx_queue_head + rx_queue_count) % RX_QUEUE_SIZE;
    memcpy(rx_queue[idx].data, data, len);
    rx_queue[idx].len = len;
    rx_queue_count++;
    fprintf(stderr, "rx_enqueue: q=%d len=%d\n", rx_queue_count, len);
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

/* Build an IP/UDP response frame and enqueue it */
static void nat_build_udp_frame(unsigned char *dst_mac, unsigned char *src_ip, unsigned char *dst_ip,
                                unsigned short src_port, unsigned short dst_port,
                                unsigned char *payload, int payload_len) {
    unsigned char frame[1536];
    int udp_len = 8 + payload_len;
    int ip_len = 20 + udp_len;
    int total = 14 + ip_len;
    if (total > 1536) return;

    /* Ethernet */
    memcpy(frame, dst_mac, 6);
    memcpy(frame + 6, nat_gw_mac, 6);
    frame[12] = 0x08; frame[13] = 0x00; /* IPv4 */

    /* IP header */
    unsigned char *ip = frame + 14;
    ip[0] = 0x45; ip[1] = 0; ip[2] = ip_len >> 8; ip[3] = ip_len & 0xFF;
    ip[4] = 0; ip[5] = 0; ip[6] = 0x40; ip[7] = 0; /* Don't Fragment */
    ip[8] = 64; ip[9] = 17; /* TTL=64, proto=UDP */
    ip[10] = 0; ip[11] = 0;
    memcpy(ip + 12, src_ip, 4);
    memcpy(ip + 16, dst_ip, 4);
    unsigned short ipcsum = ip_checksum(ip, 20);
    ip[10] = ipcsum >> 8; ip[11] = ipcsum & 0xFF;

    /* UDP header */
    unsigned char *udp = ip + 20;
    udp[0] = src_port >> 8; udp[1] = src_port & 0xFF;
    udp[2] = dst_port >> 8; udp[3] = dst_port & 0xFF;
    udp[4] = udp_len >> 8;  udp[5] = udp_len & 0xFF;
    udp[6] = 0; udp[7] = 0; /* checksum placeholder */
    if (payload_len > 0) memcpy(udp + 8, payload, payload_len);

    /* UDP checksum over pseudo-header + datagram */
    unsigned long usum = 0;
    for (int i = 0; i < 4; i += 2) usum += (src_ip[i] << 8) | src_ip[i + 1];
    for (int i = 0; i < 4; i += 2) usum += (dst_ip[i] << 8) | dst_ip[i + 1];
    usum += 17;
    usum += udp_len;
    for (int i = 0; i < udp_len - 1; i += 2) usum += (udp[i] << 8) | udp[i + 1];
    if (udp_len & 1) usum += udp[udp_len - 1] << 8;
    while (usum >> 16) usum = (usum & 0xFFFF) + (usum >> 16);
    unsigned short ucsum = (unsigned short)(~usum & 0xFFFF);
    /* A zero checksum means "not computed" on the wire, so the ones'
       complement of zero is transmitted as 0xFFFF instead. */
    if (ucsum == 0) ucsum = 0xFFFF;
    udp[6] = ucsum >> 8; udp[7] = ucsum & 0xFF;

    rx_enqueue(frame, total);
}

/* Answer a DNS A-record query from the guest.

   The guest asks 10.0.2.3:53 because that is the resolver this NAT
   advertises. Nothing was listening: the UDP branch of nat_handle_tx was
   an empty block with a comment saying forwarding "could go here", so
   every query the guest ever sent was dropped and every lookup timed out.
   The Operator's Manual claimed DNS worked the whole time.

   The name is resolved with getaddrinfo, which is the host's own resolver
   -- so the hosts file, the search domain and whatever DNS the host is
   actually configured with all apply, and no packet leaves this process.
   The answer is then dressed as a DNS response so the guest's resolver
   parses a real one. Only QTYPE=A/IN is answered; anything else comes
   back NXDOMAIN rather than a lie. */
static void nat_handle_dns(unsigned char *q, int qlen, unsigned short guest_port,
                           unsigned char *dns_ip) {
    if (qlen < 12 + 5) return;

    /* Walk the QNAME labels into a dotted host name. */
    char host[256];
    int hp = 0;
    int i = 12;
    while (i < qlen && q[i] != 0) {
        int label = q[i];
        if (label > 63 || i + 1 + label > qlen) return;   /* compression or garbage */
        if (hp + label + 1 >= (int)sizeof(host)) return;
        if (hp > 0) host[hp++] = '.';
        memcpy(host + hp, q + i + 1, label);
        hp += label;
        i += 1 + label;
    }
    if (i >= qlen) return;
    host[hp] = 0;
    int qname_end = i + 1;                 /* past the root label */
    if (qname_end + 4 > qlen) return;
    unsigned short qtype  = (q[qname_end] << 8) | q[qname_end + 1];
    unsigned short qclass = (q[qname_end + 2] << 8) | q[qname_end + 3];
    int question_len = qname_end + 4 - 12; /* QNAME + QTYPE + QCLASS */

    unsigned char addr[4];
    int found = 0;
    if (qtype == 1 && qclass == 1) {
        struct addrinfo hints, *res = NULL;
        memset(&hints, 0, sizeof(hints));
        hints.ai_family = AF_INET;
        hints.ai_socktype = SOCK_STREAM;
        if (getaddrinfo(host, NULL, &hints, &res) == 0 && res) {
            struct sockaddr_in *sa = (struct sockaddr_in *)res->ai_addr;
            memcpy(addr, &sa->sin_addr, 4);
            found = 1;
            freeaddrinfo(res);
        }
    }
    fprintf(stderr, "NAT DNS: %s -> %s", host,
            found ? "" : "NXDOMAIN\n");
    if (found) fprintf(stderr, "%d.%d.%d.%d\n", addr[0], addr[1], addr[2], addr[3]);

    unsigned char resp[512];
    int n = 0;
    resp[n++] = q[0]; resp[n++] = q[1];               /* transaction id */
    resp[n++] = 0x81;                                  /* response, recursion desired */
    resp[n++] = (unsigned char)(found ? 0x80 : 0x83);  /* recursion available / NXDOMAIN */
    resp[n++] = 0; resp[n++] = 1;                      /* QDCOUNT */
    resp[n++] = 0; resp[n++] = (unsigned char)(found ? 1 : 0); /* ANCOUNT */
    resp[n++] = 0; resp[n++] = 0;                      /* NSCOUNT */
    resp[n++] = 0; resp[n++] = 0;                      /* ARCOUNT */
    memcpy(resp + n, q + 12, question_len);            /* echo the question */
    n += question_len;
    if (found) {
        resp[n++] = 0xC0; resp[n++] = 0x0C;            /* name: pointer to offset 12 */
        resp[n++] = 0; resp[n++] = 1;                  /* TYPE A */
        resp[n++] = 0; resp[n++] = 1;                  /* CLASS IN */
        resp[n++] = 0; resp[n++] = 0; resp[n++] = 0; resp[n++] = 60; /* TTL */
        resp[n++] = 0; resp[n++] = 4;                  /* RDLENGTH */
        memcpy(resp + n, addr, 4);
        n += 4;
    }

    unsigned char guest_ip[4] = {NAT_GUEST_IP0, NAT_GUEST_IP1, NAT_GUEST_IP2, NAT_GUEST_IP3};
    nat_build_udp_frame(nat_guest_mac(), dns_ip, guest_ip, 53, guest_port, resp, n);
}

/* ------------------------------------------------------------------ */
/* General UDP forwarding.                                             */
/*                                                                     */
/* Until this existed the guest's UDP was answered for destination     */
/* port 53 and dropped for every other port, with a printed reason. So */
/* CoAP could not reach a server, NTP could not reach a time source,   */
/* and any datagram protocol at all was untestable under this          */
/* emulator -- which is a property of the emulator being read as a     */
/* property of the guest.                                              */
/*                                                                     */
/* A UDP flow is not a connection and is not modelled as one. There is */
/* no handshake, no sequence space and nothing to retransmit: a flow   */
/* is a host socket remembered long enough for a reply to come back to */
/* the port the guest sent from. It is keyed the same way a NatConn is */
/* -- guest port, destination port, destination address -- and reaped  */
/* on idleness rather than on any close, because a datagram peer never */
/* says goodbye.                                                       */
/* ------------------------------------------------------------------ */
#define UDP_MAX_FLOW 32
#define UDP_FLOW_IDLE_MS 30000.0

typedef struct {
    int active;
    SOCKET sock;
    unsigned char dst_ip[4];
    unsigned short guest_port;
    unsigned short dst_port;
    double last_ms;
} UdpFlow;

static UdpFlow udp_flows[UDP_MAX_FLOW];

/* The gateway address IS the host, which is the convention every
   user-mode NAT uses and the one this emulator advertises in DHCP and
   answers ARP for -- but nothing ever translated it, so a guest that
   addressed 10.0.2.2 had its packet handed to the host's own stack as a
   literal destination and it went nowhere. A service running on the
   machine codex-vm is running on is reachable at 127.0.0.1 from here,
   so that is what the gateway address means when a host socket is
   opened. Every other address is passed through untouched. */
static void nat_host_addr(unsigned char *dst_ip, struct sockaddr_in *addr) {
    memset(addr, 0, sizeof(*addr));
    addr->sin_family = AF_INET;
    if (dst_ip[0] == NAT_GW_IP0 && dst_ip[1] == NAT_GW_IP1 &&
        dst_ip[2] == NAT_GW_IP2 && dst_ip[3] == NAT_GW_IP3) {
        unsigned char loopback[4] = {127, 0, 0, 1};
        memcpy(&addr->sin_addr, loopback, 4);
    } else {
        memcpy(&addr->sin_addr, dst_ip, 4);
    }
}

static void udp_flow_free(UdpFlow *f) {
    if (!f->active) return;
    if (f->sock != INVALID_SOCKET) closesocket(f->sock);
    memset(f, 0, sizeof(UdpFlow));
}

static UdpFlow *udp_flow_find(unsigned short guest_port, unsigned short dst_port, unsigned char *dst_ip) {
    for (int i = 0; i < UDP_MAX_FLOW; i++) {
        UdpFlow *f = &udp_flows[i];
        if (f->active && f->guest_port == guest_port && f->dst_port == dst_port &&
            memcmp(f->dst_ip, dst_ip, 4) == 0) return f;
    }
    return NULL;
}

/* No free slot means the oldest one goes. A flow is cheap and silent;
   refusing the new datagram instead would make the failure look like
   packet loss, which is exactly the thing a UDP caller is least able to
   tell from a bug. */
static UdpFlow *udp_flow_alloc(void) {
    int oldest = -1;
    for (int i = 0; i < UDP_MAX_FLOW; i++)
        if (!udp_flows[i].active) { memset(&udp_flows[i], 0, sizeof(UdpFlow)); return &udp_flows[i]; }
    for (int i = 0; i < UDP_MAX_FLOW; i++)
        if (oldest < 0 || udp_flows[i].last_ms < udp_flows[oldest].last_ms) oldest = i;
    udp_flow_free(&udp_flows[oldest]);
    return &udp_flows[oldest];
}

/* ------------------------------------------------------------------ */
/* Inbound UDP: a host client, a guest server.                         */
/*                                                                     */
/* The outbound direction above makes the guest a datagram CLIENT. This */
/* makes it a SERVER, which is the role an IoT device actually plays -- */
/* a CoAP endpoint and an LwM2M client are both servers that a          */
/* management peer pokes.                                              */
/*                                                                     */
/* The guest must be able to answer, and a datagram carries no          */
/* connection to answer along, so each distinct host client is given a  */
/* SYNTHETIC source port on the gateway. The guest replies to that port */
/* the way it would reply to any peer, and the reply is matched back to */
/* the client by it. Without the synthetic port there is nothing in the */
/* guest's reply that names which of several host clients it is for.   */
/* ------------------------------------------------------------------ */
#define UDP_IN_MAX 16
#define UDP_IN_PORT_BASE 40000

typedef struct {
    int active;
    SOCKET sock;                /* the forward's listening socket, replied on */
    struct sockaddr_in client;  /* the host peer */
    unsigned short synth_port;  /* our source port toward the guest */
    unsigned short guest_port;
    double last_ms;
} UdpInFlow;

static UdpInFlow udp_in_flows[UDP_IN_MAX];
static unsigned short udp_in_next_port = UDP_IN_PORT_BASE;

static UdpInFlow *udp_in_find_client(struct sockaddr_in *c, unsigned short guest_port) {
    for (int i = 0; i < UDP_IN_MAX; i++) {
        UdpInFlow *f = &udp_in_flows[i];
        if (f->active && f->guest_port == guest_port &&
            f->client.sin_addr.s_addr == c->sin_addr.s_addr &&
            f->client.sin_port == c->sin_port) return f;
    }
    return NULL;
}

/* The guest is answering: find the client whose synthetic port it used. */
static UdpInFlow *udp_in_find_synth(unsigned short synth_port) {
    for (int i = 0; i < UDP_IN_MAX; i++) {
        UdpInFlow *f = &udp_in_flows[i];
        if (f->active && f->synth_port == synth_port) return f;
    }
    return NULL;
}

static UdpInFlow *udp_in_alloc(void) {
    int oldest = -1;
    for (int i = 0; i < UDP_IN_MAX; i++)
        if (!udp_in_flows[i].active) { memset(&udp_in_flows[i], 0, sizeof(UdpInFlow)); return &udp_in_flows[i]; }
    for (int i = 0; i < UDP_IN_MAX; i++)
        if (oldest < 0 || udp_in_flows[i].last_ms < udp_in_flows[oldest].last_ms) oldest = i;
    memset(&udp_in_flows[oldest], 0, sizeof(UdpInFlow));
    return &udp_in_flows[oldest];
}

/* Guest -> host. The socket is left unconnected, so a server that
   answers from a different source port still reaches us; the reply is
   matched to the flow by the socket it arrives on and not by the peer's
   address. */
static void nat_handle_udp_tx(unsigned short sport, unsigned short dport,
                              unsigned char *dst_ip, unsigned char *payload, int payload_len) {
    /* A datagram addressed to a synthetic port is the guest ANSWERING a
       host client, not opening a flow of its own. Checked first, because
       treating it as outbound would open a host socket toward a port
       nothing is listening on and drop the reply. */
    UdpInFlow *in = udp_in_find_synth(dport);
    if (in) {
        in->last_ms = now_ms_for_timer();
        int sent = sendto(in->sock, (const char*)payload, payload_len, 0,
                          (struct sockaddr*)&in->client, sizeof(in->client));
        if (sent < 0)
            fprintf(stderr, "PORTFWD UDP: reply sendto failed err=%d (guest:%d)\n",
                    WSAGetLastError(), sport);
        return;
    }

    UdpFlow *f = udp_flow_find(sport, dport, dst_ip);
    if (!f) {
        f = udp_flow_alloc();
        f->sock = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
        if (f->sock == INVALID_SOCKET) {
            fprintf(stderr, "NAT UDP: socket failed for guest:%d -> %d\n", sport, dport);
            memset(f, 0, sizeof(UdpFlow));
            return;
        }
        u_long nb = 1;
        ioctlsocket(f->sock, FIONBIO, &nb);
        f->active = 1;
        f->guest_port = sport;
        f->dst_port = dport;
        memcpy(f->dst_ip, dst_ip, 4);
        fprintf(stderr, "NAT UDP: flow guest:%d -> %d.%d.%d.%d:%d\n",
                sport, dst_ip[0], dst_ip[1], dst_ip[2], dst_ip[3], dport);
    }
    f->last_ms = now_ms_for_timer();

    struct sockaddr_in addr;
    nat_host_addr(dst_ip, &addr);
    addr.sin_port = htons(dport);
    int sent = sendto(f->sock, (const char*)payload, payload_len, 0,
                      (struct sockaddr*)&addr, sizeof(addr));
    if (sent < 0)
        fprintf(stderr, "NAT UDP: sendto failed err=%d (guest:%d -> %d)\n",
                WSAGetLastError(), sport, dport);
}

/* Host -> guest. One datagram per poll per flow keeps a chatty peer from
   starving the others and keeps the rx queue bounded. */
static void udp_poll_rx(void) {
    unsigned char guest_ip[4] = {NAT_GUEST_IP0, NAT_GUEST_IP1, NAT_GUEST_IP2, NAT_GUEST_IP3};
    double now = now_ms_for_timer();
    for (int i = 0; i < UDP_MAX_FLOW; i++) {
        UdpFlow *f = &udp_flows[i];
        if (!f->active) continue;
        if (now - f->last_ms > UDP_FLOW_IDLE_MS) { udp_flow_free(f); continue; }
        if (rx_queue_count >= RX_QUEUE_SIZE - 1) continue;
        unsigned char buf[1472];
        struct sockaddr_in from;
        int fromlen = sizeof(from);
        int n = recvfrom(f->sock, (char*)buf, sizeof(buf), 0, (struct sockaddr*)&from, &fromlen);
        if (n <= 0) continue;
        f->last_ms = now;
        /* The datagram is presented as coming from the address the guest
           addressed, not from wherever the host socket heard it. The
           guest asked 10.0.2.2 and must hear 10.0.2.2 back or its own
           demux will not match. */
        nat_build_udp_frame(nat_guest_mac(), f->dst_ip, guest_ip, f->dst_port, f->guest_port, buf, n);
    }
}

/* DHCP, RFC 2131. The guest broadcasts a DISCOVER and gets an OFFER of
   the same 10.0.2.15 this NAT has always assumed, then a REQUEST is
   answered with an ACK carrying mask, router, DNS and a lease.
   Until 2026-07-30 there was no server here at all and the manual said
   there was: a DISCOVER on port 67 went to nat_handle_udp_tx and out to
   a host socket, where nothing answers.

   The reply is a broadcast, to the address the client will have rather
   than to the one it does not have yet. A client without an address
   cannot be reached by unicast IP, and its own filter has to accept the
   frame before its stack sees it. */
static void nat_handle_dhcp(unsigned char *req_frame, unsigned char *msg, int len) {
    if (len < 240) return;                      /* header + magic cookie */
    if (msg[0] != 1) return;                    /* BOOTREQUEST only */
    unsigned char *client_mac = req_frame + 6;

    int msg_type = 0;
    for (int i = 240; i + 1 < len; ) {
        int opt = msg[i];
        if (opt == 255) break;
        if (opt == 0) { i++; continue; }
        int olen = msg[i + 1];
        if (i + 2 + olen > len) break;
        if (opt == 53 && olen >= 1) msg_type = msg[i + 2];
        i += 2 + olen;
    }
    if (msg_type != 1 && msg_type != 3) return; /* DISCOVER or REQUEST */

    unsigned char gw[4]    = {NAT_GW_IP0, NAT_GW_IP1, NAT_GW_IP2, NAT_GW_IP3};
    unsigned char lease[4] = {NAT_GUEST_IP0, NAT_GUEST_IP1, NAT_GUEST_IP2, NAT_GUEST_IP3};
    unsigned char bcast[4] = {255, 255, 255, 255};
    unsigned char dns[4]   = {NAT_GW_IP0, NAT_GW_IP1, NAT_GW_IP2, 3};

    unsigned char reply[576];
    memset(reply, 0, sizeof(reply));
    reply[0] = 2;                               /* BOOTREPLY */
    reply[1] = 1; reply[2] = 6; reply[3] = 0;   /* Ethernet, 6-byte hardware address */
    memcpy(reply + 4, msg + 4, 4);              /* the client's xid, echoed */
    memcpy(reply + 16, lease, 4);               /* yiaddr: the address offered */
    memcpy(reply + 20, gw, 4);                  /* siaddr */
    memcpy(reply + 28, client_mac, 6);          /* chaddr */

    int o = 236;
    reply[o++] = 99; reply[o++] = 130; reply[o++] = 83; reply[o++] = 99;
    reply[o++] = 53; reply[o++] = 1; reply[o++] = (msg_type == 1) ? 2 : 5;  /* OFFER or ACK */
    reply[o++] = 54; reply[o++] = 4; memcpy(reply + o, gw, 4); o += 4;      /* server id */
    reply[o++] = 1;  reply[o++] = 4; reply[o++] = 255; reply[o++] = 255;
                                     reply[o++] = 255; reply[o++] = 0;      /* /24 */
    reply[o++] = 3;  reply[o++] = 4; memcpy(reply + o, gw, 4); o += 4;      /* router */
    reply[o++] = 6;  reply[o++] = 4; memcpy(reply + o, dns, 4); o += 4;     /* DNS */
    reply[o++] = 51; reply[o++] = 4;
    reply[o++] = (unsigned char)((nat_dhcp_lease >> 24) & 0xFF);
    reply[o++] = (unsigned char)((nat_dhcp_lease >> 16) & 0xFF);
    reply[o++] = (unsigned char)((nat_dhcp_lease >> 8) & 0xFF);
    reply[o++] = (unsigned char)(nat_dhcp_lease & 0xFF);
    reply[o++] = 255;                                                       /* end */

    fprintf(stderr, "NAT DHCP: %s from %02x:%02x:%02x:%02x:%02x:%02x -> offering %d.%d.%d.%d\n",
            msg_type == 1 ? "DISCOVER" : "REQUEST",
            client_mac[0], client_mac[1], client_mac[2],
            client_mac[3], client_mac[4], client_mac[5],
            lease[0], lease[1], lease[2], lease[3]);

    nat_build_udp_frame(client_mac, gw, bcast, 67, 68, reply, o);
}

/* Handle a TX frame from the guest */
static void nat_handle_tx(unsigned char *frame, int len) {
    if (len < 14) return;
    unsigned short ethertype = (frame[12] << 8) | frame[13];
    fprintf(stderr, "NAT TX: len=%d ethertype=0x%04x dst=%02x:%02x:%02x:%02x:%02x:%02x hex=",
        len, ethertype, frame[0], frame[1], frame[2], frame[3], frame[4], frame[5]);
    for (int j = 0; j < (len < 20 ? len : 20); j++) fprintf(stderr, "%02x ", frame[j]);
    fprintf(stderr, "\n");

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

    /* THE IP TOTAL LENGTH IS AUTHORITATIVE, NOT THE FRAME LENGTH.
     *
     * A payload length derived from `len` counts every byte the wire
     * happened to carry, and the wire pads: Ethernet has a 60-byte minimum
     * frame, and an NE2000's transmit byte count is a 16-bit DMA count that
     * a driver may round up. Either way the padding lands past the real
     * payload and is delivered to the peer as data.
     *
     * It was invisible for as long as everything here spoke a
     * length-prefixed framed protocol: the reader takes the count it was
     * given and never notices trailing bytes. TLS is a byte STREAM, so a
     * phantom byte after one record is the first byte of the next record
     * header, and the connection desynchronises exactly one record in.
     * Found by openssl, which read `00 17 03 03 00` as a record header and
     * answered `bad record type`; the guest's own record was 127 bytes, an
     * odd number, and arrived as 128.
     *
     * Clamp to what actually arrived so a truncated or lying header cannot
     * walk us off the end of the buffer. */
    int ip_total_len = (ip[2] << 8) | ip[3];
    if (ip_total_len < ip_hdr_len) ip_total_len = ip_hdr_len;
    if (ip_total_len > len - 14) ip_total_len = len - 14;

    if (ip_proto == 6 && len >= 14 + ip_hdr_len + 20) {
        /* TCP */
        unsigned char *tcp = ip + ip_hdr_len;
        unsigned short sport = (tcp[0] << 8) | tcp[1];
        unsigned short dport = (tcp[2] << 8) | tcp[3];
        unsigned long seq = ((unsigned long)tcp[4] << 24) | (tcp[5] << 16) | (tcp[6] << 8) | tcp[7];
        unsigned long ack = ((unsigned long)tcp[8] << 24) | (tcp[9] << 16) | (tcp[10] << 8) | tcp[11];
        int tcp_hdr_len = ((tcp[12] >> 4) & 0xF) * 4;
        int flags = tcp[13];
        int payload_len = ip_total_len - ip_hdr_len - tcp_hdr_len;
        if (payload_len < 0) payload_len = 0;
        unsigned char *payload = tcp + tcp_hdr_len;

        unsigned char guest_ip[4] = {NAT_GUEST_IP0, NAT_GUEST_IP1, NAT_GUEST_IP2, NAT_GUEST_IP3};

        if ((flags & 0x12) == 0x12) {
            /* SYN+ACK from guest -- check if this is a forwarded connection */
            NatConn *c = nat_find(sport, dport, dst_ip);
            if (c && c->forwarded && c->state == 1) {
                portfwd_handle_synack(c, seq, ack);
            }
        }
        else if (flags & 0x02) {
            /* SYN -- new outbound connection */
            fprintf(stderr, "NAT: SYN from guest %d.%d.%d.%d:%d -> %d.%d.%d.%d:%d\n",
                src_ip[0], src_ip[1], src_ip[2], src_ip[3], sport,
                dst_ip[0], dst_ip[1], dst_ip[2], dst_ip[3], dport);
            NatConn *c = nat_find(sport, dport, dst_ip);
            /* A SYN for a connection we already have is a RETRANSMITTED
               SYN, not a new one, and it must not re-enter the branch
               below: that opens a second host socket over c->sock, leaks
               the first handle, and leaves the guest's payload going to a
               socket no listener ever accepted. The guest sees its data
               vanish and the peer sees a reset.
               A guest retransmits when it has not been answered, so the
               two live states want opposite things. state 1 is a connect
               still in flight and the answer is silence: nat_poll_connect
               sends the SYN+ACK the moment the socket is writable, which
               is what a real network makes the guest wait for. state >= 2
               means we already answered and the guest did not hear it, so
               resend that SYN+ACK rather than leaving it to time out.
               Nothing made a guest retransmit a SYN until TCP ran over
               the e1000 model, whose empty receive poll costs a RAM read
               where the NE2000's costs a VM exit, so the guest's
               tick-denominated RTO fires about 540x sooner. */
            if (c && c->active && !c->forwarded) {
                if (c->state >= 2) {
                    nat_build_tcp_frame(nat_guest_mac(), c->dst_ip, guest_ip,
                                        c->dst_port, c->guest_port,
                                        c->ack_offset, c->guest_isn + 1,
                                        0x12, /* SYN+ACK */
                                        NULL, 0);
                }
                return;
            }
            if (!c) c = nat_alloc();
            if (!c) return;
            c->active = 1;
            c->guest_port = sport;
            c->dst_port = dport;
            memcpy(c->dst_ip, dst_ip, 4);
            c->seq_offset = seq;        /* guest sequence space */
            c->ack_offset = 1000000;    /* our sequence space */
            c->guest_isn = seq;
            c->state = 1;
            c->forwarded = 0;

            /* Connect host socket */
            c->sock = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
            if (c->sock == INVALID_SOCKET) { c->active = 0; return; }
            u_long nb = 1;
            ioctlsocket(c->sock, FIONBIO, &nb);
            struct sockaddr_in addr;
            /* Same gateway-is-the-host translation the UDP path uses, so
               a guest reaches a service on this machine at the address
               this NAT tells it the gateway is. */
            nat_host_addr(dst_ip, &addr);
            addr.sin_port = htons(natmap_host_port(dport));
            connect(c->sock, (struct sockaddr*)&addr, sizeof(addr));

            /* The SYN-ACK used to go out here, before the non-blocking
               connect had completed -- so the guest believed it had a
               connection to a socket that might still be resolving, sent
               its first request into it, and lost the request. The
               handshake is finished in nat_poll_connect once the socket
               is actually writable; the guest simply waits, which is
               what a real network makes it do anyway. */
        }
        else if ((flags & 0x01) && !(flags & 0x02)) {
            /* FIN -- guest is done sending. Gracefully half-close the host
               socket so buffered data drains to the peer before the
               connection tears down. Full cleanup at VM exit. */
            NatConn *c = nat_find(sport, dport, dst_ip);
            if (c) {
                nat_build_tcp_frame(nat_guest_mac(), dst_ip, guest_ip,
                                    dport, sport,
                                    ack, seq + 1,
                                    0x11, /* FIN+ACK */
                                    NULL, 0);
                /* Anything still queued belongs to the peer before the
                   send side goes down. One non-blocking send() is not
                   "anything": a socket whose send buffer is full at this
                   moment -- which is exactly when a bulk transfer ends --
                   takes part of it and WOULDBLOCKs on the rest, and the
                   shutdown below then closed the send side over bytes the
                   kernel had never seen. State 3 was flushed nowhere else,
                   so they were freed unsent while both ends reported a
                   clean close. Defer the shutdown, and the free with it,
                   until the buffer is actually empty. */
                nat_tx_flush(c);
                if (c->txlen > 0) {
                    c->shutdown_pending = 1;
                    if (c->state == 4) { c->free_pending = 1; c->closed_at_ms = now_ms_for_timer(); }
                    else { c->state = 3; c->closed_at_ms = now_ms_for_timer(); }
                } else {
                    shutdown(c->sock, SD_SEND);
                    if (c->state == 4) nat_conn_free(c);
                    else { c->state = 3; c->closed_at_ms = now_ms_for_timer(); }
                }
            }
        }
        else if (flags & 0x10) {
            /* ACK (possibly with data) */
            NatConn *c = nat_find(sport, dport, dst_ip);
            if (payload_len > 0) nat_seg_bytes += (unsigned long long)payload_len;
            if (!c && payload_len > 0) nat_seg_noconn += (unsigned long long)payload_len;
            if (c) {
                /* The guest acknowledging our host->guest data: release
                   what it has taken from the retransmit buffer so we stop
                   resending it. A bare ACK (no payload) is the common case
                   here -- the guest ACKing a request we injected -- which
                   is why this sits outside the payload branch. */
                if (c->forwarded) nat_rtx_ack(c, ack);
                if (payload_len > 0 &&
                    !(c->state == 1 || c->state == 2 || c->state == 4))
                    nat_seg_badstate += (unsigned long long)payload_len;
                if (payload_len > 0 &&
                    (c->state == 1 || c->state == 2 || c->state == 4)) {
                    /* Queue, then push what the socket will take. The return
                       value of send() used to be discarded, so a blocked or
                       partial write was reported to the guest as delivered.
                       Data that arrives while the connect is still in flight
                       (state 1) is held rather than sent into a socket that
                       is not connected yet. */
                    nat_tx_queue(c, payload, payload_len);
                    if (c->state != 1) nat_tx_flush(c);
                    /* A half-closed connection the guest is still writing to
                       is not idle, and the reaper must not cut it off
                       mid-send. Activity pushes the deadline out, so what
                       times out is silence rather than duration. */
                    if (c->state == 4) c->closed_at_ms = now_ms_for_timer();
                    /* ACK the data: we have taken responsibility for it. */
                    nat_build_tcp_frame(nat_guest_mac(), dst_ip, guest_ip,
                                        dport, sport,
                                        ack, seq + payload_len,
                                        0x10, /* ACK */
                                        NULL, 0);
                }
            }
        }

        if (flags & 0x04) {
            /* RST */
            NatConn *c = nat_find(sport, dport, dst_ip);
            if (c) nat_conn_free(c);
        }
    }
    else if (ip_proto == 17 && len >= 14 + ip_hdr_len + 8) {
        /* UDP. Port 53 is answered locally by the resolver stub; every
           other port is forwarded to a host socket. Both halves are real
           now -- this branch used to serve DNS and drop the rest. */
        unsigned char *udp = ip + ip_hdr_len;
        unsigned short sport = (udp[0] << 8) | udp[1];
        unsigned short dport = (udp[2] << 8) | udp[3];
        int udp_len = (udp[4] << 8) | udp[5];
        int payload_len = udp_len - 8;
        int avail = len - 14 - ip_hdr_len - 8;
        if (payload_len > avail) payload_len = avail;
        if (payload_len < 0) payload_len = 0;

        if (dport == 67 && payload_len > 0) {
            nat_handle_dhcp(frame, udp + 8, payload_len);
        } else if (dport == 53 && payload_len > 0) {
            nat_handle_dns(udp + 8, payload_len, sport, dst_ip);
        } else {
            nat_handle_udp_tx(sport, dport, dst_ip, udp + 8, payload_len);
        }
    }
}

/* Finish the handshake for outbound connections whose host socket has
   now connected, and push anything the guest sent in the meantime. A
   socket that failed to connect is reset toward the guest rather than
   left to look established forever. */
static void nat_poll_connect(void) {
    unsigned char guest_ip[4] = {NAT_GUEST_IP0, NAT_GUEST_IP1, NAT_GUEST_IP2, NAT_GUEST_IP3};
    for (int i = 0; i < NAT_MAX_CONN; i++) {
        NatConn *c = &nat_conns[i];
        fd_set wr, ex;
        struct timeval tv;
        if (!c->active || c->state != 1 || c->forwarded) continue;

        FD_ZERO(&wr); FD_ZERO(&ex);
        FD_SET(c->sock, &wr); FD_SET(c->sock, &ex);
        tv.tv_sec = 0; tv.tv_usec = 0;
        if (select(0, NULL, &wr, &ex, &tv) <= 0) continue;

        if (FD_ISSET(c->sock, &ex)) {
            nat_build_tcp_frame(nat_guest_mac(), c->dst_ip, guest_ip,
                                c->dst_port, c->guest_port,
                                c->ack_offset, c->seq_offset + 1,
                                0x04, /* RST */
                                NULL, 0);
            fprintf(stderr, "NAT: connect failed for guest port %d\n", c->guest_port);
            nat_conn_free(c);
            continue;
        }
        /* Writable: the connect completed. Now the guest may be told. */
        nat_build_tcp_frame(nat_guest_mac(), c->dst_ip, guest_ip,
                            c->dst_port, c->guest_port,
                            c->ack_offset, c->guest_isn + 1,
                            0x12, /* SYN+ACK */
                            NULL, 0);
        c->state = 2;
        nat_tx_flush(c);
    }
}

/* Poll host sockets for incoming data and build RX frames */
static void nat_poll_rx(void) {
    unsigned char guest_ip[4] = {NAT_GUEST_IP0, NAT_GUEST_IP1, NAT_GUEST_IP2, NAT_GUEST_IP3};
    for (int i = 0; i < NAT_MAX_CONN; i++) {
        NatConn *c = &nat_conns[i];
        if (!c->active) continue;
        /* A half-closed connection whose other side never spoke again is
           reaped here. Without this the slot is held for the life of the
           VM by a peer that has simply gone away, which is the ordinary
           case for a client that disconnects after one request. */
        if ((c->state == 3 || c->state == 4) &&
            now_ms_for_timer() - c->closed_at_ms > NAT_HALF_CLOSED_TIMEOUT_MS) {
            if (c->txlen > 0) nat_freed_reap += (unsigned long long)c->txlen;
            nat_conn_free(c);
            continue;
        }
        /* Retry anything the socket would not take last time. State 3 is
           in this list because the guest's FIN does not entitle us to
           discard what it sent before it: the send side stays open until
           the buffer is empty, and only then does the shutdown the FIN
           handler deferred actually happen. */
        if (c->state == 2 || c->state == 3 || c->state == 4) {
            int before = c->txlen;
            nat_tx_flush(c);
            /* Bytes moving is not silence, and the reaper above only means
               to collect silence. Without this a half-closed connection
               feeding a slow reader is reaped mid-drain and the rest of
               the guest's output is freed. */
            if (c->txlen < before) c->closed_at_ms = now_ms_for_timer();
        }
        if (c->shutdown_pending && c->txlen <= 0) {
            shutdown(c->sock, SD_SEND);
            c->shutdown_pending = 0;
            if (c->free_pending) { nat_conn_free(c); continue; }
        }
        /* State 3 is the guest having sent FIN. It can still RECEIVE, and
           it is also the state that used to be skipped here -- which is
           why such a connection was never read again, never closed, and
           never gave its slot back. State 4 means the host already sent
           its FIN, so there is nothing left to read. */
        if (c->state != 2 && c->state != 3) continue;
        if (rx_queue_count >= RX_QUEUE_SIZE - 1) { continue; }
        unsigned char buf[1400];
        int n = recv(c->sock, (char*)buf, sizeof(buf), 0);
        /* Forwarded connections are the ones whose delivery has been in
           question, so say plainly what recv answered on them rather than
           leaving it to be inferred from which frames appeared. Bounded so
           a busy run does not drown in it. */
        if (c->forwarded) {
            static int fwd_rx_dbg = 0;
            if (fwd_rx_dbg++ < 40)
                fprintf(stderr, "PORTFWD recv: n=%d err=%d state=%d gport=%d dport=%d\n",
                        n, (n < 0 ? WSAGetLastError() : 0), c->state, c->guest_port, c->dst_port);
        }
        if (n < 0) {
            int werr = WSAGetLastError();
            static int neg_count = 0;
            if (neg_count++ < 5) fprintf(stderr, "NAT recv: n=%d err=%d state=%d sock=%lld\n", n, werr, c->state, (long long)c->sock);
            /* WSAEWOULDBLOCK is the ordinary "nothing to read right now" and
               must not close anything. ANY OTHER error means the socket is
               dead -- overwhelmingly WSAECONNRESET from a host client that
               gave up waiting.

               Until this existed, that case was printed and then ignored: the
               connection stayed active in the table, was polled forever, and
               never gave back its slot or its guest port. On a port-forward
               that wedges the whole forward, because a later host client is
               accepted onto a forward whose guest side is still owned by the
               dead connection and is therefore never read. Measured on the 6.2
               registry harness: six host clients accepted, exactly ONE ever
               serviced, and 23 consecutive polls of the reset socket. That is
               the "the first request works and the second never does" symptom
               that has been blamed on the guest for a long time.

               FIN+ACK rather than RST deliberately: it is the same teardown
               the n==0 path uses, so it travels a route the guest's TCP is
               known to handle, and the goal here is to release the slot rather
               than to model reset semantics faithfully. */
            if (werr != WSAEWOULDBLOCK) {
                nat_build_tcp_frame(nat_guest_mac(), c->dst_ip, guest_ip,
                                    c->dst_port, c->guest_port,
                                    c->ack_offset + 1, c->seq_offset + 1,
                                    0x11, /* FIN+ACK */
                                    NULL, 0);
                nat_conn_free(c);
                continue;
            }
        }
        if (n > 0) {
            static int nat_rx_total = 0;
            nat_rx_total += n;
            if (nat_rx_total % 100000 < n) fprintf(stderr, "NAT RX: total=%d chunk=%d q=%d\n", nat_rx_total, n, rx_queue_count);
            c->ack_offset++;
            unsigned long data_seq = c->ack_offset;
            nat_build_tcp_frame(nat_guest_mac(), c->dst_ip, guest_ip,
                                c->dst_port, c->guest_port,
                                data_seq, c->seq_offset + 1,
                                0x10, /* ACK with data */
                                buf, n);
            c->ack_offset += n - 1;
            /* Hold it for retransmission until the guest ACKs. */
            if (c->forwarded) {
                nat_rtx_append(c, data_seq, buf, n);
                c->last_tx_ms = now_ms_for_timer();
                c->rtx_count = 0;
            }
        } else if (n == 0) {
            if (c->state == 3) {
                /* Both sides have now closed: the guest sent its FIN
                   earlier and the peer has just sent its own. This is the
                   ordinary end of a connection and the point at which the
                   slot and the socket go back. */
                nat_build_tcp_frame(nat_guest_mac(), c->dst_ip, guest_ip,
                                    c->dst_port, c->guest_port,
                                    c->ack_offset + 1, c->seq_offset + 1,
                                    0x11, /* FIN+ACK */
                                    NULL, 0);
                nat_conn_free(c);
            } else {
                /* Remote half-closed (sent FIN). Deliver FIN to guest.
                   Move to state 4: stop reading but keep forwarding
                   guest TX data back to the host socket. */
                nat_build_tcp_frame(nat_guest_mac(), c->dst_ip, guest_ip,
                                    c->dst_port, c->guest_port,
                                    c->ack_offset + 1, c->seq_offset + 1,
                                    0x11, /* FIN+ACK */
                                    NULL, 0);
                c->state = 4;
                c->closed_at_ms = now_ms_for_timer();
            }
        }
    }
}

/* Retransmit un-ACKed host->guest SYN and data for forwarded connections.
   codex-vm is the sender in this direction, so a frame the guest was not
   ready for -- a SYN arriving while a single-threaded server is busy on
   another connection, or data injected before the guest is in a recv loop
   -- is simply gone unless it is resent. This is the guest-facing half of
   proper TCP; the host-facing half (retrying send() on WOULDBLOCK) already
   lives in nat_tx_flush. A duplicate the guest already has is dropped by
   its receive-window check and re-ACKed, which trims the buffer here, so
   this converges rather than looping. */
static void nat_poll_retransmit(void) {
    unsigned char guest_ip[4] = {NAT_GUEST_IP0, NAT_GUEST_IP1, NAT_GUEST_IP2, NAT_GUEST_IP3};
    unsigned char gw_ip[4] = {NAT_GW_IP0, NAT_GW_IP1, NAT_GW_IP2, NAT_GW_IP3};
    double now = now_ms_for_timer();
    for (int i = 0; i < NAT_MAX_CONN; i++) {
        NatConn *c = &nat_conns[i];
        if (!c->active || !c->forwarded) continue;

        if (c->state == 1) {
            /* A host client that connected and then gave up before the
               guest accepted leaves a socket closed with nothing buffered.
               Free it so its SYN stops being resent into the guest's accept
               loop -- an unbounded resend of dead SYNs was starving that
               loop. MSG_PEEK does not consume: 0 is an orderly close with no
               data, -1 (WOULDBLOCK) is open-but-quiet, >0 is a request still
               waiting to be delivered, so only the first frees. */
            char pk;
            int pr = recv(c->sock, &pk, 1, MSG_PEEK);
            if (pr == 0) { nat_conn_free(c); continue; }
        }
        if (rx_queue_count >= RX_QUEUE_SIZE - 1) continue;

        if (c->state == 1) {
            /* SYN not yet answered by the guest's SYN-ACK. Retransmit with
               exponential backoff (~200ms, doubling, capped), the pacing a
               real stack uses -- a flat 50ms floods a busy single-threaded
               server with hundreds of duplicate SYNs. */
            int shift = c->rtx_count < 4 ? c->rtx_count : 4;
            double rto = 200.0 * (double)(1 << shift);
            if (now - c->last_tx_ms < rto) continue;
            if (c->rtx_count++ >= NAT_MAX_RTX) { nat_conn_free(c); continue; }
            nat_build_tcp_frame(nat_guest_mac(), gw_ip, guest_ip,
                                c->dst_port, c->guest_port,
                                c->ack_offset, 0,
                                0x02, /* SYN */
                                NULL, 0);
            c->last_tx_ms = now;
        } else if (c->state == 2 && c->rtxlen > 0) {
            /* Un-ACKed data, resent from the first un-ACKed byte. Data on an
               established connection is delivered fast, so no backoff. */
            if (now - c->last_tx_ms < NAT_RTO_MS) continue;
            if (c->rtx_count++ >= NAT_MAX_RTX) { nat_conn_free(c); continue; }
            int n = c->rtxlen > 1400 ? 1400 : c->rtxlen;
            nat_build_tcp_frame(nat_guest_mac(), c->dst_ip, guest_ip,
                                c->dst_port, c->guest_port,
                                c->rtx_base, c->seq_offset + 1,
                                0x10, /* ACK with data */
                                c->rtxbuf, n);
            c->last_tx_ms = now;
        }
    }
}

/* Set up port forwarding listeners */
static void portfwd_init(void) {
    for (int i = 0; i < portfwd_count; i++) {
        PortFwd *pf = &portfwds[i];
        pf->listen_sock = (pf->proto == PF_UDP)
            ? socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP)
            : socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
        if (pf->listen_sock == INVALID_SOCKET) {
            fprintf(stderr, "portfwd: socket failed for host:%d\n", pf->host_port);
            pf->active = 0; continue;
        }
        int reuse = 1;
        setsockopt(pf->listen_sock, SOL_SOCKET, SO_REUSEADDR, (char*)&reuse, sizeof(reuse));
        struct sockaddr_in addr;
        memset(&addr, 0, sizeof(addr));
        addr.sin_family = AF_INET;
        addr.sin_addr.s_addr = INADDR_ANY;
        addr.sin_port = htons(pf->host_port);
        if (bind(pf->listen_sock, (struct sockaddr*)&addr, sizeof(addr)) < 0) {
            fprintf(stderr, "portfwd: bind failed for host:%d\n", pf->host_port);
            closesocket(pf->listen_sock); pf->active = 0; continue;
        }
        if (pf->proto == PF_TCP) listen(pf->listen_sock, 4);
        u_long nb = 1;
        ioctlsocket(pf->listen_sock, FIONBIO, &nb);
        pf->active = 1;
        fprintf(stderr, "portfwd: %s host:%d -> guest:%d\n",
                pf->proto == PF_UDP ? "udp" : "tcp", pf->host_port, pf->guest_port);
    }
}

/* Poll port forwarding listeners for new inbound connections.
   When a host client connects, allocate a NatConn, accept the socket,
   and inject a SYN frame to the guest so its TCP stack sees an
   incoming connection. */
static void portfwd_poll(void) {
    unsigned char guest_ip[4] = {NAT_GUEST_IP0, NAT_GUEST_IP1, NAT_GUEST_IP2, NAT_GUEST_IP3};
    unsigned char gw_ip[4] = {NAT_GW_IP0, NAT_GW_IP1, NAT_GW_IP2, NAT_GW_IP3};
    for (int i = 0; i < portfwd_count; i++) {
        PortFwd *pf = &portfwds[i];
        if (!pf->active) continue;
        struct sockaddr_in client_addr;
        int addrlen = sizeof(client_addr);

        if (pf->proto == PF_UDP) {
            /* No accept and no handshake: a datagram arrives and is put in
               front of the guest as-is, from a synthetic gateway port that
               names the client it came from. */
            if (rx_queue_count >= RX_QUEUE_SIZE - 1) continue;
            unsigned char buf[1472];
            int n = recvfrom(pf->listen_sock, (char*)buf, sizeof(buf), 0,
                             (struct sockaddr*)&client_addr, &addrlen);
            if (n <= 0) continue;
            UdpInFlow *f = udp_in_find_client(&client_addr, pf->guest_port);
            if (!f) {
                f = udp_in_alloc();
                f->active = 1;
                f->sock = pf->listen_sock;
                f->client = client_addr;
                f->guest_port = pf->guest_port;
                f->synth_port = udp_in_next_port++;
                if (udp_in_next_port < UDP_IN_PORT_BASE) udp_in_next_port = UDP_IN_PORT_BASE;
                fprintf(stderr, "portfwd udp: host client -> guest:%d as gw:%d\n",
                        pf->guest_port, f->synth_port);
            }
            f->last_ms = now_ms_for_timer();
            nat_build_udp_frame(nat_guest_mac(), gw_ip, guest_ip, f->synth_port, pf->guest_port, buf, n);
            continue;
        }

        SOCKET client = accept(pf->listen_sock, (struct sockaddr*)&client_addr, &addrlen);
        if (client == INVALID_SOCKET) continue;
        u_long nb = 1;
        ioctlsocket(client, FIONBIO, &nb);
        NatConn *c = nat_alloc();
        if (!c) { closesocket(client); continue; }
        c->active = 1;
        c->sock = client;
        c->guest_port = pf->guest_port;
        c->dst_port = ntohs(client_addr.sin_port);
        memcpy(c->dst_ip, gw_ip, 4);
        /* ack_offset is OUR sequence space -- see the NatConn comment.
           These two were assigned the other way round, which put every
           later data frame's sequence number in the guest's space. */
        c->ack_offset = 100000;
        c->seq_offset = 0;
        c->state = 1;
        c->forwarded = 1;
        c->guest_ack = 0;
        fprintf(stderr, "portfwd: accepted host client on :%d -> guest:%d\n",
            pf->host_port, pf->guest_port);
        nat_build_tcp_frame(nat_guest_mac(), gw_ip, guest_ip,
                            c->dst_port, pf->guest_port,
                            c->ack_offset, 0,
                            0x02, /* SYN */
                            NULL, 0);
        c->last_tx_ms = now_ms_for_timer();
        c->rtx_count = 0;
    }
}

/* Handle SYN-ACK from guest for a forwarded connection */
static void portfwd_handle_synack(NatConn *c, unsigned long seq, unsigned long ack) {
    unsigned char guest_ip[4] = {NAT_GUEST_IP0, NAT_GUEST_IP1, NAT_GUEST_IP2, NAT_GUEST_IP3};
    unsigned char gw_ip[4] = {NAT_GW_IP0, NAT_GW_IP1, NAT_GW_IP2, NAT_GW_IP3};
    /* seq is the guest's own SYN-ACK sequence, so it belongs in
       seq_offset; ack is what the guest acknowledged of ours. Written the
       other way round, the very next frame nat_poll_rx built for this
       connection swapped both fields. */
    c->seq_offset = seq;
    c->ack_offset = ack;
    c->state = 2;
    c->guest_ack = seq + 1;
    nat_build_tcp_frame(nat_guest_mac(), gw_ip, guest_ip,
                        c->dst_port, c->guest_port,
                        c->ack_offset, c->seq_offset + 1,
                        0x10, /* ACK */
                        NULL, 0);
    fprintf(stderr, "portfwd: connection established (guest port %d)\n", c->guest_port);
}

/* Inject queued RX frames into the NE2000 ring buffer */
static void ne2k_inject_rx(void) {
    static int inject_debug = 0;
    /* One NAT, one wire. The guest's stack brings up the NE2000 whether or
       not it binds the Intel part, so with both cards draining the same
       queue the NE2000 takes every frame and the e1000 receives nothing --
       which is what -e1000-nat looked like before this line existed. */
    if (e1000_nat) return;
    if (rx_queue_count > 0 && inject_debug < 5) {
        fprintf(stderr, "inject_rx: q=%d pstart=%d pstop=%d curr=%d bnry=%d\n",
            rx_queue_count, ne2k.pstart, ne2k.pstop, ne2k.curr, ne2k.bnry);
        inject_debug++;
    }
    while (rx_queue_count > 0) {
        RxFrame *f = &rx_queue[rx_queue_head];
        /* Pad odd-length frames to even: the guest uses REP INSW (word DMA)
           which reads frame_len/2 words, truncating the last byte of odd frames.
           The ip-payload fix uses ip-total-length to ignore the padding byte. */
        int frame_len = f->len;
        if (frame_len & 1) frame_len++;
        int total = frame_len + 4; /* 4-byte NE2000 header */
        int pages_needed = (total + 255) / 256;
        int next_page = ne2k.curr + pages_needed;
        if (next_page >= ne2k.pstop) next_page = ne2k.pstart + (next_page - ne2k.pstop);

        /* Check for ring buffer full: would the new frame overlap BNRY?
           In a circular ring, "full" means CURR+pages would reach or pass BNRY. */
        {
            int ring_pages = ne2k.pstop - ne2k.pstart;
            /* Guest has not programmed PSTART/PSTOP yet: dropping the frame is
               correct, and it avoids a %0 that crashes the whole VM (a host client
               hitting a forwarded port at boot could trigger it). */
            if (ring_pages <= 0) break;
            int used = (ne2k.curr - ne2k.bnry + ring_pages) % ring_pages;
            int avail = ring_pages - used - 1;
            if (pages_needed > avail) break;
        }

        int addr = ne2k.curr * 256;
        int ring_end = ne2k.pstop * 256;
        int ring_start = ne2k.pstart * 256;

        /* NE2000 RX header: status, next_page, length_lo, length_hi */
        int ne2k_len = total;
        unsigned char hdr[4] = { 0x01, next_page & 0xFF, ne2k_len & 0xFF, (ne2k_len >> 8) & 0xFF };
        /* Write header + data, wrapping at ring boundary */
        unsigned char src_all[1600];
        memset(src_all, 0, sizeof(src_all));
        memcpy(src_all, hdr, 4);
        memcpy(src_all + 4, f->data, f->len > 1596 ? 1596 : f->len);
        int first = ring_end - addr;
        if (first >= total) {
            memcpy(ne2k.mem + addr, src_all, total);
        } else {
            memcpy(ne2k.mem + addr, src_all, first);
            memcpy(ne2k.mem + ring_start, src_all + first, total - first);
        }

        ne2k.curr = next_page;
        ne2k.isr |= 0x01; /* PRX -- packet received */

        rx_queue_head = (rx_queue_head + 1) % RX_QUEUE_SIZE;
        rx_queue_count--;
    }
}


/* The e1000 counterpart to ne2k_inject_rx: drain the same NAT queue into
   the receive ring the way e1000_deliver_rx places the canned frame. The
   canned replay is left alone -- it is what e1000-bringup asserts against
   -- so the two share only the ring cursor.

   The station filter is the part that makes this a test rather than a
   conduit. Real silicon drops a frame addressed to an address it was not
   programmed with, and that is precisely the failure a stack sourcing the
   wrong MAC produces on metal: it transmits fine and never hears back.
   A model that delivered to any address could not tell the two apart. */
static void e1000_nat_rx(void) {
    unsigned long long ring = e1000_ring_base(E1000_REG_RDBAL, E1000_REG_RDBAH);
    unsigned int len = e1000_regs[E1000_REG_RDLEN / 4];
    if (!ring || !len) return;
    if (!(e1000_regs[E1000_REG_RCTL / 4] & E1000_RCTL_EN)) return;
    /* This path had NO stall gate at all until 2026-08-21, so a MAC that
       e1000_deliver_rx correctly refused to deliver through still received
       everything the moment the NAT was the source. -e1000-nat -i219 is the
       combination sitting 9's arm is written on, so the divergence sat
       exactly under the arm being built. Counted and dropped rather than
       left queued: the MAC took the frame, and it is not coming back. */
    if (e1000_ring_poisoned || i219_mac_stalled() || e1000_dma_blocked()) {
        while (rx_queue_count > 0) {
            e1000_regs[E1000_REG_GPRC / 4]++;
            rx_queue_head = (rx_queue_head + 1) % RX_QUEUE_SIZE;
            rx_queue_count--;
        }
        return;
    }
    unsigned int count = len / 16;
    if (!count) return;
    while (rx_queue_count > 0) {
        RxFrame *f = &rx_queue[rx_queue_head];
        if (f->len >= 6) {
            int bcast = f->data[0] == 0xFF && f->data[1] == 0xFF && f->data[2] == 0xFF &&
                        f->data[3] == 0xFF && f->data[4] == 0xFF && f->data[5] == 0xFF;
            int mine  = !e1000_fault_no_mac && !memcmp(f->data, e1000_station_mac, 6);
            /* RCTL.UPE. Our own driver sets it at bring-up, deliberately and
               with its reasons written down, so on this bed the filter is
               open unless -e1000-strict-filter refuses to honour it. Without
               that knob the filter below is a branch nothing can reach. */
            int promisc = !e1000_strict_filter && (e1000_regs[E1000_REG_RCTL / 4] & 0x08u) != 0;
            if (!bcast && !mine && !promisc) {
                if (e1000_rx_dropped < 5)
                    fprintf(stderr, "e1000 rx FILTERED: dst=%02x:%02x:%02x:%02x:%02x:%02x "
                            "station=%02x:%02x:%02x:%02x:%02x:%02x\n",
                            f->data[0], f->data[1], f->data[2], f->data[3], f->data[4], f->data[5],
                            e1000_station_mac[0], e1000_station_mac[1], e1000_station_mac[2],
                            e1000_station_mac[3], e1000_station_mac[4], e1000_station_mac[5]);
                e1000_rx_dropped++;
                rx_queue_head = (rx_queue_head + 1) % RX_QUEUE_SIZE;
                rx_queue_count--;
                continue;
            }
        }
        unsigned int idx = (unsigned int)e1000_rx_cursor % count;
        if (idx == e1000_regs[E1000_REG_RDT / 4]) {   /* ring full: leave it queued */
            e1000_regs[E1000_REG_RNBC / 4]++;
            return;
        }
        unsigned long long desc = ring + (unsigned long long)idx * 16;
        if (desc + 16 > guest_mem_size) return;
        unsigned char *d = (unsigned char *)guest_mem + desc;
        unsigned long long buf = *(unsigned long long *)d;
        if (!buf || buf + (unsigned long long)f->len > guest_mem_size) return;
        memcpy((unsigned char *)guest_mem + buf, f->data, (size_t)f->len);
        *(unsigned short *)(d + 8) = (unsigned short)f->len;
        d[12] = E1000_RXD_STAT_DD | E1000_RXD_STAT_EOP;
        e1000_rx_cursor = (int)((idx + 1) % count);
        e1000_regs[E1000_REG_RDH / 4] = (idx + 1) % count;
        e1000_regs[E1000_REG_GPRC / 4]++;
        rx_queue_head = (rx_queue_head + 1) % RX_QUEUE_SIZE;
        rx_queue_count--;
    }
}

/* VGA Attribute Controller -- minimal emulation for port 0x3C0/0x3C1 */
static int vga_attr_index = 0;    /* current attribute register index */
static int vga_attr_flipflop = 0; /* 0=next write is index, 1=next write is data */

/* ide declared above (forward decl for UEFI emulation) */
static PicState pic_master, pic_slave;

/* The guest listens to PS/2 when it has programmed the PIC and left the
   keyboard IRQ unmasked -- the same test the main loop uses to decide
   whether to write injected scancodes straight to cell 28680. */
static int ps2_irq_route_live(void) {
    return pic_master.vector_base && !(pic_master.mask & (1 << 1));
}
static int debug_exit_code = -1;
static int no_timer = 0;  /* set via CODEX_VM_NO_TIMER=1 to suppress timer IRQ */
static const char *input_file = NULL, *output_file = NULL;

/* PIT state */
static int pit_vector = 32;
static LARGE_INTEGER perf_freq;
static LARGE_INTEGER last_tick;

/* Watchpoint (page-protection based) */
static unsigned long long watch_addr = 0;
static int watch_size = 8;
static unsigned char watch_prev[64];
static int watch_active = 0;
static unsigned long long watch_val = 0;   /* -watch-val: only report writes of this value */
static int watch_val_set = 0;
static int r10dump = 0;   /* -r10dump: print guest R10 at each serial-output newline */
static unsigned long long dumpmem_addr = 0, dumpmem_len = 0;  /* -dumpmem: hexdump at exit */

/* Guest-armable watchpoint: the guest computes a runtime heap address and arms
 * the synchronous page-watch itself via I/O ports 0x411 (addr lo32), 0x412
 * (addr hi32), 0x413 (arm: assemble addr, watch_size=64, report all writes).
 * This catches a corruption at a runtime-determined address on the demand seed
 * without the interactive debugger, which is ~1000x too slow under demand
 * (it intercepts every #PF). See handle_io ports 0x411-0x413. */
static unsigned int guest_watch_lo = 0, guest_watch_hi = 0;
static int watch_report_all = 0;  /* guest-arm: log every changed 8B slot + writer RIP, continue */

/* Hardware watchpoint via debug registers (DR0/DR7). Traps the exact
 * linear address on access regardless of guest paging, so it does not
 * collide with the guest's own demand-paging #PF handler the way the
 * page-protection watch's host memory-access exits can. */
static unsigned long long hw_watch_addr = 0;
static int hw_watch_active = 0;
static int hw_watch_rw = 1;    /* DR7 R/W0: 1=write(01b), 3=read/write(11b) */
static int hw_watch_len = 8;   /* 1, 2, 4, or 8 bytes */
static int hw_watch_hits = 0;
static int hw_watch_log = 0;   /* one line per write and continue, no crash report */
static void hw_watch_init(void);

static void die(const char *msg) { fprintf(stderr, "FATAL: %s\n", msg); exit(1); }

static void watch_init(void);

/* ── Interactive Debugger ─────────────────────────────────────────── */

static int debug_mode = 0;
static const char *map_file_path = NULL;
static const char *g_kernel_path = NULL;
#define MAX_INIT_BREAKS 8
static const char *init_break_names[MAX_INIT_BREAKS];
static int init_break_count = 0;

#define MAX_SYMBOLS 8192
#define MAX_BREAKPOINTS 64
static struct { unsigned long long addr; int size; char name[128]; } symbols[MAX_SYMBOLS];
static int symbol_count = 0;

static struct {
    unsigned long long addr;
    unsigned char orig_byte;
    int active;
    /* conditional: if cond_reg >= 0, only break when reg == cond_val */
    int cond_reg; /* -1 = unconditional, 0=rax..17=cr2 per dump order */
    unsigned long long cond_val;
} breakpoints[MAX_BREAKPOINTS];
static int bp_count = 0;

/* WCET observation: -wcet <fn> arms a hardware execution breakpoint
   (DR0-DR3) at the function entry, single-steps each invocation to its
   return via TF, and counts only the instructions whose RIP lies inside
   the function's own [start,end) range (callee instructions are
   excluded -- matching the compiler's per-body CDX6010 static count).
   Reports WCET-OBS lines on exit. Pure observation: no guest byte is
   ever written (INT3 cannot be used here -- the guest IDT owns vector 3
   and its handler dumps !EXC and halts; #DB is host-intercepted).
   DR6.B0-B3 marks an entry hit, DR6.BS a step; RF resumes past the
   entry fault. Four functions maximum per run (one debug register
   each) -- batch across runs for more. */
#define MAX_WCET_FNS 4
static struct {
    unsigned long long start, end;
    char name[128];
    unsigned long long max_count, calls;
} wcet_fns[MAX_WCET_FNS];
static int wcet_fn_count = 0;
static const char *wcet_names[MAX_WCET_FNS];
static int wcet_name_count = 0;
static int wcet_active = -1;
static unsigned long long wcet_cur = 0, wcet_prev_rip = 0;
static unsigned long long wcet_ret_addr = 0, wcet_entry_rsp = 0;

/* Conditional execution breakpoint through the debug registers.
   -hbreak <fn>[:<reg>=<val>] arms DRn at the function entry and evaluates
   the condition at the host's #DB exit, on the guest's own register file.

   This exists because a conditional breakpoint built on INT3 cannot work in
   this guest, however it is spelled. Vector 3 belongs to the guest: its
   handler runs before the host ever sees the trap, so by the time a
   condition is evaluated the registers are the HANDLER's (rdi reads 0x33
   every time) and not the callee's. Measured 2026-07-27 with a condition
   that can never match, which broke anyway. A #DB from DRn is intercepted
   by the host through ExceptionExitBitmap and never reaches the guest IDT,
   so the registers at the exit are the ones the call was made with.

   No guest byte is written, so this also does not collide with -break, and
   an unmatched hit resumes the guest instead of halting it -- which is what
   lets it reach the Nth call of a helper that is called from everywhere.
   Four functions maximum, one debug register each, shared with -wcet. */
#define MAX_HBREAKS 4
static struct {
    unsigned long long addr;
    char name[128];
    int cond_reg;              /* -1 = unconditional */
    unsigned long long cond_val;
    unsigned long long hits;   /* entries seen */
    unsigned long long matched;/* entries whose condition held */
} hbreaks[MAX_HBREAKS];
static int hbreak_count = 0;
static const char *hbreak_specs[MAX_HBREAKS];
static int hbreak_spec_count = 0;

static void load_map_file(const char *path) {
    FILE *f = fopen(path, "r");
    if (!f) return;
    char line[256];
    while (fgets(line, sizeof(line), f) && symbol_count < MAX_SYMBOLS) {
        if (line[0] == '#' || line[0] == '\n') continue;
        unsigned long long a; int sz; char nm[128];
        if (sscanf(line, "0x%llx %d %127[^\n]", &a, &sz, nm) == 3) {
            symbols[symbol_count].addr = a;
            symbols[symbol_count].size = sz;
            strncpy(symbols[symbol_count].name, nm, 127);
            symbols[symbol_count].name[127] = 0;
            symbol_count++;
        }
    }
    fclose(f);
    fprintf(stderr, "DBG: loaded %d symbols from %s\n", symbol_count, path);
}

static const char *sym_lookup(unsigned long long addr, int *offset_out) {
    for (int i = 0; i < symbol_count; i++) {
        if (addr >= symbols[i].addr && addr < symbols[i].addr + (unsigned long long)symbols[i].size) {
            if (offset_out) *offset_out = (int)(addr - symbols[i].addr);
            return symbols[i].name;
        }
    }
    if (offset_out) *offset_out = 0;
    return NULL;
}

static unsigned long long sym_find(const char *name) {
    for (int i = 0; i < symbol_count; i++)
        if (!strcmp(symbols[i].name, name)) return symbols[i].addr;
    return 0;
}

static void dbg_print_addr(unsigned long long addr) {
    int off;
    const char *name = sym_lookup(addr, &off);
    if (name) fprintf(stderr, "0x%llx <%s+%d>", addr, name, off);
    else fprintf(stderr, "0x%llx", addr);
}

static void dbg_dump_regs(void) {
    WHV_REGISTER_NAME names[] = {
        WHvX64RegisterRip, WHvX64RegisterRsp, WHvX64RegisterRax,
        WHvX64RegisterRbx, WHvX64RegisterRcx, WHvX64RegisterRdx,
        WHvX64RegisterRsi, WHvX64RegisterRdi, WHvX64RegisterRbp,
        WHvX64RegisterR8, WHvX64RegisterR9, WHvX64RegisterR10,
        WHvX64RegisterR11, WHvX64RegisterR12, WHvX64RegisterR13,
        WHvX64RegisterR14, WHvX64RegisterR15, WHvX64RegisterRflags
    };
    WHV_REGISTER_VALUE vals[18];
    WHvGetVirtualProcessorRegisters(partition, 0, names, 18, vals);
    fprintf(stderr, "  RIP="); dbg_print_addr(vals[0].Reg64); fprintf(stderr, "\n");
    fprintf(stderr, "  RSP=%016llx RBP=%016llx RFLAGS=%016llx\n", vals[1].Reg64, vals[8].Reg64, vals[17].Reg64);
    fprintf(stderr, "  RAX=%016llx RBX=%016llx RCX=%016llx RDX=%016llx\n",
        vals[2].Reg64, vals[3].Reg64, vals[4].Reg64, vals[5].Reg64);
    fprintf(stderr, "  RSI=%016llx RDI=%016llx\n", vals[6].Reg64, vals[7].Reg64);
    fprintf(stderr, "  R8 =%016llx R9 =%016llx R10=%016llx R11=%016llx\n",
        vals[9].Reg64, vals[10].Reg64, vals[11].Reg64, vals[12].Reg64);
    fprintf(stderr, "  R12=%016llx R13=%016llx R14=%016llx R15=%016llx\n",
        vals[13].Reg64, vals[14].Reg64, vals[15].Reg64, vals[16].Reg64);
}

/* guest_mem is MEM_RESERVE with lazy commit (see guest_commit_range), and a
   crash report is exactly where the addresses of interest are ones the guest
   never successfully touched: a demand-commit that failed reports through
   here with the uncommitted GPA as its fault address, and an RBP chain or a
   wild RSP ends in a page nobody has been to. A host read of a reserved page
   is an access violation that takes the report and the process with it.
   Measured 2026-08-18: the brotli-interop HOST CRASH of 2026-08-16 was
   dbg_dump_mem reading the page it was about to describe, and with that
   guarded the same run died again in dbg_backtrace one frame past __start.
   Every host read the reporter makes asks this first. */
static int guest_page_committed(unsigned long long gpa) {
    MEMORY_BASIC_INFORMATION mbi;
    if (gpa >= guest_mem_size) return 0;
    if (VirtualQuery((unsigned char *)guest_mem + gpa, &mbi, sizeof(mbi)) != sizeof(mbi)) return 0;
    return mbi.State == MEM_COMMIT;
}

static void dbg_dump_mem(unsigned long long addr, int len) {
    if (addr + (unsigned long long)len > guest_mem_size) {
        fprintf(stderr, "  address out of range\n"); return;
    }
    unsigned char *p = (unsigned char *)guest_mem + addr;
    if (!guest_page_committed(addr) || !guest_page_committed(addr + (unsigned long long)len - 1)) {
        fprintf(stderr, "  (host page not committed: the guest never touched this range)\n");
        return;
    }
    for (int i = 0; i < len; i += 16) {
        fprintf(stderr, "  %012llx: ", addr + i);
        for (int j = 0; j < 16 && i+j < len; j++)
            fprintf(stderr, "%02x ", p[i+j]);
        fprintf(stderr, " ");
        for (int j = 0; j < 16 && i+j < len; j++) {
            unsigned char c = p[i+j];
            fprintf(stderr, "%c", (c >= 0x20 && c < 0x7f) ? c : '.');
        }
        fprintf(stderr, "\n");
    }
}

static void dbg_dump_stack(int count) {
    WHV_REGISTER_NAME names[2] = { WHvX64RegisterRsp, WHvX64RegisterRbp };
    WHV_REGISTER_VALUE vals[2];
    WHvGetVirtualProcessorRegisters(partition, 0, names, 2, vals);
    unsigned long long rsp = vals[0].Reg64;
    fprintf(stderr, "  Stack at RSP=0x%llx:\n", rsp);
    /* Bound by subtraction, not addition. `rsp + i*8 + 8 <= guest_mem_size`
       WRAPS for a wild RSP -- which is exactly the case a crash dump exists to
       report -- so the check passed and the read landed far outside the guest
       allocation, killing codex-vm with a host access violation in the middle
       of printing the diagnosis. The dump stopped after the backtrace and the
       process died, so the fault that mattered was never shown. A reporter
       that crashes on the inputs it exists for is worse than no reporter. */
    if (rsp >= guest_mem_size) { fprintf(stderr, "  (RSP outside guest memory)\n"); return; }
    for (int i = 0; i < count && (guest_mem_size - rsp) >= (unsigned long long)i*8 + 8; i++) {
        if (!guest_page_committed(rsp + i*8 + 7)) {
            fprintf(stderr, "  [RSP+%02x] (host page not committed: the guest never touched this range)\n", i*8);
            break;
        }
        unsigned long long val = *(unsigned long long *)((unsigned char *)guest_mem + rsp + i*8);
        fprintf(stderr, "  [RSP+%02x] = ", i*8);
        dbg_print_addr(val);
        fprintf(stderr, "\n");
    }
}

static void dbg_backtrace(void) {
    WHV_REGISTER_NAME names[2] = { WHvX64RegisterRip, WHvX64RegisterRbp };
    WHV_REGISTER_VALUE vals[2];
    WHvGetVirtualProcessorRegisters(partition, 0, names, 2, vals);
    unsigned long long rip = vals[0].Reg64, rbp = vals[1].Reg64;
    fprintf(stderr, "  Backtrace:\n");
    fprintf(stderr, "    #0 "); dbg_print_addr(rip); fprintf(stderr, "\n");
    /* Same wrap as dbg_dump_stack: bound by subtraction. */
    for (int depth = 1; depth < 32 && rbp > 0 && rbp < guest_mem_size &&
                        (guest_mem_size - rbp) >= 16; depth++) {
        if (!guest_page_committed(rbp) || !guest_page_committed(rbp + 15)) {
            fprintf(stderr, "    #%d (frame at 0x%llx: host page not committed, chain ends)\n", depth, rbp);
            break;
        }
        unsigned long long ret = *(unsigned long long *)((unsigned char *)guest_mem + rbp + 8);
        unsigned long long prev = *(unsigned long long *)((unsigned char *)guest_mem + rbp);
        if (ret == 0) break;
        fprintf(stderr, "    #%d ", depth); dbg_print_addr(ret); fprintf(stderr, "\n");
        rbp = prev;
    }
}

/* ── Mini x86-64 Disassembler ──────────────────────────────────────── */

static const char *reg64_names[] = {
    "rax","rcx","rdx","rbx","rsp","rbp","rsi","rdi",
    "r8","r9","r10","r11","r12","r13","r14","r15"
};

static const char *cc_names[] = {
    "o","no","b","ae","e","ne","be","a",
    "s","ns","p","np","l","ge","le","g"
};

static int disasm_one(unsigned char *code, int max_len, char *buf, int buf_sz) {
    if (max_len <= 0) { buf[0] = 0; return 0; }
    int pos = 0, rex_w = 0, rex_r = 0, rex_b = 0, rex_x = 0;
    int has_rex = 0;

    /* REX prefix */
    if (code[pos] >= 0x40 && code[pos] <= 0x4F) {
        has_rex = 1;
        rex_w = (code[pos] >> 3) & 1;
        rex_r = (code[pos] >> 2) & 1;
        rex_b = code[pos] & 1;
        rex_x = (code[pos] >> 1) & 1;
        pos++;
        if (pos >= max_len) { snprintf(buf, buf_sz, "rex"); return pos; }
    }

    unsigned char op = code[pos++];
    int rd, rs, mod, rm;
    long long imm;

    /* Helpers for modrm decoding */
    #define MODRM() do { if (pos >= max_len) goto raw; \
        mod = (code[pos]>>6)&3; rs = ((code[pos]>>3)&7)|(rex_r?8:0); \
        rm = (code[pos]&7)|(rex_b?8:0); pos++; } while(0)

    #define IMM32() do { if (pos+4 > max_len) goto raw; \
        imm = (int)(code[pos]|(code[pos+1]<<8)|(code[pos+2]<<16)|((unsigned)code[pos+3]<<24)); \
        pos += 4; } while(0)

    #define IMM8() do { if (pos >= max_len) goto raw; \
        imm = (signed char)code[pos]; pos++; } while(0)

    switch (op) {
    case 0x50: case 0x51: case 0x52: case 0x53:
    case 0x54: case 0x55: case 0x56: case 0x57:
        rd = (op - 0x50) | (rex_b ? 8 : 0);
        snprintf(buf, buf_sz, "push    %s", reg64_names[rd]); return pos;
    case 0x58: case 0x59: case 0x5A: case 0x5B:
    case 0x5C: case 0x5D: case 0x5E: case 0x5F:
        rd = (op - 0x58) | (rex_b ? 8 : 0);
        snprintf(buf, buf_sz, "pop     %s", reg64_names[rd]); return pos;
    case 0xC3:
        snprintf(buf, buf_sz, "ret"); return pos;
    case 0x90:
        snprintf(buf, buf_sz, "nop"); return pos;
    case 0xCC:
        snprintf(buf, buf_sz, "int3"); return pos;
    case 0xE8: /* call rel32 */
        IMM32();
        snprintf(buf, buf_sz, "call    0x%llx", imm + pos); return pos;
    case 0xE9: /* jmp rel32 */
        IMM32();
        snprintf(buf, buf_sz, "jmp     0x%llx", imm + pos); return pos;
    case 0xEB: /* jmp rel8 */
        IMM8();
        snprintf(buf, buf_sz, "jmp     0x%llx", imm + pos); return pos;
    case 0x89: /* mov r/m, r */
        MODRM();
        if (mod == 3)
            snprintf(buf, buf_sz, "mov     %s,%s", reg64_names[rm], reg64_names[rs]);
        else if (mod == 1) { IMM8(); snprintf(buf, buf_sz, "mov     [%s%+lld],%s", reg64_names[rm], imm, reg64_names[rs]); }
        else if (mod == 2) { IMM32(); snprintf(buf, buf_sz, "mov     [%s%+lld],%s", reg64_names[rm], imm, reg64_names[rs]); }
        else snprintf(buf, buf_sz, "mov     [%s],%s", reg64_names[rm], reg64_names[rs]);
        return pos;
    case 0x8B: /* mov r, r/m */
        MODRM();
        if (mod == 3)
            snprintf(buf, buf_sz, "mov     %s,%s", reg64_names[rs], reg64_names[rm]);
        else if (mod == 1) { IMM8(); snprintf(buf, buf_sz, "mov     %s,[%s%+lld]", reg64_names[rs], reg64_names[rm], imm); }
        else if (mod == 2) { IMM32(); snprintf(buf, buf_sz, "mov     %s,[%s%+lld]", reg64_names[rs], reg64_names[rm], imm); }
        else snprintf(buf, buf_sz, "mov     %s,[%s]", reg64_names[rs], reg64_names[rm]);
        return pos;
    case 0xC7: /* mov r/m, imm32 */
        MODRM();
        IMM32();
        if (mod == 3)
            snprintf(buf, buf_sz, "mov     %s,0x%llx", reg64_names[rm], imm & (rex_w ? 0xFFFFFFFFLL : 0xFFFFFFFFLL));
        else
            snprintf(buf, buf_sz, "mov     [%s],0x%llx", reg64_names[rm], imm);
        return pos;
    case 0xB8: case 0xB9: case 0xBA: case 0xBB:
    case 0xBC: case 0xBD: case 0xBE: case 0xBF: /* mov r, imm */
        rd = (op - 0xB8) | (rex_b ? 8 : 0);
        if (rex_w && pos + 8 <= max_len) {
            unsigned long long v = 0;
            for (int i = 0; i < 8; i++) v |= (unsigned long long)code[pos+i] << (i*8);
            pos += 8;
            snprintf(buf, buf_sz, "mov     %s,0x%llx", reg64_names[rd], v);
        } else { IMM32(); snprintf(buf, buf_sz, "mov     %s,0x%llx", reg64_names[rd], imm); }
        return pos;
    case 0x01: /* add r/m, r */
        MODRM();
        if (mod == 3) snprintf(buf, buf_sz, "add     %s,%s", reg64_names[rm], reg64_names[rs]);
        else snprintf(buf, buf_sz, "add     [%s],%s", reg64_names[rm], reg64_names[rs]);
        return pos;
    case 0x03: /* add r, r/m */
        MODRM();
        if (mod == 3) snprintf(buf, buf_sz, "add     %s,%s", reg64_names[rs], reg64_names[rm]);
        else snprintf(buf, buf_sz, "add     %s,[%s]", reg64_names[rs], reg64_names[rm]);
        return pos;
    case 0x29: /* sub r/m, r */
        MODRM();
        if (mod == 3) snprintf(buf, buf_sz, "sub     %s,%s", reg64_names[rm], reg64_names[rs]);
        else snprintf(buf, buf_sz, "sub     [%s],%s", reg64_names[rm], reg64_names[rs]);
        return pos;
    case 0x2B: /* sub r, r/m */
        MODRM();
        if (mod == 3) snprintf(buf, buf_sz, "sub     %s,%s", reg64_names[rs], reg64_names[rm]);
        else snprintf(buf, buf_sz, "sub     %s,[%s]", reg64_names[rs], reg64_names[rm]);
        return pos;
    case 0x39: /* cmp r/m, r */
        MODRM();
        if (mod == 3) snprintf(buf, buf_sz, "cmp     %s,%s", reg64_names[rm], reg64_names[rs]);
        else snprintf(buf, buf_sz, "cmp     [%s],%s", reg64_names[rm], reg64_names[rs]);
        return pos;
    case 0x3B: /* cmp r, r/m */
        MODRM();
        if (mod == 3) snprintf(buf, buf_sz, "cmp     %s,%s", reg64_names[rs], reg64_names[rm]);
        else snprintf(buf, buf_sz, "cmp     %s,[%s]", reg64_names[rs], reg64_names[rm]);
        return pos;
    case 0x85: /* test r/m, r */
        MODRM();
        if (mod == 3) snprintf(buf, buf_sz, "test    %s,%s", reg64_names[rm], reg64_names[rs]);
        else snprintf(buf, buf_sz, "test    [%s],%s", reg64_names[rm], reg64_names[rs]);
        return pos;
    case 0x31: /* xor r/m, r */
        MODRM();
        if (mod == 3) snprintf(buf, buf_sz, "xor     %s,%s", reg64_names[rm], reg64_names[rs]);
        else snprintf(buf, buf_sz, "xor     [%s],%s", reg64_names[rm], reg64_names[rs]);
        return pos;
    case 0x8D: /* lea */
        MODRM();
        if (mod == 1) { IMM8(); snprintf(buf, buf_sz, "lea     %s,[%s%+lld]", reg64_names[rs], reg64_names[rm], imm); }
        else if (mod == 2) { IMM32(); snprintf(buf, buf_sz, "lea     %s,[%s%+lld]", reg64_names[rs], reg64_names[rm], imm); }
        else snprintf(buf, buf_sz, "lea     %s,[%s]", reg64_names[rs], reg64_names[rm]);
        return pos;
    case 0x81: /* alu r/m, imm32 (add/or/adc/sbb/and/sub/xor/cmp) */
        MODRM(); IMM32();
        { const char *ops[] = {"add","or","adc","sbb","and","sub","xor","cmp"};
          snprintf(buf, buf_sz, "%-7s %s,0x%llx", ops[rs&7], reg64_names[rm], imm); }
        return pos;
    case 0x83: /* alu r/m, imm8 */
        MODRM(); IMM8();
        { const char *ops[] = {"add","or","adc","sbb","and","sub","xor","cmp"};
          snprintf(buf, buf_sz, "%-7s %s,%lld", ops[rs&7], reg64_names[rm], imm); }
        return pos;
    case 0x0F: /* two-byte opcode */
        if (pos >= max_len) goto raw;
        op = code[pos++];
        if (op >= 0x80 && op <= 0x8F) { /* jcc rel32 */
            IMM32();
            snprintf(buf, buf_sz, "j%-6s 0x%llx", cc_names[op-0x80], imm + pos);
            return pos;
        }
        if (op >= 0x90 && op <= 0x9F) { /* setcc */
            MODRM();
            snprintf(buf, buf_sz, "set%-4s %s", cc_names[op-0x90], reg64_names[rm]);
            return pos;
        }
        if (op == 0xAF) { /* imul r, r/m */
            MODRM();
            if (mod == 3) snprintf(buf, buf_sz, "imul    %s,%s", reg64_names[rs], reg64_names[rm]);
            else snprintf(buf, buf_sz, "imul    %s,[%s]", reg64_names[rs], reg64_names[rm]);
            return pos;
        }
        if (op == 0xB6) { /* movzx r, r/m8 */
            MODRM();
            snprintf(buf, buf_sz, "movzx   %s,%s", reg64_names[rs], reg64_names[rm]);
            return pos;
        }
        if (op == 0xBE) { /* movsx r, r/m8 */
            MODRM();
            snprintf(buf, buf_sz, "movsx   %s,%s", reg64_names[rs], reg64_names[rm]);
            return pos;
        }
        snprintf(buf, buf_sz, "0F %02x ...", op);
        return pos;
    case 0x70: case 0x71: case 0x72: case 0x73:
    case 0x74: case 0x75: case 0x76: case 0x77:
    case 0x78: case 0x79: case 0x7A: case 0x7B:
    case 0x7C: case 0x7D: case 0x7E: case 0x7F: /* jcc rel8 */
        IMM8();
        snprintf(buf, buf_sz, "j%-6s 0x%llx", cc_names[op-0x70], imm + pos);
        return pos;
    case 0xF7: /* group3: test/not/neg/mul/imul/div/idiv */
        MODRM();
        { const char *ops[] = {"test","test","not","neg","mul","imul","div","idiv"};
          snprintf(buf, buf_sz, "%-7s %s", ops[rs&7], reg64_names[rm]); }
        return pos;
    case 0xFF: /* group5: inc/dec/call/jmp/push */
        MODRM();
        if ((rs&7) == 2) snprintf(buf, buf_sz, "call    %s", reg64_names[rm]);
        else if ((rs&7) == 4) snprintf(buf, buf_sz, "jmp     %s", reg64_names[rm]);
        else if ((rs&7) == 6) snprintf(buf, buf_sz, "push    %s", reg64_names[rm]);
        else if ((rs&7) == 0) snprintf(buf, buf_sz, "inc     %s", reg64_names[rm]);
        else if ((rs&7) == 1) snprintf(buf, buf_sz, "dec     %s", reg64_names[rm]);
        else snprintf(buf, buf_sz, "ff /%d  %s", rs&7, reg64_names[rm]);
        return pos;
    default:
        break;
    }

raw:
    /* Fallback: raw hex */
    { int n = pos < max_len ? pos : 1;
      int off = 0;
      for (int i = 0; i < n && off < buf_sz - 4; i++)
          off += snprintf(buf + off, buf_sz - off, "%02x ", code[i]);
      snprintf(buf + off, buf_sz - off, "...");
    }
    return pos > 0 ? pos : 1;

    #undef MODRM
    #undef IMM32
    #undef IMM8
}

static void dbg_disasm_at(unsigned long long addr, int count) {
    /* Say why rather than printing an empty section under a heading. A blank
       "Code at RIP" with only the caret reads as "no instructions here" when
       what it means is "this address is not in guest memory", which is the
       single most useful fact about a wild jump. */
    if (addr >= guest_mem_size || addr < 16) {
        fprintf(stderr, "  (0x%llx is outside guest memory: cannot disassemble)\n", addr);
        return;
    }
    unsigned char *base = (unsigned char *)guest_mem + addr;
    /* Clamp BEFORE narrowing. `(int)(guest_mem_size - addr)` is negative for
       any addr below 1 GB on a 3 GB guest, so the loop below never ran and
       every crash report on this box printed an empty "Code at RIP" under the
       caret (measured 2026-08-18: 0xC0000000 - 0x100105 = 0xBFEFFEFB). */
    unsigned long long avail = guest_mem_size - addr;
    int max_bytes = avail > 256 ? 256 : (int)avail;
    if (!guest_page_committed(addr) || !guest_page_committed(addr + max_bytes - 1)) {
        fprintf(stderr, "  (0x%llx: host page not committed, the guest never executed here: cannot disassemble)\n", addr);
        return;
    }
    int offset = 0;
    for (int i = 0; i < count && offset < max_bytes; i++) {
        char buf[128];
        int len = disasm_one(base + offset, max_bytes - offset, buf, sizeof(buf));
        fprintf(stderr, "  %012llx: ", addr + offset);
        for (int j = 0; j < len && j < 8; j++) fprintf(stderr, "%02x ", base[offset+j]);
        for (int j = len; j < 8; j++) fprintf(stderr, "   ");
        fprintf(stderr, " %s", buf);
        /* symbol annotation for calls/jumps */
        if (buf[0] == 'c' || buf[0] == 'j') {
            char *at = strchr(buf, '0');
            if (at) {
                unsigned long long target = strtoull(at, NULL, 0);
                if (target > 0x100000) {
                    int off2; const char *sym = sym_lookup(addr + target, &off2);
                    if (sym) fprintf(stderr, "  ; <%s+%d>", sym, off2);
                }
            }
        }
        fprintf(stderr, "\n");
        offset += len;
    }
}

/* Forward declarations for crash report */
static unsigned long long dbg_read_reg(int idx);
static int dbg_command_loop(int vec, unsigned long long exc_rip);

/* ── Crash Report ─────────────────────────────────────────────────── */

static void dbg_auto_load_map(const char *kernel_path) {
    if (symbol_count > 0) return;
    if (!kernel_path) return;
    char map_path[512];
    /* Try <kernel-basename>.map */
    strncpy(map_path, kernel_path, sizeof(map_path) - 1);
    map_path[sizeof(map_path)-1] = 0;
    char *dot = strrchr(map_path, '.');
    if (dot && (dot - map_path) < (int)sizeof(map_path) - 5) {
        strcpy(dot, ".map");
        FILE *f = fopen(map_path, "r");
        if (f) { fclose(f); load_map_file(map_path); return; }
    }
    /* Try <kernel-dir>/Codex.map */
    strncpy(map_path, kernel_path, sizeof(map_path) - 1);
    char *sep = strrchr(map_path, '\\');
    if (!sep) sep = strrchr(map_path, '/');
    if (sep) { strcpy(sep + 1, "Codex.map"); }
    else strcpy(map_path, "Codex.map");
    FILE *f2 = fopen(map_path, "r");
    if (f2) { fclose(f2); load_map_file(map_path); return; }
    /* Try seed/Codex.map */
    f2 = fopen("seed\\Codex.map", "r");
    if (f2) { fclose(f2); load_map_file("seed\\Codex.map"); }
}

static void dbg_crash_report(const char *reason, unsigned long long fault_addr,
                             int access_type, const char *kernel_path) {
    fprintf(stderr, "\n╔══════════════════════════════════════════════════════════╗\n");
    fprintf(stderr, "║  CRASH: %-49s║\n", reason);
    fprintf(stderr, "╚══════════════════════════════════════════════════════════╝\n\n");

    dbg_auto_load_map(kernel_path);

    /* Registers */
    fprintf(stderr, "── Registers ──\n");
    dbg_dump_regs();

    /* The syscall MSRs, read back off the VP rather than out of the shadows, so
       this reports what the CPU holds and not what the host believes it set.
       A guest that jumps to a wild address on a `syscall` is indistinguishable
       from one that jumped there any other way unless you can see LSTAR, and
       that ambiguity cost a full diagnosis cycle on the UEFI boot path. */
    {
        WHV_REGISTER_NAME mn[4] = { WHvX64RegisterEfer, WHvX64RegisterStar,
                                    WHvX64RegisterLstar, WHvX64RegisterSfmask };
        WHV_REGISTER_VALUE mv[4];
        memset(mv, 0, sizeof(mv));
        if (SUCCEEDED(WHvGetVirtualProcessorRegisters(partition, 0, mn, 4, mv))) {
            fprintf(stderr, "  EFER=%016llx STAR=%016llx LSTAR=%016llx SFMASK=%016llx\n",
                    mv[0].Reg64, mv[1].Reg64, mv[2].Reg64, mv[3].Reg64);
        }
    }
    fprintf(stderr, "\n");

    /* Fault info */
    if (fault_addr != (unsigned long long)-1) {
        fprintf(stderr, "── Fault ──\n");
        fprintf(stderr, "  Address: 0x%llx  Access: %s\n", fault_addr,
            access_type == 0 ? "READ" : access_type == 1 ? "WRITE" : "EXEC");
        if (fault_addr < guest_mem_size && fault_addr > 0) {
            fprintf(stderr, "  Memory at fault address:\n");
            int dump_len = 64;
            if (fault_addr + dump_len > guest_mem_size) dump_len = (int)(guest_mem_size - fault_addr);
            dbg_dump_mem(fault_addr, dump_len);
        }
        fprintf(stderr, "\n");
    }

    /* Disassembly around RIP */
    unsigned long long rip = dbg_read_reg(0);
    fprintf(stderr, "── Code at RIP ──\n");
    /* Back up ~16 bytes for context */
    unsigned long long dis_start = rip > 32 ? rip - 32 : rip;
    dbg_disasm_at(dis_start, 12);
    fprintf(stderr, "         ^^^^^^^^ RIP\n\n");

    /* Backtrace */
    fprintf(stderr, "── Backtrace ──\n");
    dbg_backtrace();
    fprintf(stderr, "\n");

    /* Stack */
    fprintf(stderr, "── Stack ──\n");
    dbg_dump_stack(24);
    fprintf(stderr, "\n");

    if (symbol_count > 0)
        fprintf(stderr, "(%d symbols loaded)\n", symbol_count);
    else
        fprintf(stderr, "(no symbols -- pass -map <file.map> for resolved names)\n");

    if (!vga_headless) {
        fprintf(stderr, "\nEntering debugger. Type 'help' for commands, 'q' to quit.\n");
        dbg_command_loop(99, dbg_read_reg(0));
    }
}

static int dbg_set_breakpoint(unsigned long long addr, int cond_reg, unsigned long long cond_val) {
    if (addr >= guest_mem_size) { fprintf(stderr, "  address out of range\n"); return -1; }
    if (bp_count >= MAX_BREAKPOINTS) { fprintf(stderr, "  too many breakpoints\n"); return -1; }
    /* The byte we are about to save is the one we will restore. If a
       breakpoint is already patched here, the byte on the page is 0xCC and
       saving it destroys the instruction: restoring writes 0xCC back and the
       guest traps forever on an entry it no longer owns. Inherit the saved
       byte from whoever patched it, and refuse an unowned 0xCC rather than
       swallowing a real guest INT3. */
    unsigned char saved = *((unsigned char *)guest_mem + addr);
    if (saved == 0xCC) {
        int prev = -1;
        for (int k = 0; k < bp_count; k++)
            if (breakpoints[k].active && breakpoints[k].addr == addr) { prev = k; break; }
        if (prev < 0) {
            fprintf(stderr, "  0xCC already at 0x%llx and no breakpoint owns it; refusing\n", addr);
            return -1;
        }
        saved = breakpoints[prev].orig_byte;
    }
    int idx = bp_count++;
    breakpoints[idx].addr = addr;
    breakpoints[idx].orig_byte = saved;
    breakpoints[idx].active = 1;
    breakpoints[idx].cond_reg = cond_reg;
    breakpoints[idx].cond_val = cond_val;
    *((unsigned char *)guest_mem + addr) = 0xCC;
    fprintf(stderr, "DBG: breakpoint %d at 0x%llx (patched 0x%02x -> 0xCC)\n", idx, addr, breakpoints[idx].orig_byte);
    return idx;
}

static void dbg_enable_single_step(void) {
    WHV_REGISTER_NAME name = WHvX64RegisterRflags;
    WHV_REGISTER_VALUE val;
    WHvGetVirtualProcessorRegisters(partition, 0, &name, 1, &val);
    val.Reg64 |= 0x100; /* set TF */
    WHvSetVirtualProcessorRegisters(partition, 0, &name, 1, &val);
}

static const char *dbg_reg_names_tbl[18] = {
    "rip","rsp","rax","rbx","rcx","rdx","rsi","rdi","rbp",
    "r8","r9","r10","r11","r12","r13","r14","r15","rflags"
};

static int dbg_reg_index(const char *name) {
    for (int i = 0; i < 18; i++) if (!strcmp(name, dbg_reg_names_tbl[i])) return i;
    return -1;
}

static const char *dbg_reg_name(int idx) {
    if (idx < 0 || idx > 17) return "?";
    return dbg_reg_names_tbl[idx];
}

static unsigned long long dbg_read_reg(int idx) {
    WHV_REGISTER_NAME names[] = {
        WHvX64RegisterRip, WHvX64RegisterRsp, WHvX64RegisterRax,
        WHvX64RegisterRbx, WHvX64RegisterRcx, WHvX64RegisterRdx,
        WHvX64RegisterRsi, WHvX64RegisterRdi, WHvX64RegisterRbp,
        WHvX64RegisterR8, WHvX64RegisterR9, WHvX64RegisterR10,
        WHvX64RegisterR11, WHvX64RegisterR12, WHvX64RegisterR13,
        WHvX64RegisterR14, WHvX64RegisterR15, WHvX64RegisterRflags
    };
    WHV_REGISTER_VALUE val;
    WHvGetVirtualProcessorRegisters(partition, 0, &names[idx], 1, &val);
    return val.Reg64;
}

/* Returns: 0 = continue VM loop, 1 = goto done (quit) */
static int dbg_command_loop(int vec, unsigned long long exc_rip) {
    /* For #DB, check if this is a hardware breakpoint with a condition */
    if (vec == 1) {
        unsigned long long rip = dbg_read_reg(0); /* RIP */
        for (int i = 0; i < bp_count; i++) {
            if (breakpoints[i].addr == rip && breakpoints[i].active && breakpoints[i].cond_reg >= 0) {
                unsigned long long rv = dbg_read_reg(breakpoints[i].cond_reg);
                if (rv != breakpoints[i].cond_val) {
                    /* Condition not met -- resume via single-step past this address */
                    dbg_enable_single_step();
                    return 0;
                }
            }
        }
    }

    fprintf(stderr, "\n--- %s at ", vec == 1 ? "Step" : "Break");
    dbg_print_addr(vec == 3 ? exc_rip - 1 : exc_rip);
    fprintf(stderr, " ---\n");
    dbg_dump_regs();

    char cmd[256];
    for (;;) {
        fprintf(stderr, "dbg> ");
        if (!fgets(cmd, sizeof(cmd), stdin)) return 1;
        cmd[strcspn(cmd, "\n")] = 0;
        if (!cmd[0]) continue;

        if (!strcmp(cmd, "s") || !strcmp(cmd, "step")) {
            dbg_enable_single_step();
            return 0;
        }
        else if (!strcmp(cmd, "c") || !strcmp(cmd, "continue")) {
            return 0;
        }
        else if (!strcmp(cmd, "r") || !strcmp(cmd, "regs")) {
            dbg_dump_regs();
        }
        else if (!strncmp(cmd, "m ", 2) || !strncmp(cmd, "mem ", 4)) {
            char *arg = cmd + (cmd[1] == ' ' ? 2 : 4);
            unsigned long long addr = strtoull(arg, &arg, 0);
            int len = 64;
            while (*arg == ' ') arg++;
            if (*arg) len = atoi(arg);
            if (len <= 0) len = 64;
            if (len > 4096) len = 4096;
            dbg_dump_mem(addr, len);
        }
        else if (!strncmp(cmd, "x ", 2)) {
            /* x <addr> -- read 8-byte qword at addr */
            unsigned long long addr = strtoull(cmd + 2, NULL, 0);
            if (addr + 8 <= guest_mem_size) {
                unsigned long long val = *(unsigned long long *)((unsigned char *)guest_mem + addr);
                fprintf(stderr, "  [0x%llx] = 0x%llx (%llu)\n", addr, val, val);
            } else fprintf(stderr, "  out of range\n");
        }
        else if (!strcmp(cmd, "bt") || !strcmp(cmd, "backtrace")) {
            dbg_backtrace();
        }
        else if (!strcmp(cmd, "stack")) {
            dbg_dump_stack(16);
        }
        else if (!strncmp(cmd, "d ", 2) || !strncmp(cmd, "disasm ", 7)) {
            char *arg = cmd + (cmd[1] == ' ' ? 2 : 7);
            unsigned long long addr = 0;
            int count = 16;
            if (arg[0] == '0' && arg[1] == 'x') {
                addr = strtoull(arg, &arg, 0);
            } else {
                addr = sym_find(arg);
                while (*arg && *arg != ' ') arg++;
                if (!addr) { fprintf(stderr, "  symbol not found\n"); continue; }
            }
            while (*arg == ' ') arg++;
            if (*arg) count = atoi(arg);
            if (count <= 0) count = 16;
            dbg_disasm_at(addr, count);
        }
        else if (!strcmp(cmd, "di")) {
            /* disassemble at RIP, 16 instructions */
            unsigned long long rip = dbg_read_reg(0);
            dbg_disasm_at(rip, 16);
        }
        else if (!strncmp(cmd, "b ", 2) || !strncmp(cmd, "break ", 6)) {
            char *arg = cmd + (cmd[1] == ' ' ? 2 : 6);
            unsigned long long addr = 0;
            int cond_reg = -1;
            unsigned long long cond_val = 0;
            /* Try symbolic: "b funcname" or numeric "b 0x1234" */
            if (arg[0] == '0' && arg[1] == 'x') {
                addr = strtoull(arg, &arg, 16);
            } else {
                /* Parse "funcname" or "funcname if reg=val" */
                char fname[128]; int fi = 0;
                while (*arg && *arg != ' ' && fi < 127) fname[fi++] = *arg++;
                fname[fi] = 0;
                addr = sym_find(fname);
                if (!addr) { fprintf(stderr, "  symbol '%s' not found\n", fname); continue; }
            }
            /* Check for conditional: "if reg=val" */
            while (*arg == ' ') arg++;
            if (!strncmp(arg, "if ", 3)) {
                arg += 3;
                while (*arg == ' ') arg++;
                char rname[16]; int ri = 0;
                while (*arg && *arg != '=' && ri < 15) rname[ri++] = *arg++;
                rname[ri] = 0;
                cond_reg = dbg_reg_index(rname);
                if (cond_reg < 0) { fprintf(stderr, "  unknown register '%s'\n", rname); continue; }
                if (*arg == '=') arg++;
                cond_val = strtoull(arg, NULL, 0);
                fprintf(stderr, "  conditional: %s == 0x%llx\n", rname, cond_val);
            }
            int idx = dbg_set_breakpoint(addr, cond_reg, cond_val);
            if (idx >= 0) {
                fprintf(stderr, "  breakpoint %d at ", idx);
                dbg_print_addr(addr);
                fprintf(stderr, "\n");
            }
        }
        else if (!strncmp(cmd, "w ", 2) || !strncmp(cmd, "watch ", 6)) {
            char *arg = cmd + (cmd[1] == ' ' ? 2 : 6);
            watch_addr = strtoull(arg, &arg, 0);
            while (*arg == ' ') arg++;
            if (*arg) watch_size = atoi(arg);
            if (watch_size <= 0) watch_size = 8;
            watch_init();
            fprintf(stderr, "  watching 0x%llx (%d bytes)\n", watch_addr, watch_size);
        }
        else if (!strncmp(cmd, "sym ", 4)) {
            char *arg = cmd + 4;
            while (*arg == ' ') arg++;
            unsigned long long a = sym_find(arg);
            if (a) fprintf(stderr, "  %s = 0x%llx\n", arg, a);
            else fprintf(stderr, "  not found\n");
        }
        else if (!strcmp(cmd, "q") || !strcmp(cmd, "quit")) {
            return 1;
        }
        else if (!strcmp(cmd, "help") || !strcmp(cmd, "h") || !strcmp(cmd, "?")) {
            fprintf(stderr,
                "  s / step          -- single-step one instruction\n"
                "  c / continue      -- resume execution\n"
                "  r / regs          -- dump registers\n"
                "  m <addr> [len]    -- dump memory (hex+ascii)\n"
                "  x <addr>          -- read qword at address\n"
                "  d <fn|addr> [n]   -- disassemble n instructions at address\n"
                "  di                -- disassemble 16 instructions at RIP\n"
                "  bt / backtrace    -- walk RBP chain\n"
                "  stack             -- dump 16 stack slots\n"
                "  b <fn|addr> [if reg=val] -- set breakpoint (conditional)\n"
                "  w <addr> [size]   -- set memory watchpoint\n"
                "  sym <name>        -- look up symbol address\n"
                "  q / quit          -- exit VM\n");
        }
        else {
            fprintf(stderr, "  unknown command: %s (type 'help')\n", cmd);
        }
    }
}

/* Shutdown watchdog: runs in a background thread, polls serial socket
   health every 500ms.  When the harness closes the socket (or the process
   receives Ctrl+C), the watchdog calls WHvCancelRunVirtualProcessor to
   break the main loop out of the blocking WHvRunVirtualProcessor call.
   This lets the main loop exit through the normal cleanup path
   (WHvDeletePartition), avoiding vid.sys kernel heap corruption (0x13A)
   that results from TerminateProcess killing us mid-hypervisor-call. */
static volatile int shutdown_requested = 0;
static HANDLE shutdown_event;

static void create_shutdown_event(void) {
    char name[64];
    snprintf(name, sizeof(name), "Global\\CodexVmShutdown_%lu", GetCurrentProcessId());
    shutdown_event = CreateEventA(NULL, TRUE, FALSE, name);
}

static DWORD WINAPI shutdown_event_thread(LPVOID arg) {
    (void)arg;
    if (!shutdown_event) return 0;
    WaitForSingleObject(shutdown_event, INFINITE);
    shutdown_requested = 1;
    if (partition) WHvCancelRunVirtualProcessor(partition, 0, 0);
    return 0;
}

/* Host-side sampling profiler state (CODEX_VM_PROFILE=<file>). */
#define HPROF_MAX 65536
static const char *hprof_file = NULL;
static unsigned long long hprof_rips[HPROF_MAX];
static int hprof_count = 0;

/* Force a VP exit every PIT period so a compute-bound guest still gets
   timer interrupts. WHvRunVirtualProcessor only returns on exits; with
   no I/O and no HLT the main loop never regains control, so the tick
   check never runs. The cancel is benign (Canceled exits are handled)
   and costs ~18 exits/second. */
static DWORD WINAPI timer_kick_thread(LPVOID arg) {
    (void)arg;
    for (;;) {
        Sleep(55);
        if (shutdown_requested) return 0;
        if (!partition) continue;
        WHvCancelRunVirtualProcessor(partition, 0, 0);
        /* The application processors need the same shove, and for the same
           reason: a compute-bound core never leaves WHvRunVirtualProcessor,
           so the LAPIC timer tick its thread would inject on the next lap
           never gets injected, and the core is never preempted. Cancelling
           is benign -- a Canceled exit is handled -- and it is what gives an
           AP a scheduling clock at all. */
        for (int i = 1; i < SMP_MAX_CORES; i++) {
            if (lapic_state.ap_running[i]) WHvCancelRunVirtualProcessor(partition, i, 0);
        }
    }
}

/* Last-resort crash reporter. Prints the fault to stderr (which the test
   harness captures) instead of raising a dialog, then exits with 0xC0DE
   so a harness can tell a host crash from a guest failure. */
static LONG WINAPI crash_filter(EXCEPTION_POINTERS *ep) {
    DWORD code = ep && ep->ExceptionRecord ? ep->ExceptionRecord->ExceptionCode : 0;
    unsigned long long addr = 0, ref = 0;
    int writing = -1;
    if (ep && ep->ExceptionRecord) {
        addr = (unsigned long long)ep->ExceptionRecord->ExceptionAddress;
        if ((code == EXCEPTION_ACCESS_VIOLATION ||
             code == EXCEPTION_IN_PAGE_ERROR) &&
            ep->ExceptionRecord->NumberParameters >= 2) {
            writing = (int)ep->ExceptionRecord->ExceptionInformation[0];
            ref = (unsigned long long)ep->ExceptionRecord->ExceptionInformation[1];
        }
    }
    fprintf(stderr,
        "\nHOST CRASH: codex-vm faulted (code=0x%08lX) at 0x%llX",
        (unsigned long)code, addr);
    if (writing >= 0)
        fprintf(stderr, " %s memory 0x%llX",
                writing == 1 ? "writing" : writing == 8 ? "executing" : "reading", ref);
    fprintf(stderr, "\nHOST CRASH: this is a defect in codex-vm itself, not in the guest.\n");
    fflush(stderr);
    _exit(0xC0DE);
    return EXCEPTION_EXECUTE_HANDLER;
}

static BOOL WINAPI ctrl_handler(DWORD type) {
    (void)type;
    shutdown_requested = 1;
    if (partition) WHvCancelRunVirtualProcessor(partition, 0, 0);
    return TRUE;
}

/* Input drip-feed thread: when the input file exceeds the guest ring,
   periodically cancel the VP so the main loop drip-feeds overflow data.
   Without this, the guest busy-waits on the empty ring with no VM exits,
   and the drip-feed (which runs on exit) never fires -- deadlock. */
static DWORD WINAPI drip_feed_thread(LPVOID arg) {
    (void)arg;
    while (!shutdown_requested) {
        if (input_overflow && input_overflow_pos < input_overflow_len) {
            Sleep(5);
            if (partition) WHvCancelRunVirtualProcessor(partition, 0, 0);
        } else {
            Sleep(50);
        }
    }
    return 0;
}

/* Apply any scripted pointer sample whose time has come, then re-ring every
   HID interrupt endpoint left NAKing so its armed TD can complete.

   Scripted samples go through the same fields the window proc writes, press
   latch included, so the guest cannot tell a script from a hand. The re-ring
   is what makes delivery track the INPUT rather than the guest's poll cadence,
   and it only works because the NAK model leaves a TD armed. Under
   -hid-instant-complete it is skipped, and skipping it is not why that model
   drops narrow keystrokes: measured 2026-08-06, running it unconditionally
   fires twice (make and break) and produces no report, because that model
   already drained the ring at the guest's last doorbell.

   Called from the main loop and from the HID service thread, which is why the
   whole body is under xhci_db_lock: the two threads must not interleave the
   timeline cursor, the pending-sample fields or the ring walk.

   mouse_only is what the service thread passes, and it is not a
   micro-optimisation. Servicing every HID endpoint off-thread lost keystrokes
   outright -- usb-hid-combo went got=30 to got=0 -- because the keyboard's
   make/break pair and its unchanged-report latch were being walked
   concurrently with the main loop applying them. The pointer is the endpoint
   that needed rescuing; the keyboard is delivered exactly where it always
   was. */
static void hid_service_pending(int mouse_only) {
    xhci_db_lock_enter();
    if (inject_mouse_idx < inject_mouse_count && perf_freq.QuadPart) {
        LARGE_INTEGER mnow;
        QueryPerformanceCounter(&mnow);
        double mel = (double)(mnow.QuadPart - hid_timebase.QuadPart) * 1000.0 / perf_freq.QuadPart;
        while (inject_mouse_idx < inject_mouse_count &&
               mel >= inject_mouse[inject_mouse_idx].t_ms) {
            InjectMouse *ev = &inject_mouse[inject_mouse_idx++];
            int prev_btn = pending_mouse_btn;
            LONG pressed = (LONG)(ev->btn & ~prev_btn);
            if (pressed) InterlockedOr(&pending_mouse_btn_latch, pressed);
            pending_mouse_abs_x = ev->x;
            pending_mouse_abs_y = ev->y;
            pending_mouse_btn = ev->btn;
            pending_mouse_valid = 1;
            hid_mouse_fresh = 1;
            hid_input_changed = 1;
        }
    }

    if (!hid_nak_unchanged || !hid_input_changed) { xhci_db_lock_leave(); return; }
    hid_reringe_calls++;
    /* Only the full pass may clear the flag. A mouse-only pass that cleared it
       would swallow the keyboard's turn. */
    if (!mouse_only) hid_input_changed = 0;
    if (hid_nak_tracing() && hid_nak_dbg < 8) {
        hid_nak_dbg++;
        fprintf(stderr, "HIDNAK: input changed, re-ringing seen endpoints (ctl=%p)\n", (void *)hid_nak_ctl);
    }
    struct xhci_state *nak_saved = xcur;
    if (hid_nak_ctl) xcur = hid_nak_ctl;
    for (int nak_s = 1; nak_s <= XHCI_MAX_SLOTS; nak_s++)
        for (int nak_e = 2; nak_e < 32; nak_e++)
            if (hid_nak_seen[nak_s][nak_e]) {
                if (mouse_only && !(hid_combo && nak_e == 5)) continue;
                xhci_handle_doorbell(nak_s, (unsigned int)nak_e);
            }
    xcur = nak_saved;
    xhci_db_lock_leave();
}

/* The HID service thread: it is what a controller polling a periodic endpoint
   does, and nothing else in the model was doing it.

   Interrupt-IN delivery used to run only in the main loop, and the main loop
   runs only when the guest exits. A desk PANE loop is pure guest memory --
   kbd-take, mouse-pump's event-ring peek, a compare -- so it exits for
   nothing, and the pointer arrived at the 55 ms timer kicker's cadence. The
   desktop escaped it for one accidental reason: desk-clock reads the CMOS
   every trip, which is a port exit. Real silicon has no such coupling, so
   this is bed fidelity rather than a guest defect.

   Cancelling the VP to force main-loop laps was the first attempt and it only
   reached two thirds: measured, delivery tracked the CANCEL round trip at
   ~66/s, and Sleep(0) here -- kicking without bound -- measured the same,
   because a cancel raised while the VP is not running is dropped. Servicing
   directly off this thread removes the exit path from the question entirely,
   which is why the ring walk is under xhci_db_lock.

   And then the thread's own lap rate was the cap: Sleep(1) at the default
   15.6 ms quantum measured 61.8 laps a second, one report a lap, 65.6
   reports/s. timeBeginPeriod(1) at the CreateThread site takes it to 507.

   Measured with a pointer driven at 100 samples/s, before -> after:
   Calendar 17.8 -> 102.2 reports/s, Calc 18.0 -> 102.2, Files 102.2, against
   the desktop's 102.2 unmoved -- every pane now saturates the input. The
   Calendar pane's loop repaints nothing at all, which is what ruled its old
   17.8 out of being repaint cost.

   Gated so a run with no pointer activity is untouched: it wakes on the slow
   lap unless a scripted sample is still due or input is undelivered. */
static DWORD WINAPI hid_kick_thread(LPVOID arg) {
    (void)arg;
    while (!shutdown_requested) {
        if (hid_nak_unchanged &&
            (hid_input_changed || inject_mouse_idx < inject_mouse_count)) {
            Sleep(1);
            hid_service_laps++;
            hid_service_pending(1);
        } else {
            Sleep(20);
        }
    }
    return 0;
}

/* ── Serial ────────────────────────────────────────────────────────── */

/* Memory-mapped I/O: load input file directly into the guest's serial ring
   buffer at 0x500000. Set write-pos so the guest reads it immediately.
   The guest compiler's __bare_metal_read_serial polls the ring buffer --
   by pre-filling it, we bypass UART entirely. Zero TCP, zero port I/O. */
static void load_input_file(const char *path) {
    if (!path || !guest_mem) return;
    FILE *f = fopen(path, "rb");
    if (!f) { fprintf(stderr, "ERROR: cannot open input %s\n", path); return; }
    fseek(f, 0, SEEK_END);
    size_t sz = ftell(f);
    fseek(f, 0, SEEK_SET);
    if (sz > INPUT_BUF_MAX) { fprintf(stderr, "ERROR: input too large (%zu > %d)\n", sz, INPUT_BUF_MAX); fclose(f); return; }

    unsigned char *ring = (unsigned char *)guest_mem + INPUT_BUF_ADDR;
    size_t initial = sz < GUEST_RING_SIZE ? sz : GUEST_RING_SIZE;
    fread(ring, 1, initial, f);
    input_total_written = initial;

    if (sz > initial) {
        input_overflow_len = sz - initial;
        input_overflow = (unsigned char *)malloc(input_overflow_len);
        fread(input_overflow, 1, input_overflow_len, f);
        input_overflow_pos = 0;
    }
    fclose(f);

    *(unsigned long long *)((unsigned char *)guest_mem + 28704) = (unsigned long long)initial;
    *(unsigned long long *)((unsigned char *)guest_mem + 28712) = 0ULL;
    fprintf(stderr, "Input: %s (%zu bytes) -> ring buffer at 0x500000 (initial %zu, overflow %zu)\n",
            path, sz, initial, input_overflow_len);
}

/* Drip-feed: called periodically from the main loop. When the guest has
   consumed data (read-pos advanced), copy more from the overflow buffer
   into the freed ring-buffer space and advance write-pos. */
static void input_drip_feed(void) {
    if (!input_overflow || input_overflow_pos >= input_overflow_len) return;
    unsigned long long wpos = *(unsigned long long *)((unsigned char *)guest_mem + 28704);
    unsigned long long rpos = *(unsigned long long *)((unsigned char *)guest_mem + 28712);
    /* Don't drip-feed until the guest has booted and the COM1 re-injection
       has set wpos to the initial load size.  Before that, wpos is either
       the raw pre-boot value or 0 (zeroed by the boot stub).  The
       re-injection sets wpos = input_total_written (initial load size).
       Only start drip-feeding once the guest is actively consuming. */
    if (wpos < input_total_written) return;
    unsigned long long used = wpos - rpos;
    if (used >= GUEST_RING_SIZE) return;
    size_t free_space = GUEST_RING_SIZE - (size_t)used;
    size_t remaining = input_overflow_len - input_overflow_pos;
    size_t to_copy = free_space < remaining ? free_space : remaining;
    unsigned char *ring = (unsigned char *)guest_mem + INPUT_BUF_ADDR;
    for (size_t i = 0; i < to_copy; i++) {
        ring[(wpos + i) & GUEST_RING_MASK] = input_overflow[input_overflow_pos + i];
    }
    input_overflow_pos += to_copy;
    wpos += to_copy;
    *(unsigned long long *)((unsigned char *)guest_mem + 28704) = wpos;
    input_total_written = wpos;
}

/* Output buffer: accumulate guest UART writes (port 0x3F8 OUT) */
static unsigned char *output_buf = NULL;
static size_t output_len = 0;
static size_t output_cap = 0;
/* Serial bytes dropped because the buffer could not grow. Reported once when
   the first is dropped and again with the total at dump_output_file, the same
   discipline as the xHCI event ring's er_dropped: a short -output must be
   attributable, because the guest's OUT completed and it believes the byte
   was delivered. */
static size_t output_dropped = 0;

/* Application processors write serial too -- an AP's exception dump is the only
 * way a fault on one is ever seen -- and they run on their own host threads, so
 * this append is no longer the boot processor's alone. */
static CRITICAL_SECTION output_lock;
static int output_lock_ready = 0;

static void output_buf_init(void) {
    output_cap = 16 * 1024 * 1024;  /* 16MB */
    output_buf = (unsigned char *)malloc(output_cap);
    output_len = 0;
    InitializeCriticalSection(&output_lock);
    output_lock_ready = 1;
}

/* Debug: detect "!EXC=03" in serial stream */
static char exc_detect_buf[8];
static int exc_detect_pos = 0;
static int dbg_exc_pending = 0;

static void output_buf_write(unsigned char b) {
    if (!output_buf) return;
    if (output_lock_ready) EnterCriticalSection(&output_lock);
    if (output_len == output_cap) {
        /* Same growth-and-move discipline as blit_guest_output below. The
           16MB starting cap silently truncated any serial stream past it,
           and a streamed IR payload for a wide-citation program is bigger
           than that (measured 2026-07-28: ui-orchestrator-test's IR frame
           cut off at exactly the cap). On growth failure keep the old
           buffer and drop the byte, which is the old behavior. */
        size_t new_cap = output_cap * 2;
        unsigned char *grown = (unsigned char *)realloc(output_buf, new_cap);
        if (grown) { output_buf = grown; output_cap = new_cap; }
    }
    if (output_len < output_cap) output_buf[output_len++] = b;
    else {
        if (!output_dropped)
            fprintf(stderr, "SERIAL: output buffer growth failed at %zu bytes: dropping guest serial bytes -- -output will be SHORT\n",
                    output_cap);
        output_dropped++;
    }
    if (output_lock_ready) LeaveCriticalSection(&output_lock);

    /* Detect "!EXC=03" pattern for debugger */
    if (debug_mode && bp_count > 0) {
        const char *pattern = "!EXC=03";
        if (b == (unsigned char)pattern[exc_detect_pos]) {
            exc_detect_pos++;
            if (exc_detect_pos == 7) {
                dbg_exc_pending = 1;
                exc_detect_pos = 0;
            }
        } else {
            exc_detect_pos = (b == '!') ? 1 : 0;
        }
    }
}

/* Write the serial capture to -output WHILE THE GUEST IS STILL RUNNING.
   dump_output_file below is called once, at exit, which is fine for a program
   that terminates and useless for a server: its -output stayed empty however
   healthy it was, and the only way to flush it was to stop the VM, which for a
   server means killing it -- and a killed codex-vm never reaches the exit dump,
   so the diagnostics were lost precisely when they were wanted. Debugging a
   Codex server through its own log was therefore impossible, and it has cost
   more than one session.

   Cheap enough to be unconditional: a full rewrite of a few KB, at most twice a
   second, and only when the buffer has actually grown. Takes output_lock
   because APs append to output_buf from their own host threads. */
static void poll_output_dump(void) {
    static double last_ms = 0;
    static size_t last_len = 0;
    if (!output_file || !output_buf) return;
    double now = now_ms_for_timer();
    if (now - last_ms < 500.0) return;
    last_ms = now;
    if (output_len == last_len || output_len == 0) return;
    if (output_lock_ready) EnterCriticalSection(&output_lock);
    FILE *f = fopen(output_file, "wb");
    if (f) { fwrite(output_buf, 1, output_len, f); fclose(f); }
    last_len = output_len;
    if (output_lock_ready) LeaveCriticalSection(&output_lock);
}

static void dump_output_file(const char *path) {
    if (!path || !output_buf || output_len == 0) return;
    FILE *f = fopen(path, "wb");
    if (!f) { fprintf(stderr, "ERROR: cannot write output %s\n", path); return; }
    fwrite(output_buf, 1, output_len, f);
    fclose(f);
    fprintf(stderr, "Output: %zu bytes -> %s\n", output_len, path);
    if (output_dropped)
        fprintf(stderr, "SERIAL: %zu guest serial byte(s) DROPPED (buffer growth failed at %zu bytes); %s is SHORT\n",
                output_dropped, output_cap, path);
}

/* Bulk blit: append guest RAM [addr, addr+len) to the output buffer in
   one exit. addr/len come from the fixed guest cells; grow the buffer
   as needed instead of silently dropping like output_buf_write. */
static void blit_guest_output(void) {
    if (!guest_mem || !output_buf) return;
    if (BLIT_LEN_CELL + 8 > guest_mem_size) return;
    unsigned long long addr = *(unsigned long long *)((unsigned char *)guest_mem + BLIT_ADDR_CELL);
    unsigned long long len  = *(unsigned long long *)((unsigned char *)guest_mem + BLIT_LEN_CELL);
    if (len == 0) return;
    if (addr >= guest_mem_size || len > guest_mem_size - addr) {
        /* Also a whole-blit discard, and also uncounted until now. */
        output_dropped += (size_t)len;
        fprintf(stderr, "BLIT: rejected addr=0x%llx len=%llu (guest_mem_size=%llu)\n",
                addr, len, (unsigned long long)guest_mem_size);
        fprintf(stderr, "SERIAL: %llu guest serial byte(s) DROPPED (blit out of range); -output is SHORT\n", len);
        return;
    }
    /* APs append serial bytes under output_lock from their own host threads; this
       path reallocs and MOVES output_buf, so it must hold the same lock or a
       concurrent AP write dereferences the freed pointer. */
    if (output_lock_ready) EnterCriticalSection(&output_lock);
    if (output_len + len > output_cap) {
        size_t new_cap = output_cap * 2;
        while (output_len + len > new_cap) new_cap *= 2;
        unsigned char *grown = (unsigned char *)realloc(output_buf, new_cap);
        if (!grown) {
            /* Count it the way output_buf_write counts its own drops, and say
               the same words. This path discards a WHOLE BLIT -- one contiguous
               chunk, typically a complete protocol block -- and it used to do so
               without touching output_dropped, so dump_output_file's "N byte(s)
               DROPPED ... is SHORT" line could not fire for the path that
               carries bulk output. The batch parser assigns blocks to test names
               by sequence and cannot notice a missing one, so a silent drop here
               files every later block under the wrong test's name
               (ExaminersAssay, "The batch stream can lose bytes"). One marker
               for both paths is what lets a reader attribute a short capture. */
            output_dropped += (size_t)len;
            fprintf(stderr, "BLIT: output buffer growth failed (%zu bytes)\n", new_cap);
            fprintf(stderr, "SERIAL: %llu guest serial byte(s) DROPPED (blit growth failed at %zu bytes); -output is SHORT\n",
                    len, output_cap);
            if (output_lock_ready) LeaveCriticalSection(&output_lock);
            return;
        }
        output_buf = grown;
        output_cap = new_cap;
    }
    memcpy(output_buf + output_len, (unsigned char *)guest_mem + addr, (size_t)len);
    output_len += (size_t)len;
    if (output_lock_ready) LeaveCriticalSection(&output_lock);
}

/* ── IDE ───────────────────────────────────────────────────────────── */

/* IDE write census. A 2.9 MB save takes 57 s in this bed and the FAT logic
   is not the cost; two sites on the transfer path can account for it and no
   total can separate them. One is the VM exit per 16-bit PIO word, counted
   as pio-exits against the words those exits carried. The other is this
   model's own host file I/O: ide_flush reopens the image per completed
   sector, which is not a VM exit and would survive a repair aimed only at
   the first. Counted as sites, printed at exit, and the flush arm is timed
   because its cost is wall clock rather than a count. */
static unsigned long long ide_pio_exits = 0;    /* exits taken on the data port */
static unsigned long long ide_pio_str_exits = 0;/* ...of which carried a string op */
static unsigned long long ide_out_batched = 0;  /* words the batched OUT arm carried */
static unsigned long long ide_out_batch_hits = 0;/* exits that arm served */
static unsigned long long ide_in_batched = 0;   /* words the batched IN arm carried */
static unsigned long long ide_in_batch_hits = 0;
static unsigned long long ide_pio_words = 0;    /* words those exits carried */
static unsigned long long ide_reg_exits = 0;    /* exits on the other task-file registers */
static unsigned long long ide_flush_calls = 0;  /* ide_flush entries that reached the file */
static unsigned long long ide_flush_bytes = 0;
static double ide_flush_ms = 0.0;
static unsigned long long ide_flush_entries = 0;  /* ...before the guard */
static unsigned long long ide_flush_nopath = 0;   /* refused: no image file behind this drive */
static unsigned long long ide_flush_nodata = 0;   /* refused: no in-memory image */
static unsigned long long ide_flush_oob = 0;      /* refused: region past the end of the image */
static unsigned long long ide_flush_openfail = 0; /* refused: the reopen failed */

static void ide_init(IdeState *d, const char *path) {
    memset(d, 0, sizeof(*d));
    d->status = 0x50; /* DRDY */
    if (!path) return;
    d->path = path;
    FILE *f = fopen(path, "rb");
    if (!f) { fprintf(stderr, "WARN: cannot open disk %s\n", path); return; }
    d->present = 1;
    fseek(f, 0, SEEK_END);
    d->size = ftell(f);
    fseek(f, 0, SEEK_SET);
    d->data = malloc(d->size);
    fread(d->data, 1, d->size, f);
    fclose(f);
    fprintf(stderr, "IDE: %s (%zu bytes, %zu sectors)\n", path, d->size, d->size/512);
}

/* Persist a written region back to the disk image file (durability).

   The handle is opened once and HELD. Reopening it per completed sector is
   what a 2.9 MB save actually spent its time on: measured 2026-08-20 against
   docs/Probes/fat-write-phases.codex, 5,865 sectors cost 62.5 s of a 68 s
   write, against 6 s for the same run with the write-back refused. The VM
   exit per PIO word is real (pio-exits and words come out equal in the
   census) and is the other 6 s.

   fflush per sector keeps what the old fclose promised: the bytes are with
   the OS when the sector completes, so a codex-vm killed mid-run leaves the
   same image behind as before. What is dropped is the open and the close.

   A refused open is reported ONCE and latched. It used to print per sector
   and be counted nowhere, which is how a read-only image -- the state a
   plain Copy-Item of a Perforce file leaves behind, and exactly what the
   probe's own run instructions produce -- lost all 5,865 sectors while the
   guest reported the save complete and the run finished ten times faster. */
static void ide_flush(IdeState *d, size_t off, size_t len) {
    ide_flush_entries++;
    if (!d->path) { ide_flush_nopath++; return; }
    if (!d->data) { ide_flush_nodata++; return; }
    if (off + len > d->size) { ide_flush_oob++; return; }
    if (d->wfp_failed) { ide_flush_openfail++; return; }
    double t0 = now_ms_for_timer();
    if (!d->wfp) {
        d->wfp = fopen(d->path, "r+b");
        if (!d->wfp) {
            d->wfp_failed = 1;
            ide_flush_openfail++;
            fprintf(stderr, "WARN: disk %s cannot be opened for write -- every write "
                    "the guest makes is now LOST, and the guest cannot tell\n", d->path);
            return;
        }
    }
    fseek(d->wfp, (long)off, SEEK_SET);
    fwrite(d->data + off, 1, len, d->wfp);
    fflush(d->wfp);
    ide_flush_calls++;
    ide_flush_bytes += (unsigned long long)len;
    ide_flush_ms += now_ms_for_timer() - t0;
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

/* Called when the guest has drained the current sector. buf_off already sits
   at the next sector: ide_read_data advanced it two bytes per word for all
   256 words. Adding 512 here skipped every other sector, which no caller
   noticed until a driver issued a multi-sector READ SECTORS (count > 1). */
static void ide_advance(IdeState *d) {
    if (d->sectors_left <= 0) { d->status = 0x50; d->buf_remaining = 0; return; }
    d->buf_remaining = 512;
    d->sectors_left--;
    d->status = 0x58;
}

static void ide_start_write(IdeState *d) {
    unsigned int lba = ide_get_lba(d);
    int count = d->sect_count ? d->sect_count : 256;
    if ((size_t)lba * 512 >= d->size) { d->status = 0x51; d->error = 0x10; return; }
    d->buf_off = (size_t)lba * 512;
    d->buf_remaining = 512;
    d->sectors_left = count - 1;
    d->writing = 1;
    d->status = 0x58; /* DRDY|DRQ -- ready to accept data */
    d->error = 0;
}

/* Accept one 16-bit word during a WRITE SECTORS transfer (REP OUTSW). Stores
   into the in-memory disk and flushes each completed sector to the image. */
static void ide_write_data(IdeState *d, int val) {
    if (!d->writing || d->buf_remaining <= 0) return;
    ide_pio_words++;
    if (d->buf_off + 1 < d->size) {
        d->data[d->buf_off]     = (unsigned char)(val & 0xFF);
        d->data[d->buf_off + 1] = (unsigned char)((val >> 8) & 0xFF);
    }
    d->buf_off += 2;
    d->buf_remaining -= 2;
    if (d->buf_remaining <= 0) {
        size_t sec_off = d->buf_off - 512;
        ide_flush(d, sec_off, 512);
        if (d->sectors_left <= 0) { d->status = 0x50; d->writing = 0; }
        else { d->buf_remaining = 512; d->sectors_left--; d->status = 0x58; }
    }
}

/* IDENTIFY DEVICE (0xEC): one synthesized 512-byte sector describing the
   drive, never touching the image bytes. Model at words 27-46 in the ATA
   string layout (two chars per word, first char in the HIGH byte, words
   little-endian on the wire = consecutive chars pairwise swapped), sector
   count at words 60-61 (LBA28, clamped) and 100-103 (LBA48). Real drives
   and QEMU answer this; drivers size and name disks by it. */
static unsigned char ide_ident[512];

static void ide_start_identify(IdeState *d) {
    if (!d->data || d->size == 0) { d->status = 0x51; d->error = 0x04; return; } /* ABRT */
    memset(ide_ident, 0, sizeof ide_ident);
    const char *model = "CODEX VM IDE DISK";
    size_t mlen = strlen(model);
    for (int i = 0; i < 20; i++) {
        char a = (size_t)(i*2)   < mlen ? model[i*2]   : ' ';
        char b = (size_t)(i*2+1) < mlen ? model[i*2+1] : ' ';
        ide_ident[54 + i*2]     = (unsigned char)b;
        ide_ident[54 + i*2 + 1] = (unsigned char)a;
    }
    unsigned long long sectors = d->size / 512;
    unsigned int lba28 = sectors > 0x0FFFFFFFULL ? 0x0FFFFFFFu : (unsigned int)sectors;
    for (int i = 0; i < 4; i++) ide_ident[120 + i] = (unsigned char)(lba28 >> (8*i));
    for (int i = 0; i < 8; i++) ide_ident[200 + i] = (unsigned char)(sectors >> (8*i));
    d->identing = 1;
    d->writing = 0;
    d->buf_remaining = 512;
    d->sectors_left = 0;
    d->status = 0x58; /* DRDY|DRQ */
    d->error = 0;
}

static int ide_read_data(IdeState *d) {
    if (d->buf_remaining <= 0) return 0;
    ide_pio_words++;
    if (d->identing) {
        int pos = 512 - d->buf_remaining;
        int lo = ide_ident[pos];
        int hi = ide_ident[pos + 1];
        d->buf_remaining -= 2;
        if (d->buf_remaining <= 0) { d->identing = 0; d->status = 0x50; }
        return lo | (hi << 8);
    }
    int lo = (d->buf_off < d->size) ? d->data[d->buf_off] : 0;
    int hi = (d->buf_off+1 < d->size) ? d->data[d->buf_off+1] : 0;
    d->buf_off += 2;
    d->buf_remaining -= 2;
    if (d->buf_remaining <= 0) ide_advance(d);
    return lo | (hi << 8);
}

/* -no-ide: the disk image stays loaded (backing the USB mass-storage
   device model) but the legacy IDE ports answer as if no drive were
   present -- the shape of a modern board, where a USB-booted machine
   reaches its own medium only through the USB stack. Status reads
   return ERR-only so fuel-bounded drivers fail fast instead of
   burning a full poll budget against a floating bus. */
static int no_ide = 0;

static void ide_handle_out(IdeState *d, int port, int val) {
    int reg = port - 0x1F0;
    if (no_ide) return;
    if (!d->present) return;
    /* Data register during a WRITE SECTORS transfer. The REP OUTSW fast path in
       handle_io reaches ide_write_data directly; a driver that writes the data
       phase with single 16-bit OUTs (port-out-16) lands here instead, and reg 0
       was dropped -- so single-OUT writes silently did nothing. */
    if (reg == 0) { ide_write_data(d, val & 0xFFFF); return; }
    if (reg == 2) d->sect_count = val & 0xFF;
    else if (reg == 3) d->lba_lo = val & 0xFF;
    else if (reg == 4) d->lba_mid = val & 0xFF;
    else if (reg == 5) d->lba_hi = val & 0xFF;
    else if (reg == 6) d->drive_head = val & 0xFF;
    else if (reg == 7) {
        if (val == 0x20) ide_start_read(d);
        else if (val == 0x30) ide_start_write(d);
        else if (val == 0xEC) ide_start_identify(d);
        else { d->writing = 0; d->identing = 0; d->status = 0x50; } /* flush (0xE7/0xEA) and others -> DRDY */
    }
}

/* The task-file registers read back what was written. This is not a
   convenience: LBA-mid and LBA-high reading 0x00/0x00 after a drive select
   IS the ATA device signature, and it is how a driver detects the drive at
   all. Returning 0xFF here is the floating-bus "no drive" answer, so the
   guest's textbook detect bailed before it ever issued IDENTIFY -- the sector
   count stayed 0 on a disk whose sectors read perfectly (the write path had
   always stored these registers; only the read path dropped them). */
static int ide_handle_in(IdeState *d, int port) {
    if (no_ide) return 0x01; /* ERR, no BSY/DRQ: drivers bail fast */
    /* A position with no medium. The signature registers float to 0xFF, which
       is what the guest's detect reads as "no drive" -- it compares LBA-mid
       against zero and branches away. But STATUS answers 0x00, not 0xFF, and
       the difference is not cosmetic: emit-ata-bring-up runs a bounded BSY wait
       BEFORE it looks at the signature, so a status with bit 7 set costs a
       million port INs, and a port IN is a VM exit. That is seconds of wall
       clock on every diskless boot, which is most of them -- it took the
       cross-arch lane over its timeout the first time this was tried. 0x00 is
       also what the guest tests for explicitly as no-drive, so it is the honest
       answer as well as the fast one. */
    if (!d->present) return (port == 0x3F6 || port == 0x1F7) ? 0x00 : 0xFF;
    if (port == 0x3F6) return d->status;
    int reg = port - 0x1F0;
    if (reg == 7) return d->status;
    if (reg == 1) return d->error;
    if (reg == 0) return ide_read_data(d);
    if (reg == 2) return d->sect_count;
    if (reg == 3) return d->lba_lo;
    if (reg == 4) return d->lba_mid;
    if (reg == 5) return d->lba_hi;
    if (reg == 6) return d->drive_head;
    return 0xFF;
}

/* The primary channel's two positions behind one pair of port handlers. The
   drive/head register is the selector, so it is intercepted here rather than in
   ide_handle_out: it must land on both devices (it carries LBA 27:24) while
   only its bit 4 decides which device the following accesses reach. */
static void ide_bus_out(int port, int val) {
    if (no_ide) return;
    if (port == 0x1F6) {
        ide_sel = (val >> 4) & 1;
        ide.drive_head = val & 0xFF;
        ide_slave.drive_head = val & 0xFF;
        return;
    }
    ide_handle_out(ide_active(), port, val);
}

static int ide_bus_in(int port) { return ide_handle_in(ide_active(), port); }

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
            /* serial_irq_pending removed -- no serial */
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
    /* PML4 + PDPT + one PD per GB of guest RAM, 2 MB huge pages throughout.
       Real UEFI firmware identity-maps what it advertises, so this has to
       track -mem rather than sit at a constant.

       It was 2 GB once and that was unfaithful: a correct Option A stub that
       built its own page tables in allocated high memory faulted writing
       them. Raising it to a fixed 4 GB moved the threshold without removing
       the class. Measured 2026-08-08: GET_MEMMAP above advertises
       conventional RAM at 0x100000000 and up whenever -mem exceeds 4 GB, and
       AllocateAnyPages hands out top-down from guest_mem_size - 1 MB, so at
       -mem 8192 the emulator returned 0x1f7f00000 from its own allocator and
       then triple-faulted the guest on the address it had just handed over.
       Advertising memory you do not map is the defect; the allocator and the
       memory map were both right.

       Minimum 4 GB so the device windows and the GOP framebuffer stay mapped
       when -mem is small. */
    unsigned long long map_gb = (guest_mem_size + 0x3FFFFFFFULL) >> 30;
    if (map_gb < 4) map_gb = 4;
    if (map_gb > UEFI_MAP_MAX_GB) {
        /* Loud rather than silent: this is the exact shape of the bug above,
           so it must never be reintroduced quietly at a higher threshold. */
        fprintf(stderr, "UEFI: identity map covers %d GB but guest RAM is %llu GB; "
                        "memory at or above 0x%llx is ADVERTISED AND NOT MAPPED\n",
                UEFI_MAP_MAX_GB, map_gb, (unsigned long long)UEFI_MAP_MAX_GB << 30);
        map_gb = UEFI_MAP_MAX_GB;
    }
    int pd_count = (int)map_gb;
    /* PML4 + PDPT + pd_count PDs, then one spare page for strict mode's 4 KB PT. */
    memset(pt, 0, (size_t)(2 + pd_count + 1) * 4096);
    /* PML4[0] -> PDPT */
    *(unsigned long long*)(pt) = (PAGE_TABLE_ADDR + 4096) | 3;
    /* PDPT[0..pd_count-1] -> PD0.. (one per GB) */
    for (int g = 0; g < pd_count; g++)
        *(unsigned long long*)(pt + 4096 + g*8) = (PAGE_TABLE_ADDR + (2 + g) * 4096) | 3;
    /* PDs: pd_count * 512 x 2 MB huge pages, identity */
    for (int i = 0; i < pd_count * 512; i++)
        *(unsigned long long*)(pt + 8192 + (size_t)i*8) = ((unsigned long long)i * 0x200000) | 0x83;

    if (uefi_strict) {
        /* Split the first 2 MB (PD0[0]) into 4 KB pages so firmware-owned low
           memory can be marked NOT-PRESENT. Real UEFI firmware owns all of low
           memory before ExitBootServices; a UEFI app that writes to a fixed low
           address it never allocated (this project's stub writes the SystemTable
           pointer to 0x8000 and copies the compiler to 0x100000) faults on real
           hardware. Here that becomes a clean, VM-reproducible #PF instead of a
           board-dependent coin-flip. Only the structures the CPU and our fake
           firmware legitimately expose stay present:
             0xA000-0xBFFF  GDT + TSS (0xA000/0xA100) and IDT (0xB000)
             0xF0000-0xF1FFF SystemTable/BootServices tables + HLT trap page
           Everything else in [0, 0x200000) -- including 0x8000 and 0x100000 --
           is not present. A correct kernel stub allocates its memory, calls
           ExitBootServices, and only then owns low memory (a future strict-mode
           refinement will flip these present on ExitBootServices). */
        unsigned long long *pt0 = (unsigned long long*)(pt + (size_t)(2 + pd_count)*4096); /* the page past PML4+PDPT+PDs */
        for (int i = 0; i < 512; i++) {
            unsigned long long pa = (unsigned long long)i * 0x1000;
            int present = 0;
            if (pa >= 0xA000 && pa < 0xC000) present = 1;                              /* GDT+TSS+IDT */
            else if (pa >= UEFI_TABLE_PAGE && pa < UEFI_TRAP_PAGE + 0x1000) present = 1; /* tables+trap */
            pt0[i] = present ? (pa | 3) : 0;
        }
        /* PD0[0] now references the 4KB PT (PS bit clear) instead of a 2MB huge page. */
        *(unsigned long long*)(pt + 8192 + 0) = (PAGE_TABLE_ADDR + (unsigned long long)(2 + pd_count)*4096) | 3;
    }
}

/* ── WHP setup ─────────────────────────────────────────────────────── */

static void create_vm(size_t mem_mb) {
    HRESULT hr;
    guest_mem_size = mem_mb * 1024ULL * 1024ULL;
    if (guest_mem_size > MAX_MEM) guest_mem_size = MAX_MEM;

    whp_lock();
    hr = WHvCreatePartition(&partition);
    if (FAILED(hr)) { fprintf(stderr, "WHvCreatePartition: 0x%lx\n", hr); exit(1); }

    WHV_PARTITION_PROPERTY prop;
    memset(&prop, 0, sizeof(prop));
    prop.ProcessorCount = smp_cores > 1 ? (smp_cores > SMP_MAX_CORES ? SMP_MAX_CORES : smp_cores) : 1;
    WHvSetPartitionProperty(partition, WHvPartitionPropertyCodeProcessorCount, &prop, sizeof(prop));

    /* Enable I/O port and CPUID exits. MSR exits handled selectively.
       ExceptionExit is enabled ONLY for -wcet and -hbreak runs: without this
       bit the ExceptionExitBitmap below is inert and every exception is
       delivered to the guest IDT (the !EXC dump path). Host-intercepting #PF
       would break guest-side demand paging, so both modes narrow the bitmap
       to #DB alone and normal runs keep today's behavior untouched.

       This is also why a -debug conditional breakpoint never discriminated:
       with the bit off, the guest's own vector-3 handler runs first and any
       condition is evaluated against ITS registers. */
    memset(&prop, 0, sizeof(prop));
    prop.ExtendedVmExits.X64CpuidExit = 1;
    prop.ExtendedVmExits.X64MsrExit = 1;
    if (wcet_name_count > 0 || hbreak_spec_count > 0 || hw_watch_active)
        prop.ExtendedVmExits.ExceptionExit = 1;
    WHvSetPartitionProperty(partition, WHvPartitionPropertyCodeExtendedVmExits, &prop, sizeof(prop));

    hr = WHvSetupPartition(partition);
    if (FAILED(hr)) { fprintf(stderr, "WHvSetupPartition: 0x%lx\n", hr); exit(1); }

    /* Enable exception exit for debug/breakpoint vectors (must be after setup) */
    memset(&prop, 0, sizeof(prop));
    if (wcet_name_count > 0 || hbreak_spec_count > 0 || hw_watch_active)
        prop.ExceptionExitBitmap = (1ULL << 1); /* #DB only: DR entry hits + TF steps */
    else
    prop.ExceptionExitBitmap = (1ULL << 0) | (1ULL << 1) | (1ULL << 3) | (1ULL << 6) | (1ULL << 8)
                             | (1ULL << 10) | (1ULL << 11) | (1ULL << 12) | (1ULL << 13) | (1ULL << 14)
                             | (1ULL << 16) | (1ULL << 17) | (1ULL << 19);
    /* #DE,#DB,#BP,#UD,#DF,#TS,#NP,#SS,#GP,#PF,#MF,#AC,#XM. Intercept all
       hardware fault vectors so a guest fault surfaces its RIP via the
       exception VM-exit (dbg_crash_report), instead of being swallowed by
       the guest IDT handler which just HLTs (hiding the real fault site,
       e.g. a #DE/#GP from poison 0xCD data). NMI(2) and reserved(9,15) are
       left to the guest. */
    hr = WHvSetPartitionProperty(partition, WHvPartitionPropertyCodeExceptionExitBitmap, &prop, sizeof(prop.ExceptionExitBitmap));
    if (FAILED(hr)) fprintf(stderr, "WARNING: ExceptionExitBitmap failed: 0x%lx\n", hr);

    guest_mem = VirtualAlloc(NULL, guest_mem_size, MEM_RESERVE, PAGE_READWRITE);
    if (!guest_mem) die("VirtualAlloc(reserve)");

    /* Commit and map in 2MB chunks.  Pre-commit the first 32MB (low memory,
       page tables, kernel load area, serial ring, UEFI PE load area at
       0x1000000) and the top 2MB (stack).
       The main loop commits additional chunks on demand when the guest
       touches unmapped GPA ranges. */
    {
        size_t chunk = 2ULL * 1024 * 1024;
        size_t pre_lo = 32ULL * 1024 * 1024;
        if (pre_lo > guest_mem_size) pre_lo = guest_mem_size;
        if (!VirtualAlloc(guest_mem, pre_lo, MEM_COMMIT, PAGE_READWRITE))
            die("VirtualAlloc(commit low)");
        hr = WHvMapGpaRange(partition, guest_mem, 0, pre_lo,
            WHvMapGpaRangeFlagRead | WHvMapGpaRangeFlagWrite | WHvMapGpaRangeFlagExecute);
        if (FAILED(hr)) { fprintf(stderr, "WHvMapGpaRange(low): 0x%lx\n", hr); exit(1); }

        if (guest_mem_size > pre_lo) {
            size_t stack_base = guest_mem_size - chunk;
            if (stack_base >= pre_lo) {
                if (!VirtualAlloc((unsigned char *)guest_mem + stack_base, chunk, MEM_COMMIT, PAGE_READWRITE))
                    die("VirtualAlloc(commit stack)");
                hr = WHvMapGpaRange(partition, (unsigned char *)guest_mem + stack_base,
                    stack_base, chunk,
                    WHvMapGpaRangeFlagRead | WHvMapGpaRangeFlagWrite | WHvMapGpaRangeFlagExecute);
                if (FAILED(hr)) { fprintf(stderr, "WHvMapGpaRange(stack): 0x%lx\n", hr); exit(1); }
            }
        }
    }

    hr = WHvCreateVirtualProcessor(partition, 0, 0);
    if (FAILED(hr)) { fprintf(stderr, "WHvCreateVirtualProcessor: 0x%lx\n", hr); exit(1); }
    memset(&lapic_state, 0, sizeof(lapic_state));
    lapic_state.ap_count = smp_cores > 1 ? smp_cores - 1 : 0;
    lapic_state.ap_running[0] = 1;
    lapic_state.sivr = 0xFF;
    if (smp_cores > 1) {
        /* Enable BSP LAPIC via APIC_BASE MSR so WHP delivers
           PendingInterruption in multi-VP mode. */
        WHV_REGISTER_NAME apic_name = WHvX64RegisterApicBase;
        WHV_REGISTER_VALUE apic_val;
        memset(&apic_val, 0, sizeof(apic_val));
        apic_val.Reg64 = 0xFEE00800ULL;  /* base + global enable (bit 11) */
        WHvSetVirtualProcessorRegisters(partition, 0, &apic_name, 1, &apic_val);
        fprintf(stderr, "SMP: enabled, %d cores (%d APs)\n", smp_cores, lapic_state.ap_count);
    }
    whp_unlock();

    if (uefi_mode) {
        uefi_alloc_hi = guest_mem_size - 0x100000; /* top-down allocator starts below top 1MB */
        uefi_setup_tables(guest_mem);
        /* Fill trap page with HLT (0xF4) opcodes -- each UEFI function is at a known offset.
           When the guest CALLs a function, it executes HLT. The VM checks RIP on halt. */
        memset((unsigned char *)guest_mem + UEFI_TRAP_PAGE, 0xF4, 4096);
        fprintf(stderr, "UEFI mode: tables at 0x%x, traps at 0x%x%s\n", UEFI_TABLE_PAGE, UEFI_TRAP_PAGE,
            uefi_strict ? " [STRICT: firmware owns low memory, AllocateAddress honored]" : "");
    }
    /* Auto-activate GOP framebuffer if requested. The guest writes pixels
       directly to GOP_FB_ADDR using poke-32; the VM renders them.
       Window resize happens in vga_thread after creation (checks gop_active). */
    if (gop_active) {
        if (!gop_fb) gop_fb = (unsigned char *)calloc(1, GOP_FB_SIZE);
        size_t fb_bytes = (size_t)gop_stride * gop_height * 4;
        size_t fb_pages = (fb_bytes + 4095) & ~(size_t)4095;
        /* Commit GPU command buffer + depth buffer + GOP framebuffer */
        size_t gpu_region_start = 0xBE000000ULL;
        size_t gpu_region_end = GOP_FB_ADDR + fb_pages;
        size_t gpu_region_size = gpu_region_end - gpu_region_start;
        if (gpu_region_end <= guest_mem_size) {
            if (!VirtualAlloc((unsigned char *)guest_mem + gpu_region_start, gpu_region_size, MEM_COMMIT, PAGE_READWRITE))
                die("VirtualAlloc(commit GPU region)");
            hr = WHvMapGpaRange(partition, (unsigned char *)guest_mem + gpu_region_start,
                gpu_region_start, gpu_region_size,
                WHvMapGpaRangeFlagRead | WHvMapGpaRangeFlagWrite | WHvMapGpaRangeFlagExecute);
            if (FAILED(hr)) fprintf(stderr, "WARNING: WHvMapGpaRange(GPU region): 0x%lx\n", hr);
            memset((unsigned char *)guest_mem + gpu_region_start, 0, gpu_region_size);
        }
        fprintf(stderr, "GOP: %dx%d framebuffer at 0x%llx\n", gop_width, gop_height, (unsigned long long)GOP_FB_ADDR);
    }

    /* The GPU compute bridge (COM3 doorbell) puts its command and reply
       buffers in the 16MB slab at 0xBD000000..0xBE000000, just below the
       rasterizer's own buffer. Unlike ordinary heap the host writes the
       reply there DIRECTLY, so it cannot rely on the guest having touched
       the page first to demand-commit it -- the guest only reads the reply
       AFTER the host writes it. Commit and map the whole slab up front, so
       it is genuinely present from boot as the bridge assumes. */
    {
        size_t com3_start = 0xBD000000ULL;
        size_t com3_end   = 0xBE000000ULL;
        if (com3_end <= guest_mem_size) {
            if (!VirtualAlloc((unsigned char *)guest_mem + com3_start,
                              com3_end - com3_start, MEM_COMMIT, PAGE_READWRITE))
                die("VirtualAlloc(commit COM3 region)");
            hr = WHvMapGpaRange(partition, (unsigned char *)guest_mem + com3_start,
                com3_start, com3_end - com3_start,
                WHvMapGpaRangeFlagRead | WHvMapGpaRangeFlagWrite | WHvMapGpaRangeFlagExecute);
            if (FAILED(hr)) fprintf(stderr, "WARNING: WHvMapGpaRange(COM3 region): 0x%lx\n", hr);
        }
    }

    if (board_mmio) {
        for (int i = 0; i < BOARD_MMIO_REGIONS; i++) {
            unsigned long long base = board_mmio_map[i].base;
            size_t size = board_mmio_map[i].size;
            void *host = VirtualAlloc(NULL, size, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
            if (!host) die("VirtualAlloc(board mmio)");
            board_mmio_host[i] = host;
            hr = WHvMapGpaRange(partition, host, base, size,
                WHvMapGpaRangeFlagRead | WHvMapGpaRangeFlagWrite);
            if (FAILED(hr)) {
                fprintf(stderr, "WHvMapGpaRange(board mmio 0x%llx): 0x%lx\n", base, hr);
                exit(1);
            }
            fprintf(stderr, "board-mmio: %s at 0x%llx (%llu KB)\n",
                board_mmio_map[i].what, base, (unsigned long long)(size / 1024));
        }
        fprintf(stderr, "board-mmio: HDA and xHCI BARs are shadowed by RAM; audio and USB are off.\n");
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

    /* Detect format and load accordingly */
    if (sz > 52 && buf[0] == 0x7F && buf[1] == 'E' && buf[2] == 'L' && buf[3] == 'F') {
        /* ELF32 multiboot kernel -- load PT_LOAD segments at their vaddrs */
        unsigned int e_entry = *(unsigned int*)(buf + 24);
        unsigned int phoff = *(unsigned int*)(buf + 28);
        unsigned short phnum = *(unsigned short*)(buf + 44);
        unsigned short phentsize = *(unsigned short*)(buf + 42);
        fprintf(stderr, "ELF: %d program headers at offset %d, entry=0x%x\n", phnum, phoff, e_entry);
        for (int i = 0; i < phnum; i++) {
            unsigned char *ph = buf + phoff + i * phentsize;
            unsigned int p_type = *(unsigned int*)(ph);
            if (p_type != 1) continue; /* PT_LOAD only */
            unsigned int p_offset = *(unsigned int*)(ph + 4);
            unsigned int p_vaddr = *(unsigned int*)(ph + 8);
            unsigned int p_filesz = *(unsigned int*)(ph + 16);
            if (p_vaddr + p_filesz <= guest_mem_size && p_offset + p_filesz <= sz) {
                memcpy((unsigned char*)guest_mem + p_vaddr, buf + p_offset, p_filesz);
                fprintf(stderr, "  LOAD: vaddr=0x%x filesz=%u\n", p_vaddr, p_filesz);
            }
        }
        /* Store ELF entry point for set_initial_regs */
        *(unsigned int*)((unsigned char*)guest_mem + 0x500) = e_entry;
    } else {
        size_t skip = 0;
        if (sz > 224 && buf[0] == 'C' && buf[1] == 'D' && buf[2] == 'X') {
            skip = 224;
            fprintf(stderr, "CDX header detected, skipping %zu bytes\n", skip);
        }
        size_t payload = sz - skip;
        /* The ELF and PE paths bounds-check their copies; this one did not, so an
           oversized CDX/raw kernel overran the serial ring, the page tables, and
           eventually host memory. */
        if ((unsigned long long)LOAD_ADDR + payload > guest_mem_size) {
            fprintf(stderr, "FATAL: kernel payload %zu at 0x%x exceeds guest RAM %llu\n",
                    payload, LOAD_ADDR, (unsigned long long)guest_mem_size);
            exit(1);
        }
        /* Fitting in guest RAM is not the same as being COMMITTED, and that
           gap crashed the host. Only the first 32 MB is committed up front,
           so a disk image past 31 MB (LOAD_ADDR + payload > 32 MB) ran this
           memcpy off the end of the commit into reserved address space:
           0xC0000005 on a write, before the guest executed an instruction and
           before any GPT diagnostic could print, which made it read as a fault
           in the directory walk below. Measured 2026-08-10: 32,505,856 bytes
           (LOAD_ADDR + size = exactly 32 MB) boots, and one 64 KB step past it
           faults. A disk image is the ordinary way to exceed this -- the ESPs
           the drive installer formats are well above 16 MB. */
        guest_commit_range(LOAD_ADDR, payload);
        memcpy((unsigned char*)guest_mem + LOAD_ADDR, buf + skip, payload);
    }

    /* Auto-extract the PE from a GPT disk image: find the ESP, locate
       BOOTX64.EFI. FAT16 and FAT32 both, which it was not.

       This read the BPB as FAT16 unconditionally: root entry count at offset
       17, sectors-per-FAT at 22, a cluster number as a bare 16-bit field at
       26. On FAT32 all three are wrong. The first two read zero there, the
       real FAT size is a 32-bit field at 36, the root is a CLUSTER named at
       44 rather than a fixed area before the data region, and a cluster
       number carries its high half at offset 20. So the walk found nothing
       and the caller fell through to loading the whole disk image as a raw
       kernel -- no diagnostic, no boot, indistinguishable from a blank disk.

       It mattered the moment the drive installer started working, because
       that formats the target ESP as FAT32, as it must for anything
       sizeable. A stick Codex had installed could not be booted by Codex's
       own firmware emulation while being perfectly readable by real
       firmware, and the emulator said nothing either way. */
    if (uefi_mode && sz > 1024 && buf[0] != 'M' &&
        buf[512] == 'E' && buf[513] == 'F' && buf[514] == 'I' && buf[515] == ' ') {
        unsigned long long part_lba = *(unsigned long long*)(buf + 1024 + 32);
        unsigned long long part_start = part_lba * 512;
        if (part_start + 512 < sz) {
            unsigned char *bpb = buf + part_start;
            unsigned int bps = *(unsigned short*)(bpb + 11);
            unsigned int spc = bpb[13];
            unsigned int reserved = *(unsigned short*)(bpb + 14);
            unsigned int nfats = bpb[16];
            unsigned int root_entries = *(unsigned short*)(bpb + 17);
            unsigned int spf = *(unsigned short*)(bpb + 22);
            /* Both zero is how the format itself says the 32-bit fields are
               the real ones. */
            int is_fat32 = (spf == 0 && root_entries == 0);
            unsigned int root_cluster = 0;
            if (is_fat32) {
                spf = *(unsigned int*)(bpb + 36);
                root_cluster = *(unsigned int*)(bpb + 44);
            }
            if (bps && spc && nfats && spf && !(is_fat32 && root_cluster < 2)) {
            unsigned long long root_off = part_start + (unsigned long long)(reserved + nfats * spf) * bps;
            /* FAT16 keeps a fixed root area ahead of the data region; FAT32's
               root is an ordinary cluster inside it. */
            unsigned long long data_off = root_off + (unsigned long long)root_entries * 32;
            unsigned int dir_entries = (unsigned int)(spc * bps / 32);
            if (is_fat32) root_off = data_off + (unsigned long long)(root_cluster - 2) * spc * bps;
            unsigned int root_scan = is_fat32 ? dir_entries : root_entries;
            /* Scan root directory for EFI subdir */
            unsigned int efi_cluster = 0;
            for (int i = 0; i < (int)root_scan && root_off + (i+1)*32 <= sz; i++) {
                unsigned char *e = buf + root_off + i * 32;
                if (e[0] == 0) break;
                if (e[11] == 0x10 && memcmp(e, "EFI        ", 11) == 0) {
                    efi_cluster = *(unsigned short*)(e + 26) | ((unsigned int)*(unsigned short*)(e + 20) << 16);
                    break;
                }
            }
            /* Scan EFI subdir for BOOT subdir */
            unsigned int boot_cluster = 0;
            if (efi_cluster >= 2) {
                unsigned long long dir_off = data_off + (unsigned long long)(efi_cluster - 2) * spc * bps;
                for (int i = 0; i < (int)dir_entries && dir_off + (i+1)*32 <= sz; i++) {
                    unsigned char *e = buf + dir_off + i * 32;
                    if (e[0] == 0) break;
                    if (e[11] == 0x10 && memcmp(e, "BOOT       ", 11) == 0) {
                        boot_cluster = *(unsigned short*)(e + 26) | ((unsigned int)*(unsigned short*)(e + 20) << 16);
                        break;
                    }
                }
            }
            /* Scan BOOT subdir for BOOTX64.EFI */
            if (boot_cluster >= 2) {
                unsigned long long dir_off = data_off + (unsigned long long)(boot_cluster - 2) * spc * bps;
                for (int i = 0; i < (int)dir_entries && dir_off + (i+1)*32 <= sz; i++) {
                    unsigned char *e = buf + dir_off + i * 32;
                    if (e[0] == 0) break;
                    if (memcmp(e, "BOOTX64 EFI", 11) == 0) {
                        unsigned int file_cluster = *(unsigned short*)(e + 26) | ((unsigned int)*(unsigned short*)(e + 20) << 16);
                        unsigned int file_size = *(unsigned int*)(e + 28);
                        unsigned long long file_off = data_off + (unsigned long long)(file_cluster - 2) * spc * bps;
                        /* Two bounds, and they are different questions. The
                           first says the file lies inside the image we read;
                           the second says it fits in guest RAM. Only the first
                           was checked, so a BOOTX64.EFI larger than guest RAM
                           would have run the copy below off the end -- found by
                           reek reading this path, and not the cause of the
                           crash he was chasing, which is upstream at the raw
                           copy. */
                        if (file_off + file_size <= sz && file_size > 64 &&
                            (unsigned long long)LOAD_ADDR + file_size <= guest_mem_size) {
                            fprintf(stderr, "GPT: extracted BOOTX64.EFI (%u bytes, %s) from partition at LBA %llu\n",
                                file_size, is_fat32 ? "FAT32" : "FAT16", part_lba);
                            /* Replace buf with the extracted PE */
                            unsigned char *pe = malloc(file_size);
                            memcpy(pe, buf + file_off, file_size);
                            free(buf);
                            buf = pe;
                            sz = file_size;
                            guest_commit_range(LOAD_ADDR, sz);
                            memcpy((unsigned char*)guest_mem + LOAD_ADDR, buf, sz);
                        }
                        break;
                    }
                }
            }
            /* The walk reads one cluster per directory and takes the file as
               contiguous from its first cluster. That is true of an ESP with a
               handful of entries and a freshly written payload, which is what
               this is for; it is not a general FAT driver and does not claim
               to be. A payload that outgrows one cluster chain's contiguity
               would need the FAT walked, and the failure would be a truncated
               PE rather than a silent miss. */
            } else {
                fprintf(stderr, "GPT: ESP at LBA %llu has an unusable BPB "
                    "(bps=%u spc=%u nfats=%u spf=%u root_cluster=%u)\n",
                    part_lba, bps, spc, nfats, spf, root_cluster);
            }
        }
    }

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
            uefi_image_base = load_base;
            uefi_image_size = (unsigned long long)sz;

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

            /* Populate Loaded Image Protocol with actual image details */
            unsigned char *li = (unsigned char*)guest_mem + UEFI_TABLE_PAGE + 0x880;
            unsigned long long li_base = load_base;
            unsigned long long li_size = (unsigned long long)sz;
            unsigned long long li_dev = 1; /* handle #1 = boot disk */
            memcpy(li + 40, &li_base, 8);  /* ImageBase */
            memcpy(li + 48, &li_size, 8);  /* ImageSize */
            *(int*)(li + 56) = 1;          /* ImageCodeType = EfiLoaderCode */
            *(int*)(li + 60) = 2;          /* ImageDataType = EfiLoaderData */
        }
        free(buf);
        fprintf(stderr, "Loaded PE %s (%zu bytes)\n", path, sz);
        return;
    }

    /* Parse multiboot header for entry point -- scan loaded memory at LOAD_ADDR */
    unsigned char *mb = (unsigned char*)guest_mem + LOAD_ADDR;
    if (*(unsigned int*)mb == 0x1BADB002) {
        unsigned int flags = *(unsigned int*)(mb + 4);
        if (flags & 0x10000) {
            unsigned int entry = *(unsigned int*)(mb + 28);
            fprintf(stderr, "Multiboot entry: 0x%x\n", entry);
            *(unsigned int*)((unsigned char*)guest_mem + 0x500) = entry;
        }
    }
    free(buf);
    fprintf(stderr, "Loaded %s at 0x%x (%zu bytes)\n", path, LOAD_ADDR, sz);
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
        /* EFER: LME + LMA + SCE.
           Keep the shadow in step. `handle_msr` answers a guest `rdmsr` of EFER
           out of msr_efer, NOT out of the VP, so leaving the shadow at 0 while
           the register really holds 0xD01 makes the read a lie -- and the read
           is only ever the first half of a read-modify-write. A guest that adds
           SCE the way every real kernel does (rdmsr; or rax,1; wrmsr) would read
           0, write 1, and clear LME and LMA while running in long mode with
           paging on. That is not a subtle degradation, it is an immediate
           triple fault, and it is what the UEFI stub hit the moment it started
           programming the syscall MSRs. */
        vals[6].Reg64 = 0xD01;
        msr_efer = 0xD01;

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
        uefi_vals[0].Reg64 = 2;            /* ImageHandle -- nonzero pseudo-handle.
                                              Real firmware never passes NULL, and the guest's
                                              block helpers treat a zero stashed handle as "no
                                              stub primed it" and skip the LoadedImage binding
                                              entirely; HandleProtocol above ignores the handle
                                              value, so 2 exercises the same chain the board
                                              runs (2, not 1, so a guest confusing ImageHandle
                                              with the boot-disk DeviceHandle would be caught
                                              rather than accidentally right). */
        uefi_vals[1].Reg64 = UEFI_TABLE_PAGE; /* SystemTable */
        uefi_vals[2].Table.Base = 0xA000;   /* GDT base */
        uefi_vals[2].Table.Limit = 39;      /* 5 entries * 8 - 1 (TSS is 16 bytes) */
        uefi_vals[3].Table.Base = 0xB000;   /* IDT base (empty) */
        uefi_vals[3].Table.Limit = 0xFFF;
        uefi_vals[4].Reg64 = 0x900000;      /* R10 = heap base */
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

    /* Demand builds reserve guest RAM and commit/map it lazily in 2MB chunks
       (see the demand-commit path). If the watched page's chunk is not yet
       committed+mapped, WHvUnmapGpaRange fails and the watch never arms, and
       a later demand-commit would remap the whole 2MB chunk WRITABLE, silently
       clobbering the read-only watch. Fix both: commit+map the whole chunk now
       so the demand-commit never touches it, then carve the 4KB page RO. */
    unsigned long long chunk = 2ULL * 1024 * 1024;
    unsigned long long cbase = (watch_page_base / chunk) * chunk;
    unsigned long long clen = chunk;
    if (cbase + clen > guest_mem_size) clen = guest_mem_size - cbase;
    VirtualAlloc((unsigned char*)guest_mem + cbase, (size_t)clen, MEM_COMMIT, PAGE_READWRITE);
    WHvUnmapGpaRange(partition, cbase, clen);   /* ignore failure: may be unmapped */
    WHvMapGpaRange(partition, (unsigned char*)guest_mem + cbase, cbase, clen,
        WHvMapGpaRangeFlagRead | WHvMapGpaRangeFlagWrite | WHvMapGpaRangeFlagExecute);

    memcpy(watch_prev, (unsigned char*)guest_mem + watch_addr, watch_size);
    watch_active = 1;
    fprintf(stderr, "WATCH: 0x%llx (%d bytes), page 0x%llx, chunk 0x%llx\n",
        watch_addr, watch_size, watch_page_base, cbase);
    if (watch_val_set) fprintf(stderr, "WATCH: value filter = 0x%llx\n", watch_val);
    fprintf(stderr, "WATCH: initial value=");
    for (int i = 0; i < watch_size; i++) fprintf(stderr, "%02x", watch_prev[i]);
    fprintf(stderr, "\n");

    /* Carve the watched 4KB page out of the chunk as read+execute only. */
    WHvUnmapGpaRange(partition, watch_page_base, 4096);
    HRESULT hr = WHvMapGpaRange(partition, (unsigned char*)guest_mem + watch_page_base,
        watch_page_base, 4096, WHvMapGpaRangeFlagRead | WHvMapGpaRangeFlagExecute);
    if (FAILED(hr)) { fprintf(stderr, "WHvMapGpaRange(RX): 0x%lx\n", hr); watch_active = 0; return; }
    fprintf(stderr, "WATCH: page 0x%llx set to READ-ONLY (write traps enabled)\n", watch_page_base);
}

/* Commit a guest range the HOST is about to store into.

   guest_mem is MEM_RESERVE with lazy commit, and the run loop's demand-commit
   is driven by a guest fault. A store performed by the host rather than the
   guest never produces that fault, so if the destination page has never been
   touched the store lands in reserved address space and takes the whole
   process down with 0xC0000005 -- no guest diagnostic and no emulator
   diagnostic, because nothing survives to write one.

   REP INSW is where that bites in practice. The guest's block-read-sector
   syscall allocates a 512-byte buffer at the top of the bump heap and reads
   the sector straight into it, so the destination is by construction memory
   the guest has not touched yet. It survives as long as the heap top stays
   inside a chunk something else already committed, which is why the failure
   needs a read AND a live accumulator: only a retained allocation marches the
   heap top into fresh chunks. codex/test/sector-read-list-growth is the pair.

   The seen-bitmap is not an optimisation detail. Without it this is two
   syscalls per 16-bit word of every sector read. */
static unsigned char *guest_chunk_seen = NULL;

static void guest_host_touch(unsigned long long gpa, unsigned long long len) {
    unsigned long long chunk = 2ULL * 1024 * 1024;
    if (!guest_mem || gpa >= guest_mem_size) return;
    if (!guest_chunk_seen) {
        guest_chunk_seen = (unsigned char *)calloc((size_t)(guest_mem_size / chunk + 2), 1);
        if (!guest_chunk_seen) return;
    }
    unsigned long long first = gpa / chunk;
    unsigned long long last = (gpa + (len ? len - 1 : 0)) / chunk;
    for (unsigned long long c = first; c <= last; c++) {
        unsigned long long base = c * chunk;
        if (base >= guest_mem_size) return;
        if (guest_chunk_seen[c]) continue;
        guest_chunk_seen[c] = 1;
        /* Leave the chunk carrying an armed watchpoint alone: watch_init
           committed it already and carved the page read-only, and a remap
           here would quietly hand write access back. */
        if (watch_active && watch_page_base / chunk == c) continue;
        unsigned long long clen = chunk;
        if (base + clen > guest_mem_size) clen = guest_mem_size - base;
        if (VirtualAlloc((unsigned char *)guest_mem + base, (size_t)clen,
                         MEM_COMMIT, PAGE_READWRITE)) {
            WHvMapGpaRange(partition, (unsigned char *)guest_mem + base, base, clen,
                WHvMapGpaRangeFlagRead | WHvMapGpaRangeFlagWrite | WHvMapGpaRangeFlagExecute);
        }
    }
}

static void hw_watch_init(void) {
    if (!hw_watch_active) return;
    /* DR7 LEN encoding: 00=1B, 01=2B, 11=4B, 10=8B */
    int lenbits = (hw_watch_len == 1) ? 0 : (hw_watch_len == 2) ? 1
                : (hw_watch_len == 8) ? 2 : 3;
    unsigned long long dr7 = (1ULL << 0)     /* L0: local enable DR0 */
        | (1ULL << 8)                        /* LE: local exact */
        | ((unsigned long long)(hw_watch_rw & 3) << 16)   /* R/W0 */
        | ((unsigned long long)(lenbits & 3) << 18);      /* LEN0 */
    WHV_REGISTER_NAME names[3] = { WHvX64RegisterDr0, WHvX64RegisterDr6, WHvX64RegisterDr7 };
    WHV_REGISTER_VALUE vals[3];
    memset(vals, 0, sizeof(vals));
    vals[0].Reg64 = hw_watch_addr;   /* DR0 = watched linear address */
    vals[1].Reg64 = 0;               /* DR6 = clear status */
    vals[2].Reg64 = dr7;
    HRESULT hr = WHvSetVirtualProcessorRegisters(partition, 0, names, 3, vals);
    fprintf(stderr, "HWWATCH: DR0=0x%llx DR7=0x%llx rw=%d len=%d (hr=0x%lx)\n",
        hw_watch_addr, dr7, hw_watch_rw, hw_watch_len, (unsigned long)hr);
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

    /* Walk the stack for return addresses. guest_mem is RESERVED for the
       full size but COMMITTED page-by-page on guest touch, so a host read
       past the last committed page access-violates and kills the whole
       dump (everything after "Stack:" silently vanished, including the
       guest's dying output). Probe commitment before each read. */
    unsigned long long rsp = vals[1].Reg64;
    fprintf(stderr, "Stack (code-range return addrs):\n");
    for (int i = 0; i < 32; i++) {
        unsigned long long saddr = rsp + i * 8;
        if (saddr + 8 > guest_mem_size) break;
        MEMORY_BASIC_INFORMATION mbi;
        if (!VirtualQuery((unsigned char*)guest_mem + saddr, &mbi, sizeof mbi) || mbi.State != MEM_COMMIT) break;
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

    /* The writing instruction's RIP is the RIP at this memory-access exit. */
    unsigned long long writer_rip = ctx->VpContext.Rip;

    /* Let the write execute: unprotect the page, single-step the faulting
       instruction via RFLAGS.TF (a real single-step; the old interrupt-window
       trick did NOT retire a store under demand, so it re-trapped forever),
       then re-protect. The TF #DB is host-intercepted (ExceptionExitBitmap
       bit 1); we clear TF after. */
    WHvUnmapGpaRange(partition, watch_page_base, 4096);
    WHvMapGpaRange(partition, (unsigned char*)guest_mem + watch_page_base,
        watch_page_base, 4096, WHvMapGpaRangeFlagRead | WHvMapGpaRangeFlagWrite | WHvMapGpaRangeFlagExecute);
    {
        WHV_REGISTER_NAME fn = WHvX64RegisterRflags;
        WHV_REGISTER_VALUE fv;
        WHvGetVirtualProcessorRegisters(partition, 0, &fn, 1, &fv);
        WHV_REGISTER_NAME names[2] = { WHvX64RegisterRflags, WHvRegisterInternalActivityState };
        WHV_REGISTER_VALUE vals[2];
        memset(vals, 0, sizeof(vals));
        vals[0].Reg64 = fv.Reg64 | 0x100ULL;   /* set TF */
        WHvSetVirtualProcessorRegisters(partition, 0, names, 2, vals);
        WHV_RUN_VP_EXIT_CONTEXT step_ctx;
        WHvRunVirtualProcessor(partition, 0, &step_ctx, sizeof(step_ctx));
        if (step_ctx.ExitReason == WHvRunVpExitReasonX64IoPortAccess) handle_io(&step_ctx);
        /* clear TF so we don't keep single-stepping the guest */
        WHvGetVirtualProcessorRegisters(partition, 0, &fn, 1, &fv);
        fv.Reg64 &= ~0x100ULL;
        WHvSetVirtualProcessorRegisters(partition, 0, &fn, 1, &fv);
    }

    /* Did the watched bytes change, and (if a value filter is set) to the
       value we care about? If so, report the writer. Then always re-protect
       and continue, so we can observe every writer, not just the first. */
    unsigned char *cur = (unsigned char*)guest_mem + watch_addr;
    int reported = 0;
    if (watch_report_all) {
        /* Guest-armed: log the raw write target (GPA) + writer RIP for every
           trapping write on the page, plus any changed 8-byte slot in the
           watched window. Shows exactly where the just-armed code writes. */
        if (watch_hit_count <= 400) {
            WHV_REGISTER_NAME sn[2] = { WHvX64RegisterRsp, WHvX64RegisterR10 };
            WHV_REGISTER_VALUE sv[2]; memset(sv, 0, sizeof(sv));
            WHvGetVirtualProcessorRegisters(partition, 0, sn, 2, sv);
            fprintf(stderr, "WATCH-PG #%d RIP=0x%llx GPA=0x%llx RSP=0x%llx R10=0x%llx\n",
                    watch_hit_count, writer_rip, gpa, sv[0].Reg64, sv[1].Reg64);
        }
        if (memcmp(cur, watch_prev, watch_size) != 0) {
            for (int s = 0; s + 8 <= watch_size; s += 8) {
                if (memcmp(cur + s, watch_prev + s, 8) != 0) {
                    unsigned long long nv = 0; memcpy(&nv, cur + s, 8);
                    fprintf(stderr, "WATCH-W #%d RIP=0x%llx +%d = 0x%llx\n",
                            watch_hit_count, writer_rip, s, nv);
                }
            }
            memcpy(watch_prev, cur, watch_size);
        }
        /* Bound the burst: the watched heap page is hot, so disarm after a short
           window (which covers the just-armed concat + the corrupting store) and
           let the page go RW again so the run completes instead of storming. */
        if (watch_hit_count >= 400) {
            WHvUnmapGpaRange(partition, watch_page_base, 4096);
            WHvMapGpaRange(partition, (unsigned char*)guest_mem + watch_page_base,
                watch_page_base, 4096,
                WHvMapGpaRangeFlagRead | WHvMapGpaRangeFlagWrite | WHvMapGpaRangeFlagExecute);
            watch_active = 0; watch_report_all = 0;
            fprintf(stderr, "GUEST-ARM WATCH: disarmed after %d writes\n", watch_hit_count);
            return 1;
        }
        WHvUnmapGpaRange(partition, watch_page_base, 4096);
        WHvMapGpaRange(partition, (unsigned char*)guest_mem + watch_page_base,
            watch_page_base, 4096, WHvMapGpaRangeFlagRead | WHvMapGpaRangeFlagExecute);
        return 1;
    }
    if (memcmp(cur, watch_prev, watch_size) != 0) {
        unsigned long long newval = 0, oldval = 0;
        int n = watch_size < 8 ? watch_size : 8;
        memcpy(&newval, cur, n);
        memcpy(&oldval, watch_prev, n);
        if (!watch_val_set || newval == watch_val) {
            fprintf(stderr, "\n=== WATCH WRITE #%d: writer RIP=0x%llx  [0x%llx] 0x%llx -> 0x%llx ===\n",
                watch_hit_count, writer_rip, watch_addr, oldval, newval);
            dump_guest_regs("writer state", gpa);
            reported = 1;
        }
        memcpy(watch_prev, cur, watch_size);
    }

    /* Re-protect the page RO for the next write. */
    WHvUnmapGpaRange(partition, watch_page_base, 4096);
    WHvMapGpaRange(partition, (unsigned char*)guest_mem + watch_page_base,
        watch_page_base, 4096, WHvMapGpaRangeFlagRead | WHvMapGpaRangeFlagExecute);

    /* With no value filter, stop on the first real write (old behaviour).
       With a value filter, keep running to catch every matching write. */
    if (reported && !watch_val_set) return 2;
    return 1;
}

/* forward declarations for GPU rasterizer */
static void gpu_clear_fb(int color);
static void gpu_fade_clear(int color);
static void gpu_clear_depth(void);
static void gpu_rasterize_triangles(int count);
static void gpu_rasterize_band(unsigned int *fb, unsigned int *db, unsigned char *cmd,
                                int w, int h, int count, int band_y0, int band_y1);
static void gpu_atmosphere_glow(void);

/* ── COM3 GPU compute bridge (0x3E8) ───────────────────────────────────

   codex/os/kernel/GpuBridge.codex drives a compute bridge over COM3 and
   until now had nothing on the other end: no handler existed for 0x3E8,
   so OUT bytes were discarded and an LSR read returned the 0xFF
   initializer -- which reads as "transmitter ready, receiver ready" -- so
   gpu-recv-u32 answered 0xFFFFFFFF, every gpu-bridge-matmul/relu returned
   None, and format-gpu-bridge reported bridge-ready = True the whole time.

   The wire format is the guest driver's, read off GpuBridge.codex: a
   command is little-endian u32s -- status, op, rows-a, cols-a, cols-b --
   followed by the operands as IEEE-754 f32. The reply is a status u32,
   then for matmul the result dims, then the result elements. relu does
   NOT echo dims; the guest reuses the operand's. That asymmetry is the
   driver's, not a simplification here.

   The header is self-delimiting: operand count follows from the dims, so
   a command completes without a length prefix or a terminator. That holds
   for every op except the PTX launch, which carries a program instead of
   a shape and so brings a header and a length of its own.

   All seventeen arithmetic ops are computed here now. Fifteen fit the
   shape header, reading their sizes from the three dimension fields:
   conv1d and max-pool take a length, a window/kernel and a stride, and
   clamp carries its lo/hi as two scalars after the data the way scale
   carries its factor. conv2d is the one whose descriptor -- in and out
   channels, spatial H and W, kernel H and W, stride and padding -- does
   not fit three fields, so like the PTX launch it brings a header of its
   own and is routed to com3_conv2d around com3_shape. An op with no
   branch at all consumes what its dims imply and answers gpu-status-error
   rather than a plausible-looking wrong answer. */

#define COM3_MAX_ELEMS  524288      /* per operand and per result; a 512x512
                                       square matmul (2*512^2 operand floats)
                                       is the largest that fits, chosen so a
                                       device dispatch can outrun the scalar
                                       loop's one-time context+JIT cost. */
#define COM3_HDR_BYTES  20          /* 5 u32: status, op, rows-a, cols-a, cols-b */
#define COM3_STATUS_COMPLETE 2
#define COM3_STATUS_ERROR    3

/* op 32 carries a program rather than a shape, so it has a header of its
   own: eight u32 instead of five, then the PTX text, then the kernel
   name, then the operand floats. See com3_launch_ptx below. */
#define COM3_PTX_HDR_BYTES 32
#define COM3_PTX_MAX       65536
#define COM3_PTX_NAME_MAX  256
#define COM3_OP_LAUNCH_PTX 32

/* conv2d (op 12) also carries more than three dimension fields -- in and
   out channels, spatial H and W, kernel H and W, stride and padding -- so
   it brings a header of its own the way the PTX launch does: ten u32
   (status, op, then the eight), then the input floats, then the kernel
   floats. See com3_conv2d below. */
#define COM3_CONV2D_HDR_BYTES 40
#define COM3_OP_CONV2D        12

static struct {
    unsigned char cmd[COM3_PTX_HDR_BYTES + COM3_PTX_MAX + COM3_PTX_NAME_MAX
                      + 2 * COM3_MAX_ELEMS * 4];
    unsigned char reply[12 + COM3_MAX_ELEMS * 4];
    int reply_len;
} com3;

/* Every guest access to the COM3 window is one VM exit. Counting them is
   the only honest measure of what this transport costs, because the
   per-byte serial protocol makes the count a function of the OPERAND SIZE
   rather than of the dispatch. Opt-in: set CODEX_VM_COM3_STAT.
   Test the CONTENT and not the pointer -- an empty-string env var is
   still a non-NULL getenv, which is how a toggle comes to read as set on
   both legs of a measurement. */
static unsigned long long com3_io_exits = 0;

static void com3_stat(const char *phase) {
    const char *p = getenv("CODEX_VM_COM3_STAT");
    if (!(p && p[0])) return;
    fprintf(stderr, "COM3-EXITS: %s %llu\n", phase, com3_io_exits);
}

static unsigned int com3_get_u32(const unsigned char *p) {
    return (unsigned int)p[0] | ((unsigned int)p[1] << 8)
         | ((unsigned int)p[2] << 16) | ((unsigned int)p[3] << 24);
}

static void com3_put_u32(unsigned int v) {
    if (com3.reply_len + 4 > (int)sizeof(com3.reply)) return;
    com3.reply[com3.reply_len++] = (unsigned char)(v & 0xFF);
    com3.reply[com3.reply_len++] = (unsigned char)((v >> 8) & 0xFF);
    com3.reply[com3.reply_len++] = (unsigned char)((v >> 16) & 0xFF);
    com3.reply[com3.reply_len++] = (unsigned char)((v >> 24) & 0xFF);
}

static float com3_get_f32(const unsigned char *p) {
    unsigned int raw = com3_get_u32(p);
    float f;
    memcpy(&f, &raw, sizeof(f));
    return f;
}

static void com3_put_f32(float f) {
    unsigned int raw;
    memcpy(&raw, &f, sizeof(raw));
    com3_put_u32(raw);
}

/* Operand and result element counts for a header. Answers 0 when the dims
   cannot be served (overflow, or larger than the bridge buffers) so the
   caller answers error instead of running off the end of cmd[]. The
   products are formed in double first, because ra*ca in unsigned int
   wraps silently and a wrapped count would pass a size check. */
static int com3_shape(unsigned int op, unsigned int ra, unsigned int ca,
                      unsigned int cb, int *operand_elems, int *result_elems) {
    double a_sz = (double)ra * (double)ca;
    double b_sz = (double)ca * (double)cb;
    double r_sz = (double)ra * (double)cb;

    if (op == 0) {                               /* matmul */
        if (a_sz + b_sz > COM3_MAX_ELEMS || r_sz > COM3_MAX_ELEMS) return 0;
        *operand_elems = (int)(a_sz + b_sz);
        *result_elems  = (int)r_sz;
        return 1;
    }
    /* Elementwise unary over ra*ca: relu, softmax, layer-norm, gelu, silu.
       Softmax is a reduction and the rest are pointwise, but they agree on
       shape, which is all this function decides. */
    if (op == 2 || op == 3 || op == 8 || op == 10 || op == 14) {
        if (a_sz > COM3_MAX_ELEMS) return 0;
        *operand_elems = (int)a_sz;
        *result_elems  = (int)a_sz;
        return 1;
    }
    /* Elementwise binary: two operands of the same length, one result. */
    if (op == 5 || op == 6) {
        if (a_sz * 2 > COM3_MAX_ELEMS) return 0;
        *operand_elems = (int)(a_sz * 2);
        *result_elems  = (int)a_sz;
        return 1;
    }
    /* Transpose keeps the element count and swaps the shape. */
    if (op == 7) {
        if (a_sz > COM3_MAX_ELEMS) return 0;
        *operand_elems = (int)a_sz;
        *result_elems  = (int)a_sz;
        return 1;
    }
    /* Scale: the vector, then ONE scalar after it. */
    if (op == 11) {
        if (a_sz + 1 > COM3_MAX_ELEMS) return 0;
        *operand_elems = (int)a_sz + 1;
        *result_elems  = (int)a_sz;
        return 1;
    }
    /* Group norm reads its dimensions differently from every neighbour
       here: gpu-cmd-group-norm puts the LENGTH in rows-a and the GROUP
       COUNT in cols-a, so ra*ca is not the element count. A length that
       does not divide into whole groups is refused rather than rounded. */
    if (op == 13) {
        if (ra > COM3_MAX_ELEMS || ca == 0 || (ra % ca) != 0) return 0;
        *operand_elems = (int)ra;
        *result_elems  = (int)ra;
        return 1;
    }
    /* Upsample 2x: channels x h x w in, channels x 2h x 2w out. */
    if (op == 15) {
        double n = (double)ra * (double)ca * (double)cb;
        if (n > COM3_MAX_ELEMS || n * 4 > COM3_MAX_ELEMS) return 0;
        *operand_elems = (int)n;
        *result_elems  = (int)(n * 4);
        return 1;
    }
    /* conv1d (op 4), valid 1D cross-correlation: ra is the input length,
       ca the kernel length, cb the stride. Two operands -- input then
       kernel -- so the operand count is ra + ca, not a product. */
    if (op == 4) {
        double total = (double)ra + (double)ca;
        if (ca == 0 || cb == 0 || ra < ca || total > COM3_MAX_ELEMS) return 0;
        *operand_elems = (int)total;
        *result_elems  = (int)((ra - ca) / cb + 1);
        return 1;
    }
    /* max-pool (op 9), valid 1D: ra is the input length, ca the window,
       cb the stride. One operand of ra floats. */
    if (op == 9) {
        if (ca == 0 || cb == 0 || ra < ca || ra > COM3_MAX_ELEMS) return 0;
        *operand_elems = (int)ra;
        *result_elems  = (int)((ra - ca) / cb + 1);
        return 1;
    }
    /* clamp (op 16): ra*ca values, then TWO scalars lo and hi after them,
       the way scale carries its one factor. */
    if (op == 16) {
        if (a_sz + 2 > COM3_MAX_ELEMS) return 0;
        *operand_elems = (int)a_sz + 2;
        *result_elems  = (int)a_sz;
        return 1;
    }
    /* Unserved op: consume what its dims imply so the byte stream stays
       in step, then answer error. */
    if (a_sz + b_sz > COM3_MAX_ELEMS) return 0;
    *operand_elems = (int)(a_sz + (cb ? b_sz : 0));
    *result_elems  = 0;
    return 1;
}

/* ── The GPU, actually ─────────────────────────────────────────────────

   Everything else in this file computes the bridge's arithmetic on the
   host CPU in scalar C. That was honest as a transport exercise and
   dishonest as a GPU: a caller reaching for the bridge to go faster got
   a slower CPU with a round trip in front of it.

   This is the other half. `gpu-op-launch-ptx` hands over a PTX module
   and a kernel name, and the kernel is JITted and launched on the real
   device through the CUDA DRIVER API. The driver API is chosen over the
   runtime API deliberately: `nvcuda.dll` ships with every NVIDIA display
   driver, so there is no CUDA toolkit to install and nothing to link
   against. It is loaded with LoadLibrary at first use and its absence is
   an answer rather than a crash -- a box with no NVIDIA GPU still builds
   and boots this emulator, and a launch on it is REFUSED rather than
   quietly served on the CPU. That distinction is the whole point: a
   caller must be able to tell a GPU that ran its kernel from a CPU
   pretending to.

   THE KERNEL ABI IS FIXED AND IT IS STATED IN BOTH HALVES, because a
   caller cannot recover a calling convention by looking at an answer.
   A launched kernel takes exactly three parameters, in this order:

       .param .u64 in     -- the operand buffer, n_in floats
       .param .u64 out    -- the result buffer, n_out floats
       .param .u32 n      -- n_out, the number of RESULTS

   The bound is the output count and not the input count, and the grid is
   sized to cover it, because a kernel's job is to fill `out`. For a
   mapping kernel the two are equal and the choice does not show; for a
   kernel that writes fewer results than it reads it is the difference
   between a guarded kernel and one that walks off the end.

   A general argument-passing scheme would be a protocol with a
   descriptor nobody has designed, which is the same wall the four
   unserved ops stand behind. One stated convention is worth more here
   than a flexible one that has to be guessed at. */

typedef int CUresult_t;
typedef int CUdevice_t;
typedef void *CUcontext_t;
typedef void *CUmodule_t;
typedef void *CUfunction_t;
typedef unsigned long long CUdeviceptr_t;

typedef CUresult_t (*pfn_cuInit)(unsigned int);
typedef CUresult_t (*pfn_cuDeviceGet)(CUdevice_t *, int);
typedef CUresult_t (*pfn_cuDeviceGetName)(char *, int, CUdevice_t);
typedef CUresult_t (*pfn_cuCtxCreate)(CUcontext_t *, unsigned int, CUdevice_t);
typedef CUresult_t (*pfn_cuModuleLoadData)(CUmodule_t *, const void *);
typedef CUresult_t (*pfn_cuModuleGetFunction)(CUfunction_t *, CUmodule_t, const char *);
typedef CUresult_t (*pfn_cuModuleUnload)(CUmodule_t);
typedef CUresult_t (*pfn_cuMemAlloc)(CUdeviceptr_t *, size_t);
typedef CUresult_t (*pfn_cuMemFree)(CUdeviceptr_t);
typedef CUresult_t (*pfn_cuMemcpyHtoD)(CUdeviceptr_t, const void *, size_t);
typedef CUresult_t (*pfn_cuMemcpyDtoH)(void *, CUdeviceptr_t, size_t);
typedef CUresult_t (*pfn_cuLaunchKernel)(CUfunction_t, unsigned, unsigned, unsigned,
                                         unsigned, unsigned, unsigned,
                                         unsigned, void *, void **, void **);
typedef CUresult_t (*pfn_cuCtxSynchronize)(void);
typedef CUresult_t (*pfn_cuGetErrorString)(CUresult_t, const char **);

static struct {
    int state;                      /* 0 untried, 1 ready, -1 unavailable */
    HMODULE lib;
    CUcontext_t ctx;
    char device_name[128];
    pfn_cuInit init;
    pfn_cuDeviceGet device_get;
    pfn_cuDeviceGetName device_get_name;
    pfn_cuCtxCreate ctx_create;
    pfn_cuModuleLoadData module_load;
    pfn_cuModuleGetFunction module_get_fn;
    pfn_cuModuleUnload module_unload;
    pfn_cuMemAlloc mem_alloc;
    pfn_cuMemFree mem_free;
    pfn_cuMemcpyHtoD memcpy_htod;
    pfn_cuMemcpyDtoH memcpy_dtoh;
    pfn_cuLaunchKernel launch;
    pfn_cuCtxSynchronize sync;
    pfn_cuGetErrorString err_string;
    /* One-module cache. A JIT of a small kernel is milliseconds, which is
       a thousand times the dispatch this bridge exists to make cheap, so
       relaunching the same kernel must not pay for it twice. Keyed on the
       PTX bytes and the kernel name together, because the same text can
       be asked for a different entry point. */
    unsigned char *cached_ptx;
    unsigned int cached_ptx_len;
    char cached_name[COM3_PTX_NAME_MAX];
    CUmodule_t cached_module;
    CUfunction_t cached_fn;
} cuda;

static const char *cuda_err(CUresult_t r) {
    const char *s = 0;
    if (cuda.err_string && cuda.err_string(r, &s) == 0 && s) return s;
    return "(no description)";
}

/* Answers 1 when a real device is behind this and 0 when there is not.
   Reports the reason exactly once, because a guest that keeps asking
   should not fill the log with the same absence. */
static int cuda_ready(void) {
    CUresult_t r;
    CUdevice_t dev = 0;

    if (cuda.state) return cuda.state == 1;
    cuda.state = -1;

    cuda.lib = LoadLibraryA("nvcuda.dll");
    if (!cuda.lib) {
        fprintf(stderr, "CUDA: nvcuda.dll not present -- no GPU on this box, "
                        "PTX launches will be refused\n");
        return 0;
    }

#define CUDA_SYM(field, name)                                                  \
    do {                                                                       \
        cuda.field = (void *)GetProcAddress(cuda.lib, name);                   \
        if (!cuda.field) {                                                     \
            fprintf(stderr, "CUDA: nvcuda.dll has no %s\n", name);             \
            return 0;                                                          \
        }                                                                      \
    } while (0)

    CUDA_SYM(init,            "cuInit");
    CUDA_SYM(device_get,      "cuDeviceGet");
    CUDA_SYM(device_get_name, "cuDeviceGetName");
    CUDA_SYM(ctx_create,      "cuCtxCreate_v2");
    CUDA_SYM(module_load,     "cuModuleLoadData");
    CUDA_SYM(module_get_fn,   "cuModuleGetFunction");
    CUDA_SYM(module_unload,   "cuModuleUnload");
    CUDA_SYM(mem_alloc,       "cuMemAlloc_v2");
    CUDA_SYM(mem_free,        "cuMemFree_v2");
    CUDA_SYM(memcpy_htod,     "cuMemcpyHtoD_v2");
    CUDA_SYM(memcpy_dtoh,     "cuMemcpyDtoH_v2");
    CUDA_SYM(launch,          "cuLaunchKernel");
    CUDA_SYM(sync,            "cuCtxSynchronize");
#undef CUDA_SYM
    /* Optional: only used to make an error message readable. */
    cuda.err_string = (pfn_cuGetErrorString)(void *)GetProcAddress(cuda.lib, "cuGetErrorString");

    if ((r = cuda.init(0)) != 0) {
        fprintf(stderr, "CUDA: cuInit failed: %s\n", cuda_err(r));
        return 0;
    }
    if ((r = cuda.device_get(&dev, 0)) != 0) {
        fprintf(stderr, "CUDA: no device 0: %s\n", cuda_err(r));
        return 0;
    }
    if ((r = cuda.ctx_create(&cuda.ctx, 0, dev)) != 0) {
        fprintf(stderr, "CUDA: cuCtxCreate failed: %s\n", cuda_err(r));
        return 0;
    }
    cuda.device_name[0] = 0;
    cuda.device_get_name(cuda.device_name, (int)sizeof(cuda.device_name), dev);
    fprintf(stderr, "CUDA: %s\n", cuda.device_name[0] ? cuda.device_name : "device 0");
    cuda.state = 1;
    return 1;
}

/* Resolve the kernel, reusing the cached module when the same PTX and the
   same entry point are asked for again. Answers 0 on success. */
static int cuda_get_function(const unsigned char *ptx, unsigned int ptx_len,
                             const char *name, CUfunction_t *out_fn) {
    CUresult_t r;
    CUmodule_t mod = 0;
    CUfunction_t fn = 0;

    if (cuda.cached_module && cuda.cached_ptx_len == ptx_len &&
        memcmp(cuda.cached_ptx, ptx, ptx_len) == 0 &&
        strcmp(cuda.cached_name, name) == 0) {
        *out_fn = cuda.cached_fn;
        return 0;
    }

    if ((r = cuda.module_load(&mod, ptx)) != 0) {
        fprintf(stderr, "CUDA: PTX would not load: %s\n", cuda_err(r));
        return 1;
    }
    if ((r = cuda.module_get_fn(&fn, mod, name)) != 0) {
        fprintf(stderr, "CUDA: module has no kernel '%s': %s\n", name, cuda_err(r));
        cuda.module_unload(mod);
        return 1;
    }

    if (cuda.cached_module) cuda.module_unload(cuda.cached_module);
    free(cuda.cached_ptx);
    cuda.cached_ptx = (unsigned char *)malloc(ptx_len ? ptx_len : 1);
    if (!cuda.cached_ptx) {          /* keep going uncached rather than fail */
        cuda.cached_module = 0;
        cuda.cached_ptx_len = 0;
        cuda.cached_name[0] = 0;
    } else {
        memcpy(cuda.cached_ptx, ptx, ptx_len);
        cuda.cached_ptx_len = ptx_len;
        strncpy(cuda.cached_name, name, sizeof(cuda.cached_name) - 1);
        cuda.cached_name[sizeof(cuda.cached_name) - 1] = 0;
        cuda.cached_module = mod;
        cuda.cached_fn = fn;
    }
    *out_fn = fn;
    return 0;
}

/* ── The arithmetic, on the device ─────────────────────────────────────

   op 32 lets a caller bring its own kernel. This is the other direction:
   the operations the bridge already serves, run on the card instead of
   in the scalar C below.

   MATMUL FIRST BECAUSE IT IS THE ONLY ONE WHOSE WORK GROWS FASTER THAN
   ITS OPERAND. It is O(ra*ca*cb) over O(ra*ca + ca*cb) bytes, so it is
   the one op where a launch and two copies can be repaid. Every other
   operation the bridge serves is elementwise or a single reduction --
   O(n) work over O(n) bytes -- and for those the transfer IS the
   computation, so moving them to the device would spend a launch to save
   nothing. They are deliberately left on the CPU rather than moved for
   symmetry, and this paragraph is here so the next person does not
   "finish the job" by moving them.

   THE SCALAR LOOP IS NOT DELETED, AND THAT IS THE POINT. When the serial
   transport went, the doorbell test lost its control: running every case
   two ways on identical operands was what would catch an implementation
   that computed WRONGLY rather than not at all. A second implementation
   of the arithmetic gives that control back, and it is a better one,
   because the two paths here share no code at all -- one is C on the
   host, the other is PTX on a graphics processor. CODEX_VM_GPU_MATMUL
   selects: unset is automatic, "0" forces the scalar path, "1" forces
   the device. The content is tested and not the pointer, because an
   empty-string environment variable is still a non-NULL getenv and that
   is how a toggle comes to read as set on both legs of a measurement.

   The kernel is naive on purpose: one thread per output element, a
   running sum over k. A tiled kernel with shared memory is the standard
   next step and it is not written, because the honest thing to establish
   first is whether the round trip pays at all at the sizes this bridge
   can carry (COM3_MAX_ELEMS caps an operand at 16384 floats, so the
   largest square matmul is 128x128). Optimising the kernel before that
   is measured would be optimising a path that might not be worth taking. */

static const char *CUDA_PTX_MATMUL =
".version 6.0\n"
".target sm_50\n"
".address_size 64\n"
"\n"
".visible .entry mm(\n"
".param .u64 p_in,\n"
".param .u64 p_out,\n"
".param .u32 p_ra,\n"
".param .u32 p_ca,\n"
".param .u32 p_cb\n"
")\n"
"{\n"
".reg .pred %p<4>;\n"
".reg .f32 %f<6>;\n"
".reg .b32 %r<24>;\n"
".reg .b64 %rd<20>;\n"
"ld.param.u64 %rd1, [p_in];\n"
"ld.param.u64 %rd2, [p_out];\n"
"ld.param.u32 %r1, [p_ra];\n"
"ld.param.u32 %r2, [p_ca];\n"
"ld.param.u32 %r3, [p_cb];\n"
"mov.u32 %r4, %ctaid.x;\n"
"mov.u32 %r5, %ntid.x;\n"
"mov.u32 %r6, %tid.x;\n"
"mad.lo.s32 %r7, %r4, %r5, %r6;\n"          /* i = global thread index */
"mul.lo.s32 %r8, %r1, %r3;\n"               /* n_out = ra * cb */
"setp.ge.u32 %p1, %r7, %r8;\n"
"@%p1 bra DONE;\n"
"div.u32 %r9, %r7, %r3;\n"                  /* row = i / cb */
"rem.u32 %r10, %r7, %r3;\n"                 /* col = i % cb */
"mul.lo.s32 %r11, %r9, %r2;\n"              /* row * ca */
"mul.lo.s32 %r12, %r1, %r2;\n"              /* base of B = ra * ca */
"cvta.to.global.u64 %rd3, %rd1;\n"
"cvta.to.global.u64 %rd4, %rd2;\n"
"mov.f32 %f1, 0f00000000;\n"                /* acc = 0.0 */
"mov.u32 %r13, 0;\n"                        /* k = 0 */
"LOOP:\n"
"setp.ge.u32 %p2, %r13, %r2;\n"
"@%p2 bra STORE;\n"
"add.s32 %r14, %r11, %r13;\n"               /* a index = row*ca + k */
"mul.wide.u32 %rd5, %r14, 4;\n"
"add.s64 %rd6, %rd3, %rd5;\n"
"ld.global.f32 %f2, [%rd6];\n"
"mad.lo.s32 %r15, %r13, %r3, %r10;\n"       /* k*cb + col */
"add.s32 %r16, %r12, %r15;\n"               /* + base of B */
"mul.wide.u32 %rd7, %r16, 4;\n"
"add.s64 %rd8, %rd3, %rd7;\n"
"ld.global.f32 %f3, [%rd8];\n"
"fma.rn.f32 %f1, %f2, %f3, %f1;\n"
"add.s32 %r13, %r13, 1;\n"
"bra LOOP;\n"
"STORE:\n"
"mul.wide.u32 %rd9, %r7, 4;\n"
"add.s64 %rd10, %rd4, %rd9;\n"
"st.global.f32 [%rd10], %f1;\n"
"DONE:\n"
"ret;\n"
"}\n";

/* Which path op 0 takes. The device is chosen automatically when the
   matmul is large enough that it pays, and the threshold is MEASURED.

   The device's cost is dominated by a one-time ~85 ms context creation
   and PTX JIT, paid once per process; its per-dispatch compute after that
   is small. The scalar loop costs O(N^3). So the device wins on a single
   dispatch only once the scalar loop would cost more than ~85 ms, which on
   an RTX 4060 Ti (a naive one-thread-per-output kernel against a scalar
   loop that reads every element through a 4-byte memcpy) measured out at
   about a 400x400x400 square:

       N     scalar    device (incl. JIT)
       256   23.0 ms   95.1 ms      scalar wins
       384   80.0 ms   72.9 ms      device wins
       512  198.1 ms   88.5 ms      device wins 2.2x

   Below the threshold the CPU is faster, so a small matmul -- every matmul
   the tests do, and every one a short program does -- stays scalar and is
   not slowed. At or above it the device is picked. COM3_MAX_ELEMS caps a
   square at 512x512, which is well past the break-even, and a caller that
   needs bigger ships its own kernel through the PTX launch (op 32). The
   env var forces a path either way (1 device, 0 scalar) so the doorbell
   test keeps its two-implementation control on a small matmul. */
#define COM3_DEVICE_MATMUL_FLOPS 64000000ULL   /* ~400^3, the measured break-even */

static int cuda_matmul_wanted(unsigned int ra, unsigned int ca, unsigned int cb) {
    const char *p = getenv("CODEX_VM_GPU_MATMUL");
    if (p && p[0] == '1') return cuda_ready();
    if (p && p[0] == '0') return 0;
    if ((double)ra * (double)ca * (double)cb < (double)COM3_DEVICE_MATMUL_FLOPS)
        return 0;
    return cuda_ready();
}

/* Answers 0 when the device produced the result. Any non-zero answer
   means the caller must fall back to the scalar loop -- a matmul is a
   matmul, and a caller that asked for one is owed the answer rather than
   a refusal. That is the opposite of op 32's contract, where the caller
   asked for a GPU specifically and a silent CPU answer would be a lie. */
static int cuda_matmul(const float *in, float *out,
                       unsigned int ra, unsigned int ca, unsigned int cb) {
    CUfunction_t fn = 0;
    CUdeviceptr_t d_in = 0, d_out = 0;
    CUresult_t r;
    unsigned int n_in = ra * ca + ca * cb;
    unsigned int n_out = ra * cb;
    unsigned int block = 256;
    unsigned int grid = (n_out + block - 1) / block;
    void *args[5];

    if (cuda_get_function((const unsigned char *)CUDA_PTX_MATMUL,
                          (unsigned int)strlen(CUDA_PTX_MATMUL), "mm", &fn)) return 1;
    if ((r = cuda.mem_alloc(&d_in, n_in * sizeof(float))) != 0) return 1;
    if ((r = cuda.mem_alloc(&d_out, n_out * sizeof(float))) != 0) { cuda.mem_free(d_in); return 1; }
    if ((r = cuda.memcpy_htod(d_in, in, n_in * sizeof(float))) != 0) goto fail;

    args[0] = &d_in; args[1] = &d_out;
    args[2] = &ra;   args[3] = &ca;   args[4] = &cb;
    if ((r = cuda.launch(fn, grid, 1, 1, block, 1, 1, 0, 0, args, 0)) != 0) goto fail;
    if ((r = cuda.sync()) != 0) goto fail;
    if ((r = cuda.memcpy_dtoh(out, d_out, n_out * sizeof(float))) != 0) goto fail;

    cuda.mem_free(d_in);
    cuda.mem_free(d_out);
    return 0;
fail:
    fprintf(stderr, "CUDA: matmul failed: %s -- falling back to the scalar loop\n", cuda_err(r));
    cuda.mem_free(d_in);
    cuda.mem_free(d_out);
    return 1;
}

/* op 32. The command's own header, in u32:

     0 status      4 op (32)     8 n_in       12 grid_x
    16 block_x    20 ptx_len    24 name_len   28 n_out

   then ptx_len bytes of PTX, then name_len bytes of kernel name, then
   n_in floats. The reply is a status u32 followed by n_out floats, which
   is the layout every op but matmul already answers in.

   Every refusal answers STATUS_ERROR. A launch that cannot happen must
   look different from one that happened and produced zeros. */
static void com3_launch_ptx(unsigned int cmd_bytes) {
    unsigned int n_in    = com3_get_u32(com3.cmd + 8);
    unsigned int grid_x  = com3_get_u32(com3.cmd + 12);
    unsigned int block_x = com3_get_u32(com3.cmd + 16);
    unsigned int ptx_len = com3_get_u32(com3.cmd + 20);
    unsigned int name_len= com3_get_u32(com3.cmd + 24);
    unsigned int n_out   = com3_get_u32(com3.cmd + 28);
    unsigned int want;
    static char ptx_text[COM3_PTX_MAX + 1];
    static char kernel_name[COM3_PTX_NAME_MAX + 1];
    static float host_in[COM3_MAX_ELEMS];
    static float host_out[COM3_MAX_ELEMS];
    CUfunction_t fn = 0;
    CUdeviceptr_t d_in = 0, d_out = 0;
    CUresult_t r;
    void *args[3];
    int n_arg;
    unsigned int i;

    if (ptx_len == 0 || ptx_len > COM3_PTX_MAX ||
        name_len == 0 || name_len > COM3_PTX_NAME_MAX ||
        n_in > COM3_MAX_ELEMS || n_out == 0 || n_out > COM3_MAX_ELEMS ||
        grid_x == 0 || block_x == 0) {
        fprintf(stderr, "COM3: ptx launch dims refused "
                        "(ptx %u name %u in %u out %u grid %u block %u)\n",
                ptx_len, name_len, n_in, n_out, grid_x, block_x);
        com3_put_u32(COM3_STATUS_ERROR);
        return;
    }
    want = COM3_PTX_HDR_BYTES + ptx_len + name_len + n_in * 4;
    if (cmd_bytes != want) {
        fprintf(stderr, "COM3: ptx launch length %u, expected %u\n", cmd_bytes, want);
        com3_put_u32(COM3_STATUS_ERROR);
        return;
    }
    if (!cuda_ready()) {
        com3_put_u32(COM3_STATUS_ERROR);
        return;
    }

    memcpy(ptx_text, com3.cmd + COM3_PTX_HDR_BYTES, ptx_len);
    ptx_text[ptx_len] = 0;                      /* cuModuleLoadData wants a C string */
    memcpy(kernel_name, com3.cmd + COM3_PTX_HDR_BYTES + ptx_len, name_len);
    kernel_name[name_len] = 0;
    for (i = 0; i < n_in; i++)
        host_in[i] = com3_get_f32(com3.cmd + COM3_PTX_HDR_BYTES + ptx_len + name_len + i * 4);

    if (cuda_get_function((const unsigned char *)ptx_text, ptx_len, kernel_name, &fn)) {
        com3_put_u32(COM3_STATUS_ERROR);
        return;
    }

    /* The device allocations are per launch. They could be pooled, and
       the reason not to yet is that the sizes are the guest's and a pool
       keyed on them is a cache with an eviction policy nobody has needed
       to choose. A cuMemAlloc is microseconds against a JIT's
       milliseconds, and the JIT is what the cache above removes. */
    if ((r = cuda.mem_alloc(&d_in, (n_in ? n_in : 1) * sizeof(float))) != 0) {
        fprintf(stderr, "CUDA: input alloc failed: %s\n", cuda_err(r));
        com3_put_u32(COM3_STATUS_ERROR);
        return;
    }
    if ((r = cuda.mem_alloc(&d_out, n_out * sizeof(float))) != 0) {
        fprintf(stderr, "CUDA: output alloc failed: %s\n", cuda_err(r));
        cuda.mem_free(d_in);
        com3_put_u32(COM3_STATUS_ERROR);
        return;
    }
    if (n_in && (r = cuda.memcpy_htod(d_in, host_in, n_in * sizeof(float))) != 0) {
        fprintf(stderr, "CUDA: upload failed: %s\n", cuda_err(r));
        cuda.mem_free(d_in); cuda.mem_free(d_out);
        com3_put_u32(COM3_STATUS_ERROR);
        return;
    }

    args[0] = &d_in;
    args[1] = &d_out;
    args[2] = &n_out;
    n_arg = 3;
    (void)n_arg;
    r = cuda.launch(fn, grid_x, 1, 1, block_x, 1, 1, 0, 0, args, 0);
    if (r == 0) r = cuda.sync();
    if (r != 0) {
        fprintf(stderr, "CUDA: launch of '%s' failed: %s\n", kernel_name, cuda_err(r));
        cuda.mem_free(d_in); cuda.mem_free(d_out);
        com3_put_u32(COM3_STATUS_ERROR);
        return;
    }
    if ((r = cuda.memcpy_dtoh(host_out, d_out, n_out * sizeof(float))) != 0) {
        fprintf(stderr, "CUDA: download failed: %s\n", cuda_err(r));
        cuda.mem_free(d_in); cuda.mem_free(d_out);
        com3_put_u32(COM3_STATUS_ERROR);
        return;
    }
    cuda.mem_free(d_in);
    cuda.mem_free(d_out);

    com3_put_u32(COM3_STATUS_COMPLETE);
    for (i = 0; i < n_out; i++) com3_put_f32(host_out[i]);
}

static void com3_execute(void) {
    unsigned int op = com3_get_u32(com3.cmd + 4);
    unsigned int ra = com3_get_u32(com3.cmd + 8);
    unsigned int ca = com3_get_u32(com3.cmd + 12);
    unsigned int cb = com3_get_u32(com3.cmd + 16);
    const unsigned char *data = com3.cmd + COM3_HDR_BYTES;

    com3.reply_len = 0;


    if (op == 0) {
        /* The device first when there is one, the scalar loop otherwise
           and whenever the device refuses. Both must agree, and the
           doorbell test is what holds them to it. */
        static float mm_in[2 * COM3_MAX_ELEMS];
        static float mm_out[COM3_MAX_ELEMS];
        unsigned int n_in = ra * ca + ca * cb;
        unsigned int n_out = ra * cb;
        int on_device = 0;
        const char *stat = getenv("CODEX_VM_COM3_STAT");
        LARGE_INTEGER t0, t1, freq;
        QueryPerformanceFrequency(&freq);
        QueryPerformanceCounter(&t0);

        if (cuda_matmul_wanted(ra, ca, cb) && n_in <= 2 * COM3_MAX_ELEMS && n_out <= COM3_MAX_ELEMS) {
            unsigned int e;
            for (e = 0; e < n_in; e++) mm_in[e] = com3_get_f32(data + e * 4);
            on_device = (cuda_matmul(mm_in, mm_out, ra, ca, cb) == 0);
        }

        com3_put_u32(COM3_STATUS_COMPLETE);
        com3_put_u32(ra);
        com3_put_u32(cb);
        if (on_device) {
            unsigned int e;
            for (e = 0; e < n_out; e++) com3_put_f32(mm_out[e]);
        } else {
            for (unsigned int i = 0; i < ra; i++) {
                for (unsigned int j = 0; j < cb; j++) {
                    float acc = 0.0f;
                    for (unsigned int k = 0; k < ca; k++) {
                        acc += com3_get_f32(data + (i * ca + k) * 4)
                             * com3_get_f32(data + (ra * ca + k * cb + j) * 4);
                    }
                    com3_put_f32(acc);
                }
            }
        }
        QueryPerformanceCounter(&t1);
        if (stat && stat[0])
            fprintf(stderr, "COM3-MATMUL: %s %ux%ux%u %.1f us\n",
                    on_device ? "device" : "scalar", ra, ca, cb,
                    (double)(t1.QuadPart - t0.QuadPart) * 1e6 / (double)freq.QuadPart);
    } else if (op == 2 || op == 3 || op == 8 || op == 10 || op == 14) {
        /* Elementwise unary and the two reductions that share its shape.

           WHERE A CONVENTION HAD TO BE CHOSEN IT IS NAMED HERE, because a
           caller cannot tell one convention from the other by looking at
           an answer. GELU is the tanh approximation, not the exact erf
           form. LAYER NORM uses epsilon 1e-5. Both are the usual choices
           in this corner and both are stated in GpuBridge's prose too. */
        unsigned int n = ra * ca;
        unsigned int i;
        com3_put_u32(COM3_STATUS_COMPLETE);
        if (op == 3) {                            /* softmax */
            float mx = -3.402823e38f, sum = 0.0f;
            for (i = 0; i < n; i++) { float v = com3_get_f32(data + i * 4); if (v > mx) mx = v; }
            for (i = 0; i < n; i++) sum += expf(com3_get_f32(data + i * 4) - mx);
            for (i = 0; i < n; i++)
                com3_put_f32(sum > 0.0f ? expf(com3_get_f32(data + i * 4) - mx) / sum : 0.0f);
        } else if (op == 8) {                     /* layer norm */
            float mean = 0.0f, var = 0.0f;
            for (i = 0; i < n; i++) mean += com3_get_f32(data + i * 4);
            if (n) mean /= (float)n;
            for (i = 0; i < n; i++) { float d = com3_get_f32(data + i * 4) - mean; var += d * d; }
            if (n) var /= (float)n;
            {
                float inv = 1.0f / sqrtf(var + 1e-5f);
                for (i = 0; i < n; i++)
                    com3_put_f32((com3_get_f32(data + i * 4) - mean) * inv);
            }
        } else {
            for (i = 0; i < n; i++) {
                float v = com3_get_f32(data + i * 4);
                if (op == 2) com3_put_f32(v > 0.0f ? v : 0.0f);
                else if (op == 10) {
                    float c = 0.7978845608f * (v + 0.044715f * v * v * v);
                    com3_put_f32(0.5f * v * (1.0f + tanhf(c)));
                } else com3_put_f32(v / (1.0f + expf(-v)));   /* silu */
            }
        }
    } else if (op == 5 || op == 6) {
        /* Elementwise binary: the second operand follows the first. */
        unsigned int n = ra * ca, i;
        com3_put_u32(COM3_STATUS_COMPLETE);
        for (i = 0; i < n; i++) {
            float x = com3_get_f32(data + i * 4);
            float y = com3_get_f32(data + (n + i) * 4);
            com3_put_f32(op == 5 ? x + y : x * y);
        }
    } else if (op == 7) {
        /* Transpose. The reply carries elements only, like every other op
           but matmul: the caller sent the shape and can swap it itself, so
           echoing dimensions back would buy a second reply layout for
           nothing. */
        unsigned int i, j;
        com3_put_u32(COM3_STATUS_COMPLETE);
        for (i = 0; i < ca; i++)
            for (j = 0; j < ra; j++)
                com3_put_f32(com3_get_f32(data + (j * ca + i) * 4));
    } else if (op == 11) {
        /* Scale by the single scalar sitting after the vector. */
        unsigned int n = ra * ca, i;
        float k = com3_get_f32(data + n * 4);
        com3_put_u32(COM3_STATUS_COMPLETE);
        for (i = 0; i < n; i++) com3_put_f32(com3_get_f32(data + i * 4) * k);
    } else if (op == 13) {
        /* Group norm: ra is the length, ca the group count, and each group
           is normalised against its own mean and variance. */
        unsigned int n = ra, groups = ca, per = ra / ca, g, i;
        com3_put_u32(COM3_STATUS_COMPLETE);
        for (g = 0; g < groups; g++) {
            const unsigned char *p = data + g * per * 4;
            float mean = 0.0f, var = 0.0f, inv;
            for (i = 0; i < per; i++) mean += com3_get_f32(p + i * 4);
            if (per) mean /= (float)per;
            for (i = 0; i < per; i++) { float d = com3_get_f32(p + i * 4) - mean; var += d * d; }
            if (per) var /= (float)per;
            inv = 1.0f / sqrtf(var + 1e-5f);
            for (i = 0; i < per; i++) com3_put_f32((com3_get_f32(p + i * 4) - mean) * inv);
        }
        (void)n;
    } else if (op == 15) {
        /* Upsample 2x, NEAREST NEIGHBOUR. The other reading of this op is
           bilinear and the two disagree everywhere except on a constant
           input, so the choice is named rather than left to be inferred. */
        unsigned int c = ra, h = ca, w = cb, ch, y, x;
        com3_put_u32(COM3_STATUS_COMPLETE);
        for (ch = 0; ch < c; ch++)
            for (y = 0; y < h * 2; y++)
                for (x = 0; x < w * 2; x++)
                    com3_put_f32(com3_get_f32(data + (ch * h * w + (y / 2) * w + (x / 2)) * 4));
    } else if (op == 4) {
        /* conv1d, valid 1D cross-correlation: input is data[0..ra), kernel
           is data[ra..ra+ca), stride is cb. out[o] = sum_k input[o*cb + k]
           * kernel[k]. The kernel is NOT flipped -- this is the correlation
           an ML conv layer computes -- and padding is the caller's to add
           to the input, so the op is the valid (unpadded) convolution. */
        unsigned int L = ra, K = ca, S = cb, out = (ra - ca) / cb + 1, o, k;
        com3_put_u32(COM3_STATUS_COMPLETE);
        for (o = 0; o < out; o++) {
            float acc = 0.0f;
            for (k = 0; k < K; k++)
                acc += com3_get_f32(data + (o * S + k) * 4)
                     * com3_get_f32(data + (L + k) * 4);
            com3_put_f32(acc);
        }
    } else if (op == 9) {
        /* max-pool, valid 1D: out[o] = max over the window [o*cb, o*cb+ca). */
        unsigned int S = cb, out = (ra - ca) / cb + 1, o, k;
        com3_put_u32(COM3_STATUS_COMPLETE);
        for (o = 0; o < out; o++) {
            float m = com3_get_f32(data + (o * S) * 4);
            for (k = 1; k < ca; k++) {
                float v = com3_get_f32(data + (o * S + k) * 4);
                if (v > m) m = v;
            }
            com3_put_f32(m);
        }
    } else if (op == 16) {
        /* clamp each of the ra*ca values to [lo, hi], the two scalars that
           follow the data the way scale's single factor does. */
        unsigned int n = ra * ca, i;
        float lo = com3_get_f32(data + n * 4);
        float hi = com3_get_f32(data + (n + 1) * 4);
        com3_put_u32(COM3_STATUS_COMPLETE);
        for (i = 0; i < n; i++) {
            float v = com3_get_f32(data + i * 4);
            com3_put_f32(v < lo ? lo : (v > hi ? hi : v));
        }
    } else {
        fprintf(stderr, "COM3: op %u not served -- answering error\n", op);
        com3_put_u32(COM3_STATUS_ERROR);
    }
}

/* conv2d: valid 2D cross-correlation, zero-padded by `pad`, no kernel flip
   -- the correlation an ML conv layer computes. Input is cin x h x w,
   kernel is cout x cin x kh x kw, output is cout x oh x ow with
   oh = (h + 2*pad - kh)/stride + 1 and ow likewise. Routed here from the
   doorbell around com3_shape because its eight shape fields do not fit the
   three the shape header carries; it validates its own length instead. */
static void com3_conv2d(unsigned int cmd_bytes) {
    unsigned int cin  = com3_get_u32(com3.cmd + 8);
    unsigned int h    = com3_get_u32(com3.cmd + 12);
    unsigned int w    = com3_get_u32(com3.cmd + 16);
    unsigned int cout = com3_get_u32(com3.cmd + 20);
    unsigned int kh   = com3_get_u32(com3.cmd + 24);
    unsigned int kw   = com3_get_u32(com3.cmd + 28);
    unsigned int s    = com3_get_u32(com3.cmd + 32);
    unsigned int pad  = com3_get_u32(com3.cmd + 36);
    unsigned int oh, ow, oc, y, x, ic, ky, kx, in_n, ker_n;
    double in_sz, ker_sz, out_sz;
    const unsigned char *in, *ker;

    com3.reply_len = 0;

    if (s == 0 || kh == 0 || kw == 0 || h + 2 * pad < kh || w + 2 * pad < kw) {
        fprintf(stderr, "COM3: conv2d bad shape %ux%ux%u k%ux%u s%u p%u\n",
                cin, h, w, kh, kw, s, pad);
        com3_put_u32(COM3_STATUS_ERROR);
        return;
    }
    oh = (h + 2 * pad - kh) / s + 1;
    ow = (w + 2 * pad - kw) / s + 1;
    in_sz  = (double)cin * (double)h * (double)w;
    ker_sz = (double)cout * (double)cin * (double)kh * (double)kw;
    out_sz = (double)cout * (double)oh * (double)ow;
    if (in_sz + ker_sz > COM3_MAX_ELEMS || out_sz > COM3_MAX_ELEMS) {
        fprintf(stderr, "COM3: conv2d operands too large\n");
        com3_put_u32(COM3_STATUS_ERROR);
        return;
    }
    in_n  = (unsigned int)in_sz;
    ker_n = (unsigned int)ker_sz;
    if (cmd_bytes != COM3_CONV2D_HDR_BYTES + (in_n + ker_n) * 4) {
        fprintf(stderr, "COM3: conv2d len %u vs expected %u\n",
                cmd_bytes, COM3_CONV2D_HDR_BYTES + (in_n + ker_n) * 4);
        com3_put_u32(COM3_STATUS_ERROR);
        return;
    }
    in  = com3.cmd + COM3_CONV2D_HDR_BYTES;
    ker = in + in_n * 4;
    com3_put_u32(COM3_STATUS_COMPLETE);
    for (oc = 0; oc < cout; oc++) {
        for (y = 0; y < oh; y++) {
            for (x = 0; x < ow; x++) {
                float acc = 0.0f;
                for (ic = 0; ic < cin; ic++) {
                    for (ky = 0; ky < kh; ky++) {
                        for (kx = 0; kx < kw; kx++) {
                            int iy = (int)(y * s + ky) - (int)pad;
                            int ix = (int)(x * s + kx) - (int)pad;
                            if (iy >= 0 && iy < (int)h && ix >= 0 && ix < (int)w) {
                                float iv = com3_get_f32(in  + ((ic * h + (unsigned int)iy) * w + (unsigned int)ix) * 4);
                                float kv = com3_get_f32(ker + (((oc * cin + ic) * kh + ky) * kw + kx) * 4);
                                acc += iv * kv;
                            }
                        }
                    }
                }
                com3_put_f32(acc);
            }
        }
    }
}

/* ── GPU compute doorbell (0x420-0x423) ────────────────────────────────

   The serial bridge above costs TWO VM exits per byte -- one LSR poll and
   one transfer -- in each direction, so its cost is a function of the
   operand size rather than of the dispatch. Measured with
   CODEX_VM_COM3_STAT: a 2x2 matmul plus a 2x2 relu is 272 exits, and one
   32x32 matmul is 24,640.

   The doorbell moves the same command through guest RAM instead. The
   guest builds the command with ordinary memory writes, which are not
   exits at all, then names the buffer and rings: three OUTs and one IN,
   four exits, whatever the operand size. Handing the host a guest address
   is already this emulator's idiom -- the triangle rasterizer takes its
   command buffer that way (0x400) and so does the asset loader
   (0x40C/0x40D).

   The command format is unchanged for the arithmetic ops, and
   com3_execute is the same function the serial path called. Only the
   transport was new, which is what kept the two paths from disagreeing
   about arithmetic. The PTX launch (op 32) is the one command with a
   header of its own; it arrives through this same doorbell and is routed
   to com3_launch_ptx before the shape check, because its length follows
   from its program and not from three dimension fields. */

#define COM3_PORT_CMD_ADDR    0x420
#define COM3_PORT_REPLY_ADDR  0x421
#define COM3_PORT_DOORBELL    0x422
#define COM3_PORT_REPLY_LEN   0x423

static unsigned int com3_cmd_addr = 0;
static unsigned int com3_reply_addr = 0;

static void com3_doorbell(unsigned int cmd_bytes) {
    unsigned char *g = (unsigned char *)guest_mem;
    unsigned int op, ra, ca, cb;
    int operand_elems = 0, result_elems = 0;

    com3.reply_len = 0;


    if (!g) return;

    /* Every refusal below still answers STATUS_ERROR rather than leaving
       the reply empty: a guest that cannot tell "refused" from "nothing
       happened" has to time out to learn anything. */
    if (cmd_bytes < (unsigned int)COM3_HDR_BYTES || cmd_bytes > sizeof(com3.cmd) ||
        (size_t)com3_cmd_addr + cmd_bytes > guest_mem_size) {
        fprintf(stderr, "COM3: doorbell cmd addr 0x%x len %u out of range\n",
                com3_cmd_addr, cmd_bytes);
        com3_put_u32(COM3_STATUS_ERROR);
    } else {
        memcpy(com3.cmd, g + com3_cmd_addr, cmd_bytes);
        op = com3_get_u32(com3.cmd + 4);
        ra = com3_get_u32(com3.cmd + 8);
        ca = com3_get_u32(com3.cmd + 12);
        cb = com3_get_u32(com3.cmd + 16);
        /* A PTX launch carries a program, not a shape, so its length
           cannot be derived from three dimension fields. It validates
           its own header and is routed around com3_shape rather than
           given a fake shape there. */
        if (op == COM3_OP_LAUNCH_PTX) {
            com3_launch_ptx(cmd_bytes);
        } else if (op == COM3_OP_CONV2D) {
            /* conv2d carries eight shape fields, not three, so it validates
               its own length in com3_conv2d rather than through com3_shape. */
            com3_conv2d(cmd_bytes);
        } else if (!com3_shape(op, ra, ca, cb, &operand_elems, &result_elems) ||
            cmd_bytes != (unsigned int)COM3_HDR_BYTES + (unsigned int)operand_elems * 4) {
            fprintf(stderr, "COM3: doorbell dims %u x %u x %u vs len %u -- answering error\n",
                    ra, ca, cb, cmd_bytes);
            com3_put_u32(COM3_STATUS_ERROR);
        } else {
            com3_execute();
        }
    }

    if ((size_t)com3_reply_addr + (size_t)com3.reply_len > guest_mem_size) {
        fprintf(stderr, "COM3: doorbell reply addr 0x%x len %d out of range\n",
                com3_reply_addr, com3.reply_len);
        com3.reply_len = 0;
        return;
    }
    memcpy(g + com3_reply_addr, com3.reply, (size_t)com3.reply_len);
}

/* ── I/O dispatch ──────────────────────────────────────────────────── */

static void handle_io(WHV_RUN_VP_EXIT_CONTEXT *ctx) {
    int port = ctx->IoPortAccess.PortNumber;
    int is_out = (ctx->IoPortAccess.AccessInfo.IsWrite != 0);
    int size = ctx->IoPortAccess.AccessInfo.AccessSize;
    int val = 0;
    if (is_out) val = (int)ctx->IoPortAccess.Rax;
    if (smp_cores > 1 && is_out && port >= 0x510) fprintf(stderr, "IO-HI[0x%x]=0x%x\n", port, val);

    /* Census, before any arm returns. The data port and the task-file
       registers are counted apart because they answer different questions:
       the first is the transfer, the second is the driver's polling around
       it, and the standing figure of ~306 exits per sector is 256 of one
       plus a tail of the other. */
    if (port == 0x1F0) {
        ide_pio_exits++;
        if (ctx->IoPortAccess.AccessInfo.StringOp) ide_pio_str_exits++;
    }
    else if ((port >= 0x1F1 && port <= 0x1F7) || port == 0x3F6) ide_reg_exits++;

    if (is_out) {
        /* REP OUTSB/OUTSW to the NE2K data port: batch the whole remaining
           count in one exit. The generic OUT path would read data from RAX
           (string data lives at guest [RSI]) and then skip the rest of the
           REP - and per-iteration exits cost one exit per word. rbcr bounds
           the useful count; a hostile RCX is capped per exit. */
        if (ctx->IoPortAccess.AccessInfo.StringOp && port == NE2K_BASE + 0x10) {
            unsigned long long gpa = ctx->IoPortAccess.Rsi;
            unsigned long long cnt = ctx->IoPortAccess.Rcx;
            unsigned long long done = 0;
            unsigned char *gmem = (unsigned char *)guest_mem;
            if (cnt > (1ULL << 20)) cnt = (1ULL << 20);
            for (; done < cnt; done++) {
                unsigned long long p = gpa + done * (unsigned long long)size;
                int wval = 0;
                if (p + (unsigned long long)size > guest_mem_size) break;
                if (size == 1) wval = gmem[p];
                else if (size == 2) wval = gmem[p] | (gmem[p + 1] << 8);
                else wval = (int)(*(unsigned int *)(gmem + p));
                ne2k_handle_out(port, wval, size);
            }
            WHV_REGISTER_NAME sn[] = { WHvX64RegisterRsi, WHvX64RegisterRcx };
            WHV_REGISTER_VALUE sv[2];
            sv[0].Reg64 = ctx->IoPortAccess.Rsi + done * (unsigned long long)size;
            sv[1].Reg64 = ctx->IoPortAccess.Rcx - done;
            WHvSetVirtualProcessorRegisters(partition, 0, sn, 2, sv);
            if (ctx->IoPortAccess.Rcx - done == 0) {
                WHV_REGISTER_NAME rn = WHvX64RegisterRip;
                WHV_REGISTER_VALUE rv;
                rv.Reg64 = ctx->VpContext.Rip + ctx->VpContext.InstructionLength;
                WHvSetVirtualProcessorRegisters(partition, 0, &rn, 1, &rv);
            }
            return;
        }
        /* REP OUTSW to the IDE data port: a WRITE SECTORS data phase. Consume
           the whole remaining count in one exit, the way the NE2K and COM1
           arms above already do.

           This arm used to take one word per exit, so a 512-byte sector cost
           256 exits and the census read words/pio-exits = 1.0 exactly. The
           model's own bookkeeping is what makes the batch safe: ide_write_data
           refuses once buf_remaining reaches zero and the drive is no longer
           writing, so a count that runs past the sector the guest set up stops
           being consumed at the boundary rather than spilling into the next
           one. The guest polls the status register between sectors either way,
           and those exits are counted apart as reg-exits. */
        if (ctx->IoPortAccess.AccessInfo.StringOp && port == 0x1F0) {
            unsigned long long gpa = ctx->IoPortAccess.Rsi;
            unsigned long long cnt = ctx->IoPortAccess.Rcx;
            unsigned long long done = 0;
            unsigned char *gmem = (unsigned char *)guest_mem;
            if (cnt > (1ULL << 20)) cnt = (1ULL << 20);
            for (; done < cnt; done++) {
                unsigned long long p = gpa + done * (unsigned long long)size;
                int wval = 0;
                if (p + (unsigned long long)size > guest_mem_size) break;
                if (size == 1) wval = gmem[p];
                else if (size == 2) wval = gmem[p] | (gmem[p + 1] << 8);
                else wval = (int)(*(unsigned int *)(gmem + p));
                ide_write_data(ide_active(), wval);
            }
            ide_out_batch_hits++;
            ide_out_batched += done;
            WHV_REGISTER_NAME sn[] = { WHvX64RegisterRsi, WHvX64RegisterRcx };
            WHV_REGISTER_VALUE sv[2];
            sv[0].Reg64 = ctx->IoPortAccess.Rsi + done * (unsigned long long)size;
            sv[1].Reg64 = ctx->IoPortAccess.Rcx - done;
            WHvSetVirtualProcessorRegisters(partition, 0, sn, 2, sv);
            if (ctx->IoPortAccess.Rcx - done == 0) {
                WHV_REGISTER_NAME rn = WHvX64RegisterRip;
                WHV_REGISTER_VALUE rv;
                rv.Reg64 = ctx->VpContext.Rip + ctx->VpContext.InstructionLength;
                WHvSetVirtualProcessorRegisters(partition, 0, &rn, 1, &rv);
            }
            return;
        }
        /* REP OUTSB to COM1's THR: the guest bursts a FIFO-full at a time.
           The generic COM1 path below takes its byte from RAX, where string
           data does not live, so without this arm a burst would emit one
           garbage byte and skip the rest. Consume the whole count here. */
        if (ctx->IoPortAccess.AccessInfo.StringOp && port == 0x3F8) {
            unsigned long long gpa = ctx->IoPortAccess.Rsi;
            unsigned long long cnt = ctx->IoPortAccess.Rcx;
            unsigned long long done = 0;
            unsigned char *gmem = (unsigned char *)guest_mem;
            if (cnt > (1ULL << 20)) cnt = (1ULL << 20);
            for (; done < cnt; done++) {
                unsigned long long p = gpa + done * (unsigned long long)size;
                if (p + (unsigned long long)size > guest_mem_size) break;
                output_buf_write(gmem[p]);
            }
            WHV_REGISTER_NAME sn[] = { WHvX64RegisterRsi, WHvX64RegisterRcx };
            WHV_REGISTER_VALUE sv[2];
            sv[0].Reg64 = ctx->IoPortAccess.Rsi + done * (unsigned long long)size;
            sv[1].Reg64 = ctx->IoPortAccess.Rcx - done;
            WHvSetVirtualProcessorRegisters(partition, 0, sn, 2, sv);
            if (ctx->IoPortAccess.Rcx - done == 0) {
                WHV_REGISTER_NAME rn = WHvX64RegisterRip;
                WHV_REGISTER_VALUE rv;
                rv.Reg64 = ctx->VpContext.Rip + ctx->VpContext.InstructionLength;
                WHvSetVirtualProcessorRegisters(partition, 0, &rn, 1, &rv);
            }
            return;
        }
        /* ACPI PM1a control block (0x604, the address this VM's FADT
           publishes and the one QEMU's PIIX4/ICH9 use). Bit 13 is SLP_EN and
           bits 10-12 are SLP_TYP. A guest that read _S5_ out of the DSDT and
           writes (SLP_TYPa << 10) | SLP_EN is asking to power off; our _S5_
           says SLP_TYPa = 0, so the value is 0x2000 -- identical to what the
           same driver emits on QEMU. Any other SLP_TYP is a sleep state we
           do not model, and is ignored rather than faked. */
        if (port == 0x604 && (val & 0x2000)) {
            int slp_typ = (val >> 10) & 0x7;
            if (slp_typ == 0) {
                fprintf(stderr, "ACPI: S5 sleep requested (PM1a_CNT=0x%x) -- powering off\n", val);
                shutdown_requested = 1;
                return;
            }
            fprintf(stderr, "ACPI: PM1a_CNT=0x%x SLP_TYP=%d not modeled, ignored\n", val, slp_typ);
            return;
        }
        /* Reset control (0xCF9). Bit 2 pulses the reset line; a full guest
           reboot is not modeled, so a requested reset ends the VM loudly --
           the exit message is the verdict a test looks for. */
        if (port == 0xCF9 && (val & 0x04)) {
            fprintf(stderr, "RESET: 0xCF9 system reset requested (val=0x%x) -- exiting\n", val);
            shutdown_requested = 1;
            return;
        }
        /* COM1 OUT: buffer the byte for file output */
        if (port >= 0x3F8 && port <= 0x3FF) {
            if (port == 0x3F8) {
                output_buf_write((unsigned char)val);
                if (r10dump && (unsigned char)val == '\n') {
                    WHV_REGISTER_NAME rns[2] = { WHvX64RegisterR10, WHvX64RegisterRsp };
                    WHV_REGISTER_VALUE rvs[2];
                    WHvGetVirtualProcessorRegisters(partition, 0, rns, 2, rvs);
                    fprintf(stderr, "R10DUMP: R10=0x%llx RSP=0x%llx\n", rvs[0].Reg64, rvs[1].Reg64);
                }
            }
        }
        /* COM3 OUT: the GPU compute bridge consumes command bytes. */
        else if (port >= 0x3E8 && port <= 0x3EF) {
            /* COM3 is no longer a compute transport; the guest rings the
               doorbell below instead. Writes here are accepted and dropped
               rather than faulting, because this is an ordinary UART
               window and something may yet be wired to it. */
        }
        /* GPU compute doorbell: the command is read out of guest RAM */
        else if (port >= COM3_PORT_CMD_ADDR && port <= COM3_PORT_REPLY_LEN) {
            com3_io_exits++;
            if (port == COM3_PORT_CMD_ADDR) com3_cmd_addr = (unsigned int)val;
            else if (port == COM3_PORT_REPLY_ADDR) com3_reply_addr = (unsigned int)val;
            else if (port == COM3_PORT_DOORBELL) {
                com3_doorbell((unsigned int)val);
                com3_stat("doorbell");
            }
        }
        /* COM2 OUT: detect HEAP: for clean exit (old-seed compat) */
        else if (port >= 0x2F8 && port <= 0x2FF) {
            if (port == 0x2F8) {
                static char ctrl_buf[8];
                static int ctrl_pos = 0;
                ctrl_buf[ctrl_pos++] = (char)val;
                if (ctrl_pos >= 5 && ctrl_buf[0]=='H' && ctrl_buf[1]=='E' && ctrl_buf[2]=='A' && ctrl_buf[3]=='P' && ctrl_buf[4]==':') {
                    /* Only exit if all input has been consumed (supports REPL batch) */
                    int all_consumed = 1;
                    if (input_overflow && input_overflow_pos < input_overflow_len) all_consumed = 0;
                    if (guest_mem) {
                        unsigned long long wpos = *(unsigned long long *)((unsigned char *)guest_mem + 28704);
                        unsigned long long rpos = *(unsigned long long *)((unsigned char *)guest_mem + 28712);
                        if (wpos > rpos) all_consumed = 0;
                        fprintf(stderr, "HEAP: check wpos=%llu rpos=%llu overflow=%d all_consumed=%d\n",
                                wpos, rpos, (input_overflow ? (int)(input_overflow_len - input_overflow_pos) : 0), all_consumed);
                    }
                    if (all_consumed) debug_exit_code = 0;
                }
                if (val == '\n' || ctrl_pos >= 7) ctrl_pos = 0;
            }
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
        else if (port == 0x43) {
            int channel = (val >> 6) & 3;
            int access = (val >> 4) & 3;
            if (channel < 3) {
                if (access == 0) {
                    /* Counter-latch command: freeze the count for reading.
                       It carries no mode or access field, so overwriting
                       either from these bits would corrupt the channel's
                       configuration on every latch. */
                    pit_latched[channel] = (unsigned short)pit_current_count(channel);
                    pit_latch_valid[channel] = 1;
                    pit_read_hi[channel] = 0;
                } else {
                    pit_access[channel] = access;
                    pit_mode[channel] = (val >> 1) & 7;
                    pit_latch_valid[channel] = 0;
                    pit_load_hi[channel] = 0;
                    pit_read_hi[channel] = 0;
                    if (channel == 2) speaker_freq_latch = 0; /* reset latch on mode write */
                }
            }
        }
        /* PIT channel data. Access mode 3 (lobyte/hibyte) loads in two
           writes; modes 1 and 2 load a single byte. Channel 2 also drives
           the speaker, and its latch used to live in a later branch of
           this same chain that this one already matched -- so it was
           unreachable and speaker_freq was never loaded from a guest
           write at all. Both are updated here. */
        else if (port >= 0x40 && port <= 0x42) {
            int ch = port - 0x40;
            if (pit_access[ch] == 3) {
                if (!pit_load_hi[ch]) {
                    pit_reload[ch] = (unsigned short)((pit_reload[ch] & 0xFF00) | (val & 0xFF));
                    pit_load_hi[ch] = 1;
                } else {
                    pit_reload[ch] = (unsigned short)((pit_reload[ch] & 0x00FF) | ((val & 0xFF) << 8));
                    pit_load_hi[ch] = 0;
                }
            } else if (pit_access[ch] == 2) {
                pit_reload[ch] = (unsigned short)((pit_reload[ch] & 0x00FF) | ((val & 0xFF) << 8));
            } else {
                pit_reload[ch] = (unsigned short)((pit_reload[ch] & 0xFF00) | (val & 0xFF));
            }
            if (ch == 2) {
                speaker_freq = pit_reload[2];
                speaker_freq_latch = 0;
            }
        }
        /* IDE */
        else if ((port >= 0x1F0 && port <= 0x1F7) || port == 0x3F6) {
            ide_bus_out(port, val);
        }
        /* Doorbell -- guest signals output status */
        else if (port == DOORBELL_PORT) {
            if (val == DOORBELL_BLIT) blit_guest_output();
            if (val == DOORBELL_COMPILE_DONE || val == DOORBELL_FATAL)
                debug_exit_code = (val == DOORBELL_FATAL) ? 1 : 0;
        }
        /* Debug exit */
        else if (port == 0xF4) {
            fprintf(stderr, "DEBUG EXIT: val=%d RIP=0x%llx\n", val, (unsigned long long)ctx->VpContext.Rip);
            debug_exit_code = val;
        }
        /* NE2000 NIC (0x300-0x31F) */
        else if (port >= NE2K_BASE && port < NE2K_BASE + 0x20) {
            ne2k_handle_out(port, val, size);
        }
        /* CMOS RTC (0x70=index, 0x71=data) */
        else if (port == 0x70) {
            cmos_index = val & 0x7F;
        }
        else if (port == 0x71) {
            /* write to CMOS -- ignore */
        }
        /* Guest sleep request: out 0xE0, ms -- yields to host for N ms */
        else if (port == 0xE0) {
            DWORD ms = (val > 0 && val <= 1000) ? (DWORD)val : 1;
            Sleep(ms);
        }
        /* Keyboard controller (0x60/0x64) -- accept silently */
        else if (port == 0x60 || port == 0x64) {
            /* guest disables keyboard; ignore */
        }
        /* VGA Attribute Controller (0x3C0) */
        else if (port == 0x3C0) {
            if (!vga_attr_flipflop) { vga_attr_index = val & 0x3F; }
            vga_attr_flipflop ^= 1;
        }
        /* VGA misc/sequencer/DAC -- accept silently */
        else if (port >= 0x3C2 && port <= 0x3CF) { }
        else if (port >= 0x3D4 && port <= 0x3D5) { }
        /* Bochs VBE */
        else if (port == VBE_INDEX_PORT) {
            vbe_index = val & 0xF;
        }
        else if (port == VBE_DATA_PORT) {
            vbe_regs[vbe_index] = (unsigned short)val;
            if (vbe_index == 4 && (val & 1)) { /* VBE_DISPI_ENABLED */
                int w = vbe_regs[1] ? vbe_regs[1] : 640;
                int h = vbe_regs[2] ? vbe_regs[2] : 480;
                if (w > GOP_MAX_W) w = GOP_MAX_W;
                if (h > GOP_MAX_H) h = GOP_MAX_H;
                gop_width = w; gop_height = h;
                gop_stride = gop_stride_opt > w ? gop_stride_opt : w;
                gop_active = 1; vbe_active = 1;
                if (!gop_fb) gop_fb = (unsigned char *)calloc(1, GOP_FB_SIZE);
                /* The startup path commits this region only when gop_active is
                   already set, and a VBE mode set turns it on HERE, at runtime,
                   long after that. sync_shadow_buffers then reads the
                   framebuffer out of guest RAM bounded against guest_mem_size,
                   which the region is inside, so the bound passes and the
                   memcpy reads reserved uncommitted address space: 0xC0000005
                   on a READ, one line after the mode set. Same defect as the
                   oversized-disk write (main 14494), other direction. */
                guest_commit_range(0xBE000000ULL,
                    (GOP_FB_ADDR + (unsigned long long)((size_t)gop_stride * gop_height * 4)) - 0xBE000000ULL);
                fprintf(stderr, "VBE: mode set %dx%d fb=0x%llx\n", w, h, (unsigned long long)VBE_FB_ADDR);
            }
        }
        /* GPU triangle rasterizer commands */
        else if (port == 0x400) {
            if ((unsigned int)val & GPU_SHADOW_FLAG) {
                gpu_shadow_render((int)((unsigned int)val & ~GPU_SHADOW_FLAG));
            } else {
                gpu_rasterize_triangles(val);
                /* The glow is a fullscreen post-process that reads fb[0] as the
                   background colour and blooms every edge against it. Inside a
                   viewport that background is the desktop, not the scene, so it
                   would bloom the chrome and paint outside the pane. */
                if (gpu_cine) { gpu_cinematic_post(); sync_shadow_buffers(); gpu_cine_pace(); }
                else if (!gpu_vp_active) gpu_atmosphere_glow();
                /* A pane renders inside a desktop that is not otherwise
                   redrawing, and the guest's loop has nothing to wait on, so
                   this path free-runs: measured at 271 fps on the desk's 3D
                   view. Nothing consumes frames at that rate, and producing
                   them saturates the rasterizer threads and the display copy,
                   which is why the paced software renderer could feel smoother
                   than the unpaced fast one. The sleep lands inside a port
                   write, so the guest thread idles rather than spinning. Only
                   the pane path is paced: a program that owns the screen keeps
                   the behaviour it has always had. */
                else gpu_cine_pace();
                /* One shadow map serves one main pass. A frame that wants
                   shadows re-arms; a frame that does not simply stops sending
                   the size, so nothing has to disarm. */
                gpu_shadow_pending = 0;
            }
        }
        /* Viewport origin: (x0 << 16) | y0. See gpu_clip_rect for why 0x403. */
        else if (port == 0x403) {
            gpu_vp_x0 = (int)((val >> 16) & 0xFFFF);
            gpu_vp_y0 = (int)(val & 0xFFFF);
        }
        /* Viewport extent, inclusive: (x1 << 16) | y1. Zero disarms, which is
           unambiguous because an armed viewport whose far corner is the origin
           would be a single pixel at (0,0) and no caller wants that. */
        else if (port == 0x40F) {
            if (val == 0) {
                gpu_vp_active = 0;
            } else {
                gpu_vp_x1 = (int)((val >> 16) & 0xFFFF);
                gpu_vp_y1 = (int)(val & 0xFFFF);
                gpu_vp_active = 1;
            }
        }
        else if (port == 0x410) {
            gpu_cine = (int)val;
        }
        /* Guest-armable page-watch (ports 0x411 lo, 0x412 hi, 0x413 arm) */
        else if (port == 0x411) { guest_watch_lo = (unsigned int)val; }
        else if (port == 0x412) { guest_watch_hi = (unsigned int)val; }
        else if (port == 0x413) {
            /* Read the guest's live R10 (bump allocator) at this exit: that is
               exactly where the very next heap allocation (the concat result)
               will land, with no staleness from act-block machinery. */
            WHV_REGISTER_NAME rn = WHvX64RegisterR10;
            WHV_REGISTER_VALUE rv; memset(&rv, 0, sizeof(rv));
            WHvGetVirtualProcessorRegisters(partition, 0, &rn, 1, &rv);
            watch_addr = rv.Reg64;
            watch_size = 64;      /* cover result+0..63 */
            watch_val_set = 0;    /* report every write to the watched bytes */
            watch_report_all = 1; /* log every changed slot + RIP, keep running */
            watch_hit_count = 0;
            fprintf(stderr, "GUEST-ARM WATCH: addr=0x%llx size=%d (guest port 0x413, tag=%llu)\n",
                    watch_addr, watch_size, (unsigned long long)val);
            watch_init();
        }
        /* Guest-armed DR (hardware) watch at live R10 + val(offset). DR traps
           AFTER the write (no single-step) so it works under demand where the
           page-watch's single-step-over-write fails. #DB is host-intercepted. */
        else if (port == 0x414) {
            WHV_REGISTER_NAME rn = WHvX64RegisterR10;
            WHV_REGISTER_VALUE rv; memset(&rv, 0, sizeof(rv));
            WHvGetVirtualProcessorRegisters(partition, 0, &rn, 1, &rv);
            hw_watch_addr = rv.Reg64 + (unsigned long long)val;
            hw_watch_len = 8; hw_watch_rw = 1; hw_watch_active = 1; hw_watch_hits = 0;
            fprintf(stderr, "GUEST-ARM HWWATCH: DR0=0x%llx (R10=0x%llx + off=%llu)\n",
                    hw_watch_addr, rv.Reg64, (unsigned long long)val);
            hw_watch_init();
        }
        /* Arm DR at an EXPLICIT guest-provided address (0x411 lo, 0x412 hi) + val offset. */
        else if (port == 0x415) {
            unsigned long long a = ((unsigned long long)guest_watch_hi << 32) | guest_watch_lo;
            hw_watch_addr = a + (unsigned long long)val;
            hw_watch_len = 8; hw_watch_rw = 1; hw_watch_active = 1; hw_watch_hits = 0;
            fprintf(stderr, "GUEST-ARM HWWATCH(explicit): DR0=0x%llx (addr=0x%llx + off=%llu)\n",
                    hw_watch_addr, a, (unsigned long long)val);
            hw_watch_init();
        }
        /* Print an explicit guest-provided address (0x411 lo, 0x412 hi) + hexdump 64B. */
        else if (port == 0x416) {
            unsigned long long a = ((unsigned long long)guest_watch_hi << 32) | guest_watch_lo;
            fprintf(stderr, "GUEST-ADDR: 0x%llx (tag=%llu) bytes=", a, (unsigned long long)val);
            if (a + 64 <= guest_mem_size)
                for (int i = 0; i < 64; i++) fprintf(stderr, "%02x", *((unsigned char*)guest_mem + a + i));
            fprintf(stderr, "\n");
        }
        else if (port == 0x401) {
            gpu_clear_fb(val);
        }
        else if (port == 0x40E) {
            gpu_fade_clear(val);
        }
        /* Zero keeps the original meaning, a main depth clear. A size arms the
           shadow map instead, which is why this needed no new port: there is
           none free inside the 0x400-0x417 window the capability guard covers. */
        else if (port == 0x402) {
            if (val == 0) gpu_clear_depth();
            else gpu_shadow_begin((int)val);
        }
        else if (port == 0x404) {
            gpu_light[0] = (float)(int)val / 1000.0f;
        }
        else if (port == 0x405) {
            gpu_light[1] = (float)(int)val / 1000.0f;
        }
        else if (port == 0x406) {
            gpu_light[2] = (float)(int)val / 1000.0f;
        }
        else if (port == 0x407) {
            gpu_eye[0] = (float)(int)val / 1000.0f;
            gpu_eye[1] = gpu_light[1]; gpu_eye[2] = gpu_light[2];
        }
        else if (port == 0x408) {
            /* Mask to 32 bits before widening. The port carries a guest address,
               and any address at or above 2 GB has bit 31 set, so widening the
               signed port value directly sign-extends it: #BE800000 arrived as
               0xFFFFFFFFBE800000, the bounds check below rejected it, and the
               upload was skipped in silence. Every buffer this rasterizer uses
               lives in the [2GB,3GB) band, so this is the normal case, not an
               edge one. */
            gpu_tex_guest_addr = (unsigned long long)(unsigned int)val;
        }
        else if (port == 0x409) {
            gpu_tex_upload_w = (int)val;
        }
        else if (port == 0x40A) {
            gpu_tex_upload_h = (int)val;
        }
        else if (port == 0x40B) {
            /* Commit: copy texture from guest RAM to rasterizer */
            if (gpu_tex_upload_w > 0 && gpu_tex_upload_h > 0) {
                /* The committed value declares the texture completely: 0 is the
                   original wire, three packed RGB bytes a pixel written with
                   poke-byte, sampled bilinear as a globe; 1 is one 32-bit
                   0x00RRGGBB word a pixel, which is what a Codex EngineTexture
                   holds and what gpu-mem-write writes, sampled nearest and
                   shaded as an ordinary surface. TerrainGen has committed 0
                   since this port was written, so 0 must keep its meaning. */
                int mode = (val == 1) ? 1 : 0;
                unsigned long long sz = (unsigned long long)gpu_tex_upload_w * gpu_tex_upload_h * (mode == 1 ? 4 : 3);
                if (gpu_tex_guest_addr + sz <= guest_mem_size) {
                    /* This memcpy reads guest RAM from the host. If the guest
                       points us at a region it has never touched, those pages
                       are reserved and the read faults -- killing the VM rather
                       than the guest. Commit first: a bad texture address must
                       not be able to take the process down. */
                    guest_commit_range(gpu_tex_guest_addr, sz);
                    unsigned char *src = (unsigned char *)guest_mem + gpu_tex_guest_addr;
                    if (earth_tex_data) free(earth_tex_data);
                    earth_tex_data = (unsigned char *)malloc(sz);
                    if (earth_tex_data) {
                        memcpy(earth_tex_data, src, sz);
                        earth_tex_w = gpu_tex_upload_w;
                        earth_tex_h = gpu_tex_upload_h;
                        gpu_tex_mode = mode;
                        fprintf(stderr, "GPU texture uploaded from guest 0x%llx (%dx%d) mode %d\n",
                                gpu_tex_guest_addr, earth_tex_w, earth_tex_h, gpu_tex_mode);
                    }
                }
            }
        }
        else if (port == 0x40C) {
            asset_path_addr = (unsigned long long)val;
        }
        else if (port == 0x40D) {
            asset_dest_addr = (unsigned long long)val;
        }
        else if (port == 0x417) {
            /* Execute asset load: read file from host into guest RAM.
             * Not 0x40E: that is matched earlier in this chain as the
             * fireworks fade-clear, so an asset load fired at 0x40E only
             * ever faded the sky and left asset_last_size at zero. The
             * size is still read back on 0x40E/0x40F, which do not
             * collide: fade-clear is OUT-only. */
            asset_last_size = 0;
            if (asset_path_addr < guest_mem_size && asset_dest_addr < guest_mem_size) {
                char path[256];
                int pi = 0;
                while (pi < 255 && asset_path_addr + pi < guest_mem_size) {
                    char ch = (char)*((unsigned char *)guest_mem + asset_path_addr + pi);
                    if (ch == 0) break;
                    path[pi++] = ch;
                }
                path[pi] = 0;
                FILE *fp = fopen(path, "rb");
                if (fp) {
                    fseek(fp, 0, SEEK_END);
                    long sz = ftell(fp);
                    fseek(fp, 0, SEEK_SET);
                    if (sz > 0 && asset_dest_addr + (unsigned long long)sz <= guest_mem_size) {
                        /* The guest has never touched the destination, so it is
                           reserved, not committed. Without this the fread below
                           reads zero bytes and says nothing about it. */
                        guest_commit_range(asset_dest_addr, (unsigned long long)sz);
                        size_t got = fread((unsigned char *)guest_mem + asset_dest_addr, 1, (size_t)sz, fp);
                        asset_last_size = (unsigned long long)got;
                        if (got != (size_t)sz)
                            fprintf(stderr, "Asset short read: %s (%llu of %ld bytes -> guest 0x%llx)\n",
                                    path, (unsigned long long)got, sz, asset_dest_addr);
                        else
                            fprintf(stderr, "Asset loaded: %s (%ld bytes -> guest 0x%llx)\n", path, sz, asset_dest_addr);
                    }
                    fclose(fp);
                } else {
                    fprintf(stderr, "Asset not found: %s\n", path);
                }
            }
        }
        /* PCI Configuration Space */
        else if (port == 0xCF8) {
            pci_config_addr = (unsigned int)val;
        }
        else if (port >= 0xCFC && port <= 0xCFF) {
            if (pci_config_addr & 0x80000000) {
                int bus = (pci_config_addr >> 16) & 0xFF;
                int dev = (pci_config_addr >> 11) & 0x1F;
                int func = (pci_config_addr >> 8) & 0x7;
                int off = pci_config_addr & 0xFC;
                pci_write_config(bus, dev, func, off, (unsigned int)val);
            }
        }
        /* PC Speaker */
        else if (port == 0x61) {
            int was = speaker_enabled;
            speaker_enabled = (val & 3) == 3;
            if (speaker_enabled && !was && speaker_freq > 0) {
                unsigned int hz = 1193182 / speaker_freq;
                if (hz >= 37 && hz <= 32767) Beep(hz, 200);
            }
        }
        else if (port == 0x42) {
            if (!speaker_freq_latch) { speaker_freq = val & 0xFF; speaker_freq_latch = 1; }
            else { speaker_freq |= (val & 0xFF) << 8; speaker_freq_latch = 0; }
        }
        /* LAPIC disable via MSR is handled in handle_msr; ignore port 0xFEE00xx */
    } else {
        /* REP INSB/INSW from the NE2K data port: batch the whole remaining
           count in one exit, the mirror of the REP OUTSW batch above. The
           generic string-op IN path below moves ONE element and re-executes
           the instruction, so draining an RX ring cost one VM exit per word.

           Semantics are those of the generic path repeated, deliberately: the
           NIC is asked for each element in turn (rbcr runs out inside
           ne2k_handle_in exactly as it would across separate exits), and a
           destination outside guest memory SKIPS the store while still
           advancing RDI/RCX. Skipping rather than breaking is what keeps the
           REP terminating -- a break at done == 0 would re-execute into the
           same out-of-bounds address forever. A hostile RCX is capped per
           exit; the instruction simply re-executes for the remainder. */
        if (ctx->IoPortAccess.AccessInfo.StringOp && port == NE2K_BASE + 0x10) {
            unsigned long long gpa = ctx->IoPortAccess.Rdi;
            unsigned long long cnt = ctx->IoPortAccess.Rcx;
            unsigned long long done = 0;
            unsigned char *gmem = (unsigned char *)guest_mem;
            if (cnt > (1ULL << 20)) cnt = (1ULL << 20);
            guest_host_touch(gpa, cnt * (unsigned long long)size);
            for (; done < cnt; done++) {
                unsigned long long p = gpa + done * (unsigned long long)size;
                int rval = ne2k_handle_in(port, size);
                if (p + (unsigned long long)size > guest_mem_size) continue;
                if (size == 1) gmem[p] = rval & 0xFF;
                else if (size == 2) { gmem[p] = rval & 0xFF; gmem[p + 1] = (rval >> 8) & 0xFF; }
                else *(unsigned int *)(gmem + p) = (unsigned int)rval;
            }
            WHV_REGISTER_NAME sn[] = { WHvX64RegisterRdi, WHvX64RegisterRcx };
            WHV_REGISTER_VALUE sv[2];
            sv[0].Reg64 = ctx->IoPortAccess.Rdi + done * (unsigned long long)size;
            sv[1].Reg64 = ctx->IoPortAccess.Rcx - done;
            WHvSetVirtualProcessorRegisters(partition, 0, sn, 2, sv);
            if (ctx->IoPortAccess.Rcx - done == 0) {
                WHV_REGISTER_NAME rn = WHvX64RegisterRip;
                WHV_REGISTER_VALUE rv;
                rv.Reg64 = ctx->VpContext.Rip + ctx->VpContext.InstructionLength;
                WHvSetVirtualProcessorRegisters(partition, 0, &rn, 1, &rv);
            }
            return;
        }
        /* REP INSW from the IDE data port: a READ SECTORS data phase, batched
           for the same reason as the NE2K arm above and the OUT arm on the
           write side. The generic string path below serves one word per exit,
           and the census says that is where the reads still are: fat16-write
           took 44,296 string exits of which only 8 were the batched write arm,
           so all but a sector's worth were reads paying an exit each.

           ide_read_data carries its own sector bookkeeping and calls
           ide_advance when a sector drains, so the batch sees exactly what
           separate exits would have seen. A destination outside guest memory
           SKIPS the store and still advances RDI/RCX: breaking at done == 0
           would re-execute into the same bad address forever. */
        if (ctx->IoPortAccess.AccessInfo.StringOp && port == 0x1F0) {
            unsigned long long gpa = ctx->IoPortAccess.Rdi;
            unsigned long long cnt = ctx->IoPortAccess.Rcx;
            unsigned long long done = 0;
            unsigned char *gmem = (unsigned char *)guest_mem;
            if (cnt > (1ULL << 20)) cnt = (1ULL << 20);
            guest_host_touch(gpa, cnt * (unsigned long long)size);
            for (; done < cnt; done++) {
                unsigned long long p = gpa + done * (unsigned long long)size;
                int rval = no_ide ? 0xFF : ide_read_data(ide_active());
                if (p + (unsigned long long)size > guest_mem_size) continue;
                if (size == 1) gmem[p] = rval & 0xFF;
                else if (size == 2) { gmem[p] = rval & 0xFF; gmem[p + 1] = (rval >> 8) & 0xFF; }
                else *(unsigned int *)(gmem + p) = (unsigned int)rval;
            }
            ide_in_batch_hits++;
            ide_in_batched += done;
            WHV_REGISTER_NAME sn[] = { WHvX64RegisterRdi, WHvX64RegisterRcx };
            WHV_REGISTER_VALUE sv[2];
            sv[0].Reg64 = ctx->IoPortAccess.Rdi + done * (unsigned long long)size;
            sv[1].Reg64 = ctx->IoPortAccess.Rcx - done;
            WHvSetVirtualProcessorRegisters(partition, 0, sn, 2, sv);
            if (ctx->IoPortAccess.Rcx - done == 0) {
                WHV_REGISTER_NAME rn = WHvX64RegisterRip;
                WHV_REGISTER_VALUE rv;
                rv.Reg64 = ctx->VpContext.Rip + ctx->VpContext.InstructionLength;
                WHvSetVirtualProcessorRegisters(partition, 0, &rn, 1, &rv);
            }
            return;
        }
        int result = 0xFF;
        /* COM1 IN: serve input data to guest via UART emulation.
           Initial load goes into ring buffer directly; overflow bytes
           are served one at a time through COM1 data port.  The guest's
           uart-poll-drain reads LSR, sees data-ready, reads the byte,
           and writes it into the ring buffer itself. */
        if (port >= 0x3F8 && port <= 0x3FF) {
            if (port == 0x3FD) {
                int did_reinject = 0;
                if (input_file && guest_mem) {
                    unsigned long long cur = *(unsigned long long *)((unsigned char *)guest_mem + 28704);
                    if (cur == 0 && input_total_written > 0) {
                        *(unsigned long long *)((unsigned char *)guest_mem + 28704) = input_total_written;
                        *(unsigned long long *)((unsigned char *)guest_mem + 28712) = 0ULL;
                        did_reinject = 1;
                    }
                }
                result = 0x60;  /* no UART data; overflow is handled by drip-feed */
            }
            else if (port == 0x3F8) {
                result = 0;
            }
            else result = 0;
        }
        /* COM3 IN: the GPU compute bridge serves its reply.
           LSR bit 5 is transmitter-ready (always -- the bridge consumes a
           byte per OUT); bit 0 is receiver-ready, and it is set only while
           a reply is actually pending. The old 0xFF default asserted both
           unconditionally, which is why the guest read 0xFFFFFFFF out of an
           empty bridge instead of waiting for one. */
        else if (port >= 0x3E8 && port <= 0x3EF) {
            /* An idle UART: transmitter ready, nothing to receive. The
               compute reply is collected through the doorbell's length
               port, not read back a byte at a time. */
            result = (port == 0x3ED) ? 0x20 : 0;
        }
        /* Doorbell reply length: 0 means the dispatch was refused. */
        else if (port >= COM3_PORT_CMD_ADDR && port <= COM3_PORT_REPLY_LEN) {
            com3_io_exits++;
            result = (port == COM3_PORT_REPLY_LEN) ? com3.reply_len : 0;
        }
        /* COM2 IN: signal EOF when guest has consumed all input */
        else if (port >= 0x2F8 && port <= 0x2FF) {
            if (port == 0x2FD) {
                int eof_ready = 0;
                if (guest_mem) {
                    if (!input_file) {
                        eof_ready = 1;
                    } else {
                        unsigned long long wpos = *(unsigned long long *)((unsigned char *)guest_mem + 28704);
                        unsigned long long rpos = *(unsigned long long *)((unsigned char *)guest_mem + 28712);
                        int all_fed = !input_overflow || input_overflow_pos >= input_overflow_len;
                        if (wpos > 0 && rpos >= wpos && all_fed) eof_ready = 1;
                    }
                }
                result = eof_ready ? 0x61 : 0x60;
            }
            else if (port == 0x2F8) result = 0x04;
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
        /* PIT counter reads. A real counter counts DOWN from the reload
           value at 1.193182 MHz and wraps, and a guest calibrating a
           delay reads it twice and subtracts. It used to answer 0 every
           time, which is indistinguishable from a stopped clock -- and,
           being constant, is exactly the shape a test cannot catch.
           Access mode 3 returns low byte then high byte on successive
           reads, which is the same latch the write path uses. */
        else if (port >= 0x40 && port <= 0x43) {
            if (port == 0x43) {
                result = 0;   /* the command register is write-only */
            } else {
                int ch = port - 0x40;
                /* A latched channel answers the frozen value until the
                   guest has taken all of it; the latch then lifts and
                   reads go back to the live counter. */
                unsigned int count = pit_latch_valid[ch]
                                   ? (unsigned int)pit_latched[ch]
                                   : pit_current_count(ch);
                if (pit_access[ch] == 3) {
                    if (!pit_read_hi[ch]) { result = count & 0xFF; pit_read_hi[ch] = 1; }
                    else {
                        result = (count >> 8) & 0xFF;
                        pit_read_hi[ch] = 0;
                        pit_latch_valid[ch] = 0;
                    }
                } else if (pit_access[ch] == 2) {
                    result = (count >> 8) & 0xFF;
                    pit_latch_valid[ch] = 0;
                } else {
                    result = count & 0xFF;
                    pit_latch_valid[ch] = 0;
                }
            }
        }
        /* IDE */
        else if ((port >= 0x1F0 && port <= 0x1F7) || port == 0x3F6) {
            result = ide_bus_in(port);
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
        /* CMOS RTC (0x71 read -- return BCD time from host clock) */
        else if (port == 0x71) {
            SYSTEMTIME st;
            int uip;
            if (rtc_fixed) { st = rtc_fixed_st; uip = 0; }
            else { GetLocalTime(&st); uip = rtc_updating(&st); }
            switch (cmos_index) {
            /* Time registers are UNDEFINED while an update is in progress.
               A guest must wait for Status A bit 7 to clear before reading
               them; one that does not gets junk, here as on the metal. */
            case 0:  result = uip ? rtc_junk() : (((st.wSecond / 10) << 4) | (st.wSecond % 10)); break;
            case 2:  result = uip ? rtc_junk() : (((st.wMinute / 10) << 4) | (st.wMinute % 10)); break;
            case 4:  result = uip ? rtc_junk() : (((st.wHour / 10) << 4) | (st.wHour % 10)); break;
            case 6:  result = uip ? rtc_junk() : (st.wDayOfWeek + 1); break;
            case 7:  result = uip ? rtc_junk() : (((st.wDay / 10) << 4) | (st.wDay % 10)); break;
            case 8:  result = uip ? rtc_junk() : (((st.wMonth / 10) << 4) | (st.wMonth % 10)); break;
            case 9:  result = uip ? rtc_junk() : ((((st.wYear % 100) / 10) << 4) | (st.wYear % 10)); break;
            case 10: result = (uip ? 0x80 : 0x00) | 0x26; break; /* Status A, UIP in bit 7 */
            case 11: result = 0x02; break; /* Status B: 24h mode, BCD */
            case 12: result = 0;    break; /* Status C */
            case 13: result = 0x80; break; /* Status D: valid RAM */
            default: result = 0;    break;
            }
        }
        /* Bochs VBE */
        else if (port == VBE_DATA_PORT) {
            result = vbe_regs[vbe_index];
        }
        /* GPU capability check */
        else if (port == 0x403) {
            result = 1;  /* GPU rasterizer present */
        }
        /* Bulk output blit capability probe */
        else if (port == BLIT_PROBE_PORT) {
            result = BLIT_PROBE_MAGIC;
        }
        else if (port == 0x40E) {
            result = (unsigned int)(asset_last_size & 0xFFFFFFFF);
        }
        else if (port == 0x40F) {
            result = (unsigned int)((asset_last_size >> 32) & 0xFFFFFFFF);
        }
        /* PCI Configuration Space */
        else if (port >= 0xCFC && port <= 0xCFF) {
            if (pci_config_addr & 0x80000000) {
                int bus = (pci_config_addr >> 16) & 0xFF;
                int dev = (pci_config_addr >> 11) & 0x1F;
                int func = (pci_config_addr >> 8) & 0x7;
                int off = pci_config_addr & 0xFC;
                unsigned int val32 = pci_read_config(bus, dev, func, off);
                int byte_off = port - 0xCFC;
                result = (int)((val32 >> (byte_off * 8)) & (size == 1 ? 0xFF : size == 2 ? 0xFFFF : 0xFFFFFFFF));
            }
        }
        /* PC Speaker state */
        else if (port == 0x61) {
            result = speaker_enabled ? 3 : 0;
        }
        /* Keyboard controller */
        else if (port == 0x60) {
            int sc = kbd_dequeue();
            result = (sc >= 0) ? sc : 0;
        }
        else if (port == 0x64) {
            result = (kbd_count > 0) ? 1 : 0; /* bit 0 = OBF (output buffer full) */
        }
        /* Mouse data via I/O ports (avoids WHP host-guest memory coherency issues).
           0xE1: button mask -- the live level OR the presses latched since the
                 last read, so a click entirely between two guest polls is still
                 delivered exactly once. Reading it consumes the latch.
           0xE2: absolute x.  0xE3: absolute y (reading it consumes 0xE4).
           0xE4: a new mouse event has arrived since 0xE3 was last read. It gates
                 the position reads only -- never the button read, which is a level
                 and stays valid between events. */
        else if (port == 0xE1) {
            LONG latched = InterlockedExchange(&pending_mouse_btn_latch, 0);
            result = pending_mouse_btn | (int)latched;
        }
        else if (port == 0xE2) { result = pending_mouse_abs_x & 0xFFFF; }
        else if (port == 0xE3) { result = pending_mouse_abs_y & 0xFFFF; pending_mouse_valid = 0; }
        else if (port == 0xE4) { result = pending_mouse_valid; }

        if (ctx->IoPortAccess.AccessInfo.StringOp) {
            /* REP INSW/INSB: write to guest memory at [RDI], update RDI and RCX */
            unsigned long long gpa = ctx->IoPortAccess.Rdi;
            unsigned char *gmem = (unsigned char *)guest_mem;
            guest_host_touch(gpa, (unsigned long long)size);
            if (gpa < guest_mem_size) {
                if (size == 1) gmem[gpa] = result & 0xFF;
                else if (size == 2 && gpa + 1 < guest_mem_size) { gmem[gpa] = result & 0xFF; gmem[gpa + 1] = (result >> 8) & 0xFF; }
                else if (size == 4 && gpa + 3 < guest_mem_size) { *(unsigned int *)(gmem + gpa) = result; }
            }
            /* Update RDI and RCX */
            WHV_REGISTER_NAME str_names[] = { WHvX64RegisterRdi, WHvX64RegisterRcx };
            WHV_REGISTER_VALUE str_vals[2];
            str_vals[0].Reg64 = ctx->IoPortAccess.Rdi + size;
            str_vals[1].Reg64 = ctx->IoPortAccess.Rcx - 1;
            WHvSetVirtualProcessorRegisters(partition, 0, str_names, 2, str_vals);
            if (ctx->IoPortAccess.Rcx <= 1) {
                /* REP complete: advance RIP */
                WHV_REGISTER_NAME rip_name = WHvX64RegisterRip;
                WHV_REGISTER_VALUE rip_val;
                rip_val.Reg64 = ctx->VpContext.Rip + ctx->VpContext.InstructionLength;
                WHvSetVirtualProcessorRegisters(partition, 0, &rip_name, 1, &rip_val);
            }
            /* else: don't advance RIP, re-execute REP instruction */
        } else {
            /* Regular I/O IN: inject result into RAX */
            WHV_REGISTER_NAME rax_name = WHvX64RegisterRax;
            WHV_REGISTER_VALUE rax_val;
            WHvGetVirtualProcessorRegisters(partition, 0, &rax_name, 1, &rax_val);
            if (size == 1) rax_val.Reg64 = (rax_val.Reg64 & ~0xFFULL) | (result & 0xFF);
            else if (size == 2) rax_val.Reg64 = (rax_val.Reg64 & ~0xFFFFULL) | (result & 0xFFFF);
            /* A 32-bit write to a GPR zeroes the upper half of the 64-bit
               register; `result` is a signed int, so assigning it directly
               SIGN-extended every value with bit 31 set and the guest saw
               0xFFFFFFFF80000000-style garbage where hardware gives a clean
               dword. Measured 2026-07-30: a PCI vendor/device dword of
               0x8C318086 reached the guest as -1942912890. */
            else rax_val.Reg64 = (unsigned int)result;
            WHvSetVirtualProcessorRegisters(partition, 0, &rax_name, 1, &rax_val);
            /* Advance RIP past the I/O instruction */
            WHV_REGISTER_NAME rip_name = WHvX64RegisterRip;
            WHV_REGISTER_VALUE rip_val;
            rip_val.Reg64 = ctx->VpContext.Rip + ctx->VpContext.InstructionLength;
            WHvSetVirtualProcessorRegisters(partition, 0, &rip_name, 1, &rip_val);
        }
    }

    if (is_out) {
        /* Advance RIP past the I/O instruction */
        WHV_REGISTER_NAME rip_name = WHvX64RegisterRip;
        WHV_REGISTER_VALUE rip_val;
        rip_val.Reg64 = ctx->VpContext.Rip + ctx->VpContext.InstructionLength;
        WHvSetVirtualProcessorRegisters(partition, 0, &rip_name, 1, &rip_val);
    }
}

static void handle_cpuid(WHV_RUN_VP_EXIT_CONTEXT *ctx) {
    unsigned long long leaf = ctx->CpuidAccess.Rax;
    WHV_REGISTER_NAME names[] = { WHvX64RegisterRax, WHvX64RegisterRbx, WHvX64RegisterRcx, WHvX64RegisterRdx, WHvX64RegisterRip };
    WHV_REGISTER_VALUE vals[5];
    memset(vals, 0, sizeof(vals));
    if (leaf == 0) { vals[0].Reg64 = 1; vals[1].Reg64 = 0x756E6547; vals[2].Reg64 = 0x6C65746E; vals[3].Reg64 = 0x49656E69; }
    /* Leaf 1 ecx bit 31 is the hypervisor-present bit (Intel SDM 3.1.2.1,
       reserved for that use); a guest asking is told the truth. Leaves
       8000_0002h..04h carry a brand string so a diagnostic names the part it
       is running on rather than reporting one absent (2026-08-18). */
    else if (leaf == 1) { vals[0].Reg64 = 0x000306C3; vals[2].Reg64 = 0x80000000ULL; vals[3].Reg64 = 0x078BFBFF; }
    else if (leaf == 0x80000000) { vals[0].Reg64 = 0x80000004; }
    else if (leaf == 0x80000001) { vals[3].Reg64 = (1 << 29) | (1 << 20); } /* LM + NX */
    else if (leaf >= 0x80000002 && leaf <= 0x80000004) {
        static const char brand[48] = "codex-vm virtual CPU (WHP)";
        const char *b = brand + (leaf - 0x80000002) * 16;
        memcpy(&vals[0].Reg64, b, 4); memcpy(&vals[1].Reg64, b + 4, 4);
        memcpy(&vals[2].Reg64, b + 8, 4); memcpy(&vals[3].Reg64, b + 12, 4);
    }
    vals[4].Reg64 = ctx->VpContext.Rip + ctx->VpContext.InstructionLength;
    WHvSetVirtualProcessorRegisters(partition, 0, names, 5, vals);
}

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
        case 0x1B: msr_apic_base = write_val; break; /* IA32_APIC_BASE -- just store */
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
    return 0;  /* serial removed -- memory-mapped I/O, no IRQ needed */
}

/* (monitor thread removed -- page protection provides precise trapping) */

/* ── GPU triangle rasterizer (host-side, native speed) ──────────────── */
/*
 * Command buffer at GPU_CMD_ADDR in guest RAM.  Each triangle is 8 ints:
 *   [0] x0  [1] y0  [2] x1  [3] y1  [4] x2  [5] y2  [6] color  [7] depth
 * Port 0x400 OUT: value = triangle count, rasterize all triangles
 * Port 0x401 OUT: value = XRGB color, clear framebuffer
 * Port 0x402 OUT: value = 0, clear depth buffer; value = N, arm an NxN shadow map
 * Port 0x403 IN:  returns 1 (GPU present capability check)
 * Port 0x403 OUT: viewport origin (x0 << 16) | y0
 * Port 0x40F OUT: viewport extent, inclusive (x1 << 16) | y1; 0 disarms
 * Port 0x400 OUT with GPU_SHADOW_FLAG: depth-only pass into the shadow map
 */
#define GPU_CMD_ADDR   0xBE000000ULL
/* Command-buffer capacity. The rasterizer used to stop dead at 16384
   triangles while the buffer was sized for exactly that many -- a guest that
   submitted more (circuits draws ~18k for a busy schematic) had every
   triangle past the cap silently dropped. A frame is drawn back to front, so
   what vanished was whatever was drawn LAST: the tail of a text panel, the
   dropdown menu, the help overlay. Nothing was reported. The cap is now the
   real capacity of the region between the command buffer and the depth
   buffer, and an overflow says so once instead of quietly eating the top of
   the frame. */
#define GPU_MAX_TRIS   65536
#define GPU_CMD_SIZE   (GPU_MAX_TRIS * 72)
#define GPU_DEPTH_ADDR 0xBE800000ULL
#define GPU_DEPTH_FAR  999999

/* Viewport (scissor) rect, inclusive. The rasterizer draws fullscreen to the
   GOP framebuffer, which is correct for a demo that owns the screen and wrong
   for a pane inside a desktop: the clear wipes the chrome and geometry lands
   over it. Armed, every write below is confined to the rect.

   The two ports are 0x403 and 0x40F because the compiler's cap guard covers
   0x400-0x417 and nothing above (`gpu-port-hi` in X86_64Boot.codex), so a port
   at 0x418 would be reachable without holding Gpu.Compute. Both are IN-only
   today (GPU present probe, asset-size high word) and IN and OUT are separate
   directions, so the OUT side of each was free -- the same overloading the
   asset loader documents at port 0x417. */
static void gpu_clip_rect(int w, int h, int *cx0, int *cy0, int *cx1, int *cy1) {
    if (gpu_vp_active) {
        *cx0 = gpu_vp_x0 < 0 ? 0 : gpu_vp_x0;
        *cy0 = gpu_vp_y0 < 0 ? 0 : gpu_vp_y0;
        *cx1 = gpu_vp_x1 >= w ? w - 1 : gpu_vp_x1;
        *cy1 = gpu_vp_y1 >= h ? h - 1 : gpu_vp_y1;
    } else {
        *cx0 = 0; *cy0 = 0; *cx1 = w - 1; *cy1 = h - 1;
    }
}

/* A frame arrives as a clear on one port and a draw on another, and the
   rasterizer writes straight into the GOP framebuffer the display thread is
   reading. Between those two writes the pane holds nothing but the clear
   colour, and the display samples it there often enough to see it: captured
   headless it produced a frame of pure sky with the label still drawn beside
   it, and on the glass it reads as the pane flickering while it animates.

   So a frame that is confined to a VIEWPORT is built in this back buffer and
   presented in one copy at the end. Only that case changes: a program that
   owns the whole screen has no viewport armed, still draws directly, and keeps
   whatever timing it has today. Nothing but the rasterizer draws inside an
   armed rect -- gsc-frame deliberately holds its label outside the band -- so
   there is nothing in there for the copy to overwrite. */
static unsigned int *gpu_back = NULL;

static unsigned int *gpu_target_fb(void) {
    unsigned int *fb = (unsigned int *)((unsigned char *)guest_mem + GOP_FB_ADDR);
    if (!gpu_vp_active) return fb;
    if (!gpu_back) gpu_back = (unsigned int *)calloc(GOP_MAX_W * GOP_MAX_H, sizeof(unsigned int));
    return gpu_back ? gpu_back : fb;
}

static void gpu_present_viewport(void) {
    if (!gpu_vp_active || !gpu_back) return;
    unsigned int *fb = (unsigned int *)((unsigned char *)guest_mem + GOP_FB_ADDR);
    int cx0, cy0, cx1, cy1;
    gpu_clip_rect(gop_width, gop_height, &cx0, &cy0, &cx1, &cy1);
    if (cx1 < cx0) return;
    size_t span = (size_t)(cx1 - cx0 + 1) * sizeof(unsigned int);
    for (int y = cy0; y <= cy1; y++) {
        size_t off = (size_t)y * gop_width + cx0;
        memcpy(fb + off, gpu_back + off, span);
    }
}

static int gpu_last_clear_color = 0;

static void gpu_clear_fb(int color) {
    gpu_last_clear_color = color;
    gpu_frame_ready = 0;
    if (gop_host_gpu_refuses()) return;
    if (!gop_active || GOP_FB_ADDR + (unsigned long long)gop_width * gop_height * 4 > guest_mem_size) return;
    unsigned int *fb = gpu_target_fb();
    int cx0, cy0, cx1, cy1;
    gpu_clip_rect(gop_width, gop_height, &cx0, &cy0, &cx1, &cy1);
    for (int y = cy0; y <= cy1; y++) {
        unsigned int *row = fb + (size_t)y * gop_width;
        for (int x = cx0; x <= cx1; x++) row[x] = (unsigned int)color;
    }
}

/* Persistence clear: blend the frame ~7/8 toward the target color instead
   of wiping it, so moving sparks leave fading long-exposure trails. */
static void gpu_fade_clear(int color) {
    gpu_frame_ready = 0;
    if (gop_host_gpu_refuses()) return;
    if (!gop_active || GOP_FB_ADDR + (unsigned long long)gop_width * gop_height * 4 > guest_mem_size) return;
    unsigned int *fb = (unsigned int *)((unsigned char *)guest_mem + GOP_FB_ADDR);
    int total = gop_width * gop_height;
    int tr = (color>>16)&0xFF, tg = (color>>8)&0xFF, tb = color&0xFF;
    for (int i = 0; i < total; i++) {
        unsigned int p = fb[i];
        int r = (p>>16)&0xFF, g = (p>>8)&0xFF, b = p&0xFF;
        r = tr + (r - tr) * 7 / 8;
        g = tg + (g - tg) * 7 / 8;
        b = tb + (b - tb) * 7 / 8;
        fb[i] = ((unsigned int)r<<16)|((unsigned int)g<<8)|(unsigned int)b;
    }
}

static void gpu_clear_depth(void) {
    if (gop_host_gpu_refuses()) return;
    if (GPU_DEPTH_ADDR + (unsigned long long)gop_width * gop_height * 4 > guest_mem_size) return;
    unsigned int *db = (unsigned int *)((unsigned char *)guest_mem + GPU_DEPTH_ADDR);
    int cx0, cy0, cx1, cy1;
    gpu_clip_rect(gop_width, gop_height, &cx0, &cy0, &cx1, &cy1);
    for (int y = cy0; y <= cy1; y++) {
        unsigned int *row = db + (size_t)y * gop_width;
        for (int x = cx0; x <= cx1; x++) row[x] = GPU_DEPTH_FAR;
    }
}

static inline long long gpu_edge(int ax, int ay, int bx, int by, int px, int py);

/* Size the light-space depth buffer and clear it to far. The guest sends the
   size rather than the emulator picking one, so the shadow map matches the
   software renderer's own map-size argument. */
static void gpu_shadow_begin(int size) {
    if (size < 16) size = 16;
    if (size > 4096) size = 4096;
    if (size != gpu_shadow_size || !gpu_shadow_buf) {
        unsigned int *nb = (unsigned int *)realloc(gpu_shadow_buf, (size_t)size * size * 4);
        if (!nb) return;
        gpu_shadow_buf = nb;
        gpu_shadow_size = size;
    }
    int total = gpu_shadow_size * gpu_shadow_size;
    for (int i = 0; i < total; i++) gpu_shadow_buf[i] = GPU_DEPTH_FAR;
    gpu_shadow_pending = 1;
}

/* Depth-only rasterization into the shadow map. Deliberately not
   gpu_rasterize_band: this target is size x size rather than the screen, the
   viewport is a screen rect and must not apply in light space, and at a few
   hundred triangles over a 256x256 buffer the threading would cost more than
   it saves. */
static void gpu_shadow_render(int count) {
    if (!gpu_shadow_buf || gpu_shadow_size <= 0) return;
    if (GPU_CMD_ADDR + (unsigned long long)count * 72 > guest_mem_size) return;
    unsigned char *cmd = (unsigned char *)guest_mem + GPU_CMD_ADDR;
    int s = gpu_shadow_size;
    for (int t = 0; t < count && t < GPU_MAX_TRIS; t++) {
        int *tri = (int *)(cmd + t * 72);
        int x0 = tri[0], y0 = tri[1], x1 = tri[2], y1 = tri[3], x2 = tri[4], y2 = tri[5];
        int d0 = tri[9], d1 = tri[10], d2 = tri[11];
        int minx = x0 < x1 ? (x0 < x2 ? x0 : x2) : (x1 < x2 ? x1 : x2);
        int miny = y0 < y1 ? (y0 < y2 ? y0 : y2) : (y1 < y2 ? y1 : y2);
        int maxx = x0 > x1 ? (x0 > x2 ? x0 : x2) : (x1 > x2 ? x1 : x2);
        int maxy = y0 > y1 ? (y0 > y2 ? y0 : y2) : (y1 > y2 ? y1 : y2);
        if (minx < 0) minx = 0; if (miny < 0) miny = 0;
        if (maxx >= s) maxx = s - 1; if (maxy >= s) maxy = s - 1;
        if (minx > maxx || miny > maxy) continue;
        long long area = gpu_edge(x0, y0, x1, y1, x2, y2);
        if (area == 0) continue;
        int sign = area > 0 ? 1 : -1;
        long long abs_area = area > 0 ? area : -area;
        for (int y = miny; y <= maxy; y++) {
            for (int x = minx; x <= maxx; x++) {
                long long bw0 = gpu_edge(x1, y1, x2, y2, x, y) * sign;
                long long bw1 = gpu_edge(x2, y2, x0, y0, x, y) * sign;
                long long bw2 = gpu_edge(x0, y0, x1, y1, x, y) * sign;
                if (bw0 >= 0 && bw1 >= 0 && bw2 >= 0) {
                    int depth = (int)(((long long)d0 * bw0 + (long long)d1 * bw1 + (long long)d2 * bw2) / abs_area);
                    int idx = y * s + x;
                    if ((unsigned int)depth < gpu_shadow_buf[idx]) gpu_shadow_buf[idx] = (unsigned int)depth;
                }
            }
        }
    }
}

/* 64-bit: the guest hands us raw screen coordinates with nothing clipping
   them, so a triangle that straddles the viewport can be thousands of pixels
   wide and its edge products do not fit in 32 bits. The barycentric weights
   this returns are then multiplied by depth (up to 1e6) and summed, which
   overflows int32 for any triangle bigger than roughly 46x46 pixels. */
static inline long long gpu_edge(int ax, int ay, int bx, int by, int px, int py) {
    return (long long)(bx - ax) * (py - ay) - (long long)(by - ay) * (px - ax);
}

/*
 * Gouraud triangle rasterizer with per-vertex color, depth, and UV interpolation.
 * Command format: 72 bytes per triangle (18 ints):
 *   [0] x0 [1] y0 [2] x1 [3] y1 [4] x2 [5] y2
 *   [6] color0 [7] color1 [8] color2
 *   [9] depth0 [10] depth1 [11] depth2
 *   [12] u0*1000 [13] v0*1000 [14] u1*1000 [15] v1*1000 [16] u2*1000 [17] v2*1000
 * If u0==0 && v0==0 && u1==0 && v1==0: use vertex colors (Gouraud mode)
 * Otherwise: sample procedural Earth texture from interpolated UV
 */

/* ── Procedural Earth texture ─────────────────────────────────────── */
#include <math.h>

static int land_shape(float lat, float lon, float clat, float clon_base, float clon_slope,
                      float w_base, float w_slope, float w_center, float lat_lo, float lat_hi, float cn) {
    if (lat < lat_lo || lat > lat_hi) return 0;
    float clon = clon_base + lat * clon_slope;
    float w = w_base - fabsf(lat - w_center) * w_slope + cn * 1.2f;
    if (w < 3) w = 3;
    return (lon > clon - w && lon < clon + w) ? 1 : 0;
}

static int earth_is_land(float lat, float lon) {
    float cn = noise_smooth(lon * 0.35f + 50, lat * 0.45f + 50) * 2.0f - 1.0f;
    /* North America */
    if (lat > 25 && lat < 72 && lon > -170 && lon < -52) {
        float clon = -97 + (lat - 45) * 0.15f;
        float w = 25 - fabsf(lat - 42) * 0.4f + cn * 1.2f;
        if (lat > 60) w = 35 + cn;
        if (lat < 32) { clon = -102; w = 10 + cn * 0.8f; }
        if (w < 4) w = 4;
        if (lon > clon - w && lon < clon + w) return 1;
    }
    /* Central America */
    if (lat > 7 && lat < 25 && lon > -92 && lon < -77) {
        float w = 6 - fabsf(lat - 15) * 0.4f + cn * 0.8f;
        if (w > 0 && lon > -85 - w && lon < -85 + w) return 1;
    }
    /* Greenland */
    if (lat > 60 && lat < 83 && lon > -58 && lon < -12) {
        float w = 18 - fabsf(lat - 72) * 1.2f + cn;
        if (w > 0 && lon > -42 - w && lon < -42 + w) return 1;
    }
    /* South America */
    if (land_shape(lat, lon, 0, -57, 0.15f, 20, 0.38f, -15, -56, 13, cn)) return 1;
    /* Europe */
    if (lat > 36 && lat < 71 && lon > -12 && lon < 42) {
        float clon = 15 + (lat - 50) * 0.3f;
        float w = 20 - fabsf(lat - 50) * 0.45f + cn * 1.2f;
        if (lat < 40) { clon = 5; w = 10 + cn; }
        if (lat > 62) { clon = 18; w = 10 + cn; }
        if (w < 4) w = 4;
        if (lon > clon - w && lon < clon + w) return 1;
    }
    /* British Isles / Iceland */
    if (lat > 50 && lat < 66 && lon > -25 && lon < -4) {
        float w = 6 - fabsf(lat - 56) * 0.4f + cn * 0.5f;
        if (w > 0 && lon > -10 - w && lon < -10 + w) return 1;
    }
    /* Africa */
    if (land_shape(lat, lon, 0, 18, 0.05f, 28, 0.42f, 5, -35, 37, cn)) return 1;
    /* Arabian peninsula */
    if (lat > 12 && lat < 32 && lon > 34 && lon < 60) {
        float w = 10 - fabsf(lat - 22) * 0.5f + cn * 0.8f;
        if (w > 0 && lon > 47 - w && lon < 47 + w) return 1;
    }
    /* India */
    if (lat > 8 && lat < 35 && lon > 68 && lon < 90) {
        float clon = 79; float w = 10 - fabsf(lat - 22) * 0.5f + cn;
        if (w < 3) w = 3;
        if (lon > clon - w && lon < clon + w) return 1;
    }
    /* Asia mainland */
    if (lat > 25 && lat < 55 && lon > 60 && lon < 140) {
        float clon = 95 + (lat - 40) * 0.3f;
        float w = 32 - fabsf(lat - 40) * 0.5f + cn * 1.5f;
        if (w < 6) w = 6;
        if (lon > clon - w && lon < clon + w) return 1;
    }
    /* Siberia */
    if (lat > 50 && lat < 73 && lon > 42 && lon < 175) {
        float w = 55 - (lat - 58) * 2.5f;
        if (w < 5) w = 5;
        if (w > 0 && lon > 105 - w && lon < 105 + w) return 1;
    }
    /* Southeast Asia + Indonesia */
    if (lat > -8 && lat < 20 && lon > 95 && lon < 135) {
        float w = 15 - fabsf(lat - 8) * 0.7f + cn;
        if (w > 0 && lon > 110 - w && lon < 110 + w) return 1;
    }
    /* Japan */
    if (lat > 30 && lat < 45 && lon > 129 && lon < 145) {
        float w = 4 - fabsf(lat - 37) * 0.3f + cn * 0.5f;
        if (w > 0 && lon > 137 - w && lon < 137 + w) return 1;
    }
    /* Australia */
    if (lat > -40 && lat < -10 && lon > 113 && lon < 154) {
        float clon = 134; float w = 18 - fabsf(lat + 25) * 0.5f + cn * 1.2f;
        if (w < 5) w = 5;
        if (lon > clon - w && lon < clon + w) return 1;
    }
    /* New Zealand */
    if (lat > -47 && lat < -34 && lon > 166 && lon < 179) {
        float w = 3 + cn * 0.3f;
        if (w > 0 && lon > 173 - w && lon < 173 + w) return 1;
    }
    return 0;
}

/* ── Smooth value noise for natural-looking textures ──────────────── */

static float noise_hash(int ix, int iy) {
    unsigned int n = (unsigned int)(ix * 1619 + iy * 31337 + 1013904223);
    n = (n >> 13) ^ n;
    n = n * (n * n * 60493 + 19990303) + 1376312589;
    return (float)(n & 0x7FFFFFFF) / (float)0x7FFFFFFF;
}

static float noise_smooth(float x, float y) {
    int ix = (x >= 0) ? (int)x : (int)x - 1;
    int iy = (y >= 0) ? (int)y : (int)y - 1;
    float fx = x - ix, fy = y - iy;
    fx = fx * fx * (3 - 2 * fx);
    fy = fy * fy * (3 - 2 * fy);
    float a = noise_hash(ix, iy), b = noise_hash(ix+1, iy);
    float c = noise_hash(ix, iy+1), d = noise_hash(ix+1, iy+1);
    float top = a + (b - a) * fx;
    float bot = c + (d - c) * fx;
    return top + (bot - top) * fy;
}

static float fbm(float x, float y, int octaves) {
    float val = 0, amp = 0.5f, freq = 1.0f, max_val = 0;
    for (int i = 0; i < octaves; i++) {
        val += noise_smooth(x * freq, y * freq) * amp;
        max_val += amp;
        amp *= 0.5f;
        freq *= 2.0f;
    }
    return val / max_val;
}

static float cloud_coverage(float lat, float lon) {
    float n = fbm(lon * 0.08f + 200, lat * 0.12f + 100, 4);
    float abs_lat = fabsf(lat);
    float band;
    if (abs_lat < 10) band = 0.75f;
    else if (abs_lat < 20) band = 0.35f;
    else if (abs_lat < 35) band = 0.50f;
    else if (abs_lat < 55) band = 0.65f;
    else band = 0.45f;
    float detail = fbm(lon * 0.3f + 500, lat * 0.4f + 300, 3) * 0.25f;
    float cov = (n + detail) * band;
    cov = (cov - 0.32f) * 2.5f;
    if (cov < 0) cov = 0; if (cov > 1) cov = 1;
    return cov * cov;
}

static unsigned int color_lerp(unsigned int c0, unsigned int c1, float t) {
    if (t <= 0) return c0; if (t >= 1.0f) return c1;
    int r0 = (c0>>16)&0xFF, g0 = (c0>>8)&0xFF, b0 = c0&0xFF;
    int r1 = (c1>>16)&0xFF, g1 = (c1>>8)&0xFF, b1 = c1&0xFF;
    int r = (int)(r0 + (r1-r0)*t), g = (int)(g0 + (g1-g0)*t), b = (int)(b0 + (b1-b0)*t);
    return ((unsigned int)r<<16)|((unsigned int)g<<8)|(unsigned int)b;
}

static unsigned int earth_texel(float lat, float lon) {
    float abs_lat = fabsf(lat);
    unsigned int base;
    float terrain_n = fbm(lon * 0.12f, lat * 0.12f, 4);
    float fine_n = fbm(lon * 0.4f + 1000, lat * 0.5f + 1000, 4);
    float micro_n = noise_smooth(lon * 1.5f + 2000, lat * 1.8f + 2000);

    if (abs_lat > 58) {
        float ice = (abs_lat - 58) / 24.0f;
        if (ice > 1) ice = 1;
        float snow_var = fine_n * 0.08f;
        int w = (int)(210 + 40 * ice + snow_var * 200);
        if (w > 255) w = 255; if (w < 180) w = 180;
        unsigned int ice_col = ((unsigned int)w << 16) | ((unsigned int)(w-3) << 8) | (unsigned int)(w+5>255?255:w+5);
        if (ice < 0.4f) {
            /* Blend with underlying biome at ice edge */
            unsigned int under = earth_is_land(lat, lon) ? 0x8AAA88 : 0x1A3A68;
            base = color_lerp(under, ice_col, ice / 0.4f);
        } else {
            base = ice_col;
        }
    } else if (earth_is_land(lat, lon)) {
        float elev = terrain_n * 0.35f + fine_n * 0.15f + micro_n * 0.08f;
        /* Biome colors: pairs of (dark, light) for elevation variation */
        unsigned int c_tundra   = color_lerp(0x7A9A78, 0x96B094, elev);
        unsigned int c_boreal   = color_lerp(0x2A6A28, 0x4A8A40, elev);
        unsigned int c_temper   = color_lerp(0x3A7A32, 0x68A858, elev);
        unsigned int c_jungle   = color_lerp(0x1A6818, 0x3A8830, elev);
        unsigned int c_equator  = color_lerp(0x146414, 0x2A7A28, elev);
        unsigned int c_desert;
        if (lon > -20 && lon < 65) c_desert = color_lerp(0xC8A850, 0xD8C070, elev);
        else if (lon > 65 && lon < 92) c_desert = color_lerp(0xBEA048, 0xCCB060, elev);
        else c_desert = color_lerp(0x5A9A42, 0x78B060, elev);
        /* Smooth blending between biome zones */
        unsigned int land;
        if (abs_lat > 65) { land = c_tundra; }
        else if (abs_lat > 55) { float t = (abs_lat - 55) / 10.0f; land = color_lerp(c_boreal, c_tundra, t); }
        else if (abs_lat > 42) { float t = (abs_lat - 42) / 13.0f; land = color_lerp(c_temper, c_boreal, t); }
        else if (abs_lat > 30) { float t = (abs_lat - 30) / 12.0f; land = color_lerp(c_desert, c_temper, t); }
        else if (abs_lat > 18) { float t = (abs_lat - 18) / 12.0f; land = color_lerp(c_jungle, c_desert, t); }
        else if (abs_lat > 8)  { float t = (abs_lat - 8)  / 10.0f; land = color_lerp(c_equator, c_jungle, t); }
        else { land = c_equator; }
        base = land;
    } else {
        float ocean_n = noise_smooth(lon * 0.04f + 700, lat * 0.06f + 400) * 0.7f + micro_n * 0.3f;
        unsigned int c_arctic  = color_lerp(0x142848, 0x1A3860, ocean_n);
        unsigned int c_cold    = color_lerp(0x123060, 0x1A4880, ocean_n);
        unsigned int c_mid     = color_lerp(0x103868, 0x1858A0, ocean_n);
        unsigned int c_tropic  = color_lerp(0x0E3870, 0x1860B0, ocean_n);
        if (abs_lat > 55) { float t = (abs_lat-55)/15.0f; if(t>1)t=1; base = color_lerp(c_cold, c_arctic, t); }
        else if (abs_lat > 35) { float t = (abs_lat-35)/20.0f; base = color_lerp(c_mid, c_cold, t); }
        else if (abs_lat > 15) { float t = (abs_lat-15)/20.0f; base = color_lerp(c_tropic, c_mid, t); }
        else { base = c_tropic; }
    }

    float cl = cloud_coverage(lat, lon);
    if (cl > 0.01f) {
        unsigned int white = 0xF0F4F8;
        base = color_lerp(base, white, cl);
    }
    return base;
}

static unsigned int gpu_sample_texture(int u1000, int v1000) {
    if (!earth_tex_data) return 0x1A4888;
    float fu = 1.0f - u1000 / 1000.0f, fv = 1.0f - v1000 / 1000.0f;
    fu = fu - floorf(fu);
    if (fv < 0) fv = 0; if (fv > 1) fv = 1;
    /* Bilinear interpolation */
    float px = fu * (earth_tex_w - 1), py = fv * (earth_tex_h - 1);
    int ix = (int)px, iy = (int)py;
    float fx = px - ix, fy = py - iy;
    int ix1 = ix + 1 < earth_tex_w ? ix + 1 : 0;
    int iy1 = iy + 1 < earth_tex_h ? iy + 1 : iy;
    unsigned char *p00 = earth_tex_data + (iy * earth_tex_w + ix) * 3;
    unsigned char *p10 = earth_tex_data + (iy * earth_tex_w + ix1) * 3;
    unsigned char *p01 = earth_tex_data + (iy1 * earth_tex_w + ix) * 3;
    unsigned char *p11 = earth_tex_data + (iy1 * earth_tex_w + ix1) * 3;
    int r = (int)((p00[0]*(1-fx)*(1-fy) + p10[0]*fx*(1-fy) + p01[0]*(1-fx)*fy + p11[0]*fx*fy));
    int g = (int)((p00[1]*(1-fx)*(1-fy) + p10[1]*fx*(1-fy) + p01[1]*(1-fx)*fy + p11[1]*fx*fy));
    int b = (int)((p00[2]*(1-fx)*(1-fy) + p10[2]*fx*(1-fy) + p01[2]*(1-fx)*fy + p11[2]*fx*fy));
    return ((unsigned int)r << 16) | ((unsigned int)g << 8) | (unsigned int)b;
}

/* The plain sampler, for a texture uploaded in mode 1: one 32-bit 0x00RRGGBB
   word per pixel, which is what a Codex EngineTexture holds and what
   gpu-mem-write writes. Both axes WRAP, because a texture tiled across a ground
   plane needs repeat, and the sample is NEAREST because the software renderer
   samples with etx-get and the two paths have to agree -- the desk's 8x8 checker
   stretched over a 6000-unit plane puts one texel across hundreds of pixels, so
   interpolating turns a checkerboard into a smooth wash. Mode 0's sampler above
   keeps its packed-RGB bilinear read: TerrainGen has been writing three bytes a
   pixel with poke-byte since that path was built. */
static unsigned int gpu_sample_plain(int u1000, int v1000) {
    if (!earth_tex_data) return 0x1A4888;
    unsigned int *tex = (unsigned int *)earth_tex_data;
    float fu = u1000 / 1000.0f, fv = v1000 / 1000.0f;
    fu = fu - floorf(fu);
    fv = fv - floorf(fv);
    int ix = (int)(fu * earth_tex_w), iy = (int)(fv * earth_tex_h);
    if (ix < 0) ix = 0; if (iy < 0) iy = 0;
    if (ix >= earth_tex_w) ix = earth_tex_w - 1;
    if (iy >= earth_tex_h) iy = earth_tex_h - 1;
    return tex[iy * earth_tex_w + ix] & 0xFFFFFFu;
}

static unsigned int gpu_sample_earth(int u1000, int v1000) {
    if (earth_tex_data) return gpu_sample_texture(u1000, v1000);
    float lon = -180.0f + (u1000 / 1000.0f) * 360.0f;
    float lat = 90.0f - (v1000 / 1000.0f) * 180.0f;
    return earth_texel(lat, lon);
}

/* The globe shader. It reads the UVs as spherical coordinates and everything
   after the sample follows from that: a fabricated sphere normal, polar ice,
   specular on whatever looks like water, and a Fresnel atmosphere rim worth up
   to +140 blue. Correct for a planet, wrong for any other textured surface,
   which is why gpu_tex_mode decides between this and a plain modulate. */
static unsigned int gpu_shade_globe(int u_interp, int v_interp) {
    unsigned int tex;
    if (v_interp < 140 || v_interp > 860) {
        float pole_t = (v_interp < 140) ? v_interp / 140.0f : (1000 - v_interp) / 140.0f;
        int u_fixed = (int)(500 + (u_interp - 500) * pole_t);
        int v_edge = v_interp < 140 ? (int)(v_interp + (140 - v_interp) * (1.0f - pole_t)) : (int)(v_interp - (v_interp - 860) * (1.0f - pole_t));
        unsigned int sampled = gpu_sample_earth(u_fixed, v_edge);
        if (pole_t < 0.3f) {
            unsigned int ice = 0xD8E0EC;
            tex = color_lerp(ice, sampled, pole_t / 0.3f);
        } else {
            tex = sampled;
        }
    } else {
        tex = gpu_sample_earth(u_interp, v_interp);
    }
    /* Per-pixel normal from UV (sphere) */
    float px_lon = -3.14159f + (u_interp / 1000.0f) * 6.28318f;
    float px_lat = 1.5708f - (v_interp / 1000.0f) * 3.14159f;
    float clat = cosf(px_lat), slat = sinf(px_lat);
    float clon = cosf(px_lon), slon = sinf(px_lon);
    float nx = clat * clon, ny = slat, nz = clat * slon;
    /* Diffuse lighting */
    float ndl = nx*gpu_light[0] + ny*gpu_light[1] + nz*gpu_light[2];
    float wrap = (ndl + 0.5f) / 1.5f;
    if (wrap < 0) wrap = 0;
    float intensity = 0.22f + wrap * 1.1f;
    if (intensity > 1.4f) intensity = 1.4f;
    /* View dot for Fresnel */
    float nde = nx*gpu_eye[0] + ny*gpu_eye[1] + nz*gpu_eye[2];
    if (nde < 0) nde = 0;
    float fresnel = 1.0f - nde;
    if (fresnel < 0) fresnel = 0;
    float rim = fresnel * fresnel * fresnel;
    /* Specular on water (detect by blue dominance in texture) */
    float spec = 0;
    int is_water = ((tex & 0xFF) > ((tex >> 16) & 0xFF) + 15);
    if (is_water && ndl > 0) {
        float hx = gpu_light[0]+gpu_eye[0], hy = gpu_light[1]+gpu_eye[1], hz = gpu_light[2]+gpu_eye[2];
        float hlen = sqrtf(hx*hx+hy*hy+hz*hz);
        if (hlen > 0.001f) { hx/=hlen; hy/=hlen; hz/=hlen; }
        float ndh = nx*hx + ny*hy + nz*hz;
        if (ndh > 0) {
            float s4 = ndh*ndh*ndh*ndh;
            float sharp = s4*s4*s4*s4; sharp *= 0.7f;
            float broad = s4 * 0.12f;
            spec = sharp + broad;
        }
    }
    /* Combine */
    float lat_abs = fabsf(px_lat * 57.2958f);
    float rim_scale = (lat_abs > 65) ? 0.0f : (lat_abs > 45) ? (65 - lat_abs) / 20.0f : 1.0f;
    int tr = (int)(((tex>>16)&0xFF) * intensity + spec * 255 + rim * rim_scale * 40);
    int tg = (int)(((tex>>8)&0xFF) * intensity + spec * 255 + rim * rim_scale * 70);
    int tb = (int)((tex&0xFF) * intensity + spec * 255 + rim * rim_scale * 140);
    if (tr > 255) tr = 255; if (tg > 255) tg = 255; if (tb > 255) tb = 255;
    if (tr < 0) tr = 0; if (tg < 0) tg = 0; if (tb < 0) tb = 0;
    return ((unsigned int)tr << 16) | ((unsigned int)tg << 8) | (unsigned int)tb;
}

static inline unsigned int gpu_lerp_color(unsigned int c0, unsigned int c1, unsigned int c2,
                                           long long w0, long long w1, long long w2, long long area) {
    long long r0 = (c0 >> 16) & 0xFF, g0 = (c0 >> 8) & 0xFF, b0 = c0 & 0xFF;
    long long r1 = (c1 >> 16) & 0xFF, g1 = (c1 >> 8) & 0xFF, b1 = c1 & 0xFF;
    long long r2 = (c2 >> 16) & 0xFF, g2 = (c2 >> 8) & 0xFF, b2 = c2 & 0xFF;
    int r = (int)((r0 * w0 + r1 * w1 + r2 * w2) / area);
    int g = (int)((g0 * w0 + g1 * w1 + g2 * w2) / area);
    int b = (int)((b0 * w0 + b1 * w1 + b2 * w2) / area);
    if (r > 255) r = 255; if (g > 255) g = 255; if (b > 255) b = 255;
    if (r < 0) r = 0; if (g < 0) g = 0; if (b < 0) b = 0;
    return ((unsigned int)r << 16) | ((unsigned int)g << 8) | (unsigned int)b;
}

#define GPU_MAX_THREADS 16
typedef struct {
    unsigned int *fb;
    unsigned int *db;
    unsigned char *cmd;
    int w, h, count;
    int job;
    int band_y0, band_y1;
    HANDLE done_event;
} GpuBand;

static GpuBand gpu_bands[GPU_MAX_THREADS];
static HANDLE gpu_threads[GPU_MAX_THREADS];
static HANDLE gpu_start_events[GPU_MAX_THREADS];
static int gpu_thread_count = 0;
static volatile int gpu_threads_running = 1;

static DWORD WINAPI gpu_band_thread(LPVOID param) {
    int id = (int)(intptr_t)param;
    while (gpu_threads_running) {
        WaitForSingleObject(gpu_start_events[id], INFINITE);
        if (!gpu_threads_running) break;
        GpuBand *b = &gpu_bands[id];
        if (b->job == 1) gpu_composite_band(b->fb, b->w, b->h, b->band_y0, b->band_y1);
        else gpu_rasterize_band(b->fb, b->db, b->cmd, b->w, b->h, b->count, b->band_y0, b->band_y1);
        SetEvent(b->done_event);
    }
    return 0;
}

static void gpu_init_threads(void) {
    if (gpu_thread_count > 0) return;
    gpu_probe_init();
    SYSTEM_INFO si; GetSystemInfo(&si);
    gpu_thread_count = si.dwNumberOfProcessors;
    if (gpu_thread_count > GPU_MAX_THREADS) gpu_thread_count = GPU_MAX_THREADS;
    if (gpu_thread_count < 2) gpu_thread_count = 2;
    for (int i = 0; i < gpu_thread_count; i++) {
        gpu_start_events[i] = CreateEventA(NULL, FALSE, FALSE, NULL);
        gpu_bands[i].done_event = CreateEventA(NULL, FALSE, FALSE, NULL);
        gpu_threads[i] = CreateThread(NULL, 0, gpu_band_thread, (LPVOID)(intptr_t)i, 0, NULL);
    }
    fprintf(stderr, "GPU: %d rasterizer threads\n", gpu_thread_count);
}

static void gpu_rasterize_band(unsigned int *fb, unsigned int *db, unsigned char *cmd,
                                int w, int h, int count, int band_y0, int band_y1) {
    for (int t = 0; t < count && t < GPU_MAX_TRIS; t++) {
        int *tri = (int *)(cmd + t * 72);
        int x0 = tri[0], y0 = tri[1], x1 = tri[2], y1 = tri[3], x2 = tri[4], y2 = tri[5];
        unsigned int c0 = (unsigned int)tri[6], c1 = (unsigned int)tri[7], c2 = (unsigned int)tri[8];
        int d0 = tri[9], d1 = tri[10], d2 = tri[11];
        int u0 = tri[12], v0t = tri[13], u1 = tri[14], v1t = tri[15], u2 = tri[16], v2t = tri[17];
        int additive = 0;
        int use_texture = (u0 | v0t | u1 | v1t | u2 | v2t) != 0;
        if (gpu_cine) { additive = u0; use_texture = 0; }  /* u0: 0 opaque, 1 hard-add, 2 soft radial sprite (center u1,v1t radius u2) */
        int lx0 = 0, ly0 = 0, lsd0 = 0, lx1 = 0, ly1 = 0, lsd1 = 0, lx2 = 0, ly2 = 0, lsd2 = 0, shadow_color = 0;
        long long shadow_bias = GPU_SHADOW_BIAS;
        int use_shadow = gpu_shadow_pending && gpu_shadow_buf && gpu_shadow_size > 0;
        if (use_shadow) {
            int *lt = (int *)((unsigned char *)guest_mem + GPU_LIGHT_ADDR + (size_t)t * GPU_LIGHT_STRIDE);
            lx0 = lt[0]; ly0 = lt[1]; lsd0 = lt[2];
            lx1 = lt[3]; ly1 = lt[4]; lsd1 = lt[5];
            lx2 = lt[6]; ly2 = lt[7]; lsd2 = lt[8];
            shadow_color = lt[9];
            /* Per triangle, not per fragment: these are triangle constants. */
            int dlo = lsd0 < lsd1 ? (lsd0 < lsd2 ? lsd0 : lsd2) : (lsd1 < lsd2 ? lsd1 : lsd2);
            int dhi = lsd0 > lsd1 ? (lsd0 > lsd2 ? lsd0 : lsd2) : (lsd1 > lsd2 ? lsd1 : lsd2);
            int xlo = lx0 < lx1 ? (lx0 < lx2 ? lx0 : lx2) : (lx1 < lx2 ? lx1 : lx2);
            int xhi = lx0 > lx1 ? (lx0 > lx2 ? lx0 : lx2) : (lx1 > lx2 ? lx1 : lx2);
            int ylo = ly0 < ly1 ? (ly0 < ly2 ? ly0 : ly2) : (ly1 < ly2 ? ly1 : ly2);
            int yhi = ly0 > ly1 ? (ly0 > ly2 ? ly0 : ly2) : (ly1 > ly2 ? ly1 : ly2);
            int spanx = (xhi - xlo) / 1000, spany = (yhi - ylo) / 1000;
            int span = spanx > spany ? spanx : spany;
            if (span < 1) span = 1;
            shadow_bias = GPU_SHADOW_BIAS + (long long)(dhi - dlo) / span * GPU_SHADOW_SLOPE;
        }
        int minx = x0 < x1 ? (x0 < x2 ? x0 : x2) : (x1 < x2 ? x1 : x2);
        int miny = y0 < y1 ? (y0 < y2 ? y0 : y2) : (y1 < y2 ? y1 : y2);
        int maxx = x0 > x1 ? (x0 > x2 ? x0 : x2) : (x1 > x2 ? x1 : x2);
        int maxy = y0 > y1 ? (y0 > y2 ? y0 : y2) : (y1 > y2 ? y1 : y2);
        int cx0, cy0, cx1, cy1;
        gpu_clip_rect(w, h, &cx0, &cy0, &cx1, &cy1);
        if (minx < cx0) minx = cx0; if (miny < cy0) miny = cy0;
        if (maxx > cx1) maxx = cx1; if (maxy > cy1) maxy = cy1;
        if (minx > maxx || miny > maxy) continue;
        /* Skip triangles entirely outside this band */
        if (maxy < band_y0 || miny > band_y1) continue;
        if (miny < band_y0) miny = band_y0;
        if (maxy > band_y1) maxy = band_y1;
        long long area = gpu_edge(x0, y0, x1, y1, x2, y2);
        if (area == 0) continue;
        int sign = area > 0 ? 1 : -1;
        long long abs_area = area > 0 ? area : -area;
        for (int y = miny; y <= maxy; y++) {
            for (int x = minx; x <= maxx; x++) {
                long long bw0 = gpu_edge(x1, y1, x2, y2, x, y) * sign;
                long long bw1 = gpu_edge(x2, y2, x0, y0, x, y) * sign;
                long long bw2 = gpu_edge(x0, y0, x1, y1, x, y) * sign;
                if (bw0 >= 0 && bw1 >= 0 && bw2 >= 0) {
                    int depth = (int)(((long long)d0 * bw0 + (long long)d1 * bw1 + (long long)d2 * bw2) / abs_area);
                    int idx = y * w + x;
                    if (additive == 1) {
                        unsigned int ap = gpu_lerp_color(c0, c1, c2, bw0, bw1, bw2, abs_area);
                        unsigned int dst = fb[idx];
                        int arr = (int)((dst>>16)&0xFF) + (int)((ap>>16)&0xFF); if (arr > 255) arr = 255;
                        int agg = (int)((dst>>8)&0xFF) + (int)((ap>>8)&0xFF); if (agg > 255) agg = 255;
                        int abb = (int)(dst&0xFF) + (int)(ap&0xFF); if (abb > 255) abb = 255;
                        fb[idx] = ((unsigned int)arr<<16)|((unsigned int)agg<<8)|(unsigned int)abb;
                    } else if (additive == 2) {
                        int ddx = x - u1, ddy = y - v1t;
                        int dr2 = ddx*ddx + ddy*ddy;
                        int r2 = u2*u2;
                        if (r2 > 0 && dr2 < r2) {
                            float tt = 1.0f - (float)dr2/(float)r2;
                            float ff = tt*tt;
                            unsigned int ap = gpu_lerp_color(c0, c1, c2, bw0, bw1, bw2, abs_area);
                            int cr=(int)(((ap>>16)&0xFF)*ff), cg=(int)(((ap>>8)&0xFF)*ff), cb=(int)((ap&0xFF)*ff);
                            unsigned int dst = fb[idx];
                            int arr=(int)((dst>>16)&0xFF)+cr; if(arr>255)arr=255;
                            int agg=(int)((dst>>8)&0xFF)+cg; if(agg>255)agg=255;
                            int abb=(int)(dst&0xFF)+cb; if(abb>255)abb=255;
                            fb[idx]=((unsigned int)arr<<16)|((unsigned int)agg<<8)|(unsigned int)abb;
                        }
                    } else if ((unsigned int)depth < db[idx]) {
                        unsigned int pixel;
                        unsigned int probe_tex = 0;
                        int probe_u = -1, probe_v = -1;
                        if (use_texture) {
                            int u_interp = (int)(((long long)u0 * bw0 + (long long)u1 * bw1 + (long long)u2 * bw2) / abs_area);
                            int v_interp = (int)(((long long)v0t * bw0 + (long long)v1t * bw1 + (long long)v2t * bw2) / abs_area);
                            probe_u = u_interp; probe_v = v_interp;
                            if (gpu_tex_mode == 1) {
                                /* The rule r3d-tex-px makes: modulate the shaded
                                   vertex colour by the texel, so the two
                                   renderers agree on a textured surface the way
                                   they already agree on an untextured one. */
                                unsigned int tex = gpu_sample_plain(u_interp, v_interp);
                                unsigned int lit = gpu_lerp_color(c0, c1, c2, bw0, bw1, bw2, abs_area);
                                int tr = (int)(((lit >> 16) & 0xFF) * ((tex >> 16) & 0xFF) / 255);
                                int tg = (int)(((lit >> 8) & 0xFF) * ((tex >> 8) & 0xFF) / 255);
                                int tb = (int)((lit & 0xFF) * (tex & 0xFF) / 255);
                                pixel = ((unsigned int)tr << 16) | ((unsigned int)tg << 8) | (unsigned int)tb;
                                probe_tex = tex;
                            } else {
                                pixel = gpu_shade_globe(u_interp, v_interp);
                                if (gpu_probe_on) probe_tex = gpu_sample_earth(u_interp, v_interp);
                            }
                        } else {
                            pixel = gpu_lerp_color(c0, c1, c2, bw0, bw1, bw2, abs_area);
                        }
                        if (gpu_probe_on && x == gpu_probe_x && y == gpu_probe_y && gpu_probe_lines++ < 80) {
                            fprintf(stderr,
                                "GPUPROBE f=%d tri=%d tex=%d uv=(%d,%d %d,%d %d,%d) c=(%06X %06X %06X) "
                                "interp=(%d,%d) texel=%06X mode=%d d=%d pixel=%06X\n",
                                gpu_frame_count, t, use_texture, u0, v0t, u1, v1t, u2, v2t, c0, c1, c2,
                                probe_u, probe_v, probe_tex, gpu_tex_mode, depth, pixel);
                        }
                        /* Same test the software renderer makes in
                           r3d-shadow-test: interpolate the light-space
                           position, sample the map, and take the shadowed
                           colour when this fragment is further from the light
                           than whatever the map recorded. Outside the map is
                           lit, not shadowed, which is what keeps geometry
                           beyond the light's extent from going black. */
                        /* The map is far coarser than the screen -- one texel
                           covers many pixels on a ground plane this large --
                           so a single in/out sample puts texel-shaped teeth
                           along every shadow edge. Averaging a 3x3
                           neighbourhood turns that boundary into ten steps
                           between lit and shadowed, which is what the eye
                           reads as an edge rather than a comb. Off the map
                           counts as lit, so geometry beyond the light's
                           extent does not darken at the border. */
                        if (use_shadow) {
                            long long tx = ((long long)lx0 * bw0 + (long long)lx1 * bw1 + (long long)lx2 * bw2) / abs_area / 1000;
                            long long ty = ((long long)ly0 * bw0 + (long long)ly1 * bw1 + (long long)ly2 * bw2) / abs_area / 1000;
                            if (tx >= 0 && ty >= 0 && tx < gpu_shadow_size && ty < gpu_shadow_size) {
                                long long fl = ((long long)lsd0 * bw0 + (long long)lsd1 * bw1 + (long long)lsd2 * bw2) / abs_area;
                                int lit = 0;
                                for (int oy = -1; oy <= 1; oy++) {
                                    for (int ox = -1; ox <= 1; ox++) {
                                        long long sx = tx + ox, sy = ty + oy;
                                        if (sx < 0 || sy < 0 || sx >= gpu_shadow_size || sy >= gpu_shadow_size) { lit++; continue; }
                                        unsigned int md = gpu_shadow_buf[sy * gpu_shadow_size + sx];
                                        if (fl <= (long long)md + shadow_bias) lit++;
                                    }
                                }
                                if (lit < 9) {
                                    int lr = (pixel >> 16) & 0xFF, lg = (pixel >> 8) & 0xFF, lb = pixel & 0xFF;
                                    int sr = ((unsigned int)shadow_color >> 16) & 0xFF, sg = ((unsigned int)shadow_color >> 8) & 0xFF, sb = (unsigned int)shadow_color & 0xFF;
                                    int rr = sr + (lr - sr) * lit / 9;
                                    int rg = sg + (lg - sg) * lit / 9;
                                    int rb = sb + (lb - sb) * lit / 9;
                                    pixel = ((unsigned int)rr << 16) | ((unsigned int)rg << 8) | (unsigned int)rb;
                                }
                            }
                        }
                        fb[idx] = pixel;
                        db[idx] = (unsigned int)depth;
                    } else if (gpu_probe_on && x == gpu_probe_x && y == gpu_probe_y && gpu_probe_lines++ < 80) {
                        fprintf(stderr, "GPUPROBE f=%d tri=%d tex=%d OCCLUDED d=%d db=%u\n",
                                gpu_frame_count, t, use_texture, depth, db[idx]);
                    }
                }
            }
        }
    }
}

static void gpu_rasterize_triangles(int count) {
    if (!gop_active) return;
    if (gop_host_gpu_refuses()) return;
    gpu_frame_count++;
    gpu_last_tri_count = count;
    if (gpu_probe_on && gpu_probe_lines++ < 80) {
        int n = count > GPU_MAX_TRIS ? GPU_MAX_TRIS : count;
        int xlo = 1 << 30, xhi = -(1 << 30), ylo = 1 << 30, yhi = -(1 << 30);
        long long dlo = 1LL << 62, dhi = -(1LL << 62);
        unsigned char *cmd = (unsigned char *)guest_mem + GPU_CMD_ADDR;
        for (int t = 0; t < n; t++) {
            int *tri = (int *)(cmd + t * 72);
            for (int v = 0; v < 3; v++) {
                if (tri[v*2] < xlo) xlo = tri[v*2];
                if (tri[v*2] > xhi) xhi = tri[v*2];
                if (tri[v*2+1] < ylo) ylo = tri[v*2+1];
                if (tri[v*2+1] > yhi) yhi = tri[v*2+1];
                if (tri[9+v] < dlo) dlo = tri[9+v];
                if (tri[9+v] > dhi) dhi = tri[9+v];
            }
        }
        int cx0, cy0, cx1, cy1;
        gpu_clip_rect(gop_width, gop_height, &cx0, &cy0, &cx1, &cy1);
        /* What the DISPLAY would see if it sampled right now, between the clear
           and the draw. Every pixel equal to the clear colour is the blank-pane
           window; on the front buffer that is the whole rect. */
        unsigned int *vis = (unsigned int *)((unsigned char *)guest_mem + GOP_FB_ADDR);
        long long blank = 0, tot = 0;
        for (int y = cy0; y <= cy1; y++)
            for (int x = cx0; x <= cx1; x++) {
                tot++;
                if ((vis[(size_t)y * gop_width + x] & 0xFFFFFF) == ((unsigned int)gpu_last_clear_color & 0xFFFFFF)) blank++;
            }
        fprintf(stderr, "GPUPROBE f=%d submitted=%d x=%d..%d y=%d..%d d=%lld..%lld clip=%d,%d..%d,%d visible-blank=%lld/%lld\n",
                gpu_frame_count, count, xlo, xhi, ylo, yhi, dlo, dhi, cx0, cy0, cx1, cy1, blank, tot);
    }
    if (count > GPU_MAX_TRIS) {
        static int gpu_overflow_warned = 0;
        if (!gpu_overflow_warned) {
            fprintf(stderr, "GPU: frame submitted %d triangles, cap %d -- the excess is DROPPED; whatever is drawn last vanishes (menus, overlays, panel text)\n", count, GPU_MAX_TRIS);
            gpu_overflow_warned = 1;
        }
    }
    if (GPU_CMD_ADDR + (unsigned long long)count * 72 > guest_mem_size) return;
    if (GOP_FB_ADDR + (unsigned long long)gop_width * gop_height * 4 > guest_mem_size) return;
    /* The band function reads the light buffer per triangle. If the guest asked
       for more triangles than that region can hold, drop the shadow compare
       rather than read past the end of guest RAM. */
    if (gpu_shadow_pending &&
        GPU_LIGHT_ADDR + (unsigned long long)count * GPU_LIGHT_STRIDE > guest_mem_size)
        gpu_shadow_pending = 0;
    unsigned int *fb = gpu_target_fb();
    unsigned int *db = (unsigned int *)((unsigned char *)guest_mem + GPU_DEPTH_ADDR);
    int w = gop_width, h = gop_height;
    unsigned char *cmd = (unsigned char *)guest_mem + GPU_CMD_ADDR;
    gpu_init_threads();
    int band_h = h / gpu_thread_count;
    HANDLE done_events[GPU_MAX_THREADS];
    for (int i = 0; i < gpu_thread_count; i++) {
        gpu_bands[i].fb = fb;
        gpu_bands[i].db = db;
        gpu_bands[i].cmd = cmd;
        gpu_bands[i].job = 0;
        gpu_bands[i].w = w;
        gpu_bands[i].h = h;
        gpu_bands[i].count = count;
        gpu_bands[i].band_y0 = i * band_h;
        gpu_bands[i].band_y1 = (i == gpu_thread_count - 1) ? h - 1 : (i + 1) * band_h - 1;
        done_events[i] = gpu_bands[i].done_event;
        SetEvent(gpu_start_events[i]);
    }
    WaitForMultipleObjects(gpu_thread_count, done_events, TRUE, INFINITE);
    gpu_present_viewport();
    gpu_frame_ready = 1;
}

static unsigned char *glow_dist = NULL;
static int glow_valid = 0;

static void gpu_atmosphere_glow(void) {
    if (!gop_active) return;
    if (gop_host_gpu_refuses()) return;
    int w = gop_width, h = gop_height;
    if (GOP_FB_ADDR + (unsigned long long)w * h * 4 > guest_mem_size) return;
    unsigned int *fb = (unsigned int *)((unsigned char *)guest_mem + GOP_FB_ADDR);
    int total = w * h;
    if (!glow_dist) glow_dist = (unsigned char *)calloc(1, GOP_MAX_W * GOP_MAX_H);
    int glow_radius = 16;
    /* Recompute distance field every 8 frames */
    if ((gpu_frame_count & 3) == 1 || !glow_valid) {
        unsigned int bg = fb[0];
        for (int i = 0; i < total; i++)
            glow_dist[i] = (fb[i] == bg) ? 255 : 0;
        for (int pass = 0; pass < 2; pass++) {
            for (int y = 0; y < h; y++) {
                int row = y * w;
                for (int x = 1; x < w; x++)
                    if (glow_dist[row+x] > glow_dist[row+x-1] + 1)
                        glow_dist[row+x] = glow_dist[row+x-1] + 1;
                for (int x = w - 2; x >= 0; x--)
                    if (glow_dist[row+x] > glow_dist[row+x+1] + 1)
                        glow_dist[row+x] = glow_dist[row+x+1] + 1;
            }
            for (int x = 0; x < w; x++) {
                for (int y = 1; y < h; y++)
                    if (glow_dist[y*w+x] > glow_dist[(y-1)*w+x] + 1)
                        glow_dist[y*w+x] = glow_dist[(y-1)*w+x] + 1;
                for (int y = h - 2; y >= 0; y--)
                    if (glow_dist[y*w+x] > glow_dist[(y+1)*w+x] + 1)
                        glow_dist[y*w+x] = glow_dist[(y+1)*w+x] + 1;
            }
        }
        glow_valid = 1;
    }
    for (int i = 0; i < total; i++) {
        int d = glow_dist[i];
        if (d > 0 && d < glow_radius) {
            float t = 1.0f - (float)d / glow_radius;
            float glow = t * t * t;
            int r = (fb[i] >> 16) & 0xFF, g = (fb[i] >> 8) & 0xFF, b = fb[i] & 0xFF;
            r = (int)(r + glow * 25); if (r > 255) r = 255;
            g = (int)(g + glow * 55); if (g > 255) g = 255;
            b = (int)(b + glow * 150); if (b > 255) b = 255;
            fb[i] = ((unsigned int)r << 16) | ((unsigned int)g << 8) | (unsigned int)b;
        }
    }
}

/* ── Cinematic post: additive bloom + tonemap + teal/orange grade + vignette ── */
static float *cine_bloom = NULL;
static float *cine_tmp = NULL;

/* Composite one horizontal band: add bloom, tonemap, grade, vignette.
   Runs on the worker pool so the post pass holds a steady frame rate. */
static void gpu_composite_band(unsigned int *fb, int w, int h, int y0, int y1) {
    int bw = w / 4;
    float cxf = w*0.5f, cyf = h*0.5f;
    float maxd2 = cxf*cxf + cyf*cyf;
    for (int y = y0; y <= y1; y++) {
        for (int x = 0; x < w; x++) {
            int idx = y*w + x;
            unsigned int p = fb[idx];
            float r = (float)((p>>16)&0xFF), g = (float)((p>>8)&0xFF), b = (float)(p&0xFF);
            int bi = ((y>>2)*bw + (x>>2))*3;
            r += cine_bloom[bi]*0.9f; g += cine_bloom[bi+1]*0.9f; b += cine_bloom[bi+2]*0.9f;
            float rn = r/255.0f, gn = g/255.0f, bn = b/255.0f;
            float ex = 1.25f; rn*=ex; gn*=ex; bn*=ex;
            rn = rn/(1.0f+rn); gn = gn/(1.0f+gn); bn = bn/(1.0f+bn);
            rn = (rn-0.5f)*1.28f + 0.5f;
            gn = (gn-0.5f)*1.28f + 0.5f;
            bn = (bn-0.5f)*1.28f + 0.5f;
            float luma = 0.299f*rn + 0.587f*gn + 0.114f*bn;
            float s = luma - 0.5f;
            rn += s*0.12f; bn -= s*0.10f; gn += s*0.015f;
            if (luma < 0.5f) { float sh = (0.5f-luma)*0.14f; bn += sh; gn += sh*0.5f; }
            float dx = x-cxf, dy = y-cyf; float vd = (dx*dx+dy*dy)/maxd2;
            float vig = 1.0f - 0.5f*vd;
            rn*=vig; gn*=vig; bn*=vig;
            int R=(int)(rn*255.0f), G=(int)(gn*255.0f), B=(int)(bn*255.0f);
            if(R<0)R=0; if(R>255)R=255; if(G<0)G=0; if(G>255)G=255; if(B<0)B=0; if(B>255)B=255;
            fb[idx] = ((unsigned int)R<<16)|((unsigned int)G<<8)|(unsigned int)B;
        }
    }
}

static void gpu_cinematic_post(void) {
    if (!gop_active) return;
    if (gop_host_gpu_refuses()) return;
    int w = gop_width, h = gop_height;
    if (GOP_FB_ADDR + (unsigned long long)w * h * 4 > guest_mem_size) return;
    unsigned int *fb = (unsigned int *)((unsigned char *)guest_mem + GOP_FB_ADDR);
    int bw = w / 4, bh = h / 4;
    if (bw < 1 || bh < 1) return;
    size_t bcap = (size_t)(GOP_MAX_W/2) * (GOP_MAX_H/2) * 3;
    if (!cine_bloom) cine_bloom = (float *)malloc(sizeof(float) * bcap);
    if (!cine_tmp)   cine_tmp   = (float *)malloc(sizeof(float) * bcap);
    if (!cine_bloom || !cine_tmp) return;
    /* bright pass into half-res bloom buffer */
    for (int y = 0; y < bh; y++) {
        for (int x = 0; x < bw; x++) {
            unsigned int p = fb[(y*4)*w + (x*4)];
            float r = (float)((p>>16)&0xFF), g = (float)((p>>8)&0xFF), b = (float)(p&0xFF);
            float lum = 0.299f*r + 0.587f*g + 0.114f*b;
            float t = lum - 78.0f; if (t < 0) t = 0;
            float k = t / 170.0f; if (k > 1.5f) k = 1.5f;
            int i = (y*bw + x) * 3;
            cine_bloom[i] = r*k; cine_bloom[i+1] = g*k; cine_bloom[i+2] = b*k;
        }
    }
    /* separable gaussian blur, 2 iterations (soft wide bloom) */
    static const float wt[5] = {0.2270f, 0.1940f, 0.1216f, 0.0540f, 0.0162f};
    for (int pass = 0; pass < 2; pass++) {
        for (int y = 0; y < bh; y++) {
            for (int x = 0; x < bw; x++) {
                float ar=0,ag=0,ab=0;
                for (int k = -4; k <= 4; k++) {
                    int xx = x + k; if (xx < 0) xx = 0; if (xx >= bw) xx = bw-1;
                    float ww = wt[k<0?-k:k]; int i = (y*bw+xx)*3;
                    ar += cine_bloom[i]*ww; ag += cine_bloom[i+1]*ww; ab += cine_bloom[i+2]*ww;
                }
                int o=(y*bw+x)*3; cine_tmp[o]=ar; cine_tmp[o+1]=ag; cine_tmp[o+2]=ab;
            }
        }
        for (int y = 0; y < bh; y++) {
            for (int x = 0; x < bw; x++) {
                float ar=0,ag=0,ab=0;
                for (int k = -4; k <= 4; k++) {
                    int yy = y + k; if (yy < 0) yy = 0; if (yy >= bh) yy = bh-1;
                    float ww = wt[k<0?-k:k]; int i = (yy*bw+x)*3;
                    ar += cine_tmp[i]*ww; ag += cine_tmp[i+1]*ww; ab += cine_tmp[i+2]*ww;
                }
                int o=(y*bw+x)*3; cine_bloom[o]=ar; cine_bloom[o+1]=ag; cine_bloom[o+2]=ab;
            }
        }
    }
    /* composite + grade across the worker pool for a steady frame rate */
    if (gpu_thread_count > 0) {
        int band_h = h / gpu_thread_count;
        HANDLE cdone[GPU_MAX_THREADS];
        for (int i = 0; i < gpu_thread_count; i++) {
            gpu_bands[i].job = 1;
            gpu_bands[i].fb = fb;
            gpu_bands[i].w = w; gpu_bands[i].h = h;
            gpu_bands[i].band_y0 = i*band_h;
            gpu_bands[i].band_y1 = (i==gpu_thread_count-1) ? h-1 : (i+1)*band_h-1;
            cdone[i] = gpu_bands[i].done_event;
            SetEvent(gpu_start_events[i]);
        }
        WaitForMultipleObjects(gpu_thread_count, cdone, TRUE, INFINITE);
    } else {
        gpu_composite_band(fb, w, h, 0, h-1);
    }
}

__declspec(dllimport) unsigned int __stdcall timeBeginPeriod(unsigned int);
static void gpu_cine_pace(void) {
    static long long freq = 0;
    LARGE_INTEGER li;
    if (!freq) { timeBeginPeriod(1); QueryPerformanceFrequency(&li); freq = li.QuadPart ? li.QuadPart : 1; }
    QueryPerformanceCounter(&li);
    long long now = li.QuadPart;
    if (gpu_cine_last) {
        double elapsed_ms = (double)(now - gpu_cine_last) * 1000.0 / (double)freq;
        double target = 16.6; /* ~60 fps cap */
        if (elapsed_ms < target) {
            int sms = (int)(target - elapsed_ms);
            if (sms > 0 && sms < 100) Sleep(sms);
            QueryPerformanceCounter(&li); now = li.QuadPart;
        }
    }
    gpu_cine_last = now;
}

/* ── Shadow buffer sync (main thread only, between VP exits) ───────── */

static long shadow_sync_count = 0;

static void sync_shadow_buffers(void) {
    shadow_sync_count++;
    /* Copy VGA text buffer */
    if (VGA_BASE + sizeof(shadow_vga) <= guest_mem_size)
        memcpy(shadow_vga, (unsigned char *)guest_mem + VGA_BASE, sizeof(shadow_vga));

    /* Copy the GOP framebuffer to the shadow the window and -screenshot read.
       A guest that drives the rasterizer, VBE, or UEFI announces its frames, so
       those sync immediately. But a guest can also just POKE PIXELS: the GOP
       framebuffer is ordinary RAM, and an app that writes it directly (spark)
       announces nothing. Such an app used to be invisible -- black window, empty
       screenshot, frames=0 -- with no error anywhere. It is not the guest's job
       to tell us it drew something; if GOP is active, the framebuffer is live.
       Unannounced writers are synced on a ~60 Hz pace so the copy costs nothing
       on a hot VP-exit path. (The UEFI arm of this condition was this same bug,
       fixed for one boot path only.) */
    int gop_announced = (gpu_frame_ready || vbe_active || uefi_mode);
    int gop_due = gop_announced;
    if (gop_active && !gop_announced) {
        static LARGE_INTEGER gop_last = {0};
        static LARGE_INTEGER gop_freq = {0};
        LARGE_INTEGER now;
        if (!gop_freq.QuadPart) QueryPerformanceFrequency(&gop_freq);
        QueryPerformanceCounter(&now);
        double since = (double)(now.QuadPart - gop_last.QuadPart) * 1000.0 / (double)gop_freq.QuadPart;
        if (!gop_last.QuadPart || since >= 16.0) { gop_last = now; gop_due = 1; }
    }
    if (gop_active && gop_width > 0 && gop_height > 0 && gop_due) {
        size_t fb_bytes = (size_t)gop_stride * gop_height * 4;
        if (!shadow_gop) shadow_gop = (unsigned char *)calloc(1, GOP_FB_SIZE);
        if (shadow_gop && GOP_FB_ADDR + fb_bytes <= guest_mem_size) {
            memcpy(shadow_gop, (unsigned char *)guest_mem + GOP_FB_ADDR, fb_bytes);
            shadow_gop_w = gop_width;
            shadow_gop_h = gop_height;
            shadow_gop_stride = gop_stride;
        }
    }

    /* Flush pending keyboard scancode to guest memory. If the guest has
       programmed the PIC with IRQ1 unmasked, its own keyboard ISR reads
       port 0x60 and stores to 28680 (real-hardware semantics) -- drop the
       host-side delivery so each key arrives exactly once. */
    if (pending_kbd_valid && 28680 + 1 <= guest_mem_size) {
        if (pic_master.vector_base && !(pic_master.mask & (1 << 1))) {
            pending_kbd_valid = 0;
        } else {
            *((unsigned char *)guest_mem + 28680) = (unsigned char)pending_kbd_scancode;
            pending_kbd_valid = 0;
        }
    }

    /* Flush pending mouse state to guest memory (PS/2 path).
       Do NOT clear pending_mouse_valid here -- the absolute-coordinate
       port path (0xE1-0xE4) uses the same flag and clears it on 0xE3 read. */
    if (pending_mouse_valid && MOUSE_BUF_ADDR + 3 <= (int)guest_mem_size) {
        unsigned char *mbuf = (unsigned char *)guest_mem + MOUSE_BUF_ADDR;
        mbuf[0] = pending_mouse[0];
        mbuf[1] = pending_mouse[1];
        mbuf[2] = pending_mouse[2];
    }
}

/* ── -run-list ─────────────────────────────────────────────────────────
   Run many kernels from one invocation, spawning a FRESH codex-vm child
   per line rather than reusing this process.

   Measured 2026-08-22, one kernel, twelve-core box: 574.8 ms per test
   through build/test-run.ps1, 73.7 ms for codex-vm alone, and 12.6 ms of
   that 73.7 is this process's own start. The pwsh child is 501 of the
   575, so what a batch mode has to remove is the SCRIPT per test, not the
   exe. Reusing the process would buy the 12.6 ms and require resetting
   376 file-scope statics between runs: deleting a partition does not
   reset them, the device models being host state and not partition state,
   and a single missed one makes a test's result depend on what preceded
   it in the batch. A fresh child pays the 12.6 ms and makes the batch
   byte-identical to N single runs by construction rather than by test. */

#define RUNLIST_MAX_ARGS 128
#define RUNLIST_WALL_MS  60000   /* the per-kernel budget test-run.ps1 keeps */
#define RUNLIST_CMD_MAX  16384

/* Whitespace-separated, and a token may be double-quoted so a path with a
   space survives. Rewrites line in place. */
static int runlist_split(char *line, char **out, int max) {
    int n = 0;
    char *p = line;
    while (*p && n < max) {
        while (*p == ' ' || *p == '\t' || *p == '\r') p++;
        if (!*p) break;
        if (*p == '"') {
            p++;
            out[n++] = p;
            while (*p && *p != '"') p++;
            if (*p) *p++ = 0;
        } else {
            out[n++] = p;
            while (*p && *p != ' ' && *p != '\t' && *p != '\r') p++;
            if (*p) *p++ = 0;
        }
    }
    return n;
}

/* Every token is quoted, which needs no per-token decision and cannot
   split a path on a space. A run of trailing backslashes is doubled: by
   the CRT's argv rules those would otherwise escape the closing quote and
   swallow the next argument into this one. */
static int runlist_append_arg(char *cmd, size_t cap, const char *arg) {
    size_t len = strlen(cmd);
    size_t alen = strlen(arg);
    size_t bs = 0;
    while (bs < alen && arg[alen - 1 - bs] == '\\') bs++;
    if (len + alen + bs + 4 >= cap) return 0;
    if (len) cmd[len++] = ' ';
    cmd[len++] = '"';
    memcpy(cmd + len, arg, alen);
    len += alen;
    for (size_t i = 0; i < bs; i++) cmd[len++] = '\\';
    cmd[len++] = '"';
    cmd[len] = 0;
    return 1;
}

/* Signal the child's own shutdown event and let it leave through the
   normal WHvDeletePartition path. TerminateProcess mid-hypervisor-call is
   what corrupts vid.sys's kernel heap, so it is the fallback and not the
   first move -- the same order build/vm-config.ps1 Stop-VmGraceful uses. */
static void runlist_stop_child(DWORD pid, HANDLE h) {
    char ev[64];
    snprintf(ev, sizeof(ev), "Global\\CodexVmShutdown_%lu", (unsigned long)pid);
    HANDLE e = OpenEventA(EVENT_MODIFY_STATE, FALSE, ev);
    if (e) {
        SetEvent(e);
        CloseHandle(e);
        if (WaitForSingleObject(h, 5000) == WAIT_OBJECT_0) return;
    }
    TerminateProcess(h, 0xC0DEDEAD);
    WaitForSingleObject(h, 2000);
}

/* The child's "Output: N bytes -> path" line, or -1 when it printed none. */
static long long runlist_scan_output(const char *buf) {
    long long got = -1;
    const char *p = buf;
    while ((p = strstr(p, "Output: ")) != NULL) {
        p += 8;
        char *end = NULL;
        long long v = strtoll(p, &end, 10);
        if (end && end != p && !strncmp(end, " bytes", 6)) got = v;
    }
    return got;
}

/* Serial bytes the child dropped. Keyed on the whole "SERIAL: N guest
   serial byte(s) DROPPED" phrase and not on the word DROPPED, which the
   GPU triangle-cap warning also prints and which is not a short capture. */
static unsigned long long runlist_scan_dropped(const char *buf) {
    unsigned long long total = 0;
    const char *p = buf;
    while ((p = strstr(p, "SERIAL: ")) != NULL) {
        p += 8;
        char *end = NULL;
        unsigned long long v = strtoull(p, &end, 10);
        if (end && end != p && !strncmp(end, " guest serial byte(s) DROPPED", 29))
            total += v;
    }
    return total;
}

static int runlist_main(const char *list_path, unsigned wall_ms) {
    char self[MAX_PATH];
    if (!GetModuleFileNameA(NULL, self, sizeof(self))) {
        fprintf(stderr, "-run-list: cannot resolve my own path\n");
        return 1;
    }

    FILE *lf = fopen(list_path, "rb");
    if (!lf) { fprintf(stderr, "-run-list: cannot open %s\n", list_path); return 1; }
    fseek(lf, 0, SEEK_END);
    long lsz = ftell(lf);
    fseek(lf, 0, SEEK_SET);
    char *ltext = (char *)malloc((size_t)lsz + 1);
    if (!ltext) { fclose(lf); fprintf(stderr, "-run-list: out of memory\n"); return 1; }
    size_t lgot = fread(ltext, 1, (size_t)lsz, lf);
    ltext[lgot] = 0;
    fclose(lf);

    /* Counted first so every line can report itself as i/N. */
    int total = 0;
    for (char *s = ltext; *s; ) {
        char *e = strchr(s, '\n');
        char *stop = e ? e : s + strlen(s);
        char *t = s;
        while (t < stop && (*t == ' ' || *t == '\t' || *t == '\r')) t++;
        if (t < stop && *t != '#') total++;
        if (!e) break;
        s = e + 1;
    }

    char tmpdir[MAX_PATH];
    if (!GetTempPathA(sizeof(tmpdir), tmpdir)) snprintf(tmpdir, sizeof(tmpdir), ".\\");

    int idx = 0, failed = 0, timed_out = 0, short_caps = 0;
    for (char *s = ltext; *s; ) {
        char *e = strchr(s, '\n');
        if (e) *e = 0;
        char *t = s;
        while (*t == ' ' || *t == '\t' || *t == '\r') t++;
        if (!*t || *t == '#') { if (!e) break; s = e + 1; continue; }

        idx++;
        char *args[RUNLIST_MAX_ARGS];
        int n = runlist_split(t, args, RUNLIST_MAX_ARGS);

        const char *tag = n ? args[0] : "(empty)";
        for (int i = 0; i + 1 < n; i++)
            if (!strcmp(args[i], "-kernel")) { tag = args[i+1]; break; }

        /* A line may not open a list of its own. One that names its own file
           is an unbounded fork bomb on a box the whole fleet shares, and it
           costs a generator bug rather than an attacker to write one. */
        int nested = 0;
        for (int i = 0; i < n; i++)
            if (!strcmp(args[i], "-run-list")) { nested = 1; break; }
        if (nested) {
            fprintf(stderr, "RUN-LIST BEGIN [%d/%d] %s\n", idx, total, tag);
            fprintf(stderr, "-run-list: line %d carries -run-list; a list may not nest\n", idx);
            fprintf(stderr, "RUN-LIST END [%d/%d] %s exit=SPAWNFAIL output=-1 dropped=0 ms=0\n",
                    idx, total, tag);
            failed++;
            if (!e) break;
            s = e + 1;
            continue;
        }

        char cmd[RUNLIST_CMD_MAX];
        cmd[0] = 0;
        int built = runlist_append_arg(cmd, sizeof(cmd), self);
        for (int i = 0; i < n && built; i++)
            built = runlist_append_arg(cmd, sizeof(cmd), args[i]);
        if (!built) {
            fprintf(stderr, "RUN-LIST BEGIN [%d/%d] %s\n", idx, total, tag);
            fprintf(stderr, "-run-list: line %d does not fit in %d bytes of command line\n",
                    idx, RUNLIST_CMD_MAX);
            fprintf(stderr, "RUN-LIST END [%d/%d] %s exit=SPAWNFAIL output=-1 dropped=0 ms=0\n",
                    idx, total, tag);
            failed++;
            if (!e) break;
            s = e + 1;
            continue;
        }

        char errpath[MAX_PATH];
        if (!GetTempFileNameA(tmpdir, "cvr", 0, errpath)) {
            fprintf(stderr, "RUN-LIST BEGIN [%d/%d] %s\n", idx, total, tag);
            fprintf(stderr, "-run-list: cannot make a temp file for line %d\n", idx);
            fprintf(stderr, "RUN-LIST END [%d/%d] %s exit=SPAWNFAIL output=-1 dropped=0 ms=0\n",
                    idx, total, tag);
            failed++;
            if (!e) break;
            s = e + 1;
            continue;
        }

        SECURITY_ATTRIBUTES sa;
        sa.nLength = sizeof(sa);
        sa.lpSecurityDescriptor = NULL;
        sa.bInheritHandle = TRUE;
        HANDLE eh = CreateFileA(errpath, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                                &sa, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);

        fprintf(stderr, "RUN-LIST BEGIN [%d/%d] %s\n", idx, total, tag);

        STARTUPINFOA si;
        PROCESS_INFORMATION pi;
        memset(&si, 0, sizeof(si));
        memset(&pi, 0, sizeof(pi));
        si.cb = sizeof(si);
        if (eh != INVALID_HANDLE_VALUE) {
            si.dwFlags = STARTF_USESTDHANDLES;
            si.hStdInput = GetStdHandle(STD_INPUT_HANDLE);
            si.hStdOutput = GetStdHandle(STD_OUTPUT_HANDLE);
            si.hStdError = eh;
        }

        BOOL ok = CreateProcessA(self, cmd, NULL, NULL,
                                 eh != INVALID_HANDLE_VALUE, 0, NULL, NULL, &si, &pi);
        if (eh != INVALID_HANDLE_VALUE) CloseHandle(eh);

        if (!ok) {
            fprintf(stderr, "-run-list: CreateProcess failed (%lu) for line %d\n",
                    GetLastError(), idx);
            fprintf(stderr, "RUN-LIST END [%d/%d] %s exit=SPAWNFAIL output=-1 dropped=0 ms=0\n",
                    idx, total, tag);
            failed++;
            DeleteFileA(errpath);
            if (!e) break;
            s = e + 1;
            continue;
        }

        LARGE_INTEGER t0, t1, tf;
        QueryPerformanceFrequency(&tf);
        QueryPerformanceCounter(&t0);
        int tmo = 0;
        if (WaitForSingleObject(pi.hProcess, wall_ms) == WAIT_TIMEOUT) {
            tmo = 1;
            runlist_stop_child(pi.dwProcessId, pi.hProcess);
        }
        QueryPerformanceCounter(&t1);
        unsigned long long elapsed_ms = tf.QuadPart
            ? (unsigned long long)((t1.QuadPart - t0.QuadPart) * 1000 / tf.QuadPart) : 0;
        DWORD rc = 0;
        GetExitCodeProcess(pi.hProcess, &rc);
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);

        /* The child's stderr goes through verbatim. Every existing
           diagnostic -- the crash report, the RAM cap, the IDE census --
           is why a red test is readable at all, and a batch mode that
           summarised them would be the one run nobody can debug. */
        long long outbytes = -1;
        unsigned long long dropped = 0;
        FILE *ef = fopen(errpath, "rb");
        if (ef) {
            fseek(ef, 0, SEEK_END);
            long esz = ftell(ef);
            fseek(ef, 0, SEEK_SET);
            char *ebuf = (char *)malloc((size_t)esz + 1);
            if (ebuf) {
                size_t egot = fread(ebuf, 1, (size_t)esz, ef);
                ebuf[egot] = 0;
                fwrite(ebuf, 1, egot, stderr);
                outbytes = runlist_scan_output(ebuf);
                dropped = runlist_scan_dropped(ebuf);
                free(ebuf);
            }
            fclose(ef);
        }
        DeleteFileA(errpath);

        if (tmo) {
            fprintf(stderr, "RUN-LIST END [%d/%d] %s exit=TIMEOUT output=%lld dropped=%llu ms=%llu\n",
                    idx, total, tag, outbytes, dropped, elapsed_ms);
            timed_out++;
        } else {
            fprintf(stderr, "RUN-LIST END [%d/%d] %s exit=%lu output=%lld dropped=%llu ms=%llu\n",
                    idx, total, tag, (unsigned long)rc, outbytes, dropped, elapsed_ms);
        }
        if (dropped) short_caps++;

        if (!e) break;
        s = e + 1;
    }

    free(ltext);
    fprintf(stderr, "RUN-LIST DONE: %d line(s), %d timed out, %d not spawned, %d with dropped bytes\n",
            idx, timed_out, failed, short_caps);
    /* A child's own exit code is NOT a verdict -- a healthy test exits
       (debug_exit_code << 1) | 1 -- so it is reported per line and never
       summed. This code answers only whether the supervisor ran the list. */
    return (timed_out || failed) ? 1 : 0;
}

/* ── Main loop ─────────────────────────────────────────────────────── */

int main(int argc, char **argv) {
    /* Unbuffered stderr. Redirected to a file it block-buffers, so anything
       written after the last flush is lost when the process dies -- and the
       process dying is precisely when the diagnostics matter. A crash dump
       that ends mid-section reads as "it crashed here" when it means "the
       buffer was never flushed", which sent this chase after the wrong
       function once already. */
    setvbuf(stderr, NULL, _IONBF, 0);
    /* Never let Windows put a modal dialog in front of a crash. An
       unhandled access violation otherwise raises "codex-vm.exe -
       Application Error", which BLOCKS the process until a human clicks
       it: under the battery that turns a fast failure into a
       wall-budget-exceeded hang, on a box nobody is watching, and the
       crash text never reaches the log. Measured 2026-08-03 during a
       release battery run (exc-stack-heap, pid 43952). SEM_ flags cover
       the OS dialogs; the filter covers the CRT's own, prints the fault
       to the log that is being captured, and dies with a distinguishable
       code. */
    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
    SetUnhandledExceptionFilter(crash_filter);
    _set_abort_behavior(0, _WRITE_ABORT_MSG | _CALL_REPORTFAULT);
    SetConsoleCtrlHandler(ctrl_handler, TRUE);
    create_shutdown_event();
    CreateThread(NULL, 0, shutdown_event_thread, NULL, 0, NULL);
    atexit(cleanup_whp);
    const char *kernel = NULL, *disk = NULL, *disk2 = NULL, *boot_args = NULL, *trace_file = NULL;
    int mem_mb = 3072;  /* matches the build harness; binaries from pre-7209
                           seeds triple-fault below 2 GB + stack reserve */
    int mem_nocap = 0;

    /* -run-list is a supervisor mode: it spawns a child per line and never
       builds a partition of its own, so it is answered before any of the
       flags below are parsed. Refusing -kernel beside it keeps the two
       modes from looking combinable when only one of them would run. */
    for (int i = 1; i < argc; i++) {
        if (!strcmp(argv[i], "-run-list") && i+1 < argc) {
            /* The budget is a flag defaulting to the 60 s test-run.ps1
               keeps, because a timeout nothing can provoke is a path
               nothing has tested: the arm that proves a stopped child does
               not take the rest of the list runs at two seconds. */
            unsigned wall = RUNLIST_WALL_MS;
            for (int j = 1; j < argc; j++) {
                if (!strcmp(argv[j], "-kernel")) {
                    fprintf(stderr, "-run-list runs the kernels its list names; "
                                    "-kernel is not accepted beside it\n");
                    return 1;
                }
                if (!strcmp(argv[j], "-run-list-wall") && j+1 < argc)
                    wall = (unsigned)strtoul(argv[j+1], NULL, 10);
            }
            if (wall == 0) wall = RUNLIST_WALL_MS;
            return runlist_main(argv[i+1], wall);
        }
    }

    for (int i = 1; i < argc; i++) {
        if (!strcmp(argv[i], "-kernel") && i+1 < argc) { kernel = argv[++i]; g_kernel_path = kernel; }
        else if (!strcmp(argv[i], "-disk") && i+1 < argc) disk = argv[++i];
        else if (!strcmp(argv[i], "-disk2") && i+1 < argc) disk2 = argv[++i];
        else if (!strcmp(argv[i], "-no-ide")) no_ide = 1;
        else if (!strcmp(argv[i], "-mem") && i+1 < argc) mem_mb = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-input") && i+1 < argc) input_file = argv[++i];
        else if (!strcmp(argv[i], "-output") && i+1 < argc) output_file = argv[++i];
        else if (!strcmp(argv[i], "-watch") && i+1 < argc) watch_addr = strtoull(argv[++i], NULL, 0);
        else if (!strcmp(argv[i], "-watch-size") && i+1 < argc) watch_size = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-watch-val") && i+1 < argc) { watch_val = strtoull(argv[++i], NULL, 0); watch_val_set = 1; }
        else if (!strcmp(argv[i], "-watchall") && i+1 < argc) { watch_addr = strtoull(argv[++i], NULL, 0); watch_report_all = 1; watch_size = 64; }
        else if (!strcmp(argv[i], "-r10dump")) r10dump = 1;
        else if (!strcmp(argv[i], "-dumpmem") && i+2 < argc) { dumpmem_addr = strtoull(argv[++i], NULL, 0); dumpmem_len = strtoull(argv[++i], NULL, 0); }
        else if (!strcmp(argv[i], "-hwwatch") && i+1 < argc) { hw_watch_addr = strtoull(argv[++i], NULL, 0); hw_watch_active = 1; }
        else if (!strcmp(argv[i], "-hwwatch-rw")) hw_watch_rw = 3;
        else if (!strcmp(argv[i], "-hwwatch-log")) hw_watch_log = 1;
        else if (!strcmp(argv[i], "-hwwatch-len") && i+1 < argc) hw_watch_len = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-debug")) debug_mode = 1;
        else if (!strcmp(argv[i], "-wcet") && i+1 < argc) {
            if (wcet_name_count < MAX_WCET_FNS) wcet_names[wcet_name_count++] = argv[++i];
        }
        else if (!strcmp(argv[i], "-break") && i+1 < argc) {
            debug_mode = 1;
            if (init_break_count < MAX_INIT_BREAKS) init_break_names[init_break_count++] = argv[++i];
        }
        else if (!strcmp(argv[i], "-hbreak") && i+1 < argc) {
            if (hbreak_spec_count < MAX_HBREAKS) hbreak_specs[hbreak_spec_count++] = argv[++i];
        }
        else if (!strcmp(argv[i], "-map") && i+1 < argc) map_file_path = argv[++i];
        else if (!strcmp(argv[i], "-headless")) vga_headless = 1;
        else if (!strcmp(argv[i], "-rtc-lenient")) rtc_lenient = 1;
        else if (!strcmp(argv[i], "-rtc") && i+1 < argc) {
            if (!rtc_parse_fixed(argv[++i])) {
                fprintf(stderr, "-rtc: expected YYYY-MM-DDTHH:MM:SS, got '%s'\n", argv[i]);
                return 1;
            }
            rtc_fixed = 1;
        }
        else if (!strcmp(argv[i], "-smp")) {
            if (i+1 < argc && argv[i+1][0] >= '0' && argv[i+1][0] <= '9') {
                smp_cores = atoi(argv[++i]);
                if (smp_cores < 1) smp_cores = 1;
                if (smp_cores > SMP_MAX_CORES) smp_cores = SMP_MAX_CORES;
            } else {
                smp_cores = 4;
            }
        }
        else if (!strcmp(argv[i], "-screenshot") && i+1 < argc) screenshot_path = argv[++i];
        else if (!strcmp(argv[i], "-screenshot-delay") && i+1 < argc) screenshot_delay_ms = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-keys") && i+1 < argc) {
            /* comma-separated Set-1 scancodes, e.g. -keys 80,80,28 (down,down,enter) */
            const char *s = argv[++i];
            while (*s && inject_key_count < 256) {
                int v = atoi(s);
                inject_keys[inject_key_count++] = (unsigned char)v;
                while (*s && *s != ',') s++;
                if (*s == ',') s++;
            }
        }
        else if (!strcmp(argv[i], "-keys-file") && i+1 < argc) {
            FILE *kf = fopen(argv[++i], "rb");
            if (!kf) { fprintf(stderr, "-keys-file: cannot open %s\n", argv[i]); }
            else {
                fseek(kf, 0, SEEK_END); long ksz = ftell(kf); fseek(kf, 0, SEEK_SET);
                char *kbuf = (char *)malloc((size_t)ksz + 1);
                if (kbuf) {
                    size_t kgot = fread(kbuf, 1, (size_t)ksz, kf);
                    kbuf[kgot] = 0;
                    inject_keyt_parse(kbuf);
                    free(kbuf);
                }
                fclose(kf);
            }
        }
        else if (!strcmp(argv[i], "-mouse") && i+1 < argc) {
            inject_mouse_parse(argv[++i]);
        }
        else if (!strcmp(argv[i], "-mouse-file") && i+1 < argc) {
            FILE *mf = fopen(argv[++i], "rb");
            if (!mf) { fprintf(stderr, "-mouse-file: cannot open %s\n", argv[i]); }
            else {
                fseek(mf, 0, SEEK_END); long msz = ftell(mf); fseek(mf, 0, SEEK_SET);
                char *mbuf = (char *)malloc((size_t)msz + 1);
                if (mbuf) {
                    size_t got = fread(mbuf, 1, (size_t)msz, mf);
                    mbuf[got] = 0;
                    inject_mouse_parse(mbuf);
                    free(mbuf);
                }
                fclose(mf);
            }
        }
        else if (!strcmp(argv[i], "-e1000")) e1000_present = 1;
        else if (!strcmp(argv[i], "-e1000-inject") && i+1 < argc) { e1000_present = 1; e1000_inject_want = atoi(argv[++i]); }
        else if (!strcmp(argv[i], "-e1000-no-reset")) { e1000_present = 1; e1000_fault_no_reset = 1; }
        else if (!strcmp(argv[i], "-e1000-no-link"))  { e1000_present = 1; e1000_fault_no_link = 1; }
        else if (!strcmp(argv[i], "-e1000-no-mac"))   { e1000_present = 1; e1000_fault_no_mac = 1; }
        else if (!strcmp(argv[i], "-e1000-no-tx-dd")) { e1000_present = 1; e1000_fault_no_tx_dd = 1; }
        else if (!strcmp(argv[i], "-e1000-rdh-ro"))   { e1000_present = 1; e1000_fault_rdh_ro = 1; }
        else if (!strcmp(argv[i], "-i219"))           { e1000_present = 1; i219_present = 1; }
        else if (!strcmp(argv[i], "-i219-k1-nvm") && i+1 < argc) { e1000_present = 1; i219_present = 1; i219_k1_nvm = atoi(argv[++i]); }
        else if (!strcmp(argv[i], "-i219-swflag"))    { e1000_present = 1; i219_present = 1; i219_swflag_enforce = 1; }
        else if (!strcmp(argv[i], "-i219-mng-holds")) { e1000_present = 1; i219_present = 1; i219_swflag_enforce = 1; i219_mng_holds = 1; }
        else if (!strcmp(argv[i], "-i219-extcnf-strict")) { e1000_present = 1; i219_present = 1; i219_swflag_enforce = 1; i219_extcnf_strict = 1; }
        else if (!strcmp(argv[i], "-i219-mng-release-after") && i+1 < argc) {
            e1000_present = 1; i219_present = 1; i219_swflag_enforce = 1; i219_mng_holds = 1;
            i219_mng_release_after = (unsigned int)strtoul(argv[++i], NULL, 0);
        }
        else if (!strcmp(argv[i], "-i219-ulp-armed")) { e1000_present = 1; i219_present = 1; i219_ulp_cfg1 = (unsigned short)(I219_ULP_CFG1_BOARD | I219_ULP_STICKY | I219_ULP_EN_LANPHYPC); }
        else if (!strcmp(argv[i], "-nic-bme-clear"))  { e1000_present = 1; e1000_bme_clear = 1; }
        else if (!strcmp(argv[i], "-dmar"))           { acpi_dmar = 1; }
        else if (!strcmp(argv[i], "-dhcp-lease") && i+1 < argc) { nat_dhcp_lease = (unsigned int)atoi(argv[++i]); }
        else if (!strcmp(argv[i], "-e1000-nat"))      { e1000_present = 1; e1000_nat = 1; }
        else if (!strcmp(argv[i], "-e1000-strict-filter")) { e1000_present = 1; e1000_strict_filter = 1; }
        else if (!strcmp(argv[i], "-pci-bridge")) pci_bridge = 1;
        else if (!strcmp(argv[i], "-pci-bridge-deep")) { pci_bridge = 1; pci_bridge_deep = 1; }
        else if (!strcmp(argv[i], "-pci-bridge-levels") && i+1 < argc) { pci_bridge = 1; pci_bridge_levels = atoi(argv[++i]); }
        else if (!strcmp(argv[i], "-pci-bridge-backward")) { pci_bridge = 1; pci_bridge_backward = 1; }
        else if (!strcmp(argv[i], "-e1000-inject-armed")) { e1000_present = 1; e1000_inject_armed = 1; }
        else if (!strcmp(argv[i], "-e1000-no-phy"))   { e1000_present = 1; e1000_fault_no_phy = 1; }
        else if (!strcmp(argv[i], "-e1000-phy-err"))  { e1000_present = 1; e1000_fault_phy_err = 1; }
        else if (!strcmp(argv[i], "-e1000-phy-link")) { e1000_present = 1; e1000_phy_link = 1; }
        else if (!strcmp(argv[i], "-e1000-mdio-window")) { e1000_present = 1; e1000_mdio_window = 1; }
        else if (!strcmp(argv[i], "-e1000-mdio-slow")) { e1000_present = 1; e1000_mdio_slow = 1; }
        else if (!strcmp(argv[i], "-e1000-preconfigured")) {
            e1000_present = 1; e1000_preconfigured = 1;
            /* Receiver live, with a ring already programmed somewhere the
               driver knows nothing about. RDLEN is one descriptor so the
               state is self-consistent rather than merely non-zero. */
            e1000_regs[E1000_REG_RCTL / 4] = E1000_RCTL_EN;
            e1000_regs[E1000_REG_RDLEN / 4] = 16;
        }
        else if (!strcmp(argv[i], "-e1000-ctrl-ro")) {
            e1000_present = 1; e1000_ctrl_ro = 1;
            /* Hand the guest the state the ASUS firmware hands it, so a
               driver reading CTRL before writing sees SLU already set --
               which is what makes the refusal invisible without a readback. */
            e1000_regs[E1000_REG_CTRL / 4] = E1000_CTRL_FIRMWARE_VALUE;
        }
        else if (!strcmp(argv[i], "-e1000-asde")) { e1000_present = 1; e1000_asde = 1; }
        else if (!strcmp(argv[i], "-keys-start") && i+1 < argc) inject_key_start_ms = atof(argv[++i]);
        else if (!strcmp(argv[i], "-keys-interval") && i+1 < argc) inject_key_interval_ms = atof(argv[++i]);
        else if (!strcmp(argv[i], "-board-mmio")) board_mmio = 1;
        else if (!strcmp(argv[i], "-xhci-no-root-kbd")) xhci_no_root_kbd = 1;
        else if (!strcmp(argv[i], "-usb-cfgval") && i+1 < argc) {
            usb_cfgval = atoi(argv[++i]);
            usb_cfg_desc[5] = (unsigned char)usb_cfgval;
        }
        else if (!strcmp(argv[i], "-usb-setcfg-fault") && i+1 < argc) usb_setcfg_fault = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-usb-setcfg-fault-once") && i+1 < argc) usb_setcfg_fault_once = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-usb-no-unit-attention")) usb_unit_attention = 0;
        else if (!strcmp(argv[i], "-usb-bot-drop") && i+1 < argc) usb_bot_drop = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-usb-bot-die-len") && i+1 < argc) usb_bot_die_len = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-usb-bot-census")) usb_bot_census = 1;
        else if (!strcmp(argv[i], "-census") && i+1 < argc) {
            census_fp = fopen(argv[++i], "w");
            if (!census_fp) { fprintf(stderr, "codex-vm: -census: cannot open %s\n", argv[i]); return 1; }
            usb_bot_census = 1;
        }
        else if (!strcmp(argv[i], "-usb-bot-die-lba") && i+1 < argc) usb_bot_die_lba = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-usb-bot-revive-on-reset")) usb_bot_revive = 1;
        else if (!strcmp(argv[i], "-usb-bot-die-on-nic")) usb_bot_die_on_nic = 1;
        else if (!strcmp(argv[i], "-usb-bot-drops") && i+1 < argc) usb_bot_drops = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-usb-bot-drop-len") && i+1 < argc) usb_bot_drop_len = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-usb-bot-drop-len-max") && i+1 < argc) usb_bot_drop_len_max = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-usb-disk-port") && i+1 < argc) {
            usb_disk_port = atoi(argv[++i]);
            if (usb_disk_port < 1) usb_disk_port = 1;
            if (usb_disk_port > XHCI_MAX_PORTS) usb_disk_port = XHCI_MAX_PORTS;
        }
        else if (!strcmp(argv[i], "-xhci-ports") && i+1 < argc) {
            xhci_num_ports = atoi(argv[++i]);
            if (xhci_num_ports < XHCI_MODELLED_PORTS) xhci_num_ports = XHCI_MODELLED_PORTS;
            if (xhci_num_ports > XHCI_MAX_PORTS) xhci_num_ports = XHCI_MAX_PORTS;
        }
        else if (!strcmp(argv[i], "-hid-nak")) xhci_hid_nak = 1;
        else if (!strcmp(argv[i], "-hid-idle-quirk")) hid_idle_quirk = 1;
        else if (!strcmp(argv[i], "-hid-root-silent")) xhci_hid_root_silent = 1;
        else if (!strcmp(argv[i], "-hid-keys")) hid_keys_only = 1;
        else if (!strcmp(argv[i], "-hid-combo")) hid_combo = 1;
        else if (!strcmp(argv[i], "-hid-nak-unchanged")) hid_nak_unchanged = 1;
        else if (!strcmp(argv[i], "-hid-instant-complete")) hid_nak_unchanged = 0;
        else if (!strcmp(argv[i], "-xhci-calibrate-periodic")) xhci_calibrate_periodic = 1;
        else if (!strcmp(argv[i], "-xhci-psi")) xhci_psi = 1;
        else if (!strcmp(argv[i], "-xhci-bar") && i+1 < argc) xhci_bar_initial = (unsigned int)strtoul(argv[++i], NULL, 0);
        else if (!strcmp(argv[i], "-xhci-two")) xhci_two = 1;
        else if (!strcmp(argv[i], "-xhci-no-disk")) xhci_no_disk = 1;
        else if (!strcmp(argv[i], "-xhci-bar2") && i+1 < argc) { xhci_two = 1; xhci_bar2_initial = (unsigned int)strtoul(argv[++i], NULL, 0); }
        else if (!strcmp(argv[i], "-xhci-intel")) xhci_intel = 1;
        else if (!strcmp(argv[i], "-xhci-intel-lock")) { xhci_intel = 1; xhci_intel_lock = 1; }
        else if (!strcmp(argv[i], "-xhci-csz")) xhci_csz64 = 1;
        else if (!strcmp(argv[i], "-xhci-scratch") && i+1 < argc) xhci_scratch_bufs = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-uefi-conout-remode")) uefi_conout_remode = 1;
        else if (!strcmp(argv[i], "-no-hpet")) hpet_absent = 1;
        else if (!strcmp(argv[i], "-hpet-frozen")) hpet_allones = 1;
        else if (!strcmp(argv[i], "-no-smbios")) uefi_no_smbios = 1;
        else if (!strcmp(argv[i], "-no-edid")) uefi_no_edid = 1;
        else if (!strcmp(argv[i], "-edid-bad")) uefi_edid_bad = 1;
        else if (!strcmp(argv[i], "-xhci-evt-flood") && i+1 < argc) xhci_evt_flood = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-xhci-hub-tiers") && i + 1 < argc) {
            xhci_hub_tiers = atoi(argv[++i]);
            if (xhci_hub_tiers < 1) xhci_hub_tiers = 1;
            if (xhci_hub_tiers > XHCI_HUB_TIERS) xhci_hub_tiers = XHCI_HUB_TIERS;
        }
        else if (!strcmp(argv[i], "-mem-nocap")) mem_nocap = 1;
        else if (!strcmp(argv[i], "-uefi")) uefi_mode = 1;
        else if (!strcmp(argv[i], "-uefi-strict")) { uefi_mode = 1; uefi_strict = 1; }
        else if (!strcmp(argv[i], "-gop")) { gop_active = 1; }
        else if (!strcmp(argv[i], "-gop-width") && i+1 < argc) { gop_width = atoi(argv[++i]); gop_stride = gop_width; gop_active = 1; }
        else if (!strcmp(argv[i], "-gop-max-mode") && i+1 < argc) { gop_max_mode_opt = atoi(argv[++i]); }
        else if (!strcmp(argv[i], "-gop-height") && i+1 < argc) { gop_height = atoi(argv[++i]); gop_active = 1; }
        else if (!strcmp(argv[i], "-gop-stride") && i+1 < argc) { gop_stride_opt = atoi(argv[++i]); gop_active = 1; }
        else if (!strcmp(argv[i], "-args") && i+1 < argc) boot_args = argv[++i];
        else if (!strcmp(argv[i], "-trace-file") && i+1 < argc) trace_file = argv[++i];
        else if (!strcmp(argv[i], "-portfwd") && i+1 < argc) {
            char *spec = argv[++i];
            int hp = 0, gp = 0;
            /* An optional "udp:" prefix. The default stays TCP so every
               existing invocation means what it always did. */
            int proto = PF_TCP;
            char *body = spec;
            if (!strncmp(spec, "udp:", 4)) { proto = PF_UDP; body = spec + 4; }
            else if (!strncmp(spec, "tcp:", 4)) { body = spec + 4; }
            if (sscanf(body, "%d:%d", &hp, &gp) == 2 && portfwd_count < PORTFWD_MAX) {
                portfwds[portfwd_count].host_port = (unsigned short)hp;
                portfwds[portfwd_count].guest_port = (unsigned short)gp;
                portfwds[portfwd_count].proto = proto;
                portfwd_count++;
            } else {
                fprintf(stderr, "Bad -portfwd spec: %s (expected [udp:]host:guest)\n", spec);
            }
        }
        else if (!strcmp(argv[i], "-natmap") && i+1 < argc) {
            char *spec = argv[++i];
            int gp = 0, hp = 0;
            if (sscanf(spec, "%d:%d", &gp, &hp) == 2 && natmap_count < NATMAP_MAX) {
                natmaps[natmap_count].guest_dport = (unsigned short)gp;
                natmaps[natmap_count].host_port   = (unsigned short)hp;
                natmap_count++;
            } else {
                fprintf(stderr, "Bad -natmap spec: %s (expected guestdest:hostport)\n", spec);
            }
        }
    }
    if (!kernel) {
        fprintf(stderr, "Usage: codex-vm -kernel file.cdx [-input file.codex] [-output file.cdx]\n"
                        "       [-disk file.img] [-mem MB] [-mem-nocap] [-args STRING]\n"
                        "       [-watch 0xADDR] [-watch-size N] [-headless] [-uefi] [-uefi-strict]\n"
                        "       [-gop] [-gop-width N] [-gop-height N] [-gop-stride N] [-gop-max-mode N] [-keys sc,sc,..]\n"
                        "       [-portfwd hostport:guestport] ...\n"
                        "   or: codex-vm -run-list file.txt [-run-list-wall MS]\n"
                        "       one line per run, each line the flags a single run takes;\n"
                        "       '#' comments and blank lines ignored\n");
        return 1;
    }
    if (watch_size > 64) watch_size = 64;

    if (getenv("CODEX_VM_NO_TIMER")) { no_timer = 1; fprintf(stderr, "TIMER INTERRUPTS DISABLED\n"); }
    hprof_file = getenv("CODEX_VM_PROFILE");
    if (hprof_file && !hprof_file[0]) hprof_file = NULL;

    WSADATA wsa; WSAStartup(MAKEWORD(2,2), &wsa);
    if (portfwd_count > 0) portfwd_init();

    ide_init(&ide, disk);
    ide_init(&ide_slave, disk2);
    pic_init(&pic_master);
    pic_init(&pic_slave);
    ne2k_reset();

    xhci_init();

    /* Register PCI devices */
    pci_add_device(0x1234, 0x1111, 0x03, 0x00, 0x00, 0xFD000000, 0);  /* slot 0: Bochs VGA (BAR at high MMIO, FB read from RAM at GOP_FB_ADDR) */
    {   /* slot 1: xHCI (NEC/Renesas). Its BAR decodes the register space it
           actually serves, so sizing reports 16 KB and not the old blanket
           64 KB. */
        int xi = xhci_intel
            ? pci_add_device(0x8086, 0x8C31, 0x0C, 0x03, 0x30, xhci_bar_initial, 10)  /* Lynx Point PCH */
            : pci_add_device(0x1033, 0x0194, 0x0C, 0x03, 0x30, xhci_bar_initial, 10);
        if (xi >= 0) pci_devices[xi].bar_size[0] = XHCI_BAR_SIZE;
        xhci_pci_slot = xi;
        if (xhci_two) {   /* the ASMedia, ordinal 1, as on the ASUS */
            int x2 = pci_add_device(0x1B21, 0x1242, 0x0C, 0x03, 0x30, xhci_bar2_initial, 10);
            if (x2 >= 0) pci_devices[x2].bar_size[0] = XHCI_BAR_SIZE;
            xhci_pci_slot2 = x2;
        }
    }
    pci_add_device(0x8086, 0x2668, 0x04, 0x03, 0x00, 0xFE000000, 11);  /* slot 2: Intel HDA */
    if (e1000_present) {   /* slot 3: Intel gigabit Ethernet, e1000e family */
        /* 0x100E is an 82540EM, and it is the id this model can honestly
           carry: what is decoded below is the common 8254x core (CTRL,
           STATUS, MDIC, ICR, RCTL, TCTL, the two rings, RAL/RAH and four
           counters) and nothing above it. There is no EXTCNF_CTRL, no
           SWSM, no MSI-X, so 82574 would overclaim and 0x15B8 (I219-LM,
           the part on the ASUS) overclaims further: the I219 MAC sits in
           the PCH and needs a semaphore before any PHY access, which
           nothing here models.

           It advertised 0x15B8 until 2026-08-20. The driver matches on
           vendor plus class plus subclass and never reads the device id
           (E1000e.codex e1000-is-candidate), so the wrong id bound
           correctly and told a reader the bed was the board. A bed must
           not advertise an id it does not implement: 0x15B8 is reserved
           for a model written from the I219 datasheet. */
        int ei = pci_add_device(0x8086, i219_present ? I219_DEVICE_ID : 0x100E,
                                0x02, 0x00, 0x00, (unsigned int)E1000_BAR, 12);
        if (ei >= 0) pci_devices[ei].bar_size[0] = E1000_BAR_SIZE;
        e1000_pci_slot = ei;
        /* Without this the PHY id registers read zero, which is what a bus
           with nothing on it reads, so a driver could not tell a working
           model from an absent PHY. */
        e1000_phy_reset_regs();
        /* The MNG bit is firmware's and 4.5.2 says a reset does not clear
           it, so it is seeded here at power-up rather than in the PHY reset
           path with the bits that do clear. */
        i219_extcnf = i219_mng_holds ? I219_EXTCNF_MNG : 0;
    }

    if (pci_bridge) {
        /* A PCI-to-PCI bridge on bus 0 forwarding to bus 1, with one endpoint
           behind it, so pci-scan-all descends. The bridge is class 06 subclass
           04, header type 1; 1b36:000c is QEMU's pcie-root-port, the exact part
           fester saw on the board (build/boot/diag/README.md). The endpoint is
           a virtio-net, which this x86 model does not otherwise emulate, so it
           is inert config space -- the walk finds it, nothing tries to drive
           it. Both go on new slots after the bus-0 devices already added. */
        /* levels: 1 is -pci-bridge, 2 is -pci-bridge-deep, N is
           -pci-bridge-levels N. One mechanism, and the creation ORDER is
           preserved so the one- and two-level topologies are exactly the ones
           those flags already built and their arms keep their floors. */
        int levels = pci_bridge_levels ? pci_bridge_levels
                   : (pci_bridge_deep ? 2 : 1);
        int br = -1;
        for (int lv = 1; lv <= levels; lv++) {
            int b = pci_add_device(0x1B36, 0x000C, 0x06, 0x04, 0x00, 0, 0);
            if (b >= 0) {
                pci_devices[b].header_type = 1;
                pci_devices[b].bus = (unsigned char)(lv - 1);
                /* The first bridge lands on a bus-0 slot after the devices
                   already there; the rest sit beside the endpoint on their
                   parent's bus. */
                pci_devices[b].slot = (unsigned char)(lv == 1 ? b : 1);
                pci_devices[b].sec_bus = (unsigned char)lv;
                /* The subordinate is the HIGHEST bus behind this bridge, not
                   the secondary. Stating the secondary describes a topology
                   that does not exist. */
                pci_devices[b].sub_bus = (unsigned char)levels;
                if (lv == 1) br = b;
                /* An unconfigured bridge reads 0 in its secondary-bus field,
                   which is at or below its own bus, and pci-bridge-one is
                   written to refuse exactly that. Nothing had ever handed it
                   one, so the refusal was an assertion rather than a branch. */
                if (pci_bridge_backward && lv == levels)
                    pci_devices[b].sec_bus = 0;
            }
            int e = pci_add_device(0x1AF4, 0x1041, 0x02, 0x00, 0x00, 0, 0);
            if (e >= 0) {
                pci_devices[e].bus = (unsigned char)lv;
                pci_devices[e].slot = 0;  /* first slot on the secondary bus */
            }
        }
        /* -pci-bridge-deep: a bridge BEHIND the bridge, so pci-collect runs at
           depth 2. pci-scan-max-depth is 3 and pci-collect has always been
           written to recurse, but one bridge one level deep is all any bed has
           ever presented, so depth 2 and 3 have never executed anywhere -- the
           recursion was reachable and untravelled, which reads exactly like a
           tested walk (L-UNCALLED one level out). The ASUS presents 21 devices
           over four buses.

           OFF by default and separate from -pci-bridge (L-FALLBACK): the
           second level adds devices, so folding it into the existing flag
           would move pci-bridge-scan's count and cost that arm its floor. */
        fprintf(stderr, "PCI: %d bridge level(s), first 1b36:000c on 00:%02x.0 -> bus 1, "
                        "endpoint 1af4:1041 at 01:00.0%s\n",
                levels, br >= 0 ? pci_devices[br].slot : 0,
                pci_bridge_backward ? ", deepest points BACKWARD" : "");
    }

    /* Resolve -gop-stride before create_vm, which commits the guest GPU/GOP
       region and sizes it from gop_stride: resolving after the call would
       commit a region too small for the padded scanlines and fault the first
       time the guest touched the tail. Resolving here rather than inline in
       the parse loop is what makes flag order not matter, since -gop-width
       assigns gop_stride as well. A stride below the width is not a padded
       scanline but a corrupt one, so it is refused rather than clamped. */
    if (gop_stride_opt > 0) {
        if (gop_stride_opt < gop_width) {
            fprintf(stderr, "-gop-stride %d is less than -gop-width %d; ignoring\n",
                    gop_stride_opt, gop_width);
            gop_stride_opt = 0;
        } else if (gop_stride_opt > GOP_MAX_STRIDE) {
            fprintf(stderr, "-gop-stride %d exceeds the %d maximum; clamping\n",
                    gop_stride_opt, GOP_MAX_STRIDE);
            gop_stride_opt = GOP_MAX_STRIDE;
        }
    }
    if (gop_stride_opt > gop_width) gop_stride = gop_stride_opt;

    create_vm(mem_mb);
    acpi_setup_tables(guest_mem);
    smbios_setup_tables(guest_mem);
    load_kernel(kernel);
    set_initial_regs();

    /* Write requested core count to ap-core-count-addr (GPA 0xFF8 = 4088).
       The boot code reads this; if <= 1, SMP init is skipped entirely. */
    *(unsigned int *)((unsigned char *)guest_mem + 0xFF8) = smp_cores > 1 ? smp_cores : 0;

    /* Write RAM size to ram-size-addr (GPA 0xFE8 = 4072).
       The guest __start reads this to set RSP = ram_size (stack at top of RAM). */
    {
        /* Cap reported RAM so guest stack (at RAM top) stays below GPU/GOP region.
           GPU cmd buffer=0xBE000000, depth=0xBE800000, GOP fb=0xBF000000.
           Guest RSP starts at reported RAM size and grows DOWN -- must not overlap.

           The cap binds whatever -mem says, so a guest is told 3040 MB at
           -mem 8192 and its heap frontier meets the boot stack at the same
           address either way. That is what made compile.ps1's crash retry a
           no-op: "retrying with 8192MB" ran a byte-identical machine, and a
           whole-compiler IR emit crashed with the same RIP, RSP and heap
           frontier at both sizes.

           -mem-nocap reports the real size. It is opt-in and not the default
           because the three windows sit at FIXED GPAs and the guest may start
           using them after boot (UEFI GOP, VBE), by which time the RAM size is
           long written: an uncapped heap that grows past 0xBE000000 then
           overwrites the framebuffer it is about to scan out. Pass it only for
           a run that draws nothing -- a headless compile is the case it exists
           for. */
        unsigned long long effective = guest_mem_size;
        if (!mem_nocap && effective > GPU_CMD_ADDR)
            effective = GPU_CMD_ADDR;
        fprintf(stderr, "RAM cap: guest_mem_size=0x%llx effective=0x%llx gop=%d\n", guest_mem_size, effective, gop_active);
        *(unsigned long long *)((unsigned char *)guest_mem + 0xFE8) = effective;
    }

    /* Write GOP resolution to GPA 0x7C4/0x7C8 so guests can read display size.
       The stride goes at 0x7E0, the same offset PixelsPerScanLine occupies in
       a UEFI mode-info block, so the two paths agree on where it lives. A
       guest booted without -uefi never sees a mode-info block at all, so
       before this it had no way to learn the scanline was padded and could
       only assume stride equals width -- which is what the whole -gop-stride
       bed exists to stop being true by construction. */
    if (gop_active) {
        *(int *)((unsigned char *)guest_mem + 0x7C4) = gop_width;
        *(int *)((unsigned char *)guest_mem + 0x7C8) = gop_height;
        *(int *)((unsigned char *)guest_mem + 0x7E0) = gop_stride;
    }

    /* Write boot args string to guest memory at 0x4800 (CCE-encoded).
       Format: 8-byte length prefix + string bytes, like a Codex Text value. */
    if (boot_args) {
        static const int cce_to_uni[128] = {
            0, 10, 32,
            48, 49, 50, 51, 52, 53, 54, 55, 56, 57,
            101, 116, 97, 111, 105, 110, 115, 104, 114, 100,
            108, 99, 117, 109, 119, 102, 103, 121, 112, 98,
            118, 107, 106, 120, 113, 122,
            69, 84, 65, 79, 73, 78, 83, 72, 82, 68,
            76, 67, 85, 77, 87, 70, 71, 89, 80, 66,
            86, 75, 74, 88, 81, 90,
            46, 44, 33, 63, 58, 59, 39, 34, 45, 40, 41,
            43, 61, 42, 60, 62,
            47, 64, 35, 38, 95, 92, 124, 91, 93, 123, 125, 126, 96,
            94, 36, 37,
            233, 232, 234, 235, 225, 224, 226, 228,
            243, 244, 246, 250, 252, 241, 231, 237,
            1072, 1086, 1077, 1080, 1085, 1090, 1089, 1088,
            1074, 1083, 1082, 1084, 1076, 1087, 1091
        };
        unsigned char uni_to_cce[128];
        memset(uni_to_cce, 0, sizeof(uni_to_cce));
        for (int i = 0; i < 128; i++) {
            if (cce_to_uni[i] < 128) uni_to_cce[cce_to_uni[i]] = (unsigned char)i;
        }
        unsigned char *dst = (unsigned char*)guest_mem + 0x4800;
        size_t alen = strlen(boot_args);
        if (alen > 4000) alen = 4000;
        *(unsigned long long*)dst = (unsigned long long)alen;
        for (size_t i = 0; i < alen; i++) {
            unsigned char ch = (unsigned char)boot_args[i];
            dst[8 + i] = (ch < 128) ? uni_to_cce[ch] : 0;
        }
        fprintf(stderr, "Boot args at 0x4800: \"%s\" (%zu bytes, CCE-encoded)\n", boot_args, alen);
    }

    output_buf_init();
    if (input_file) load_input_file(input_file);

    if (!uefi_mode) {
        fprintf(stderr, "VM starting (mem=%dMB)...\n", mem_mb);
    } else {
        fprintf(stderr, "UEFI VM starting (mem=%dMB)...\n", mem_mb);
    }
    if (!no_timer) CreateThread(NULL, 0, timer_kick_thread, NULL, 0, NULL);
    vga_start();

    QueryPerformanceFrequency(&perf_freq);
    QueryPerformanceCounter(&last_tick);

    LARGE_INTEGER screenshot_start;
    QueryPerformanceCounter(&screenshot_start);
    hid_timebase = screenshot_start;

    if (watch_addr) watch_init();

    /* Load symbol map if provided or auto-detect from kernel path */
    if (map_file_path) {
        load_map_file(map_file_path);
    } else if ((debug_mode || wcet_name_count > 0) && kernel) {
        /* Try <kernel-dir>/Codex.map, then seed/Codex.map */
        char auto_map[512];
        strncpy(auto_map, kernel, sizeof(auto_map)-1);
        char *last_sep = strrchr(auto_map, '\\');
        if (!last_sep) last_sep = strrchr(auto_map, '/');
        if (last_sep) { strcpy(last_sep + 1, "Codex.map"); }
        else strcpy(auto_map, "Codex.map");
        FILE *tf = fopen(auto_map, "r");
        if (tf) { fclose(tf); load_map_file(auto_map); }
    }

    /* Apply initial breakpoints from -break args */
    for (int bi = 0; bi < init_break_count; bi++) {
        unsigned long long ba = sym_find(init_break_names[bi]);
        if (ba) {
            int idx = dbg_set_breakpoint(ba, -1, 0);
            if (idx >= 0) fprintf(stderr, "DBG: break %d at %s (0x%llx)\n", idx, init_break_names[bi], ba);
        } else {
            fprintf(stderr, "DBG: symbol '%s' not found\n", init_break_names[bi]);
        }
    }

    /* Instrument -wcet functions: DR0-DR3 execution breakpoints at
       entry, range from the map. No guest memory is modified. */
    for (int wi = 0; wi < wcet_name_count; wi++) {
        int found = -1;
        for (int si = 0; si < symbol_count; si++)
            if (!strcmp(symbols[si].name, wcet_names[wi])) { found = si; break; }
        if (found < 0) {
            fprintf(stderr, "WCET: symbol '%s' not found (need -map)\n", wcet_names[wi]);
            continue;
        }
        if (wcet_fn_count >= MAX_WCET_FNS) {
            fprintf(stderr, "WCET: '%s' skipped (max %d functions per run, one debug register each)\n",
                wcet_names[wi], MAX_WCET_FNS);
            continue;
        }
        if (symbols[found].addr < guest_mem_size) {
            int w = wcet_fn_count++;
            wcet_fns[w].start = symbols[found].addr;
            wcet_fns[w].end = symbols[found].addr + (unsigned long long)symbols[found].size;
            strncpy(wcet_fns[w].name, symbols[found].name, 127);
            wcet_fns[w].name[127] = 0;
            wcet_fns[w].max_count = 0;
            wcet_fns[w].calls = 0;
            fprintf(stderr, "WCET: instrumenting %s [0x%llx,0x%llx) via DR%d\n",
                wcet_fns[w].name, wcet_fns[w].start, wcet_fns[w].end, w);
        }
    }
    /* Resolve -hbreak specs: "<name>" or "<name>:<reg>=<val>". */
    for (int hi = 0; hi < hbreak_spec_count; hi++) {
        const char *spec = hbreak_specs[hi];
        const char *colon = strchr(spec, ':');
        char nbuf[128];
        size_t nlen = colon ? (size_t)(colon - spec) : strlen(spec);
        if (nlen >= sizeof(nbuf)) nlen = sizeof(nbuf) - 1;
        memcpy(nbuf, spec, nlen);
        nbuf[nlen] = 0;
        int cr = -1; unsigned long long cv = 0;
        if (colon) {
            const char *p = colon + 1;
            char rname[16]; int ri = 0;
            while (*p && *p != '=' && ri < 15) rname[ri++] = *p++;
            rname[ri] = 0;
            cr = dbg_reg_index(rname);
            if (cr < 0) { fprintf(stderr, "HBREAK: unknown register '%s' in '%s'\n", rname, spec); continue; }
            if (*p == '=') p++;
            cv = strtoull(p, NULL, 0);
        }
        unsigned long long a = sym_find(nbuf);
        if (!a) { fprintf(stderr, "HBREAK: symbol '%s' not found (need -map)\n", nbuf); continue; }
        if (hbreak_count >= MAX_HBREAKS) {
            fprintf(stderr, "HBREAK: '%s' skipped (max %d, one debug register each)\n", nbuf, MAX_HBREAKS);
            continue;
        }
        int h = hbreak_count++;
        hbreaks[h].addr = a;
        strncpy(hbreaks[h].name, nbuf, 127);
        hbreaks[h].name[127] = 0;
        hbreaks[h].cond_reg = cr;
        hbreaks[h].cond_val = cv;
        hbreaks[h].hits = 0;
        hbreaks[h].matched = 0;
        fprintf(stderr, "HBREAK: arming %s (0x%llx)", hbreaks[h].name, a);
        if (cr >= 0) fprintf(stderr, " when %s == 0x%llx", dbg_reg_name(cr), cv);
        fprintf(stderr, "\n");
    }

    if (wcet_fn_count > 0 || hbreak_count > 0) {
        WHV_REGISTER_NAME drn[5] = { WHvX64RegisterDr0, WHvX64RegisterDr1,
            WHvX64RegisterDr2, WHvX64RegisterDr3, WHvX64RegisterDr7 };
        WHV_REGISTER_VALUE drv[5];
        memset(drv, 0, sizeof(drv));
        unsigned long long dr7 = 0;
        int slot = 0;
        for (int w = 0; w < wcet_fn_count && slot < 4; w++, slot++) {
            drv[slot].Reg64 = wcet_fns[w].start;
            dr7 |= (2ULL << (2 * slot)); /* Gn enable; RW=00 LEN=00 = exec */
        }
        for (int h = 0; h < hbreak_count && slot < 4; h++, slot++) {
            drv[slot].Reg64 = hbreaks[h].addr;
            dr7 |= (2ULL << (2 * slot));
        }
        if (wcet_fn_count + hbreak_count > 4)
            fprintf(stderr, "WARNING: %d execution breakpoints requested, only 4 debug registers; the rest are NOT armed\n",
                wcet_fn_count + hbreak_count);
        drv[4].Reg64 = dr7;
        HRESULT whr = WHvSetVirtualProcessorRegisters(partition, 0, drn, 5, drv);
        if (FAILED(whr)) fprintf(stderr, "DR setup failed: 0x%lx\n", whr);
    }

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
    /* Start drip-feed thread for input overflow */
    CreateThread(NULL, 0, drip_feed_thread, NULL, 0, NULL);
    InitializeCriticalSection(&xhci_db_lock);
    xhci_db_lock_ready = 1;
    /* The service thread's whole job is a millisecond-scale lap, and at the
       default 15.6 ms quantum its Sleep(1) measured 61.8 laps a second --
       which is exactly the rate the pointer was then delivered at. */
    timeBeginPeriod(1);
    CreateThread(NULL, 0, hid_kick_thread, NULL, 0, NULL);

    unsigned long long exits = 0;
    int watch_hits = 0;
    int pending_irq = -1;      /* next interrupt vector to deliver, or -1 */
    int halted = 0;
    int window_registered = 0;

    /* Shadow register file -- workaround for WHP corrupting GPRs across VM exits.
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

    hw_watch_init();

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

        /* ── SMP halt spin: WHP blocks on halted VP in multi-VP mode.
              Poll timer and clear halt before re-entering VP. ── */
        if (halted && smp_cores > 1) {
            LARGE_INTEGER now;
            QueryPerformanceCounter(&now);
            double elapsed = (double)(now.QuadPart - last_tick.QuadPart) / perf_freq.QuadPart;
            if (elapsed < 0.055) {
                DWORD ms = (DWORD)((0.055 - elapsed) * 1000.0);
                if (ms > 0 && ms <= 55) Sleep(ms);
            }
            QueryPerformanceCounter(&last_tick);
            unsigned int *tc = (unsigned int *)((unsigned char *)guest_mem + 28672);
            (*tc)++;
            WHV_REGISTER_NAME clr = WHvRegisterInternalActivityState;
            WHV_REGISTER_VALUE clr_val;
            memset(&clr_val, 0, sizeof(clr_val));
            WHvSetVirtualProcessorRegisters(partition, 0, &clr, 1, &clr_val);
            halted = 0;
            exits++;
            continue;
        }

        /* ── Run VP ── */
        HRESULT hr = WHvRunVirtualProcessor(partition, 0, &ctx, sizeof(ctx));
        if (FAILED(hr)) { fprintf(stderr, "WHvRunVirtualProcessor: 0x%lx\n", hr); break; }
        exits++;
        if (uefi_mode && exits <= 20) {
            fprintf(stderr, "  exit #%llu: reason=%d RIP=0x%llx\n", exits, ctx.ExitReason, ctx.VpContext.Rip);
        }

        /* Check if guest exception handler wrote !EXC=03 to serial */
        if (dbg_exc_pending) {
            dbg_exc_pending = 0;
            /* Guest is inside its exception handler -- read registers directly */
            unsigned long long rip = 0;
            WHV_REGISTER_NAME rn = WHvX64RegisterRip;
            WHV_REGISTER_VALUE rv;
            WHvGetVirtualProcessorRegisters(partition, 0, &rn, 1, &rv);
            rip = rv.Reg64;
            int r = dbg_command_loop(3, rip);
            if (r == 1) goto done;
        }

        /* ── Handle exit ── */
        switch (ctx.ExitReason) {
        case WHvRunVpExitReasonCanceled:
            /* Host-side wall-clock sampler: the timer-kick thread cancels
               the VP every tick period, and VpContext.Rip is exactly where
               the guest was -- no injection-delivery bias (the guest-side
               PROF sampler records the interrupt-frame RIP, which WHP
               skews toward hot call targets). Enable with
               CODEX_VM_PROFILE=<file>; one HPROF:<hex-rip> line per kick. */
            if (hprof_file && hprof_count < HPROF_MAX) {
                hprof_rips[hprof_count++] = ctx.VpContext.Rip;
            }
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
        case WHvRunVpExitReasonX64Halt: {
            WHV_REGISTER_NAME hrn[] = {WHvX64RegisterRip, WHvX64RegisterRsp, WHvX64RegisterR10};
            WHV_REGISTER_VALUE hrv[3];
            WHvGetVirtualProcessorRegisters(partition, 0, hrn, 3, hrv);
            {
                const char *fn = "?";
                /* Quick symbol lookup */
                /* HLT debug output removed -- floods console at 500+ FPS, blocks Win32 message pump */
            }
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
                /* Watchdog ring dump. An IF=0 halt is often __watchdog_panic
                   firing on a stalled/looping guest (e.g. poison 0xCD data
                   driving an infinite loop). The timer ISR samples the
                   interrupted RIP into fixed kernel cells; resolving them
                   names the looping function -- the real fault site, which
                   the handler's own RIP hides. Cells: wd-last-rip 28760,
                   wd-ring-buf 28768 (16x8), wd-ring-head 28896. */
                if (28896 + 8 <= guest_mem_size) {
                    dbg_auto_load_map(g_kernel_path);
                    unsigned char *gm = (unsigned char *)guest_mem;
                    unsigned long long wd_last = *(unsigned long long *)(gm + 28760);
                    if (wd_last) {
                        int off = 0;
                        const char *nm = sym_lookup(wd_last, &off);
                        fprintf(stderr, "Watchdog last-RIP: 0x%llx", wd_last);
                        if (nm) fprintf(stderr, " <%s+0x%x>", nm, off);
                        fprintf(stderr, "\n");
                        unsigned long long head = *(unsigned long long *)(gm + 28896);
                        fprintf(stderr, "Watchdog ring (recent sampled RIPs, head=%llu, newest last):\n", head);
                        for (int wi = 0; wi < 16; wi++) {
                            unsigned long long r = *(unsigned long long *)(gm + 28768 + wi * 8);
                            if (!r) continue;
                            int o = 0;
                            const char *n = sym_lookup(r, &o);
                            fprintf(stderr, "  ring[%2d] = 0x%llx", wi, r);
                            if (n) fprintf(stderr, " <%s+0x%x>", n, o);
                            fprintf(stderr, "\n");
                        }
                    }
                }
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
                /* A guest that panics prints its dying message to COM1 and
                   halts; the message sits in output_buf. Discarding it here
                   turned every guest panic into a silent "FAIL: no output" --
                   show the tail so the guest's last words survive its death. */
                if (output_len > 0) {
                    size_t tail = output_len > 2048 ? 2048 : output_len;
                    fprintf(stderr, "Guest output before halt (%zu bytes, last %zu):\n", output_len, tail);
                    fwrite(output_buf + (output_len - tail), 1, tail, stderr);
                    fprintf(stderr, "\n");
                }
                goto done;
            }
            break;
        }
        case WHvRunVpExitReasonX64InterruptWindow:
            window_registered = 0;
            /* Fall through to post-exit logic which will inject */
            break;
        case WHvRunVpExitReasonException: {
            int vec = ctx.VpException.ExceptionType;
            unsigned long long exc_rip = ctx.VpContext.Rip;
            if (!((wcet_fn_count > 0 || hbreak_count > 0) && (vec == 1 || vec == 3)))
                fprintf(stderr, "EXC: vec=%d RIP=0x%llx exits=%llu\n", vec, exc_rip, exits);
            if (vec == 1 || vec == 3) {
                /* #DB (single-step / debug-reg watchpoint) or #BP (INT3) */
                if (vec == 1 && hw_watch_active) {
                    /* Distinguish a DR data-breakpoint hit from a single-step
                     * by inspecting DR6 (B0..B3 set => a DRn matched). */
                    WHV_REGISTER_NAME d6n = WHvX64RegisterDr6;
                    WHV_REGISTER_VALUE d6v;
                    WHvGetVirtualProcessorRegisters(partition, 0, &d6n, 1, &d6v);
                    if (d6v.Reg64 & 0xF) {
                        hw_watch_hits++;
                        unsigned long long wv = 0;
                        if (hw_watch_addr + 8 <= guest_mem_size)
                            memcpy(&wv, (unsigned char*)guest_mem + hw_watch_addr, 8);
                        if (hw_watch_log) {
                            /* One line per write, and keep going. The full crash
                               report is the wrong instrument for a cell written
                               more than once: it is the SECOND write that names
                               the corrupter, and a report that walks the stack
                               costs far more than the one fact wanted here. */
                            fprintf(stderr, "HWWATCH #%d: writer=", hw_watch_hits);
                            dbg_print_addr(exc_rip);
                            fprintf(stderr, " [0x%llx] now=0x%llx\n", hw_watch_addr, wv);
                        } else {
                        char rr[224];
                        snprintf(rr, sizeof(rr),
                            "HW WATCHPOINT #%d watch=0x%llx now=0x%llx (RIP is the instruction AFTER the access)",
                            hw_watch_hits, hw_watch_addr, wv);
                        dbg_crash_report(rr, hw_watch_addr, -1, g_kernel_path);
                        }
                        /* clear DR6 status so the next match reports cleanly */
                        WHV_REGISTER_VALUE z; z.Reg64 = 0;
                        WHvSetVirtualProcessorRegisters(partition, 0, &d6n, 1, &z);
                        if (hw_watch_hits >= 500) { fprintf(stderr, "HWWATCH: 500 hits, stopping.\n"); goto done; }
                        break;  /* resume guest at RIP (data #DB is a trap) */
                    }
                }
                if (vec == 1) {
                    /* Clear TF for single-step */
                    WHV_REGISTER_NAME fn = WHvX64RegisterRflags;
                    WHV_REGISTER_VALUE fv;
                    WHvGetVirtualProcessorRegisters(partition, 0, &fn, 1, &fv);
                    fv.Reg64 &= ~0x100ULL;
                    WHvSetVirtualProcessorRegisters(partition, 0, &fn, 1, &fv);
                }
                if (vec == 3) {
                    /* INT3: back up RIP */
                    WHV_REGISTER_NAME rn = WHvX64RegisterRip;
                    WHV_REGISTER_VALUE rv;
                    rv.Reg64 = exc_rip - 1;
                    WHvSetVirtualProcessorRegisters(partition, 0, &rn, 1, &rv);
                }
                /* Write to guest 0x7100 for kernel debugger compat */
                if (0x7100 + 32 <= guest_mem_size) {
                    unsigned char *exc = (unsigned char *)guest_mem + 0x7100;
                    *(unsigned long long *)(exc + 0) = (unsigned long long)vec;
                    *(unsigned long long *)(exc + 8) = exc_rip;
                    WHV_REGISTER_NAME en[2] = { WHvX64RegisterRsp, WHvX64RegisterRflags };
                    WHV_REGISTER_VALUE ev[2];
                    WHvGetVirtualProcessorRegisters(partition, 0, en, 2, ev);
                    *(unsigned long long *)(exc + 16) = ev[0].Reg64;
                    *(unsigned long long *)(exc + 24) = ev[1].Reg64;
                }
                /* Conditional execution breakpoint (-hbreak). A DR entry hit
                   is a FAULT: RIP is the entry and the instruction has not
                   run yet, so the register file is the caller's -- which is
                   the whole reason the condition is evaluated here and not in
                   the guest's INT3 handler. Whether or not it matches, RF is
                   set so the entry instruction executes despite DRn, and the
                   guest is resumed; the breakpoint stays armed for the next
                   call. TF is never set: this is not a stepping mode. */
                if (vec == 1 && hbreak_count > 0) {
                    WHV_REGISTER_NAME d6nh = WHvX64RegisterDr6;
                    WHV_REGISTER_VALUE d6vh;
                    WHvGetVirtualProcessorRegisters(partition, 0, &d6nh, 1, &d6vh);
                    if (d6vh.Reg64 & 0xF) {
                        int hh = -1;
                        for (int h = 0; h < hbreak_count; h++)
                            if (hbreaks[h].addr == exc_rip) { hh = h; break; }
                        if (hh >= 0) {
                            hbreaks[hh].hits++;
                            int match = 1;
                            if (hbreaks[hh].cond_reg >= 0)
                                match = (dbg_read_reg(hbreaks[hh].cond_reg) == hbreaks[hh].cond_val);
                            if (match) {
                                hbreaks[hh].matched++;
                                fprintf(stderr, "\n--- HBREAK %s hit %llu (match %llu) exits=%llu ---\n",
                                    hbreaks[hh].name, hbreaks[hh].hits, hbreaks[hh].matched, exits);
                                dbg_dump_regs();
                                if (!vga_headless || debug_mode) {
                                    int r = dbg_command_loop(99, exc_rip);
                                    if (r == 1) goto done;
                                }
                            }
                            WHV_REGISTER_VALUE zvh; zvh.Reg64 = 0;
                            WHvSetVirtualProcessorRegisters(partition, 0, &d6nh, 1, &zvh);
                            WHV_REGISTER_NAME fnh = WHvX64RegisterRflags;
                            WHV_REGISTER_VALUE fvh;
                            WHvGetVirtualProcessorRegisters(partition, 0, &fnh, 1, &fvh);
                            fvh.Reg64 |= (1ULL << 16); /* RF */
                            fvh.Reg64 &= ~0x100ULL;    /* never step */
                            WHvSetVirtualProcessorRegisters(partition, 0, &fnh, 1, &fvh);
                            break;
                        }
                    }
                }
                /* WCET measurement (observation only, takes priority).
                   DR6.B0-B3 = entry exec-breakpoint (fault, RIP = entry,
                   not yet executed); DR6.BS = TF step (trap, the
                   instruction at the previous RIP completed). */
                if (vec == 1 && wcet_fn_count > 0) {
                    WHV_REGISTER_NAME d6nw = WHvX64RegisterDr6;
                    WHV_REGISTER_VALUE d6vw;
                    WHvGetVirtualProcessorRegisters(partition, 0, &d6nw, 1, &d6vw);
                    unsigned long long dr6w = d6vw.Reg64;
                    if (dr6w & 0xF) {
                        if (wcet_active < 0) {
                            int hit = -1;
                            for (int w = 0; w < wcet_fn_count; w++)
                                if (wcet_fns[w].start == exc_rip) { hit = w; break; }
                            if (hit >= 0) {
                                WHV_REGISTER_NAME rn2 = WHvX64RegisterRsp;
                                WHV_REGISTER_VALUE rv2;
                                WHvGetVirtualProcessorRegisters(partition, 0, &rn2, 1, &rv2);
                                wcet_entry_rsp = rv2.Reg64;
                                wcet_ret_addr = 0;
                                if (wcet_entry_rsp + 8 <= guest_mem_size)
                                    memcpy(&wcet_ret_addr, (unsigned char *)guest_mem + wcet_entry_rsp, 8);
                                wcet_active = hit;
                                wcet_cur = 0;
                                wcet_prev_rip = exc_rip;
                            }
                        }
                        WHV_REGISTER_VALUE zvw; zvw.Reg64 = 0;
                        WHvSetVirtualProcessorRegisters(partition, 0, &d6nw, 1, &zvw);
                        WHV_REGISTER_NAME fnw = WHvX64RegisterRflags;
                        WHV_REGISTER_VALUE fvw;
                        WHvGetVirtualProcessorRegisters(partition, 0, &fnw, 1, &fvw);
                        fvw.Reg64 |= (1ULL << 16); /* RF: execute the entry insn despite DRn */
                        if (wcet_active >= 0) fvw.Reg64 |= 0x100; /* TF */
                        WHvSetVirtualProcessorRegisters(partition, 0, &fnw, 1, &fvw);
                        break;
                    }
                    if (wcet_active >= 0) {
                        int w = wcet_active;
                        if (wcet_prev_rip >= wcet_fns[w].start && wcet_prev_rip < wcet_fns[w].end)
                            wcet_cur++;
                        WHV_REGISTER_VALUE zvw; zvw.Reg64 = 0;
                        WHvSetVirtualProcessorRegisters(partition, 0, &d6nw, 1, &zvw);
                        WHV_REGISTER_NAME rn2 = WHvX64RegisterRsp;
                        WHV_REGISTER_VALUE rv2;
                        WHvGetVirtualProcessorRegisters(partition, 0, &rn2, 1, &rv2);
                        if (exc_rip == wcet_ret_addr && rv2.Reg64 > wcet_entry_rsp) {
                            wcet_fns[w].calls++;
                            if (wcet_cur > wcet_fns[w].max_count) wcet_fns[w].max_count = wcet_cur;
                            wcet_active = -1;
                            break; /* TF already cleared above */
                        }
                        wcet_prev_rip = exc_rip;
                        dbg_enable_single_step();
                        break;
                    }
                }
                if (debug_mode) {
                    int r = dbg_command_loop(vec, exc_rip);
                    if (r == 1) goto done;
                    if (r < 0) {
                        /* stepping over breakpoint -- will re-patch on next #DB */
                    }
                    break;
                }
                fprintf(stderr, "%s at RIP=0x%llx\n", vec == 1 ? "Single-step" : "Breakpoint", exc_rip);
                break;
            }
            { char reason[128];
              const char *exc_names[] = {"#DE","#DB","NMI","#BP","#OF","#BR","#UD","#NM",
                  "#DF","","#TS","#NP","#SS","#GP","#PF"};
              const char *exc_name = vec < 15 ? exc_names[vec] : "???";
              snprintf(reason, sizeof(reason), "Exception %s (vec=%d) at RIP=0x%llx after %llu exits",
                  exc_name, vec, exc_rip, exits);
              dbg_crash_report(reason, (unsigned long long)-1, -1, g_kernel_path);
            }
            goto done;
        }
        case WHvRunVpExitReasonMemoryAccess:
            if (uefi_mode && ctx.MemoryAccess.AccessInfo.AccessType == 2 && uefi_handle_trap(&ctx)) {
                break;  /* UEFI protocol call handled */
            }
            if (handle_device_mmio(&ctx)) {
                break;  /* device MMIO handled */
            }
            if (watch_active) {
                int wr = handle_watch_write(&ctx);
                if (wr == 2) goto done;  /* target hit or crash */
                if (wr == 1) {
                    if (watch_hit_count >= 50000) { fprintf(stderr, "WATCH: 50000 page hits.\n"); goto done; }
                    break;
                }
            }
            /* Demand-commit: if GPA is in the guest range but not yet committed,
               commit the 2MB chunk and map it, then retry the instruction. */
            {
                unsigned long long gpa = ctx.MemoryAccess.Gpa;
                if (gpa < guest_mem_size) {
                    DWORD cerr; HRESULT chr;
                    int dc = demand_commit_chunk(gpa, &cerr, &chr);
                    if (dc == 1) break;
                    if (dc == 0) {
                        char reason[160];
                        report_commit_failure(gpa, cerr, chr);
                        snprintf(reason, sizeof(reason), "Guest RAM commit failed at GPA=0x%llx (%s), host err=%lu hr=0x%08lX, after %llu exits",
                            gpa,
                            ctx.MemoryAccess.AccessInfo.AccessType == 0 ? "READ" :
                            ctx.MemoryAccess.AccessInfo.AccessType == 1 ? "WRITE" : "EXEC",
                            (unsigned long)cerr, (unsigned long)chr, exits);
                        dbg_crash_report(reason, gpa, ctx.MemoryAccess.AccessInfo.AccessType, g_kernel_path);
                        goto done;
                    }
                    /* -1: already backed, so this is the guest's own fault; report it as before. */
                }
            }
            { char reason[128];
              snprintf(reason, sizeof(reason), "Unmapped MMIO GPA=0x%llx (%s) after %llu exits",
                  ctx.MemoryAccess.Gpa,
                  ctx.MemoryAccess.AccessInfo.AccessType == 0 ? "READ" :
                  ctx.MemoryAccess.AccessInfo.AccessType == 1 ? "WRITE" : "EXEC",
                  exits);
              dbg_crash_report(reason, ctx.MemoryAccess.Gpa,
                  ctx.MemoryAccess.AccessInfo.AccessType, g_kernel_path);
            }
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
                /* Walk the tables THROUGH CR3 rather than from a hardcoded
                   0x1000. That address is where the multiboot trampoline puts
                   its tables; a UEFI boot runs on the ones built at
                   PAGE_TABLE_ADDR, so this printed four zeroes and read as
                   "the guest has no page tables" when it meant "you are
                   looking at the wrong page". A diagnostic that hardcodes an
                   address the machine is free to change lies exactly when the
                   machine does something unusual, which is the only time
                   anybody reads it. */
                WHV_REGISTER_NAME cr3n[1] = { WHvX64RegisterCr3 };
                WHV_REGISTER_VALUE cr3v[1];
                WHvGetVirtualProcessorRegisters(partition, 0, cr3n, 1, cr3v);
                unsigned long long cr3 = cr3v[0].Reg64 & ~0xFFFULL;
                unsigned long long *pml4 = (cr3 + 4096 <= guest_mem_size)
                    ? (unsigned long long *)((unsigned char *)guest_mem + cr3) : NULL;
                unsigned long long pdpt_pa = pml4 ? (pml4[0] & 0x000FFFFFFFFFF000ULL) : 0;
                unsigned long long *pdpt = (pdpt_pa && pdpt_pa + 4096 <= guest_mem_size)
                    ? (unsigned long long *)((unsigned char *)guest_mem + pdpt_pa) : NULL;
                unsigned long long pd_pa = pdpt ? (pdpt[0] & 0x000FFFFFFFFFF000ULL) : 0;
                unsigned long long *pd = (pd_pa && pd_pa + 4096 <= guest_mem_size)
                    ? (unsigned long long *)((unsigned char *)guest_mem + pd_pa) : NULL;
                fprintf(stderr, "  CR3=0x%llx PML4[0]=0x%llx PDPT[0]=0x%llx PD[0]=0x%llx PD[1]=0x%llx\n",
                    cr3, pml4 ? pml4[0] : 0ULL, pdpt ? pdpt[0] : 0ULL,
                    pd ? pd[0] : 0ULL, pd ? pd[1] : 0ULL);
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
                    msr_efer = 0xD01;                    /* keep the rdmsr shadow honest; see the UEFI entry path */
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
            if (uefi_strict && ctx.ExitReason == 4) {
                /* In strict mode the first 2 MB (minus GDT/IDT and the UEFI
                   tables) is not present, modeling firmware ownership of low
                   memory before ExitBootServices. A UEFI app that writes to a
                   fixed low address it never allocated triple-faults here. Read
                   CR2 to name the illegal address and stop -- do NOT fall into
                   the demand-commit retry below, which would spin forever on a
                   guest-PTE fault. */
                WHV_REGISTER_NAME cr2n = WHvX64RegisterCr2;
                WHV_REGISTER_VALUE cr2v; memset(&cr2v, 0, sizeof(cr2v));
                WHvGetVirtualProcessorRegisters(partition, 0, &cr2n, 1, &cr2v);
                char reason[224];
                snprintf(reason, sizeof(reason),
                    "UEFI-strict: fault at RIP=0x%llx accessing CR2=0x%llx after %llu exits -- "
                    "the UEFI app touched firmware-owned low memory it never allocated "
                    "(illegal before ExitBootServices). This is the fixed-address boot bug.",
                    ctx.VpContext.Rip, cr2v.Reg64, exits);
                dbg_crash_report(reason, cr2v.Reg64, 1, g_kernel_path);
                goto done;
            }
            if (ctx.ExitReason == 4) {
                /* Unrecoverable exception (triple fault). ctx.MemoryAccess is NOT
                   valid on this exit, so the faulting address must come from CR2 --
                   reading ctx.MemoryAccess.Gpa gave stale garbage (usually 0),
                   committed the wrong page, and resumed straight back into the
                   fault: an infinite, silent spin. A triple fault rooted in
                   host-uncommitted RAM is recoverable by committing the CR2 chunk,
                   but only a bounded number of times so a genuinely fatal fault
                   terminates with a report instead of hanging. */
                static unsigned long long tf_last_cr2 = ~0ULL;
                static int tf_retries = 0;
                WHV_REGISTER_NAME cr2n = WHvX64RegisterCr2;
                WHV_REGISTER_VALUE cr2v; memset(&cr2v, 0, sizeof(cr2v));
                WHvGetVirtualProcessorRegisters(partition, 0, &cr2n, 1, &cr2v);
                unsigned long long cr2 = cr2v.Reg64;
                if (cr2 != tf_last_cr2) { tf_last_cr2 = cr2; tf_retries = 0; }
                if (tf_retries < 2 && cr2 < guest_mem_size) {
                    DWORD cerr; HRESULT chr;
                    int dc = demand_commit_chunk(cr2, &cerr, &chr);
                    if (dc == 1) { tf_retries++; break; }
                    if (dc == 0) report_commit_failure(cr2, cerr, chr);
                }
                char reason[160];
                snprintf(reason, sizeof(reason),
                    "Triple fault (unrecoverable) CR2=0x%llx RIP=0x%llx after %llu exits",
                    cr2, (unsigned long long)ctx.VpContext.Rip, exits);
                dbg_crash_report(reason, cr2, 1, g_kernel_path);
            } else {
                char reason[128];
                snprintf(reason, sizeof(reason), "Unhandled exit reason %d after %llu exits",
                    ctx.ExitReason, exits);
                dbg_crash_report(reason, (unsigned long long)-1, -1, g_kernel_path);
            }
            goto done;
        }

        /* ── Snapshot GPRs after exit handling (captures handler's RAX/RIP changes) ── */
        WHvGetVirtualProcessorRegisters(partition, 0, shadow_names, 16, shadow_gprs);
        shadow_valid = 1;

        /* ── Sync shadow buffers + HDA while VP is NOT running ── */
        if (exits % 64 == 0) sync_shadow_buffers();
        if (hda.sd0ctl & 2) hda_drain_stream();
        if (hda.sd1ctl & 2) hda_fill_input();

        /* APs are launched by the guest's SIPI, in lapic_write(). There used to
           be a poll of the AP entry cell here that launched them from the host
           instead, because the guest never sent a SIPI -- and it never created a
           thread to run them, incremented the guest's ap-ready-count itself, and
           forced the BSP's RIP past its own wait loop. That made a dead AP look
           live. The guest sends a real SIPI now; none of it is needed. */

        /* ── Post-exit: decide what interrupt to queue ── */
        /* A device line that resolved to a vector goes first. It is
           already gated by its own mask in ioapic_raise, it is rarer than
           the tick, and the tick recurs on its own -- so preferring the
           device here costs at most one late tick and losing it would
           strand a guest waiting on the only edge it asked for. */
        if (pending_irq < 0) {
            int dv = devirq_pop();
            if (dv >= 0) pending_irq = dv;
        }
        if (pending_irq < 0) {
            int vec = pic_master.vector_base ? pic_master.vector_base : 32;
            if (kbd_irq_pending && kbd_count > 0 && pic_master.vector_base && !(pic_master.mask & (1 << 1))) {
                kbd_irq_pending = 0;
                pending_irq = vec + 1;  /* IRQ 1 = keyboard */
            } else if (!halted && pic_master.vector_base) {
                /* Busy guest: deliver the PIT tick on schedule. Exits during
                   compute come from the timer-kick thread cancelling the VP
                   run every tick period -- without it a compute-bound guest
                   never exits and never ticks (watchdog, preemption, and the
                   sampling profiler were all blind during compute). */
                LARGE_INTEGER bnow;
                QueryPerformanceCounter(&bnow);
                double belapsed = (double)(bnow.QuadPart - last_tick.QuadPart) / perf_freq.QuadPart;
                if (!no_timer && belapsed >= 0.055) {
                    QueryPerformanceCounter(&last_tick);
                    pending_irq = vec;  /* timer tick */
                }
            } else if (halted) {
                /* Halted waiting for interrupt -- timer only (no serial) */
                LARGE_INTEGER now;
                QueryPerformanceCounter(&now);
                double elapsed = (double)(now.QuadPart - last_tick.QuadPart) / perf_freq.QuadPart;
                if (!no_timer && elapsed >= 0.055) {
                    QueryPerformanceCounter(&last_tick);
                    if (smp_cores > 1) {
                        unsigned int *tc = (unsigned int *)((unsigned char *)guest_mem + 28672);
                        (*tc)++;
                        /* TICK-BUMP debug removed */
                        WHV_REGISTER_NAME clr_name = WHvRegisterInternalActivityState;
                        WHV_REGISTER_VALUE clr_val;
                        memset(&clr_val, 0, sizeof(clr_val));
                        WHvSetVirtualProcessorRegisters(partition, 0, &clr_name, 1, &clr_val);
                        halted = 0;
                    } else {
                        pending_irq = vec;  /* timer tick */
                    }
                } else {
                    DWORD ms = (DWORD)((0.055 - elapsed) * 1000.0);
                    if (ms > 50) ms = 50;
                    if (ms > 0) Sleep(ms);
                    QueryPerformanceCounter(&now);
                    elapsed = (double)(now.QuadPart - last_tick.QuadPart) / perf_freq.QuadPart;
                    if (!no_timer && elapsed >= 0.055) {
                        QueryPerformanceCounter(&last_tick);
                        pending_irq = vec;
                    }
                }
            }
        }

        /* Poll NAT sockets for incoming data and inject into NE2000 ring buffer */
        if (exits % 10 == 0) { nat_poll_connect(); nat_poll_rx(); udp_poll_rx(); if (portfwd_count > 0) { portfwd_poll(); nat_poll_retransmit(); } ne2k_inject_rx(); if (e1000_nat) e1000_nat_rx(); }


        /* Comparator match is checked on the same cadence as the other
           devices. It only queues a vector; the slot above delivers it. */
        hpet_poll();

        /* Make a running guest's serial output readable on disk. Rate-limited
           inside; see poll_output_dump. */
        if (exits % 10 == 0) poll_output_dump();

        /* Drip-feed input and set stdin-eof when fully consumed */
        if (guest_mem) {
            if (input_file) {
                input_drip_feed();
                unsigned long long wpos = *(unsigned long long *)((unsigned char *)guest_mem + 28704);
                unsigned long long rpos = *(unsigned long long *)((unsigned char *)guest_mem + 28712);
                int all_fed = !input_overflow || input_overflow_pos >= input_overflow_len;
                if (wpos > 0 && rpos >= wpos && all_fed)
                    *(unsigned long long *)((unsigned char *)guest_mem + 28920) = 1ULL;
            } else {
                *(unsigned long long *)((unsigned char *)guest_mem + 28920) = 1ULL;
            }
        }

        /* Scripted keyboard injection: deliver the next scancode once its
           scheduled time arrives. Uses the same 28680 key-buffer path the
           window uses, so the guest sees ordinary keystrokes. */
        /* Timeline keyboard: deliver each scancode at its scheduled time. */
        if (inject_keyt_idx < inject_keyt_count) {
            LARGE_INTEGER know;
            QueryPerformanceCounter(&know);
            double kel = (double)(know.QuadPart - screenshot_start.QuadPart) * 1000.0 / perf_freq.QuadPart;
            if (kel >= inject_keyt[inject_keyt_idx].t_ms) {
                unsigned char sc = inject_keyt[inject_keyt_idx++].sc;
                if (hid_keys_only) {
                    hid_key_event(sc);
                } else {
                    pending_kbd_scancode = sc;
                    pending_kbd_valid = 1;
                    kbd_enqueue(sc);
                    kbd_irq_pending = 1;
                }
            }
        }

        hid_service_pending(0);

        if (inject_key_idx < inject_key_count) {
            LARGE_INTEGER now;
            QueryPerformanceCounter(&now);
            double el = (double)(now.QuadPart - screenshot_start.QuadPart) * 1000.0 / perf_freq.QuadPart;
            if (el >= inject_key_start_ms + inject_key_idx * inject_key_interval_ms) {
                unsigned char sc = inject_keys[inject_key_idx++];
                pending_kbd_scancode = sc;
                pending_kbd_valid = 1;
                kbd_enqueue(sc);       /* also feed the PS/2 port 0x60 queue */
                hid_key_event(sc);     /* and the USB HID held-key set */
                kbd_irq_pending = 1;
            }
        }

        /* Deliver a pending scancode to the kernel key buffer (28680) every
           iteration. A compute-bound GOP guest only exits ~18x/sec (the 55 ms
           kicker), so leaving this to sync_shadow_buffers -- which runs every
           64 exits (~3.5 s) -- made keyboard input unusably laggy for both
           injected and real keys. Guests that programmed the PIC with IRQ1
           unmasked get keys through their own ISR instead (real-hardware
           semantics) -- drop the host-side copy so keys arrive exactly once. */
        if (pending_kbd_valid && 28680 + 1 <= guest_mem_size) {
            if (pic_master.vector_base && !(pic_master.mask & (1 << 1))) {
                pending_kbd_valid = 0;
            } else {
                *((unsigned char *)guest_mem + 28680) = (unsigned char)pending_kbd_scancode;
                pending_kbd_valid = 0;
            }
        }

        /* Screenshot timer */
        if (screenshot_path) {
            LARGE_INTEGER now;
            QueryPerformanceCounter(&now);
            double elapsed_ms = (double)(now.QuadPart - screenshot_start.QuadPart) * 1000.0 / perf_freq.QuadPart;
            if (elapsed_ms >= screenshot_delay_ms) {
                double fps = (elapsed_ms > 0) ? gpu_frame_count * 1000.0 / elapsed_ms : 0;
                /* Behind the probe switch: an unconditional extra DIAG line
                   would land in every screenshot run, including the GUI tests.
                   Gated on the environment DIRECTLY rather than on
                   gpu_probe_on, which is set from gpu_init_threads and so only
                   exists once the rasterizer has run -- a desk with no 3D pane
                   open never set it, which is exactly where these lines were
                   wanted. The braces matter too: without them the second line
                   below escaped the guard and printed in every run. */
                if (getenv("CODEX_GPU_PROBE")) {
                    fprintf(stderr, "DIAG: mouse-reports=%ld report-rate=%.1f/s arms=%ld arm-rate=%.1f/s\n",
                            hid_mouse_reports,
                            (elapsed_ms > 0) ? hid_mouse_reports * 1000.0 / elapsed_ms : 0.0,
                            xhci_mouse_doorbells,
                            (elapsed_ms > 0) ? xhci_mouse_doorbells * 1000.0 / elapsed_ms : 0.0);
                        fprintf(stderr, "DIAG: syncs=%ld sync-fps=%.1f\n", shadow_sync_count,
                            (elapsed_ms > 0) ? shadow_sync_count * 1000.0 / elapsed_ms : 0.0);
                        /* The HID service thread's lap rate BOUNDS the report
                           rate: one lap delivers at most one report, so a lap
                           rate below the input rate is the whole story and
                           reads from outside as a slow pointer. It sat at 61.8
                           until timeBeginPeriod(1) was asked for. */
                        fprintf(stderr, "DIAG: service-laps=%ld lap-rate=%.1f/s rerings=%ld rering-rate=%.1f/s\n",
                            hid_service_laps, (elapsed_ms > 0) ? hid_service_laps * 1000.0 / elapsed_ms : 0.0,
                            hid_reringe_calls, (elapsed_ms > 0) ? hid_reringe_calls * 1000.0 / elapsed_ms : 0.0);
                }
                fprintf(stderr, "DIAG: frames=%d elapsed=%.0fms fps=%.1f tris/frame=%d slices=%d stacks=%d\n",
                    gpu_frame_count, elapsed_ms, fps, gpu_last_tri_count,
                    0, 0);
                sync_shadow_buffers();
                save_screenshot_bmp(screenshot_path);
                debug_exit_code = 0;
                goto done;
            }
        }

        /* Ctrl+C/Break handler sets shutdown_requested */
        if (shutdown_requested) {
            debug_exit_code = 0;
            goto done;
        }
    }
done:
    fprintf(stderr, "VM exited (code=%d, exits=%llu, watch_hits=%d)\n", debug_exit_code, exits, watch_hit_count);
    if (hprof_file && hprof_count > 0) {
        FILE *hf = fopen(hprof_file, "w");
        if (hf) {
            for (int hi = 0; hi < hprof_count; hi++) fprintf(hf, "HPROF:%016llx\n", hprof_rips[hi]);
            fclose(hf);
            fprintf(stderr, "Profile: %d samples -> %s\n", hprof_count, hprof_file);
        }
    }
    for (int w = 0; w < wcet_fn_count; w++)
        fprintf(stderr, "WCET-OBS: %s max=%llu calls=%llu range=0x%llx-0x%llx\n",
            wcet_fns[w].name, wcet_fns[w].max_count, wcet_fns[w].calls,
            wcet_fns[w].start, wcet_fns[w].end);
    /* Both counts, always. A run that reports only the matches cannot be
       told from one whose breakpoint was never reached, and that difference
       is the whole negative control. */
    for (int h = 0; h < hbreak_count; h++) {
        fprintf(stderr, "HBREAK-OBS: %s hits=%llu matched=%llu",
            hbreaks[h].name, hbreaks[h].hits, hbreaks[h].matched);
        if (hbreaks[h].cond_reg >= 0)
            fprintf(stderr, " cond=%s==0x%llx", dbg_reg_name(hbreaks[h].cond_reg), hbreaks[h].cond_val);
        fprintf(stderr, "\n");
    }
    if (trace_file && guest_mem) {
        unsigned long long te = 458752, tc = 458760, tb = 458768;  /* trace cells, mirrors X86_64Boot.codex */
        if (te + 8 <= guest_mem_size) {
            unsigned long long enabled = *(unsigned long long *)((unsigned char *)guest_mem + te);
            unsigned long long cursor = *(unsigned long long *)((unsigned char *)guest_mem + tc);
            if (enabled && cursor > 0 && tb + cursor * 16 <= guest_mem_size) {
                FILE *tf = fopen(trace_file, "wb");
                if (tf) {
                    fwrite(&cursor, 8, 1, tf);
                    fwrite((unsigned char *)guest_mem + tb, 16, (size_t)cursor, tf);
                    fclose(tf);
                    fprintf(stderr, "Trace: %llu allocations -> %s\n", cursor, trace_file);
                }
            }
        }
    }
    /* Close any NAT sockets still open. Connections in state 3 already
       had shutdown(SD_SEND) called by the FIN handler -- buffered data
       drains normally. Active connections get a graceful half-close. */
    for (int i = 0; i < NAT_MAX_CONN; i++) {
        if (nat_conns[i].sock != INVALID_SOCKET && (nat_conns[i].active || nat_conns[i].state == 3)) {
            /* State 3 used to be skipped here on the reasoning that its
               FIN handler had already called shutdown, so the data would
               drain by itself. What drains by itself is what the KERNEL
               holds; txbuf is ours, and a state-3 connection was flushed
               nowhere else, so whatever the one send() in the FIN handler
               could not take died here. Drain every connection, bounded,
               and only then take the send side down. */
            double drain_until = now_ms_for_timer() + NAT_EXIT_DRAIN_MS;
            nat_tx_flush(&nat_conns[i]);
            while (nat_conns[i].txlen > 0 && now_ms_for_timer() < drain_until) {
                /* Wait for writability rather than spinning on a
                   non-blocking socket: the peer here is a reader that is
                   slow, not one that is gone, and a spin would burn a core
                   for the whole drain. */
                fd_set wr;
                struct timeval tv;
                int before;
                FD_ZERO(&wr);
                FD_SET(nat_conns[i].sock, &wr);
                tv.tv_sec = 1; tv.tv_usec = 0;
                if (select(0, NULL, &wr, NULL, &tv) <= 0) continue;
                before = nat_conns[i].txlen;
                nat_tx_flush(&nat_conns[i]);
                /* Writable and yet it took nothing: the peer is gone, not
                   slow. Without this the loop spins on a socket that will
                   never drain, for the whole bound. */
                if (nat_conns[i].txlen >= before) break;
            }
            if (nat_conns[i].txlen > 0) nat_freed_exit += (unsigned long long)nat_conns[i].txlen;
            shutdown(nat_conns[i].sock, SD_SEND);
            nat_conn_free(&nat_conns[i]);
        }
    }
    /* Printed unconditionally and after the drain above, so a run that
       loses bytes names the site that lost them instead of leaving the
       two ends to disagree about a number neither can account for. seg is
       what the guest's data segments carried; the four drop counters and
       sent should add up to it. */
    if (nat_seg_bytes) {
        fprintf(stderr, "NAT TX BYTES: seg=%llu queued=%llu sent=%llu "
                "drop-noconn=%llu drop-badstate=%llu drop-oom=%llu drop-freed=%llu "
                "(reap=%llu exit=%llu)\n",
                nat_seg_bytes, nat_queued, nat_sock_sent,
                nat_seg_noconn, nat_seg_badstate, nat_queue_oom, nat_freed_unsent,
                nat_freed_reap, nat_freed_exit);
    }
    /* words/pio-exits is 1.0 while the data port takes one exit per word and
       rises as a batched path carries more; flush-ms is the other site's cost
       in wall clock, which is the only unit that compares against the 57 s the
       write takes. A run that transfers nothing prints nothing. */
    if (ide.wfp) { fclose(ide.wfp); ide.wfp = NULL; }
    if (ide_slave.wfp) { fclose(ide_slave.wfp); ide_slave.wfp = NULL; }
    if (ide_pio_words || ide_flush_calls) {
        fprintf(census_out(), "IDE CENSUS: pio-exits=%llu str-exits=%llu words=%llu reg-exits=%llu "
                "out-batch(hits=%llu words=%llu) in-batch(hits=%llu words=%llu) "
                "flush-entries=%llu flush-calls=%llu flush-bytes=%llu flush-ms=%.1f "
                "refused(nopath=%llu nodata=%llu oob=%llu openfail=%llu)\n",
                ide_pio_exits, ide_pio_str_exits, ide_pio_words, ide_reg_exits,
                ide_out_batch_hits, ide_out_batched,
                ide_in_batch_hits, ide_in_batched,
                ide_flush_entries, ide_flush_calls, ide_flush_bytes, ide_flush_ms,
                ide_flush_nopath, ide_flush_nodata, ide_flush_oob, ide_flush_openfail);
    }
    /* A run that never touches EXTCNF_CTRL prints nothing, so this line
       appearing at all says the semaphore path executed. foreign=0 is the
       claim a correct acquire makes; writes= is the loop's cost in MMIO
       transactions, which is the other half of the registered defect. */
    if (i219_extcnf_writes) {
        fprintf(census_out(), "EXTCNF CENSUS: writes=%llu foreign=%llu cfgbits=%llu violated=%d final=%08x\n",
                i219_extcnf_writes, i219_extcnf_foreign, i219_extcnf_cfgbits,
                i219_extcnf_violated, i219_extcnf);
        fflush(census_out());
    }
    if (dumpmem_addr && guest_mem && dumpmem_addr + dumpmem_len <= guest_mem_size) {
        fprintf(stderr, "DUMPMEM 0x%llx len %llu:\n", dumpmem_addr, dumpmem_len);
        unsigned char *base = (unsigned char*)guest_mem + dumpmem_addr;
        for (unsigned long long off = 0; off < dumpmem_len; off += 16) {
            fprintf(stderr, "  0x%llx:", dumpmem_addr + off);
            for (int j = 0; j < 16 && off+j < dumpmem_len; j++) fprintf(stderr, " %02x", base[off+j]);
            fprintf(stderr, "  |");
            for (int j = 0; j < 16 && off+j < dumpmem_len; j++) {
                unsigned char c = base[off+j];
                fprintf(stderr, "%c", (c >= 32 && c < 127) ? c : '.');
            }
            fprintf(stderr, "|\n");
        }
    }
    if (output_file) dump_output_file(output_file);
    cleanup_whp();  /* also runs via atexit if we exit abnormally */
    if (ide.data) free(ide.data);
    if (ide_slave.data) free(ide_slave.data);
    if (guest_mem) { VirtualFree(guest_mem, 0, MEM_RELEASE); guest_mem = NULL; }
    WSACleanup();
    int rc = (debug_exit_code >= 0) ? (debug_exit_code << 1) | 1 : 0;
    fprintf(stderr, "FINAL: debug_exit_code=%d process_exit=%d\n", debug_exit_code, rc);
    return rc;
}
