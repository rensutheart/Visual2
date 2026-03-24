# Implementing New ARM Instructions in VisUAL2

This document describes what would be needed to add the following instruction groups to the simulator:

1. [Half-word memory: LDRH, LDRSH, STRH, LDRSB](#1-half-word-memory-ldrh-ldrsh-strh-ldrsb)
2. [Multiply: MUL, MLA, UMULL, UMLAL, SMULL, SMLAL](#2-multiply-mul-mla-umull-umlal-smull-smlal)
3. [Branch exchange: BX, BLX](#3-branch-exchange-bx-blx)
4. [Stack shorthand: PUSH, POP](#4-stack-shorthand-push-pop)

Each section covers the ARM semantics, the files to modify, types to add, and specific integration points.

---

## How the Existing Architecture Works (Quick Recap)

Every instruction module follows the same pattern:

1. **Types** — define an `Instr` discriminated union for the module's instruction variants
2. **OpSpec** — declare `{InstrC; Roots; Suffixes}` to generate valid opcode strings via `opCodeExpand`
3. **`parse`** — `LineData -> Parse<Instr> option`: return `None` if opcode doesn't belong to this module, `Some result` if it does
4. **`(|IMatch|_|)`** — active pattern wrapping `parse`, used by `ParseTop.IMatch`
5. **`execute`** — takes the module's `Instr` + `DataPath`, returns `Result<DataPath, ExecuteError>` (or with `UFlags` for DP)

`ParseTop.IMatch` tries each module in order: Memory → DP → Misc → Branch. The first match wins.

`ExecutionTop.dataPathStep` dispatches execution by matching on the `ParseTop.Instr` DU.

---

## 1. Half-Word Memory: LDRH, LDRSH, STRH, LDRSB

### ARM Semantics

| Instruction | Operation | Size | Sign Extension |
|-------------|-----------|------|----------------|
| `LDRH Rd, [Rn, #off]` | Rd ← Memory16[addr], zero-extended to 32 bits | 16-bit | No |
| `LDRSH Rd, [Rn, #off]` | Rd ← Memory16[addr], sign-extended to 32 bits | 16-bit | Yes |
| `STRH Rd, [Rn, #off]` | Memory16[addr] ← Rd[15:0] | 16-bit | N/A |
| `LDRSB Rd, [Rn, #off]` | Rd ← Memory8[addr], sign-extended to 32 bits | 8-bit | Yes |

All support condition codes, and the same three addressing modes as LDR/STR: offset, pre-indexed (`!`), post-indexed.

**Key difference from LDR/LDRB:** Half-word instructions use a different encoding in real ARM (misc loads/stores), but for the simulator this only matters for operand format constraints:
- Immediate offsets are ±255 (8-bit) rather than ±4095 for word/byte (though the simulator could relax this)
- Register offset is `±Rm` only (no shifted register like LDR allows)

### Files to Modify

#### `src/Emulator/Memory.fs`

**1. Extend `MSize` type:**

```fsharp
type MSize =
    | MWord    // LDR, STR — 32-bit
    | MByte    // LDRB, STRB — 8-bit unsigned
    | MHalf    // LDRH, STRH — 16-bit unsigned
    | MSignedHalf  // LDRSH — 16-bit signed
    | MSignedByte  // LDRSB — 8-bit signed
```

**2. Extend `memSpecSingle`** (or add a new spec):

```fsharp
let memSpecHalf = {
    InstrC = MEM
    Roots = [ "LDR"; "STR" ]
    Suffixes = [ "H"; "SH"; "SB" ]
}
```

Note: `STR` with suffix `SH` or `SB` is invalid — the parser must reject `STRSH` and `STRSB`.

**3. Update `opCodes`** to include the new spec:

```fsharp
let opCodes =
    let toArr = opCodeExpand >> Map.toArray
    Array.collect toArr [| memSpecSingle; memSpecMultiple; memSpecHalf |]
    |> Map.ofArray
```

**4. Add half-word memory access functions:**

```fsharp
let getDataMemHalfWord (addr : uint32) (dp : DataPath) =
    if addr % 2u <> 0u then
        ``Run time error`` (addr, sprintf "half-word address %d must be even" addr) |> Error
    else
        let wordAddr = addr &&& 0xFFFFFFFCu
        let bitOffset = (addr &&& 0x2u) * 8u |> int
        getDataMemWord wordAddr dp
        |> Result.map (fun w -> (w >>> bitOffset) &&& 0xFFFFu)

let getDataMemSignedHalfWord addr dp =
    getDataMemHalfWord addr dp
    |> Result.map (fun v -> if v > 0x7FFFu then v ||| 0xFFFF0000u else v)

let getDataMemSignedByte addr dp =
    getDataMemByte addr dp
    |> Result.map (fun v -> if v > 0x7Fu then v ||| 0xFFFFFF00u else v)

let updateMemHalfWord (value : uint16) (addr : uint32) (dp : DataPath) =
    if addr % 2u <> 0u then
        ``Run time error`` (addr, sprintf "half-word address %d must be even" addr) |> Error
    else
        let wordAddr = addr &&& 0xFFFFFFFCu
        let bitOffset = (addr &&& 0x2u) * 8u |> int
        getDataMemWord wordAddr dp
        |> Result.map (fun w ->
            let mask = 0xFFFFu <<< bitOffset
            let newWord = (w &&& ~~~mask) ||| ((uint32 value) <<< bitOffset)
            { dp with MM = Map.add (WA wordAddr) (Dat newWord) dp.MM })
```

**5. Update `executeLDRSTR`** to handle the new `MSize` variants:

```fsharp
| LOAD ->
    match ins.MemSize with
    | MWord -> getDataMemWord ef dp
    | MByte -> getDataMemByte ef dp
    | MHalf -> getDataMemHalfWord ef dp
    | MSignedHalf -> getDataMemSignedHalfWord ef dp
    | MSignedByte -> getDataMemSignedByte ef dp
    |> Result.map (fun dat -> updateReg dat ins.Rd dp)
| STORE ->
    match ins.MemSize with
    | MWord -> updateMemData (Dat dp.Regs.[ins.Rd]) ef dp
    | MByte -> updateMemByte (dp.Regs.[ins.Rd] |> byte) ef dp
    | MHalf -> updateMemHalfWord (dp.Regs.[ins.Rd] |> uint16) ef dp
    | MSignedHalf | MSignedByte ->
        failwithf "Cannot store with signed load type"
```

**6. Update `parseSingle`** to map the new suffixes:

```fsharp
let mSize =
    match uSuffix with
    | "" -> MWord
    | "B" -> MByte
    | "H" -> MHalf
    | "SH" -> MSignedHalf
    | "SB" -> MSignedByte
    | _ -> failwithf "What? Suffix '%s' on LDR or STR is not possible" uSuffix
// Also reject STRSH and STRSB:
if lsType = STORE && (mSize = MSignedHalf || mSize = MSignedByte) then
    makeParseError "LDR (not STR) with signed suffix" ls.OpCode "" |> Error |> ...
```

#### No changes needed to `ParseTop.fs` or `ExecutionTop.fs`

Half-word loads/stores naturally fall into the existing `IMEM` path since they're parsed by `Memory.parse` and executed by `Memory.executeMem`.

### Difficulty: Low–Medium

The memory infrastructure already handles word and byte access. Half-word is a straightforward extension of the same pattern. The main work is the parse suffix handling and adding half-word read/write helper functions.

---

## 2. Multiply: MUL, MLA, UMULL, UMLAL, SMULL, SMLAL

### ARM Semantics

**32-bit multiply (result fits in one register):**

| Instruction | Operation | Operands |
|-------------|-----------|----------|
| `MUL{S}{cond} Rd, Rm, Rs` | Rd = Rm × Rs (low 32 bits) | 3 registers |
| `MLA{S}{cond} Rd, Rm, Rs, Rn` | Rd = (Rm × Rs) + Rn (low 32 bits) | 4 registers |

**64-bit multiply (result in two registers):**

| Instruction | Operation | Operands |
|-------------|-----------|----------|
| `UMULL{S}{cond} RdLo, RdHi, Rm, Rs` | RdHi:RdLo = Rm × Rs (unsigned 64-bit) | 4 registers |
| `UMLAL{S}{cond} RdLo, RdHi, Rm, Rs` | RdHi:RdLo += Rm × Rs (unsigned 64-bit) | 4 registers |
| `SMULL{S}{cond} RdLo, RdHi, Rm, Rs` | RdHi:RdLo = Rm × Rs (signed 64-bit) | 4 registers |
| `SMLAL{S}{cond} RdLo, RdHi, Rm, Rs` | RdHi:RdLo += Rm × Rs (signed 64-bit) | 4 registers |

**Flag behaviour (when S suffix present):**
- N = result bit 31 (MUL/MLA) or bit 63 (long multiplies)
- Z = result is zero
- C = unpredictable (typically preserved in simulator)
- V = unaffected

**Constraints:**
- Rd must not be the same as Rm (on ARMv4, relaxed in later architectures)
- For long multiplies, RdLo ≠ RdHi, and neither can be Rm
- R15 (PC) cannot be used as any operand

### Implementation Approach: New Module (`Multiply.fs`)

The multiply instructions don't fit the existing `DP.Instr` type, which is `(DataPath -> Result<DataPath * UFlags, ExecuteError>) * Op2`. Multiplies don't use flexible operand 2 — they use 3 or 4 register operands instead. The cleanest approach is a new module.

#### New file: `src/Emulator/Multiply.fs`

```fsharp
module Multiply
open CommonData
open CommonLex
open Errors
open Helpers
open DP  // for UFlags, toUFlags

type MulOp = MUL | MLA
type LongMulOp = UMULL | UMLAL | SMULL | SMLAL

type Instr =
    | Mul of MulOp * RName * RName * RName * bool          // op, Rd, Rm, Rs, setFlags
    | MulWithAcc of RName * RName * RName * RName * bool    // Rd, Rm, Rs, Rn, setFlags
    | LongMul of LongMulOp * RName * RName * RName * RName * bool  // op, RdLo, RdHi, Rm, Rs, setFlags

let mulSpec = {
    InstrC = DP   // classify as DP for dispatch purposes
    Roots = [ "MUL"; "MLA"; "UMULL"; "UMLAL"; "SMULL"; "SMLAL" ]
    Suffixes = [ ""; "S" ]
}

let opCodes = opCodeExpand mulSpec
```

**Parsing:** Split operands on commas, match register names. Validate constraints (no PC, Rd ≠ Rm, etc.).

**Execution:**

```fsharp
let executeMul instr (dp : DataPath) =
    let getReg r = dp.Regs.[r]
    match instr with
    | Mul(MUL, rd, rm, rs, sf) ->
        let result = (getReg rm) * (getReg rs) // uint32 truncation is automatic
        let ufl = { toUFlags dp.Fl with
                      F = { dp.Fl with N = setFlagN result; Z = setFlagZ result }
                      NZU = sf }
        Ok (setReg rd result dp, ufl)

    | MulWithAcc(rd, rm, rs, rn, sf) ->
        let result = (getReg rm) * (getReg rs) + (getReg rn)
        let ufl = { toUFlags dp.Fl with
                      F = { dp.Fl with N = setFlagN result; Z = setFlagZ result }
                      NZU = sf }
        Ok (setReg rd result dp, ufl)

    | LongMul(op, rdLo, rdHi, rm, rs, sf) ->
        let isSigned = match op with | SMULL | SMLAL -> true | _ -> false
        let isAccumulate = match op with | UMLAL | SMLAL -> true | _ -> false
        let a, b =
            if isSigned then int64 (int32 (getReg rm)), int64 (int32 (getReg rs))
            else int64 (getReg rm), int64 (getReg rs)
        let product = a * b
        let acc =
            if isAccumulate then
                (int64 (getReg rdHi) <<< 32) ||| int64 (getReg rdLo)
            else 0L
        let result = product + acc
        let lo = uint32 (result &&& 0xFFFFFFFFL)
        let hi = uint32 ((result >>> 32) &&& 0xFFFFFFFFL)
        let ufl = { toUFlags dp.Fl with
                      F = { dp.Fl with N = hi > 0x7FFFFFFFu; Z = (lo = 0u && hi = 0u) }
                      NZU = sf }
        dp |> setReg rdLo lo |> setReg rdHi hi
        |> fun dp' -> Ok (dp', ufl)
```

> **Fable caveat:** Fable's `int64` and `uint64` support works but is slower than `uint32`. The `Long.js` module (already included in the build) handles this. Verify 64-bit multiplication correctness in Fable — there may be edge cases with signed overflow.

#### Changes to other files

**`Emulator.fsproj`** — add `Multiply.fs` between `DP.fs` and `Branch.fs` in compile order:

```xml
<Compile Include="DP.fs" />
<Compile Include="Multiply.fs" />
<Compile Include="Branch.fs" />
```

**`ParseTop.fs`** — add a new DU case and wire into `IMatch`:

```fsharp
type Instr =
    | IMEM of Memory.Instr
    | IDP of DP.Instr
    | IMUL of Multiply.Instr    // NEW
    | IMISC of Misc.Instr
    | IBRANCH of Branch.Instr
    | EMPTY

// In IMatch, add before Branch:
| Multiply.IMatch pa -> copy IMUL pa
```

**`ExecutionTop.fs`** — add dispatch:

```fsharp
| IMUL instr' ->
    Multiply.executeMul instr' dp'
```

**`app/js/monaco-init.js`** — add `MUL`, `MLA`, `UMULL`, `UMLAL`, `SMULL`, `SMLAL` to the syntax highlighting keyword list.

### Difficulty: Medium

The main complexity is the 64-bit arithmetic (use Fable's `int64`), the signed/unsigned distinction, and validating all the register constraints. The module boundary is clean — new file, new DU case, two wiring points.

---

## 3. Branch Exchange: BX, BLX

### ARM Semantics

| Instruction | Operation |
|-------------|-----------|
| `BX{cond} Rm` | PC = Rm; if Rm[0]=1, switch to Thumb mode |
| `BLX{cond} Rm` | LR = next instruction address; PC = Rm; if Rm[0]=1, switch to Thumb |
| `BLX label` | LR = next instruction; PC = label; switch to Thumb (always unconditional in ARM) |

**Key difference from B/BL:** The operand is a *register*, not a label. The bit 0 of the register determines ARM vs Thumb mode.

### Implementation Approach

Since VisUAL2 does not support Thumb mode, the implementation would:
- Accept `BX Rm` and `BLX Rm` as valid syntax
- Branch to the address in Rm (masking off bit 0)
- Either **ignore** the Thumb bit or **error** if bit 0 is set (design choice — ignoring is simpler and more useful for teaching)

#### `src/Emulator/Branch.fs`

**1. Extend the `Instr` type:**

```fsharp
type Instr =
    | B of uint32
    | BL of uint32
    | BX of RName         // NEW
    | BLX of RName        // NEW
    | END
```

**2. Extend `branchSpec`:**

```fsharp
let branchSpec = {
    InstrC = BRANCH
    Roots = [ "B"; "BL"; "BX"; "BLX"; "END" ]
    Suffixes = [ "" ]
}
```

**3. Update `parse`:**

```fsharp
| "BX", _ ->
    match parseRegister (ls.Operands.Trim()) with
    | Ok rm -> BX rm |> Ok
    | Error e -> Error e
| "BLX", _ ->
    match parseRegister (ls.Operands.Trim()) with
    | Ok rm -> BLX rm |> Ok
    | Error e -> Error e
```

**4. Update `executeBranch`:**

```fsharp
| BX rm ->
    let addr = getReg rm cpuData
    let target = addr &&& 0xFFFFFFFEu  // mask off Thumb bit
    cpuData |> setReg R15 target |> Ok
| BLX rm ->
    let addr = getReg rm cpuData
    let target = addr &&& 0xFFFFFFFEu
    cpuData
    |> setReg R15 target
    |> setReg R14 nxt
    |> Ok
```

**5. Update `branchTarget`** (used for stack tracking):

```fsharp
let branchTarget dp = function
    | B t | BL t -> [ t ]
    | BX rm -> [ dp.Regs.[rm] &&& 0xFFFFFFFEu ]
    | BLX rm -> [ dp.Regs.[rm] &&& 0xFFFFFFFEu ]
    | END -> []
```

Note: `branchTarget` currently doesn't take `DataPath` as an argument — it only works with immediate targets. You'd need to modify this or handle register branches separately in `ExecutionTop.asmStep` where the stack tracking calls `branchTarget`.

#### Optional: Thumb mode error

If you want to enforce ARM-only:

```fsharp
| BX rm ->
    let addr = getReg rm cpuData
    if addr &&& 1u <> 0u then
        ``Run time error`` (addr, "BX with bit 0 set switches to Thumb mode, which is not supported") |> Error
    else
        cpuData |> setReg R15 addr |> Ok
```

#### Other files

- **`app/js/monaco-init.js`**: Add `BX`, `BLX` to syntax highlighting
- **No changes to `ParseTop.fs` or `ExecutionTop.fs`** — these already dispatch to `Branch.executeBranch` for all `IBRANCH` cases

### Difficulty: Low

This is the simplest addition. Two new DU cases, straightforward register-operand parsing, and execution is just `setReg R15`. The only complication is deciding what to do about the Thumb bit.

---

## 4. Stack Shorthand: PUSH, POP

### ARM Semantics

| Instruction | Equivalent | Operation |
|-------------|------------|-----------|
| `PUSH{cond} {reglist}` | `STMDB SP!, {reglist}` | Store registers, decrement SP before each |
| `POP{cond} {reglist}` | `LDMIA SP!, {reglist}` | Load registers, increment SP after each |

These are pure aliases — they map directly to existing LDM/STM instructions with SP as the base register and writeback enabled.

### Implementation Approach: Parse-Time Desugaring in `Memory.fs`

The simplest and most maintainable approach is to desugar PUSH/POP into STM/LDM at parse time, reusing the existing `parseMult` and `executeMem` functions entirely.

#### `src/Emulator/Memory.fs`

**1. Add to opcode specs:**

```fsharp
let memSpecStack = {
    InstrC = MEM
    Roots = [ "PUSH"; "POP" ]
    Suffixes = [ "" ]  // no suffix — the mode is implicit
}
```

**2. Update `opCodes`:**

```fsharp
let opCodes =
    let toArr = opCodeExpand >> Map.toArray
    Array.collect toArr [| memSpecSingle; memSpecMultiple; memSpecStack |]
    |> Map.ofArray
```

**3. Update `parse'`** to handle the new roots:

```fsharp
| _, "PUSH" ->
    // Rewrite as STMDB SP!, {reglist}
    // Parse the register list from ls.Operands
    // Construct InstrMemMult with Rn=R13, WB=true, suff=DB
    parsePushPop STM DB pCond
| _, "POP" ->
    // Rewrite as LDMIA SP!, {reglist}
    parsePushPop LDM IA pCond
```

**4. Add `parsePushPop` helper:**

```fsharp
let parsePushPop memType suffix pCond =
    // PUSH/POP operand is just {reglist} — no base register in syntax
    // Rewrite operands as "SP!, {reglist}" and reuse parseMult logic
    // OR parse the register list directly:
    let ops = ls.Operands.Trim()
    match ops with
    | BRACKETED '{' '}' (rl, TRIM "") ->
        let regList = splitAny rl ','
        // ... same register list parsing as parseMult ...
        // Construct: consMemMult true(*WB*) R13(*SP*) checkedRegs suffix
        // Return: memType result
    | _ ->
        makeParseError "register list in braces, e.g. {R0-R3, LR}" ops "" |> Error
    |> fun ins -> copyParse ls ins pCond
```

Alternatively, the even simpler approach: in `parse'`, rewrite the opcode and operands as if the user wrote `STMDB SP!, {reglist}` or `LDMIA SP!, {reglist}`, patch the `LineData`, and call `parseMult` directly:

```fsharp
| _, "PUSH" ->
    let syntheticOperands = "SP!, " + ls.Operands.Trim()
    parseMult "STM" "DB" pCond  // with ls.Operands replaced
| _, "POP" ->
    let syntheticOperands = "SP!, " + ls.Operands.Trim()
    parseMult "LDM" "IA" pCond
```

This is the least code — `parseMult` already handles register lists, writeback, and validation.

#### Other files

- **`app/js/monaco-init.js`**: Add `PUSH`, `POP` to syntax highlighting
- **No changes to `ParseTop.fs`, `ExecutionTop.fs`, or any other file** — PUSH/POP desugar to existing LDM/STM instructions at parse time

### Difficulty: Low

PUSH and POP are syntactic sugar over LDM/STM which already work correctly. The implementation is essentially a parse-time rewrite. No new execution logic needed.

---

## Summary

| Feature | Difficulty | New Files | Files Modified | New DU Cases |
|---------|-----------|-----------|----------------|--------------|
| Half-word memory | Low–Medium | None | Memory.fs | `MHalf`, `MSignedHalf`, `MSignedByte` in `MSize` |
| Multiply | Medium | **Multiply.fs** | ParseTop.fs, ExecutionTop.fs, Emulator.fsproj | `IMUL` in `ParseTop.Instr` |
| Branch exchange | Low | None | Branch.fs | `BX`, `BLX` in `Branch.Instr` |
| Stack shorthand | Low | None | Memory.fs | None (desugars to existing LDM/STM) |

### Recommended Implementation Order

1. **PUSH / POP** — Trivial alias, instant value for students, no new types
2. **BX / BLX** — Small, self-contained, two new DU cases
3. **Half-word memory** — Extends existing pattern, moderate parsing work
4. **Multiply** — Most involved: new module, 64-bit arithmetic, new dispatch path

### Cross-Cutting Concerns

For all additions:
- Update **`app/js/monaco-init.js`** to syntax-highlight the new mnemonics
- Add entries to **`app/hovers/`** markdown files if you want hover documentation
- Update **`docs/arm-instructions.md`** to move items from "Notable Limitations" to their proper sections
- Consider adding **test data** in `app/test-data/` using the VisualRandomTestGen tool
- Consider adding **testbench** examples exercising the new instructions
