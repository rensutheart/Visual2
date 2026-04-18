# Headless Marking Guide — VisUAL2-SU

## Overview

VisUAL2-SU's ARM emulator is a pure F# library (`src/Emulator/`) that is completely independent of the Electron GUI. This means student assembly code can be parsed, executed, and verified entirely from a standalone .NET console application — no display, no Electron, no user interaction.

This guide explains how to build a headless marking harness that:
1. Takes a student's ARM assembly code as a string
2. Parses and loads it into the emulator
3. Runs it to completion (or a step limit)
4. Inspects registers, flags, memory (including the display region at `0x2000`), and execution metadata
5. Reports pass/fail results

---

## Architecture Summary

```
src/Emulator/          <-- Pure F# library, no GUI dependencies
  CommonData.fs        <-- Core types: DataPath, Flags, RName, WAddr, DataMemory
  ExecutionTop.fs      <-- Top-level API: reLoadProgram, asmStep, getRunInfoFromImageWithInits
  Testlib.fs           <-- Built-in testbench framework (spec-driven testing)
  ParseTop.fs          <-- Assembly parser
  DP.fs / Memory.fs / Branch.fs / Multiply.fs / Misc.fs / Saturate.fs  <-- Instruction modules

src/Emulator/Emulator.fsproj   <-- netstandard2.0 library, can be referenced from any .NET project
test/                           <-- Existing headless test projects (working examples)
```

The emulator compiles to a `netstandard2.0` library. Any .NET Core console app can reference it and call the API directly.

---

## Step 1: Create a Marking Console Project

Create a new .NET Core console project that references the emulator library:

```xml
<!-- MarkingHarness/MarkingHarness.fsproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>netcoreapp2.1</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Program.fs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\src\Emulator\Emulator.fsproj" />
  </ItemGroup>
</Project>
```

Build with:
```bash
dotnet build MarkingHarness/MarkingHarness.fsproj
```

Run with:
```bash
dotnet run --project MarkingHarness/MarkingHarness.fsproj
```

> **Note:** The project uses .NET Core SDK 2.1.818 (see `global.json`). Ensure this SDK version is installed, or update `global.json` / the `TargetFramework` to match your environment.

---

## Step 2: Core API Reference

All key functions live in the `ExecutionTop` module. Open these namespaces:

```fsharp
open CommonData
open ExecutionTop
```

### 2.1 Parse and Load Assembly

```fsharp
let reLoadProgram (lines: string list) : LoadImage
```

Takes a list of assembly lines (one string per line) and returns a `LoadImage` containing:
- `Code` — parsed instructions mapped by word address
- `Mem` — data memory (DCD/DCB/FILL regions)
- `Errors` — list of `(ParseError * lineNumber * opcodeString)`
- `SymInf` — symbol table and reference info
- `Source` — indented source listing

**Always check `lim.Errors` before proceeding.** If non-empty, the code has syntax/parse errors.

### 2.2 Initialise Execution Context

```fsharp
let getRunInfoFromImageWithInits
    (breakCond: BreakCondition)
    (lim: LoadImage)
    (regsInit: Map<RName, uint32>)
    (flagsInit: Flags)
    (mMap: Map<uint32, uint32>)   // extra memory to pre-load (address -> value)
    (mm: DataMemory)              // existing data memory (usually lim.Mem)
    : RunInfo
```

Parameters:
- `breakCond` — use `NoBreak` for marking (runs to completion or step limit)
- `lim` — the `LoadImage` from `reLoadProgram`
- `regsInit` — use `initialRegMap` for defaults (R13=0xFF000000, all others=0), or override specific registers
- `flagsInit` — typically `{N=false; C=false; Z=false; V=false}`
- `mMap` — additional memory words to pre-load (e.g., test input data); use `Map.empty` if none
- `mm` — pass `lim.Mem` to include data declarations from the student's code

### 2.3 Execute

```fsharp
let asmStep (numSteps: int64) (ri: RunInfo) : RunInfo
```

Runs up to `numSteps` instructions. Returns updated `RunInfo`. The `State` field tells you how execution ended:

