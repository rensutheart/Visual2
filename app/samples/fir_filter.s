; FIR Filter Demo - Memory-Mapped Pixel Display
; Demonstrates a 2D lowpass FIR filter (spatial convolution)
; applied repeatedly to a pixel pattern, using the R10 refresh
; trigger to animate each successive filter pass.
;
; Instructions:
;   1. Enable Display Mode in the Display tab
;   2. Set grid size to 16x16
;   3. Click Run - the initial pattern appears (frame 0)
;   4. Click Continue (or enable Auto) to step through filter passes
;
; What is a FIR filter?
;   A Finite Impulse Response filter computes each output pixel
;   as a weighted sum of its neighbours. This particular kernel
;   is a lowpass (smoothing/blurring) filter:
;
;         [0  1  0]
;         [1  4  1]  / 8      (divide by 8 = LSR #3)
;         [0  1  0]
;
;   The centre pixel gets weight 4, each direct neighbour gets
;   weight 1. Total weight = 8, so we divide by shifting right 3.
;
; What to observe:
;   - The dot (impulse) quickly spreads and fades
;   - The square keeps its centre but edges soften
;   - The line widens vertically while staying sharp along its length
;   - Palette indices 32-44 map to blue-purple-red-orange-yellow,
;     so the blur halos create a colourful fire-gradient effect
;
; VGA palette reference (key indices):
;   32 = blue       36 = purple     40 = red
;   42 = orange     44 = yellow
;
; Memory layout:
;   0x2000 - display buffer (16x16 = 256 bytes)
;   0x2100 - temporary buffer for filtered output

        ; === Setup ===
        MOV R9, #0x2000     ; display base address
        MOV R11, #0x2100    ; temp buffer base

        ; === Fill display with blue background (palette 32) ===
        MOV R0, #0
        MOV R1, #32
clr     ADD R2, R9, R0
        STRB R1, [R2]
        ADD R0, R0, #1
        CMP R0, #256
        BLT clr

        ; === Draw yellow shapes (palette 44) ===
        MOV R2, #44

        ; (a) Single dot at (x=2, y=2) -- impulse response
        ADD R0, R9, #34     ; offset = 2*16 + 2 = 34
        STRB R2, [R0]

        ; (b) 4x4 square at rows 6-9, cols 6-9 -- edge blur
        MOV R3, #6
sq_r    CMP R3, #10
        BGE sq_dn
        MOV R4, #6
sq_c    CMP R4, #10
        BGE sq_nr
        ADD R7, R4, R3, LSL #4
        ADD R0, R9, R7
        STRB R2, [R0]
        ADD R4, R4, #1
        B sq_c
sq_nr   ADD R3, R3, #1
        B sq_r
sq_dn

        ; (c) Horizontal line at row 13, cols 2-13 -- line blur
        MOV R4, #2
ln_lp   CMP R4, #14
        BGE ln_dn
        ADD R7, R4, #208    ; 13*16 = 208; offset = 208 + col
        ADD R0, R9, R7
        STRB R2, [R0]
        ADD R4, R4, #1
        B ln_lp
ln_dn

        ; === Show initial pattern (frame 0) ===
        MOV R10, #1
        MOV R10, #0

        ; === FIR filter loop: 8 passes ===
        MOV R8, #0

filt    CMP R8, #8
        BGE fin

        ; --- Filter interior pixels (y=1..14, x=1..14) ---
        ; The border ring stays fixed at blue (32), providing a
        ; constant boundary condition. Interior pixels always
        ; have 4 valid neighbours so no bounds checks are needed.

        MOV R3, #1          ; y = 1

fy      CMP R3, #15
        BGE cpbk

        MOV R4, #1          ; x = 1

fx      CMP R4, #15
        BGE fxdn

        ; Source pixel address
        ADD R7, R4, R3, LSL #4   ; offset = col + row*16
        ADD R0, R9, R7           ; R0 = &display[y][x]

        ; Weighted centre: centre * 4
        LDRB R5, [R0]
        MOV R5, R5, LSL #2

        ; Up neighbour (y-1)
        SUB R1, R0, #16
        LDRB R6, [R1]
        ADD R5, R5, R6

        ; Down neighbour (y+1)
        ADD R1, R0, #16
        LDRB R6, [R1]
        ADD R5, R5, R6

        ; Left neighbour (x-1)
        SUB R1, R0, #1
        LDRB R6, [R1]
        ADD R5, R5, R6

        ; Right neighbour (x+1)
        ADD R1, R0, #1
        LDRB R6, [R1]
        ADD R5, R5, R6

        ; Divide weighted sum by 8
        MOV R5, R5, LSR #3

        ; Store result in temp buffer
        ADD R1, R11, R7
        STRB R5, [R1]

        ADD R4, R4, #1
        B fx

fxdn    ADD R3, R3, #1
        B fy

        ; --- Copy filtered interior back to display ---
cpbk    MOV R3, #1

cpy     CMP R3, #15
        BGE cpdn
        MOV R4, #1

cpx     CMP R4, #15
        BGE cpxn
        ADD R7, R4, R3, LSL #4
        ADD R0, R11, R7     ; source: temp buffer
        LDRB R2, [R0]
        ADD R0, R9, R7      ; dest: display buffer
        STRB R2, [R0]
        ADD R4, R4, #1
        B cpx

cpxn    ADD R3, R3, #1
        B cpy

cpdn
        ; --- Trigger display refresh (pauses execution) ---
        MOV R10, #1
        MOV R10, #0

        ADD R8, R8, #1
        B filt

fin     END
