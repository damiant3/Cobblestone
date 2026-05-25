# UEFI 2.10 Media Access Protocols - Technical Reference Summary

Source: https://uefi.org/specs/UEFI/2.10/13_Protocols_Media_Access.html

---

## EFI_BLOCK_IO_PROTOCOL

Provides control over block devices for mass storage access.

### GUID

```c
#define EFI_BLOCK_IO_PROTOCOL_GUID \
 {0x964e5b21, 0x6459, 0x11d2, \
  {0x8e, 0x39, 0x00, 0xa0, 0xc9, 0x69, 0x72, 0x3b}}

#define EFI_BLOCK_IO_PROTOCOL_REVISION2   0x00020001
#define EFI_BLOCK_IO_PROTOCOL_REVISION3   ((2<<16) | (31))
```

### Struct

```c
typedef struct _EFI_BLOCK_IO_PROTOCOL {
  UINT64                         Revision;
  EFI_BLOCK_IO_MEDIA             *Media;
  EFI_BLOCK_RESET                Reset;
  EFI_BLOCK_READ                 ReadBlocks;
  EFI_BLOCK_WRITE                WriteBlocks;
  EFI_BLOCK_FLUSH                FlushBlocks;
} EFI_BLOCK_IO_PROTOCOL;
```

### EFI_BLOCK_IO_MEDIA

```c
typedef UINT64 EFI_LBA;

typedef struct {
  UINT32                    MediaId;
  BOOLEAN                   RemovableMedia;
  BOOLEAN                   MediaPresent;
  BOOLEAN                   LogicalPartition;
  BOOLEAN                   ReadOnly;
  BOOLEAN                   WriteCaching;
  UINT32                    BlockSize;
  UINT32                    IoAlign;
  EFI_LBA                   LastBlock;
  EFI_LBA                   LowestAlignedLba;                  // Rev 2+
  UINT32                    LogicalBlocksPerPhysicalBlock;      // Rev 2+
  UINT32                    OptimalTransferLengthGranularity;   // Rev 3+
} EFI_BLOCK_IO_MEDIA;
```

**Fields:**
| Field | Type | Description |
|-------|------|-------------|
| MediaId | UINT32 | Current media ID; changes when media changes |
| RemovableMedia | BOOLEAN | TRUE if removable |
| MediaPresent | BOOLEAN | TRUE if media currently present |
| LogicalPartition | BOOLEAN | TRUE if this handle represents a partition |
| ReadOnly | BOOLEAN | TRUE if write-protected |
| WriteCaching | BOOLEAN | TRUE if WriteBlocks() caches writes |
| BlockSize | UINT32 | Bytes per logical block |
| IoAlign | UINT32 | Buffer alignment requirement (0 or 1 = any; else power-of-2) |
| LastBlock | EFI_LBA | Last valid LBA on device |
| LowestAlignedLba | EFI_LBA | First LBA aligned to physical block boundary (Rev 2+) |
| LogicalBlocksPerPhysicalBlock | UINT32 | Logical blocks per physical block (Rev 2+, 0 = 1:1) |
| OptimalTransferLengthGranularity | UINT32 | Optimal transfer granularity in logical blocks (Rev 3+) |

### Reset()

```c
typedef
EFI_STATUS
(EFIAPI *EFI_BLOCK_RESET) (
  IN EFI_BLOCK_IO_PROTOCOL    *This,
  IN BOOLEAN                  ExtendedVerification
  );
```

| Code | Meaning |
|------|---------|
| EFI_SUCCESS | Device reset |
| EFI_DEVICE_ERROR | Device not functioning, could not be reset |

### ReadBlocks()

```c
typedef
EFI_STATUS
(EFIAPI *EFI_BLOCK_READ) (
  IN EFI_BLOCK_IO_PROTOCOL    *This,
  IN UINT32                   MediaId,
  IN EFI_LBA                  LBA,
  IN UINTN                    BufferSize,
  OUT VOID                    *Buffer
  );
```

- `BufferSize` must be a multiple of `BlockSize`
- Must return EFI_NO_MEDIA or EFI_MEDIA_CHANGED even if other params are invalid

| Code | Meaning |
|------|---------|
| EFI_SUCCESS | Data read correctly |
| EFI_DEVICE_ERROR | Device error during read |
| EFI_NO_MEDIA | No media in device |
| EFI_MEDIA_CHANGED | MediaId mismatch |
| EFI_BAD_BUFFER_SIZE | BufferSize not a multiple of BlockSize |
| EFI_INVALID_PARAMETER | Invalid LBAs or buffer alignment |

