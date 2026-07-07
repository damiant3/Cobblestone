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
#pragma comment(lib, "winmm.lib")
#include <mmsystem.h>

/* Forward declarations (defined later in the file) */
static void *guest_mem;
static size_t guest_mem_size;
static WHV_PARTITION_HANDLE partition;
static int smp_cores;  /* 0 or 1 = single-core (default); 2-16 = multi-core */

/* System-wide mutex: serialize WHP partition create/destroy across all
   codex-vm instances.  vid.sys on Win11 26100 corrupts its kernel heap
   when multiple processes hit WHvCreatePartition / WHvDeletePartition /
   WHvMapGpaRange concurrently — cascade of 20 event-viewer entries and
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
static volatile unsigned char pending_mouse[3] = {0};
static volatile int pending_mouse_valid = 0;
static volatile int pending_mouse_abs_x = 0, pending_mouse_abs_y = 0, pending_mouse_btn = 0;
static volatile unsigned long long pending_kbd_scancode = 0;
static volatile int pending_kbd_valid = 0;

/* Forward declaration — full definition + instance below serial/PIC/NE2K */
typedef struct IdeState_ {
    unsigned char *data;
    size_t size;
    int sect_count, lba_lo, lba_mid, lba_hi, drive_head;
    int status, error;
    size_t buf_off;
    int buf_remaining;
    int sectors_left;
    int writing;            /* 1 during a WRITE SECTORS (0x30) transfer */
    const char *path;       /* disk image path, for write-back */
} IdeState;
static IdeState ide;
static void ide_flush(IdeState *d, size_t off, size_t len);

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

/* UEFI protocol GUIDs (in-memory layout: Data1 LE32, Data2 LE16, Data3 LE16, Data4 raw) */
static const unsigned char GUID_BLOCK_IO[16]     = {0x21,0x5B,0x4E,0x96, 0x59,0x64, 0xD2,0x11, 0x8E,0x39,0x00,0xA0,0xC9,0x69,0x72,0x3B};
static const unsigned char GUID_SFS[16]          = {0x22,0x5B,0x4E,0x96, 0x59,0x64, 0xD2,0x11, 0x8E,0x39,0x00,0xA0,0xC9,0x69,0x72,0x3B};
static const unsigned char GUID_LOADED_IMAGE[16] = {0xA1,0x31,0x1B,0x5B, 0x62,0x95, 0xD2,0x11, 0x8E,0x3F,0x00,0xA0,0xC9,0x69,0x72,0x3B};
static const unsigned char GUID_DEVICE_PATH[16]  = {0x91,0x6E,0x57,0x09, 0x3F,0x6D, 0xD2,0x11, 0x8E,0x39,0x00,0xA0,0xC9,0x69,0x72,0x3B};
static const unsigned char GUID_GOP[16]          = {0xDE,0xA9,0x42,0x90, 0xDC,0x23, 0x38,0x4A, 0x96,0xFB,0x7A,0xDE,0xD0,0x80,0x51,0x6A};

/* ══ PCI Configuration Space ══ */
#define PCI_MAX_DEVICES 8
static unsigned int pci_config_addr = 0;
static struct {
    unsigned short vendor, device;
    unsigned char class_code, subclass, progif, header_type;
    unsigned int bar[6];
    unsigned char irq_line;
    unsigned short command;
} pci_devices[PCI_MAX_DEVICES];
static int pci_device_count = 0;

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
    pci_devices[i].irq_line = irq;
    pci_devices[i].command = 0x0003; /* IO + MMIO enabled */
    return i;
}

static unsigned int pci_read_config(int dev, int func, int offset) {
    if (func != 0 || dev < 0 || dev >= pci_device_count) return 0xFFFFFFFF;
    switch (offset & 0xFC) {
    case 0x00: return pci_devices[dev].vendor | ((unsigned int)pci_devices[dev].device << 16);
    case 0x04: return pci_devices[dev].command;
    case 0x08: return ((unsigned int)pci_devices[dev].class_code << 24) |
                      ((unsigned int)pci_devices[dev].subclass << 16) |
                      ((unsigned int)pci_devices[dev].progif << 8);
    case 0x0C: return (unsigned int)pci_devices[dev].header_type << 16;
    case 0x10: return pci_devices[dev].bar[0];
    case 0x14: return pci_devices[dev].bar[1];
    case 0x18: return pci_devices[dev].bar[2];
    case 0x1C: return pci_devices[dev].bar[3];
    case 0x20: return pci_devices[dev].bar[4];
    case 0x24: return pci_devices[dev].bar[5];
    case 0x3C: return pci_devices[dev].irq_line;
    default: return 0;
    }
}

