module TestbenchTest

open CommonData
open ExecutionTop
open TestLib
open Errors

let testbenchSrc = [
    "##TESTBENCH"
    ""
    "#TEST 1"
    "IN R0 is 1"
    "IN R1 is 2"
    "OUT R2 is 3"
    "IN R3 ptr 1,2,3,7,11"
    "IN R4 ptr 6,10,88,100,900"
    ""
    "#TEST 2"
    "IN R0 is 10"
    "IN R1 is 200"
    "OUT R2 is 210"
    "IN R3 ptr 1,2,3,7,11"
    "IN R4 ptr 6,10,88,100,900"
]

let codeSrc = [
    "SUB R0, R1, R2"
]

[<EntryPoint>]
let main _ =
    printfn "=== Testbench Tutorial Example Reproduction ==="
    let initStack = 0xFF000000u
    let dStart = 0x80000000u
    let parsed = parseTests initStack dStart (testbenchSrc |> List.map (fun s -> s.ToUpper()))
    let tests =
        parsed
        |> List.choose (function | Ok t -> Some t | Error e -> printfn "Parse err %A" e; None)
    printfn "Parsed %d tests" tests.Length
    for t in tests do
        printfn "--- Running Test %d ---" t.TNum
        let res = parseCodeAndRunTest codeSrc t
        match res with
        | Error e -> printfn "Test %d ERROR: %s" t.TNum e
        | Ok (ri, _lim) ->
            printfn "Test %d State=%A StepsDone=%d" t.TNum ri.State ri.StepsDone
            let dp = fst ri.dpCurrent
            printfn "  R0=%d R1=%d R2=%d PC=0x%X LR=0x%X" dp.Regs.[R0] dp.Regs.[R1] dp.Regs.[R2] dp.Regs.[R15] dp.Regs.[R14]
            let passed, lines = computeTestResults t dp
            printfn "  Passed=%b" passed
            for l in lines do printfn "    %s" l

        printfn "--- Renderer-equivalent path (no transformCodeByTest) ---"
        let lim = reLoadProgram codeSrc
        printfn "  parse errors: %d, code instrs: %d" lim.Errors.Length (Map.count lim.Code)
        for KeyValue (WA addr, (ci, ln)) in lim.Code do
            printfn "    [0x%X] line %d opcode=%s" addr ln ci.InsOpCode
        let dp0 = initTestDP (lim.Mem, lim.SymInf.SymTab) t
        match dp0 with
        | Error e -> printfn "  initTestDP err %s" e
        | Ok dp ->
            let ri = getRunInfoFromImageWithInits NoBreak lim dp.Regs dp.Fl Map.empty dp.MM
            let ri' = asmStep 100000L ri
            printfn "  Renderer-path Test %d State=%A StepsDone=%d" t.TNum ri'.State ri'.StepsDone
            let dp' = fst ri'.dpCurrent
            printfn "  R0=%d R1=%d R2=%d PC=0x%X LR=0x%X" dp'.Regs.[R0] dp'.Regs.[R1] dp'.Regs.[R2] dp'.Regs.[R15] dp'.Regs.[R14]
    0
