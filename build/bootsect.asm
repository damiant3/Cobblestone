; Codex boot sector -- loads CDX to 0x100000, enters 32-bit protected mode,
; jumps to CDX trampoline at 0x100020 (which handles 64-bit transition).
; Assemble: nasm -f bin bootsect.asm -o bootsect.bin
; Image builder patches sector_count (use listing to find offset).

[bits 16]
[org 0x7C00]

start:
    xor ax, ax
    mov ds, ax
    mov es, ax
    mov ss, ax
    mov sp, 0x7C00
    mov [boot_drive], dl
    mov ax, 0x0003
    int 0x10
    mov ax, 0x0E43         ; 'C' = alive
    int 0x10

    mov ax, 0x2401
    int 0x15
    jnc .a20ok
    in al, 0x92
    or al, 2
    and al, 0xFE
    out 0x92, al
.a20ok:
    mov dword [dest_addr], 0x100000
    movzx ecx, word [sector_count]
    test ecx, ecx
    jz .err

.rdloop:
    test ecx, ecx
    jz .loaded
    push ecx
    cmp ecx, 64
    jbe .cok
    mov cx, 64
.cok:
    mov [dap_count], cx
    mov ah, 0x42
    mov dl, [boot_drive]
    mov si, dap
    int 0x13
    jc .derr

    mov word [gdt_src], 0xFFFF
    mov byte [gdt_src+2], 0x00
    mov byte [gdt_src+3], 0x80
    mov byte [gdt_src+4], 0x00
    mov byte [gdt_src+5], 0x93
    mov word [gdt_src+6], 0x0000
    mov eax, [dest_addr]
    mov word [gdt_dst], 0xFFFF
    mov byte [gdt_dst+2], al
    shr eax, 8
    mov byte [gdt_dst+3], al
    shr eax, 8
    mov byte [gdt_dst+4], al
    mov byte [gdt_dst+5], 0x93
    shr eax, 8
    mov byte [gdt_dst+6], al
    mov byte [gdt_dst+7], 0x00
    movzx ecx, word [dap_count]
    shl cx, 8
    mov ah, 0x87
    mov si, gdt_block
    int 0x15
    jc .cerr

    movzx eax, word [dap_count]
    shl eax, 9
    add [dest_addr], eax
    movzx eax, word [dap_count]
    add [dap_lba_lo], eax
    pop ecx
    sub ecx, eax
    jmp .rdloop

.loaded:
    mov ax, 0x0E2E         ; '.' = loaded, switching
    int 0x10
    cli
    lgdt [pm_gdt_desc]
    mov eax, cr0
    or al, 1
    mov cr0, eax
    jmp 0x08:.pm32

[bits 32]
.pm32:
    mov ax, 0x10
    mov ds, ax
    mov es, ax
    mov fs, ax
    mov gs, ax
    mov ss, ax
    mov esp, 0x7C00
    mov eax, 0x00100020
    jmp eax

[bits 16]
.err:
    mov ax, 0x0E21         ; '!' = sector_count=0
    int 0x10
    cli
    hlt
.derr:
    mov ax, 0x0E44         ; 'D' = disk read error
    int 0x10
    cli
    hlt
.cerr:
    pop ecx
    mov ax, 0x0E58         ; 'X' = copy error
    int 0x10
    cli
    hlt

; ============================================================
boot_drive:   db 0
dest_addr:    dd 0
sector_count: dw 0

gdt_block: dq 0, 0
gdt_src:   dq 0
gdt_dst:   dq 0
           dq 0, 0

pm_gdt:
    dq 0
    db 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x9A, 0xCF, 0x00  ; 32-bit code
    db 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x92, 0xCF, 0x00  ; 32-bit data
pm_gdt_desc:
    dw pm_gdt_desc - pm_gdt - 1
    dd pm_gdt

dap:    db 16, 0
dap_count: dw 0
    dw 0x8000, 0x0000
dap_lba_lo: dd 1, 0

    times 510-($-$$) db 0
    dw 0xAA55
