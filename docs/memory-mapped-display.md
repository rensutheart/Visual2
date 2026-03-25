# Memory-Mapped Display — Design & Planning

## 1. Overview

Add a built-in memory-mapped pixel display to VisUAL2-SU. Students write ARM assembly that stores colour values into a dedicated memory region, and a visual display renders those bytes as a grid of coloured pixels — all within the simulator itself (no external tool needed).

### Existing Implementation (VisUAL v1 external tool)

The previous approach (`VisUAL_MemoryScreen`) was a separate C# WinForms application that:
- Ran VisUAL v1 headlessly via `visual_headless.jar`
- Iterated through every instruction of the execution trace
- Checked R10 after each instruction — when `R10 == 0x1`, it read 256 bytes from address `0x2000` and rendered a 16×16 pixel grid
- Used a **VGA 256-colour palette** (the standard Mode 13h / VGA palette) to map each byte value to an RGB colour
- The display rendered each pixel as a 25×25 square (400×400 total)
- The "animation" was achieved by re-reading memory at each R10 toggle point, with a configurable frame rate

### Goals for VisUAL2-SU

- **Built-in** — no external tool, works on all platforms
- **Interactive** — display updates live during stepping or at breakpoints
- **Simple API** — students use a base address register + STRB to draw pixels
- **Animation-friendly** — some mechanism to pause/continue for frame-by-frame rendering

---

## 2. Display Specification

### Grid Size Options

| Option | Pixels | Memory (bytes) | Notes |
|--------|--------|----------------|-------|
| 16×16 | 256 | 256 | Same as original, very small |
| 32×32 | 1024 | 1024 | Good balance |
| 64×64 | 4096 | 4096 | More detail, still manageable |
| **Configurable** | W×H | W×H | Dropdown or setting |

