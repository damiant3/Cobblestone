; Option A UEFI boot stub (validation prototype, MASM).
;
; Wraps a compiled Codex CDX. Strict-clean ordering (proven by A1):
;   acquire GOP -> AllocateAnyPages (keep addr) -> GetMemoryMap ->
;   ExitBootServices -> build own 4 GB identity page tables -> mov cr3 ->
;   ONLY THEN write the GOP handoff cells, copy .text/.rodata to their link
;   addresses, set up R10/RSP + kernel metadata cells, and jump to `opening`.
;
; The build wrapper (build-option-a.ps1) assembles this, extracts the stub's
; machine code, patches the placeholder immediates below with the CDX's real
; sizes/offsets, then lays out the final PE as [stub][cdx .text][cdx .rodata].
; The CDX payload sits immediately after `cdx_payload:`, so the RIP-relative
; source lea resolves to it.
;
; MS x64 ABI: firmware preserves rbx, rbp, rsi, rdi, r12-r15 across calls, so
; we hold live state there. Placeholder magics (positive imm32, patched by the
; wrapper) are chosen distinctive and unlikely to collide with real code bytes.

ALLOC_PAGES_MAGIC   EQU 07A000001h   ; total pages to AllocateAnyPages
TEXTSIZE_MAGIC      EQU 07A000002h   ; CDX .text byte count
RODATASIZE_MAGIC    EQU 07A000003h   ; CDX .rodata byte count
DATAVADDR_MAGIC     EQU 07A000004h   ; rodata link vaddr (0x100000 + align8(textSize))
OPENING_OFF_MAGIC   EQU 07A000005h   ; opening's text-relative offset

CODE_VADDR          EQU 0100000h
SYS_BOOTSVCS        EQU 060h
BS_ALLOCATEPAGES    EQU 028h
BS_GETMEMORYMAP     EQU 038h
BS_EXITBOOTSVCS     EQU 0E8h
BS_LOCATEPROTOCOL   EQU 140h
GOP_MODE            EQU 018h
MODE_INFO           EQU 008h
MODE_FBBASE         EQU 018h
INFO_HRES           EQU 004h
INFO_VRES           EQU 008h
INFO_STRIDE         EQU 020h        ; PixelsPerScanLine (standard offset +32)

CELL_FB             EQU 08000h
CELL_W              EQU 08008h
CELL_H              EQU 08010h
CELL_STRIDE         EQU 08018h
CELL_HEAP           EQU 08020h
CELL_RSDP           EQU 08028h        ; ACPI RSDP pointer, 0 if firmware exposed none
CELL_ENTROPY        EQU 07770h        ; device seed: 32 bytes (kernel cell 30576)

SYS_CFG_COUNT       EQU 068h          ; SystemTable.NumberOfTableEntries
SYS_CFG_TABLE       EQU 070h          ; SystemTable.ConfigurationTable
CFG_ENTRY_SIZE      EQU 018h          ; sizeof(EFI_CONFIGURATION_TABLE)

DECK_POS_ADDR       EQU 07030h
HEAP_HWM_ADDR       EQU 07038h
STACK_MIN_RSP_ADDR  EQU 07040h
DECK_BOUND_ADDR     EQU 070E8h
BIVY_SAVE_ADDR      EQU 070F0h

MEMMAP_OFF          EQU 08000h        ; GetMemoryMap buffer, 16 KB, within the allocation
HEAPBASE_OFF        EQU 0C000h        ; Codex heap base = rbp + 0xC000

_TEXT SEGMENT

