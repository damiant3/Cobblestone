# UI Capability Map

**Status:** Initial survey (2026-05-24). Tracks what the Codex UI
foreword primitives can express, what each target platform supports,
and where gaps exist.

## Codex UI Primitives (codex.foreword.ui)

28 modules. These are the source-of-truth abstractions that all plug
emitters must map to their target platform.

### Core Widget Tree

| Primitive | Module | What it represents |
|---|---|---|
| `WidgetNode` | Widget | Tree node: kind, state, children, layout, bounds |
| `WidgetKind` | Widget | WkPanel, WkLabel, WkButton, WkGauge, WkSeparator, WkInput, WkCustom |
| `WidgetState` | Widget | 5 states: normal, hover, pressed, disabled, focused |

### Layout

| Primitive | Module | What it represents |
|---|---|---|
| `LayoutDir` | Layout | DirRow, DirColumn |
| `LayoutItem` | Layout | min-w, min-h, flex weight, margin |
| `flex-layout` | Layout | Flexbox: row/column, gap, proportional sizing |
| `GridLayout` | Layout | Column count, row/col gap |
| `SplitLayout` | Layout | Two-pane with ratio |
| `stack-layout` | Layout | Absolute-positioned overlay stack |

### Box Model

| Primitive | Module | What it represents |
|---|---|---|
| `LayoutRect` | BoxModel | x, y, width, height |
| `Edges` | BoxModel | top, right, bottom, left (margin/padding) |
| `Border` | BoxModel | Per-side width + color, corner style |

### Theming

| Primitive | Module | What it represents |
|---|---|---|
| `Palette` | Theme | 10 named colors (bg, fg, primary, secondary, accent, muted, error, success, warning, border) |
| `WidgetStyle` | Theme | bg, fg, border, padding, margin, min dimensions |
| `StateStyles` | Theme | Style per widget state (normal/hover/pressed/disabled/focused) |
| `Theme` | Theme | Named theme with palette + per-widget-type StateStyles |

### Events

| Primitive | Module | What it represents |
|---|---|---|
| `EventKind` | Event | KeyDown, KeyUp, MouseMove, MouseDown, MouseUp, Scroll, Focus, Blur, Resize, Timer, Network, Custom |
| `Event` | Event | Kind + target widget ID + timestamp + stopped/handled flags |
| `HandlerTable` | Event | Registered event handlers with capture/bubble routing |
| `EventPath` | Event | Propagation path from target to root |

### Rendering

| Primitive | Module | What it represents |
|---|---|---|
| `Framebuf` | Render | ARGB pixel buffer (bare-metal backend) |
| `Surface` | Surface | Named layer with position, z-order, opacity, dirty flag |
| `Compositor` | Surface | Z-ordered surface stack with background color |

### Window Management

| Primitive | Module | What it represents |
|---|---|---|
| `Window` | Window | ID, title, position, size, state, z-order, focused, closeable |
| `WindowManager` | Window | Window list with screen dimensions, z-order tracking |
| `WindowState` | Window | WsNormal, WsMinimized, WsMaximized |

### Interactive Components

| Primitive | Module | What it represents |
|---|---|---|
| `TextFieldState` | TextField | Text, cursor position, selection range, multiline |
| `DialogConfig` | Dialog | Title, message, buttons, input field |
| `DialogResult` | Dialog | DlgOk, DlgCancel, DlgInput |
| `Overlay` | Overlay | Tooltip, Popup, ContextMenu, Notification, Modal |
| `ScrollState` | Scroll | Viewport offset, content size, scrollbar geometry |

### Data Binding

| Primitive | Module | What it represents |
|---|---|---|
| `Observable` | Binding | Integer value with dirty tracking |
| `ObsText` | Binding | Text value with dirty tracking |
| `BindingTable` | Binding | Routes dirty observables to widget update targets |

### Animation