### WriteBlocks()

```c
typedef
EFI_STATUS
(EFIAPI *EFI_BLOCK_WRITE) (
  IN EFI_BLOCK_IO_PROTOCOL       *This,
  IN UINT32                      MediaId,
  IN EFI_LBA                     LBA,
  IN UINTN                       BufferSize,
  IN VOID                        *Buffer
  );
```

| Code | Meaning |
|------|---------|
| EFI_SUCCESS | Data written correctly |
| EFI_WRITE_PROTECTED | Device cannot be written to |
| EFI_NO_MEDIA | No media in device |
| EFI_MEDIA_CHANGED | MediaId mismatch |
| EFI_DEVICE_ERROR | Device error during write |
| EFI_BAD_BUFFER_SIZE | BufferSize not a multiple of BlockSize |
| EFI_INVALID_PARAMETER | Invalid LBAs or buffer alignment |

### FlushBlocks()

```c
typedef
EFI_STATUS
(EFIAPI *EFI_BLOCK_FLUSH) (
  IN EFI_BLOCK_IO_PROTOCOL    *This
  );
```

| Code | Meaning |
|------|---------|
| EFI_SUCCESS | All data flushed |
| EFI_DEVICE_ERROR | Device error during flush |
| EFI_NO_MEDIA | No media in device |

---

## EFI_SIMPLE_FILE_SYSTEM_PROTOCOL

Minimal interface for file-type access to a device. Firmware auto-creates handles for FAT12/FAT16/FAT32 volumes.

### GUID

```c
#define EFI_SIMPLE_FILE_SYSTEM_PROTOCOL_GUID \
 {0x0964e5b22, 0x6459, 0x11d2, \
  {0x8e, 0x39, 0x00, 0xa0, 0xc9, 0x69, 0x72, 0x3b}}

#define EFI_SIMPLE_FILE_SYSTEM_PROTOCOL_REVISION 0x00010000
```

### Struct

```c
typedef struct _EFI_SIMPLE_FILE_SYSTEM_PROTOCOL {
 UINT64                                         Revision;
 EFI_SIMPLE_FILE_SYSTEM_PROTOCOL_OPEN_VOLUME    OpenVolume;
} EFI_SIMPLE_FILE_SYSTEM_PROTOCOL;
```

### OpenVolume()

```c
typedef
EFI_STATUS
(EFIAPI *EFI_SIMPLE_FILE_SYSTEM_PROTOCOL_OPEN_VOLUME) (
  IN EFI_SIMPLE_FILE_SYSTEM_PROTOCOL      *This,
  OUT EFI_FILE_PROTOCOL                   **Root
  );
```

Returns an EFI_FILE_PROTOCOL handle to the root directory of the volume.

| Code | Meaning |
|------|---------|
| EFI_SUCCESS | Volume opened |
| EFI_UNSUPPORTED | Volume does not support requested file system type |
| EFI_NO_MEDIA | No medium |
| EFI_DEVICE_ERROR | Device error |
| EFI_VOLUME_CORRUPTED | File system structures corrupted |
| EFI_ACCESS_DENIED | Access denied |
| EFI_OUT_OF_RESOURCES | Could not open |
| EFI_MEDIA_CHANGED | Medium changed; reopen required |

---

## EFI_FILE_PROTOCOL

Provides file-based access to supported file systems.

### Revision Numbers

```c
#define EFI_FILE_PROTOCOL_REVISION           0x00010000
#define EFI_FILE_PROTOCOL_REVISION2          0x00020000
#define EFI_FILE_PROTOCOL_LATEST_REVISION    EFI_FILE_PROTOCOL_REVISION2
```

### Struct

```c
typedef struct _EFI_FILE_PROTOCOL {
  UINT64                          Revision;
  EFI_FILE_OPEN                   Open;
  EFI_FILE_CLOSE                  Close;
  EFI_FILE_DELETE                 Delete;
  EFI_FILE_READ                   Read;
  EFI_FILE_WRITE                  Write;
  EFI_FILE_GET_POSITION           GetPosition;
  EFI_FILE_SET_POSITION           SetPosition;
  EFI_FILE_GET_INFO               GetInfo;
  EFI_FILE_SET_INFO               SetInfo;
  EFI_FILE_FLUSH                  Flush;
  EFI_FILE_OPEN_EX                OpenEx;    // Rev 2
  EFI_FILE_READ_EX                ReadEx;    // Rev 2
  EFI_FILE_WRITE_EX               WriteEx;   // Rev 2
  EFI_FILE_FLUSH_EX               FlushEx;   // Rev 2
} EFI_FILE_PROTOCOL;
```

