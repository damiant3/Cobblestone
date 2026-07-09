; A1 -- Option A "boot and paint" proof stub (UEFI PE32+ application).
;
; Purpose: prove the Option A boot mechanism in isolation, with NO compiler
; involvement. Acquire the GOP framebuffer while boot services are alive,
; call ExitBootServices, install our OWN identity page tables, then paint the
; framebuffer from our own map. If this shows a test pattern under codex-vm
; (-gop -screenshot) AND passes -uefi-strict, the whole boot handshake is
; proven and we port this exact sequence into the blessed builder.
;
; This MASM file is a validation PROTOTYPE, not part of the trusted boot
; chain. The real stub is hand-emitted by the blessed builder.
;
; Build: ml64 /c a1_boot_paint.asm ; link /SUBSYSTEM:EFI_APPLICATION
;        /ENTRY:efi_main /NODEFAULTLIB a1_boot_paint.obj
;
; MS x64 ABI on entry: RCX=ImageHandle, RDX=SystemTable. 32 bytes shadow
; space required above the return address before every firmware call.

_TEXT SEGMENT

; ---- EFI_SYSTEM_TABLE / EFI_BOOT_SERVICES offsets ----
ST_BOOTSERVICES   EQU 060h
BS_ALLOCATEPAGES  EQU 028h
BS_GETMEMORYMAP   EQU 038h
BS_EXITBOOTSVCS   EQU 0E8h
BS_LOCATEPROTOCOL EQU 140h

; ---- EFI_GRAPHICS_OUTPUT_PROTOCOL layout ----
GOP_MODE          EQU 018h   ; ptr to EFI_GRAPHICS_OUTPUT_PROTOCOL_MODE
MODE_INFO         EQU 008h   ; ptr to MODE_INFORMATION
MODE_FBBASE       EQU 018h   ; FrameBufferBase (UINTN)
INFO_HRES         EQU 004h   ; HorizontalResolution (UINT32)
INFO_VRES         EQU 008h   ; VerticalResolution (UINT32)

efi_main PROC
    ; Prolog: stack frame with room for firmware call args (5th arg of
    ; GetMemoryMap goes at [rsp+20h]) plus locals. Keep 16-byte alignment.
    sub     rsp, 088h
    mov     r15, rdx                 ; r15 = SystemTable
    mov     r14, rcx                 ; r14 = ImageHandle
    mov     rdi, [r15+ST_BOOTSERVICES] ; rdi = BootServices (callee-saved use)

    ; ---- LocateProtocol(&GOP_GUID, NULL, &gop) ----
    lea     rcx, gop_guid
    xor     rdx, rdx
    lea     r8,  [rsp+70h]           ; &gop interface out-ptr (local slot)
    call    QWORD PTR [rdi+BS_LOCATEPROTOCOL]
    test    rax, rax
    jnz     hang                     ; no GOP -> give up (would print in real stub)

    ; Read framebuffer base + resolution from the GOP protocol.
    mov     rbx, [rsp+70h]           ; rbx = gop
    mov     rbx, [rbx+GOP_MODE]      ; rbx = mode
    mov     rsi, [rbx+MODE_FBBASE]   ; rsi = FrameBufferBase (save across EBS)
    mov     rcx, [rbx+MODE_INFO]     ; rcx = info
    mov     r12d, DWORD PTR [rcx+INFO_HRES] ; r12 = width
    mov     r13d, DWORD PTR [rcx+INFO_VRES] ; r13 = height

    ; ---- AllocatePages(AllocateAnyPages=0, EfiLoaderData=2, 8, &pt) ----
    xor     rcx, rcx                 ; AllocateAnyPages
    mov     rdx, 2                   ; EfiLoaderData
    mov     r8,  8                   ; 8 pages: PML4 + PDPT + 4 PDs + slack
    lea     r9,  [rsp+78h]           ; &pt out-ptr
    call    QWORD PTR [rdi+BS_ALLOCATEPAGES]
    test    rax, rax
    jnz     hang
    mov     r11, [rsp+78h]           ; r11 = page-table base (page-aligned)

    ; ---- GetMemoryMap(&size, buf, &key, &dsz, &dver) ----
    ; buf = a 16KB scratch inside our allocated PT block, past the 6 pages we
    ; use for tables (PT block is 8 pages = 32KB; tables use first 6 pages =
    ; 24KB, leaving pages 6-7 = 8KB -- too small). Use a separate stack buffer.
    mov     QWORD PTR [rsp+40h], 04000h ; MapSize = 16KB (in/out)
    lea     rcx, [rsp+40h]           ; &MapSize
    lea     rdx, mm_buf              ; MemoryMap buffer (bss, 16KB)
    lea     r8,  [rsp+48h]           ; &MapKey
    lea     r9,  [rsp+50h]           ; &DescriptorSize
    lea     rax, [rsp+58h]           ; &DescriptorVersion
    mov     QWORD PTR [rsp+20h], rax ; 5th arg on stack
    call    QWORD PTR [rdi+BS_GETMEMORYMAP]

    ; ---- ExitBootServices(ImageHandle, MapKey) ----
    mov     rcx, r14
    mov     rdx, [rsp+48h]
    call    QWORD PTR [rdi+BS_EXITBOOTSVCS]
    test    rax, rax
    jz      ebs_ok
    ; stale key: refetch once and retry
    mov     QWORD PTR [rsp+40h], 04000h
    lea     rcx, [rsp+40h]
    lea     rdx, mm_buf
    lea     r8,  [rsp+48h]
    lea     r9,  [rsp+50h]
    lea     rax, [rsp+58h]
    mov     QWORD PTR [rsp+20h], rax
    call    QWORD PTR [rdi+BS_GETMEMORYMAP]
    mov     rcx, r14
    mov     rdx, [rsp+48h]
    call    QWORD PTR [rdi+BS_EXITBOOTSVCS]
