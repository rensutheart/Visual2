; South African Flag - Memory-Mapped Pixel Display
; Draws the flag of South Africa on the 16x16 pixel display.
; Base address: 0x2000 (one byte per pixel, VGA palette index)
;
; Instructions:
;   1. Open the Display tab and enable Display Mode
;   2. Set grid size to 16x16 (default)
;   3. Click Run (or step through)
;   4. The SA flag will appear on the display
;
; Flag geometry (16x16):
;   - Black triangle on the left (hoist side)
;   - Gold (yellow) fimbriation around the black
;   - Green Y-shaped pall extending across the flag
;   - White fimbriation between green and red/blue
;   - Red on the upper right
;   - Blue on the lower right
;
; VGA palette colours used:
;   0  = Black    (#000000)
;   1  = Blue     (#0000AA)
;   2  = Green    (#00AA00)
;   4  = Red      (#AA0000)
;   14 = Yellow   (gold)
;   15 = White    (#FFFFFF)

        MOV R9, #0x2000     ; R9 = display base address
        MOV R0, #0          ; R0 = pixel offset (byte index into display)

        ; === Outer loop: rows (y = R3, from 0 to 15) ===
        MOV R3, #0

row_loop
        CMP R3, #16
        BGE done

        ; === Inner loop: columns (x = R6, from 0 to 15) ===
        MOV R6, #0

col_loop
        CMP R6, #16
        BGE col_done

        ; ----- Determine colour for pixel (x=R6, y=R3) -----

        ; Branch: diagonal section (x <= 5) vs horizontal section (x > 5)
        CMP R6, #5
        BGT horiz

        ; === DIAGONAL SECTION (x = 0..5) ===
        ; Boundary lines (all functions of x):
        ;   upper_white = x           (R4)
        ;   lower_white = 15 - x      (R5)
        ;   gold_upper  = 3 + x       (R7)
        ;   gold_lower  = 12 - x      (R8)

        MOV R4, R6          ; R4 = upper_white = x
        RSB R5, R6, #15     ; R5 = lower_white = 15 - x
        ADD R7, R6, #3      ; R7 = gold_upper  = 3 + x
        RSB R8, R6, #12     ; R8 = gold_lower  = 12 - x

        ; y < upper_white  -->  RED
        CMP R3, R4
        BLT set_red

        ; y == upper_white  -->  WHITE
        BEQ set_white

        ; y < gold_upper  -->  GREEN  (upper green arm)
        CMP R3, R7
        BLT set_green

        ; Check whether the gold/black zone exists (gold_upper < gold_lower)
        CMP R7, R8
        BGE no_gold         ; Golds have crossed — no black triangle at this x

        ; --- Gold zone active ---
        ; y == gold_upper  -->  GOLD
        CMP R3, R7
        BEQ set_gold

        ; y < gold_lower  -->  BLACK
        CMP R3, R8
        BLT set_black

        ; y == gold_lower  -->  GOLD
        BEQ set_gold

        ; y > gold_lower: fall through to green / white / blue

no_gold
        ; y < lower_white  -->  GREEN  (lower green arm or merged band)
        CMP R3, R5
        BLT set_green

        ; y == lower_white  -->  WHITE
        BEQ set_white

        ; else  -->  BLUE
        B set_blue

        ; === HORIZONTAL SECTION (x = 6..15) ===
        ; Constant boundaries:  white at y=5, green y=6..9, white at y=10
horiz
        CMP R3, #5
        BLT set_red         ; y < 5  -->  RED
        BEQ set_white       ; y == 5 -->  WHITE
        CMP R3, #10
        BLT set_green       ; y < 10 -->  GREEN  (y = 6..9)
        BEQ set_white       ; y == 10 --> WHITE
        B set_blue          ; y > 10 -->  BLUE

        ; === Colour assignments ===
set_red
        MOV R2, #4          ; VGA index 4 = Red
        B store

set_blue
        MOV R2, #1          ; VGA index 1 = Blue
        B store

set_green
        MOV R2, #2          ; VGA index 2 = Green
        B store

set_gold
        MOV R2, #14         ; VGA index 14 = Yellow (gold)
        B store

set_white
        MOV R2, #15         ; VGA index 15 = White
        B store

set_black
        MOV R2, #0          ; VGA index 0 = Black

store
        ; Write pixel byte to display memory
        ADD R1, R9, R0      ; R1 = base + offset
        STRB R2, [R1]       ; Store colour byte

        ADD R0, R0, #1      ; Advance pixel offset
        ADD R6, R6, #1      ; Next column
        B col_loop

col_done
        ADD R3, R3, #1      ; Next row
        B row_loop

done    END