### Open Mode Constants

```c
#define EFI_FILE_MODE_READ       0x0000000000000001
#define EFI_FILE_MODE_WRITE      0x0000000000000002
#define EFI_FILE_MODE_CREATE     0x8000000000000000
```

Valid combinations: Read | Read+Write | Create+Read+Write

### File Attribute Constants

```c
#define EFI_FILE_READ_ONLY       0x0000000000000001
#define EFI_FILE_HIDDEN          0x0000000000000002
#define EFI_FILE_SYSTEM          0x0000000000000004
#define EFI_FILE_RESERVED        0x0000000000000008
#define EFI_FILE_DIRECTORY       0x0000000000000010
#define EFI_FILE_ARCHIVE         0x0000000000000020
#define EFI_FILE_VALID_ATTR      0x0000000000000037
```

### Open()

```c
typedef
EFI_STATUS
(EFIAPI *EFI_FILE_OPEN) (
  IN EFI_FILE_PROTOCOL         *This,
  OUT EFI_FILE_PROTOCOL        **NewHandle,
  IN CHAR16                    *FileName,
  IN UINT64                    OpenMode,
  IN UINT64                    Attributes
  );
```

- `FileName` may contain path modifiers: `\`, `.`, `..`
- `Attributes` only used when EFI_FILE_MODE_CREATE is set

| Code | Meaning |
|------|---------|
| EFI_SUCCESS | File opened |
| EFI_NOT_FOUND | File not found |
| EFI_NO_MEDIA | No medium |
| EFI_MEDIA_CHANGED | Medium changed |
| EFI_DEVICE_ERROR | Device error |
| EFI_VOLUME_CORRUPTED | FS corrupted |
| EFI_WRITE_PROTECTED | Write-protected |
| EFI_ACCESS_DENIED | Access denied |
| EFI_OUT_OF_RESOURCES | Resources exhausted |
| EFI_VOLUME_FULL | Volume full |

### Close()

```c
typedef
EFI_STATUS
(EFIAPI *EFI_FILE_CLOSE) (
  IN EFI_FILE_PROTOCOL      *This
  );
```

Always returns EFI_SUCCESS.

### Delete()

```c
typedef
EFI_STATUS
(EFIAPI *EFI_FILE_DELETE) (
  IN EFI_FILE_PROTOCOL      *This
  );
```

Closes and deletes. Handle always closed even on failure.

| Code | Meaning |
|------|---------|
| EFI_SUCCESS | Deleted |
| EFI_WARN_DELETE_FAILURE | Handle closed but file not deleted |

### Read()

```c
typedef
EFI_STATUS
(EFIAPI *EFI_FILE_READ) (
  IN EFI_FILE_PROTOCOL        *This,
  IN OUT UINTN                *BufferSize,
  OUT VOID                    *Buffer
  );
```

- For files: reads bytes at current position, advances position
- For directories: reads one EFI_FILE_INFO entry per call; zero-length = no more entries

| Code | Meaning |
|------|---------|
| EFI_SUCCESS | Data read |
| EFI_NO_MEDIA | No medium |
| EFI_DEVICE_ERROR | Device error / deleted file / past EOF |
| EFI_VOLUME_CORRUPTED | FS corrupted |
| EFI_BUFFER_TOO_SMALL | Buffer too small for directory entry (BufferSize updated) |

### Write()

```c
typedef
EFI_STATUS
(EFIAPI *EFI_FILE_WRITE) (
  IN EFI_FILE_PROTOCOL         *This,
  IN OUT UINTN                 *BufferSize,
  IN VOID                      *Buffer
  );
```

- File auto-grows as needed
- Directory writes not supported

| Code | Meaning |
|------|---------|
| EFI_SUCCESS | Data written |
| EFI_UNSUPPORTED | Write to directory |
| EFI_NO_MEDIA | No medium |
| EFI_DEVICE_ERROR | Device error / deleted file |
| EFI_VOLUME_CORRUPTED | FS corrupted |
| EFI_WRITE_PROTECTED | Write-protected |
| EFI_ACCESS_DENIED | Opened read-only |
| EFI_VOLUME_FULL | Volume full |

### SetPosition()

```c
typedef
EFI_STATUS
(EFIAPI *EFI_FILE_SET_POSITION) (
   IN EFI_FILE_PROTOCOL      *This,
   IN UINT64                 Position
   );