| State | Meaning |
|-------|---------|
| `PSExit` | Program reached `END` or fell off the end cleanly |
| `PSRunning` | Step limit reached, program still had more to do |
| `PSError e` | Runtime error (e.g., invalid memory access) |
| `PSBreak` | Breakpoint hit (not typically used in marking) |

### 2.4 Read Results

From the returned `RunInfo`:

```fsharp
let dp = fst ri.dpCurrent   // DataPath — the final CPU state

// Registers
let r0 = dp.Regs.[R0]       // uint32
let sp = dp.Regs.[R13]
let lr = dp.Regs.[R14]
let pc = dp.Regs.[R15]

// Flags
let flags = dp.Fl            // { N: bool; C: bool; Z: bool; V: bool }

// Memory (word-addressed)
match Map.tryFind (WA 0x100u) dp.MM with
| Some (Dat value) -> printfn "0x%08X" value
| Some CodeSpace   -> printfn "Code region"
| None             -> printfn "Uninitialized"

// Metadata
ri.StepsDone    // int64 — instructions executed
ri.CyclesDone   // int64 — cycle count
ri.Coverage     // int Set — executed source line numbers
ri.State        // ProgState — how execution ended
```

---

## Step 3: Minimal Marking Harness

```fsharp
module MarkingHarness

open CommonData
open ExecutionTop

/// Parse, load, and run student code. Returns None on parse error.
let runStudentCode (code: string) (maxSteps: int64) =
    let lines = code.Split([| '\n'; '\r' |], System.StringSplitOptions.RemoveEmptyEntries)
                |> Array.toList
    let lim = reLoadProgram lines
    if lim.Errors.Length > 0 then
        lim.Errors |> List.iter (fun (e, line, opc) ->
            printfn "  Parse error line %d (%s): %A" line opc e)
        None
    else
        let ri = getRunInfoFromImageWithInits
                    NoBreak lim initialRegMap
                    {N=false; C=false; Z=false; V=false}
                    Map.empty lim.Mem
        asmStep maxSteps ri |> Some


/// Check a register value. Returns (passed, message).
let checkReg (ri: RunInfo) (rn: RName) (expected: uint32) (desc: string) =
    let actual = (fst ri.dpCurrent).Regs.[rn]
    if actual = expected then
        true, sprintf "PASS: %s (%A = 0x%X)" desc rn actual
    else
        false, sprintf "FAIL: %s (%A expected 0x%X, got 0x%X)" desc rn expected actual


/// Check a memory word. Returns (passed, message).
let checkMem (ri: RunInfo) (addr: uint32) (expected: uint32) (desc: string) =
    let mm = (fst ri.dpCurrent).MM
    match Map.tryFind (WA addr) mm with
    | Some (Dat v) when v = expected ->
        true, sprintf "PASS: %s ([0x%X] = 0x%X)" desc addr v
    | Some (Dat v) ->
        false, sprintf "FAIL: %s ([0x%X] expected 0x%X, got 0x%X)" desc addr expected v
    | _ ->
        false, sprintf "FAIL: %s ([0x%X] not in memory)" desc addr


/// Check a flag value. Returns (passed, message).
let checkFlag (ri: RunInfo) (flagName: string) (expected: bool) =
    let flags = (fst ri.dpCurrent).Fl
    let actual = match flagName with
                 | "N" -> flags.N | "Z" -> flags.Z
                 | "C" -> flags.C | "V" -> flags.V
                 | _ -> failwithf "Unknown flag: %s" flagName
    if actual = expected then
        true, sprintf "PASS: %s = %b" flagName actual
    else
        false, sprintf "FAIL: %s expected %b, got %b" flagName expected actual


/// Check that execution completed normally.
let checkExited (ri: RunInfo) =
    match ri.State with
    | PSExit -> true, "PASS: Program exited normally"
    | PSRunning -> false, "FAIL: Program did not terminate (step limit reached)"
    | PSError e -> false, sprintf "FAIL: Runtime error: %A" e
    | PSBreak -> false, "FAIL: Unexpected breakpoint"


[<EntryPoint>]
let main argv =
    // Example: read student code from a file path passed as argument,
    // or embed it as a string
    let studentCode = """
        MOV R0, #10
        MOV R1, #20
        ADD R2, R0, R1
    """

    printfn "=== Running student code ==="
    match runStudentCode studentCode 10000L with
    | None ->
        printfn "RESULT: 0/N — code failed to parse"
        1
    | Some ri ->
        let results = [
            checkExited ri
            checkReg ri R2 30u "R2 = R0 + R1"
        ]
        results |> List.iter (fun (_, msg) -> printfn "  %s" msg)
        let passed = results |> List.filter fst |> List.length
        let total = results.Length
        printfn "RESULT: %d/%d" passed total
        if passed = total then 0 else 1
```