efi_main PROC
    mov     r15, rdx                     ; SystemTable
    mov     r14, rcx                     ; ImageHandle
    mov     rdi, [r15+SYS_BOOTSVCS]      ; BootServices (preserved across calls)
    sub     rsp, 0108h                   ; entry rsp is 8-mod-16; 0x108 makes calls 16-aligned

    ; ---- LocateProtocol(GOP) ----
    lea     rcx, gop_guid
    xor     rdx, rdx
    lea     r8,  [rsp+70h]
    call    QWORD PTR [rdi+BS_LOCATEPROTOCOL]
    test    rax, rax
    jnz     fatal
    mov     rax, [rsp+70h]               ; gop
    mov     rax, [rax+GOP_MODE]          ; mode
    mov     rsi, [rax+MODE_FBBASE]       ; rsi = FrameBufferBase (preserved)
    mov     rcx, [rax+MODE_INFO]         ; info
    mov     r12d, DWORD PTR [rcx+INFO_HRES]  ; r12 = width  (preserved)
    mov     r13d, DWORD PTR [rcx+INFO_VRES]  ; r13 = height (preserved)
    mov     ebx, DWORD PTR [rcx+INFO_STRIDE] ; rbx = PixelsPerScanLine (preserved; ebx zero-extends)

    ; ---- LIVENESS MARK 1: we have GOP ----
    ; Every failure path in this stub ends at `fatal`, which is `jmp fatal`:
    ; a silent spin. So a board that cannot give us GOP, cannot give us
    ; 128 MB, or refuses ExitBootServices produces exactly the same thing an
    ; operator sees when the firmware never loaded us at all -- an unchanged
    ; screen. On 2026-07-29 that ambiguity cost a boot on the ASUS and
    ; returned one bit of information. Two solid fills split the silence into
    ; three states readable across a room with no camera:
    ;   firmware screen unchanged -> never loaded, or no GOP
    ;   DARK BLUE and nothing more -> GOP acquired, died in allocation,
    ;                                 GetMemoryMap or ExitBootServices
    ;   DARK GREEN and nothing more -> through ExitBootServices and paging,
    ;                                  died in the payload
    ; rdi holds BootServices and is needed for every call below, so it is
    ; saved around the store.
    push    rdi
    cld
    mov     rdi, rsi
    mov     eax, r13d
    imul    eax, ebx                     ; pixels = height * PixelsPerScanLine
    mov     ecx, eax
    mov     eax, 00202060h               ; dark blue
    rep     stosd
    pop     rdi

    ; ---- AllocateAnyPages(0, EfiLoaderData=2, ALLOC_PAGES, &base) ----
    xor     rcx, rcx
    mov     rdx, 2
    mov     r8,  ALLOC_PAGES_MAGIC
    lea     r9,  [rsp+78h]
    call    QWORD PTR [rdi+BS_ALLOCATEPAGES]
    test    rax, rax
    jnz     fatal
    mov     rbp, [rsp+78h]               ; rbp = allocation base (preserved)

    ; ---- GetMemoryMap(&size, buf, &key, &dsz, &dver) ----
    mov     QWORD PTR [rsp+40h], 04000h
    lea     rcx, [rsp+40h]
    lea     rdx, [rbp+MEMMAP_OFF]
    lea     r8,  [rsp+48h]
    lea     r9,  [rsp+50h]
    lea     rax, [rsp+58h]
    mov     QWORD PTR [rsp+20h], rax
    call    QWORD PTR [rdi+BS_GETMEMORYMAP]

    ; ---- ExitBootServices(ImageHandle, MapKey), retry once on stale key ----
    mov     rcx, r14
    mov     rdx, [rsp+48h]
    call    QWORD PTR [rdi+BS_EXITBOOTSVCS]
    test    rax, rax
    jz      ebs_ok
    mov     QWORD PTR [rsp+40h], 04000h
    lea     rcx, [rsp+40h]
    lea     rdx, [rbp+MEMMAP_OFF]
    lea     r8,  [rsp+48h]
    lea     r9,  [rsp+50h]
    lea     rax, [rsp+58h]
    mov     QWORD PTR [rsp+20h], rax
    call    QWORD PTR [rdi+BS_GETMEMORYMAP]
    mov     rcx, r14
    mov     rdx, [rsp+48h]
    call    QWORD PTR [rdi+BS_EXITBOOTSVCS]