| Primitive | Module | What it represents |
|---|---|---|
| `Throbber` | Animation | Spin, Pulse, Bounce, Bar -- tick-driven |
| `Transition` | Animation | Property easing (start/end/duration/curve) |
| `KeyframeSeq` | Animation | Looping keyframe sequence |

### Additional

| Primitive | Module | What it represents |
|---|---|---|
| `Font` | Font | Bitmap font with glyph metrics (CCE-indexed) |
| `Icon` | Icon | Multi-size bitmap icon (16/24/32/48px) |
| `Color` (game) | Color | ARGB manipulation, blending, HSL conversion |
| `Accessibility` | Accessibility | Role, label, live region, keyboard nav hints |
| `Focus` | Focus | Focus ring, tab order, focus group |
| `Selection` | Selection | Range selection in text/lists |
| `Cursor` | Cursor | Cursor shape (arrow, hand, text, resize, etc.) |
| `Drag` | Drag | Drag source, drop target, drag data |
| `Touch` | Touch | Touch points, gesture recognition |
| `Charts` | Charts | Bar, line, pie, area chart data + rendering |
| `Sound` | Sound | UI sound effects (click, hover, error, success) |
| `Clipboard` | Clipboard | Copy/paste text |
| `Vector` | Vector | SVG-like vector path rendering |

## Platform Coverage Matrix

### Widget Types

| Widget | HTML | React | WPF | WinForms | SwiftUI | MAUI | Compose | Flutter | Qt | Electron | GTK |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Panel | div | div | StackPanel | Panel | VStack/HStack | StackLayout | Column/Row | Column/Row | Rectangle | div | GtkBox |
| Label | span | span | TextBlock | Label | Text | Label | Text | Text | Text | span | GtkLabel |
| Button | button | button | Button | Button | Button | Button | Button | ElevatedButton | Button | button | GtkButton |
| Input | input | input | TextBox | TextBox | TextField | Entry | TextField | TextField | TextInput | input | GtkEntry |
| Gauge | progress | progress | ProgressBar | ProgressBar | ProgressView | ProgressBar | LinearProgressIndicator | LinearProgressIndicator | ProgressBar | progress | GtkProgressBar |
| Separator | hr | hr | Separator | - | Divider | BoxView | Divider | Divider | Rectangle | hr | GtkSeparator |
| Custom | div | div | ContentControl | UserControl | AnyView | ContentView | Box | Container | Item | div | GtkWidget |

### Layout

| Feature | HTML | React | WPF | WinForms | SwiftUI | MAUI | Compose | Flutter | Qt | GTK |
|---|---|---|---|---|---|---|---|---|---|---|
| Flex row | flex-direction:row | flex | StackPanel Horizontal | FlowLayoutPanel | HStack | HorizontalStackLayout | Row | Row | RowLayout | GtkBox horizontal |
| Flex column | flex-direction:column | flex | StackPanel | FlowLayoutPanel | VStack | VerticalStackLayout | Column | Column | ColumnLayout | GtkBox vertical |
| Flex weight | flex:N | flex:N | - | - | Spacer | - | Modifier.weight | Expanded | Layout.fillWidth | expand=true |
| Grid | CSS Grid | CSS Grid | Grid | TableLayoutPanel | LazyVGrid | Grid | LazyVerticalGrid | GridView | GridLayout | GtkGrid |
| Stack/overlay | position:absolute | position:absolute | Canvas | - | ZStack | AbsoluteLayout | Box | Stack | anchors | GtkOverlay |
| Gap | gap | gap | - | - | spacing | Spacing | Arrangement.spacedBy | SizedBox | spacing | spacing |

### Theming

