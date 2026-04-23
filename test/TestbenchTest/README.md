# TestbenchTest — Headless Testbench Reproduction

A small standalone .NET console app that exercises the VisUAL2-SU emulator's
testbench engine **outside the Electron GUI**. It is useful for:

- Reproducing student-reported testbench issues without the Electron renderer
- Confirming that the emulator engine reports test PASS/FAIL correctly when the
  GUI shows unexpected behaviour
- Sanity-checking changes to `src/Emulator/Testlib.fs` and
  `src/Emulator/ExecutionTop.fs`

## What it does

`Program.fs` embeds the tutorial testbench (the same one shown in the
`headless-marking-guide.md` walkthrough) and a short snippet of student code
(`codeSrc`). It runs each `#TEST` against that code via two paths:

1. `parseCodeAndRunTest` — the high-level helper used by the headless flow
2. `reLoadProgram` + `initTestDP` + `getRunInfoFromImageWithInits` + `asmStep`
   — the same sequence the Electron renderer uses internally

Both paths print the resulting state, register values, and per-test
PASS/FAIL lines. If the engine and the GUI disagree, the bug is in the GUI
plumbing (e.g. tab selection, `##TESTBENCH` header detection, dialog
dismissal), not in the emulator core.

## Running

This project targets **net10.0** with `<RollForward>LatestMajor</RollForward>`
so it runs against any modern installed .NET SDK (it is *not* tied to the
.NET Core 2.1 SDK that the main app's Fable build requires).

```bash
cd test/TestbenchTest
dotnet run
```

Edit `codeSrc` in `Program.fs` to try different student programs against the
embedded testbench.

## Notes

- The project references `src/Emulator/Emulator.fsproj` directly. Changes to
  the emulator are picked up on the next `dotnet run`.
- This harness does **not** exercise the renderer (`src/Renderer/`) — that
  layer still has to be tested in the packaged Electron app.
