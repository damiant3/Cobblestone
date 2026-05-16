/*
 * codex-vm: Minimal WHP-based VM for Codex bare-metal binaries.
 * Replaces QEMU for development. Serial on TCP sockets, IDE from raw file.
 *
 * Usage: codex-vm.exe -kernel file.cdx [-disk file.img] [-mem 1024]
 *        [-data-port 12345] [-ctrl-port 12346]
 *
 * Serial protocol matches QEMU chardev (tcp listen, same as qemu-config.ps1).
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

#define GUEST_MEM_BASE  0
#define LOAD_ADDR       0x100000
#define STACK_TOP       0x7FFE00
#define PAGE_TABLE_ADDR 0xC00000
#define MAX_MEM         (1024*1024*1024ULL)

/* Serial state */
typedef struct {
    SOCKET sock;
    SOCKET client;
    int port;
    int connected;
    int dlab;
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

static void *guest_mem;
static size_t guest_mem_size;
static WHV_PARTITION_HANDLE partition;
static SerialPort com1, com2;
static IdeState ide;
static int debug_exit_code = -1;

/* PIT state — guest programs channel 0 mode 2, we just track the vector */
static int pit_vector = 32;
static LARGE_INTEGER perf_freq;
static LARGE_INTEGER last_tick;

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
    if (sp->client != INVALID_SOCKET) sp->connected = 1;
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

static int serial_recv(SerialPort *sp) {
    if (!sp->connected) return -1;
    unsigned char b;
    u_long avail = 0;
    ioctlsocket(sp->client, FIONREAD, &avail);
    if (avail == 0) return -1;
    int n = recv(sp->client, (char*)&b, 1, 0);
    return (n == 1) ? b : -1;
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

/* ── Page tables ───────────────────────────────────────────────────── */

static void setup_page_tables(void) {
    unsigned char *pt = (unsigned char*)guest_mem + PAGE_TABLE_ADDR;
    memset(pt, 0, 3 * 4096);
    /* PML4[0] -> PDPT */
    *(unsigned long long*)(pt) = (PAGE_TABLE_ADDR + 4096) | 3;
    /* PDPT[0] -> PD */
    *(unsigned long long*)(pt + 4096) = (PAGE_TABLE_ADDR + 8192) | 3;
    /* PD: 512 x 2MB huge pages = 1GB identity map */
    for (int i = 0; i < 512; i++)
        *(unsigned long long*)(pt + 8192 + i*8) = ((unsigned long long)i * 0x200000) | 0x83;
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

    /* Enable I/O port, CPUID, MSR exits */
    memset(&prop, 0, sizeof(prop));
    prop.ExtendedVmExits.X64CpuidExit = 1;
    prop.ExtendedVmExits.X64MsrExit = 1;
    WHvSetPartitionProperty(partition, WHvPartitionPropertyCodeExtendedVmExits, &prop, sizeof(prop));

    hr = WHvSetupPartition(partition);
    if (FAILED(hr)) { fprintf(stderr, "WHvSetupPartition: 0x%lx\n", hr); exit(1); }

    guest_mem = VirtualAlloc(NULL, guest_mem_size, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!guest_mem) die("VirtualAlloc");
    memset(guest_mem, 0, guest_mem_size);

    hr = WHvMapGpaRange(partition, guest_mem, 0, guest_mem_size,
        WHvMapGpaRangeFlagRead | WHvMapGpaRangeFlagWrite | WHvMapGpaRangeFlagExecute);
    if (FAILED(hr)) { fprintf(stderr, "WHvMapGpaRange: 0x%lx\n", hr); exit(1); }

    hr = WHvCreateVirtualProcessor(partition, 0, 0);
    if (FAILED(hr)) { fprintf(stderr, "WHvCreateVirtualProcessor: 0x%lx\n", hr); exit(1); }
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

    /* Parse multiboot header for entry point */
    unsigned char *mb = buf + skip;
    if (payload > 32 && *(unsigned int*)mb == 0x1BADB002) {
        unsigned int flags = *(unsigned int*)(mb + 4);
        if (flags & 0x10000) {
            unsigned int entry = *(unsigned int*)(mb + 28);
            fprintf(stderr, "Multiboot entry: 0x%x\n", entry);
            /* Store entry point for set_initial_regs */
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

    /* RIP: use multiboot entry if parsed, else LOAD_ADDR */
    unsigned int mb_entry = *(unsigned int*)((unsigned char*)guest_mem + 0x500);
    vals[0].Reg64 = mb_entry ? mb_entry : LOAD_ADDR;
    vals[1].Reg64 = STACK_TOP;
    vals[2].Reg64 = 0x2;

    /* CR0: PE only (no paging — trampoline enables it), CR3/CR4/EFER: zero */
    vals[3].Reg64 = 0x11;  /* PE + ET */
    vals[4].Reg64 = 0;     /* CR3: trampoline sets this */
    vals[5].Reg64 = 0;     /* CR4: trampoline sets this */
    vals[6].Reg64 = 0;     /* EFER: trampoline sets LME */

    /* CS: 32-bit code segment (multiboot starts in 32-bit PM) */
    vals[7].Segment.Base = 0; vals[7].Segment.Limit = 0xFFFFFFFF;
    vals[7].Segment.Selector = 0x08;
    vals[7].Segment.Attributes = 0xC09B;

    /* DS, ES, SS: 32-bit data */
    for (int i = 8; i <= 11; i++) {
        vals[i].Segment.Base = 0; vals[i].Segment.Limit = 0xFFFFFFFF;
        vals[i].Segment.Selector = 0x10;
        vals[i].Segment.Attributes = 0xC093;
    }
    /* FS, GS: 32-bit data */
    vals[12].Segment.Base = 0; vals[12].Segment.Limit = 0xFFFFFFFF;
    vals[12].Segment.Selector = 0x10; vals[12].Segment.Attributes = 0xC093;

    /* TR: 32-bit TSS */
    vals[13].Segment.Base = 0; vals[13].Segment.Limit = 0x67;
    vals[13].Segment.Selector = 0x18; vals[13].Segment.Attributes = 0x8B;

    /* EAX = multiboot magic, EBX = multiboot info (0 = none) */
    vals[14].Reg64 = 0x2BADB002;
    vals[15].Reg64 = 0;

    HRESULT hr = WHvSetVirtualProcessorRegisters(partition, 0, names, 16, vals);
    if (FAILED(hr)) { fprintf(stderr, "SetRegs: 0x%lx\n", hr); exit(1); }
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
        /* IDE */
        else if ((port >= 0x1F0 && port <= 0x1F7) || port == 0x3F6) {
            ide_handle_out(&ide, port, val);
        }
        /* Debug exit */
        else if (port == 0xF4) {
            debug_exit_code = val;
        }
    } else {
        int result = 0xFF;
        /* Serial COM1 */
        if (port >= 0x3F8 && port <= 0x3FF) {
            if (port == 0x3F8 && !com1.dlab) { int b = serial_recv(&com1); result = (b >= 0) ? b : 0; }
            else if (port == 0x3FA) result = 1;
            else if (port == 0x3FD) result = 0x60 | (serial_has_data(&com1) ? 1 : 0);
            else if (port == 0x3FE) result = 0xB0;
            else result = 0;
        }
        /* Serial COM2 */
        else if (port >= 0x2F8 && port <= 0x2FF) {
            if (port == 0x2F8 && !com2.dlab) { int b = serial_recv(&com2); result = (b >= 0) ? b : 0; }
            else if (port == 0x2FA) result = 1;
            else if (port == 0x2FD) result = 0x60 | (serial_has_data(&com2) ? 1 : 0);
            else if (port == 0x2FE) result = 0xB0;
            else result = 0;
        }
        /* IDE */
        else if ((port >= 0x1F0 && port <= 0x1F7) || port == 0x3F6) {
            result = ide_handle_in(&ide, port);
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
    /* Return zeros for most leaves; bare minimum for 64-bit */
    if (leaf == 0) { vals[0].Reg64 = 1; vals[1].Reg64 = 0x756E6547; vals[2].Reg64 = 0x6C65746E; vals[3].Reg64 = 0x49656E69; }
    else if (leaf == 1) { vals[0].Reg64 = 0x000306C3; vals[2].Reg64 = 0; vals[3].Reg64 = 0x078BFBFF; }
    vals[4].Reg64 = ctx->VpContext.Rip + ctx->VpContext.InstructionLength;
    WHvSetVirtualProcessorRegisters(partition, 0, names, 5, vals);
}

static void handle_msr(WHV_RUN_VP_EXIT_CONTEXT *ctx, int is_write) {
    /* Skip MSR access — advance RIP, return 0 for reads */
    WHV_REGISTER_NAME names[] = { WHvX64RegisterRax, WHvX64RegisterRdx, WHvX64RegisterRip };
    WHV_REGISTER_VALUE vals[3];
    memset(vals, 0, sizeof(vals));
    vals[2].Reg64 = ctx->VpContext.Rip + ctx->VpContext.InstructionLength;
    WHvSetVirtualProcessorRegisters(partition, 0, names, 3, vals);
}

static void inject_timer_interrupt(void) {
    WHV_REGISTER_NAME names[2];
    WHV_REGISTER_VALUE vals[2];
    memset(vals, 0, sizeof(vals));
    names[0] = WHvRegisterInternalActivityState;
    names[1] = WHvRegisterPendingInterruption;
    vals[1].PendingInterruption.InterruptionPending = 1;
    vals[1].PendingInterruption.InterruptionType = 0;
    vals[1].PendingInterruption.InterruptionVector = pit_vector;
    WHvSetVirtualProcessorRegisters(partition, 0, names, 2, vals);
}

/* ── Main loop ─────────────────────────────────────────────────────── */

int main(int argc, char **argv) {
    const char *kernel = NULL, *disk = NULL;
    int mem_mb = 1024, data_port = 12345, ctrl_port = 12346;

    for (int i = 1; i < argc; i++) {
        if (!strcmp(argv[i], "-kernel") && i+1 < argc) kernel = argv[++i];
        else if (!strcmp(argv[i], "-disk") && i+1 < argc) disk = argv[++i];
        else if (!strcmp(argv[i], "-mem") && i+1 < argc) mem_mb = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-data-port") && i+1 < argc) data_port = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-ctrl-port") && i+1 < argc) ctrl_port = atoi(argv[++i]);
    }
    if (!kernel) { fprintf(stderr, "Usage: codex-vm -kernel file.cdx [-disk file.img] [-mem MB] [-data-port N] [-ctrl-port N]\n"); return 1; }

    WSADATA wsa; WSAStartup(MAKEWORD(2,2), &wsa);

    serial_init(&com1, data_port);
    serial_init(&com2, ctrl_port);
    ide_init(&ide, disk);

    create_vm(mem_mb);
    load_kernel(kernel);
    set_initial_regs();

    fprintf(stderr, "VM ready. Waiting for connections on ports %d/%d...\n", data_port, ctrl_port);
    serial_accept(&com1);
    serial_accept(&com2);
    fprintf(stderr, "Connected. Running guest...\n");

    QueryPerformanceFrequency(&perf_freq);
    QueryPerformanceCounter(&last_tick);

    WHV_RUN_VP_EXIT_CONTEXT ctx;
    unsigned long long exits = 0;
    for (;;) {
        HRESULT hr = WHvRunVirtualProcessor(partition, 0, &ctx, sizeof(ctx));
        if (FAILED(hr)) { fprintf(stderr, "WHvRunVirtualProcessor: 0x%lx\n", hr); break; }
        exits++;

        switch (ctx.ExitReason) {
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
            if (ctx.VpContext.Rflags & 0x200) {
                LARGE_INTEGER now;
                QueryPerformanceCounter(&now);
                double elapsed = (double)(now.QuadPart - last_tick.QuadPart) / perf_freq.QuadPart;
                if (elapsed < 0.055) {
                    DWORD ms = (DWORD)((0.055 - elapsed) * 1000.0);
                    if (ms > 0) Sleep(ms);
                }
                QueryPerformanceCounter(&last_tick);
                inject_timer_interrupt();
            } else {
                fprintf(stderr, "Guest halted with IF=0 after %llu exits\n", exits);
                goto done;
            }
            break;
        case WHvRunVpExitReasonX64InterruptWindow:
            inject_timer_interrupt();
            break;
        case WHvRunVpExitReasonMemoryAccess:
            fprintf(stderr, "MMIO at 0x%llx (RIP=0x%llx) after %llu exits\n",
                ctx.MemoryAccess.Gpa, ctx.VpContext.Rip, exits);
            goto done;
        default:
            fprintf(stderr, "Unhandled exit reason %d (RIP=0x%llx) after %llu exits\n",
                ctx.ExitReason, ctx.VpContext.Rip, exits);
            goto done;
        }
    }
done:
    fprintf(stderr, "VM exited (code=%d, exits=%llu)\n", debug_exit_code, exits);
    serial_close(&com1);
    serial_close(&com2);
    WHvDeleteVirtualProcessor(partition, 0);
    WHvDeletePartition(partition);
    if (ide.data) free(ide.data);
    VirtualFree(guest_mem, 0, MEM_RELEASE);
    WSACleanup();
    return (debug_exit_code >= 0) ? (debug_exit_code << 1) | 1 : 0;
}
