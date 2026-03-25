# Memory-Mapped Display

VisUAL2-SU includes a built-in memory-mapped pixel display. You write ARM assembly that stores colour values into a dedicated memory region, and the display renders those bytes as a grid of coloured pixels — all within the simulator.

---

## Enabling Display Mode

1. Open the **Display** tab (alongside Registers, Memory, Symbols)
2. Check the **Display Mode** checkbox

When Display Mode is **off**, everything works as normal — R10 is a regular register and memory at the display address is ordinary data memory.

When Display Mode is **on**:

- Register **R10** becomes the **refresh trigger** (see [Refresh Trigger](#refresh-trigger) below)
- Memory starting at address `0x2000` is rendered as pixels on the display grid
- The display updates automatically when you **Step** through instructions

---

## Display Specification

| Property | Value |
|----------|-------|
| Base address | `0x2000` |
| Colour model | VGA 256-colour palette (1 byte per pixel) |
| Pixel ordering | Row-major, top-left origin |
| Grid sizes | 16×16, 32×32, or 64×64 (selectable via dropdown) |

Each byte in the display memory region is an index (0–255) into the standard [VGA 256-colour palette](https://www.fountainware.com/EXPL/vga_color_palettes.htm). The number of bytes used depends on the grid size:

| Grid Size | Pixels | Memory Used |
|-----------|--------|-------------|
| 16×16 | 256 | 256 bytes (`0x2000`–`0x20FF`) |
| 32×32 | 1024 | 1024 bytes (`0x2000`–`0x23FF`) |
| 64×64 | 4096 | 4096 bytes (`0x2000`–`0x2FFF`) |

### Pixel Addressing

To write a pixel at column `x`, row `y`:

$$\text{address} = \texttt{0x2000} + y \times \text{width} + x$$

For example, on a 16×16 grid, pixel (5, 2) is at address `0x2000 + 2×16 + 5 = 0x2025`.

---

## Drawing Pixels

Use `STRB` (store byte) to write a colour index to the pixel address:

```arm
        MOV  R9, #0x2000        ; base address of display memory
        ; Draw pixel at (x=5, y=2) with colour index 9 (bright blue)
        MOV  R0, #5             ; x coordinate
        MOV  R1, #2             ; y coordinate
        MOV  R2, #9             ; VGA palette colour index
        ADD  R0, R0, R1, LSL #4 ; offset = x + y * 16
        STRB R2, [R9, R0]       ; write pixel to display memory
```

> **Tip:** Use `STRB` (not `STR`) — each pixel is a single byte. Writing a full word with `STR` would overwrite 4 adjacent pixels.

---

## Refresh Trigger (R10)

When Display Mode is active and you **Run** your program, execution checks register **R10** after each instruction:

- When `R10 == 1`, execution **pauses**, the display **refreshes**, and R10 is automatically **cleared back to 0**
- You then click **Continue** (or **Run**) to resume execution

This gives you frame-by-frame control for animations.

### Animation Flow

```
Your code                        Simulator behaviour
─────────                        ───────────────────
; draw frame 1
STRB R2, [R9, R0]
...
MOV R10, #1          ──────►    Display refreshes, execution PAUSES
                                (R10 automatically cleared to 0)
                     ◄──────    You click Continue
; draw frame 2
STRB R2, [R9, R1]
...
MOV R10, #1          ──────►    Display refreshes, execution PAUSES again
```

### Typical Animation Loop

```arm
        MOV R9, #0x2000     ; display base address

frame
        ; ... draw pixels for this frame ...

        MOV R10, #1          ; trigger display refresh (pauses here)

        ; ... update animation state for next frame ...

        B frame              ; loop forever
        END
```

> **Note:** When stepping through instructions (rather than running), the display updates after every step — you do not need R10 for single-step mode.

---

## Animation Controls

The Display tab includes controls for automating animation playback:

| Control | Description |
|---------|-------------|
| **Continue ▶** | Resume execution until the next R10 trigger or program end (disabled when Auto is on) |
| **Clear** | Zero all display memory and clear the canvas |
| **Auto** checkbox | When checked, automatically continues after each frame (no need to click Continue) |
| **FPS** dropdown | Animation speed when Auto is enabled: 1, 2, 4, 10, 20, or 30 frames per second |
| **Frame** counter | Shows how many frames have been rendered since the last reset |
| **Grid size** dropdown | Switch between 16×16, 32×32, and 64×64 pixel grids |

---

## Examples

### Static Pattern

This program fills the grid with coloured stripes. Change `WIDTH` to match your grid size:

```arm
; Colourful stripe pattern — works with any grid size
; Change the WIDTH value to match your Display tab setting.

        MOV R9, #0x2000     ; display base address
        MOV R11, #16        ; WIDTH: 16, 32, or 64
        MUL R12, R11, R11   ; TOTAL = WIDTH * WIDTH
        MOV R8, R11, LSR #2 ; BAND_HEIGHT = WIDTH / 4

        MOV R3, #0          ; current row
        MOV R0, #0          ; pixel index

row_loop
        CMP R3, R11
        BGE done
        MOV R4, #0          ; group = row / BAND_HEIGHT
        MOV R5, R3
grp_div CMP R5, R8
        BLT grp_end
        SUB R5, R5, R8
        ADD R4, R4, #1
        B grp_div
grp_end MOV R5, R4, LSL #4
        ADD R5, R5, #32     ; base palette offset
        AND R5, R5, #0xFF
        MOV R6, #0          ; column
col_loop
        CMP R6, R11
        BGE col_done
        AND R7, R6, #0xF
        ADD R2, R5, R7      ; colour index
        ADD R1, R9, R0
        STRB R2, [R1]
        ADD R0, R0, #1
        ADD R6, R6, #1
        B col_loop
col_done
        ADD R3, R3, #1
        B row_loop

done    END
```

Run this program, then switch to the Display tab to see the result. No R10 trigger is needed for a static image — the display shows the current memory state whenever execution is paused.

### Animation (Moving Colour Bar)

This program animates a colour bar moving down the screen. Change `WIDTH` to match your grid size:

```arm
; Moving colour bar — works with any grid size
; Change the WIDTH value to match your Display tab setting.

        MOV R9, #0x2000     ; display base address
        MOV R12, #16        ; WIDTH: 16, 32, or 64
        MUL R8, R12, R12    ; TOTAL
        PUSH {R8}
        SUB R8, R12, #1     ; WRAP_MASK
        PUSH {R8}
        MOV R11, #0         ; bar Y position

frame   LDR R8, [SP, #4]    ; TOTAL
        LDR R2, [SP]        ; WRAP_MASK
        MOV R0, #0
        MOV R1, #0          ; black
clear   ADD R3, R9, R0
        STRB R1, [R3]
        ADD R0, R0, #1
        CMP R0, R8
        BLT clear

        MOV R4, #0          ; bar row offset (0..2)
bar_row CMP R4, #3
        BGE bar_done
        ADD R5, R11, R4
        AND R5, R5, R2      ; wrap row
        MUL R6, R5, R12     ; row offset
        MOV R7, #0
bar_col CMP R7, R12
        BGE bar_next
        MOV R8, R4, LSL #2
        ADD R8, R8, R7
        ADD R8, R8, #32
        AND R8, R8, #0xFF
        ADD R0, R6, R7
        ADD R3, R9, R0
        STRB R8, [R3]
        ADD R7, R7, #1
        B bar_col
bar_next ADD R4, R4, #1
        B bar_row

bar_done
        MOV R10, #1          ; refresh and pause
        LDR R2, [SP]         ; reload WRAP_MASK
        ADD R11, R11, #1
        AND R11, R11, R2     ; wrap
        B frame

        END
```

1. Enable Display Mode
2. Click **Run** — the first frame renders, execution pauses
3. Click **Continue** to advance frame by frame, or enable **Auto** for continuous animation

> **Tip:** You can also load these examples from the **Help** menu: *Load display demo code* (static) and *Load display animation demo* (animated).

---

## Quick Reference

| Task | How |
|------|-----|
| Set a pixel | `STRB colour, [base, offset]` where offset = x + y × width |
| Trigger a display refresh | `MOV R10, #1` (Display Mode must be on) |
| Change grid size | Use the grid size dropdown in the Display tab |
| Clear the display | Click the **Clear** button |
| Animate automatically | Check **Auto** and select a frame rate |
| Load example code | **Help** menu → *Load display demo code* or *Load display animation demo* |
