# UEFI 2.10 Boot Services - Technical Reference Summary

Source: https://uefi.org/specs/UEFI/2.10/07_Services_Boot_Services.html

---

## Memory Allocation Services

### EFI_BOOT_SERVICES.AllocatePages()

Allocates memory pages from the system.

```c
typedef
EFI_STATUS
(EFIAPI *EFI_ALLOCATE_PAGES) (
   IN EFI_ALLOCATE_TYPE            Type,
   IN EFI_MEMORY_TYPE              MemoryType,
   IN UINTN                        Pages,
   IN OUT EFI_PHYSICAL_ADDRESS     *Memory
   );
```

**Parameters:**
- `Type` -- allocation strategy (see EFI_ALLOCATE_TYPE)
- `MemoryType` -- type of memory to allocate (see EFI_MEMORY_TYPE)
- `Pages` -- number of contiguous 4 KiB pages to allocate
- `Memory` -- pointer to physical address; on output set to base of allocated range

**Status Codes:**
| Code | Meaning |
|------|---------|
| EFI_SUCCESS | Pages allocated |
| EFI_OUT_OF_RESOURCES | Pages could not be allocated |
| EFI_INVALID_PARAMETER | Type is not a valid EFI_ALLOCATE_TYPE |
| EFI_INVALID_PARAMETER | MemoryType is in range EfiMaxMemoryType..0x6FFFFFFF |
| EFI_INVALID_PARAMETER | MemoryType is EfiPersistentMemory or EfiUnacceptedMemory |
| EFI_INVALID_PARAMETER | Memory is NULL |
| EFI_NOT_FOUND | Requested pages could not be found |

---

### EFI_ALLOCATE_TYPE

```c
typedef enum {
   AllocateAnyPages,     // allocate any available range
   AllocateMaxAddress,   // allocate below address in *Memory
   AllocateAddress,      // allocate at exact address in *Memory
   MaxAllocateType
} EFI_ALLOCATE_TYPE;
```

---

### EFI_MEMORY_TYPE

```c
typedef enum {
   EfiReservedMemoryType,       // 0  - not usable
   EfiLoaderCode,               // 1  - UEFI app code
   EfiLoaderData,               // 2  - UEFI app data (default for apps)
   EfiBootServicesCode,         // 3  - boot services driver code
   EfiBootServicesData,         // 4  - boot services driver data
   EfiRuntimeServicesCode,      // 5  - runtime driver code
   EfiRuntimeServicesData,      // 6  - runtime driver data
   EfiConventionalMemory,       // 7  - free unallocated memory
   EfiUnusableMemory,           // 8  - memory with errors
   EfiACPIReclaimMemory,        // 9  - ACPI tables (reclaimable)
   EfiACPIMemoryNVS,            // 10 - ACPI NVS memory (reserved)
   EfiMemoryMappedIO,           // 11 - MMIO
   EfiMemoryMappedIOPortSpace,  // 12 - MMIO port space
   EfiPalCode,                  // 13 - PAL code (Itanium)
   EfiPersistentMemory,         // 14 - byte-addressable non-volatile
   EfiUnacceptedMemoryType,     // 15 - unaccepted memory (TDX/SEV)
   EfiMaxMemoryType             // 16 - sentinel
} EFI_MEMORY_TYPE;
```

**Allocation guidelines:**
- UEFI apps/OS loaders: use `EfiLoaderData`
- Boot services drivers: use `EfiBootServicesData`
- Runtime drivers: use `EfiRuntimeServicesData`
- Must NOT allocate: `EfiReservedMemoryType`, `EfiMemoryMappedIO`, `EfiUnacceptedMemoryType`
- OEM reserved range: 0x70000000..0x7FFFFFFF
- OS loader reserved range: 0x80000000..0xFFFFFFFF

```c
typedef UINT64 EFI_PHYSICAL_ADDRESS;
```

---

### EFI_BOOT_SERVICES.FreePages()

```c
typedef
EFI_STATUS
(EFIAPI *EFI_FREE_PAGES) (
   IN EFI_PHYSICAL_ADDRESS    Memory,
   IN UINTN                   Pages
);
```

**Status Codes:**
| Code | Meaning |
|------|---------|
| EFI_SUCCESS | Pages freed |
| EFI_NOT_FOUND | Pages were not allocated with AllocatePages() |
| EFI_INVALID_PARAMETER | Memory not page-aligned or Pages invalid |

---

### EFI_BOOT_SERVICES.AllocatePool()

```c
typedef
EFI_STATUS
(EFIAPI *EFI_ALLOCATE_POOL) (
   IN EFI_MEMORY_TYPE         PoolType,
   IN UINTN                   Size,
   OUT VOID                   **Buffer
   );
```