ebs_ok:
    cli

    ; ---- build a 4 GB identity map at rbp, then load it ----
    lea     rax, [rbp+1000h]
    or      rax, 3
    mov     [rbp], rax                   ; PML4[0] -> PDPT
    lea     rax, [rbp+2000h]
    or      rax, 3
    xor     rcx, rcx
pdpt_l:
    mov     [rbp+1000h+rcx*8], rax
    add     rax, 1000h
    inc     rcx
    cmp     rcx, 4
    jl      pdpt_l
    lea     r8, [rbp+2000h]
    xor     rcx, rcx
    xor     rax, rax
pd_l:
    mov     rdx, rax
    or      rdx, 83h
    mov     [r8+rcx*8], rdx
    add     rax, 200000h
    inc     rcx
    cmp     rcx, 2048
    jl      pd_l
    mov     cr3, rbp                     ; our map is live; low memory is ours

    ; ---- hand off GOP info to the Codex program (cells now writable) ----
    mov     [CELL_FB], rsi
    mov     [CELL_W], r12
    mov     [CELL_H], r13
    mov     [CELL_STRIDE], rbx           ; real PixelsPerScanLine (correct on padded-scanline hw)
    lea     rax, [rbp+HEAPBASE_OFF]
    mov     [CELL_HEAP], rax

    ; ---- LIVENESS MARK 2: past ExitBootServices, our page tables are live ----
    ; Placed after the handoff cells so the fill also proves the framebuffer
    ; address we just handed the payload is one WE can write through our own
    ; map, not merely one the firmware could reach. If the payload paints
    ; nothing over this, the fault is in the payload rather than the handoff.
    ; rdi is dead here -- BootServices is finished with, and the next read of
    ; rdi is `mov rdi, CELL_ENTROPY` below -- so it is clobbered rather than
    ; pushed. Nothing else in this stub touches the stack after `mov cr3`,
    ; and a push here would newly depend on the firmware's stack being
    ; mapped by OUR page tables.
    cld
    mov     rdi, rsi
    mov     eax, r13d
    imul    eax, ebx
    mov     ecx, eax
    mov     eax, 00104020h               ; dark green
    rep     stosd

    ; ---- capture the ACPI RSDP from the UEFI configuration table ----
    ; The RSDP is not a protocol -- LocateProtocol cannot find it. It is a
    ; vendor table hanging off SystemTable.ConfigurationTable, keyed by GUID.
    ; The SystemTable, the configuration table, and the ACPI tables all
    ; survive ExitBootServices (runtime memory), and our identity map covers
    ; them, so the walk is safe here. Prefer the ACPI 2.0 GUID (its RSDP has
    ; an XSDT) and fall back to the 1.0 GUID. Zero means the firmware exposed
    ; neither, and the payload reports "no ACPI" rather than guessing.
    xor     r8, r8                       ; r8 = ACPI 1.0 RSDP candidate
    xor     r9, r9                       ; r9 = ACPI 2.0 RSDP candidate
    mov     rcx, [r15+SYS_CFG_COUNT]
    mov     rdx, [r15+SYS_CFG_TABLE]
    test    rcx, rcx
    jz      acpi_done
    test    rdx, rdx
    jz      acpi_done
cfg_loop:
    lea     rax, acpi20_guid
    mov     r11, [rax]
    cmp     r11, [rdx]
    jne     cfg_try10
    mov     r11, [rax+8]
    cmp     r11, [rdx+8]
    jne     cfg_try10
    mov     r9, [rdx+16]
    jmp     cfg_next
cfg_try10:
    lea     rax, acpi10_guid
    mov     r11, [rax]
    cmp     r11, [rdx]
    jne     cfg_next
    mov     r11, [rax+8]
    cmp     r11, [rdx+8]
    jne     cfg_next
    mov     r8, [rdx+16]
cfg_next:
    add     rdx, CFG_ENTRY_SIZE
    dec     rcx
    jnz     cfg_loop
