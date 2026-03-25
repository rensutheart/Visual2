; Display Animation Demo - Moving Colour Bar
; Demonstrates the R10 refresh trigger for frame-by-frame animation.
;
; Instructions:
;   1. Enable Display Mode in the Display tab
;   2. Click Run - execution pauses each frame when R10 is set to 1
;   3. Click Continue (or Run) to advance to the next frame
;   4. Enable "Auto" checkbox for automatic animation
;
; This program draws a horizontal colour bar that moves down the screen.
; Base address: 0x2000, VGA palette colours.
;
; ===== GRID SIZE CONFIGURATION =====
; Change these three values to match your Display tab grid size setting.
; 16x16:  WIDTH = 16,  TOTAL = 256,  WRAP_MASK = 0xF
; 32x32:  WIDTH = 32,  TOTAL = 1024, WRAP_MASK = 0x1F
; 64x64:  WIDTH = 64,  TOTAL = 4096, WRAP_MASK = 0x3F
; ===================================

        MOV R9, #0x2000     ; R9 = display base address

        ; --- Grid size parameters (edit these for 32x32 or 64x64) ---
        MOV R12, #16        ; WIDTH: pixels per row (16, 32, or 64)
        MUL R8, R12, R12    ; TOTAL: WIDTH * WIDTH (stored in R8 temporarily)
        PUSH {R8}           ; save TOTAL on stack for later use
        SUB R8, R12, #1     ; WRAP_MASK: WIDTH - 1 (0xF, 0x1F, or 0x3F)
        PUSH {R8}           ; save WRAP_MASK on stack

        MOV R11, #0         ; R11 = current bar Y position (row)

frame
        ; --- Recover parameters ---
        LDR R8, [SP, #4]    ; R8 = TOTAL (pixels to clear)
        LDR R2, [SP]        ; R2 = WRAP_MASK

        ; --- Clear screen to black (colour 0) ---
        MOV R0, #0          ; pixel index
        MOV R1, #0          ; colour 0 = black
clear
        ADD R3, R9, R0
        STRB R1, [R3]
        ADD R0, R0, #1
        CMP R0, R8
        BLT clear

        ; --- Draw a 3-row colour bar at row R11 ---
        ; Bar rows: R11, R11+1, R11+2 (wrapping at WIDTH)
        MOV R4, #0          ; R4 = bar row offset (0..2)

bar_row
        CMP R4, #3
        BGE bar_done

        ; Calculate actual row with wrapping: row = (R11 + R4) AND WRAP_MASK
        ADD R5, R11, R4     ; R5 = R11 + offset
        AND R5, R5, R2      ; wrap to 0..(WIDTH-1) using WRAP_MASK in R2

        ; Row start offset = row * WIDTH
        MUL R6, R5, R12     ; R6 = row * WIDTH

        ; Draw WIDTH pixels across this row
        MOV R7, #0          ; R7 = column

bar_col
        CMP R7, R12         ; compare against WIDTH
        BGE bar_next_row

        ; Colour varies by column: palette 32 + column + row_offset*4
        MOV R8, R4, LSL #2  ; shift for variety
        ADD R8, R8, R7
        ADD R8, R8, #32     ; base palette offset
        AND R8, R8, #0xFF   ; clamp to valid palette range

        ; Calculate pixel address and store
        ADD R0, R6, R7      ; offset = row*WIDTH + col
        ADD R3, R9, R0      ; address = base + offset
        STRB R8, [R3]

        ADD R7, R7, #1
        B bar_col

bar_next_row
        ADD R4, R4, #1
        B bar_row

bar_done
        ; --- Trigger display refresh ---
        MOV R10, #1          ; Signal the display to refresh and pause

        ; --- Advance bar position for next frame ---
        LDR R2, [SP]        ; reload WRAP_MASK
        ADD R11, R11, #1
        AND R11, R11, R2    ; wrap around at WIDTH

        B frame              ; loop forever (animation)

        END