---

## Step 4: Pre-loading Registers and Memory

For questions where students write subroutines, you often need to set up input values before running.

### Pre-set Registers

```fsharp
let customRegs =
    initialRegMap
    |> Map.add R0 42u        // set R0 = 42
    |> Map.add R1 0x1000u    // set R1 = pointer to data

let ri = getRunInfoFromImageWithInits NoBreak lim customRegs
            {N=false; C=false; Z=false; V=false} Map.empty lim.Mem
```

### Pre-load Memory (Test Input Data)

Use the `mMap` parameter to inject word-sized data at specific addresses:

```fsharp
let inputData =
    [ (0x1000u, 5u); (0x1004u, 10u); (0x1008u, 15u) ]
    |> Map.ofList

let ri = getRunInfoFromImageWithInits NoBreak lim initialRegMap
            {N=false; C=false; Z=false; V=false} inputData lim.Mem
```

### Pre-load Byte-Level Memory (e.g., Display Region)

For byte-granularity data (like display pixels), you need to pack bytes into words and add them to `lim.Mem` directly:

```fsharp
/// Write a byte into a DataMemory map
let writeByte (mm: DataMemory) (byteAddr: uint32) (value: byte) =
    let wordAddr = byteAddr &&& 0xFFFFFFFCu
    let byteOffset = int (byteAddr &&& 3u)
    let existing =
        match Map.tryFind (WA wordAddr) mm with
        | Some (Dat v) -> v
        | _ -> 0u
    let mask = ~~~(0xFFu <<< (byteOffset * 8))
    let newVal = (existing &&& mask) ||| ((uint32 value) <<< (byteOffset * 8))
    Map.add (WA wordAddr) (Dat newVal) mm
```

---

## Step 5: Reading the Memory-Mapped Display

The display is just a region of memory starting at base address `0x2000`. Each byte is a VGA 256-colour palette index. Grid layout is row-major: pixel (x, y) = base + y * width + x.

To read the display after execution:

```fsharp
/// Read a byte from DataMemory
let readByte (mm: DataMemory) (byteAddr: uint32) : byte option =
    let wordAddr = byteAddr &&& 0xFFFFFFFCu
    let byteOffset = int (byteAddr &&& 3u)
    match Map.tryFind (WA wordAddr) mm with
    | Some (Dat v) -> Some (byte ((v >>> (byteOffset * 8)) &&& 0xFFu))
    | _ -> None

/// Read the entire display grid as a 2D byte array
let readDisplay (ri: RunInfo) (baseAddr: uint32) (width: int) =
    let mm = (fst ri.dpCurrent).MM
    Array2D.init width width (fun y x ->
        let addr = baseAddr + uint32 (y * width + x)
        readByte mm addr |> Option.defaultValue 0uy)

// Usage
let grid = readDisplay result 0x2000u 16   // 16x16 grid
let pixel_5_2 = grid.[2, 5]               // colour index at (x=5, y=2)
```

### Checking Display Output

For marking display-based questions, compare the display memory region against expected pixel values:

```fsharp
/// Check specific pixels in the display
let checkPixel (ri: RunInfo) (baseAddr: uint32) (width: int) (x: int) (y: int) (expected: byte) =
    let mm = (fst ri.dpCurrent).MM
    let addr = baseAddr + uint32 (y * width + x)
    match readByte mm addr with
    | Some v when v = expected ->
        true, sprintf "PASS: pixel(%d,%d) = 0x%02X" x y v
    | Some v ->
        false, sprintf "FAIL: pixel(%d,%d) expected 0x%02X, got 0x%02X" x y expected v
    | None ->
        false, sprintf "FAIL: pixel(%d,%d) not written" x y

/// Check that the entire display matches an expected grid
let checkDisplayRegion (ri: RunInfo) (baseAddr: uint32) (width: int) (expected: byte[,]) =
    let mm = (fst ri.dpCurrent).MM
    let mutable errors = []
    for y in 0 .. width - 1 do
        for x in 0 .. width - 1 do
            let addr = baseAddr + uint32 (y * width + x)
            let exp = expected.[y, x]
            match readByte mm addr with
            | Some v when v <> exp ->
                errors <- sprintf "pixel(%d,%d): expected 0x%02X, got 0x%02X" x y exp v :: errors
            | None when exp <> 0uy ->
                errors <- sprintf "pixel(%d,%d): expected 0x%02X, not written" x y exp :: errors
            | _ -> ()
    errors |> List.rev
```

### Animations and R10

In the GUI, `R10 = 1` triggers a display refresh. When running headlessly, this has **no effect** — the display is not rendered, and `R10` is just a normal register. After execution, the memory at `0x2000` contains whatever the final state of the display is (the last frame drawn).

If you need to verify intermediate animation frames, you can use `DisplayBreak` as the break condition, which causes execution to pause whenever `R10` becomes 1:

```fsharp
let ri = getRunInfoFromImageWithInits DisplayBreak lim initialRegMap
            {N=false; C=false; Z=false; V=false} Map.empty lim.Mem

// Run until first R10=1 frame
let frame1 = asmStep 100000L ri
let grid1 = readDisplay frame1 0x2000u 16
// ... check frame 1 ...

// Continue to next frame
let frame2 = asmStep 200000L frame1
let grid2 = readDisplay frame2 0x2000u 16
// ... check frame 2 ...
```

Note: after a `DisplayBreak` pause, `ri.State = PSBreak` and `ri.BreakCond = DisplayBreak`. Call `asmStep` with a higher step count to continue.

---

## Step 6: Using the Built-in Testbench System

VisUAL2 has a built-in testbench specification language (in `Testlib.fs`) designed for exactly this use case. A testbench is a text file describing inputs and expected outputs.

### Testbench File Format

```
;; Testbench for student subroutine
#TEST 1 Add two numbers
IN R0 IS 10
IN R1 IS 20
OUT R2 IS 30

#TEST 2 With memory
IN R0 IS 0x1000
IN R0 PTR 5, 10, 15, 20
OUT R1 IS 50

#TEST 3 Preserve registers
PERSISTENTREGS R4, R5, R6, R7
IN R0 IS 42
OUT R1 IS 84
STACKPROTECT
```

### Testbench Spec Commands

| Command | Direction | Meaning |
|---------|-----------|---------|
| `IN Rn IS value` | Input | Set register Rn to value before execution |
| `OUT Rn IS value` | Output | Assert register Rn equals value after execution |
| `IN Rn PTR v1, v2, ...` | Input | Set Rn to point to allocated memory containing [v1, v2, ...] |
| `OUT Rn PTR v1, v2, ...` | Output | Assert memory pointed to by Rn contains [v1, v2, ...] |
| `PERSISTENTREGS R4, R5, ...` | Both | Set listed regs to random values; assert they are unchanged after |
| `STACKPROTECT` | Output | Assert SP is restored and no writes above initial SP |
| `RANDOMISEINITVALS` | Input | Randomise all R0-R12 before execution |
| `BRANCHTOSUB name` | Input | Start execution at label `name` (for testing subroutines) |
| `RELABEL old new` | Input | Rename a label in student code (e.g., if students use different names) |
| `DATAAREA addr` | Input | Set data allocation start address |
| `APPENDCODE n` | Input | Append a code block (defined by `#BLOCK n`) to the student code |