- Allocates `Size` bytes from pool of type `PoolType`
- All allocations are 8-byte aligned
- Internally allocates pages from EfiConventionalMemory as needed

**Status Codes:**
| Code | Meaning |
|------|---------|
| EFI_SUCCESS | Bytes allocated |
| EFI_OUT_OF_RESOURCES | Pool could not be allocated |
| EFI_INVALID_PARAMETER | PoolType in range EfiMaxMemoryType..0x6FFFFFFF |
| EFI_INVALID_PARAMETER | PoolType is EfiPersistentMemory |
| EFI_INVALID_PARAMETER | Buffer is NULL |

---

### EFI_BOOT_SERVICES.FreePool()

```c
typedef
EFI_STATUS
(EFIAPI *EFI_FREE_POOL) (
   IN VOID           *Buffer
   );
```

Returns pool memory to EfiConventionalMemory. Buffer must have been allocated by AllocatePool().

---

## EFI_LOADED_IMAGE_PROTOCOL (Chapter 9)

Source: https://uefi.org/specs/UEFI/2.10/09_Protocols_EFI_Loaded_Image.html

### GUID

```c
#define EFI_LOADED_IMAGE_PROTOCOL_GUID \
  {0x5B1B31A1, 0x9562, 0x11d2, \
    {0x8E, 0x3F, 0x00, 0xA0, 0xC9, 0x69, 0x72, 0x3B}}

#define EFI_LOADED_IMAGE_PROTOCOL_REVISION 0x1000
```

### Struct

```c
typedef struct {
   UINT32                        Revision;
   EFI_HANDLE                    ParentHandle;
   EFI_System_Table              *SystemTable;

   // Source location of the image
   EFI_HANDLE                    DeviceHandle;
   EFI_DEVICE_PATH_PROTOCOL      *FilePath;
   VOID                          *Reserved;

   // Image's load options
   UINT32                        LoadOptionsSize;
   VOID                          *LoadOptions;

   // Location where image was loaded
   VOID                          *ImageBase;
   UINT64                        ImageSize;
   EFI_MEMORY_TYPE               ImageCodeType;
   EFI_MEMORY_TYPE               ImageDataType;
   EFI_IMAGE_UNLOAD              Unload;
} EFI_LOADED_IMAGE_PROTOCOL;
```

**Fields:**
| Field | Type | Description |
|-------|------|-------------|
| Revision | UINT32 | Protocol revision |
| ParentHandle | EFI_HANDLE | Parent image handle (NULL if loaded by boot manager) |
| SystemTable | EFI_System_Table* | Image's EFI system table pointer |
| DeviceHandle | EFI_HANDLE | Device handle image was loaded from |
| FilePath | EFI_DEVICE_PATH_PROTOCOL* | File path specific to DeviceHandle |
| Reserved | VOID* | Reserved, DO NOT USE |
| LoadOptionsSize | UINT32 | Size in bytes of LoadOptions |
| LoadOptions | VOID* | Pointer to image's binary load options |
| ImageBase | VOID* | Base address where image was loaded |
| ImageSize | UINT64 | Size in bytes of loaded image |
| ImageCodeType | EFI_MEMORY_TYPE | Memory type for code sections |
| ImageDataType | EFI_MEMORY_TYPE | Memory type for data sections |
| Unload | EFI_IMAGE_UNLOAD | Unload callback function |

### EFI_IMAGE_UNLOAD

```c
typedef
EFI_STATUS
(EFIAPI *EFI_IMAGE_UNLOAD) (
  IN EFI_HANDLE               ImageHandle
  );
```

---

## EFI_DEVICE_PATH_PROTOCOL (Chapter 10)

Source: https://uefi.org/specs/UEFI/2.10/10_Protocols_Device_Path_Protocol.html

### GUID

```c
#define EFI_DEVICE_PATH_PROTOCOL_GUID \
  {0x09576e91, 0x6d3f, 0x11d2, \
    {0x8e, 0x39, 0x00, 0xa0, 0xc9, 0x69, 0x72, 0x3b}}
```

### Struct

```c
typedef struct _EFI_DEVICE_PATH_PROTOCOL {
  UINT8           Type;
  UINT8           SubType;
  UINT8           Length[2];
} EFI_DEVICE_PATH_PROTOCOL;
```

**Fields:**
| Field | Offset | Size | Description |
|-------|--------|------|-------------|
| Type | 0 | 1 | Device path type (0x01=Hardware, 0x02=ACPI, 0x03=Messaging, 0x04=Media, 0x05=BIOS Boot, 0x7F=End) |
| SubType | 1 | 1 | Sub-type within the type |
| Length | 2 | 2 | Total length of this node in bytes (little-endian), including this header |

Device paths are variable-length linked lists of these nodes. The end node has Type=0x7F, SubType=0xFF.