```

- Position 0xFFFFFFFFFFFFFFFF = seek to end
- Seeking past EOF allowed (write will grow file)
- Directories: only position 0 is valid (restarts enumeration)

### GetPosition()

```c
typedef
EFI_STATUS
(EFIAPI *EFI_FILE_GET_POSITION) (
  IN EFI_FILE_PROTOCOL        *This,
  OUT UINT64                  *Position
  );
```

Not supported on directories (returns EFI_UNSUPPORTED).

### GetInfo()

```c
typedef
EFI_STATUS
(EFIAPI *EFI_FILE_GET_INFO) (
  IN EFI_FILE_PROTOCOL        *This,
  IN EFI_GUID                 *InformationType,
  IN OUT UINTN                *BufferSize,
  OUT VOID                    *Buffer
  );
```

Pass `EFI_FILE_INFO_ID` for file info or `EFI_FILE_SYSTEM_INFO_ID` for volume info.

| Code | Meaning |
|------|---------|
| EFI_SUCCESS | Info returned |
| EFI_UNSUPPORTED | Unknown InformationType |
| EFI_NO_MEDIA | No medium |
| EFI_DEVICE_ERROR | Device error |
| EFI_VOLUME_CORRUPTED | FS corrupted |
| EFI_BUFFER_TOO_SMALL | Buffer too small (BufferSize updated) |

### SetInfo()

```c
typedef
EFI_STATUS
(EFIAPI *EFI_FILE_SET_INFO) (
  IN EFI_FILE_PROTOCOL        *This,
  IN EFI_GUID                 *InformationType,
  IN UINTN                    BufferSize,
  IN VOID                     *Buffer
  );
```

### Flush()

```c
typedef
EFI_STATUS
(EFIAPI *EFI_FILE_FLUSH) (
  IN EFI_FILE_PROTOCOL        *This
  );
```

| Code | Meaning |
|------|---------|
| EFI_SUCCESS | Data flushed |
| EFI_NO_MEDIA | No medium |
| EFI_DEVICE_ERROR | Device error |
| EFI_VOLUME_CORRUPTED | FS corrupted |
| EFI_WRITE_PROTECTED | Write-protected |
| EFI_ACCESS_DENIED | Opened read-only |
| EFI_VOLUME_FULL | Volume full |

---

## EFI_FILE_INFO

### GUID

```c
#define EFI_FILE_INFO_ID \
 {0x09576e92, 0x6d3f, 0x11d2, \
  {0x8e39, 0x00, 0xa0, 0xc9, 0x69, 0x72, 0x3b}}
```

### Struct

```c
typedef struct {
  UINT64                         Size;
  UINT64                         FileSize;
  UINT64                         PhysicalSize;
  EFI_TIME                       CreateTime;
  EFI_TIME                       LastAccessTime;
  EFI_TIME                       ModificationTime;
  UINT64                         Attribute;
  CHAR16                         FileName[];
} EFI_FILE_INFO;
```

| Field | Type | Description |
|-------|------|-------------|
| Size | UINT64 | Size of this EFI_FILE_INFO structure including FileName |
| FileSize | UINT64 | File size in bytes |
| PhysicalSize | UINT64 | Physical space consumed on volume |
| CreateTime | EFI_TIME | Creation time |
| LastAccessTime | EFI_TIME | Last access time |
| ModificationTime | EFI_TIME | Last modification time |
| Attribute | UINT64 | File attribute bits (see constants above) |
| FileName | CHAR16[] | Null-terminated file name (empty string for root) |

**SetInfo rules:**
- Directory FileSize is read-only (determined by contents)
- PhysicalSize is read-only
- EFI_FILE_DIRECTORY attribute cannot be changed
- Zero timestamps are ignored (not updated)

---

## EFI_FILE_SYSTEM_INFO

### GUID

```c
#define EFI_FILE_SYSTEM_INFO_ID \
 {0x09576e93, 0x6d3f, 0x11d2, \
  {0x8e39, 0x00, 0xa0, 0xc9, 0x69, 0x72, 0x3b}}
```

### Struct

```c
typedef struct {
  UINT64                               Size;
  BOOLEAN                              ReadOnly;
  UINT64                               VolumeSize;
  UINT64                               FreeSpace;
  UINT32                               BlockSize;
  CHAR16                               VolumeLabel[];
} EFI_FILE_SYSTEM_INFO;
```

---

## Async I/O Token (Rev 2)

```c
typedef struct {
  EFI_EVENT                         Event;
  EFI_STATUS                        Status;
  UINTN                             BufferSize;
  VOID                              *Buffer;
} EFI_FILE_IO_TOKEN;
```

Used by OpenEx(), ReadEx(), WriteEx(), FlushEx(). If Event is NULL, blocking I/O is performed.