### Running Testbenches Programmatically

```fsharp
open TestLib

let tbText = """
;; My testbench
#TEST 1 Basic add
IN R0 IS 10
IN R1 IS 20
OUT R2 IS 30
"""

let studentCode = [
    "    ADD R2, R0, R1"
]

// Parse the testbench
let tbLines = tbText.Split([| '\n'; '\r' |]) |> Array.toList
let tests = parseTests initStackPointer 0x200u tbLines

// Run each test
tests |> List.iter (fun testResult ->
    match testResult with
    | Error errs ->
        errs |> List.iter (fun (line, msg) -> printfn "TB parse error line %d: %s" line msg)
    | Ok test ->
        let result = runTestOnCode test studentCode
        printfn "Test %d (%s): %s" result.TestNum result.TestName
            (if result.TestOk then "PASSED" else "FAILED")
        result.TestMsgs |> List.iter (printfn "  %s")
)
```

### Subroutine Testing with `BRANCHTOSUB`

For questions where students write a subroutine (not a complete program), use `BRANCHTOSUB`:

```
#TEST 1 Multiply subroutine
BRANCHTOSUB multiply
IN R0 IS 6
IN R1 IS 7
OUT R0 IS 42
STACKPROTECT
PERSISTENTREGS R4, R5, R6
```

This sets `PC` to the address of label `multiply` and `LR` to a sentinel (`0xFFFFFFFC`). The test passes when execution reaches `BX LR` (returning to the sentinel address), the expected outputs match, and the stack / persistent registers are preserved.

---

## Step 7: Batch Marking from Files

A practical batch marking workflow:

```fsharp
open System.IO

[<EntryPoint>]
let main argv =
    let submissionsDir = argv.[0]    // e.g., "./submissions/"
    let tbFile = argv.[1]            // e.g., "./testbench.txt"

    let tbLines = File.ReadAllLines(tbFile) |> Array.toList
    let tests = TestLib.parseTests initStackPointer 0x200u tbLines

    let studentFiles = Directory.GetFiles(submissionsDir, "*.s")

    printfn "Student, Total, Passed, Failed"
    for file in studentFiles do
        let studentName = Path.GetFileNameWithoutExtension(file)
        let code = File.ReadAllLines(file) |> Array.toList

        let mutable passed = 0
        let mutable failed = 0

        tests |> List.iter (fun testResult ->
            match testResult with
            | Ok test ->
                let result = TestLib.runTestOnCode test code
                if result.TestOk then passed <- passed + 1
                else failed <- failed + 1
            | Error _ -> failed <- failed + 1
        )

        printfn "%s, %d, %d, %d" studentName (passed + failed) passed failed

    0
```

Run:
```bash
dotnet run --project MarkingHarness -- ./submissions/ ./q1-testbench.txt
```

---

## Step 8: Edge Cases and Practical Considerations

### Step Limit

Always set a reasonable step limit (e.g., 100,000) to catch infinite loops:

```fsharp
let maxSteps = 100000L
let result = asmStep maxSteps ri
match result.State with
| PSRunning -> printfn "FAIL: Program did not terminate within %d instructions" maxSteps
| _ -> ()
```

### Runtime Errors

The emulator reports runtime errors (invalid memory access, executing data, etc.) via `PSError`:

```fsharp
match result.State with
| PSError (Errors.``Run time error``(addr, msg)) ->
    printfn "Runtime error at 0x%X: %s" addr msg
| PSError e ->
    printfn "Error: %A" e
| _ -> ()
```

### Symbol Table Access

To check that students defined correct labels or to find subroutine addresses:

```fsharp
let lim = reLoadProgram lines

// Symbol table: Map<string, uint32>
let symTab = lim.SymInf.SymTab

match Map.tryFind "mySubroutine" symTab with
| Some addr -> printfn "mySubroutine is at address 0x%X" addr
| None -> printfn "Label 'mySubroutine' not found"
```

### Code Coverage

`ri.Coverage` is a `Set<int>` of source line numbers that were executed. Useful for checking that students actually used required instructions or didn't have dead code:

