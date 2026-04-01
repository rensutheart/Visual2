; Spirograph / Lissajous Curves
; =============================
; Draws parametric curves on the 16x16 display using a 32-entry
; sine lookup table stored with DCD.
;
; The curve is defined by:
;     x(t) = A * sin(a * t)  +  centre
;     y(t) = B * sin(b * t)  +  centre
; where a and b are integer frequency parameters.
; For a:b = 3:2 this produces a classic Lissajous figure.
;
; The program animates by plotting one new point per frame,
; gradually building up the curve.  After a full cycle it
; changes the frequency ratio and colour to overlay a new
; pattern, creating a spirograph-like composition.
;
; Instructions:
;   1. Enable Display Mode in the Display tab
;   2. Run — one dot appears per frame
;   3. Enable "Auto" for smooth drawing animation
;
; Demonstrates: DCD lookup table (sine), LDR =label,
;   MUL + ASR for scaled lookup, AND for modular indexing,
;   PUSH / POP, nested loops, multiple patterns over time

; ===== SETUP =====
        MOV  R9, #0x2000        ; display base address
        MOV  R12, #16           ; grid width

; ===== PATTERN PARAMETERS =====
; Each pattern runs for 32 steps (one full sine cycle).
; freq_a, freq_b control the Lissajous shape.
; We cycle through 4 parameter sets.
        MOV  R11, #0            ; current step (0..31)
        MOV  R4, #0             ; pattern index (0..3)

; ===== MAIN ANIMATION LOOP =====
frame
        ; --- Load frequency pair for current pattern ---
        ; Pattern 0: a=3, b=2  (Lissajous 3:2)
        ; Pattern 1: a=5, b=4  (Lissajous 5:4)
        ; Pattern 2: a=3, b=4  (Lissajous 3:4)
        ; Pattern 3: a=7, b=6  (Lissajous 7:6)
        LDR  R5, =freq_a
        LDR  R5, [R5, R4, LSL #2]   ; R5 = freq_a for this pattern
        LDR  R6, =freq_b
        LDR  R6, [R6, R4, LSL #2]   ; R6 = freq_b for this pattern

        ; --- Compute x-index into sine table ---
        ; index_x = (freq_a * step) AND 0x1F   (mod 32)
        MUL  R0, R5, R11       ; R0 = freq_a * step
        AND  R0, R0, #0x1F     ; mod 32

        ; Look up sin(index_x), scaled to [-7, +7]
        LDR  R1, =sin_tab
        LDR  R2, [R1, R0, LSL #2]   ; R2 = sin_tab[index_x]
        ADD  R2, R2, #8              ; shift to display centre → x in [1..15]

        ; --- Compute y-index into sine table ---
        MUL  R0, R6, R11       ; R0 = freq_b * step
        AND  R0, R0, #0x1F     ; mod 32

        LDR  R3, [R1, R0, LSL #2]   ; R3 = sin_tab[index_y]
        ADD  R3, R3, #8              ; shift to display centre → y in [1..15]

        ; --- Plot the pixel ---
        ; Bounds check (should always pass, but safety first)
        CMP  R2, #0
        BLT  skip
        CMP  R2, #15
        BGT  skip
        CMP  R3, #0
        BLT  skip
        CMP  R3, #15
        BGT  skip

        ; Address = base + y * 16 + x
        MUL  R0, R3, R12       ; y * WIDTH  (Rd=R0 ≠ Rm=R3 ✓)
        ADD  R0, R0, R2        ; + x
        ADD  R0, R0, R9        ; + base

        ; Colour varies by pattern: palette offsets 9, 12, 14, 11
        LDR  R1, =colours
        LDR  R1, [R1, R4, LSL #2]
        STRB R1, [R0]

skip
        ; --- Trigger display refresh ---
        MOV  R10, #1

        ; --- Advance step ---
        ADD  R11, R11, #1
        CMP  R11, #32
        BLT  frame

        ; Finished one full cycle — move to next pattern
        MOV  R11, #0
        ADD  R4, R4, #1
        AND  R4, R4, #3        ; wrap pattern index to 0..3
        B    frame

; ===== LOOKUP TABLES =====

; Sine table: 32 entries, sin(i * 11.25°) scaled to [-7, +7].
; Stored as signed 32-bit words for easy LDR access.
;
; i :  0    1    2    3    4    5    6    7    8    9   10   11   12   13   14   15
; sin: 0    1    3    4    5    6    6    7    7    7    6    6    5    4    3    1
; i : 16   17   18   19   20   21   22   23   24   25   26   27   28   29   30   31
; sin: 0   -1   -3   -4   -5   -6   -6   -7   -7   -7   -6   -6   -5   -4   -3   -1

sin_tab DCD  0,  1,  3,  4,  5,  6,  6,  7
        DCD  7,  7,  6,  6,  5,  4,  3,  1
        DCD  0, -1, -3, -4, -5, -6, -6, -7
        DCD -7, -7, -6, -6, -5, -4, -3, -1

; Frequency parameters for each pattern
freq_a  DCD  3, 5, 3, 7
freq_b  DCD  2, 4, 4, 6

; Colour per pattern (VGA palette indices)
colours DCD  9, 12, 14, 11

        END
