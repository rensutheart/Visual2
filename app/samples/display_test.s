; Display Test - Memory-Mapped Pixel Display
; Writes a colourful pattern to the display grid.
; Base address: 0x2000 (one byte per pixel, VGA palette index)
;
; ===== GRID SIZE CONFIGURATION =====
; Change the WIDTH value below to match your Display tab grid size.
; Supports 16, 32, or 64. Everything else adapts automatically.
; ===================================

        MOV R9, #0x2000     ; R9 = display base address

        ; --- Grid size parameter (edit this for 32x32 or 64x64) ---
        MOV R11, #16        ; WIDTH: pixels per row (16, 32, or 64)

        ; Compute derived values
        MUL R12, R11, R11   ; TOTAL = WIDTH * WIDTH
        MOV R8, R11, LSR #2 ; BAND_HEIGHT = WIDTH / 4 (rows per colour band)

        ; Loop by row and column (avoids division)
        MOV R3, #0          ; R3 = current row
        MOV R0, #0          ; R0 = pixel index (byte offset)

row_loop
        CMP R3, R11
        BGE done

        ; Determine colour band group: group = row / BAND_HEIGHT
        ; Use repeated subtraction to divide (works for any WIDTH)
        MOV R4, #0          ; R4 = group number
        MOV R5, R3          ; R5 = remaining rows
grp_div CMP R5, R8
        BLT grp_end
        SUB R5, R5, R8
        ADD R4, R4, #1
        B grp_div
grp_end
        ; group * 16 + 32 = base colour for this band
        MOV R5, R4, LSL #4  ; R5 = group * 16
        ADD R5, R5, #32     ; base palette offset
        AND R5, R5, #0xFF   ; clamp to valid palette range

        ; Draw all columns in this row
        MOV R6, #0          ; R6 = column

col_loop
        CMP R6, R11
        BGE col_done

        ; colour = base + (col mod 16)
        AND R7, R6, #0xF
        ADD R2, R5, R7      ; R2 = final palette index

        ; Store the pixel byte
        ADD R1, R9, R0       ; R1 = address for this pixel
        STRB R2, [R1]        ; Write colour to display memory

        ADD R0, R0, #1       ; next pixel offset
        ADD R6, R6, #1       ; next column
        B col_loop

col_done
        ADD R3, R3, #1       ; next row
        B row_loop

done    END
