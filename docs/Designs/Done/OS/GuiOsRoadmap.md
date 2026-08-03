# GUI OS Feature Roadmap

**Date**: 2026-06-22
**Status**: Active
**Depends on**: GuiOsBringup (done), Widget framework (done),
Theme/Render enrichment (done CL 5611), system modules (done)

---

## Current State (CL 5660)

The GUI OS has strong system utilities, a rich widget framework, and
good device management -- but lacks the desktop shell and settings
panels that make it feel like an OS to use.

**Completeness: ~45% of a standard desktop OS.**

### What We Have

| Area | Modules |
|------|---------|
| Widget framework | 37 foreword/ui modules incl. Dropdown, TreeView, DataTable, Editor, Markdown, Validation |
| Rendering | Shadows, gradients, accent borders, rounded rects, event-dot dispatch |
| Theme | 3 built-in + CvmmTheme dark with enriched card/sidebar/header styles |
| Icons | 55 monochrome 8x8 icons, scaling to any size |
| Terminal | Lolcat rainbow, 12 palettes, configurable freq/speed/seed/psychedelic |
| Calendar | 7-column grid, today highlight, week/day/agenda views |
| Notifications | System notification manager + persistent audit log + Journal |
| Audio | System audio control, per-app mixer, mic privacy |
| Display | Multi-monitor extend/mirror, virtual canvas, workspace switching |
| Screen Saver | 8 built-in savers, FishTank default, idle tracking, lock-on-wake |
| Keyboard RGB | USB HID class driver, VIA/QMK protocol, palette sync |
| Auth | OAuth2/PKCE, JWT, Google/Microsoft providers, trust bridge, IMAP |
| File Manager | FileExplorer with breadcrumbs, 3 view modes, filtering |
| Process Mgmt | ProcessManager, TaskScheduler, Monitor |
| System Info | SystemInfo, PerfMonitor, MetricStore |
| Logging | Journal, EventBus, NotificationLog, CapabilityAudit |
| Networking | HttpClient, WebServer, NE2K NIC, Accounts |
| Clipboard | Text + binary with mime types |
| Screenshot | Screenshot capture |
| Calculator | Calculator app |
| Keybindings | Full keybinding system |

---

## Tier 1: Desktop Shell (Priority: NOW)

These make it feel like an OS. Without them, all the system modules
have no home in the UI.

### 1A. Taskbar (`codex/os/dev/Taskbar.codex`)

Persistent bar at the bottom (or top) of the screen. Sections:

- **App launcher button** (left) -- opens the start menu
- **Pinned apps** -- icon buttons for favorites
- **Running apps** -- buttons for open windows, click to focus
- **System tray** (right) -- clock, volume icon, network icon, battery
  icon, notification bell with unread count, keyboard layout indicator
- **Workspace indicator** -- dots/numbers for virtual desktops

State: pinned app list, running app list, tray icon states, clock
text, taskbar position (top/bottom), auto-hide toggle.

Widget: WkPanel DirRow with left/center/right sections.

### 1B. App Launcher (`codex/os/dev/AppLauncher.codex`)

Overlay that opens on taskbar button click or keyboard shortcut.

- **Search bar** at top -- filters apps by name
- **Pinned section** -- grid of favorite app icons
- **All apps list** -- alphabetical, scrollable
- **Recent apps** -- last 5 launched
- **Power menu** -- shutdown, restart, lock, sleep

App registry: list of `AppEntry` records with id, name, icon, command,
category (System, Productivity, Development, Media, Games).

### 1C. Wallpaper (`codex/os/dev/Wallpaper.codex`)

Background image/color behind all windows.

- **Solid color** -- pick from palette or custom
- **Gradient** -- top-to-bottom or side-to-side with two colors
- **Pattern** -- tiled procedural patterns (grid, dots, stripes)
- **Image** -- load from file (future: when image decode is wired)
- **Slideshow** -- rotate through a folder of images on a timer

State: mode, color1, color2, pattern-id, image-path, slideshow-interval.
The compositor draws the wallpaper as the bottommost surface.

### 1D. Window Snapping (`codex/os/dev/WindowSnap.codex`)

When a window is dragged to a screen edge, snap it to a zone.

- **Left half / right half** -- drag to left/right edge
- **Top half / bottom half** -- drag to top/bottom edge  
- **Quarters** -- drag to corners
- **Maximize** -- drag to top center
- **Custom zones** -- power user grid layout (future)

