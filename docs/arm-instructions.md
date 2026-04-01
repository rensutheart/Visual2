# VisUAL2-SU — Supported ARM Instructions

A complete reference of all ARM assembly instructions and directives supported by the VisUAL2-SU simulator.

VisUAL2-SU emulates **32-bit ARM (ARMv4)** — all registers are 32 bits wide, memory addresses are 32-bit, and no AArch64 (64-bit ARM) features are supported.

> **Source files:** Instruction definitions live in `src/Emulator/` — specifically `DP.fs`, `Multiply.fs`, `Memory.fs`, `Branch.fs`, and `Misc.fs`.

---

## Table of Contents

1. [Condition Codes](#condition-codes)
2. [Registers](#registers)
3. [Data Processing Instructions](#data-processing-instructions)
4. [Flexible Operand 2](#flexible-operand-2)
5. [Multiply Instructions](#multiply-instructions)
6. [Division Instructions](#division-instructions)
7. [Saturating Arithmetic Instructions](#saturating-arithmetic-instructions)
8. [Memory Instructions — Single Register](#memory-instructions--single-register)
9. [Memory Instructions — Multiple Registers](#memory-instructions--multiple-registers)
10. [Branch Instructions](#branch-instructions)
11. [Pseudo-Instructions & Directives](#pseudo-instructions--directives)
12. [Expressions](#expressions)
13. [Notable Limitations](#notable-limitations)

---

## Condition Codes

All instructions (except `END` and the data directives `DCD`, `DCB`, `FILL`, `EQU`) can be conditionally executed by appending a 2-letter condition suffix. If omitted, the instruction always executes (equivalent to `AL`).

| Suffix | Condition | Flags Tested |
|--------|-----------|--------------|
| `EQ` | Equal | Z = 1 |
| `NE` | Not equal | Z = 0 |
| `CS` / `HS` | Carry set / Unsigned ≥ | C = 1 |
| `CC` / `LO` | Carry clear / Unsigned < | C = 0 |
| `MI` | Minus (negative) | N = 1 |
| `PL` | Plus (positive or zero) | N = 0 |
| `VS` | Overflow set | V = 1 |
| `VC` | Overflow clear | V = 0 |
| `HI` | Unsigned higher | C = 1 AND Z = 0 |
| `LS` | Unsigned lower or same | C = 0 OR Z = 1 |
| `GE` | Signed ≥ | N = V |
| `GT` | Signed > | Z = 0 AND N = V |
| `LT` | Signed < | N ≠ V |
| `LE` | Signed ≤ | Z = 1 OR N ≠ V |
| `AL` | Always (default) | — |
| `NV` | Never | — |

---

## Registers

All 16 registers are **32 bits** wide (ARMv4 architecture). Values are unsigned 32-bit integers (`0x00000000`–`0xFFFFFFFF`); signed interpretation is applied only where relevant (e.g., `ASR`, `SMULL`, `QADD`).

| Name | Alias | Description |
|------|-------|-------------|
| `R0`–`R12` | — | General-purpose 32-bit registers |
| `R13` | `SP` | Stack pointer |
| `R14` | `LR` | Link register (return address for `BL`) |
| `R15` | `PC` | Program counter (reads as current instruction + 8 due to pipelining) |

**Flags** (updated by comparison instructions and `S`-suffixed instructions):

| Flag | Meaning |
|------|---------|
| `N` | Negative — result bit 31 is set |
| `Z` | Zero — result is zero |
| `C` | Carry — carry out / shift out |
| `V` | Overflow — signed overflow |

---

## Data Processing Instructions

All data processing instructions support:
- Optional `S` suffix to update flags (comparison instructions always update flags)
- All 16 condition codes
- Flexible Operand 2 for the last operand (see below)

### Arithmetic (3-operand: `OP{S}{cond} Rd, Rn, Op2`)

| Mnemonic | Operation | Notes |
|----------|-----------|-------|
| `ADD` | Rd = Rn + Op2 | If literal invalid, tries SUB with negated literal |
| `SUB` | Rd = Rn − Op2 | If literal invalid, tries ADD with negated literal |
| `ADC` | Rd = Rn + Op2 + C | Add with carry |
| `SBC` | Rd = Rn − Op2 − !C | Subtract with carry |
| `RSB` | Rd = Op2 − Rn | Reverse subtract |
| `RSC` | Rd = Op2 − Rn − !C | Reverse subtract with carry |

### Logical (3-operand: `OP{S}{cond} Rd, Rn, Op2`)

| Mnemonic | Operation | Notes |
|----------|-----------|-------|
| `AND` | Rd = Rn AND Op2 | If literal invalid, tries BIC with inverted literal |
| `ORR` | Rd = Rn OR Op2 | |
| `EOR` | Rd = Rn XOR Op2 | |
| `BIC` | Rd = Rn AND NOT Op2 | Bit clear; if literal invalid, tries AND with inverted literal |

### Move (2-operand: `OP{S}{cond} Rd, Op2`)

| Mnemonic | Operation | Notes |
|----------|-----------|-------|
| `MOV` | Rd = Op2 | If literal invalid, tries MVN with inverted literal |
| `MVN` | Rd = NOT Op2 | If literal invalid, tries MOV with inverted literal |

### Comparison (2-operand: `OP{cond} Rn, Op2` — no destination, always updates flags)

| Mnemonic | Operation | Notes |
|----------|-----------|-------|
| `CMP` | Flags = Rn − Op2 | If literal invalid, tries CMN with negated literal |
| `CMN` | Flags = Rn + Op2 | If literal invalid, tries CMP with negated literal |
| `TST` | Flags = Rn AND Op2 | |
| `TEQ` | Flags = Rn XOR Op2 | |

> **Note:** `CMP`/`CMN`/`TST`/`TEQ` do not accept the `S` suffix — flags are always updated. Writing `CMPS` is a parse error.

### Shift (3-operand: `OP{S}{cond} Rd, Rn, #imm | Rs`)

| Mnemonic | Operation | Immediate Range |
|----------|-----------|-----------------|
| `LSL` | Rd = Rn << amount | #0–#31 |
| `LSR` | Rd = Rn >>> amount (logical) | #1–#32 |
| `ASR` | Rd = Rn >> amount (arithmetic) | #1–#32 |
| `ROR` | Rd = Rn rotated right by amount | #1–#31 |

### RRX (2-operand: `RRX{S}{cond} Rd, Rn`)

| Mnemonic | Operation |
|----------|-----------|
| `RRX` | Rd = (C << 31) OR (Rn >>> 1); C = Rn[0] |

---

## Flexible Operand 2

The last operand of all data processing instructions uses the ARM "flexible operand 2" format:

| Format | Syntax | Example | Description |
|--------|--------|---------|-------------|
| Immediate | `#value` | `#42`, `#0xFF` | 8-bit constant rotated right by even amount (0–30). Not all 32-bit values are valid. |
| Register | `Rn` | `R3` | Value in register |
| Register + immediate shift | `Rn, SHIFT #imm` | `R2, LSL #3` | Shifted register with constant shift amount |
| Register + register shift | `Rn, SHIFT Rs` | `R2, LSL R4` | Shifted register with shift amount from Rs[4:0] |
| Register + RRX | `Rn, RRX` | `R2, RRX` | Rotate right extended (1-bit through carry) |

**Restriction:** `R15` (`PC`) cannot be used for any register (`Rd`, `Rn`, `Rm`, or `Rs`) in a data processing instruction with a register-controlled shift (`Rm, SHIFT Rs`). Using R15 in any position produces UNPREDICTABLE results on real hardware. This restriction applies to all ARM architecture versions (ARMv4 through ARMv7+).

**Valid shift types:** `LSL`, `LSR`, `ASR`, `ROR`

**Immediate literal rules:** The value must be expressible as an 8-bit number rotated right by an even amount. For example, `#0xFF` (valid), `#0x3FC` (valid: 0xFF ROR 30), but `#0x101` (invalid: not representable). When a literal is invalid, the assembler automatically tries:
- **Negated literal** (for ADD↔SUB, CMP↔CMN): e.g., `ADD R0, R1, #-1` becomes `SUB R0, R1, #1`
- **Inverted literal** (for AND↔BIC, MOV↔MVN): e.g., `MOV R0, #0xFFFFFF00` becomes `MVN R0, #0xFF`

---

## Multiply Instructions

> **Source file:** `src/Emulator/Multiply.fs`

### 32-bit Multiply (`MUL{S}{cond} Rd, Rm, Rs`)

| Mnemonic | Operation | Notes |
|----------|-----------|-------|
| `MUL` | Rd = Rm × Rs (low 32 bits) | Result truncated to 32 bits |

### 32-bit Multiply-Accumulate (`MLA{S}{cond} Rd, Rm, Rs, Rn`)

| Mnemonic | Operation | Notes |
|----------|-----------|-------|
| `MLA` | Rd = (Rm × Rs) + Rn (low 32 bits) | Result truncated to 32 bits |

### 64-bit Long Multiply (`OP{S}{cond} RdLo, RdHi, Rm, Rs`)

| Mnemonic | Operation | Signed? | Accumulate? |
|----------|-----------|---------|-------------|
| `UMULL` | RdHi:RdLo = Rm × Rs | Unsigned | No |
| `UMLAL` | RdHi:RdLo += Rm × Rs | Unsigned | Yes |
| `SMULL` | RdHi:RdLo = Rm × Rs | Signed | No |
| `SMLAL` | RdHi:RdLo += Rm × Rs | Signed | Yes |

### Flags

With the `S` suffix:
- **N** — Set if result bit 31 (32-bit) or bit 63 (64-bit) is set
- **Z** — Set if result is zero (for 64-bit: both hi and lo are zero)
- **C, V** — Preserved (not affected)

Without the `S` suffix, flags are not modified.

### Register Constraints

- `PC` (`R15`) must not be used as any operand
- **MUL/MLA:** `Rd` must differ from `Rm`
- **Long multiplies:** `RdLo` must differ from `RdHi` and from `Rm`; `RdHi` must differ from `Rm`

---

## Division Instructions

> **Source file:** `src/Emulator/Multiply.fs`

### Signed and Unsigned Division (`OP{cond} Rd, Rn, Rm`)

| Mnemonic | Operation | Notes |
|----------|-----------|-------|
| `SDIV` | Rd = Rn ÷ Rm (signed) | Truncates towards zero |
| `UDIV` | Rd = Rn ÷ Rm (unsigned) | Truncates towards zero |

### Flags

Division instructions **do not** support the `S` suffix and **never** modify flags.

### Division by Zero

Division by zero returns `0` (per ARM architecture specification).

### Register Constraints

- `PC` (`R15`) must not be used as any operand

---

## Saturating Arithmetic Instructions

> **Source file:** `src/Emulator/Saturate.fs`

### QADD / QSUB — Saturating Add and Subtract

**Syntax:** `OP{cond} Rd, Rm, Rn`

| Mnemonic | Operation | Notes |
|----------|-----------|-------|
| `QADD` | Rd = SAT(Rm + Rn) | Signed saturating add |
| `QSUB` | Rd = SAT(Rm - Rn) | Signed saturating subtract |

Saturation clamps the result to the signed 32-bit range:
- If the result exceeds +2,147,483,647 (`0x7FFFFFFF`), it is clamped to `0x7FFFFFFF`
- If the result is below −2,147,483,648 (`0x80000000`), it is clamped to `0x80000000`

### Flags

QADD and QSUB do **not** modify N, Z, C, or V flags. In real ARM hardware they set the Q (sticky saturation) flag, which is not modelled in VisUAL2.

### Register Constraints

- `PC` (`R15`) must not be used as `Rd`, `Rm`, or `Rn`
- No `S` suffix is supported

---

## Memory Instructions — Single Register

### LDR / STR — Word and Byte Load/Store

**Syntax:** `OP{B}{cond} Rd, <address>`

| Mnemonic | Operation | Transfer Size |
|----------|-----------|---------------|
| `LDR` | Rd ← Memory[addr] | 32-bit word |
| `LDRB` | Rd ← Memory[addr] (zero-extended) | 8-bit byte |
| `STR` | Memory[addr] ← Rd | 32-bit word |
| `STRB` | Memory[addr] ← Rd (lowest byte) | 8-bit byte |

### Addressing Modes

| Mode | Syntax | Behaviour |
|------|--------|-----------|
| Offset | `[Rn]` or `[Rn, #offset]` | Address = Rn + offset; Rn unchanged |
| Pre-indexed | `[Rn, #offset]!` | Address = Rn + offset; Rn updated to address |
| Post-indexed | `[Rn], #offset` | Address = Rn; Rn updated to Rn + offset |

**Offset formats:**
- Immediate: `#value` (±4095 for word or byte per ARM spec; VisUAL2 requires word offsets to be divisible by 4, giving an effective word range of ±4092)
- Register: `±Rm`
- Scaled register: `±Rm, LSL #n` / `±Rm, LSR #n` / `±Rm, ASR #n`

**Endianness:** Memory is **little-endian**. For byte access (`LDRB`/`STRB`), byte 0 is bits [7:0] of the word at the aligned address, byte 1 is bits [15:8], etc.

### LDR pseudo-instruction

| Syntax | Description |
|--------|-------------|
| `LDR Rd, =constant` | Load any 32-bit constant into Rd. The assembler handles encoding. |

This is useful when the constant is not a valid immediate for `MOV`. Internally represented as `LDREQUAL` in the parser.

### LDRH / LDRSH / STRH / LDRSB — Half-Word and Signed Byte Load/Store

**Syntax:** `OP{cond} Rd, <address>`

| Mnemonic | Operation | Transfer Size | Sign Extension |
|----------|-----------|---------------|----------------|
| `LDRH` | Rd ← Memory16[addr], zero-extended to 32 bits | 16-bit | No |
| `LDRSH` | Rd ← Memory16[addr], sign-extended to 32 bits | 16-bit | Yes (bit 15) |
| `STRH` | Memory16[addr] ← Rd[15:0] | 16-bit | N/A |
| `LDRSB` | Rd ← Memory8[addr], sign-extended to 32 bits | 8-bit | Yes (bit 7) |

**Addressing modes:** Same offset, pre-indexed, and post-indexed modes as LDR/STR.

**Offset constraints:**
- Immediate: ±255
- Register: `±Rm` (no shifted register)

**Alignment:** Half-word instructions (`LDRH`, `LDRSH`, `STRH`) require the effective address to be 2-byte aligned (even address). Misaligned access produces a runtime error.

**Invalid combinations:** `STRSH` and `STRSB` do not exist — they are rejected at parse time.

### LDRD / STRD — Double-Word Load/Store

> **Source file:** `src/Emulator/Memory.fs`

**Syntax:** `OP{cond} Rd, Rd2, <address>`

| Mnemonic | Operation | Transfer Size |
|----------|-----------|---------------|
| `LDRD` | Rd ← Memory[addr], Rd2 ← Memory[addr+4] | Two 32-bit words |
| `STRD` | Memory[addr] ← Rd, Memory[addr+4] ← Rd2 | Two 32-bit words |

### Addressing Modes

| Mode | Syntax | Behaviour |
|------|--------|-----------|
| Offset | `[Rn]` or `[Rn, #offset]` | Address = Rn + offset; Rn unchanged |
| Pre-indexed | `[Rn, #offset]!` | Address = Rn + offset; Rn updated to address |
| Post-indexed | `[Rn], #offset` | Address = Rn; Rn updated to Rn + offset |

### Register and Offset Constraints

- `Rd` must be an **even-numbered** register (R0, R2, R4, R6, R8, R10, R12)
- `Rd2` must be `Rd + 1` (the next register)
- The pair `R14`–`R15` is not allowed (`Rd` cannot be `R14`)
- `PC` (`R15`) cannot be used as `Rd` or `Rd2`
- Immediate offset: ±255, must be **divisible by 4**
- Effective address must be **word-aligned** (divisible by 4) — misaligned access produces a runtime error

---

## Memory Instructions — Multiple Registers

### LDM / STM — Block Load/Store

**Syntax:** `OP{mode}{cond} Rn{!}, {reglist}`

| Mnemonic | Operation |
|----------|-----------|
| `LDM` | Load multiple registers from consecutive memory addresses |
| `STM` | Store multiple registers to consecutive memory addresses |

**`!`** = writeback: update Rn with the final address after the transfer.

**`{reglist}`** = comma-separated register list in braces, e.g., `{R0, R2-R5, LR}`

### Addressing Mode Suffixes

| Suffix | Full Name | Direction | When Rn Updated |
|--------|-----------|-----------|-----------------|
| `IA` | Increment After | Ascending | After each transfer |
| `IB` | Increment Before | Ascending | Before each transfer |
| `DA` | Decrement After | Descending | After each transfer |
| `DB` | Decrement Before | Descending | Before each transfer |

### Stack Operation Aliases

These are equivalent to the addressing modes above but named for stack usage:

| Alias | LDM equivalent | STM equivalent | Stack Type |
|-------|---------------|----------------|------------|
| `FD` | Full Descending | `LDMDB` / `STMDB` | Standard ARM stack |
| `FA` | Full Ascending | `LDMDA` / `STMDA` | |
| `ED` | Empty Descending | `LDMIB` / `STMIB` | |
| `EA` | Empty Ascending | `LDMIA` / `STMIA` | |

### PUSH / POP — Stack Shorthand

**Syntax:** `PUSH{cond} {reglist}` / `POP{cond} {reglist}`

| Mnemonic | Equivalent | Operation |
|----------|------------|----------|
| `PUSH` | `STMDB SP!, {reglist}` | Decrement SP, store registers (lowest-numbered at lowest address) |
| `POP` | `LDMIA SP!, {reglist}` | Load registers, increment SP |

- Supports all 16 condition codes
- Register list uses same syntax as LDM/STM: `{R0, R2-R5, LR}`
- SP (R13) must not appear in the register list
- These are pure parse-time aliases — no new execution logic

---

## Branch Instructions

### B / BL — Branch and Branch with Link

**Syntax:** `OP{cond} label`

| Mnemonic | Operation | Cycles |
|----------|-----------|--------|
| `B` | Branch to label (PC = label address) | 2 |
| `BL` | Branch with link (LR = next instruction address, PC = label) | 2 |
| `END` | Terminate program execution | 0 |

- `B` and `BL` support all 16 condition codes
- `END` does not support condition codes or operands
- The label must be a valid expression that resolves to an instruction address

### BX / BLX — Branch (with Link) and Exchange

**Syntax:** `OP{cond} Rm`

| Mnemonic | Operation | Cycles |
|----------|-----------|--------|
| `BX` | Branch to address in Rm (PC = Rm with bit 0 masked off) | 2 |
| `BLX` | Branch with link to address in Rm (LR = next instruction, PC = Rm) | 2 |

- Support all 16 condition codes
- Operand must be a register (not `PC`)
- Bit 0 of the register value is masked off (Thumb bit ignored — Thumb mode not supported)
- Typically used with `BX LR` to return from subroutines called with `BL` or `BLX`

---

## Pseudo-Instructions & Directives

These are assembler directives — they do not generate executable code but affect memory layout and symbols.

### Data Definitions

| Directive | Syntax | Description |
|-----------|--------|-------------|
| `DCD` | `label DCD expr1, expr2, ...` | Define 32-bit word constants. Allocates 4 bytes per value. |
| `DCB` | `label DCB byte1, byte2, ...` or `label DCB "string",0` | Define byte constants or string characters. Padded with zeros to a word (4-byte) boundary. |
| `FILL` | `label FILL nBytes [, value]` | Fill memory with `value` (default 0). `nBytes` must be divisible by 4. |
| `EQU` | `label EQU expression` | Define a named constant (no memory allocated). |

All data directives **require a label**.

Values can be:
- Decimal: `100`, `-5`
- Hexadecimal: `0xFF`
- Binary: `0b1010`
- Labels: `myLabel`
- Expressions: `myLabel + 4`, `(SIZE * 2) - 1`

### ADR — Load Address

| Directive | Syntax | Description |
|-----------|--------|-------------|
| `ADR` | `ADR{cond} Rd, label` | Load the address of `label` into `Rd`. |

**Constraints:**
- Byte offset (label − PC − 8) must be in range −248 to +264 if not word-aligned
- Word offset must be in range −1016 to +1032
- For larger offsets, use `LDR Rd, =label` instead
- Supports all condition codes
- Writing to R15 (PC) causes a 2-cycle stall

---

## Expressions

Operands that accept numeric values (immediates, DCD values, EQU definitions, etc.) support expressions:

| Element | Example | Description |
|---------|---------|-------------|
| Decimal | `42` | |
| Hexadecimal | `0xFF`, `0XFF`, `&FF` | `0x` / `0X` prefix or `&` prefix |
| Binary | `0b1010` | `0b` / `0B` prefix |
| Label reference | `myLabel` | Resolves to the label's address or EQU value |
| Addition | `a + b` | |
| Subtraction | `a - b` | |
| Multiplication | `a * b` | |
| Unary minus | `-expr` | |
| Parentheses | `(a + b) * c` | |
| Underscore separator | `0xFF_FF`, `1_000_000` | Ignored — for readability only |

Only the operators `+`, `-`, and `*` are supported (no division). Forward references to labels are resolved automatically via multi-pass assembly.

---

## Notable Limitations

VisUAL2 targets the **32-bit ARMv4 instruction set**. The following ARM features are **not supported**:

| Category | Missing Instructions/Features |
|----------|-------------------------------|
| Swap | `SWP`, `SWPB` (deprecated in ARMv6+) |
| Software interrupt | `SWI` / `SVC` |
| Coprocessor | `MCR`, `MRC`, `LDC`, `STC` |
| Thumb mode | All Thumb/Thumb-2 encoding |
| Privileged mode | `MSR`, `MRS`, mode switching |
| CPSR/SPSR access | No direct flag register read/write |
| Q (saturation) flag | `QADD`/`QSUB` saturate correctly but the Q sticky flag is not tracked |
| Division | `SDIV`, `UDIV` (ARMv7-R/ARMv7-M only) |

---

## Quick Reference — All Mnemonics

```
Data Processing:   MOV  MVN  ADD  SUB  ADC  SBC  RSB  RSC
                   AND  ORR  EOR  BIC
                   CMP  CMN  TST  TEQ
                   LSL  LSR  ASR  ROR  RRX

Multiply:          MUL  MLA  UMULL  UMLAL  SMULL  SMLAL

Divide:            SDIV  UDIV

Saturating:        QADD  QSUB

Memory (single):   LDR  LDRB  STR  STRB  LDR Rd,=val
                   LDRH  LDRSH  STRH  LDRSB
                   LDRD  STRD

Memory (multiple): LDM{IA|IB|DA|DB|FD|ED|FA|EA}
                   STM{IA|IB|DA|DB|FD|ED|FA|EA}
                   PUSH  POP

Branches:          B    BL   BX   BLX  END

Directives:        DCD  DCB  FILL  EQU  ADR
```

**Total:** 21 data processing + 6 multiply + 2 divide + 2 saturating + 11 memory (single) + 16 memory (multiple modes) + 2 stack (PUSH/POP) + 5 branch + 5 directives = **70 base mnemonics**, expanding to hundreds of valid opcode strings with condition codes and suffixes.