| Feature | HTML | React | WPF | WinForms | SwiftUI | MAUI | Compose | Flutter |
|---|---|---|---|---|---|---|---|---|
| CSS variables | Yes | Yes | ResourceDictionary | - | Environment | ResourceDictionary | MaterialTheme | ThemeData |
| State styles | :hover/:active/:focus | onMouse* | Triggers | - | .buttonStyle | VisualStateManager | Interaction | InkWell |
| Dark mode | prefers-color-scheme | useMediaQuery | SystemColors | - | @Environment colorScheme | AppThemeBinding | isSystemInDarkTheme | ThemeMode |
| Custom palette | :root vars | Context/Provider | StaticResource | - | custom modifier | - | custom colors | custom ThemeData |

### Events

| Event | HTML | React | WPF | WinForms | SwiftUI | MAUI | Compose | Flutter |
|---|---|---|---|---|---|---|---|---|
| Click | onclick | onClick | Click | Click | onTapGesture | Clicked | clickable | onTap |
| Key | onkeydown/up | onKeyDown/Up | KeyDown/Up | KeyDown/Up | onKeyPress | - | onKeyEvent | RawKeyboardListener |
| Mouse move | onmousemove | onMouseMove | MouseMove | MouseMove | onHover | - | pointerInput | MouseRegion |
| Scroll | onwheel | onWheel | ScrollChanged | Scroll | - | Scrolled | verticalScroll | SingleChildScrollView |
| Focus | onfocus/blur | onFocus/Blur | GotFocus/LostFocus | Enter/Leave | onFocusChange | Focused/Unfocused | onFocusChanged | FocusNode |
| Resize | onresize | useResize | SizeChanged | Resize | GeometryReader | SizeChanged | onSizeChanged | LayoutBuilder |
| Touch | ontouchstart | onTouch* | TouchDown | - | gesture | - | pointerInput | GestureDetector |
| Drag | ondragstart | onDrag* | DragDelta | DragDrop | onDrag | DragGestureRecognizer | draggable | Draggable |

### Interactive Components

| Component | HTML | React | WPF | WinForms | SwiftUI | MAUI | Compose | Flutter |
|---|---|---|---|---|---|---|---|---|
| TextField | input/textarea | useState + input | TextBox | TextBox | TextField | Entry/Editor | TextField | TextField |
| Dialog | dialog | portal/modal | Window | Form.ShowDialog | .alert/.sheet | DisplayAlert | AlertDialog | showDialog |
| Tooltip | title attr | Tooltip lib | ToolTip | ToolTip | .help | - | PlainTooltip | Tooltip |
| Context menu | contextmenu | custom | ContextMenu | ContextMenuStrip | .contextMenu | - | DropdownMenu | PopupMenuButton |
| Scrollbar | overflow:auto | custom | ScrollViewer | AutoScroll | ScrollView | ScrollView | verticalScroll | SingleChildScrollView |

### Animation

| Feature | HTML | React | WPF | WinForms | SwiftUI | MAUI | Compose | Flutter |
|---|---|---|---|---|---|---|---|---|
| Transitions | CSS transition | CSSTransition | Storyboard | Timer | withAnimation | Animation | animateXAsState | AnimatedContainer |
| Keyframes | @keyframes | css-in-js | KeyFrame | Timer | KeyframeAnimator | - | Animatable | AnimationController |
| Throbber/spinner | CSS animation | component | - | - | ProgressView | ActivityIndicator | CircularProgressIndicator | CircularProgressIndicator |
| Easing curves | cubic-bezier | - | EasingFunction | - | .easeInOut | Easing | FastOutSlowIn | Curves |

## Gaps and Extension Opportunities

### Missing from Codex UI (needs new foreword modules or extensions)

