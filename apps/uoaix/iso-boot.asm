; El Torito no-emulation boot. The builder supplies 2048-byte payload blocks.
; BIOS E820 supplies the contiguous usable region above 1 MiB to the runtime.
[bits 16]
[org 0x7c00]
%ifndef PAYLOAD_LBA
%error PAYLOAD_LBA required
%endif
%ifndef PAYLOAD_BLOCKS
%error PAYLOAD_BLOCKS required
%endif
start:
    cli
    xor ax, ax
    mov ds, ax
    mov es, ax
    mov ss, ax
    mov sp, 0x7c00
    sti
    cld
    mov [drive], dl
    xor ebx, ebx
    mov dword [0xfe8], 0
    mov dword [0xfec], 0
.memory:
    dec word [memory_fuel]
    jz .error
    mov eax, 0xe820
    mov edx, 0x534d4150
    mov ecx, 24
    mov di, 0x6000
    mov dword [di+20], 1
    int 0x15
    jc .memory_done
    cmp eax, 0x534d4150
    jne .error
    cmp ecx, 20
    jb .error
    cmp ecx, 24
    jb .memory_type
    test dword [di+20], 1
    jz .next_memory
.memory_type:
    cmp dword [di+16], 1
    jne .next_memory
    cmp dword [di+4], 0
    jne .next_memory
    cmp dword [di], 0x100000
    ja .next_memory
    cmp dword [di+12], 0
    jne .next_memory
    mov eax, [di]
    add eax, [di+8]
    jc .next_memory
    cmp eax, 0x30000000
    jb .next_memory
    cmp eax, 0xc0000000
    jbe .store_memory
    mov eax, 0xc0000000
.store_memory:
    and eax, 0xfffff000
    mov [0xfe8], eax
.next_memory:
    test ebx, ebx
    jnz .memory
.memory_done:
    cmp dword [0xfe8], 0
    je .error
    mov ax, 0x2401
    int 0x15
    in al, 0x92
    or al, 2
    and al, 0xfe
    out 0x92, al
    mov ecx, PAYLOAD_BLOCKS
.read:
    test ecx, ecx
    jz .loaded
    push ecx
    cmp ecx, 16
    jbe .count
    mov cx, 16
.count:
    mov [dap_count], cx
    mov [transfer_count], cx
    mov ah, 0x42
    mov dl, [drive]
    mov si, dap
    int 0x13
    jc .error
    mov eax, [destination]
    mov [gdt_dst+2], ax
    shr eax, 16
    mov [gdt_dst+4], al
    mov [gdt_dst+7], ah
    mov cx, [transfer_count]
    shl cx, 10
    mov ah, 0x87
    mov si, gdt_block
    int 0x15
    jc .error
    movzx eax, word [transfer_count]
    add [dap_lba], eax
    mov edx, eax
    shl edx, 11
    add [destination], edx
    pop ecx
    sub ecx, eax
    jmp .read
.loaded:
    cli
    lgdt [pm_gdt_desc]
    mov eax, cr0
    or al, 1
    mov cr0, eax
    jmp 0x08:protected
.error:
    mov dx, 0x3f8
    mov al, 'E'
    out dx, al
    mov dx, 0xf4
    mov eax, 1
    out dx, eax
    cli
.halt:
    hlt
    jmp .halt
[bits 32]
protected:
    mov ax, 0x10
    mov ds, ax
    mov es, ax
    mov fs, ax
    mov gs, ax
    mov ss, ax
    mov esp, 0x7c00
    mov eax, 0x100020
    jmp eax
[bits 16]
align 8
drive: db 0
memory_fuel: dw 257
transfer_count: dw 0
destination: dd 0x100000
gdt_block: dq 0, 0
gdt_src: db 0xff,0xff,0,0x80,0,0x93,0,0
gdt_dst: db 0xff,0xff,0,0,0x10,0x93,0,0
    dq 0,0
pm_gdt:
    dq 0
    db 0xff,0xff,0,0,0,0x9a,0xcf,0
    db 0xff,0xff,0,0,0,0x92,0xcf,0
pm_gdt_desc:
    dw pm_gdt_desc-pm_gdt-1
    dd pm_gdt
align 4
dap: db 16,0
dap_count: dw 0
    dw 0x8000,0
dap_lba: dd PAYLOAD_LBA,0
times 2048-($-$$) db 0