**Decision needed**: Fixed size or configurable?
- Recommendation: Start with **16×16** (matching existing practical) with a setting to change later.
Rensu: default is 16x16, but in the Preferences menu add a section where this can be chosen from a dropdown to be 16x16, 32x32 or 64x64 (don't make it configurable for now to any WxH, just these set ones)

### Colour Model Options

| Option | Description | Bytes/pixel | Palette needed |
|--------|-------------|-------------|----------------|
| **VGA 256-colour palette** | Classic indexed colour, 1 byte per pixel | 1 | Yes (built-in 256-entry table) |
| RGB332 | 3-3-2 bit direct colour | 1 | No |
| RGB565 | 5-6-5 bit direct colour | 2 | No |
| Greyscale | 256 shades | 1 | No |

**Decision needed**: Which colour model?
- Recommendation: **VGA 256-colour palette** — matches the existing practical, pedagogically interesting (students learn about colour mapping), and the palette is already defined in Display.cs.
Rensu: Keep it to only VGA for now, but add a reference to the color palette here: https://www.fountainware.com/EXPL/vga_color_palettes.htm

### Memory Layout

| Field | Value | Notes |
|-------|-------|-------|
| Base address | `0x2000` (configurable) | Matches existing practical |
| Size | `W × H` bytes | e.g., 256 bytes for 16×16 |
| Pixel ordering | Row-major, top-left origin | Pixel (x,y) = base + y×width + x |
| Pixel size | 1 byte | Index into VGA palette |

Student code pattern:
```arm
    MOV  R9, #0x2000       ; base address of display memory
    ; Draw pixel at (x=5, y=2) with colour 0x09
    MOV  R0, #5            ; x
    MOV  R1, #2            ; y
    MOV  R2, #0x09          ; colour index
    ADD  R0, R0, R1, LSL #4 ; offset = x + y * 16
    STRB R2, [R9, R0]       ; write pixel
```

---

## 3. Refresh / Animation Mechanism

### Option A: R10 Toggle (original approach)
- When `R10` transitions to `1`, the display refreshes
- Student writes `MOV R10, #1` / `MOV R10, #0` to trigger a frame
- **Pros**: Compatible with existing practical, simple for students
- **Cons**: Uses up a register, checked after every instruction (perf impact)

### Option B: Special memory address trigger
- Writing to a specific "control" address (e.g., `0x1FFC`) triggers a refresh
- **Pros**: Doesn't consume a register, natural memory-mapped I/O pattern
- **Cons**: Slightly less intuitive for beginners

### Option C: Automatic periodic refresh
- Display refreshes every N instructions or on a timer
- **Pros**: No special code needed
- **Cons**: May show partially-drawn frames, less educational

### Option D: Refresh on step/pause
- Display always shows current memory state when execution pauses (step, breakpoint, end)
- For "animation": use a "Continue" button that runs until R10 is set, then pauses
- **Pros**: Simple, natural, integrates with existing step/run
- **Cons**: Full-speed animation requires auto-step mode

### Recommended: Hybrid of A + D
- **During stepping/pause**: Display always shows current memory state (Option D)
- **During "Run"**: Check R10 after each instruction (Option A) — when R10 becomes 1, pause execution and update display, then wait for user to click "Continue" (or auto-continue with a configurable delay)
- This gives students frame-by-frame control AND animation capability
- Rensu: Yes, follow this recommendation, but remember there should be a toggle to indicate whether the screen mode is active or not. If it is not active, then the memory and register is just normal, and nothing should be displayed on the memory mapped display.

### Animation Flow

```
Student code                    Simulator behaviour
─────────────                   ───────────────────
; draw frame 1
STRB R2, [R9, R0]
...
MOV R10, #1          ──────►   Display refreshes, execution PAUSES
MOV R10, #0                     (waits for Continue button / auto-delay)
                     ◄──────   User clicks Continue (or delay expires)
; draw frame 2
STRB R2, [R9, R1]
...
MOV R10, #1          ──────►   Display refreshes, execution PAUSES again
MOV R10, #0
```

### Animation Controls (in display panel)
- **[▶ Continue]** — Resume execution until next R10 trigger or end
- **[⏩ Auto]** — Auto-continue with delay (dropdown: 100ms, 250ms, 500ms, 1s)
- **[⏹ Stop]** — Halt execution

---

## 4. UI Placement Options

### Option A: New tab alongside Registers / Memory / Symbols

```
┌──────────────────────────────────────────────────────┐
│  [Registers] [Memory] [Symbols] [Display]            │
├──────────────────────────────────────────────────────┤
│                                                      │
│    ┌─────────────────────────────┐                   │
│    │  16×16 pixel grid           │                   │
│    │  (rendered as coloured      │                   │
│    │   squares, ~20px each)      │                   │
│    │                             │                   │
│    └─────────────────────────────┘                   │
│                                                      │
│    [▶ Continue] [⏩ Auto ▼] [⏹ Stop]                │
│    Mode: [●] Display Mode  Base: 0x2000              │
│    Size: 16×16  Frame: 3/∞                           │
└──────────────────────────────────────────────────────┘
```

**Pros**: 
- Fits existing architecture perfectly (just add a Views case)
- Easy to implement — same pattern as Memory/Symbols tabs
- Students can switch between Display and Memory views freely
- No complex window management

**Cons**: 
- Limited vertical space for the grid (dashboard is narrow ~300px)
- Can't see registers AND display simultaneously

### Option B: Pop-out window

```
Main window:                    Pop-out:
┌─────────────────────┐        ┌──────────────────┐
│ Editor │ Registers  │        │  Memory Display   │
│        │ Memory     │        │  ┌──────────┐    │
│        │ Symbols    │        │  │ 16×16    │    │
│        │            │        │  │ grid     │    │
│        │            │        │  └──────────┘    │
│        │            │        │  [▶] [⏩] [⏹]   │
└─────────────────────┘        └──────────────────┘
```

**Pros**: 
- Dedicated space, resizable
- Can see registers/memory AND display at the same time
- Can make the display larger

**Cons**: 
- Electron multi-window is complex (IPC between windows)
- Loses focus issues, window management headaches
- More work to implement
- May confuse students (which window to look at?)

### Option C: Below editor (horizontal split)

```
┌─────────────────────────────────────────────────┐
│  Editor                          │ Registers    │
│                                  │ Memory       │
│                                  │ Symbols      │
├──────────────────────────────────┤              │
│  ┌──────────┐                    │              │
│  │ Display  │  [▶] [⏩] [⏹]    │              │
│  └──────────┘                    │              │
└─────────────────────────────────────────────────┘
```

**Pros**: Can see display, editor, and registers simultaneously
**Cons**: Complex layout changes, reduces editor space

### Recommended: Option A (tab) with Option B as a stretch goal

Option A is straightforward — add a `Display` tab to the existing tab group. The dashboard panel is ~300px wide, which comfortably fits a 16×16 grid at ~18px per pixel (288px). Controls go below the grid.

Later, a "Pop Out" button on the Display tab could open a resizable Electron child window if needed.
Rensu: Ok, just Option A for now

---

## 5. "Display Mode" Toggle

### Concept
A toggle switch activates Memory-Mapped Display Mode. When active:
1. The Display tab becomes visible/selectable
2. R10 becomes the **refresh trigger** register (writes to R10 are intercepted)
3. Memory at the configured base address is interpreted as pixel data
4. The VisUAL2-SU step/run system gains R10-aware breakpoint logic

### Where to attach the toggle
- **In the Display tab itself** — a checkbox at the top: `☑ Enable Display Mode`
- Or **in the toolbar** — a toggle button next to the representation buttons (Hex/Bin/Dec/UDec)
- Recommendation: **Display tab checkbox** — keeps it discoverable but not intrusive
Rensu: I agree with the recommendation

### What changes when Display Mode is ON
| Aspect | Normal Mode | Display Mode |
|--------|-------------|-------------|
| R10 | Normal register | Refresh trigger (write 1 pauses execution) |
| Memory at base addr | Normal data memory | Rendered as pixels |
| Display tab | Hidden or greyed out | Active, shows grid |
| Run behaviour | Run to end or error | Also pauses on R10 == 1 |
| Step behaviour | Normal step | Also refreshes display after each step |

### What changes when Display Mode is OFF
- Everything works as normal — no performance impact, no R10 interception
- Display tab can remain visible but shows "Display Mode is off" message
Rensu: Yes
---

## 6. Implementation Architecture

### Files to Modify/Create

| File | Changes |
|------|---------|
| `app/index.html` | Add `<tab id="tab-display">Display</tab>` and `<div id="view-display">` with canvas/grid container |
| `src/Renderer/Refs.fs` | Add `Display` to `Views` enum, add DOM mappings, add display settings |
| `src/Renderer/Views.fs` | Add `updateDisplay()` function that reads memory and renders pixel grid |
| `src/Renderer/Integration.fs` | Hook into `showInfoFromCurrentMode()` to call `updateDisplay()`, add R10 breakpoint logic to `asmStepDisplay` |
| `src/Renderer/Renderer.fs` | Attach click handlers for Display tab and controls |
| `app/css/vistally.css` | Styles for pixel grid, controls |
| `app/js/monaco-init.js` | (none expected) |
| `src/Emulator/ExecutionTop.fs` | Possibly add R10-aware break condition to `asmStep` |
| `src/Emulator/CommonData.fs` | (none expected — memory/regs already sufficient) |

### Rendering Approach: HTML Canvas vs. CSS Grid vs. SVG

| Approach | Pros | Cons |
|----------|------|------|
| **HTML5 Canvas** | Fast pixel rendering, scales well | Slightly more JS code, not Fable-native |
| CSS Grid of divs | Pure DOM, easy in Fable | Slow for 64×64 (4096 divs), DOM churn |
| **SVG rects** | Clean, scalable | svg.js already included, but slower than canvas |
| HTML table cells | Simple | Even slower than CSS grid |

**Recommendation**: **HTML5 Canvas** — it's ideal for pixel grids. The `<canvas>` element goes in the Display tab. The F# code uses `Fable.Import.Browser` to get a 2D context and draw coloured rectangles. For 16×16 at 18px each, we draw 256 `fillRect` calls — trivially fast.

### VGA Palette

The 256-colour VGA palette from Display.cs should be embedded as an F# array in a new file or in Views.fs:
```fsharp
let vgaPalette : (byte * byte * byte) array = [|
    (0uy, 0uy, 0uy)         // 0: black
    (0uy, 2uy, 170uy)       // 1: blue
    (20uy, 170uy, 0uy)      // 2: green
    // ... all 256 entries from Display.cs colormap
    |]
```

### R10 Breakpoint Logic

In `Integration.fs` `asmStepDisplay`, after each `asmStep` chunk:
```fsharp
// Check if display mode is on and R10 == 1
let dp = fst ri'.dpCurrent
if displayModeEnabled && dp.Regs.[R10] = 1u then
    // Pause execution, refresh display
    updateDisplay()
    // Set state to paused, wait for Continue button
```

This is similar to how the existing breakpoint system works but triggers on a register value rather than a line number.

---

## 7. Student-Facing API Summary

When Display Mode is enabled:

| Register/Address | Purpose |
|-----------------|---------|
| R9 (convention) | Base address of display memory (student sets this, e.g., `MOV R9, #0x2000`) |
| R10 | Refresh trigger — write `1` to update display and pause |
| `[base + offset]` | Pixel data: 1 byte per pixel, VGA 256-colour palette index |

### Pixel Coordinate Calculation
```
offset = x + y × width
```
For 16×16: `offset = x + y × 16` → `ADD R0, Rx, Ry, LSL #4`

### Example: Draw a red pixel at (3, 7)
```arm
    MOV  R9, #0x2000        ; display base address
    MOV  R0, #3             ; x coordinate
    MOV  R1, #7             ; y coordinate
    MOV  R2, #4             ; colour index 4 = red in VGA palette
    ADD  R3, R0, R1, LSL #4 ; offset = x + y * 16
    STRB R2, [R9, R3]       ; store pixel
    MOV  R10, #1            ; trigger display refresh
    MOV  R10, #0            ; reset trigger
```

### Example: Fill screen with colour
```arm
    MOV  R9, #0x2000
    MOV  R0, #0             ; counter
    MOV  R1, #256            ; total pixels (16×16)
    MOV  R2, #9             ; colour index 9 = light blue
loop
    STRB R2, [R9, R0]
    ADD  R0, R0, #1
    CMP  R0, R1
    BLT  loop
    MOV  R10, #1            ; refresh display
    MOV  R10, #0
```

---

## 8. Open Questions / Decisions Needed

1. **Grid size**: Fixed 16×16 or configurable? Start with 16×16?
2. **Base address**: Fixed 0x2000 or configurable via a setting?
3. **Colour model**: VGA 256-colour palette (matching existing practical)?
4. **R10 vs memory-mapped control register**: Which refresh trigger mechanism?
5. **Auto-animation**: Timer-based auto-continue, or manual Continue button only?
6. **Display tab vs pop-out**: Tab first, pop-out later?
7. **Performance**: How many instructions between R10 checks during Run mode?
8. **Memory initialization**: Should display memory be zero-initialized (black screen) when Display Mode is toggled on?
9. **Pixel grid rendering**: Canvas (recommended) or div-based?
10. **Palette display**: Show a small palette reference below the grid so students can look up colour indices?
Rensu: If the palette display is easy to implement great, but I think it might not be so easy to make it legible without taking up too much space. So for now, just a link to the website for the palette is probably sufficient.
---

## 9. Implementation Phases

### Phase 1: Basic Display Tab (MVP)
- [ ] Add Display to Views enum and DOM (Refs.fs, index.html)
- [ ] Create canvas rendering in Views.fs
- [ ] Embed VGA 256-colour palette
- [ ] Read memory range on each step/pause and render grid
- [ ] Enable Display Mode toggle
- [ ] Basic CSS styling

### Phase 2: R10 Refresh Trigger
- [ ] Add R10 breakpoint logic to Integration.fs asmStepDisplay
- [ ] Pause execution when R10 == 1 in Display Mode
- [ ] Add Continue button
- [ ] Reset R10 detection (transition-based, not level-based)

### Phase 3: Animation Controls
- [ ] Auto-continue with configurable delay
- [ ] Frame counter display
- [ ] Stop button during animation
- [ ] Speed control (FPS slider or dropdown)

### Phase 4: Polish & Practical Integration
- [ ] Palette reference display
- [ ] Configurable grid size (settings)
- [ ] Configurable base address
- [ ] Update arm-instructions.md with display mode documentation
- [ ] Sample programs
- [ ] Update practical worksheet for VisUAL2-SU

---

## 10. Reference: VGA 256-Colour Palette

The first 16 colours (standard CGA):
| Index | Colour | RGB |
|-------|--------|-----|
| 0 | Black | (0, 0, 0) |
| 1 | Blue | (0, 2, 170) |
| 2 | Green | (20, 170, 0) |
| 3 | Cyan | (0, 170, 170) |
| 4 | Red | (170, 0, 3) |
| 5 | Magenta | (170, 0, 170) |
| 6 | Brown | (170, 85, 0) |
| 7 | Light Grey | (170, 170, 170) |
| 8 | Dark Grey | (85, 85, 85) |
| 9 | Light Blue | (85, 85, 255) |
| 10 | Light Green | (85, 255, 85) |
| 11 | Light Cyan | (85, 255, 255) |
| 12 | Light Red | (255, 85, 85) |
| 13 | Light Magenta | (253, 85, 255) |
| 14 | Yellow | (255, 255, 85) |
| 15 | White | (255, 255, 255) |

Indices 16–31: Greyscale ramp (16 shades)
Indices 32–55: Colour wheel (saturated)
Indices 56–247: Extended colour cube (various saturations/brightnesses)
Indices 248–255: Reserved (black/unused)

Full palette is defined in `VisUAL_MemoryScreen/Display.cs` and the VGA standard.
