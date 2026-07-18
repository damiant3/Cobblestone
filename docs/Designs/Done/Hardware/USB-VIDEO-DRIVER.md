# USB Video Class (UVC) Kernel Driver

**Created**: 2026-05-23
**Status**: Active — design phase
**File**: `codex/os/kernel/UsbVideo.codex`

## Motivation

The Codex OS needs camera input for agent vision, identity verification,
and UI applications. The USB Video Class (UVC) is a standard USB device
class — no vendor-specific drivers needed. Most webcams, including the
one on the test bench, speak UVC.

The driver runs in the kernel, talks to xHCI directly, and receives
video frames on bare metal. No host OS involved. In codex-vm, the same
driver talks to the emulated xHCI which provides a test pattern; on
real hardware, it talks to real silicon.

## Architecture

```
Camera (USB) → xHCI hardware → Xhci.codex → UsbVideo.codex → framebuffer
                                                              (VgaGraphics.codex)
```

The UVC driver sits between the USB transport layer (Xhci.codex) and
the display layer (VgaGraphics.codex). It:

1. Discovers UVC devices via interface class 14 (video)
2. Parses UVC-specific descriptors (VideoControl, VideoStreaming)
3. Negotiates stream format via Probe/Commit control transfers
4. Switches to the active alternate interface
5. Reads isochronous frames from the streaming endpoint
6. Converts YUYV pixel data to RGB for display

## UVC Device Model

A UVC device has two interfaces via an Interface Association Descriptor:

```
Interface 0: VideoControl (class 14, subclass 1)
  - Camera Terminal (input)
  - Processing Unit (brightness, contrast, etc.)
  - Output Terminal (USB streaming)

Interface 1: VideoStreaming (class 14, subclass 2)
  - Alt Setting 0: zero bandwidth (no endpoint)
  - Alt Setting 1: active (isochronous IN endpoint)
```

The driver selects Alt Setting 1 to start streaming.

## UVC Control Request Codes

| Request | bRequest | Direction |
|---------|----------|-----------|
| SET_CUR | 0x01 | Host → Device |
| GET_CUR | 0x81 | Device → Host |
| GET_MIN | 0x82 | Device → Host |
| GET_MAX | 0x83 | Device → Host |
| GET_RES | 0x84 | Device → Host |
| GET_DEF | 0x87 | Device → Host |

bmRequestType: GET = 0xA1 (class, interface, D→H), SET = 0x21 (H→D).
wValue high byte = control selector, low = 0. wIndex high = unit/terminal
ID, low = interface number.

**Processing Unit selectors**: PU_BRIGHTNESS = 0x02, PU_CONTRAST = 0x03,
PU_GAIN = 0x04, PU_SATURATION = 0x07, PU_SHARPNESS = 0x08.

**Camera Terminal selectors**: CT_AE_MODE = 0x02, CT_EXPOSURE_TIME = 0x04,
CT_FOCUS_ABS = 0x06.

**Streaming selectors**: VS_PROBE_CONTROL = 0x01, VS_COMMIT_CONTROL = 0x02.

## UVC Descriptor Types (class-specific, bDescriptorType = 0x24)

| Subtype | Value | Interface | Purpose |
|---------|-------|-----------|---------|
| VC_HEADER | 0x01 | Control | UVC version, clock frequency |
| VC_INPUT_TERMINAL | 0x02 | Control | Camera sensor (type 0x0201) |
| VC_OUTPUT_TERMINAL | 0x03 | Control | USB streaming output |
| VC_PROCESSING_UNIT | 0x05 | Control | Image adjustments |
| VS_INPUT_HEADER | 0x01 | Streaming | Format count, endpoint address |
| VS_FORMAT_UNCOMPRESSED | 0x04 | Streaming | YUYV/NV12 format GUID |
| VS_FRAME_UNCOMPRESSED | 0x05 | Streaming | Resolution, frame interval |
| VS_FORMAT_MJPEG | 0x06 | Streaming | MJPEG format |
| VS_FRAME_MJPEG | 0x07 | Streaming | MJPEG resolution |

