# codex-vm UEFI Firmware Emulation

## Goal

Run the DevConsole (and any UEFI Codex app) inside codex-vm by
emulating the UEFI firmware protocols the guest depends on. The
guest is compiled as a PE/EFI executable (`-Efi` or `-Uefi` mode)
and expects a UEFI SystemTable at address 0x8000.

## What the guest expects

### Entry point

The PE stub stores the UEFI SystemTable pointer at 0x8000:
```asm
mov [0x8000], r15    ; SystemTable from firmware
```

Then it reads it back when calling UEFI services:
```asm
mov rax, [0x8000]    ; fetch SystemTable
mov rcx, [rax + 48]  ; ConIn protocol (SimpleTextInput)
mov rcx, [rax + 64]  ; ConOut protocol (SimpleTextOutput)
```

### UEFI protocols used

**ConOut (Simple Text Output) -- SystemTable offset 64:**
| Offset | Function          | Signature                                    |
|--------|-------------------|----------------------------------------------|
| 0      | Reset             | (This, ExtVerify) → Status                   |
| 8      | OutputString      | (This, String) → Status (UTF-16LE string)    |
| 40     | SetAttribute      | (This, Attribute) → Status                   |
| 48     | ClearScreen       | (This) → Status                              |
| 56     | SetCursorPosition | (This, Column, Row) → Status                 |
| 64     | EnableCursor      | (This, Visible) → Status                     |

**ConIn (Simple Text Input) -- SystemTable offset 48:**
| Offset | Function       | Signature                              |
|--------|----------------|----------------------------------------|
| 0      | Reset          | (This, ExtVerify) → Status             |
| 8      | ReadKeyStroke  | (This, Key) → Status                   |

**ConIn Extended (Simple Text Input Ex) -- SystemTable offset 96:**
| Offset | Function              | Signature                          |
|--------|-----------------------|------------------------------------|
| 248    | ReadKeyStrokeEx       | (This, KeyData) → Status           |

**Boot Services -- SystemTable offset 96 (for ExitBootServices):**
Not needed -- the DevConsole stays in UEFI mode.

### Call convention

All UEFI calls use Microsoft x64 ABI: RCX, RDX, R8, R9 + stack.
Return value in RAX. Callee-save: RBX, RBP, RDI, RSI, R12-R15.

The guest does: `CALL [reg + offset]` which reads a function pointer
from the protocol table and calls it. The VM needs to intercept this.

## Implementation approach: trap-page dispatch

### Memory layout for fake UEFI tables

Reserve a 4KB page at a high address (e.g., 0x70000) for fake UEFI
structures. Fill it with protocol tables whose function pointers
point into an unmapped trap page (0x71000). When the guest does
`CALL [ptr]`, it jumps to 0x71000+N. WHP delivers a memory access
exit (exec fault on unmapped page). The VM reads the fault address,
determines which UEFI function was called, handles it, and returns
to the guest.

```
0x70000: SystemTable
  +48  → 0x70100 (ConIn protocol)
  +64  → 0x70200 (ConOut protocol)

0x70100: ConIn protocol
  +0   → 0x71000 (Reset -- stub, return 0)
  +8   → 0x71008 (ReadKeyStroke -- handle in VM)

0x70200: ConOut protocol
  +0   → 0x71020 (Reset -- stub)
  +8   → 0x71028 (OutputString -- render to VGA window)
  +40  → 0x71050 (SetAttribute -- update current color)
  +48  → 0x71058 (ClearScreen -- clear VGA window)
  +56  → 0x71060 (SetCursorPosition -- move cursor)
  +64  → 0x71068 (EnableCursor -- toggle cursor visibility)
```

0x71000-0x71FFF: **Not mapped** in guest address space. Any CALL to
this range causes `WHvRunVpExitReasonMemoryAccess` with AccessType=exec.

### VM exit handling

When the VM sees an exec fault in the trap page:
1. Compute `func_id = (fault_addr - 0x71000) / 8`
2. Read arguments from guest registers (RCX, RDX, R8, R9)
3. Dispatch to the appropriate handler
4. Set RAX = 0 (EFI_SUCCESS)
5. Execute a RET by: pop return address from guest stack into RIP

### ConOut handlers

**OutputString(This, String)**: RDX points to a UTF-16LE string in
guest memory. Read the string, convert to display characters, render
to the VGA window at the current cursor position. Advance cursor.

**SetAttribute(This, Attr)**: RDX contains the attribute value
(foreground | background << 4). Store for subsequent character output.

**ClearScreen(This)**: Fill VGA buffer with spaces using current
attribute. Reset cursor to (0,0).

**SetCursorPosition(This, Col, Row)**: RDX = column, R8 = row.
Update cursor position.

### ConIn handlers

**ReadKeyStroke(This, Key)**: RDX points to a 4-byte EFI_INPUT_KEY
structure in guest memory (UINT16 ScanCode + UINT16 UnicodeChar).
If no key is queued, return EFI_NOT_READY (0x8000000000000006).
Otherwise, pop from keyboard queue, write to Key struct, return 0.

### Boot sequence

1. Compile the DevConsole as EFI: `test-compile.ps1 -Efi`
2. Load the PE/EFI binary at the standard load address
3. Before starting the VP, write the fake SystemTable at 0x70000
4. Store 0x70000 at guest address 0x8000
5. Map 0x70000-0x70FFF as guest memory (the tables)
6. Leave 0x71000-0x71FFF unmapped (the trap page)
7. Boot the guest -- it reads [0x8000] → 0x70000, calls protocol
   functions → faults into trap page → VM handles

### Complexity

| Component | Lines | Notes |
|-----------|-------|-------|
| Fake table setup | ~60 | Write SystemTable + protocols into guest memory |
| Trap dispatch | ~40 | Switch on fault address in MemoryAccess handler |
| ConOut handlers | ~80 | OutputString (UTF-16 decode + VGA render), SetAttribute, ClearScreen, SetCursorPosition |
| ConIn handler | ~30 | ReadKeyStroke with keyboard queue |
| PE loader tweaks | ~20 | Parse PE header, find entry point |
| **Total** | **~230** | |

### Testing

1. Compile a minimal UEFI hello-world (uefi-print "Hello")
2. Boot in codex-vm without -headless
3. Verify "Hello" appears in the VGA window
4. Then: compile DevConsoleBoot.codex as EFI, boot it, see the menu

### Future

- ExitBootServices (transition to bare-metal mode within same binary)
- Boot Services memory allocation (AllocatePages, FreePages)
- File system protocol (for loading source from disk in the DevConsole)
- Graphics Output Protocol (framebuffer mode for the UI launcher)
