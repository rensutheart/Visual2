# VisUAL2-SU — Stellenbosch University Edition

An ARM assembly language simulator for learning and teaching ARM (ARMv4) programming.

This is the **Stellenbosch University (SU) edition** of [VisUAL2](https://github.com/ImperialCollegeLondon/Visual2), originally developed by Imperial College London. It is based on VisUAL2 v1.06.10 with an expanded instruction set, ARM spec compliance fixes, and additional features.

## Quick Start

### Download (Recommended)

Go to the [**Releases**](https://github.com/rensutheart/Visual2/releases) page and download the zip for your operating system:

| OS | File | Notes |
|----|------|-------|
| Windows | `VisUAL2-SU-win32-x64.zip` | Windows 7+ (64-bit) |
| macOS | `VisUAL2-SU-macOS-x64.zip` | macOS 10.10+ (Intel & Apple Silicon via Rosetta) |
| Linux | `VisUAL2-SU-linux-x64.zip` | 64-bit, most distros |

#### Windows

1. Download `VisUAL2-SU-win32-x64.zip` from [Releases](https://github.com/rensutheart/Visual2/releases)
2. Extract the zip to any folder
3. Double-click `VisUAL2-SU.exe`

#### macOS

1. Download `VisUAL2-SU-macOS-x64.zip` from [Releases](https://github.com/rensutheart/Visual2/releases)
2. Extract:
   ```bash
   unzip VisUAL2-SU-macOS-x64.zip
   cd VisUAL2-SU-darwin-x64
   open VisUAL2-SU.app
   ```

> **Note:** macOS may show a security warning for unsigned apps. Go to **System Preferences → Security & Privacy → General** and click **Open Anyway**.

#### Linux

1. Download `VisUAL2-SU-linux-x64.zip` from [Releases](https://github.com/rensutheart/Visual2/releases)
2. Extract:
   ```bash
   unzip VisUAL2-SU-linux-x64.zip
   cd VisUAL2-SU-linux-x64
   chmod +x VisUAL2-SU
   ./VisUAL2-SU
   ```

---

## What Is VisUAL2-SU?

VisUAL2-SU is a simulator for 32-bit ARM assembly language. It lets you:

- **Write** ARM assembly in a syntax-highlighted editor with autocomplete
- **Step through** instructions one at a time, or run to completion
- **See registers** update in real time (R0–R15, CPSR flags N, Z, C, V)
- **Inspect memory** contents as your program executes
- **Get error messages** with clear descriptions when something is wrong

It does **not** run on real ARM hardware — it simulates the ARM instruction set in software, so it is safe to experiment freely.

---

## Supported Instructions

VisUAL2-SU supports a large subset of the ARM instruction set. For full details with syntax, constraints, and examples, see the [**Supported ARM Instructions**](docs/arm-instructions.md) reference.

### Summary

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

**70 base mnemonics**, expanding to hundreds of valid opcodes with condition codes (EQ, NE, CS, CC, MI, PL, VS, VC, HI, LS, GE, LT, GT, LE, AL) and the S suffix.

### Key Features

- **Condition codes** — All instructions (except `END` and data directives) support conditional execution
- **Flexible operand 2** — Immediate constants, register shifts, and register-controlled shifts
- **Multiple addressing modes** — Pre-indexed, post-indexed, with writeback
- **Stack operations** — PUSH/POP with full register lists
- **Expressions** — Labels, `&`-prefix hex, `_` digit separators, simple arithmetic in operands

### What Is NOT Supported

| Category | Missing |
|----------|---------|
| Swap | `SWP`, `SWPB` |
| Software interrupt | `SWI` / `SVC` |
| Coprocessor | `MCR`, `MRC`, `LDC`, `STC` |
| Thumb mode | All Thumb/Thumb-2 |
| Privileged mode | `MSR`, `MRS`, mode switching |
| Division | `SDIV`, `UDIV` |
| CPSR/SPSR access | No direct flag register read/write |

See [docs/arm-instructions.md](docs/arm-instructions.md) for full details.

---

## Writing Your First Program

1. Launch VisUAL2-SU
2. Type the following in the editor:

```arm
        MOV   R0, #5        ; load 5 into R0
        MOV   R1, #3        ; load 3 into R1
        ADD   R2, R0, R1    ; R2 = R0 + R1 = 8
        SUB   R3, R0, R1    ; R3 = R0 - R1 = 2
        END
```

3. Click **Step** to execute one instruction at a time, or **Run** to execute all
4. Watch the register panel update as each instruction executes

### Tips

- `END` is optional (programs will stop after the last instruction)
- Labels go at the start of a line (no indentation), instructions are indented
- Comments start with `;` — GNU syntax `//` and `/* */` is also supported
- Immediate values need a `#` prefix: `#42`, `#0xFF`, `#&1A`
- Use `Ctrl+Shift+I` to open the developer console for debug output

---

## Memory-Mapped Display

VisUAL2-SU includes a built-in memory-mapped pixel display for graphics and animation exercises. Students write ARM assembly that stores colour values to memory starting at address `0x2000`, and the Display tab renders those bytes as a grid of coloured pixels using the VGA 256-colour palette.

- **Grid sizes:** 16×16, 32×32, or 64×64 pixels
- **Animation:** Write `MOV R10, #1` to trigger a display refresh and pause execution
- **Controls:** Continue, Clear, Auto-animate with configurable FPS

See [docs/display-mode.md](docs/display-mode.md) for full details, examples, and the animation workflow.

---

## Building from Source (For Developers)

If you want to modify VisUAL2-SU or build it yourself, see the [original project wiki](https://github.com/ImperialCollegeLondon/Visual2/wiki) for background.

### Prerequisites

- **Node.js 16** (later versions are incompatible with webpack 3 / Electron 2) — install via [nvm](https://github.com/nvm-sh/nvm)
- [Yarn](https://yarnpkg.com/) (v1 / Classic)
- [.NET Core SDK 2.1](https://dotnet.microsoft.com/download/dotnet/2.1) — required by the Fable 2.x F#-to-JS compiler
- On macOS/Linux: [Mono](http://www.mono-project.com/download/stable/) (for Paket package manager)

### macOS (Apple Silicon / ARM64)

.NET Core 2.1 and Electron 2.0 do **not** have native ARM64 builds, so on Apple Silicon Macs (M1/M2/M3/M4) everything must run under **Rosetta 2** (x86_64 emulation).

#### 1. Install Node.js 16 via nvm

```bash
# Install nvm if not already installed (see https://github.com/nvm-sh/nvm)
nvm install 16
nvm use 16
```

#### 2. Install .NET Core 2.1 SDK (x64, via Rosetta)

The official .NET install script can fetch the x64 SDK:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh
arch -x86_64 /tmp/dotnet-install.sh --channel 2.1 --install-dir $HOME/.dotnet-x64
```

This installs the x64 .NET Core 2.1 SDK to `~/.dotnet-x64/`. Verify with:

```bash
arch -x86_64 $HOME/.dotnet-x64/dotnet --version   # should print 2.1.818 or similar
```

#### 3. Install dependencies

```bash
yarn install
```

> The `fsevents` optional dependency may show build errors on ARM64 — this is harmless and can be ignored.

#### 4. Install Electron x64 binary

Electron 2.0.8 has no ARM64 macOS build. Force the x64 download:

```bash
npm_config_arch=x64 electron_config_arch=x64 node node_modules/electron/install.js
```

#### 5. Restore .NET projects

```bash
arch -x86_64 $HOME/.dotnet-x64/dotnet restore src/Main/Main.fsproj
arch -x86_64 $HOME/.dotnet-x64/dotnet restore src/Renderer/Renderer.fsproj
arch -x86_64 $HOME/.dotnet-x64/dotnet restore src/Emulator/Emulator.fsproj
```

#### 6. Build

From the project root:

```bash
cd src/Main
arch -x86_64 $HOME/.dotnet-x64/dotnet fable webpack --port free -- --config webpack.config.js
```

This compiles all F# source to JavaScript via Fable and bundles with webpack. Outputs:
- `main.js` (Electron main process) in the project root
- `app/js/renderer.js` (Electron renderer) in `app/js/`

#### 7. Run

```bash
# From the project root:
npx electron . -w
```

### macOS (Intel), Windows, Linux

These platforms can use .NET Core 2.1 SDK natively.

```bash
# Clone
git clone https://github.com/rensutheart/Visual2.git
cd Visual2

# Install dependencies
yarn install

# Development mode (watch + hot reload)
yarn start          # terminal 1: compile F# → JS
yarn launch         # terminal 2: run the app

# Production build + package
yarn build
yarn pack-win       # Windows binary → dist/
yarn pack-linux     # Linux binary → dist/
yarn pack-osx       # macOS binary → dist/ (macOS host only for DMG)
```

---

## Credits

This is the **Stellenbosch University (SU) edition** of [VisUAL2](https://github.com/ImperialCollegeLondon/Visual2), originally developed at Imperial College London. Based on VisUAL2 v1.06.10.

**SU edition by:** Rensu Theart, Stellenbosch University — expanded instruction set (PUSH/POP, BX/BLX, half-word, multiply, saturating arithmetic, LDRD/STRD), ARM spec compliance fixes, memory-mapped pixel display, and documentation.

---

## Changelog (SU Edition)

All changes relative to the original [VisUAL2 v1.06.10](https://github.com/ImperialCollegeLondon/Visual2) from Imperial College London.

### v2.2.2-SU

**DCB improvements**
- DCB now accepts any number of byte operands (no longer requires a multiple of 4); values are automatically padded with zeros to the next word boundary
- DCB now supports quoted string literals, e.g. `DCB "Hello",0` — each character is expanded to its byte value
- Updated hover/tooltip documentation for DCB

**Maintenance**
- Disabled the "new release of Visual2" version-check popup (referenced unreachable Imperial College intranet)

### v2.2.1-SU

**UI Polish**
- Register aliases shown: R13/SP, R14/LR, R15/PC
- CPSR label added next to status flags
- Run and Reset buttons now include icons (▶ and ↺)
- Wider register label column for consistent appearance
- Brighter LR return line highlight (more visible purple)
- Display tab tooltip added

### v2.2.0-SU

**Breakpoints**
- Toggle breakpoints by clicking the glyph margin (red dot indicator)
- Breakpoint validation: cannot set on comment-only or label-only lines
- Execute-then-stop semantics with red "Breakpoint Reached" status
- Breakpoint line highlighted with red/pink background

**Execution visualization**
- Current instruction line coloured by condition: amber (unconditional), green (condition met), red (not met)
- Status flags: only the flags relevant to the condition are highlighted (e.g., BNE highlights only Z)
- Relevant flags coloured green (condition met) or red (not met)
- Changed registers highlighted in yellow after each step
- Changed flags highlighted in yellow for non-conditional instructions
- Next-instruction arrow only shown when a branch occurs (not on sequential execution)
- END instruction now properly highlighted

**Branch tooltip & LR return line**
- "Branch" info button appears on branch instructions (like existing Pointer/Stack/Shift buttons)
- Shows branch condition, source address, destination address, and destination line number
- When LR holds a valid code address, the return line is highlighted in purple

**Editor improvements**
- Full-width line highlights extending across margin and code area
- Cursor no longer jumps when clicking the glyph margin to toggle breakpoints
- Reduced stepping-mode overlay brightness for better contrast

### v2.1.0-SU

**Memory-mapped pixel display**
- Built-in pixel display for graphics and animation exercises
- Grid sizes: 16×16, 32×32, or 64×64 pixels using VGA 256-colour palette
- Animation via `MOV R10, #1` breakpoint trigger
- Display tab with continue, clear, and auto-animate controls

### v2.0.1-SU

**GNU ARM comment support**
- `//` line comments and `/* */` block comments alongside existing `;` comments

**VisUAL Classic colour theme**
- Default dark theme matching the original VisUAL look
- Theme selection and defaults

### v2.0.0-SU

**Expanded instruction set** (added to the original VisUAL2)
- `PUSH` / `POP` — stack operations
- `BX` / `BLX` — branch and exchange
- `LDRH` / `LDRSH` / `STRH` / `LDRSB` — half-word and signed byte memory access
- `MUL` / `MLA` / `UMULL` / `UMLAL` / `SMULL` / `SMLAL` — multiply instructions
- `QADD` / `QSUB` — saturating arithmetic
- `LDRD` / `STRD` — double-word memory access

**ARM spec compliance fixes**
- ADR condition codes and byte offset range (±4095)
- R15 blocked in register-controlled shift positions
- Various edge-case fixes

**Rebranding**
- Renamed to VisUAL2-SU (Stellenbosch University Edition)
- Help menu links to SU repository and instructions

**Documentation**
- Comprehensive supported ARM instructions reference
- Student-facing README

---

**Original acknowledgements:** Salman Arif (VisUAL), HLP 2018 class (F# reimplementation), Thomas Carrotti, Lorenzo Silvestri, and HLP Team 10. See the original [acknowledgements](https://github.com/ImperialCollegeLondon/Visual2/wiki/Acknowledgements).

Built with [F#](https://fsharp.org/), [Fable](https://fable.io/), [Electron](https://electronjs.org/), and [Monaco Editor](https://microsoft.github.io/monaco-editor/).
