; Mandelbrot Set
; ==============
; Renders the Mandelbrot set on the pixel display using
; Q8.8 fixed-point arithmetic (8 integer bits, 8 fractional).
; Best viewed at 64×64 resolution — set Grid Width to 64 in
; the Display tab before running.
;
; For each pixel (px, py) the program maps to a complex number
;   c = cr + ci*j  in the region  x: [-2.1, 0.6], y: [-1.35, 1.35]
; and iterates  z = z^2 + c  up to 24 times.  Pixels that escape
; (|z| > 2) are coloured from the VGA saturated rainbow palette;
; pixels inside the set are black.
;
; This is a static image — no animation, runs once and stops.
;
; Instructions:
;   1. Enable Display Mode in the Display tab
;   2. Click Run — the image takes a few seconds to compute
;
; Demonstrates: MUL for fixed-point multiply, ASR for Q8.8
;   conversion, RSB (reverse subtract) for negation, nested
;   loops, BL / BX LR subroutines, CMP / BGT for escape test

; ===== FIXED-POINT Q8.8 CONSTANTS =====
; 1.0  = 256   (0x100)
; -2.12 = -542 (built as 512+30 then negated)
; -1.35 = -347 (built as 320+27 then negated)
; step  = 2.7/63 ≈ 0.043 → 11 in Q8.8
; escape² = 4.0 = 1024 in Q8.8

; ===== SETUP =====
        MOV  R9, #0x2000        ; display base address
        MOV  R12, #64           ; grid width
        MOV  R11, #24           ; max iterations

        ; Build Q8.8 constants
        MOV  R0, #0x200         ; 512
        ADD  R0, R0, #30        ; 542
        RSB  R0, R0, #0         ; R0 = -542 = -2.12 in Q8.8
        PUSH {R0}               ; [SP+8] = x_start = -542

        MOV  R0, #0x140         ; 320
        ADD  R0, R0, #27        ; 347
        RSB  R0, R0, #0         ; R0 = -347 = -1.36 in Q8.8
        PUSH {R0}               ; [SP+4] = y_start = -347

        MOV  R0, #11            ; step ≈ 0.043 in Q8.8
        PUSH {R0}               ; [SP]   = step = 11

; ===== OUTER LOOPS: iterate over pixels =====
        MOV  R4, #0             ; R4 = py (pixel row, 0..63)
py_loop
        CMP  R4, R12
        BGE  done

        MOV  R5, #0             ; R5 = px (pixel column, 0..63)
px_loop
        CMP  R5, R12
        BGE  px_done

        ; --- Map pixel to complex c = (cr, ci) ---
        ; cr = x_start + px * step
        ; ci = y_start + py * step
        LDR  R0, [SP]           ; step
        MUL  R1, R5, R0         ; px * step  (Rd=R1 ≠ Rm=R5 ✓)
        LDR  R2, [SP, #8]       ; x_start
        ADD  R2, R2, R1         ; cr = x_start + px*step

        MUL  R1, R4, R0         ; py * step  (Rd=R1 ≠ Rm=R4 ✓)
        LDR  R3, [SP, #4]       ; y_start
        ADD  R3, R3, R1         ; ci = y_start + py*step

        ; --- Mandelbrot iteration ---
        ; z_r = 0, z_i = 0;  iterate z = z^2 + c
        PUSH {R2, R3}           ; save cr, ci
        MOV  R6, #0             ; z_r (Q8.8)
        MOV  R7, #0             ; z_i (Q8.8)
        MOV  R8, #0             ; iteration counter

iter_loop
        CMP  R8, R11            ; reached max iterations?
        BGE  iter_done

        ; z_r_sq = z_r * z_r >> 8   (Q16.16 → Q8.8)
        MUL  R0, R6, R6         ; z_r * z_r  (Rd=R0 ≠ Rm=R6 ✓)
        MOV  R0, R0, ASR #8     ; R0 = z_r²  in Q8.8

        ; z_i_sq = z_i * z_i >> 8
        MUL  R1, R7, R7         ; z_i * z_i  (Rd=R1 ≠ Rm=R7 ✓)
        MOV  R1, R1, ASR #8     ; R1 = z_i²  in Q8.8

        ; Escape test: z_r² + z_i² > 4.0 (1024 in Q8.8)?
        ADD  R10, R0, R1        ; R10 = z_r² + z_i²
        MOV  R3, #0x400         ; 1024 = 4.0 in Q8.8
        CMP  R10, R3
        BGT  escaped

        ; z_i_new = 2 * z_r * z_i + ci
        ; Trick: shift the product right by 7 instead of 8 to double it
        MUL  R10, R6, R7        ; z_r * z_i  (Rd=R10 ≠ Rm=R6 ✓)
        MOV  R10, R10, ASR #7   ; 2 * z_r * z_i in Q8.8
        LDR  R3, [SP, #4]       ; ci  (saved on stack)
        ADD  R7, R10, R3        ; z_i_new = 2*zr*zi + ci

        ; z_r_new = z_r² - z_i² + cr
        SUB  R0, R0, R1         ; z_r² - z_i²
        LDR  R3, [SP]           ; cr  (saved on stack)
        ADD  R6, R0, R3         ; z_r_new = zr²-zi² + cr

        ADD  R8, R8, #1
        B    iter_loop

escaped
iter_done
        POP  {R2, R3}           ; restore cr, ci (clean up stack)

        ; --- Colour the pixel ---
        ; Inside the set (R8 == max_iter) → black (0)
        ; Outside → colour by iteration count
        CMP  R8, R11
        MOVEQ R8, #0            ; set → black
        ADDNE R8, R8, #32       ; outside → rainbow palette (33..55)
        ; Non-set pixels map to VGA saturated rainbow band

        ; Pixel address = base + py * WIDTH + px
        MUL  R0, R4, R12        ; py * 16  (Rd=R0 ≠ Rm=R4 ✓)
        ADD  R0, R0, R5         ; + px
        ADD  R0, R0, R9         ; + base
        STRB R8, [R0]           ; write colour

        ADD  R5, R5, #1         ; next px
        B    px_loop

px_done
        ADD  R4, R4, #1         ; next py
        B    py_loop

done
        ; Clean up the stack (remove 3 pushed words)
        ADD  SP, SP, #12

        END
