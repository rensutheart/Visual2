# VisUAL2 Potential Bugs & Code Issues

Issues identified during code review. Items marked **FIXED** have been resolved.

---

## FIXED: FILL directive with 2 arguments was broken

**File:** `src/Emulator/Misc.fs` (line ~131)

The `FILL` directive supports an optional fill value (`FILL nBytes, fillValue`), and the internal `makeFILL` function correctly handles both 1-arg and 2-arg cases. However, the pattern match in `parse` only routed the 1-operand case:

```fsharp
-- Before (broken):
| "FILL", RESOLVEALL [ op ] -> makeFILL [ op, 0u ]

-- After (fixed):
| "FILL", RESOLVEALL ops -> makeFILL ops
```

`RESOLVEALL [ op ]` required exactly one resolved operand. `FILL 8, 0xFF` has two operands, so the pattern failed and fell through to a misleading "unresolved symbols" error. The 2-argument code path in `makeFILL` was dead code.

---

## Swapped doc comments on MSize type

**File:** `src/Emulator/Memory.fs` (lines 18–19)  
**Severity:** Cosmetic

The XML doc comments on `MSize` are swapped:

```fsharp
type MSize =
    | MWord /// LDRB,STRB   <-- should say "LDR,STR"
    | MByte /// LDR,STR     <-- should say "LDRB,STRB"
```

No runtime impact — these are only comments.

---

## Dead code: execAdr / makeAdrInstr in DP.fs

**File:** `src/Emulator/DP.fs` (lines 185–197, 384–393)  
**Severity:** Low (dead code)

`execAdr` and `makeAdrInstr` in DP.fs are never called. ADR is parsed and executed entirely through Misc.fs and `ExecutionTop.executeADR`. These functions appear to be leftover from an earlier implementation.

Additionally, the dead `execAdr` has a suspect range check at line 194:
```fsharp
| op when int64 (d.Regs.[R15] + 8u) - int64 (op) |> abs < 0x400L ->
```
During execution, `d.Regs.[R15]` already includes the +8 pipelining offset (added by `dataPathStep`), so `+ 8u` would double-count it. This doesn't matter since the function is unused, but would be wrong if it were ever called.

---

## Dead code: fetchMemData / fetchMemByte in Helpers.fs

**File:** `src/Emulator/Helpers.fs` (lines 380–424)  
**Severity:** Low (dead code)

`fetchMemData` and `fetchMemByte` are defined but never called anywhere. Memory access in the actual execution path goes through `Memory.getDataMemWord` and `Memory.getDataMemByte` instead.