static void pci_write_config(int dev, int func, int offset, unsigned int val) {
    if (func != 0 || dev < 0 || dev >= pci_device_count) return;
    if ((offset & 0xFC) == 0x04) pci_devices[dev].command = (unsigned short)(val & 0xFFFF);
    else if ((offset & 0xFC) >= 0x10 && (offset & 0xFC) <= 0x24) {
        int bar_idx = ((offset & 0xFC) - 0x10) / 4;
        if (val == 0xFFFFFFFF)
            pci_devices[dev].bar[bar_idx] = 0xFFFF0000; /* report 64KB size */
        else
            pci_devices[dev].bar[bar_idx] = val;
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

/* ══ xHCI Controller Emulation ══ */
#define XHCI_BAR       0xFE800000ULL
#define XHCI_BAR_SIZE  0x4000       /* 16 KB register space */
#define XHCI_CAP_LEN   32           /* capability registers: 32 bytes */
#define XHCI_MAX_SLOTS 32
#define XHCI_MAX_PORTS 4

static struct {
    unsigned int usbcmd;
    unsigned int usbsts;
    unsigned int dnctrl;
    unsigned long long crcr;
    unsigned long long dcbaap;
    unsigned int config;
    unsigned int portsc[XHCI_MAX_PORTS];
    /* Event ring */
    unsigned long long erstba;   /* event ring segment table base */
    unsigned short erdp_idx;     /* current event ring dequeue index */
    unsigned long long er_addr;  /* event ring base (from ERST entry 0) */
    unsigned short er_size;      /* event ring size (from ERST entry 0) */
    int er_ccs;                  /* consumer cycle state */
} xhci;

static int xhci_next_slot = 1;

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

/* Config + Interface + 2 Bulk Endpoints (32 bytes total) */
static const unsigned char usb_cfg_desc[] = {
    /* Config descriptor */
    9, 2,       /* bLength, bDescriptorType=CONFIG */
    32, 0,      /* wTotalLength = 32 */
    1,          /* bNumInterfaces */
    1,          /* bConfigurationValue */
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
    0x3D, 0x04, /* idVendor = 0x043D (Lexmark — generic) */
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

/* Build an 8-byte HID boot keyboard report from current PS/2 state */
static void build_hid_keyboard_report(unsigned char *report) {
    memset(report, 0, 8);
    /* report[0] = modifier byte, report[2..7] = up to 6 keycodes */
    int slot = 2;
    for (int i = 0; i < kbd_count && slot < 8; i++) {
        unsigned char sc = kbd_queue[(kbd_head + i) % KBD_QUEUE_SIZE];
        if (sc & 0x80) continue; /* key-up, skip */
        unsigned char hid = ps2_to_hid[sc & 0x7F];
        if (hid >= 0xE0 && hid <= 0xE7) {
            report[0] |= (1 << (hid - 0xE0)); /* modifier */
        } else if (hid > 0 && slot < 8) {
            report[slot++] = hid;
        }
    }
}

static void xhci_post_event(int trb_type, int slot, int completion, unsigned long long trb_ptr) {
    if (xhci.er_addr == 0 || xhci.er_size == 0) return;
    unsigned long long off = xhci.er_addr + (unsigned long long)xhci.erdp_idx * 16;
    if (off + 16 > guest_mem_size) return;
    unsigned char *ev = (unsigned char *)guest_mem + off;
    /* TRB pointer (param lo/hi) = address of command/transfer TRB that completed */
    *(unsigned int *)(ev + 0) = (unsigned int)(trb_ptr & 0xFFFFFFFF);
    *(unsigned int *)(ev + 4) = (unsigned int)(trb_ptr >> 32);
    /* Status: completion code in bits 31:24, residual in bits 23:0 */
    *(unsigned int *)(ev + 8) = (completion & 0xFF) << 24;
    /* Control: TRB type in bits 15:10, slot in bits 31:24, cycle bit */
    *(unsigned int *)(ev + 12) = ((trb_type & 0x3F) << 10) | ((slot & 0xFF) << 24) | (xhci.er_ccs & 1);
    xhci.erdp_idx++;
    if (xhci.erdp_idx >= xhci.er_size) {
        xhci.erdp_idx = 0;
        xhci.er_ccs ^= 1;
    }
}

static void xhci_handle_doorbell(int db, unsigned int val) {
    if (db == 0) {
        /* Command ring doorbell — process TRBs */
        unsigned long long ring_addr = xhci.crcr & ~0x3FULL;
        for (int safety = 0; safety < 16; safety++) {
            if (ring_addr + 16 > guest_mem_size) break;
            unsigned char *trb = (unsigned char *)guest_mem + ring_addr;
            unsigned int control = *(unsigned int *)(trb + 12);
            int trb_type = (control >> 10) & 0x3F;
            if (trb_type == 0) break;
            if (trb_type == 9) { /* ENABLE_SLOT */
                xhci_next_slot = 1;
                xhci_post_event(33, xhci_next_slot, 1, ring_addr); /* Command Completion, success */
            } else if (trb_type == 11) { /* ADDRESS_DEVICE */
                xhci_post_event(33, 1, 1, ring_addr);
            } else if (trb_type == 12) { /* CONFIGURE_ENDPOINT */
                xhci_post_event(33, 1, 1, ring_addr);
            } else if (trb_type == 13) { /* EVALUATE_CONTEXT */
                xhci_post_event(33, 1, 1, ring_addr);
            } else if (trb_type == 23) { /* NOOP */
                xhci_post_event(33, 0, 1, ring_addr);
            }
            ring_addr += 16;
            xhci.crcr = ring_addr | (xhci.crcr & 0x3F);
        }
    } else if (db >= 1 && db <= XHCI_MAX_SLOTS) {
        /* Transfer ring doorbell for a device slot — process transfer TRBs */
        /* The guest wrote TRBs at the transfer ring address stored in the
           device context's endpoint context. For simplicity, we process
           control transfers (SETUP+DATA+STATUS) by pattern matching. */
        /* Read the device context array to find the endpoint's TR dequeue pointer.
           For now, use a simpler approach: the guest just called usb-control-transfer
           or usb-bulk-in/out, which wrote TRBs and rang this doorbell. We can
           find the TRBs by reading the endpoint context from DCBAAP. */
        if (xhci.dcbaap == 0 || xhci.dcbaap + (db + 1) * 8 > guest_mem_size) return;
        unsigned long long slot_ctx_addr = *(unsigned long long *)((unsigned char *)guest_mem + xhci.dcbaap + db * 8);
        if (slot_ctx_addr == 0 || slot_ctx_addr + 64 * 32 > guest_mem_size) return;
        /* Endpoint context for the target endpoint (val & 0xFF is the endpoint index) */
        int ep_idx = val & 0xFF;
        if (ep_idx == 0) ep_idx = 1; /* control endpoint = DCI 1 */
        unsigned char *ep_ctx = (unsigned char *)guest_mem + slot_ctx_addr + ep_idx * 32;
        unsigned long long tr_dequeue = *(unsigned long long *)(ep_ctx + 8) & ~0xFULL;
        if (tr_dequeue == 0 || tr_dequeue + 16 > guest_mem_size) return;

        /* Walk the transfer ring looking for SETUP TRBs (control) or NORMAL TRBs (bulk) */
        for (int safety = 0; safety < 32; safety++) {
            if (tr_dequeue + 16 > guest_mem_size) break;
            unsigned char *trb = (unsigned char *)guest_mem + tr_dequeue;
            unsigned int ctrl = *(unsigned int *)(trb + 12);
            int tt = (ctrl >> 10) & 0x3F;
            if (tt == 0) break;

            if (tt == 2) { /* SETUP stage */
                unsigned char setup[8];
                memcpy(setup, trb, 8);
                int bmRequestType = setup[0];
                int bRequest = setup[1];
                int wValue = setup[2] | (setup[3] << 8);
                int wLength = setup[6] | (setup[7] << 8);
                (void)bmRequestType;

                /* Look for DATA stage TRB next */
                tr_dequeue += 16;
                if (tr_dequeue + 16 > guest_mem_size) break;
                unsigned char *data_trb = (unsigned char *)guest_mem + tr_dequeue;
                unsigned int data_ctrl = *(unsigned int *)(data_trb + 12);
                int data_tt = (data_ctrl >> 10) & 0x3F;
                unsigned long long data_buf = *(unsigned long long *)data_trb & ~0xFULL;
                int data_len = *(unsigned int *)(data_trb + 8) & 0x1FFFF;

                if (data_tt == 3 && data_buf > 0 && data_buf + data_len <= guest_mem_size) {
                    if (bRequest == 6) { /* GET_DESCRIPTOR */
                        int desc_type = (wValue >> 8) & 0xFF;
                        const unsigned char *dev_d = usb_dev_desc;
                        int dev_d_sz = (int)sizeof(usb_dev_desc);
                        const unsigned char *cfg_d = usb_cfg_desc;
                        int cfg_d_sz = (int)sizeof(usb_cfg_desc);
                        if (db == 2) { dev_d = usb_hid_dev_desc; dev_d_sz = (int)sizeof(usb_hid_dev_desc); cfg_d = usb_hid_cfg_desc; cfg_d_sz = (int)sizeof(usb_hid_cfg_desc); }
                        if (db == 3) { dev_d = usb_uvc_dev_desc; dev_d_sz = (int)sizeof(usb_uvc_dev_desc); cfg_d = usb_uvc_cfg_desc; cfg_d_sz = (int)sizeof(usb_uvc_cfg_desc); }
                        if (desc_type == 1) { /* DEVICE */
                            int n = data_len < dev_d_sz ? data_len : dev_d_sz;
                            memcpy((unsigned char *)guest_mem + data_buf, dev_d, n);
                        } else if (desc_type == 2) { /* CONFIG */
                            int n = data_len < cfg_d_sz ? data_len : cfg_d_sz;
                            memcpy((unsigned char *)guest_mem + data_buf, cfg_d, n);
                        } else if (desc_type == 0x22 && db == 2) { /* HID REPORT */
                            int n = data_len < (int)sizeof(usb_hid_report_desc) ? data_len : (int)sizeof(usb_hid_report_desc);
                            memcpy((unsigned char *)guest_mem + data_buf, usb_hid_report_desc, n);
                        }
                    }
                }
                /* Skip STATUS TRB and post transfer event */
                tr_dequeue += 16;
                if (tr_dequeue + 16 <= guest_mem_size) {
                    unsigned char *sts_trb = (unsigned char *)guest_mem + tr_dequeue;
                    int sts_tt = (*(unsigned int *)(sts_trb + 12) >> 10) & 0x3F;
                    if (sts_tt == 4) {
                        xhci_post_event(32, db, 1, tr_dequeue); /* Transfer Event, success */
                        tr_dequeue += 16;
                    }
                }
                (void)wLength;
            } else if (tt == 1) { /* NORMAL (bulk or interrupt transfer) */
                unsigned long long buf_addr = *(unsigned long long *)trb & ~0xFULL;
                int buf_len = *(unsigned int *)(trb + 8) & 0x1FFFF;
                int dir_in = (ctrl >> 16) & 1;

                /* HID keyboard interrupt IN — return 8-byte boot report */
                if (db == 2 && buf_addr > 0 && buf_addr + 8 <= guest_mem_size) {
                    unsigned char report[8];
                    build_hid_keyboard_report(report);
                    int n = buf_len < 8 ? buf_len : 8;
                    memcpy((unsigned char *)guest_mem + buf_addr, report, n);
                    tr_dequeue += 16;
                    continue;
                }

                if (buf_addr > 0 && buf_addr + buf_len <= guest_mem_size && ide.data) {
                    unsigned char *buf = (unsigned char *)guest_mem + buf_addr;
                    if (!dir_in && buf_len >= 31 && *(unsigned int *)buf == 0x43425355) {
                        /* CBW (Command Block Wrapper) */
                        unsigned int tag = *(unsigned int *)(buf + 4);
                        unsigned int xfer_len = *(unsigned int *)(buf + 8);
                        int flags = buf[12];
                        int cb_len = buf[14];
                        unsigned char scsi_cmd = buf[15];
                        (void)cb_len;

                        /* Look for the next NORMAL TRB (data phase) */
                        tr_dequeue += 16;
                        unsigned char *next_trb = (tr_dequeue + 16 <= guest_mem_size) ?
                            (unsigned char *)guest_mem + tr_dequeue : NULL;

                        if (scsi_cmd == 0x28 && next_trb) { /* READ_10 */
                            unsigned int lba = ((unsigned int)buf[17] << 24) | ((unsigned int)buf[18] << 16) |
                                               ((unsigned int)buf[19] << 8) | buf[20];
                            unsigned int sectors = ((unsigned int)buf[22] << 8) | buf[23];
                            unsigned long long data_addr = *(unsigned long long *)next_trb & ~0xFULL;
                            unsigned long long disk_off = (unsigned long long)lba * 512;
                            unsigned long long nbytes = (unsigned long long)sectors * 512;
                            if (data_addr > 0 && data_addr + nbytes <= guest_mem_size &&
                                disk_off + nbytes <= ide.size) {
                                memcpy((unsigned char *)guest_mem + data_addr, ide.data + disk_off, (size_t)nbytes);
                            }
                            tr_dequeue += 16;
                        } else if (scsi_cmd == 0x12 && next_trb) { /* INQUIRY */
                            unsigned long long data_addr = *(unsigned long long *)next_trb & ~0xFULL;
                            if (data_addr > 0 && data_addr + 36 <= guest_mem_size) {
                                unsigned char inq[36] = {0};
                                inq[0] = 0x00; /* direct access block device */
                                inq[1] = 0x80; /* removable */
                                inq[2] = 0x05; /* SPC-3 */
                                inq[4] = 31;   /* additional length */
                                memcpy(inq + 8, "Codex   ", 8);
                                memcpy(inq + 16, "Virtual Disk    ", 16);
                                memcpy(inq + 32, "1.0 ", 4);
                                memcpy((unsigned char *)guest_mem + data_addr, inq, 36);
                            }
                            tr_dequeue += 16;
                        } else if (scsi_cmd == 0x25 && next_trb) { /* READ_CAPACITY_10 */
                            unsigned long long data_addr = *(unsigned long long *)next_trb & ~0xFULL;
                            if (data_addr > 0 && data_addr + 8 <= guest_mem_size) {
                                unsigned int last_lba = (unsigned int)(ide.size / 512) - 1;
                                unsigned char cap[8];
                                cap[0] = (last_lba >> 24) & 0xFF;
                                cap[1] = (last_lba >> 16) & 0xFF;
                                cap[2] = (last_lba >> 8) & 0xFF;
                                cap[3] = last_lba & 0xFF;
                                cap[4] = 0; cap[5] = 0; cap[6] = 2; cap[7] = 0; /* 512 bytes */
                                memcpy((unsigned char *)guest_mem + data_addr, cap, 8);
                            }
                            tr_dequeue += 16;
                        } else if (scsi_cmd == 0x00) { /* TEST_UNIT_READY */
                            /* No data phase */
                        }

                        /* Look for CSW (status) NORMAL TRB */
                        if (tr_dequeue + 16 <= guest_mem_size) {
                            unsigned char *csw_trb = (unsigned char *)guest_mem + tr_dequeue;
                            int csw_tt = (*(unsigned int *)(csw_trb + 12) >> 10) & 0x3F;
                            if (csw_tt == 1) {
                                unsigned long long csw_addr = *(unsigned long long *)csw_trb & ~0xFULL;
                                if (csw_addr > 0 && csw_addr + 13 <= guest_mem_size) {
                                    unsigned char csw[13] = {0};
                                    *(unsigned int *)csw = 0x53425355; /* CSW signature */
                                    *(unsigned int *)(csw + 4) = tag;
                                    *(unsigned int *)(csw + 8) = 0; /* residue */
                                    csw[12] = 0; /* status = good */
                                    memcpy((unsigned char *)guest_mem + csw_addr, csw, 13);
                                }
                                tr_dequeue += 16;
                            }
                        }
                        (void)flags; (void)xfer_len;
                    }
                }
                tr_dequeue += 16;
            } else if (tt == 5) { /* ISOCH (isochronous transfer) */
                unsigned long long buf_addr = *(unsigned long long *)trb & ~0xFULL;
                int buf_len = *(unsigned int *)(trb + 8) & 0x1FFFF;
                /* UVC camera: write test pattern frame data */
                if (db == 3 && buf_addr > 0 && buf_addr + buf_len <= guest_mem_size) {
                    static unsigned char uvc_frame[UVC_FRAME_SIZE];
                    static int uvc_frame_ready = 0;
                    if (!uvc_frame_ready) {
                        uvc_generate_test_frame(uvc_frame);
                        uvc_frame_ready = 1;
                    }
                    int n = buf_len < UVC_FRAME_SIZE ? buf_len : UVC_FRAME_SIZE;
                    memcpy((unsigned char *)guest_mem + buf_addr, uvc_frame, n);
                }
                tr_dequeue += 16;
            } else {
                tr_dequeue += 16;
            }
        }
        /* Update endpoint context TR dequeue pointer */
        *(unsigned long long *)(ep_ctx + 8) = tr_dequeue | 1; /* cycle bit */
    }
}

static void xhci_init(void) {
    memset(&xhci, 0, sizeof(xhci));
    xhci.usbsts = 1;  /* HCH (halted) */
    xhci.portsc[0] = 1 | (4 << 10); /* CCS=1 (connected), speed=4 (SuperSpeed) — mass storage */
    xhci.portsc[1] = 1 | (3 << 10); /* CCS=1 (connected), speed=3 (HighSpeed) — HID keyboard */
    xhci.portsc[2] = 1 | (3 << 10); /* CCS=1 (connected), speed=3 (HighSpeed) — UVC camera */
    xhci_next_slot = 1;
}

static unsigned int xhci_read(unsigned long long offset) {
    if (offset < XHCI_CAP_LEN) {
        switch ((int)offset) {
        case 0:  return XHCI_CAP_LEN | (0x0100 << 16);  /* CAPLENGTH=32, HCIVERSION=1.0 */
        case 4:  return (XHCI_MAX_PORTS << 24) | XHCI_MAX_SLOTS;  /* HCSPARAMS1 */
        case 8:  return 0x0F;   /* HCSPARAMS2 */
        case 12: return 0;      /* HCSPARAMS3 */
        case 16: return 0x20;   /* HCCPARAMS1: 64-bit addressing */
        case 20: return 0x800;  /* DBOFF: doorbell array at offset 2048 */
        case 24: return 0x400;  /* RTSOFF: runtime regs at offset 1024 */
        default: return 0;
        }
    }
    unsigned long long op_off = offset - XHCI_CAP_LEN;
    if (op_off < 0x400) {
        switch ((int)op_off) {
        case 0:  return xhci.usbcmd;
        case 4:  return xhci.usbsts;
        case 8:  return 1;  /* PAGESIZE: 4KB */
        case 20: return xhci.dnctrl;
        case 24: return (unsigned int)(xhci.crcr & 0xFFFFFFFF);
        case 28: return (unsigned int)(xhci.crcr >> 32);
        case 48: return (unsigned int)(xhci.dcbaap & 0xFFFFFFFF);
        case 52: return (unsigned int)(xhci.dcbaap >> 32);
        case 56: return xhci.config;
        default: return 0;
        }
    }
    if (op_off >= 0x400 && op_off < 0x400 + XHCI_MAX_PORTS * 16) {
        int port = (int)(op_off - 0x400) / 16;
        int preg = (int)(op_off - 0x400) % 16;
        if (preg == 0) return xhci.portsc[port];
        return 0;
    }
    /* Runtime registers at offset 0x400 (RTSOFF) */
    /* Interrupter 0 at RTSOFF + 0x20 */
    if (offset >= 0x420 && offset < 0x440) {
        int ireg = (int)(offset - 0x420);
        if (ireg == 0) return 0; /* IMAN */
        if (ireg == 4) return 0; /* IMOD */
        if (ireg == 8) return xhci.er_size; /* ERSTSZ */
        if (ireg == 16) return (unsigned int)(xhci.erstba & 0xFFFFFFFF); /* ERSTBA lo */
        if (ireg == 20) return (unsigned int)(xhci.erstba >> 32); /* ERSTBA hi */
        if (ireg == 24) return (unsigned int)(xhci.er_addr + xhci.erdp_idx * 16); /* ERDP lo */
        if (ireg == 28) return 0; /* ERDP hi */
    }
    return 0;
}

static void xhci_write(unsigned long long offset, unsigned int val) {
    if (offset < XHCI_CAP_LEN) return;
    unsigned long long op_off = offset - XHCI_CAP_LEN;
    if (op_off < 0x400) {
        switch ((int)op_off) {
        case 0:
            if (val & 2) { xhci_init(); xhci.usbsts &= ~1; return; }
            xhci.usbcmd = val;
            if (val & 1) xhci.usbsts &= ~1; else xhci.usbsts |= 1;
            break;
        case 4:  xhci.usbsts &= ~val; break;
        case 20: xhci.dnctrl = val; break;
        case 24: xhci.crcr = (xhci.crcr & 0xFFFFFFFF00000000ULL) | val; break;
        case 28: xhci.crcr = (xhci.crcr & 0xFFFFFFFFULL) | ((unsigned long long)val << 32); break;
        case 48: xhci.dcbaap = (xhci.dcbaap & 0xFFFFFFFF00000000ULL) | val; break;
        case 52: xhci.dcbaap = (xhci.dcbaap & 0xFFFFFFFFULL) | ((unsigned long long)val << 32); break;
        case 56: xhci.config = val; break;
        }
        return;
    }
    if (op_off >= 0x400 && op_off < 0x400 + XHCI_MAX_PORTS * 16) {
        int port = (int)(op_off - 0x400) / 16;
        int preg = (int)(op_off - 0x400) % 16;
        if (preg == 0) xhci.portsc[port] = (xhci.portsc[port] & ~val & 0x00FE0002) | (val & 0x0F01FFFD);
        return;
    }
    /* Runtime / Interrupter 0 registers */
    if (offset >= 0x420 && offset < 0x440) {
        int ireg = (int)(offset - 0x420);
        if (ireg == 8) xhci.er_size = (unsigned short)val; /* ERSTSZ */
        if (ireg == 16) { /* ERSTBA lo */
            xhci.erstba = (xhci.erstba & 0xFFFFFFFF00000000ULL) | val;
            /* Read event ring segment table entry 0 to get ring address+size */
            if (xhci.erstba > 0 && xhci.erstba + 16 <= guest_mem_size) {
                unsigned char *erst = (unsigned char *)guest_mem + xhci.erstba;
                xhci.er_addr = *(unsigned long long *)erst;
                xhci.er_size = *(unsigned short *)(erst + 8);
                xhci.erdp_idx = 0;
                xhci.er_ccs = 1;
            }
        }
        if (ireg == 20) xhci.erstba = (xhci.erstba & 0xFFFFFFFFULL) | ((unsigned long long)val << 32);
        return;
    }
    if (offset >= 0x800 && offset < 0xC00) {
        int db = (int)(offset - 0x800) / 4;
        xhci_handle_doorbell(db, val);
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

static unsigned int hda_read(unsigned long long offset) {
    switch ((int)offset) {
    case 0x00: return 0x0001;  /* GCAP: 1 output stream, 0 input, 0 bidi */
    case 0x02: return 0x01;    /* VMIN=0, VMAJ=1 */
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
    default: return 0;
    }
}

/* ══ Audio Output (waveOut) ══ */
#define AUDIO_BUFS 4
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
        }
        unsigned int ioc = *(unsigned int *)(entry + 12);
        if (ioc & 1) hda.sd0sts |= 4; /* IOC flag set — raise BCIS */
    }
    if (hda_drain_idx > lvi) {
        hda_drain_idx = 0; /* wrap for continuous playback */
        hda.sd0lpib = 0;
    }
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
        if (val & 2) hda_drain_stream(); /* DMA run bit set — drain BDL to audio */
        break;
    case 0x83: hda.sd0sts &= ~(unsigned char)val; break;
    case 0x88: hda.sd0cbl = val; break;
    case 0x8C: hda.sd0lvi = (unsigned short)val; break;
    case 0x92: hda.sd0fmt = (unsigned short)val; break;
    case 0x98: hda.sd0bdpl = val; break;
    case 0x9C: hda.sd0bdpu = val; break;
    }
}

/* ══ HPET (High Precision Event Timer) ══ */
#define HPET_BAR      0xFED00000ULL
#define HPET_BAR_SIZE 0x1000

static struct {
    unsigned long long main_counter;
    unsigned int config;       /* general config: bit 0 = enable */
    unsigned int int_status;
    unsigned long long t0_config;
    unsigned long long t0_comparator;
} hpet;

static unsigned int hpet_read(unsigned long long offset) {
    switch ((int)offset) {
    case 0x00: return 0x8086A201;  /* GCAP_ID: rev=1, num_timers=1, 64-bit, vendor=Intel */
    case 0x04: return 0x0429B17F;  /* period = 69841279 femtoseconds (~14.318 MHz) */
    case 0x10: return hpet.config;
    case 0x20: return hpet.int_status;
    case 0xF0: { /* main counter low */
        LARGE_INTEGER pc;
        QueryPerformanceCounter(&pc);
        return (unsigned int)(pc.QuadPart & 0xFFFFFFFF);
    }
    case 0xF4: {
        LARGE_INTEGER pc;
        QueryPerformanceCounter(&pc);
        return (unsigned int)(pc.QuadPart >> 32);
    }
    case 0x100: return (unsigned int)(hpet.t0_config & 0xFFFFFFFF);
    case 0x104: return (unsigned int)(hpet.t0_config >> 32);
    case 0x108: return (unsigned int)(hpet.t0_comparator & 0xFFFFFFFF);
    case 0x10C: return (unsigned int)(hpet.t0_comparator >> 32);
    default: return 0;
    }
}

static void hpet_write(unsigned long long offset, unsigned int val) {
    switch ((int)offset) {
    case 0x10: hpet.config = val; break;
    case 0x20: hpet.int_status &= ~val; break; /* W1C */
    case 0x100: hpet.t0_config = (hpet.t0_config & 0xFFFFFFFF00000000ULL) | val; break;
    case 0x104: hpet.t0_config = (hpet.t0_config & 0xFFFFFFFFULL) | ((unsigned long long)val << 32); break;
    case 0x108: hpet.t0_comparator = (hpet.t0_comparator & 0xFFFFFFFF00000000ULL) | val; break;
    case 0x10C: hpet.t0_comparator = (hpet.t0_comparator & 0xFFFFFFFFULL) | ((unsigned long long)val << 32); break;
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
    if (offset == 0x10) { /* IOWIN — data register */
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

static unsigned int lapic_read(unsigned long long offset) {
    if (offset == 0x20) return lapic_state.id << 24;
    if (offset == 0x30) return 0x00050014;
    if (offset == 0xF0) return lapic_state.sivr;
    if (offset == 0x300) return lapic_state.icr_lo;
    if (offset == 0x310) return lapic_state.icr_hi;
    return 0;
}

static void ap_thread_func(void *arg);

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
            unsigned long long *entry_ptr = (unsigned long long *)((unsigned char *)guest_mem + 0x1000);
            unsigned long long ap_entry = *entry_ptr;
            unsigned long long *stack_table = (unsigned long long *)((unsigned char *)guest_mem + 0xF00);
            if (ap_entry == 0) {
                fprintf(stderr, "SMP: SIPI but no AP entry at 0x1000, ignoring\n");
                return;
            }
            for (int i = 1; i < SMP_MAX_CORES && i <= lapic_state.ap_count; i++) {
                if (lapic_state.ap_running[i]) continue;
                lapic_state.ap_running[i] = 1;
                HRESULT hr = WHvCreateVirtualProcessor(partition, i, 0);
                if (FAILED(hr)) {
                    fprintf(stderr, "WHvCreateVirtualProcessor(%d): 0x%lx\n", i, hr);
                    lapic_state.ap_running[i] = 0;
                    continue;
                }
                unsigned long long ap_stack = stack_table[i];
                if (ap_stack == 0) ap_stack = 0xC0000000ULL - (unsigned long long)i * 0x10000;
                WHV_REGISTER_NAME ap_names[] = {
                    WHvX64RegisterRip, WHvX64RegisterRsp, WHvX64RegisterRflags,
                    WHvX64RegisterCs, WHvX64RegisterDs, WHvX64RegisterEs,
                    WHvX64RegisterSs, WHvX64RegisterFs, WHvX64RegisterGs,
                    WHvX64RegisterCr0, WHvX64RegisterCr3, WHvX64RegisterCr4,
                    WHvX64RegisterEfer, WHvX64RegisterGdtr, WHvX64RegisterRdi
                };
                WHV_REGISTER_VALUE ap_vals[15];
                memset(ap_vals, 0, sizeof(ap_vals));
                ap_vals[0].Reg64 = ap_entry;
                ap_vals[1].Reg64 = ap_stack;
                ap_vals[2].Reg64 = 2;
                ap_vals[3].Segment.Selector = 0x08;
                ap_vals[3].Segment.Base = 0;
                ap_vals[3].Segment.Limit = 0xFFFFFFFF;
                ap_vals[3].Segment.Attributes = 0xA09B;
                for (int s = 4; s <= 9; s++) {
                    ap_vals[s].Segment.Selector = 0x10;
                    ap_vals[s].Segment.Base = 0;
                    ap_vals[s].Segment.Limit = 0xFFFFFFFF;
                    ap_vals[s].Segment.Attributes = 0xC093;
                }
                ap_vals[10].Reg64 = 0x80000011;
                ap_vals[11].Reg64 = 0x8000;
                ap_vals[12].Reg64 = 0x620;
                ap_vals[13].Table.Base = 0x100000 + 232;
                ap_vals[13].Table.Limit = 23;
                ap_vals[14].Reg64 = (unsigned long long)i;
                WHvSetVirtualProcessorRegisters(partition, i, ap_names, 15, ap_vals);
                fprintf(stderr, "SMP: AP %d started, entry=0x%llx stack=0x%llx\n",
                    i, ap_entry, ap_stack);
                lapic_state.ap_threads[i] = CreateThread(NULL, 0,
                    (LPTHREAD_START_ROUTINE)ap_thread_func,
                    (void*)(intptr_t)i, 0, NULL);
            }
        }
        return;
    }
}

static void ap_thread_func(void *arg) {
    int cpu_id = (int)(intptr_t)arg;
    WHV_RUN_VP_EXIT_CONTEXT ctx;
    fprintf(stderr, "SMP: AP %d thread started\n", cpu_id);
    for (;;) {
        HRESULT hr = WHvRunVirtualProcessor(partition, cpu_id, &ctx, sizeof(ctx));
        if (FAILED(hr)) {
            fprintf(stderr, "SMP: AP %d run failed: 0x%lx\n", cpu_id, hr);
            break;
        }
        switch (ctx.ExitReason) {
        case WHvRunVpExitReasonX64Halt:
            Sleep(100);
            break;
        case 0x1000: /* WHvRunVpExitReasonX64ApicInitSipiTrap — ignore, AP already running */
            /* SIPI ignored — AP already running */
            break;
        case WHvRunVpExitReasonMemoryAccess:
            if (ctx.MemoryAccess.Gpa >= LAPIC_BAR && ctx.MemoryAccess.Gpa < LAPIC_BAR + LAPIC_BAR_SIZE) {
                unsigned long long off = ctx.MemoryAccess.Gpa - LAPIC_BAR;
                WHV_REGISTER_NAME rn[2] = { WHvX64RegisterRax, WHvX64RegisterRip };
                WHV_REGISTER_VALUE rv[2];
                WHvGetVirtualProcessorRegisters(partition, cpu_id, rn, 2, rv);
                if (ctx.MemoryAccess.AccessInfo.AccessType == 0) rv[0].Reg64 = lapic_read(off);
                else lapic_write(off, (unsigned int)rv[0].Reg64);
                rv[1].Reg64 += ctx.VpContext.InstructionLength ? ctx.VpContext.InstructionLength : 2;
                WHvSetVirtualProcessorRegisters(partition, cpu_id, rn, 2, rv);
            }
            break;
        case WHvRunVpExitReasonX64IoPortAccess:
            {
                WHV_REGISTER_NAME rn = WHvX64RegisterRip;
                WHV_REGISTER_VALUE rv;
                WHvGetVirtualProcessorRegisters(partition, cpu_id, &rn, 1, &rv);
                rv.Reg64 += ctx.VpContext.InstructionLength ? ctx.VpContext.InstructionLength : 1;
                WHvSetVirtualProcessorRegisters(partition, cpu_id, &rn, 1, &rv);
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
    unsigned int madt_addr = (unsigned int)(ACPI_BASE + 0x400);
    unsigned int dsdt_addr = (unsigned int)(ACPI_BASE + 0x600);

    /* RSDP at ACPI_BASE (20 bytes for ACPI 1.0) */
    unsigned char *rsdp = base + ACPI_BASE;
    memset(rsdp, 0, 20);
    memcpy(rsdp, "RSD PTR ", 8);      /* signature */
    memcpy(rsdp + 9, "CODEX ", 6);    /* OEM ID */
    rsdp[15] = 0;                      /* revision = 0 (ACPI 1.0) */
    *(unsigned int *)(rsdp + 16) = rsdt_addr;
    rsdp[8] = acpi_checksum(rsdp, 20);

    /* RSDT at +0x100 (header + 2 pointers: FADT, MADT) */
    unsigned char *rsdt = base + rsdt_addr;
    int rsdt_len = 36 + 8; /* header(36) + 2 entries(8) */
    memset(rsdt, 0, rsdt_len);
    memcpy(rsdt, "RSDT", 4);
    *(unsigned int *)(rsdt + 4) = rsdt_len;
    rsdt[8] = 1; /* revision */
    memcpy(rsdt + 10, "CODEX ", 6);   /* OEM ID */
    memcpy(rsdt + 16, "CODEXVM ", 8); /* OEM table ID */
    *(unsigned int *)(rsdt + 24) = 1;  /* OEM revision */
    *(unsigned int *)(rsdt + 36) = fadt_addr;
    *(unsigned int *)(rsdt + 40) = madt_addr;
    rsdt[9] = acpi_checksum(rsdt, rsdt_len);

    /* FADT at +0x200 (116 bytes for ACPI 1.0) */
    unsigned char *fadt = base + fadt_addr;
    memset(fadt, 0, 116);
    memcpy(fadt, "FACP", 4);
    *(unsigned int *)(fadt + 4) = 116;
    fadt[8] = 1; /* revision */
    memcpy(fadt + 10, "CODEX ", 6);
    memcpy(fadt + 16, "CODEXVM ", 8);
    *(unsigned int *)(fadt + 36) = dsdt_addr;  /* DSDT address */
    fadt[45] = 1;                               /* preferred PM profile: desktop */
    *(unsigned short *)(fadt + 46) = 0x2000;   /* SCI interrupt */
    *(unsigned int *)(fadt + 48) = 0xB004;     /* SMI command port (Bochs compat) */
    fadt[52] = 0xF1;                            /* ACPI enable value */
    fadt[53] = 0xF0;                            /* ACPI disable value */
    *(unsigned int *)(fadt + 64) = 0x600;      /* PM1a event block */
    *(unsigned int *)(fadt + 68) = 0;          /* PM1b event block */
    *(unsigned int *)(fadt + 72) = 0x604;      /* PM1a control block */
    fadt[88] = 4; /* PM1 event length */
    fadt[89] = 2; /* PM1 control length */
    *(unsigned short *)(fadt + 109) = (1 << 10) | (1 << 5); /* boot flags: 8042, no VGA */
    fadt[9] = acpi_checksum(fadt, 116);

    /* Minimal DSDT at +0x600 (just a header, empty AML) */
    unsigned char *dsdt = base + dsdt_addr;
    memset(dsdt, 0, 36);
    memcpy(dsdt, "DSDT", 4);
    *(unsigned int *)(dsdt + 4) = 36;
    dsdt[8] = 1;
    memcpy(dsdt + 10, "CODEX ", 6);
    memcpy(dsdt + 16, "CODEXVM ", 8);
    dsdt[9] = acpi_checksum(dsdt, 36);

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

    fprintf(stderr, "ACPI: RSDP=0x%llx RSDT=0x%x FADT=0x%x MADT=0x%x DSDT=0x%x\n",
        ACPI_BASE, rsdt_addr, fadt_addr, madt_addr, dsdt_addr);
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
    memcpy(t+off, "Codex\0" "1.0\0" "05/23/2026\0", 22); off += 22;
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
    memcpy(t+off, "Codex Project\0" "Codex VM\0" "1.0\0", 28); off += 28;
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
    *(unsigned short*)(ep+28) = 4; /* number of structures */
    /* checksums */
    unsigned char sum = 0;
    for (int i = 16; i < 31; i++) sum += ep[i];
    ep[21] = (unsigned char)(0 - sum);
    sum = 0;
    for (int i = 0; i < 31; i++) sum += ep[i];
    ep[4] = (unsigned char)(0 - sum);

    fprintf(stderr, "SMBIOS: entry=0x%x tables=0x%x (%d bytes)\n", entry_addr, table_addr, off);
}

static int handle_device_mmio(WHV_RUN_VP_EXIT_CONTEXT *ctx) {
    unsigned long long gpa = ctx->MemoryAccess.Gpa;
    int access_type = ctx->MemoryAccess.AccessInfo.AccessType;
    int ilen = ctx->VpContext.InstructionLength;
    if (ilen == 0) ilen = 2;

    unsigned long long offset = 0;
    unsigned int (*read_fn)(unsigned long long) = NULL;
    void (*write_fn)(unsigned long long, unsigned int) = NULL;

    if (gpa >= XHCI_BAR && gpa < XHCI_BAR + XHCI_BAR_SIZE) {
        offset = gpa - XHCI_BAR;
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
        read_fn = lapic_read; write_fn = lapic_write;
    }

    if (read_fn) {
        WHV_REGISTER_NAME names[3] = { WHvX64RegisterRax, WHvX64RegisterRdx, WHvX64RegisterRip };
        WHV_REGISTER_VALUE vals[3];
        WHvGetVirtualProcessorRegisters(partition, 0, names, 3, vals);
        if (access_type == 0) {
            vals[0].Reg64 = read_fn(offset);
        } else {
            unsigned int wval = (gpa >= LAPIC_BAR && gpa < LAPIC_BAR + LAPIC_BAR_SIZE)
                ? (unsigned int)vals[0].Reg64 : (unsigned int)vals[1].Reg64;
            write_fn(offset, wval);
        }
        vals[2].Reg64 += ilen;
        WHvSetVirtualProcessorRegisters(partition, 0, names, 3, vals);
        return 1;
    }
    return 0;
}

/* GOP (Graphics Output Protocol) state */
#define GOP_FB_ADDR       0xBF000000ULL  /* guest physical address of framebuffer (3GB - 16MB, in RAM — fast writes, no MMIO trap) */
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
static int cmos_index = 0;        /* CMOS register selected via port 0x70 */
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

    /* LocateProtocol at BootServices+320 (0x140) = base+0x640 */
    W64(0x500 + 320, TRAP(UEFI_TRAP_BOOT_LOCATEPROTO));
    /* Fill extended BootServices range (offsets 0x100-0x17F) with stubs */
    for (int off = 0x100; off < 0x180; off += 8) {
        unsigned long long *slot = (unsigned long long *)(base + 0x500 + off);
        if (*slot == 0) *slot = TRAP(UEFI_TRAP_BOOT_STUB);
    }

    /* RuntimeServices.GetTime at offset +24 (after 24-byte header) */
    W64(0x400 + 24, TRAP(UEFI_TRAP_RT_GETTIME));

    /* GOP (Graphics Output Protocol) at 0xF0700 (moved from 0x600 to make room for BootServices) */
    W64(112, UEFI_TABLE_PAGE + 0x700);
    W64(0x700 + 0,   TRAP(UEFI_TRAP_GOP_QUERYMODE));
    W64(0x700 + 8,   TRAP(UEFI_TRAP_GOP_SETMODE));
    W64(0x700 + 16,  TRAP(UEFI_TRAP_GOP_BLT));
    W64(0x700 + 24,  UEFI_TABLE_PAGE + 0x780);
    /* GOP_MODE at 0xF0780 */
    *(int *)(base + 0x780) = 3;   /* MaxMode */
    *(int *)(base + 0x784) = 0;   /* Mode */
    W64(0x788, UEFI_TABLE_PAGE + 0x7C0);  /* Info pointer */
    W64(0x790, 36);               /* SizeOfInfo */
    W64(0x798, GOP_FB_ADDR);      /* FrameBufferBase */
    W64(0x7A0, GOP_FB_SIZE);      /* FrameBufferSize */
    /* GOP_MODE_INFO at 0xF07C0 */
    *(int *)(base + 0x7C0) = 0;   /* Version */
    *(int *)(base + 0x7C4) = 640; /* HorizontalResolution */
    *(int *)(base + 0x7C8) = 480; /* VerticalResolution */
    *(int *)(base + 0x7CC) = 1;   /* PixelFormat (BGR) */
    *(int *)(base + 0x7D4) = 640; /* PixelsPerScanLine */

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

    /* Device Path at 0xF0900 — EndEntire node */
    base[0x900] = 0x7F;  /* Type: End */
    base[0x901] = 0xFF;  /* SubType: EndEntire */
    base[0x902] = 4;     /* Length[0] */
    base[0x903] = 0;     /* Length[1] */

    /* Firmware Vendor string at 0xF0940 (UCS-2) — "Codex VM" */
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
           AMI Aptio V allocates top-down from conventional memory. */
        unsigned long long pages = r8;
        unsigned long long alloc_size = pages * 4096;
        if (rcx == 2) {
            /* AllocateAddress: caller set *R9 to exact address. Just succeed. */
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
        break;
    }
    case UEFI_TRAP_BOOT_FREE_PAGES:
    case UEFI_TRAP_BOOT_FREE_POOL:
        break; /* no-op */

    case UEFI_TRAP_BOOT_GET_MEMMAP: {
        /* GetMemoryMap — ASUS TUF (AMI Aptio V) compatible memory map
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
            unsigned long long map_key = 0x1234;
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
        /* AllocatePool(PoolType, Size, Buffer*) — RDX=size, R8=&buffer */
        unsigned long long size = rdx;
        unsigned long long addr = uefi_alloc_pool;
        uefi_alloc_pool = (uefi_alloc_pool + size + 4095) & ~4095ULL;
        if (r8 > 0 && r8 + 8 <= guest_mem_size) {
            memcpy((unsigned char *)guest_mem + r8, &addr, 8);
        }
        break;
    }
    case UEFI_TRAP_BOOT_EXIT_BOOTSVC:
    case UEFI_TRAP_BOOT_STALL: {
        /* Stall(Microseconds) — RCX = microseconds to pause.
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
        /* HandleProtocol(Handle, Protocol*, Interface**) — RCX=handle, RDX=&GUID, R8=&interface */
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
        /* LocateProtocol(Protocol*, Registration, Interface**) — RCX=&GUID, RDX=reg, R8=&interface */
        unsigned char guid[16];
        if (rcx > 0 && rcx + 16 <= guest_mem_size)
            memcpy(guid, (unsigned char *)guest_mem + rcx, 16);
        else { rax_result = EFI_NOT_FOUND_S; break; }
        unsigned long long iface = 0;
        if (memcmp(guid, GUID_BLOCK_IO, 16) == 0 && ide.data) {
            iface = UEFI_TABLE_PAGE + 0x800;
        } else if (memcmp(guid, GUID_SFS, 16) == 0) {
            /* Simple File System — not yet implemented */
        } else if (memcmp(guid, GUID_GOP, 16) == 0) {
            iface = UEFI_TABLE_PAGE + 0x700;
        } else if (memcmp(guid, GUID_LOADED_IMAGE, 16) == 0) {
            iface = UEFI_TABLE_PAGE + 0x880;
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
            rax_result = 7; /* EFI_DEVICE_ERROR */
            break;
        }
        if (buf_addr > 0 && buf_addr + buf_size <= guest_mem_size) {
            memcpy((unsigned char *)guest_mem + buf_addr, ide.data + disk_off, (size_t)buf_size);
        } else {
            rax_result = 2; /* EFI_INVALID_PARAMETER */
        }
        break;
    }
    case UEFI_TRAP_BLK_RESET:
    case UEFI_TRAP_BLK_WRITEBLOCKS:
    case UEFI_TRAP_BLK_FLUSH:
        break;

    case UEFI_TRAP_RT_GETTIME: {
        /* GetTime(Time*, Capabilities*) — RCX=&EFI_TIME, RDX=&caps (optional) */
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
        int mode_num = (int)rdx;
        int w = 640, h = 480;
        if (mode_num == 1) { w = 800; h = 600; }
        else if (mode_num == 2) { w = 1024; h = 768; }
        if (r8 > 0 && r8 + 8 <= guest_mem_size) {
            unsigned long long sz = 36;
            memcpy((unsigned char *)guest_mem + r8, &sz, 8);
        }
        if (arg_vals[3].Reg64 > 0 && arg_vals[3].Reg64 + 8 <= guest_mem_size) {
            unsigned long long info_addr = UEFI_TABLE_PAGE + 0x7C0;
            memcpy((unsigned char *)guest_mem + arg_vals[3].Reg64, &info_addr, 8);
        }
        unsigned char *info = (unsigned char *)guest_mem + UEFI_TABLE_PAGE + 0x7C0;
        *(int *)(info + 4) = w;
        *(int *)(info + 8) = h;
        *(int *)(info + 20) = w;
        break;
    }
    case UEFI_TRAP_GOP_SETMODE: {
        int mode_num = (int)rdx;
        gop_width = 640; gop_height = 480;
        if (mode_num == 1) { gop_width = 800; gop_height = 600; }
        else if (mode_num == 2) { gop_width = 1024; gop_height = 768; }
        gop_stride = gop_width;
        gop_active = 1;
        if (!gop_fb) gop_fb = (unsigned char *)calloc(1, GOP_FB_SIZE);
        unsigned char *gm = (unsigned char *)guest_mem + UEFI_TABLE_PAGE;
        *(int *)(gm + 0x784) = mode_num;
        *(int *)(gm + 0x7C4) = gop_width;
        *(int *)(gm + 0x7C8) = gop_height;
        *(int *)(gm + 0x7D4) = gop_stride;
        if (GOP_FB_ADDR + (unsigned long long)(gop_width * gop_height * 4) <= guest_mem_size) {
            memset((unsigned char *)guest_mem + GOP_FB_ADDR, 0, gop_width * gop_height * 4);
        }
        if (vga_hwnd) {
            RECT r = {0, 0, gop_width, gop_height};
            AdjustWindowRect(&r, WS_OVERLAPPEDWINDOW, FALSE);
            SetWindowPos(vga_hwnd, NULL, 0, 0, r.right - r.left, r.bottom - r.top, SWP_NOMOVE | SWP_NOZORDER);
            char title[64];
            sprintf(title, "Codex VM - %dx%d", gop_width, gop_height);
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

static const char *screenshot_path = NULL;
static int screenshot_delay_ms = 3000;
static int gpu_frame_count = 0;
static volatile int gpu_frame_ready = 0;
static int gpu_cine = 0;               /* cinematic mode: additive sparks + bloom + grade (opt-in via port 0x410) */
static long long gpu_cine_last = 0;    /* QPC of previous cine frame, for 60fps pacing */
static void gpu_cinematic_post(void);
static void gpu_cine_pace(void);
static void gpu_composite_band(unsigned int *fb, int w, int h, int y0, int y1);
static void sync_shadow_buffers(void);
static int gpu_last_tri_count = 0;
static float gpu_light[3] = {0, 0, -1};
static float gpu_eye[3] = {0, 0, -1};
static unsigned long long gpu_tex_guest_addr = 0;
static int gpu_tex_upload_w = 0, gpu_tex_upload_h = 0;
static unsigned long long asset_path_addr = 0;
static unsigned long long asset_dest_addr = 0;
static unsigned long long asset_last_size = 0;
static unsigned char *earth_tex_data = NULL;
static int earth_tex_w = 0, earth_tex_h = 0;

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
        /* GOP framebuffer mode — render from shadow (never guest_mem) */
        BITMAPINFO bmi;
        memset(&bmi, 0, sizeof(bmi));
        bmi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
        bmi.bmiHeader.biWidth = shadow_gop_stride;
        bmi.bmiHeader.biHeight = -shadow_gop_h;  /* top-down */
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = BI_RGB;
        StretchDIBits(hdc, 0, 0, shadow_gop_w, shadow_gop_h,
                      0, 0, shadow_gop_stride, shadow_gop_h,
                      shadow_gop, &bmi, DIB_RGB_COLORS, SRCCOPY);
    } else {
        /* Text mode — render from shadow VGA buffer (never guest_mem) */
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

static LRESULT CALLBACK vga_wndproc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    switch (msg) {
    case WM_PAINT:
        vga_paint(hwnd);
        return 0;
    case WM_TIMER:
        if (wp == VGA_TIMER_ID) {
            InvalidateRect(hwnd, NULL, FALSE);
            /* HDA drain moved to main loop — must not touch guest_mem here */
        }
        return 0;
    case WM_KEYDOWN: {
        unsigned char sc = vk_to_scancode((int)wp);
        if (sc) {
            kbd_enqueue(sc); kbd_irq_pending = 1;
            pending_kbd_scancode = (unsigned long long)sc;
            pending_kbd_valid = 1;
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
    case WM_MOUSEMOVE: {
        if (msg == WM_LBUTTONDOWN && !mouse_captured) {
            SetCapture(hwnd); mouse_captured = 1;
        }
        if (msg == WM_LBUTTONUP && mouse_captured) {
            ReleaseCapture(); mouse_captured = 0;
        }
        pending_mouse_abs_x = (short)LOWORD(lp);
        pending_mouse_abs_y = (short)HIWORD(lp);
        pending_mouse_btn = 0;
        if (wp & MK_LBUTTON) pending_mouse_btn |= 1;
        if (wp & MK_RBUTTON) pending_mouse_btn |= 2;
        if (wp & MK_MBUTTON) pending_mouse_btn |= 4;
        pending_mouse_valid = 1;
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
#define INPUT_BUF_MAX         0x1000000  /* 16 MB */
#define GUEST_RING_SIZE       0x100000  /* 1 MB — must match seed's serial-ring-buf-size */
#define GUEST_RING_MASK       0x0FFFFF

/* Drip-feed state: host-side overflow buffer for input > GUEST_RING_SIZE */
static unsigned char *input_overflow = NULL;
static size_t input_overflow_len = 0;
static size_t input_overflow_pos = 0;
static unsigned long long input_total_written = 0;
#define OUTPUT_RING_ADDR      0x700000
#define OUTPUT_RING_SIZE      0x200000  /* 2 MB */
#define OUTPUT_RING_MASK      0x1FFFFF
#define OUTPUT_WRITE_POS_ADDR 36152
#define DOORBELL_PORT         0x510
#define DOORBELL_DATA_READY   0x01
#define DOORBELL_COMPILE_DONE 0x02
#define DOORBELL_FATAL        0xFF

/* IDE state — typedef is above (forward-declared for UEFI emulation) */

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

static int ne2k_out_count = 0;
static void ne2k_handle_out(int port, int val, int io_size) {
    int off = port - NE2K_BASE;
    ne2k_out_count++;
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
        /* DATA port read — remote DMA read from NIC memory, wrapping at ring boundary */
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
typedef struct {
    int active;
    SOCKET listen_sock;
    unsigned short host_port;
    unsigned short guest_port;
} PortFwd;
static PortFwd portfwds[PORTFWD_MAX];
static int portfwd_count = 0;

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
    int state;  /* 0=unused, 1=connecting, 2=established, 3=fin_wait, 4=half-closed */
    int forwarded; /* 1 = inbound port-forwarded connection */
    unsigned long guest_ack;  /* last ACK from guest (for forwarded conns) */
} NatConn;

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

static NatConn *nat_alloc(void) {
    for (int i = 0; i < NAT_MAX_CONN; i++)
        if (!nat_conns[i].active) { memset(&nat_conns[i], 0, sizeof(NatConn)); return &nat_conns[i]; }
    return NULL;
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

        if ((flags & 0x12) == 0x12) {
            /* SYN+ACK from guest — check if this is a forwarded connection */
            NatConn *c = nat_find(sport, dport, dst_ip);
            if (c && c->forwarded && c->state == 1) {
                portfwd_handle_synack(c, seq, ack);
            }
        }
        else if (flags & 0x02) {
            /* SYN — new outbound connection */
            fprintf(stderr, "NAT: SYN from guest %d.%d.%d.%d:%d -> %d.%d.%d.%d:%d\n",
                src_ip[0], src_ip[1], src_ip[2], src_ip[3], sport,
                dst_ip[0], dst_ip[1], dst_ip[2], dst_ip[3], dport);
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
            c->forwarded = 0;

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

            /* Send SYN-ACK back to guest */
            nat_build_tcp_frame(ne2k.par, dst_ip, guest_ip,
                                dport, sport,
                                c->ack_offset, seq + 1,
                                0x12, /* SYN+ACK */
                                NULL, 0);
            c->state = 2;
        }
        else if ((flags & 0x01) && !(flags & 0x02)) {
            /* FIN — guest is done sending. Gracefully half-close the host
               socket so buffered data drains to the peer before the
               connection tears down. Full cleanup at VM exit. */
            NatConn *c = nat_find(sport, dport, dst_ip);
            if (c) {
                nat_build_tcp_frame(ne2k.par, dst_ip, guest_ip,
                                    dport, sport,
                                    ack, seq + 1,
                                    0x11, /* FIN+ACK */
                                    NULL, 0);
                shutdown(c->sock, SD_SEND);
                if (c->state == 4) { closesocket(c->sock); c->active = 0; }
                else c->state = 3;
            }
        }
        else if (flags & 0x10) {
            /* ACK (possibly with data) */
            NatConn *c = nat_find(sport, dport, dst_ip);
            if (c && payload_len > 0 && (c->state == 2 || c->state == 4)) {
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
        if (!c->active || (c->state != 2 && c->state != 4)) continue;
        if (rx_queue_count >= RX_QUEUE_SIZE - 1) { continue; }
        unsigned char buf[1400];
        int n = recv(c->sock, (char*)buf, sizeof(buf), 0);
        if (n < 0) {
            static int neg_count = 0;
            if (neg_count++ < 5) fprintf(stderr, "NAT recv: n=%d err=%d state=%d sock=%lld\n", n, WSAGetLastError(), c->state, (long long)c->sock);
        }
        if (n > 0) {
            static int nat_rx_total = 0;
            nat_rx_total += n;
            if (nat_rx_total % 100000 < n) fprintf(stderr, "NAT RX: total=%d chunk=%d q=%d\n", nat_rx_total, n, rx_queue_count);
            c->ack_offset++;
            nat_build_tcp_frame(ne2k.par, c->dst_ip, guest_ip,
                                c->dst_port, c->guest_port,
                                c->ack_offset, c->seq_offset + 1,
                                0x10, /* ACK with data */
                                buf, n);
            c->ack_offset += n - 1;
        } else if (n == 0) {
            /* Remote half-closed (sent FIN). Deliver FIN to guest.
               Move to state 4: stop reading but keep forwarding
               guest TX data back to the host socket. */
            nat_build_tcp_frame(ne2k.par, c->dst_ip, guest_ip,
                                c->dst_port, c->guest_port,
                                c->ack_offset + 1, c->seq_offset + 1,
                                0x11, /* FIN+ACK */
                                NULL, 0);
            c->state = 4;
        }
    }
}

/* Set up port forwarding listeners */
static void portfwd_init(void) {
    for (int i = 0; i < portfwd_count; i++) {
        PortFwd *pf = &portfwds[i];
        pf->listen_sock = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
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
        listen(pf->listen_sock, 4);
        u_long nb = 1;
        ioctlsocket(pf->listen_sock, FIONBIO, &nb);
        pf->active = 1;
        fprintf(stderr, "portfwd: host:%d -> guest:%d\n", pf->host_port, pf->guest_port);
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
        c->seq_offset = 100000;
        c->ack_offset = 0;
        c->state = 1;
        c->forwarded = 1;
        c->guest_ack = 0;
        fprintf(stderr, "portfwd: accepted host client on :%d -> guest:%d\n",
            pf->host_port, pf->guest_port);
        nat_build_tcp_frame(ne2k.par, gw_ip, guest_ip,
                            c->dst_port, pf->guest_port,
                            c->seq_offset, 0,
                            0x02, /* SYN */
                            NULL, 0);
    }
}

/* Handle SYN-ACK from guest for a forwarded connection */
static void portfwd_handle_synack(NatConn *c, unsigned long seq, unsigned long ack) {
    unsigned char guest_ip[4] = {NAT_GUEST_IP0, NAT_GUEST_IP1, NAT_GUEST_IP2, NAT_GUEST_IP3};
    unsigned char gw_ip[4] = {NAT_GW_IP0, NAT_GW_IP1, NAT_GW_IP2, NAT_GW_IP3};
    c->ack_offset = seq + 1;
    c->seq_offset = ack;
    c->state = 2;
    c->guest_ack = seq + 1;
    nat_build_tcp_frame(ne2k.par, gw_ip, guest_ip,
                        c->dst_port, c->guest_port,
                        c->seq_offset, c->ack_offset,
                        0x10, /* ACK */
                        NULL, 0);
    fprintf(stderr, "portfwd: connection established (guest port %d)\n", c->guest_port);
}

/* Inject queued RX frames into the NE2000 ring buffer */
static void ne2k_inject_rx(void) {
    static int inject_debug = 0;
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
        ne2k.isr |= 0x01; /* PRX — packet received */

        rx_queue_head = (rx_queue_head + 1) % RX_QUEUE_SIZE;
        rx_queue_count--;
    }
}


/* VGA Attribute Controller — minimal emulation for port 0x3C0/0x3C1 */
static int vga_attr_index = 0;    /* current attribute register index */
static int vga_attr_flipflop = 0; /* 0=next write is index, 1=next write is data */

/* ide declared above (forward decl for UEFI emulation) */
static PicState pic_master, pic_slave;
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

static void dbg_dump_mem(unsigned long long addr, int len) {
    if (addr + (unsigned long long)len > guest_mem_size) {
        fprintf(stderr, "  address out of range\n"); return;
    }
    unsigned char *p = (unsigned char *)guest_mem + addr;
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
    for (int i = 0; i < count && rsp + i*8 + 8 <= guest_mem_size; i++) {
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
    for (int depth = 1; depth < 32 && rbp > 0 && rbp + 16 <= guest_mem_size; depth++) {
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
    if (addr >= guest_mem_size || addr < 16) return;
    unsigned char *base = (unsigned char *)guest_mem + addr;
    int max_bytes = (int)(guest_mem_size - addr);
    if (max_bytes > 256) max_bytes = 256;
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
        fprintf(stderr, "(no symbols — pass -map <file.map> for resolved names)\n");

    if (!vga_headless) {
        fprintf(stderr, "\nEntering debugger. Type 'help' for commands, 'q' to quit.\n");
        dbg_command_loop(99, dbg_read_reg(0));
    }
}

static int dbg_set_breakpoint(unsigned long long addr, int cond_reg, unsigned long long cond_val) {
    if (addr >= guest_mem_size) { fprintf(stderr, "  address out of range\n"); return -1; }
    if (bp_count >= MAX_BREAKPOINTS) { fprintf(stderr, "  too many breakpoints\n"); return -1; }
    int idx = bp_count++;
    breakpoints[idx].addr = addr;
    breakpoints[idx].orig_byte = *((unsigned char *)guest_mem + addr);
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

static int dbg_reg_index(const char *name) {
    const char *names[] = {"rip","rsp","rax","rbx","rcx","rdx","rsi","rdi","rbp",
                           "r8","r9","r10","r11","r12","r13","r14","r15","rflags"};
    for (int i = 0; i < 18; i++) if (!strcmp(name, names[i])) return i;
    return -1;
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
                    /* Condition not met — resume via single-step past this address */
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
            /* x <addr> — read 8-byte qword at addr */
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
                "  s / step          — single-step one instruction\n"
                "  c / continue      — resume execution\n"
                "  r / regs          — dump registers\n"
                "  m <addr> [len]    — dump memory (hex+ascii)\n"
                "  x <addr>          — read qword at address\n"
                "  d <fn|addr> [n]   — disassemble n instructions at address\n"
                "  di                — disassemble 16 instructions at RIP\n"
                "  bt / backtrace    — walk RBP chain\n"
                "  stack             — dump 16 stack slots\n"
                "  b <fn|addr> [if reg=val] — set breakpoint (conditional)\n"
                "  w <addr> [size]   — set memory watchpoint\n"
                "  sym <name>        — look up symbol address\n"
                "  q / quit          — exit VM\n");
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
        if (partition) WHvCancelRunVirtualProcessor(partition, 0, 0);
    }
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
   and the drip-feed (which runs on exit) never fires — deadlock. */
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

/* ── Serial ────────────────────────────────────────────────────────── */

/* Memory-mapped I/O: load input file directly into the guest's serial ring
   buffer at 0x500000. Set write-pos so the guest reads it immediately.
   The guest compiler's __bare_metal_read_serial polls the ring buffer —
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

static void output_buf_init(void) {
    output_cap = 16 * 1024 * 1024;  /* 16MB */
    output_buf = (unsigned char *)malloc(output_cap);
    output_len = 0;
}

/* Debug: detect "!EXC=03" in serial stream */
static char exc_detect_buf[8];
static int exc_detect_pos = 0;
static int dbg_exc_pending = 0;

static void output_buf_write(unsigned char b) {
    if (!output_buf) return;
    if (output_len < output_cap) output_buf[output_len++] = b;

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

static void dump_output_file(const char *path) {
    if (!path || !output_buf || output_len == 0) return;
    FILE *f = fopen(path, "wb");
    if (!f) { fprintf(stderr, "ERROR: cannot write output %s\n", path); return; }
    fwrite(output_buf, 1, output_len, f);
    fclose(f);
    fprintf(stderr, "Output: %zu bytes -> %s\n", output_len, path);
}

static size_t output_ring_drained = 0;

static void drain_guest_output(void) {
    if (!guest_mem || !output_buf) return;
    if (OUTPUT_WRITE_POS_ADDR + 8 > guest_mem_size) return;
    if (OUTPUT_RING_ADDR + OUTPUT_RING_SIZE > guest_mem_size) return;
    unsigned long long wpos = *(unsigned long long *)((unsigned char *)guest_mem + OUTPUT_WRITE_POS_ADDR);
    unsigned char *ring = (unsigned char *)guest_mem + OUTPUT_RING_ADDR;
    while (output_ring_drained < wpos) {
        output_buf_write(ring[output_ring_drained & OUTPUT_RING_MASK]);
        output_ring_drained++;
    }
}

/* ── IDE ───────────────────────────────────────────────────────────── */

static void ide_init(IdeState *d, const char *path) {
    memset(d, 0, sizeof(*d));
    d->status = 0x50; /* DRDY */
    if (!path) return;
    d->path = path;
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

/* Persist a written region back to the disk image file (durability). */
static void ide_flush(IdeState *d, size_t off, size_t len) {
    if (!d->path || !d->data || off + len > d->size) return;
    FILE *f = fopen(d->path, "r+b");
    if (!f) { fprintf(stderr, "WARN: cannot reopen disk %s for write\n", d->path); return; }
    fseek(f, (long)off, SEEK_SET);
    fwrite(d->data + off, 1, len, f);
    fclose(f);
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

static void ide_start_write(IdeState *d) {
    unsigned int lba = ide_get_lba(d);
    int count = d->sect_count ? d->sect_count : 256;
    if ((size_t)lba * 512 >= d->size) { d->status = 0x51; d->error = 0x10; return; }
    d->buf_off = (size_t)lba * 512;
    d->buf_remaining = 512;
    d->sectors_left = count - 1;
    d->writing = 1;
    d->status = 0x58; /* DRDY|DRQ — ready to accept data */
    d->error = 0;
}

/* Accept one 16-bit word during a WRITE SECTORS transfer (REP OUTSW). Stores
   into the in-memory disk and flushes each completed sector to the image. */
static void ide_write_data(IdeState *d, int val) {
    if (!d->writing || d->buf_remaining <= 0) return;
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
    else if (reg == 7) {
        if (val == 0x20) ide_start_read(d);
        else if (val == 0x30) ide_start_write(d);
        else { d->writing = 0; d->status = 0x50; } /* flush (0xE7/0xEA) and others -> DRDY */
    }
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
            /* serial_irq_pending removed — no serial */
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
    guest_mem_size = mem_mb * 1024ULL * 1024ULL;
    if (guest_mem_size > MAX_MEM) guest_mem_size = MAX_MEM;

    whp_lock();
    hr = WHvCreatePartition(&partition);
    if (FAILED(hr)) { fprintf(stderr, "WHvCreatePartition: 0x%lx\n", hr); exit(1); }

    WHV_PARTITION_PROPERTY prop;
    memset(&prop, 0, sizeof(prop));
    prop.ProcessorCount = smp_cores > 1 ? (smp_cores > SMP_MAX_CORES ? SMP_MAX_CORES : smp_cores) : 1;
    WHvSetPartitionProperty(partition, WHvPartitionPropertyCodeProcessorCount, &prop, sizeof(prop));

    /* Enable I/O port and CPUID exits. MSR exits handled selectively. */
    memset(&prop, 0, sizeof(prop));
    prop.ExtendedVmExits.X64CpuidExit = 1;
    prop.ExtendedVmExits.X64MsrExit = 1;
    WHvSetPartitionProperty(partition, WHvPartitionPropertyCodeExtendedVmExits, &prop, sizeof(prop));

    hr = WHvSetupPartition(partition);
    if (FAILED(hr)) { fprintf(stderr, "WHvSetupPartition: 0x%lx\n", hr); exit(1); }

    /* Enable exception exit for debug/breakpoint vectors (must be after setup) */
    memset(&prop, 0, sizeof(prop));
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
        size_t fb_bytes = (size_t)gop_width * gop_height * 4;
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
        /* ELF32 multiboot kernel — load PT_LOAD segments at their vaddrs */
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
        memcpy((unsigned char*)guest_mem + LOAD_ADDR, buf + skip, payload);
    }

    /* Auto-extract PE from GPT disk image: find FAT16 ESP, locate BOOTX64.EFI */
    if (uefi_mode && sz > 1024 && buf[0] != 'M' &&
        buf[512] == 'E' && buf[513] == 'F' && buf[514] == 'I' && buf[515] == ' ') {
        unsigned long long part_lba = *(unsigned long long*)(buf + 1024 + 32);
        unsigned long long part_start = part_lba * 512;
        if (part_start + 512 < sz) {
            /* Parse FAT16 BPB */
            unsigned char *bpb = buf + part_start;
            unsigned int bps = *(unsigned short*)(bpb + 11);
            unsigned int spc = bpb[13];
            unsigned int reserved = *(unsigned short*)(bpb + 14);
            unsigned int nfats = bpb[16];
            unsigned int root_entries = *(unsigned short*)(bpb + 17);
            unsigned int spf = *(unsigned short*)(bpb + 22);
            unsigned long long root_off = part_start + (unsigned long long)(reserved + nfats * spf) * bps;
            unsigned long long data_off = root_off + (unsigned long long)root_entries * 32;
            /* Scan root directory for EFI subdir */
            unsigned int efi_cluster = 0;
            for (int i = 0; i < (int)root_entries && root_off + (i+1)*32 <= sz; i++) {
                unsigned char *e = buf + root_off + i * 32;
                if (e[0] == 0) break;
                if (e[11] == 0x10 && memcmp(e, "EFI        ", 11) == 0) {
                    efi_cluster = *(unsigned short*)(e + 26);
                    break;
                }
            }
            /* Scan EFI subdir for BOOT subdir */
            unsigned int boot_cluster = 0;
            if (efi_cluster >= 2) {
                unsigned long long dir_off = data_off + (unsigned long long)(efi_cluster - 2) * spc * bps;
                for (int i = 0; i < (int)(spc * bps / 32) && dir_off + (i+1)*32 <= sz; i++) {
                    unsigned char *e = buf + dir_off + i * 32;
                    if (e[0] == 0) break;
                    if (e[11] == 0x10 && memcmp(e, "BOOT       ", 11) == 0) {
                        boot_cluster = *(unsigned short*)(e + 26);
                        break;
                    }
                }
            }
            /* Scan BOOT subdir for BOOTX64.EFI */
            if (boot_cluster >= 2) {
                unsigned long long dir_off = data_off + (unsigned long long)(boot_cluster - 2) * spc * bps;
                for (int i = 0; i < (int)(spc * bps / 32) && dir_off + (i+1)*32 <= sz; i++) {
                    unsigned char *e = buf + dir_off + i * 32;
                    if (e[0] == 0) break;
                    if (memcmp(e, "BOOTX64 EFI", 11) == 0) {
                        unsigned int file_cluster = *(unsigned short*)(e + 26);
                        unsigned int file_size = *(unsigned int*)(e + 28);
                        unsigned long long file_off = data_off + (unsigned long long)(file_cluster - 2) * spc * bps;
                        if (file_off + file_size <= sz && file_size > 64) {
                            fprintf(stderr, "GPT: extracted BOOTX64.EFI (%u bytes) from partition at LBA %llu\n",
                                file_size, part_lba);
                            /* Replace buf with the extracted PE */
                            unsigned char *pe = malloc(file_size);
                            memcpy(pe, buf + file_off, file_size);
                            free(buf);
                            buf = pe;
                            sz = file_size;
                            memcpy((unsigned char*)guest_mem + LOAD_ADDR, buf, sz);
                        }
                        break;
                    }
                }
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

    /* Parse multiboot header for entry point — scan loaded memory at LOAD_ADDR */
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

/* ── I/O dispatch ──────────────────────────────────────────────────── */

static void handle_io(WHV_RUN_VP_EXIT_CONTEXT *ctx) {
    int port = ctx->IoPortAccess.PortNumber;
    int is_out = (ctx->IoPortAccess.AccessInfo.IsWrite != 0);
    int size = ctx->IoPortAccess.AccessInfo.AccessSize;
    int val = 0;
    if (is_out) val = (int)ctx->IoPortAccess.Rax;
    if (smp_cores > 1 && is_out && port >= 0x510) fprintf(stderr, "IO-HI[0x%x]=0x%x\n", port, val);

    if (is_out) {
        /* REP OUTSW to the IDE data port: a WRITE SECTORS data phase. Read each
           word from guest [RSI], feed the disk, manage RSI/RCX/RIP per iteration. */
        if (ctx->IoPortAccess.AccessInfo.StringOp && port == 0x1F0) {
            unsigned long long gpa = ctx->IoPortAccess.Rsi;
            unsigned char *gmem = (unsigned char *)guest_mem;
            int wval = 0;
            if (gpa + (unsigned long long)size <= guest_mem_size) {
                if (size == 1) wval = gmem[gpa];
                else if (size == 2) wval = gmem[gpa] | (gmem[gpa + 1] << 8);
                else wval = (int)(*(unsigned int *)(gmem + gpa));
            }
            ide_write_data(&ide, wval);
            WHV_REGISTER_NAME sn[] = { WHvX64RegisterRsi, WHvX64RegisterRcx };
            WHV_REGISTER_VALUE sv[2];
            sv[0].Reg64 = ctx->IoPortAccess.Rsi + size;
            sv[1].Reg64 = ctx->IoPortAccess.Rcx - 1;
            WHvSetVirtualProcessorRegisters(partition, 0, sn, 2, sv);
            if (ctx->IoPortAccess.Rcx <= 1) {
                WHV_REGISTER_NAME rn = WHvX64RegisterRip;
                WHV_REGISTER_VALUE rv;
                rv.Reg64 = ctx->VpContext.Rip + ctx->VpContext.InstructionLength;
                WHvSetVirtualProcessorRegisters(partition, 0, &rn, 1, &rv);
            }
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
            if (channel < 3) {
                pit_access[channel] = (val >> 4) & 3;
                pit_mode[channel] = (val >> 1) & 7;
                if (channel == 2) speaker_freq_latch = 0; /* reset latch on mode write */
            }
        }
        else if (port >= 0x40 && port <= 0x42) {
            /* Channel data writes — channel 2 handled by speaker */
        }
        /* IDE */
        else if ((port >= 0x1F0 && port <= 0x1F7) || port == 0x3F6) {
            ide_handle_out(&ide, port, val);
        }
        /* Doorbell — guest signals output ring buffer status */
        else if (port == DOORBELL_PORT) {
            drain_guest_output();
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
            /* write to CMOS — ignore */
        }
        /* Guest sleep request: out 0xE0, ms — yields to host for N ms */
        else if (port == 0xE0) {
            DWORD ms = (val > 0 && val <= 1000) ? (DWORD)val : 1;
            Sleep(ms);
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
                gop_width = w; gop_height = h; gop_stride = w;
                gop_active = 1; vbe_active = 1;
                if (!gop_fb) gop_fb = (unsigned char *)calloc(1, GOP_FB_SIZE);
                fprintf(stderr, "VBE: mode set %dx%d fb=0x%llx\n", w, h, (unsigned long long)VBE_FB_ADDR);
            }
        }
        /* GPU triangle rasterizer commands */
        else if (port == 0x400) {
            gpu_rasterize_triangles(val);
            if (gpu_cine) { gpu_cinematic_post(); sync_shadow_buffers(); gpu_cine_pace(); }
            else gpu_atmosphere_glow();
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
        else if (port == 0x402) {
            gpu_clear_depth();
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
            gpu_tex_guest_addr = (unsigned long long)val;
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
                unsigned long long sz = (unsigned long long)gpu_tex_upload_w * gpu_tex_upload_h * 3;
                if (gpu_tex_guest_addr + sz <= guest_mem_size) {
                    unsigned char *src = (unsigned char *)guest_mem + gpu_tex_guest_addr;
                    if (earth_tex_data) free(earth_tex_data);
                    earth_tex_data = (unsigned char *)malloc(sz);
                    if (earth_tex_data) {
                        memcpy(earth_tex_data, src, sz);
                        earth_tex_w = gpu_tex_upload_w;
                        earth_tex_h = gpu_tex_upload_h;
                        fprintf(stderr, "GPU texture uploaded from guest 0x%llx (%dx%d)\n",
                                gpu_tex_guest_addr, earth_tex_w, earth_tex_h);
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
        else if (port == 0x40E) {
            /* Execute asset load: read file from host into guest RAM */
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
                    if (asset_dest_addr + (unsigned long long)sz <= guest_mem_size) {
                        fread((unsigned char *)guest_mem + asset_dest_addr, 1, sz, fp);
                        asset_last_size = (unsigned long long)sz;
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
                int dev = (pci_config_addr >> 11) & 0x1F;
                int func = (pci_config_addr >> 8) & 0x7;
                int off = pci_config_addr & 0xFC;
                pci_write_config(dev, func, off, (unsigned int)val);
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
        /* CMOS RTC (0x71 read — return BCD time from host clock) */
        else if (port == 0x71) {
            SYSTEMTIME st;
            GetLocalTime(&st);
            switch (cmos_index) {
            case 0:  result = ((st.wSecond / 10) << 4) | (st.wSecond % 10); break;
            case 2:  result = ((st.wMinute / 10) << 4) | (st.wMinute % 10); break;
            case 4:  result = ((st.wHour / 10) << 4) | (st.wHour % 10); break;
            case 6:  result = st.wDayOfWeek + 1; break;
            case 7:  result = ((st.wDay / 10) << 4) | (st.wDay % 10); break;
            case 8:  result = ((st.wMonth / 10) << 4) | (st.wMonth % 10); break;
            case 9:  result = (((st.wYear % 100) / 10) << 4) | (st.wYear % 10); break;
            case 10: result = 0x26; break; /* Status A: update not in progress */
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
        else if (port == 0x40E) {
            result = (unsigned int)(asset_last_size & 0xFFFFFFFF);
        }
        else if (port == 0x40F) {
            result = (unsigned int)((asset_last_size >> 32) & 0xFFFFFFFF);
        }
        /* PCI Configuration Space */
        else if (port >= 0xCFC && port <= 0xCFF) {
            if (pci_config_addr & 0x80000000) {
                int dev = (pci_config_addr >> 11) & 0x1F;
                int func = (pci_config_addr >> 8) & 0x7;
                int off = pci_config_addr & 0xFC;
                unsigned int val32 = pci_read_config(dev, func, off);
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
           Port 0xE1: buttons. 0xE2: accumulated dx (signed, clamped to byte).
           0xE3: accumulated dy (signed, clamped to byte). 0xE4: availability + reset accumulators. */
        else if (port == 0xE1) { result = pending_mouse_btn; }
        else if (port == 0xE2) { result = pending_mouse_abs_x & 0xFFFF; }
        else if (port == 0xE3) { result = pending_mouse_abs_y & 0xFFFF; pending_mouse_valid = 0; }
        else if (port == 0xE4) { result = pending_mouse_valid; }

        if (ctx->IoPortAccess.AccessInfo.StringOp) {
            /* REP INSW/INSB: write to guest memory at [RDI], update RDI and RCX */
            unsigned long long gpa = ctx->IoPortAccess.Rdi;
            unsigned char *gmem = (unsigned char *)guest_mem;
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
            else rax_val.Reg64 = result;
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
    return 0;  /* serial removed — memory-mapped I/O, no IRQ needed */
}

/* (monitor thread removed — page protection provides precise trapping) */

/* ── GPU triangle rasterizer (host-side, native speed) ──────────────── */
/*
 * Command buffer at GPU_CMD_ADDR in guest RAM.  Each triangle is 8 ints:
 *   [0] x0  [1] y0  [2] x1  [3] y1  [4] x2  [5] y2  [6] color  [7] depth
 * Port 0x400 OUT: value = triangle count, rasterize all triangles
 * Port 0x401 OUT: value = XRGB color, clear framebuffer
 * Port 0x402 OUT: value = 0, clear depth buffer
 * Port 0x403 IN:  returns 1 (GPU present capability check)
 */
#define GPU_CMD_ADDR   0xBE000000ULL
#define GPU_CMD_SIZE   (16384 * 72)
#define GPU_DEPTH_ADDR 0xBE800000ULL
#define GPU_DEPTH_FAR  999999

static void gpu_clear_fb(int color) {
    gpu_frame_ready = 0;
    if (!gop_active || GOP_FB_ADDR + (unsigned long long)gop_width * gop_height * 4 > guest_mem_size) return;
    unsigned int *fb = (unsigned int *)((unsigned char *)guest_mem + GOP_FB_ADDR);
    int total = gop_width * gop_height;
    for (int i = 0; i < total; i++) fb[i] = (unsigned int)color;
}

/* Persistence clear: blend the frame ~7/8 toward the target color instead
   of wiping it, so moving sparks leave fading long-exposure trails. */
static void gpu_fade_clear(int color) {
    gpu_frame_ready = 0;
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
    if (GPU_DEPTH_ADDR + (unsigned long long)gop_width * gop_height * 4 > guest_mem_size) return;
    unsigned int *db = (unsigned int *)((unsigned char *)guest_mem + GPU_DEPTH_ADDR);
    int total = gop_width * gop_height;
    for (int i = 0; i < total; i++) db[i] = GPU_DEPTH_FAR;
}

static inline int gpu_edge(int ax, int ay, int bx, int by, int px, int py) {
    return (bx - ax) * (py - ay) - (by - ay) * (px - ax);
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

static unsigned int gpu_sample_earth(int u1000, int v1000) {
    if (earth_tex_data) return gpu_sample_texture(u1000, v1000);
    float lon = -180.0f + (u1000 / 1000.0f) * 360.0f;
    float lat = 90.0f - (v1000 / 1000.0f) * 180.0f;
    return earth_texel(lat, lon);
}
static inline unsigned int gpu_lerp_color(unsigned int c0, unsigned int c1, unsigned int c2,
                                           int w0, int w1, int w2, int area) {
    int r0 = (c0 >> 16) & 0xFF, g0 = (c0 >> 8) & 0xFF, b0 = c0 & 0xFF;
    int r1 = (c1 >> 16) & 0xFF, g1 = (c1 >> 8) & 0xFF, b1 = c1 & 0xFF;
    int r2 = (c2 >> 16) & 0xFF, g2 = (c2 >> 8) & 0xFF, b2 = c2 & 0xFF;
    int r = (r0 * w0 + r1 * w1 + r2 * w2) / area;
    int g = (g0 * w0 + g1 * w1 + g2 * w2) / area;
    int b = (b0 * w0 + b1 * w1 + b2 * w2) / area;
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
    for (int t = 0; t < count && t < 16384; t++) {
        int *tri = (int *)(cmd + t * 72);
        int x0 = tri[0], y0 = tri[1], x1 = tri[2], y1 = tri[3], x2 = tri[4], y2 = tri[5];
        unsigned int c0 = (unsigned int)tri[6], c1 = (unsigned int)tri[7], c2 = (unsigned int)tri[8];
        int d0 = tri[9], d1 = tri[10], d2 = tri[11];
        int u0 = tri[12], v0t = tri[13], u1 = tri[14], v1t = tri[15], u2 = tri[16], v2t = tri[17];
        int additive = 0;
        int use_texture = (u0 | v0t | u1 | v1t | u2 | v2t) != 0;
        if (gpu_cine) { additive = u0; use_texture = 0; }  /* u0: 0 opaque, 1 hard-add, 2 soft radial sprite (center u1,v1t radius u2) */
        int minx = x0 < x1 ? (x0 < x2 ? x0 : x2) : (x1 < x2 ? x1 : x2);
        int miny = y0 < y1 ? (y0 < y2 ? y0 : y2) : (y1 < y2 ? y1 : y2);
        int maxx = x0 > x1 ? (x0 > x2 ? x0 : x2) : (x1 > x2 ? x1 : x2);
        int maxy = y0 > y1 ? (y0 > y2 ? y0 : y2) : (y1 > y2 ? y1 : y2);
        if (minx < 0) minx = 0; if (miny < 0) miny = 0;
        if (maxx >= w) maxx = w - 1; if (maxy >= h) maxy = h - 1;
        /* Skip triangles entirely outside this band */
        if (maxy < band_y0 || miny > band_y1) continue;
        if (miny < band_y0) miny = band_y0;
        if (maxy > band_y1) maxy = band_y1;
        int area = gpu_edge(x0, y0, x1, y1, x2, y2);
        if (area == 0) continue;
        int sign = area > 0 ? 1 : -1;
        int abs_area = area > 0 ? area : -area;
        for (int y = miny; y <= maxy; y++) {
            for (int x = minx; x <= maxx; x++) {
                int bw0 = gpu_edge(x1, y1, x2, y2, x, y) * sign;
                int bw1 = gpu_edge(x2, y2, x0, y0, x, y) * sign;
                int bw2 = gpu_edge(x0, y0, x1, y1, x, y) * sign;
                if (bw0 >= 0 && bw1 >= 0 && bw2 >= 0) {
                    int depth = (d0 * bw0 + d1 * bw1 + d2 * bw2) / abs_area;
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
                        if (use_texture) {
                            int u_interp = (u0 * bw0 + u1 * bw1 + u2 * bw2) / abs_area;
                            int v_interp = (v0t * bw0 + v1t * bw1 + v2t * bw2) / abs_area;
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
                            pixel = ((unsigned int)tr << 16) | ((unsigned int)tg << 8) | (unsigned int)tb;
                        } else {
                            pixel = gpu_lerp_color(c0, c1, c2, bw0, bw1, bw2, abs_area);
                        }
                        fb[idx] = pixel;
                        db[idx] = (unsigned int)depth;
                    }
                }
            }
        }
    }
}

static void gpu_rasterize_triangles(int count) {
    if (!gop_active) return;
    gpu_frame_count++;
    gpu_last_tri_count = count;
    if (GPU_CMD_ADDR + (unsigned long long)count * 72 > guest_mem_size) return;
    if (GOP_FB_ADDR + (unsigned long long)gop_width * gop_height * 4 > guest_mem_size) return;
    unsigned int *fb = (unsigned int *)((unsigned char *)guest_mem + GOP_FB_ADDR);
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
    gpu_frame_ready = 1;
}

static unsigned char *glow_dist = NULL;
static int glow_valid = 0;

static void gpu_atmosphere_glow(void) {
    if (!gop_active) return;
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

static void sync_shadow_buffers(void) {
    /* Copy VGA text buffer */
    if (VGA_BASE + sizeof(shadow_vga) <= guest_mem_size)
        memcpy(shadow_vga, (unsigned char *)guest_mem + VGA_BASE, sizeof(shadow_vga));

    /* Copy GOP framebuffer after rasterizer frame or VBE direct writes */
    if (gop_active && gop_width > 0 && gop_height > 0 && (gpu_frame_ready || vbe_active)) {
        size_t fb_bytes = (size_t)gop_width * gop_height * 4;
        if (!shadow_gop) shadow_gop = (unsigned char *)calloc(1, GOP_FB_SIZE);
        if (shadow_gop && GOP_FB_ADDR + fb_bytes <= guest_mem_size) {
            memcpy(shadow_gop, (unsigned char *)guest_mem + GOP_FB_ADDR, fb_bytes);
            shadow_gop_w = gop_width;
            shadow_gop_h = gop_height;
            shadow_gop_stride = gop_stride;
        }
    }

    /* Flush pending keyboard scancode to guest memory */
    if (pending_kbd_valid && 28680 + 1 <= guest_mem_size) {
        *((unsigned char *)guest_mem + 28680) = (unsigned char)pending_kbd_scancode;
        pending_kbd_valid = 0;
    }

    /* Flush pending mouse state to guest memory (PS/2 path).
       Do NOT clear pending_mouse_valid here — the absolute-coordinate
       port path (0xE1-0xE4) uses the same flag and clears it on 0xE3 read. */
    if (pending_mouse_valid && MOUSE_BUF_ADDR + 3 <= (int)guest_mem_size) {
        unsigned char *mbuf = (unsigned char *)guest_mem + MOUSE_BUF_ADDR;
        mbuf[0] = pending_mouse[0];
        mbuf[1] = pending_mouse[1];
        mbuf[2] = pending_mouse[2];
    }
}

/* ── Main loop ─────────────────────────────────────────────────────── */

int main(int argc, char **argv) {
    SetConsoleCtrlHandler(ctrl_handler, TRUE);
    create_shutdown_event();
    CreateThread(NULL, 0, shutdown_event_thread, NULL, 0, NULL);
    atexit(cleanup_whp);
    const char *kernel = NULL, *disk = NULL, *boot_args = NULL, *trace_file = NULL;
    int mem_mb = 3072;  /* matches the build harness; binaries from pre-7209
                           seeds triple-fault below 2 GB + stack reserve */

    for (int i = 1; i < argc; i++) {
        if (!strcmp(argv[i], "-kernel") && i+1 < argc) { kernel = argv[++i]; g_kernel_path = kernel; }
        else if (!strcmp(argv[i], "-disk") && i+1 < argc) disk = argv[++i];
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
        else if (!strcmp(argv[i], "-hwwatch-len") && i+1 < argc) hw_watch_len = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-debug")) debug_mode = 1;
        else if (!strcmp(argv[i], "-break") && i+1 < argc) {
            debug_mode = 1;
            if (init_break_count < MAX_INIT_BREAKS) init_break_names[init_break_count++] = argv[++i];
        }
        else if (!strcmp(argv[i], "-map") && i+1 < argc) map_file_path = argv[++i];
        else if (!strcmp(argv[i], "-headless")) vga_headless = 1;
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
        else if (!strcmp(argv[i], "-uefi")) uefi_mode = 1;
        else if (!strcmp(argv[i], "-gop")) { gop_active = 1; }
        else if (!strcmp(argv[i], "-gop-width") && i+1 < argc) { gop_width = atoi(argv[++i]); gop_stride = gop_width; gop_active = 1; }
        else if (!strcmp(argv[i], "-gop-height") && i+1 < argc) { gop_height = atoi(argv[++i]); gop_active = 1; }
        else if (!strcmp(argv[i], "-args") && i+1 < argc) boot_args = argv[++i];
        else if (!strcmp(argv[i], "-trace-file") && i+1 < argc) trace_file = argv[++i];
        else if (!strcmp(argv[i], "-portfwd") && i+1 < argc) {
            char *spec = argv[++i];
            int hp = 0, gp = 0;
            if (sscanf(spec, "%d:%d", &hp, &gp) == 2 && portfwd_count < PORTFWD_MAX) {
                portfwds[portfwd_count].host_port = (unsigned short)hp;
                portfwds[portfwd_count].guest_port = (unsigned short)gp;
                portfwd_count++;
            } else {
                fprintf(stderr, "Bad -portfwd spec: %s (expected host:guest)\n", spec);
            }
        }
    }
    if (!kernel) {
        fprintf(stderr, "Usage: codex-vm -kernel file.cdx [-input file.codex] [-output file.cdx]\n"
                        "       [-disk file.img] [-mem MB] [-args STRING]\n"
                        "       [-watch 0xADDR] [-watch-size N] [-headless] [-uefi]\n"
                        "       [-gop] [-gop-width N] [-gop-height N]\n"
                        "       [-portfwd hostport:guestport] ...\n");
        return 1;
    }
    if (watch_size > 64) watch_size = 64;

    if (getenv("CODEX_VM_NO_TIMER")) { no_timer = 1; fprintf(stderr, "TIMER INTERRUPTS DISABLED\n"); }
    hprof_file = getenv("CODEX_VM_PROFILE");
    if (hprof_file && !hprof_file[0]) hprof_file = NULL;

    WSADATA wsa; WSAStartup(MAKEWORD(2,2), &wsa);
    if (portfwd_count > 0) portfwd_init();

    ide_init(&ide, disk);
    pic_init(&pic_master);
    pic_init(&pic_slave);
    ne2k_reset();

    xhci_init();

    /* Register PCI devices */
    pci_add_device(0x1234, 0x1111, 0x03, 0x00, 0x00, 0xFD000000, 0);  /* slot 0: Bochs VGA (BAR at high MMIO, FB read from RAM at GOP_FB_ADDR) */
    pci_add_device(0x1033, 0x0194, 0x0C, 0x03, 0x30, 0xFE800000, 10);  /* slot 1: xHCI (NEC/Renesas) */
    pci_add_device(0x8086, 0x2668, 0x04, 0x03, 0x00, 0xFE000000, 11);  /* slot 2: Intel HDA */

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
           Guest RSP starts at reported RAM size and grows DOWN — must not overlap. */
        unsigned long long effective = guest_mem_size;
        if (effective > GPU_CMD_ADDR)
            effective = GPU_CMD_ADDR;
        fprintf(stderr, "RAM cap: guest_mem_size=0x%llx effective=0x%llx gop=%d\n", guest_mem_size, effective, gop_active);
        *(unsigned long long *)((unsigned char *)guest_mem + 0xFE8) = effective;
    }

    /* Write GOP resolution to GPA 0x7C4/0x7C8 so guests can read display size. */
    if (gop_active) {
        *(int *)((unsigned char *)guest_mem + 0x7C4) = gop_width;
        *(int *)((unsigned char *)guest_mem + 0x7C8) = gop_height;
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

    if (watch_addr) watch_init();

    /* Load symbol map if provided or auto-detect from kernel path */
    if (map_file_path) {
        load_map_file(map_file_path);
    } else if (debug_mode && kernel) {
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
            /* Guest is inside its exception handler — read registers directly */
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
               the guest was — no injection-delivery bias (the guest-side
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
                /* HLT debug output removed — floods console at 500+ FPS, blocks Win32 message pump */
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
                        char rr[224];
                        snprintf(rr, sizeof(rr),
                            "HW WATCHPOINT #%d watch=0x%llx now=0x%llx (RIP is the instruction AFTER the access)",
                            hw_watch_hits, hw_watch_addr, wv);
                        dbg_crash_report(rr, hw_watch_addr, -1, g_kernel_path);
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
                if (debug_mode) {
                    int r = dbg_command_loop(vec, exc_rip);
                    if (r == 1) goto done;
                    if (r < 0) {
                        /* stepping over breakpoint — will re-patch on next #DB */
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
                    size_t chunk = 2ULL * 1024 * 1024;
                    size_t base = (gpa / chunk) * chunk;
                    size_t len = chunk;
                    if (base + len > guest_mem_size) len = guest_mem_size - base;
                    if (VirtualAlloc((unsigned char *)guest_mem + base, len, MEM_COMMIT, PAGE_READWRITE)) {
                        HRESULT hr2 = WHvMapGpaRange(partition, (unsigned char *)guest_mem + base,
                            base, len,
                            WHvMapGpaRangeFlagRead | WHvMapGpaRangeFlagWrite | WHvMapGpaRangeFlagExecute);
                        if (SUCCEEDED(hr2)) break;
                    }
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
            if (ctx.ExitReason == 4) {
                unsigned long long gpa = ctx.MemoryAccess.Gpa;
                if (gpa < guest_mem_size) {
                    size_t chunk = 2ULL * 1024 * 1024;
                    size_t base = (gpa / chunk) * chunk;
                    size_t len = chunk;
                    if (base + len > guest_mem_size) len = guest_mem_size - base;
                    if (VirtualAlloc((unsigned char *)guest_mem + base, len, MEM_COMMIT, PAGE_READWRITE)) {
                        HRESULT hr2 = WHvMapGpaRange(partition, (unsigned char *)guest_mem + base,
                            base, len,
                            WHvMapGpaRangeFlagRead | WHvMapGpaRangeFlagWrite | WHvMapGpaRangeFlagExecute);
                        if (SUCCEEDED(hr2)) break;
                    }
                }
                char reason[128];
                snprintf(reason, sizeof(reason), "MemAccess GPA=0x%llx (%s) after %llu exits",
                    ctx.MemoryAccess.Gpa,
                    ctx.MemoryAccess.AccessInfo.AccessType == 0 ? "READ" :
                    ctx.MemoryAccess.AccessInfo.AccessType == 1 ? "WRITE" : "EXEC",
                    exits);
                dbg_crash_report(reason, ctx.MemoryAccess.Gpa,
                    ctx.MemoryAccess.AccessInfo.AccessType, g_kernel_path);
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

        /* ── SMP: poll AP trampoline address for AP launch signal ── */
        if (smp_cores > 1 && !lapic_state.ap_running[1]) {
            unsigned long long ap_entry = *(unsigned long long *)((unsigned char *)guest_mem + 0x1000);
            if (ap_entry >= 0x100000 && ap_entry < guest_mem_size) {
                unsigned long long *stack_table = (unsigned long long *)((unsigned char *)guest_mem + 0xF00);
                for (int i = 1; i < SMP_MAX_CORES && i <= lapic_state.ap_count; i++) {
                    if (lapic_state.ap_running[i]) continue;
                    lapic_state.ap_running[i] = 1;
                    HRESULT hr2 = WHvCreateVirtualProcessor(partition, i, 0);
                    if (FAILED(hr2)) {
                        fprintf(stderr, "SMP: WHvCreateVirtualProcessor(%d): 0x%lx\n", i, hr2);
                        lapic_state.ap_running[i] = 0;
                        continue;
                    }
                    unsigned long long ap_stack = stack_table[i];
                    if (ap_stack == 0) ap_stack = 0xC0000000ULL - (unsigned long long)i * 0x10000;
                    WHV_REGISTER_NAME ap_names[] = {
                        WHvX64RegisterRip, WHvX64RegisterRsp, WHvX64RegisterRflags,
                        WHvX64RegisterCs, WHvX64RegisterDs, WHvX64RegisterEs,
                        WHvX64RegisterSs, WHvX64RegisterFs, WHvX64RegisterGs,
                        WHvX64RegisterCr0, WHvX64RegisterCr3, WHvX64RegisterCr4,
                        WHvX64RegisterEfer, WHvX64RegisterGdtr, WHvX64RegisterRdi,
                        WHvX64RegisterIdtr
                    };
                    WHV_REGISTER_VALUE ap_vals[16];
                    memset(ap_vals, 0, sizeof(ap_vals));
                    ap_vals[0].Reg64 = ap_entry;
                    ap_vals[1].Reg64 = ap_stack;
                    ap_vals[2].Reg64 = 0x202;
                    ap_vals[3].Segment.Selector = 0x08;
                    ap_vals[3].Segment.Base = 0;
                    ap_vals[3].Segment.Limit = 0xFFFFFFFF;
                    ap_vals[3].Segment.Attributes = 0xA09B;
                    for (int s = 4; s <= 9; s++) {
                        ap_vals[s].Segment.Selector = 0x10;
                        ap_vals[s].Segment.Base = 0;
                        ap_vals[s].Segment.Limit = 0xFFFFFFFF;
                        ap_vals[s].Segment.Attributes = 0xC093;
                    }
                    ap_vals[10].Reg64 = 0x80000011;
                    ap_vals[11].Reg64 = 0x8000;
                    ap_vals[12].Reg64 = 0x620;
                    ap_vals[13].Table.Base = 0x100000 + 232;
                    ap_vals[13].Table.Limit = 23;
                    ap_vals[14].Reg64 = (unsigned long long)i;
                    ap_vals[15].Table.Base = 0x6000;
                    ap_vals[15].Table.Limit = 4095;
                    WHvSetVirtualProcessorRegisters(partition, i, ap_names, 16, ap_vals);
                    lapic_state.ap_entry_addr = ap_entry;
                    /* Increment ap-ready-count and set shadow RAX so BSP sees it */
                    unsigned long long *ready = (unsigned long long *)((unsigned char *)guest_mem + 4080);
                    (*ready)++;
                    /* Force BSP past wait loop by setting RIP to after the loop.
                       The wait loop's jcc target (exit point) is at the RIP of the
                       first instruction after the hlt+jmp block. We can find it by
                       looking at the BSP's current hlt RIP and jumping past it. */
                    {
                        WHV_REGISTER_NAME rip_name = WHvX64RegisterRip;
                        WHV_REGISTER_VALUE rip_val;
                        WHvGetVirtualProcessorRegisters(partition, 0, &rip_name, 1, &rip_val);
                        /* Skip past: hlt(1) + jmp rel32(5) = 6 bytes after current hlt */
                        rip_val.Reg64 += 6;
                        WHvSetVirtualProcessorRegisters(partition, 0, &rip_name, 1, &rip_val);
                        fprintf(stderr, "SMP: forced BSP past wait loop to RIP=0x%llx\n", rip_val.Reg64);
                    }
                    fprintf(stderr, "SMP: AP %d started, entry=0x%llx stack=0x%llx\n",
                        i, ap_entry, ap_stack);
                }
            }
        }

        /* ── Post-exit: decide what interrupt to queue ── */
        if (pending_irq < 0) {
            int vec = pic_master.vector_base ? pic_master.vector_base : 32;
            if (kbd_irq_pending && kbd_count > 0 && pic_master.vector_base && !(pic_master.mask & (1 << 1))) {
                kbd_irq_pending = 0;
                pending_irq = vec + 1;  /* IRQ 1 = keyboard */
            } else if (!halted && pic_master.vector_base) {
                /* Busy guest: deliver the PIT tick on schedule. Exits during
                   compute come from the timer-kick thread cancelling the VP
                   run every tick period — without it a compute-bound guest
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
                /* Halted waiting for interrupt — timer only (no serial) */
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
        if (exits % 10 == 0) { nat_poll_rx(); if (portfwd_count > 0) portfwd_poll(); ne2k_inject_rx(); }

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

        /* Screenshot timer */
        if (screenshot_path) {
            LARGE_INTEGER now;
            QueryPerformanceCounter(&now);
            double elapsed_ms = (double)(now.QuadPart - screenshot_start.QuadPart) * 1000.0 / perf_freq.QuadPart;
            if (elapsed_ms >= screenshot_delay_ms) {
                double fps = (elapsed_ms > 0) ? gpu_frame_count * 1000.0 / elapsed_ms : 0;
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
       had shutdown(SD_SEND) called by the FIN handler — buffered data
       drains normally. Active connections get a graceful half-close. */
    for (int i = 0; i < NAT_MAX_CONN; i++) {
        if (nat_conns[i].sock != INVALID_SOCKET && (nat_conns[i].active || nat_conns[i].state == 3)) {
            if (nat_conns[i].state != 3) shutdown(nat_conns[i].sock, SD_SEND);
            closesocket(nat_conns[i].sock);
            nat_conns[i].active = 0;
        }
    }
    drain_guest_output();
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
    if (guest_mem) { VirtualFree(guest_mem, 0, MEM_RELEASE); guest_mem = NULL; }
    WSACleanup();
    int rc = (debug_exit_code >= 0) ? (debug_exit_code << 1) | 1 : 0;
    fprintf(stderr, "FINAL: debug_exit_code=%d process_exit=%d\n", debug_exit_code, rc);
    return rc;
}