| Gap | Description | Priority |
|---|---|---|
| **Table/DataGrid** | Tabular data display with sorting, filtering, column resize | High |
| **TreeView** | Hierarchical collapsible tree | Medium |
| **TabView** | Tabbed container with tab bar | High |
| **Navigation** | Page stack, back/forward, routes, breadcrumbs | High |
| **Menu/MenuBar** | Application menus, nested submenus | Medium |
| **Toolbar** | Action bar with icon buttons, overflow | Medium |
| **Toast/Snackbar** | Temporary non-modal notifications | Medium |
| **Carousel/Pager** | Horizontal paging through content | Low |
| **DatePicker** | Date/time selection | Medium |
| **ColorPicker** | Color selection | Low |
| **Slider/Range** | Continuous value selection | Medium |
| **Toggle/Switch** | Boolean on/off | High |
| **Checkbox** | Multiple selection | High |
| **Radio** | Single selection from group | High |
| **DropDown/Select** | Pick from list | High |
| **Badge/Chip** | Small label/tag | Low |
| **Avatar** | User image/initials circle | Low |
| **Breadcrumb** | Navigation path | Low |
| **Accordion** | Collapsible sections | Medium |
| **Stepper/Wizard** | Multi-step flow | Low |
| **RichText editing** | Bold/italic/links in-place | Medium |
| **Canvas/Drawing** | 2D drawing API beyond Vector | Low |
| **Video** | Video playback control | Low |
| **Map** | Geographic map display | Low |

### Missing Platform-Specific Capabilities

| Capability | Platforms affected | Description |
|---|---|---|
| **Native file picker** | All desktop/mobile | Open/save file dialogs |
| **System tray** | Desktop (Win/Mac/Linux) | Tray icon, context menu |
| **Notifications** | Mobile, desktop | Push/local notifications |
| **Deep links** | Mobile, web | URL scheme handling |
| **Biometrics** | Mobile | Face ID, fingerprint |
| **Camera access** | Mobile | Photo/video capture |
| **Geolocation** | Mobile, web | GPS coordinates |
| **Haptics** | Mobile | Vibration feedback |
| **Sharing** | Mobile | System share sheet |
| **In-app purchase** | Mobile | Payment flows |

### Version Compatibility Targets

| Platform | Minimum Version | Notes |
|---|---|---|
| HTML | HTML5 + CSS3 | All modern browsers |
| React | 18+ | Hooks, functional components |
| Angular | 17+ | Standalone components |
| Vue | 3.3+ | Composition API, script setup |
| Svelte | 5+ | $state runes |
| WPF | .NET 6+ | Modern .NET, not Framework |
| WinForms | .NET 6+ | Modern .NET |
| MAUI | .NET 8+ | Current LTS |
| SwiftUI | iOS 16+ / macOS 13+ | Latest stable APIs |
| Jetpack Compose | 1.5+ | Material3 |
| Flutter | 3.16+ | Material3, Dart 3 |
| Qt/QML | 6.5+ | Qt Quick Controls |
| Electron | 28+ | ESM support |
| GTK | 4.12+ | GTK4 + libadwaita |
| Go | 1.22+ | Generics |
| Java | 21+ | Current LTS |
| Kotlin | 2.0+ | K2 compiler |
| Swift | 5.9+ | Macros, observation |
| TypeScript | 5.3+ | Satisfies, decorators |
| Rust | 1.75+ | Current stable |
| Haskell | GHC 9.6+ | Current stable |
| Zig | 0.13+ | Current stable |

## Recommended Next Extensions

### Phase 1: Common Controls (High priority)

Add to `codex.foreword.ui`:
- `Toggle.codex` -- on/off switch, checkbox
- `Select.codex` -- dropdown, radio group
- `Slider.codex` -- range input with min/max/step
- `TabView.codex` -- tabbed container
- `Navigation.codex` -- page stack, routing
- `Table.codex` -- data grid with column definitions

These 6 modules would cover the most common gaps across all platforms.

### Phase 2: Application Chrome

- `MenuBar.codex` -- application menus
- `Toolbar.codex` -- action bar
- `Toast.codex` -- snackbar/toast notifications
- `TreeView.codex` -- hierarchical display

### Phase 3: Mobile/Platform Specific

- `NativeDialog.codex` -- file pickers, share sheets
- `Permission.codex` -- camera, location, notification permissions
- `Notification.codex` -- push/local notifications