ebs_ok:
    cli                              ; firmware IDT is gone; no interrupts

    ; ---- Build a 4 GB identity map in our allocated pages ----
    ; r11 = PML4. PDPT = r11+1000h. PD0..PD3 = r11+2000h..5000h.
    ; PML4[0] = PDPT | 3
    lea     rax, [r11+1000h]
    or      rax, 3
    mov     [r11], rax
    ; PDPT[0..3] = PD0..PD3 | 3
    lea     rax, [r11+2000h]
    or      rax, 3
    mov     rcx, 0                    ; i = 0..3
pdpt_loop:
    mov     [r11+1000h+rcx*8], rax
    add     rax, 1000h               ; next PD page
    inc     rcx
    cmp     rcx, 4
    jl      pdpt_loop
    ; PD entries: 4 PDs * 512 = 2048 entries, each maps 2MB, PS+RW+P (83h)
    lea     rdi, [r11+2000h]         ; PD0 base
    xor     rcx, rcx                 ; entry index 0..2047
    xor     rax, rax                 ; phys addr, +2MB per entry
pd_loop:
    mov     rdx, rax
    or      rdx, 83h
    mov     [rdi+rcx*8], rdx
    add     rax, 200000h
    inc     rcx
    cmp     rcx, 2048
    jl      pd_loop
    ; load our CR3 -- now all 4 GB (incl. framebuffer + our code) is ours
    mov     cr3, r11

    ; ---- Paint the framebuffer: 8 vertical color bars ----
    ; rsi = FrameBufferBase, r12 = width, r13 = height. XRGB 32bpp.
    xor     r9, r9                   ; y = 0
row_loop:
    cmp     r9, r13
    jge     done
    mov     r10, r9
    imul    r10, r12                 ; r10 = y*width
    lea     rdi, [rsi+r10*4]         ; row start ptr
    xor     rcx, rcx                 ; x = 0
col_loop:
    cmp     rcx, r12
    jge     row_next
    ; color = bar index (x*8/width) -> pick from palette
    mov     rax, rcx
    imul    rax, 8
    xor     rdx, rdx
    div     r12                      ; rax = (x*8)/width  (0..7)
    lea     r8, palette
    mov     eax, DWORD PTR [r8+rax*4]
    mov     [rdi+rcx*4], eax
    inc     rcx
    jmp     col_loop
row_next:
    inc     r9
    jmp     row_loop

done:
    ; A1 has painted and owns the machine. Spin on a benign I/O-port read so
    ; codex-vm keeps servicing exits (letting -screenshot fire) without relying
    ; on interrupts (firmware IDT is gone post-EBS). A2 replaces this tail with
    ; the jump into `opening`.
hang:
    in      al, 80h
    jmp     hang
efi_main ENDP

_TEXT ENDS

; ---- read-only data: GOP GUID and color palette ----
CONST SEGMENT
gop_guid:
    ; EFI_GRAPHICS_OUTPUT_PROTOCOL_GUID 9042A9DE-23DC-4A38-96FB-7ADED080516A
    BYTE 0DEh,0A9h,042h,090h, 0DCh,023h, 038h,04Ah, 096h,0FBh,07Ah,0DEh,0D0h,080h,051h,06Ah
palette:
    DWORD 000FF0000h  ; red
    DWORD 00000FF00h  ; green
    DWORD 0000000FFh  ; blue
    DWORD 000FFFF00h  ; yellow
    DWORD 000FF00FFh  ; magenta
    DWORD 00000FFFFh  ; cyan
    DWORD 000FFFFFFh  ; white
    DWORD 000808080h  ; gray
CONST ENDS

; ---- scratch for the memory map (uninitialized) ----
_BSS SEGMENT
mm_buf BYTE 04000h DUP (?)
_BSS ENDS

END