acpi_done:
    test    r9, r9
    jnz     acpi_store
    mov     r9, r8
acpi_store:
    mov     [CELL_RSDP], r9

    ; ---- device entropy seed: 32 hardware-random bytes to CELL_ENTROPY ----
    ; CPUID.01H:ECX[30] gates RDRAND; each qword retries up to 32 times
    ; (transient CF=0 is architectural), then degrades to RDTSC for that
    ; qword. TCG without RDRAND gets four rotated TSC samples -- weak, but
    ; never the all-zero cell the first-boot keygen mixed in before.
    mov     eax, 1
    xor     ecx, ecx
    cpuid                                ; clobbers eax/ebx/ecx/edx (all dead here)
    mov     r8d, ecx                     ; feature bits
    mov     rdi, CELL_ENTROPY
    mov     rcx, 4                       ; four qwords = 32 bytes
ent_qword:
    bt      r8d, 30
    jnc     ent_tsc
    mov     rbx, 32                      ; bounded retries per qword
ent_try:
    rdrand  rax
    jc      ent_store
    dec     rbx
    jnz     ent_try
ent_tsc:
    rdtsc                                ; edx:eax
    shl     rdx, 32
    or      rax, rdx
    ror     rax, 13
    xor     rax, rcx
ent_store:
    mov     [rdi], rax
    add     rdi, 8
    dec     rcx
    jnz     ent_qword

    ; ---- copy CDX .text -> 0x100000, .rodata -> data vaddr ----
    lea     rsi, cdx_payload
    mov     edi, CODE_VADDR
    mov     ecx, TEXTSIZE_MAGIC
    rep     movsb                        ; rsi advances to the rodata payload
    mov     edi, DATAVADDR_MAGIC
    mov     ecx, RODATASIZE_MAGIC
    rep     movsb

    ; ---- init kernel metadata cells + R10 (heap) + RSP (stack) ----
    lea     r10, [rbp+HEAPBASE_OFF]
    mov     [DECK_POS_ADDR], r10
    mov     [HEAP_HWM_ADDR], r10
    xor     rax, rax
    mov     [DECK_BOUND_ADDR], rax
    mov     [BIVY_SAVE_ADDR], rax
    mov     rax, ALLOC_PAGES_MAGIC
    shl     rax, 12                       ; ALLOC_PAGES * 4096
    add     rax, rbp
    mov     rsp, rax                      ; stack top (grows down, above heap)
    mov     [STACK_MIN_RSP_ADDR], rsp
    mov     rbp, rsp

    ; ---- jump to opening (0x100000 + openingOff) ----
    mov     rax, CODE_VADDR
    add     rax, OPENING_OFF_MAGIC
    jmp     rax

fatal:
    hlt
    jmp     fatal
efi_main ENDP

    ALIGN 16
gop_guid:
    BYTE 0DEh,0A9h,042h,090h, 0DCh,023h, 038h,04Ah, 096h,0FBh,07Ah,0DEh,0D0h,080h,051h,06Ah

    ; EFI_ACPI_20_TABLE_GUID  8868e871-e4f1-11d3-bc22-0080c73c8881
    ALIGN 16
acpi20_guid:
    BYTE 071h,0E8h,068h,088h, 0F1h,0E4h, 0D3h,011h, 0BCh,022h,000h,080h,0C7h,03Ch,088h,081h

    ; ACPI_TABLE_GUID (1.0)   eb9d2d30-2d88-11d3-9a16-0090273fc14d
    ALIGN 16
acpi10_guid:
    BYTE 030h,02Dh,09Dh,0EBh, 088h,02Dh, 0D3h,011h, 09Ah,016h,000h,090h,027h,03Fh,0C1h,04Dh

; CDX payload ([cdx .text][cdx .rodata]) is appended here by the build wrapper.
    ALIGN 16
cdx_payload:

_TEXT ENDS

END