## Streaming Setup Sequence

### 1. Probe (GET_CUR + SET_CUR on VS_PROBE_CONTROL)

```
Control transfer: bmRequestType=0xA1 (class, interface, device-to-host)
                  bRequest=GET_CUR (0x81)
                  wValue=VS_PROBE_CONTROL << 8 (0x0100)
                  wIndex=VideoStreaming interface number
                  wLength=26 (probe/commit data size)
```

The Probe/Commit data structure (26 bytes):

| Offset | Size | Field |
|--------|------|-------|
| 0 | 2 | bmHint (which fields are preferred) |
| 2 | 1 | bFormatIndex (1-based) |
| 3 | 1 | bFrameIndex (1-based) |
| 4 | 4 | dwFrameInterval (100ns units) |
| 8 | 2 | wKeyFrameRate |
| 10 | 2 | wPFrameRate |
| 12 | 2 | wCompQuality |
| 14 | 2 | wCompWindowSize |
| 16 | 2 | wDelay |
| 18 | 4 | dwMaxVideoFrameSize |
| 22 | 4 | dwMaxPayloadTransferSize |

### 2. Commit (SET_CUR on VS_COMMIT_CONTROL)

Same structure as Probe, sent with `wValue=VS_COMMIT_CONTROL << 8
(0x0200)` and `bmRequestType=0x21` (host-to-device).

### 3. Set Interface Alt Setting

```
Control transfer: bmRequestType=0x01 (standard, interface, host-to-device)
                  bRequest=SET_INTERFACE (0x0B)
                  wValue=1 (alt setting 1 = active)
                  wIndex=VideoStreaming interface number
```

### 4. Read Isochronous Frames

The driver submits isochronous IN transfer TRBs on the streaming
endpoint. Each isochronous packet has a UVC payload header:

```
Byte 0: bHeaderLength (typically 2-12)
Byte 1: bmHeaderInfo
  Bit 0: FID (Frame ID — toggles between frames)
  Bit 1: EOF (End of Frame)
  Bit 2: PTS present
  Bit 3: SCR present
  Bit 5: Error
Bytes 2+: optional PTS (4 bytes) and SCR (6 bytes)
```

Frame boundary detection: when the FID bit changes value between
consecutive packets, a new frame has started. The driver accumulates
payload data until FID toggles, then delivers the complete frame.

## Codex Driver Structure

### Records

```
UvcDevice = record {
  uvc-slot : Integer,
  uvc-stream-ep : Integer,
  uvc-format-index : Integer,
  uvc-frame-index : Integer,
  uvc-width : Integer,
  uvc-height : Integer,
  uvc-frame-interval : Integer,
  uvc-max-frame-size : Integer,
  uvc-max-payload : Integer,
  uvc-found : Boolean
}

UvcFrame = record {
  uvc-data : List Integer,       -- raw pixel data (YUYV)
  uvc-width : Integer,
  uvc-height : Integer,
  uvc-sequence : Integer
}
```

### Functions

| Function | Type | Purpose |
|----------|------|---------|
| `uvc-discover` | `XhciController -> Maybe UvcDevice` | Find UVC device on xHCI |
| `uvc-probe` | `UvcDevice -> UvcDevice` | Probe/Commit stream negotiation |
| `uvc-start-stream` | `UvcDevice -> UvcDevice` | Set alt interface, begin isoc |
| `uvc-read-frame` | `UvcDevice -> Maybe UvcFrame` | Read one complete frame |
| `uvc-stop-stream` | `UvcDevice -> UvcDevice` | Set alt 0, stop streaming |
| `yuyv-to-rgb` | `List Integer, Integer -> List Integer` | Convert YUYV to RGB pixels |
| `uvc-frame-to-fb` | `UvcFrame, GfxMode, Integer, Integer -> Integer` | Blit frame to display |

### YUYV to RGB Conversion

YUYV packs 2 pixels in 4 bytes: Y0, U, Y1, V.