State: snap zones per monitor, active snap preview, snap history for
undo. Keyboard shortcuts: Super+Left/Right/Up/Down.

### 1E. Global Search (`codex/os/dev/GlobalSearch.codex`)

Unified search across files, apps, settings, and commands.

- **Search bar** overlay (keyboard shortcut to open)
- **Result categories** -- Apps, Files, Settings, Commands
- **Inline preview** -- show file contents or setting location
- **Fuzzy matching** -- substring and prefix matching

State: query, results by category, selected index, history.

---

## Tier 2: Essential Settings

### 2A. Power Management (`codex/os/dev/PowerManager.codex`)

- Power plans: Performance, Balanced, Battery Saver
- Sleep/hibernate timers
- Battery level monitoring (for laptops/USB devices)
- Display brightness control
- Night light (blue light filter with schedule)
- Lid close action (laptop: sleep/hibernate/nothing)

### 2B. DateTime Settings (`codex/os/dev/DateTimeSettings.codex`)

- Current date/time display
- Timezone selection (UTC offset list)
- 12h/24h format toggle
- Auto-sync from NTP server (when network available)
- Calendar first-day-of-week setting (Sunday/Monday)

### 2C. Bluetooth (`codex/os/dev/Bluetooth.codex`)

- Device discovery and pairing
- Paired device list with battery levels
- Connect/disconnect toggle
- Device categories (audio, keyboard, mouse, phone)

### 2D. Privacy Settings (`codex/os/dev/PrivacySettings.codex`)

- Per-app permissions: camera, microphone, location, clipboard, network
- Permission grant/revoke history (audit trail)
- Global toggles for camera/mic (hardware kill switch state)
- App access log (which app accessed what, when)

### 2E. Default Apps (`codex/os/dev/DefaultApps.codex`)

- Default browser, mail client, file viewer, text editor, terminal
- File type associations (.txt -> editor, .png -> image viewer, etc.)
- Protocol handlers (http, mailto, ssh)

---

## Tier 3: Utilities

### 3A. Backup and Restore (`codex/os/dev/BackupRestore.codex`)

- Scheduled backups to external drive or network location
- Backup scope: full system, user data only, selected folders
- Restore from backup image
- Backup history with sizes and dates
- Incremental vs full backup toggle

### 3B. Screen Recording (`codex/os/dev/ScreenRecorder.codex`)

- Record screen to video file
- Region selection (full screen, window, custom rect)
- Audio capture toggle (system audio, mic, both)
- Recording indicator (red dot in tray)
- Timer/countdown before recording starts

### 3C. Network Diagnostics (`codex/os/dev/NetworkDiag.codex`)

- Ping test to gateway and DNS
- Traceroute visualization
- DNS lookup tool
- Port scanner
- Speed test (bandwidth measurement)
- Network interface status (IP, MAC, link speed)

### 3D. VPN/Proxy/Firewall Settings

- VPN client (OpenVPN/WireGuard config import)
- Proxy settings (HTTP/SOCKS)
- Firewall rules (allow/deny per app, per port)
- Connection profiles (home/work/public)

---

## Tier 4: Peripherals

### 4A. Printer Management (`codex/os/dev/PrintManager.codex`)

- Printer discovery (USB, network/IPP)
- Print queue management
- Default printer selection
- Paper size and quality settings

### 4B. Game Controller (`codex/os/dev/GamepadManager.codex`)

- USB HID gamepad detection
- Button mapping display
- Axis calibration
- Dead zone settings
- Vibration test

### 4C. Touchpad Settings (`codex/os/dev/TouchpadSettings.codex`)

- Sensitivity / tracking speed
- Tap-to-click toggle
- Scroll direction (natural/traditional)
- Multi-finger gestures (two-finger scroll, three-finger swipe)

---

## Commit Strategy

Tier 1 is 5 modules, one commit each:
1. Taskbar
2. AppLauncher
3. Wallpaper
4. WindowSnap
5. GlobalSearch

Tier 2 is 5 modules:
6. PowerManager
7. DateTimeSettings
8. Bluetooth
9. PrivacySettings
10. DefaultApps

Tiers 3-4 are future work, captured here for tracking.

---

## Verification

Each module follows the established pattern:
- State record + actions + queries + widget builder + formatting
- Pure functional state (no effects in the module itself)
- Widget trees compose base widget kinds from the framework
- Integration through the compositor and overlay stack
