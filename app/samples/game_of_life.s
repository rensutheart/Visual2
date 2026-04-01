; Conway's Game of Life
; =====================
; A cellular automaton on the 16x16 toroidal grid.
; Each cell is alive (1) or dead (0). Every generation:
;   - A live cell with 2 or 3 neighbours survives.
;   - A dead cell with exactly 3 neighbours becomes alive.
;   - All others die.
;
; Initial pattern: R-pentomino, a famous "methuselah" that
; evolves chaotically from just 5 cells.
;
; Instructions:
;   1. Enable Display Mode in the Display tab
;   2. Run — each generation pauses at MOV R10, #1
;   3. Click Continue or enable "Auto" for continuous evolution
;
; Demonstrates: BL / BX LR subroutines, PUSH / POP (nested
;   calls), LDRB / STRB, AND for toroidal wrapping, CMP with
;   conditional branches, MUL for address calculation

; ===== CONSTANTS =====
        MOV  R9, #0x2000        ; display base address
        MOV  R12, #16           ; grid width

; ===== BUFFER ADDRESSES =====
; Two 256-byte buffers for double-buffering the cell state.
; Buffer A ("current") at 0x3000, Buffer B ("next") at 0x3100.
        MOV  R6, #0x3000        ; R6 = current state buffer
        MOV  R7, #0x3100        ; R7 = next state buffer

; ===== INITIALISE: clear buffer, plant R-pentomino =====
        ; Clear buffer A to all zeros
        MOV  R0, #0
        MOV  R1, #0
clr     STRB R1, [R6, R0]
        ADD  R0, R0, #1
        CMP  R0, #0xFF
        BLE  clr

        ; R-pentomino centred at (7,7):    .##
        ;                                   ##.
        ;                                   .#.
        MOV  R1, #1
        ; Row 7: cells (8,7) and (9,7)
        ADD  R0, R6, #120       ; 7*16 + 8 = 120
        STRB R1, [R0]
        ADD  R0, R6, #121       ; 7*16 + 9
        STRB R1, [R0]
        ; Row 8: cells (7,8) and (8,8)
        ADD  R0, R6, #135       ; 8*16 + 7 = 135
        STRB R1, [R0]
        ADD  R0, R6, #136       ; 8*16 + 8
        STRB R1, [R0]
        ; Row 9: cell (8,9)
        ADD  R0, R6, #152       ; 9*16 + 8 = 152
        STRB R1, [R0]

; ===== MAIN GENERATION LOOP =====
gen_loop
        ; --- For each cell (x,y), compute next state ---
        MOV  R11, #0            ; R11 = y (row)
row_loop
        CMP  R11, R12
        BGE  gen_done
        MOV  R4, #0             ; R4 = x (column)

col_loop
        CMP  R4, R12
        BGE  col_done

        ; Count live neighbours (8 directions) via subroutine
        PUSH {R4, R11}
        BL   count_nbrs         ; returns count in R8
        POP  {R4, R11}

        ; Read current cell state
        MUL  R0, R11, R12       ; offset = y * 16
        ADD  R0, R0, R4         ; + x
        LDRB R1, [R6, R0]      ; R1 = current state (0 or 1)

        ; Apply rules
        MOV  R2, #0             ; default: dead
        CMP  R1, #1
        BNE  dead_cell

        ; Live cell: survives if neighbours == 2 or 3
        CMP  R8, #2
        MOVEQ R2, #1
        CMP  R8, #3
        MOVEQ R2, #1
        B    write_next

dead_cell
        ; Dead cell: born if neighbours == 3
        CMP  R8, #3
        MOVEQ R2, #1

write_next
        ; Write new state to next buffer
        MUL  R0, R11, R12
        ADD  R0, R0, R4
        STRB R2, [R7, R0]

        ADD  R4, R4, #1
        B    col_loop

col_done
        ADD  R11, R11, #1
        B    row_loop

gen_done
        ; --- Swap buffers ---
        MOV  R0, R6
        MOV  R6, R7
        MOV  R7, R0

        ; --- Write current state to display ---
        MOV  R0, #0             ; pixel index
disp
        LDRB R1, [R6, R0]      ; cell state 0 or 1
        MOV  R2, #0             ; colour for dead (black)
        CMP  R1, #0
        MOVNE R2, #10           ; colour for alive (bright green)
        ADD  R3, R9, R0
        STRB R2, [R3]
        ADD  R0, R0, #1
        CMP  R0, #0xFF
        BLE  disp

        ; --- Trigger display refresh ---
        MOV  R10, #1

        B    gen_loop

; ==========================================================
;  SUBROUTINE: count_nbrs
;  Count the 8 neighbours of cell (R4, R11) in buffer R6.
;  Result returned in R8.  Clobbers R0-R3, R5.
; ==========================================================
count_nbrs
        PUSH {LR}              ; save return address (BL get_cell inside)
        MOV  R8, #0             ; neighbour count

        ; (-1, -1)
        SUB  R1, R4, #1
        SUB  R2, R11, #1
        BL   get_cell
        ADD  R8, R8, R0
        ; ( 0, -1)
        MOV  R1, R4
        SUB  R2, R11, #1
        BL   get_cell
        ADD  R8, R8, R0
        ; (+1, -1)
        ADD  R1, R4, #1
        SUB  R2, R11, #1
        BL   get_cell
        ADD  R8, R8, R0
        ; (-1,  0)
        SUB  R1, R4, #1
        MOV  R2, R11
        BL   get_cell
        ADD  R8, R8, R0
        ; (+1,  0)
        ADD  R1, R4, #1
        MOV  R2, R11
        BL   get_cell
        ADD  R8, R8, R0
        ; (-1, +1)
        SUB  R1, R4, #1
        ADD  R2, R11, #1
        BL   get_cell
        ADD  R8, R8, R0
        ; ( 0, +1)
        MOV  R1, R4
        ADD  R2, R11, #1
        BL   get_cell
        ADD  R8, R8, R0
        ; (+1, +1)
        ADD  R1, R4, #1
        ADD  R2, R11, #1
        BL   get_cell
        ADD  R8, R8, R0

        POP  {LR}
        BX   LR

; ==========================================================
;  SUBROUTINE: get_cell  (leaf — no further calls)
;  Read cell (R1, R2) from buffer R6 with toroidal wrapping.
;  Returns value in R0.  Clobbers R3.
; ==========================================================
get_cell
        AND  R3, R2, #0xF      ; wrap y to 0..15
        MOV  R3, R3, LSL #4    ; y * 16
        AND  R0, R1, #0xF      ; wrap x to 0..15
        ADD  R3, R3, R0        ; offset = y*16 + x
        LDRB R0, [R6, R3]      ; cell value (0 or 1)
        BX   LR

        END