```
R = Y + 1.402 * (V - 128)
G = Y - 0.344 * (U - 128) - 0.714 * (V - 128)
B = Y + 1.772 * (U - 128)
```

In fixed-point (no FPU on bare metal):

```
R = Y + (V - 128) * 359 / 256
G = Y - (U - 128) * 88 / 256 - (V - 128) * 183 / 256
B = Y + (U - 128) * 454 / 256
```

Clamp to 0-255.

## xHCI Transfer Support Needed

The Xhci.codex transfer stubs must be implemented:

| Function | Status | Needed For |
|----------|--------|------------|
| `usb-enable-slot` | Stub (returns 0) | Device enumeration |
| `usb-control-transfer` | Stub (returns 0) | Probe/Commit, SET_INTERFACE |
| `usb-get-config-desc` | Stub (returns []) | Descriptor parsing |
| `usb-isoc-in` | Does not exist | Frame reception |

`usb-isoc-in` is new — it submits isochronous IN TRBs and returns
received data. The xHCI isochronous TRB (type 5) carries:

- Bits 63:0 of TRB: data buffer pointer
- Bits 16:0 of status: TRB transfer length
- Bits 31:10 of control: TRB type (5) + TRT + frame ID

codex-vm already handles isoc TRBs (CL 2040) and writes test pattern
data to the guest buffer. On real hardware, the xHCI controller DMA's
camera data directly.

## codex-vm Test Infrastructure

codex-vm provides a test pattern UVC device on xHCI port 2:

- 160x120 YUYV color bars (38,400 bytes per frame)
- UVC descriptors matching a real webcam
- Isochronous TRB response with test frame data

The kernel UVC driver should produce identical behavior on codex-vm
(test pattern) and on real hardware (real camera). The only difference
is the pixel content.

## Implementation Plan

1. **UsbVideo.codex** — device discovery, descriptor parsing, format
   negotiation records and constants (~100 lines)
2. **Probe/Commit** — control transfer sequences for stream setup
   (~40 lines)
3. **YUYV→RGB conversion** — fixed-point color space conversion
   (~30 lines)
4. **Frame assembly** — accumulate isochronous packets, detect frame
   boundaries via FID bit (~60 lines)
5. **Display integration** — blit converted frames to VgaGraphics
   framebuffer (~20 lines)
6. **xHCI isoc support** — implement `usb-isoc-in` in Xhci.codex
   (~40 lines)

Estimated total: ~290 lines across UsbVideo.codex and Xhci.codex.

## Testing Strategy

1. **codex-vm**: boot kernel with UVC test pattern, verify driver
   discovers device, negotiates stream, receives frames, converts
   to RGB, and displays color bars on the framebuffer
2. **ASUS TUF**: plug in real USB webcam, verify same driver path
   produces real camera output

## Format Selection: YUYV First

Most webcams support both YUYV (uncompressed 4:2:2) and MJPEG. Start
with YUYV — zero decode logic, raw pixels, 2 bytes/pixel. At 160x120,
a frame is 38,400 bytes. At 640x480, it's 614,400 bytes — still within
USB 2.0 high-speed isochronous bandwidth (~24 MB/s). MJPEG is only
needed at higher resolutions where uncompressed exceeds bandwidth.

## Terminal Type IDs

| Type | wTerminalType |
|------|---------------|
| TT_STREAMING (USB) | 0x0101 |
| ITT_CAMERA (sensor) | 0x0201 |
| OTT_DISPLAY | 0x0301 |

## References

- USB Video Class 1.5 spec: https://www.usb.org/document-library/video-class-v15-document-set
- UVC payload header format: UVC 1.5 Table 2-6
- `codex/os/kernel/UsbAudio.codex` — closest analog in the kernel
- `codex/os/kernel/Xhci.codex` — xHCI transport layer
- `codex/os/kernel/VgaGraphics.codex` — display target
- `tools/codex-vm.c` — test pattern UVC device (CL 2040)
