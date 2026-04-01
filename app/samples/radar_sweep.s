; Radar Sweep Animation
; =====================
; A rotating line sweeps around a circular radar display on the
; 16x16 pixel grid, leaving a fading trail.
;
; How it works:
;   Each frame, every non-black pixel fades toward black via a
;   greyscale ramp. A bright green sweep line is drawn from the
;   centre (8,8) to the current edge point using DCD lookup
;   tables (24 angles at 15° spacing). The green leading edge
;   transitions to bright grey and fades through progressively
;   darker greys, producing a classic radar afterglow.
;
; Instructions:
;   1. Enable Display Mode in the Display tab
;   2. Run the program — it pauses at each MOV R10, #1
;   3. Click Continue repeatedly, or enable "Auto" for animation
;
; Demonstrates: DCD lookup tables, LDR =label (load address),
;   MUL, ASR (arithmetic shift right), LDRB/STRB, CMP with
;   conditional SUB (SUBGE), CMP for modular wrapping

; ===== SETUP =====
        MOV  R9, #0x2000        ; display base address
        MOV  R12, #16           ; grid width
        MOV  R11, #0            ; current angle index (0..23)

; ===== MAIN ANIMATION LOOP =====
frame
        ; --- Phase 1: Fade every pixel toward black ---
        MOV  R0, #0             ; pixel offset counter
        MUL  R8, R12, R12       ; R8 = total pixels (256)
fade
        ADD  R3, R9, R0         ; pixel address = base + offset
        LDRB R1, [R3]           ; current colour
        CMP  R1, #32
        MOVGE R1, #28           ; green (or saturated) → bright grey
        BGE  store_f
        CMP  R1, #17
        SUBGE R1, R1, #3        ; greyscale fade by 3
        MOVLT R1, #0            ; below grey range → black
store_f
        STRB R1, [R3]
        ADD  R0, R0, #1
        CMP  R0, R8
        BLT  fade

        ; --- Phase 2: Draw sweep line from centre to edge ---
        ; Load endpoint for the current angle from lookup tables
        LDR  R4, =sweep_x      ; R4 = address of x-coordinates
        LDR  R5, =sweep_y      ; R5 = address of y-coordinates
        LDR  R4, [R4, R11, LSL #2]  ; R4 = endpoint x
        LDR  R5, [R5, R11, LSL #2]  ; R5 = endpoint y

        ; Direction: dx = endpoint_x - 8, dy = endpoint_y - 8
        SUB  R2, R4, #8        ; R2 = dx
        SUB  R3, R5, #8        ; R3 = dy

        ; Interpolate 9 points (step 0..8) from centre to edge.
        ; For step i: x = 8 + (dx*i)/8,  y = 8 + (dy*i)/8
        ; Division by 8 is done with ASR #3.
        MOV  R6, #0            ; step counter
draw_line
        CMP  R6, #8
        BGT  line_done

        ; Compute x = 8 + (dx * step) >> 3
        MUL  R0, R2, R6        ; R0 = dx * step
        MOV  R7, R0, ASR #3    ; R7 = (dx*step) / 8
        ADD  R7, R7, #8        ; x coordinate

        ; Compute y = 8 + (dy * step) >> 3
        MUL  R0, R3, R6        ; R0 = dy * step
        MOV  R8, R0, ASR #3    ; R8 = (dy*step) / 8
        ADD  R8, R8, #8        ; y coordinate

        ; Bounds check (skip if outside 0..15)
        CMP  R7, #0
        BLT  next_pt
        CMP  R7, #15
        BGT  next_pt
        CMP  R8, #0
        BLT  next_pt
        CMP  R8, #15
        BGT  next_pt

        ; Pixel address = base + y * WIDTH + x
        MUL  R1, R8, R12       ; R1 = y * 16
        ADD  R1, R1, R7        ; + x
        ADD  R1, R1, R9        ; + base address

        ; Colour: bright green (VGA index 47)
        MOV  R0, #47
        STRB R0, [R1]

next_pt
        ADD  R6, R6, #1
        B    draw_line

line_done
        ; --- Phase 3: Refresh display and advance angle ---
        MOV  R10, #1            ; trigger display refresh + pause

        ADD  R11, R11, #1       ; next angle
        CMP  R11, #24           ; wrap to 0..23
        MOVGE R11, #0
        B    frame

; ===== LOOKUP TABLES =====
; 24 endpoints on a circle of radius 7, centred at (8, 8).
; Angles: 0°, 15°, 30° … 345° (clockwise from east).
sweep_x DCD  15, 15, 14, 13, 12, 10, 8, 6, 5, 3, 2, 1
        DCD  1, 1, 2, 3, 5, 6, 8, 10, 12, 13, 14, 15
sweep_y DCD  8, 10, 12, 13, 14, 15, 15, 15, 14, 13, 12, 10
        DCD  8, 6, 5, 3, 2, 1, 1, 1, 2, 3, 5, 6

        END
