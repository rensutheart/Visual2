# VisUAL2 Developer Guide

A walkthrough of the VisUAL2 codebase for new developers. This document explains the project structure, build system, data flow, and key abstractions so you can understand, modify, and extend the ARM simulator.

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Build System & Toolchain](#build-system--toolchain)
3. [Project Structure](#project-structure)
4. [Emulator Core (src/Emulator/)](#emulator-core-srcemulator)
5. [Pipelining Model](#pipelining-model)
6. [Renderer / GUI (src/Renderer/)](#renderer--gui-srcrenderer)
7. [Electron Main Process (src/Main/)](#electron-main-process-srcmain)
8. [Frontend Assets (app/)](#frontend-assets-app)
9. [Data Flow: Source Code → Execution → Display](#data-flow-source-code--execution--display)
10. [Key Abstractions](#key-abstractions)
11. [How to Add a New Instruction](#how-to-add-a-new-instruction)
12. [Testing & Testbenches](#testing--testbenches)
13. [Common Tasks](#common-tasks)

> **See also:** [ARM Instructions Reference](arm-instructions.md) for the complete list of supported instructions, operand formats, and condition codes.

---

## Architecture Overview

VisUAL2 is a desktop ARM assembly simulator. It is written almost entirely in **F#**, transpiled to JavaScript via **Fable 2.x**, bundled by **Webpack 3**, and packaged as a desktop app with **Electron 2**.

```
┌─────────────────────────────────────────────────┐
│                  Electron Shell                  │
│                                                  │
│  ┌──────────────┐     ┌───────────────────────┐  │
│  │ Main Process │     │   Renderer Process    │  │
│  │  (main.js)   │◄───►│  (app/index.html +    │  │
│  │              │ IPC │   app/js/renderer.js)  │  │
│  │ Window mgmt, │     │                       │  │
│  │ file I/O,    │     │ ┌───────────────────┐ │  │
│  │ app lifecycle│     │ │  Monaco Editor    │ │  │
│  └──────────────┘     │ │  (code editing)   │ │  │
│                       │ └───────────────────┘ │  │
│                       │ ┌───────────────────┐ │  │
│                       │ │ Renderer (F#→JS)  │ │  │
│                       │ │ GUI logic, views, │ │  │
│                       │ │ event handling    │ │  │
│                       │ └────────┬──────────┘ │  │
│                       │          │             │  │
│                       │ ┌────────▼──────────┐ │  │
│                       │ │ Emulator (F#→JS)  │ │  │
│                       │ │ Parse, execute,   │ │  │
│                       │ │ ARM state mgmt    │ │  │
│                       │ └───────────────────┘ │  │
│                       └───────────────────────┘  │
└─────────────────────────────────────────────────┘
```

**Two Electron processes:**
- **Main process** (`main.js`): manages the window, handles native file dialogs, app lifecycle. Boilerplate — rarely needs changes.
- **Renderer process** (`app/js/renderer.js`): runs in Chromium. Contains the Monaco editor, the full ARM emulator, and all GUI logic. This is where 99% of development happens.

**Two F# projects compiled into the renderer:**
- **Emulator** (`src/Emulator/`): Pure ARM simulation logic — parsing assembly, executing instructions, managing CPU state. No DOM or browser dependencies.
- **Renderer** (`src/Renderer/`): GUI code that wires the Emulator to the Monaco editor and HTML dashboard. Depends on Emulator.

---

## Build System & Toolchain

### Prerequisites

| Tool | Version | Purpose |
|------|---------|---------|
| .NET Core SDK | 2.1.x | F# compiler, Paket package manager, Fable CLI tool |
| Node.js | 12+ (tested with 20) | Webpack, Electron, npm modules |
| Yarn | 1.x | Node package manager (used instead of npm) |

> **Important:** Fable 2.x requires .NET Core 2.1. If you only have .NET 8+, install 2.1 side-by-side:
> ```powershell
> # Download dotnet-install.ps1 from https://dot.net/v1/dotnet-install.ps1
> .\dotnet-install.ps1 -Channel 2.1 -InstallDir "$env:LOCALAPPDATA\dotnet21"
> ```
> Then set environment variables before building:
> ```powershell
> $env:DOTNET_MULTILEVEL_LOOKUP = "0"
> $env:DOTNET_ROOT = "$env:LOCALAPPDATA\dotnet21"
> $env:PATH = "$env:LOCALAPPDATA\dotnet21;$env:PATH"
> ```

### Setup (first time)

```bash
.paket\paket.exe install       # Install F# NuGet dependencies
dotnet restore src\Main\Main.fsproj
dotnet restore src\Renderer\Renderer.fsproj
yarn install                    # Install Node.js dependencies
```

### Build Commands

| Command | What it does |
|---------|-------------|
| `yarn build` | One-shot production build: Fable compiles F# → JS, Webpack bundles into `main.js` + `app/js/renderer.js` |
| `yarn start` | Dev mode: Fable + Webpack in watch mode — recompiles on file save |
| `yarn launch` | Run the Electron app (`electron . -w` with hot reload) |
| `yarn pack-win` | Build + package Windows .exe distribution |
| `yarn pack-all` | Build + package for Windows, macOS, and Linux |

### How the Build Pipeline Works

```
F# source (.fs files)
    │
    ▼ Fable 2.x (dotnet-fable)
ES2015 JavaScript modules
    │
    ▼ Babel (ES2015 → compatible JS)
Transpiled JavaScript
    │
    ▼ Webpack 3 (bundling)
    ├── main.js          (Electron main process)
    └── app/js/renderer.js  (Electron renderer, ~3 MB)
```

**webpack.config.js** defines two build targets:
1. `mainConfig`: compiles `src/Main/Main.fsproj` → `main.js` (Electron main)
2. `rendererConfig`: compiles `src/Renderer/Renderer.fsproj` → `app/js/renderer.js` (includes Emulator as dependency)

Webpack also copies Monaco Editor assets and Tippy.js into the app directory via `CopyWebpackPlugin`.

---

## Project Structure

```
Visual2_repo/
├── src/
│   ├── Emulator/          # ARM emulation engine (pure F#, no DOM)
│   │   ├── EEExtensions.fs    # F# stdlib extensions for Fable compatibility
│   │   ├── CommonData.fs      # Core types: DataPath, registers, memory, flags
│   │   ├── Errors.fs          # Parse & runtime error types
│   │   ├── Expressions.fs     # Numeric expression parser (labels, literals, arithmetic)
│   │   ├── CommonLex.fs       # Shared parse types, condition codes, opcode expansion
│   │   ├── Helpers.fs         # Register/memory access, active patterns, condition eval
│   │   ├── Memory.fs          # LDR, STR, LDM, STM instructions
│   │   ├── DP.fs              # Data processing: ADD, SUB, MOV, CMP, AND, ORR, shifts...
│   │   ├── Branch.fs          # B, BL, END instructions
│   │   ├── Misc.fs            # Pseudo-instructions: DCD, DCB, FILL, EQU, ADR
│   │   ├── ParseTop.fs        # Top-level parser dispatcher (routes to DP/Memory/Branch/Misc)
│   │   ├── ExecutionTop.fs    # Execution engine, program loading, multi-pass symbol resolution
│   │   ├── Testlib.fs         # Testbench parsing, execution, and result checking
│   │   ├── Test.fs            # Additional test infrastructure
│   │   ├── Emulator.fsproj    # Project file (compile order matters in F#!)
│   │   └── paket.references   # NuGet dependencies for this project
│   │
│   ├── Renderer/          # GUI & editor integration (depends on Emulator)
│   │   ├── Refs.fs            # Global state, DOM element references, settings
│   │   ├── Tooltips.fs        # Inline execution tooltips (memory addresses, shifts)
│   │   ├── Stats.fs           # Code statistics display
│   │   ├── Editors.fs         # Monaco editor configuration and decoration
│   │   ├── ErrorDocs.fs       # Error hover help messages
│   │   ├── Tabs.fs            # Multi-tab file management
│   │   ├── Settings.fs        # User preferences dialog
│   │   ├── Views.fs           # Register, memory, symbol table rendering
│   │   ├── Files.fs           # File open/save via Electron dialogs
│   │   ├── Testbench.fs       # Test runner UI
│   │   ├── Integration.fs     # Emulator ↔ GUI glue (parse, run, step, display)
│   │   ├── Tests.fs           # Dev testing utilities
│   │   ├── MenuBar.fs         # Application menus (File, Edit, View, Test)
│   │   ├── Renderer.fs        # Entry point: init(), event handler setup
│   │   ├── Renderer.fsproj    # Project file
│   │   └── paket.references
│   │
│   └── Main/              # Electron main process (boilerplate)
│       ├── Main.fs            # Window creation, IPC, app lifecycle
│       ├── Main.fsproj
│       └── paket.references
│
├── app/                   # Frontend assets (served by Electron)
│   ├── index.html             # Main HTML: toolbar, editor pane, dashboard pane
│   ├── css/                   # Stylesheets (Photon UI, Vex modals, custom)
│   ├── fonts/                 # Fira Code (editor), Material Icons, Photon icons
│   ├── js/
│   │   ├── monaco-init.js     # Monaco editor setup: ARM syntax highlighting, themes
│   │   ├── svg.min.js         # SVG rendering library
│   │   ├── tippy.all.min.js   # Tooltip library
│   │   └── vex.combined.min.js # Modal dialog library
│   ├── hovers/                # Markdown files for opcode hover help
│   ├── samples/               # Example programs (karatsuba.s)
│   ├── resources/             # Icons, error test pages
│   ├── test-data/             # Randomised test input files
│   └── test-results/          # Expected test outputs
│
├── docs/                  # Documentation (GitHub Pages)
├── .paket/                # Paket package manager binaries
├── package.json           # Node dependencies & build scripts
├── paket.dependencies     # F# NuGet dependency specs
├── paket.lock             # Locked dependency versions
├── webpack.config.js      # Webpack build configuration
├── renderer.js            # Stub (actual output goes to app/js/renderer.js)
└── main.js                # Build output: Electron main process
```

### File Compile Order

F# requires files to be compiled in dependency order (each file can only reference files above it). The order is defined in each `.fsproj`:

**Emulator** (bottom-up):
```
EEExtensions.fs → CommonData.fs → Errors.fs → Expressions.fs → CommonLex.fs
→ Helpers.fs → Memory.fs → DP.fs → Branch.fs → Misc.fs → ParseTop.fs
→ ExecutionTop.fs → Testlib.fs
```

**Renderer** (bottom-up):
```
Refs.fs → Tooltips.fs → Stats.fs → Editors.fs → ErrorDocs.fs → Tabs.fs
→ Settings.fs → Views.fs → Files.fs → Testbench.fs → Integration.fs
→ Tests.fs → MenuBar.fs → Renderer.fs
```

---

## Emulator Core (src/Emulator/)

### CommonData.fs — The ARM State

The central type is `DataPath`, representing the complete ARM CPU state:

```fsharp
type DataPath = {
    Fl: Flags          // {N: bool; C: bool; Z: bool; V: bool}
    Regs: Map<RName, uint32>  // R0–R15 (R13=SP, R14=LR, R15=PC)
    MM: DataMemory      // Map<WAddr, MemLoc<'INS>> — word-addressed memory
}
```

- **Registers**: `RName` is a discriminated union (R0, R1, ..., R15) with named aliases.
- **Memory**: word-addressed via `WAddr = WA of uint32` wrapper type.
- **Flags**: N (negative), Z (zero), C (carry), V (overflow).

Every instruction takes a `DataPath` and returns a new `DataPath` — state is immutable.

> **Pipelining note:** the `DataPath` comment says "PC can be found as R15 - 8", but in practice the PC displayed to the user is the instruction address itself. The +8 pipelining offset is applied transiently during execution (see [Pipelining Model](#pipelining-model) below).

### CommonLex.fs — Parsing Foundation

Defines how opcode strings are parsed:
- `Condition`: 16 ARM conditions (EQ, NE, MI, PL, HI, HS, LO, LS, GE, GT, LE, LT, VS, VC, NV, AL)
- `InstrClass`: DP | MEM | MISC | BRANCH | PSEUDO
- `OpSpec`: specifies valid opcodes by combining root mnemonics × suffixes × conditions
- `opCodeExpand`: generates the full set of valid opcode strings from a spec

### Instruction Modules

Each instruction family has its own module with a standard interface:

| Module | Instructions | Key Types |
|--------|-------------|-----------|
| **DP.fs** | MOV, MVN, ADD, SUB, ADC, SBC, RSB, RSC, AND, ORR, EOR, BIC, LSL, LSR, ASR, ROR, RRX, CMP, CMN, TST, TEQ | `Op2` (flexible operand 2), `Instr = (DataPath → Result<DataPath * UFlags, ExecuteError>) * Op2` — a closure paired with operand info |
| **Memory.fs** | LDR, STR, LDRB, STRB, LDM, STM | `InstrMemSingle`, `InstrMemMult`, addressing modes |
| **Branch.fs** | B, BL, END | `Instr` = B addr \| BL addr \| END |
| **Misc.fs** | DCD, DCB, FILL, EQU, ADR | Data directives and pseudo-instructions |

Each module exports:
- A `parse` function: `LineData → Parse<Instr> option` — returns `None` if the opcode doesn't belong to this module, `Some parse` if it does (where `parse.PInstr` is `Ok instr` on success or `Error parseError` on failure)
- An `execute` function (e.g., `executeDP`, `executeMem`, `executeBranch`): takes the module's `Instr` and a `DataPath`, returns `Result<DataPath, ExecuteError>`
- An `IMatch` active pattern: wraps `parse` so it can be used in pattern matching (see `ParseTop.IMatch`)

### ParseTop.fs — The Parser Dispatcher

Unifies all instruction types into a single `Instr` discriminated union:

```fsharp
type Instr = IMEM of Memory.Instr | IDP of DP.Instr | IMISC of Misc.Instr
           | IBRANCH of Branch.Instr | EMPTY
```

`IMatch` tries each module's `IMatch` active pattern in sequence (Memory → DP → Misc → Branch). The first module to return `Some` wins; if all return `None`, the opcode is unrecognised. Each result is wrapped in the appropriate constructor (`IMEM`, `IDP`, etc.).

`parseLine` is the main entry point — it splits a source line into label/opcode/operands, constructs a `LineData` record, and calls `IMatch`.

### ExecutionTop.fs — Program Loading & Execution

**Loading** (`reLoadProgram`):
1. Parse each line via `parseLine`
2. Build symbol table (labels → addresses)
3. Multi-pass: re-parse until all forward references resolve (iterates to fixed point)
4. Produce a `LoadImage` with code memory, data memory, errors, and symbol info

Code memory is stored as a `Map<WAddr, CondInstr * int>` where `CondInstr` is `{Cond; InsExec; InsOpCode}` and the `int` is the source line number. Data memory is `Map<WAddr, Data>` where `Data = Dat of uint32 | CodeSpace`.

**Execution** (`asmStep`):
1. Read PC from `DataPath.Regs[R15]`
2. Fetch instruction from code memory at PC
3. Evaluate condition code against current flags via `condExecute`
4. If condition true: dispatch to the appropriate module's execute function
5. If condition false: pass through unchanged (no flags update)
6. Update PC (see Pipelining Model below)
7. Record history snapshot every 500 steps for back-stepping

### Pipelining Model

This is the most subtle part of the codebase. ARM's 3-stage pipeline means that when an instruction executes, `PC` reads as the instruction address + 8. The simulator implements this with a bracket trick in `dataPathStep`:

```
Before execution:  addToPc +8       → PC reads as addr+8 (ARM pipelining)
Instruction runs:  uses PC value     → sees correct addr+8
After execution:   addToPc (4-8)     → net effect: addr+4 (advance to next)
```

The key functions:
- `dataPathStep` does `addToPc 8 dp` before dispatch, then `addToPc (4-8) dp` after
- Net result for sequential execution: PC advances by +4
- **`setReg R15 addr`** in `Helpers.fs` adds `setPCOffset = 4` to compensate for the post-execution -4 adjustment — so `setReg R15 target` stores `target + 4`, which after `-4` adjustment yields `target`
- Branch instructions (`B`, `BL`) write their target via `setReg R15`, so the adjustment is automatic
- The `setRegRaw` variant does NOT add the offset — used internally when the caller handles addressing manually

---

## Renderer / GUI (src/Renderer/)

### Refs.fs — Global State Hub

Contains all mutable state for the GUI:
- `runMode`: current execution state (ResetMode, ActiveMode, FinishedMode, ParseErrorMode, RunErrorMode)
- `currentRep`: display format (Hex, Bin, Dec, UDec)
- `currentView`: dashboard tab (Registers, Memory, Symbols)
- `regMap`, `memoryMap`, `symbolMap`: cached display state
- DOM element accessors for every button, register display, flag display, etc.
- `VSettings`: user preferences (font size, theme, word wrap, max steps)

### Integration.fs — The Glue Layer

This is the most important Renderer file. It connects everything:
- `tryParseAndIndentCode`: parse the active editor tab → highlight errors or indent code
- `runCode` / `stepCode` / `stepCodeBack`: execute instructions and update display
- `resetEmulator`: clear state, return to initial mode
- `highlightCurrentAndNextIns`: show execution position in editor
- `showInfoFromCurrentMode`: extract state and refresh all displays

### Views.fs — Display Rendering

Renders the right-hand dashboard:
- **Registers**: R0–R15 with values in current representation, changed-value highlighting
- **Memory**: contiguous address blocks as HTML tables, with byte view option
- **Symbols**: grouped by type (Code/Data/EQU), sorted by address
- **Flags**: N, C, Z, V with color when changed from previous step

### Editors.fs — Monaco Integration

Configures Monaco code editor instances:
- Syntax highlighting via `monaco-init.js` (ARM opcodes, registers, comments)
- Line decorations: error highlights, breakpoint glyphs, execution arrows
- Inline tooltips showing memory addresses and shift amounts during stepping
- Theme support (vs, vs-dark, hc-black)

### Other Renderer Files

| File | Purpose |
|------|---------|
| **Tabs.fs** | Multi-file tab management (create, switch, close, unsaved tracking) |
| **Files.fs** | File open/save via Electron native dialogs |
| **MenuBar.fs** | Application menu bar (File, Edit, View, Test menus) |
| **Settings.fs** | Preferences dialog (font, theme, max steps) |
| **Testbench.fs** | UI for running testbench files and displaying results |
| **Tooltips.fs** | Execution-time inline info (Tippy.js popups) |
| **ErrorDocs.fs** | Hover help text for parse errors |
| **Stats.fs** | Code statistics display |
| **Renderer.fs** | Entry point — `init()` attaches all event handlers, creates first tab |

---

## Electron Main Process (src/Main/)

`Main.fs` is mostly boilerplate:
- Creates the BrowserWindow (1200×800)
- Loads `app/index.html`
- Handles close interlock (renderer can cancel window close for unsaved files)
- Forwards resize events to renderer
- Enforces single-instance (second launch focuses existing window)
- In dev mode (`-w` flag): watches files and auto-reloads on change

You rarely need to modify this file.

---

## Frontend Assets (app/)

### index.html

The main HTML structure:

```
<header>  — Toolbar
  ├── File buttons: Open, Save
  ├── Execution buttons: Run, Reset, Step Forward, Step Back
  ├── Status bar (color changes: green=ok, red=error, blue=stepping)
  └── Representation toggle: Hex | Bin | Dec | UDec

<body>
  ├── Left pane: Editor
  │   ├── Tab bar (file tabs, + new tab)
  │   └── Monaco editor container
  │
  └── Right pane: Dashboard
      ├── View tabs: Registers | Memory | Symbols
      ├── Register display (R0–R15 + flags N,C,Z,V)
      ├── Memory display (address → value tables)
      └── Symbol table (name → address, grouped by type)
```

### monaco-init.js

Sets up Monaco editor before F# code runs:
- Defines ARM assembly syntax highlighting (opcodes, registers, comments, directives)
- Configures custom themes
- Creates the global `editor` and `monaco` objects that F# code references

### CSS

- `photon.css`: base UI framework (Photon)
- `vistally.css`: custom VisUAL2 styles
- `vex*.css`: modal dialog themes
- `material-icons.css`: icon font

---

## Data Flow: Source Code → Execution → Display

### 1. Parsing

```
User types ARM assembly in Monaco editor
    │
    ▼
Integration.tryParseAndIndentCode(tabId)
    │
    ▼
ExecutionTop.reLoadProgram(lines)
    │  Multi-pass loop:
    │  1. parseLine → ParseTop.IMatch → tries Memory, DP, Misc, Branch parsers
    │  2. Build/update symbol table (label → address)
    │  3. Repeat until all forward references resolve
    │
    ▼
LoadImage {
    Code: Map<WAddr, CondInstr>    // Executable instruction memory
    Mem: Map<WAddr, Data>          // Data memory (DCD, DCB, FILL)
    Errors: ParseError list        // Any parse failures
    SymInf: SymbolInfo             // Symbol table + type info
}
    │
    ├── If errors: highlight in editor with hover messages
    └── If ok: auto-indent code, create RunInfo, ready to execute
```

### 2. Execution

```
User clicks Run / Step Forward
    │
    ▼
Integration.runCode() or stepCode()
    │
    ▼
ExecutionTop.asmStep(numSteps, runInfo)
    │
    │  For each step (while state == PSRunning):
    │    1. PC = DataPath.Regs[R15]
    │    2. Fetch: instruction = CodeMemory[WA(PC)]
    │    3. Condition check: condExecute(cond, dp.Fl)
    │    4. If true, dispatch by instruction type:
    │       ├── IDP    → DP.executeDP (arithmetic, logic, shifts)
    │       ├── IMEM   → Memory.executeMem (loads, stores)
    │       ├── IBRANCH → Branch.executeBranch (B, BL, END)
    │       └── IMISC  → Misc.executeADR (pseudo-ops)
    │    5. Update PC: +4 (sequential) or branch target
    │    6. Record in history (every 500 steps, for back-stepping)
    │
    ▼
Updated RunInfo { dpCurrent, StepsDone, State, History, ... }
```

### 3. Display

```
Integration.showInfoFromCurrentMode()
    │
    ├── Views.updateRegisters()
    │     └── For each R0–R15: format value, highlight if changed
    │
    ├── Views.updateMemory()
    │     └── Group addresses into contiguous blocks → HTML tables
    │
    ├── Views.updateSymTable()
    │     └── Group by type (Code/Data/EQU) → sorted HTML tables
    │
    ├── Tabs.setFlags(N, C, Z, V)
    │     └── Color flags that changed since last step
    │
    └── Editors.highlightNextInstruction(line)
          └── Arrow glyph + line highlight in Monaco
```

---

## Key Abstractions

### DataPath (CPU State)

The immutable ARM state. Every instruction is a pure function: `DataPath → Result<DataPath, Error>`.

### Flexible Operand 2 (Op2)

ARM's signature feature — the second operand to data processing instructions can be:
- **Immediate** (`NumberLiteral`): 8-bit value rotated right by an even amount (0–30). The full set of valid immediates is pre-computed at startup in `makeOkLitMap()`. The parser also supports a `NegatedLit` mode (for SUB↔ADD, CMP↔CMN) and `InvertedLit` mode (for AND↔BIC, MOV↔MVN) — if the literal is invalid for the given opcode but its negation/inversion is valid, the opcode is transparently swapped.
- **Register with immediate shift** (`RegisterWithShift`): Rn optionally shifted by an immediate (LSL #0–#31, LSR #1–#32, ASR #1–#32, ROR #1–#31)
- **Register shifted by register** (`RegisterWithRegisterShift`): Rn shifted by Rm\[4:0\]
- **RRX** (`RegisterWithRRX`): rotate right through carry (1-bit rotation including carry flag)

Evaluated by `DP.evalOp2: Op2 → DataPath → (uint32 * UCarry)`. The carry output is used by `S`-suffixed logical instructions to update the C flag.

### UFlags — Flag Update Tracking

Instructions return `UFlags = {F: Flags; CU: bool; VU: bool; NZU: bool; RegU: RName list}` alongside the updated `DataPath`. The `*U` fields indicate *which* flag groups were actually updated — used by the GUI to highlight changed flags. Comparison instructions (CMP, CMN, TST, TEQ) always update flags; other DP instructions only update when the `S` suffix is present.

### Addressing Modes (Memory)

- **NoIndex**: `[Rn, #offset]` — compute address, don't modify Rn
- **PreIndex**: `[Rn, #offset]!` — compute address AND update Rn before access
- **PostIndex**: `[Rn], #offset` — access at Rn, THEN update Rn

### Instruction Dispatch

```fsharp
// ParseTop.Instr wraps all instruction types:
type Instr = IMEM of Memory.Instr | IDP of DP.Instr
           | IMISC of Misc.Instr | IBRANCH of Branch.Instr | EMPTY

// ExecutionTop.dataPathStep dispatches:
match instr with
| IDP instr'    → DP.executeDP instr' dp
| IMEM instr'   → Memory.executeMem instr' dp
| IBRANCH instr' → Branch.executeBranch instr' dp
| IMISC(ADR ai) → executeADR ai dp
```

### Symbol Resolution

Multi-pass loading handles forward references:
1. Pass 1: parse all lines — some symbols undefined, expressions return `Error ("Undefined symbol" [names])`
2. Pass 2+: re-parse lines that previously had unresolved symbols, using the expanded symbol table
3. Repeat until no new symbols resolve (fixed point)

Symbol types: `CodeSymbol` (instruction address), `DataSymbol` (DCD/DCB/FILL address), `CalculatedSymbol` (EQU value).

The expression evaluator (`Expressions.fs`) supports:
- Decimal literals: `123`
- Hex literals: `0xFF`
- Binary literals: `0b1010`
- Label references: `myLabel`
- Arithmetic: `+`, `-`, `*` between sub-expressions
- Unary minus: `-expr`
- Parentheses: `(expr)`

The active pattern `RESOLVEALL` (in `CommonLex.fs`) is used by parse functions to resolve a list of operand expressions against the symbol table. If any operand is unresolved, the pattern match fails gracefully, deferring to the next resolution pass.

### Memory Model

Memory is word-addressed via `WAddr = WA of uint32`. The map stores either data (`Dat of uint32`) or code-space markers (`CodeSpace`). Code and data occupy separate address ranges: code starts at `0x0`, data starts at configurable `minDataStart` (default `0x200`). The `LoadPos` record tracks current insertion positions during program loading.

LDR/STR instructions work with byte addresses but the underlying memory is word-aligned. Byte loads (`LDRB`) extract the appropriate byte from a 32-bit word; byte stores (`STRB`) modify only the relevant byte.

---

## How to Add a New Instruction

Example: adding a hypothetical `CLZ` (Count Leading Zeros) instruction.

1. **Choose the module**: `DP.fs` for data processing, `Memory.fs` for loads/stores, etc.

2. **Add the opcode spec** to the module's spec list:
   ```fsharp
   { InstrC = DP; Roots = ["CLZ"]; Suffixes = [""] }
   ```

3. **Write the parse function**: match the opcode, parse operands, return a `Parse<Instr>`:
   ```fsharp
   let parseCLZ (ls: LineData) : Result<Parse<Instr>, string> =
       // Parse: CLZ Rd, Rm
       ...
   ```

4. **Write the execute function**: take `DataPath`, return `Result<DataPath * UFlags, ExecuteError>`:
   ```fsharp
   let executeCLZ rd rm (dp: DataPath) =
       let value = getReg rm dp
       let count = countLeadingZeros value
       Ok (setReg rd count dp, defaultFlags)
   ```

5. **Register in ParseTop.fs**: add your parser to the `IMatch` dispatcher chain.

6. **Test**: write a testbench (see below) or manually test in the GUI.

---

## Testing & Testbenches

### Testbench Format

A testbench file **must** start with `##TESTBENCH` on the first line. It then contains one or more `#TEST` sections (and optionally `#BLOCK` sections for reusable code):

```
##TESTBENCH

#TEST 1 AdditionTest
  IN R0 IS 5
  IN R1 IS 10
  OUT R0 IS 15

#TEST 2 PointerTest
  IN R0 PTR 100, 200, 300
  OUT R0 PTR 100, 200, 300
  STACKPROTECT
```

The file is detected as a testbench by checking whether the first non-blank line starts with `##TESTBENCH` (see `Testbench.fs/getTBWithTab`).

### Testbench Directives

**Register I/O** (prefixed with `IN` or `OUT`):

| Directive | Description |
|-----------|-------------|
| `IN Rn IS value` | Set register Rn to value before execution |
| `OUT Rn IS value` | Assert Rn equals value after execution |
| `IN Rn PTR v1, v2, ...` | Auto-allocate memory, store values there, set Rn to the address |
| `OUT Rn PTR v1, v2, ...` | Assert memory contents at address in Rn match the values |

**Standalone directives** (no `IN`/`OUT` prefix):

| Directive | Description |
|-----------|-------------|
| `RANDOMISEINITVALS` | Randomise R0–R12 to pseudo-random values before execution |
| `STACKPROTECT` | Detect stack corruption (checks memory below initial SP) |
| `DATAAREA addr` | Set the starting address for auto-allocated pointer data |
| `PERSISTENTREGS R0, R1, ...` | Initialise listed registers to random values and check they are preserved (APCS compliance) |
| `BRANCHTOSUB name` | Start execution at the named subroutine label instead of the first line |
| `RELABEL old new` | Rename a symbol — useful for testing subroutines with non-standard names |
| `APPENDCODE n` | Append code from `#BLOCK n` to the program under test |

**Code blocks** (`#BLOCK`) define reusable code snippets that can be appended to the assembly program under test via `APPENDCODE`:

```
##TESTBENCH

#BLOCK 1 CallerCode
  MOV R0, #5
  BL mySubroutine
  END

#TEST 1 SubroutineTest
  APPENDCODE 1
  BRANCHTOSUB mySubroutine
  OUT R0 IS 10
```

**Result lines**: after a test runs, the system writes result lines prefixed with `>>` into the testbench file (e.g., `>>; PASSED` or `>>- FAILED: ...`). These are ignored on re-parse.

### Running Tests

1. Open your assembly file in one tab
2. Open the testbench file in another tab (must start with `##TESTBENCH`)
3. Select the **assembly** tab (not the testbench tab)
4. Use **Test → Run all tests** from the menu bar
5. Results appear as `>>; PASSED` or `>>- FAILED: ...` lines appended to each test in the testbench

> **Note:** Only one testbench can be loaded at a time. If multiple tabs contain `##TESTBENCH` files, the runner will report an error.

### Automated Test Data

The `app/test-data/` folder contains randomised test vectors generated by [VisualRandomTestGen](https://github.com/ImperialCollegeLondon/VisualRandomTestGen). Results are in `app/test-results/`.

---

## Common Tasks

### Changing the GUI Layout

Edit `app/index.html` for structure, `app/css/vistally.css` for styling. Element IDs referenced in `Refs.fs` must match.

### Modifying Editor Behaviour

Monaco editor config is in `Editors.fs` (F# side) and `app/js/monaco-init.js` (JS side for syntax highlighting).

### Adding a New Display View

1. Add HTML structure in `index.html`
2. Add DOM references in `Refs.fs`
3. Add rendering logic in `Views.fs`
4. Wire up tab switching in `Renderer.fs`

### Debugging

`debugLevel` is set automatically from command-line flags (in `Renderer.fs/setDebugLevel()`):

| Launch Command | debugLevel | Effect |
|----------------|-----------|--------|
| `yarn launch` (`electron . -w`) | 1 | Extra menu items visible (Toggle Dev Tools, Run Emulator Tests) |
| `yarn launch-debug` (`electron . -w --debug`) | 2 | Same as above + DevTools opens automatically on startup |
| Production (packaged .exe) | 0 | Dev menu items hidden |

- Use browser console (DevTools → Console) for JavaScript-level debugging
- The F# source maps in `renderer.js.map` allow stepping through original F# in DevTools
- Helpful `printfn` calls guarded by `if debugLevel > 0` appear throughout the codebase for tracing

### Packaging for Distribution

```bash
yarn pack-win     # Windows .exe
yarn pack-osx     # macOS .app + .dmg
yarn pack-linux   # Linux binary
yarn pack-all     # All platforms
```

Output goes to `dist/`.
