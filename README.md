# VisUAL2-SU — Stellenbosch University Edition

An ARM assembly language simulator for learning and teaching ARM (ARMv4) programming.

This is the **Stellenbosch University (SU) edition** of [VisUAL2](https://github.com/ImperialCollegeLondon/Visual2), originally developed by Imperial College London. It is based on VisUAL2 v1.06.10 with an expanded instruction set, ARM spec compliance fixes, and additional features.

## Quick Start

### Download (Recommended)

Go to the [**Releases**](https://github.com/rensutheart/Visual2/releases) page and download the zip for your operating system:

| OS | File | Notes |
|----|------|-------|
| Windows | `VisUAL2-SU-win32-x64.zip` | Windows 7+ (64-bit) |
| Linux | `VisUAL2-SU-linux-x64.zip` | 64-bit, most distros |
| macOS | `visual2-su-osx.dmg` | macOS 10.10+ (Intel) |

#### Windows

1. Download `VisUAL2-SU-win32-x64.zip` from [Releases](https://github.com/rensutheart/Visual2/releases)
2. Extract the zip to any folder
3. Double-click `VisUAL2-SU.exe`

#### Linux

1. Download `VisUAL2-SU-linux-x64.zip` from [Releases](https://github.com/rensutheart/Visual2/releases)
2. Extract:
   ```bash
   unzip VisUAL2-SU-linux-x64.zip
   cd VisUAL2-SU-linux-x64
   chmod +x VisUAL2-SU
   ./VisUAL2-SU
   ```

#### macOS

1. Download `visual2-su-osx.dmg` from [Releases](https://github.com/rensutheart/Visual2/releases) (when available), or the zip
2. If using the DMG: open it and drag VisUAL2-SU to Applications
3. If using the zip: extract and run:
   ```bash
   open -a VisUAL2-SU.app
   ```

> **Note:** macOS may show a security warning for unsigned apps. Go to **System Preferences → Security & Privacy → General** and click **Open Anyway**.

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

**68 base mnemonics**, expanding to hundreds of valid opcodes with condition codes (EQ, NE, CS, CC, MI, PL, VS, VC, HI, LS, GE, LT, GT, LE, AL) and the S suffix.

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

- Every program must end with `END`
- Labels go at the start of a line (no indentation), instructions are indented
- Comments start with `;`
- Immediate values need a `#` prefix: `#42`, `#0xFF`, `#&1A`
- Use `Ctrl+Shift+I` to open the developer console for debug output

---

## Building from Source (For Developers)

If you want to modify VisUAL2-SU or build it yourself, see the [original project wiki](https://github.com/ImperialCollegeLondon/Visual2/wiki) for background. The build requires:

- [Node.js](https://nodejs.org/) and [Yarn](https://yarnpkg.com/)
- [.NET Core SDK 2.1](https://dotnet.microsoft.com/download/dotnet/2.1)
- On macOS/Linux: [Mono](http://www.mono-project.com/download/stable/)

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

**SU edition by:** Rensu Theart, Stellenbosch University — expanded instruction set (PUSH/POP, BX/BLX, half-word, multiply, saturating arithmetic, LDRD/STRD), ARM spec compliance fixes, and documentation.

**Original acknowledgements:** Salman Arif (VisUAL), HLP 2018 class (F# reimplementation), Thomas Carrotti, Lorenzo Silvestri, and HLP Team 10. See the original [acknowledgements](https://github.com/ImperialCollegeLondon/Visual2/wiki/Acknowledgements).

Built with [F#](https://fsharp.org/), [Fable](https://fable.io/), [Electron](https://electronjs.org/), and [Monaco Editor](https://microsoft.github.io/monaco-editor/).
