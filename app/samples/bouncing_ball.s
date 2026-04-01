; Bouncing Ball with Gradient Background
; =======================================
; A single bright ball bounces around the 16x16 display,
; reflecting off the edges.  The background is a vertical
; grey ramp drawn with UDIV.
;
; Ball position is tracked in Q8.8 fixed-point so the motion
; is smooth even at sub-pixel resolution.  Each frame the
; background is redrawn and the ball is plotted on top.
;
; Instructions:
;   1. Enable Display Mode in the Display tab
;   2. Run — the ball moves one step per frame
;   3. Enable "Auto" for smooth continuous animation
;
; Demonstrates: UDIV (gradient calculation), Q8.8 fixed-point
;   position/velocity, RSB for velocity reversal, ASR to
;   extract integer from fixed-point, LDRB / STRB, CMP,
;   nested loops, conditional MOV
;
; Bonus exercise for students: change initial velocity values
; in R6/R7 to see different bounce angles. Try negative values!

; ===== SETUP =====
        MOV  R9, #0x2000        ; display base address
        MOV  R12, #16           ; grid width

; ===== BALL STATE (Q8.8 fixed-point) =====
; 1.0 in Q8.8 = 256.  Position starts near centre.
; Velocity of ~1.3 px/frame horizontally, ~0.8 px/frame vertically.
        MOV  R0, #0x700         ; x = 7.0  (7 * 256 = 0x700)
        MOV  R1, #0x300         ; y = 3.0  (3 * 256 = 0x300)
        PUSH {R0, R1}           ; [SP]=bx, [SP+4]=by

        MOV  R0, #0x160         ; vx ≈ 1.375 px/frame in Q8.8
        MOV  R0, #0x150         ; vx ≈ 1.3 px/frame (corrected)
        MOV  R1, #0xC0          ; vy = 0.75 px/frame in Q8.8
        PUSH {R0, R1}           ; [SP]=vx, [SP+4]=vy

; ===== GRADIENT PARAMETERS =====
; Background colour: row y gets palette index 16 + y
; (VGA palette 16-31 is a greyscale ramp, dark to light)

; ===== MAIN ANIMATION LOOP =====
frame
        ; --- Phase 1: Draw gradient background ---
        MOV  R4, #0             ; y = row index
bg_row
        CMP  R4, R12
        BGE  bg_done

        ; colour = 16 + y  (greyscale ramp 16..31)
        ADD  R5, R4, #16

        ; Fill row: WIDTH pixels
        MUL  R0, R4, R12        ; row start offset = y * 16
        MOV  R3, #0             ; x = column
bg_col
        CMP  R3, R12
        BGE  bg_row_done
        ADD  R1, R0, R3         ; pixel offset = y*16 + x
        ADD  R2, R9, R1         ; pixel address = base + offset
        STRB R5, [R2]           ; write gradient colour
        ADD  R3, R3, #1
        B    bg_col

bg_row_done
        ADD  R4, R4, #1
        B    bg_row
bg_done

        ; --- Phase 2: Update ball position ---
        ; bx += vx,  by += vy
        LDR  R4, [SP, #8]      ; bx
        LDR  R5, [SP, #12]     ; by
        LDR  R6, [SP]          ; vx
        LDR  R7, [SP, #4]      ; vy

        ADD  R4, R4, R6        ; bx += vx
        ADD  R5, R5, R7        ; by += vy

        ; --- Phase 3: Bounce check ---
        ; Boundaries: 0.0 (0) to 14.0 (0xE00) in Q8.8
        ; Upper bound is 14 so the 2x2 ball fits fully (occupies cols/rows 14..15)
        ; If bx < 0: negate vx, clamp bx to 0
        CMP  R4, #0
        RSBLT R6, R6, #0       ; reverse vx
        MOVLT R4, #0            ; clamp bx
        ; If bx > 14.0 (0xE00):
        MOV  R0, #0xE00
        CMP  R4, R0
        RSBGT R6, R6, #0       ; reverse vx
        MOVGT R4, R0            ; clamp bx

        ; Same for by
        CMP  R5, #0
        RSBLT R7, R7, #0
        MOVLT R5, #0
        CMP  R5, R0             ; R0 still = 0xE00
        RSBGT R7, R7, #0
        MOVGT R5, R0

        ; Save updated state back to stack
        STR  R4, [SP, #8]      ; bx
        STR  R5, [SP, #12]     ; by
        STR  R6, [SP]          ; vx
        STR  R7, [SP, #4]      ; vy

        ; --- Phase 4: Draw ball ---
        ; Extract integer pixel coordinates: px = bx >> 8, py = by >> 8
        MOV  R2, R4, ASR #8    ; px
        MOV  R3, R5, ASR #8    ; py

        ; Draw a 2x2 ball for visibility
        ; Pixel (px, py)
        MUL  R0, R3, R12       ; py * 16  (Rd=R0 ≠ Rm=R3 ✓)
        ADD  R0, R0, R2        ; + px
        ADD  R0, R0, R9        ; address
        MOV  R1, #14           ; bright yellow
        STRB R1, [R0]

        ; Pixel (px+1, py) — if in bounds
        ADD  R8, R2, #1
        CMP  R8, #16
        BGE  skip_r
        ADD  R0, R0, #1
        STRB R1, [R0]
        SUB  R0, R0, #1        ; restore address
skip_r
        ; Pixel (px, py+1) — if in bounds
        ADD  R8, R3, #1
        CMP  R8, #16
        BGE  skip_d
        ADD  R0, R0, R12       ; next row
        STRB R1, [R0]

        ; Pixel (px+1, py+1)
        ADD  R8, R2, #1
        CMP  R8, #16
        BGE  skip_d
        ADD  R0, R0, #1
        STRB R1, [R0]
skip_d

        ; --- Phase 5: Trigger display refresh ---
        MOV  R10, #1
        B    frame

        END