```fsharp
let coveragePercent =
    let totalLines = lim.Source.Length
    let executedLines = Set.count ri.Coverage
    float executedLines / float totalLines * 100.0
printfn "Code coverage: %.1f%%" coveragePercent
```

### Cycle Count

`ri.CyclesDone` gives the total cycle count, which can be used to assess efficiency:

```fsharp
printfn "Completed in %d cycles" ri.CyclesDone
```

### Initial Register State

Default `initialRegMap` sets:
- R13 (SP) = `0xFF000000`
- All other registers = `0`

### Memory Model

- Memory is word-addressed internally via `WAddr = WA of uint32`
- Each word stores a `Data = Dat of uint32 | CodeSpace`
- Byte-level access (LDR/LDRB/STRB) is handled by the emulator's memory instruction implementation
- Unaccessed memory locations don't exist in the map (they are implicitly zero in the emulator's load/store logic, but `Map.tryFind` returns `None`)

---

## Step 9: Quick-Start Checklist

1. **Install .NET Core SDK 2.1** (or update `global.json` + `TargetFramework`)
2. **Create a console project** referencing `src/Emulator/Emulator.fsproj`
3. **Load student code**: `reLoadProgram (code.Split('\n') |> Array.toList)`
4. **Check for parse errors**: `lim.Errors`
5. **Init execution**: `getRunInfoFromImageWithInits NoBreak lim initialRegMap {N=false;C=false;Z=false;V=false} Map.empty lim.Mem`
6. **Run**: `asmStep 100000L ri`
7. **Check state**: `ri.State` should be `PSExit`
8. **Read registers**: `(fst ri.dpCurrent).Regs.[R0]`
9. **Read memory**: `Map.tryFind (WA addr) (fst ri.dpCurrent).MM`
10. **Read display**: bytes at `0x2000` to `0x2000 + width*width - 1`

---

## Existing Examples

Working headless test projects in the repository that demonstrate all of these patterns:

| Project | What it tests | Key patterns demonstrated |
|---------|---------------|--------------------------|
| [test/BxBlxTest/](../test/BxBlxTest/Program.fs) | BX/BLX branch instructions | `runProgram`, `assertRegEq`, `assertState`, conditional tests |
| [test/PushPopTest/](../test/PushPopTest/Program.fs) | PUSH/POP stack operations | `assertMemEq`, stack pointer checks, memory word assertions |
| [test/MultiplyTest/](../test/MultiplyTest/) | MUL/MLA instructions | Arithmetic result checking |
| [test/HalfWordTest/](../test/HalfWordTest/) | Half-word loads (LDRH etc.) | Byte/half-word memory access patterns |
| [test/SatDoubleTest/](../test/SatDoubleTest/) | Saturating arithmetic, LDRD/STRD | Multi-register results, saturating checks |

Build and run any of these to confirm the headless infrastructure works:

```bash
cd test/BxBlxTest
dotnet run
```

---

## Summary of Key Types

```
LoadImage
  .Errors    : (ParseError * int * string) list    -- parse errors (empty = success)
  .Code      : Map<WAddr, CondInstr * int>         -- instruction memory
  .Mem       : Map<WAddr, Data>                    -- data memory
  .SymInf    : SymbolInfo                          -- symbol table (.SymTab : Map<string, uint32>)
  .Source     : string list                         -- indented source listing

RunInfo
  .dpCurrent : DataPath * UFlags                   -- current CPU state (use fst to get DataPath)
  .State     : PSExit | PSRunning | PSError | PSBreak
  .StepsDone : int64
  .CyclesDone: int64
  .Coverage  : int Set                             -- executed line numbers

DataPath
  .Regs      : Map<RName, uint32>                  -- register file
  .Fl        : Flags                               -- { N; Z; C; V : bool }
  .MM        : Map<WAddr, Data>                    -- memory

RName = R0 | R1 | ... | R12 | R13 | R14 | R15
WAddr = WA of uint32
Data  = Dat of uint32 | CodeSpace
```
