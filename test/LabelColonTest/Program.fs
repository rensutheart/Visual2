module LabelColonTest

open CommonData
open ExecutionTop

/// Helper to run a program and return the final RunInfo, or print errors
let runProgram (lines: string list) maxSteps =
    let lim = reLoadProgram lines
    if lim.Errors.Length > 0 then
        printfn "  PARSE ERRORS:"
        lim.Errors |> List.iter (fun (e, line, opc) -> printfn "    Line %d (%s): %A" line opc e)
        None
    else
        let ri = getRunInfoFromImageWithInits NoBreak lim initialRegMap {N=false;C=false;Z=false;V=false} Map.empty lim.Mem
        let result = asmStep maxSteps ri
        Some result

let mutable passed = 0
let mutable failed = 0

let pass testName =
    passed <- passed + 1
    printfn "  PASS: %s" testName

let fail testName msg =
    failed <- failed + 1
    printfn "  FAIL: %s - %s" testName msg

let assertRegEq (ri: RunInfo) (rn: RName) (expected: uint32) testName =
    let actual = (fst ri.dpCurrent).Regs.[rn]
    if actual = expected then pass testName
    else fail testName (sprintf "Expected %A = 0x%08X, got 0x%08X" rn expected actual)

let assertState (ri: RunInfo) (expected: ProgState) testName =
    if ri.State = expected then pass testName
    else fail testName (sprintf "Expected state %A, got %A" expected ri.State)

[<EntryPoint>]
let main _ =
    printfn "=== Label Colon Syntax Tests ==="
    printfn ""

    // ---------------------------------------------------------------
    // Test 1: Label WITHOUT colon (original syntax, should still work)
    // ---------------------------------------------------------------
    printfn "Test 1: Label without colon (original syntax)"
    let lines1 = [
        "        MOV R0, #1"
        "        B done"
        "        MOV R0, #99"
        "done"
        "        MOV R1, #2"
    ]
    match runProgram lines1 100L with
    | None -> fail "parse" "Program did not parse"
    | Some ri ->
        assertRegEq ri R0 1u "R0 = 1 (MOV before branch)"
        assertRegEq ri R1 2u "R1 = 2 (reached 'done' label)"
        assertState ri PSExit "Program exited cleanly"
    printfn ""

    // ---------------------------------------------------------------
    // Test 2: Label WITH colon (GNU syntax)
    // ---------------------------------------------------------------
    printfn "Test 2: Label with colon (GNU syntax)"
    let lines2 = [
        "        MOV R0, #1"
        "        B done"
        "        MOV R0, #99"
        "done:"
        "        MOV R1, #2"
    ]
    match runProgram lines2 100L with
    | None -> fail "parse" "Program did not parse"
    | Some ri ->
        assertRegEq ri R0 1u "R0 = 1 (MOV before branch)"
        assertRegEq ri R1 2u "R1 = 2 (reached 'done:' label)"
        assertState ri PSExit "Program exited cleanly"
    printfn ""

    // ---------------------------------------------------------------
    // Test 3: Label with colon on same line as instruction
    // ---------------------------------------------------------------
    printfn "Test 3: Label with colon on same line as instruction"
    let lines3 = [
        "        MOV R0, #0"
        "loop:   ADD R0, R0, #1"
        "        CMP R0, #5"
        "        BLT loop"
    ]
    match runProgram lines3 200L with
    | None -> fail "parse" "Program did not parse"
    | Some ri ->
        assertRegEq ri R0 5u "R0 = 5 (looped 5 times)"
        assertState ri PSExit "Program exited cleanly"
    printfn ""

    // ---------------------------------------------------------------
    // Test 4: Label without colon on same line as instruction (original)
    // ---------------------------------------------------------------
    printfn "Test 4: Label without colon on same line as instruction (original)"
    let lines4 = [
        "        MOV R0, #0"
        "loop    ADD R0, R0, #1"
        "        CMP R0, #5"
        "        BLT loop"
    ]
    match runProgram lines4 200L with
    | None -> fail "parse" "Program did not parse"
    | Some ri ->
        assertRegEq ri R0 5u "R0 = 5 (looped 5 times)"
        assertState ri PSExit "Program exited cleanly"
    printfn ""

    // ---------------------------------------------------------------
    // Test 5: BL/BX with colon labels (subroutine call)
    // ---------------------------------------------------------------
    printfn "Test 5: BL with colon-style labels"
    let lines5 = [
        "        MOV R4, #0x42"
        "        BL subroutine"
        "        B done"
        "subroutine:"
        "        PUSH {R4, LR}"
        "        MOV R4, #0x99"
        "        POP {R4, LR}"
        "        MOV PC, LR"
        "done:"
    ]
    match runProgram lines5 100L with
    | None -> fail "parse" "Program did not parse"
    | Some ri ->
        assertRegEq ri R4 0x42u "R4 preserved across subroutine"
        assertState ri PSExit "Program exited cleanly"
    printfn ""

    // ---------------------------------------------------------------
    // Test 6: Mixed colon and no-colon labels in same program
    // ---------------------------------------------------------------
    printfn "Test 6: Mixed colon and no-colon labels"
    let lines6 = [
        "        MOV R0, #10"
        "        MOV R1, #0"
        "loop:   ADD R1, R1, R0"
        "        SUBS R0, R0, #1"
        "        BNE loop"
        "        B finish"
        "finish"
        "        MOV R2, #0xFF"
    ]
    match runProgram lines6 500L with
    | None -> fail "parse" "Program did not parse"
    | Some ri ->
        assertRegEq ri R1 55u "R1 = 55 (sum 1..10)"
        assertRegEq ri R0 0u "R0 = 0 (counted down)"
        assertRegEq ri R2 0xFFu "R2 = 0xFF (reached 'finish')"
        assertState ri PSExit "Program exited cleanly"
    printfn ""

    // ---------------------------------------------------------------
    // Test 7: Label-only line with colon (no instruction)
    // ---------------------------------------------------------------
    printfn "Test 7: Label-only line with colon"
    let lines7 = [
        "        MOV R0, #1"
        "        B skip"
        "        MOV R0, #99"
        "skip:"
        "        MOV R1, #42"
    ]
    match runProgram lines7 100L with
    | None -> fail "parse" "Program did not parse"
    | Some ri ->
        assertRegEq ri R0 1u "R0 = 1 (skipped MOV #99)"
        assertRegEq ri R1 42u "R1 = 42 (reached skip:)"
        assertState ri PSExit "Program exited cleanly"
    printfn ""

    // ---------------------------------------------------------------
    // Test 8: DCD data with colon label
    // ---------------------------------------------------------------
    printfn "Test 8: DCD data with colon label"
    let lines8 = [
        "        LDR R0, =mydata"
        "        LDR R1, [R0]"
        "        B done"
        "mydata: DCD 0x12345678"
        "done:"
    ]
    match runProgram lines8 100L with
    | None -> fail "parse" "Program did not parse"
    | Some ri ->
        assertRegEq ri R1 0x12345678u "R1 = 0x12345678 (loaded from mydata:)"
        assertState ri PSExit "Program exited cleanly"
    printfn ""

    // ---------------------------------------------------------------
    // Test 9: The exact user example - colon version
    // ---------------------------------------------------------------
    printfn "Test 9: User example with colon"
    let lines9 = [
        "label:"
        "        MOV R0, R0"
        "        B label"
    ]
    // This is an infinite loop, so we run limited steps and check it's still running
    match runProgram lines9 10L with
    | None -> fail "parse" "Program did not parse"
    | Some ri ->
        assertState ri PSRunning "Program still running (infinite loop)"
        pass "Parsed and executed with colon label"
    printfn ""

    // ---------------------------------------------------------------
    // Test 10: The exact user example - no colon version
    // ---------------------------------------------------------------
    printfn "Test 10: User example without colon"
    let lines10 = [
        "label"
        "        MOV R0, R0"
        "        B label"
    ]
    match runProgram lines10 10L with
    | None -> fail "parse" "Program did not parse"
    | Some ri ->
        assertState ri PSRunning "Program still running (infinite loop)"
        pass "Parsed and executed without colon label"
    printfn ""

    // ---------------------------------------------------------------
    // Summary
    // ---------------------------------------------------------------
    printfn "=============================="
    printfn "Results: %d passed, %d failed" passed failed
    if failed > 0 then
        printfn "SOME TESTS FAILED"
        1
    else
        printfn "ALL TESTS PASSED"
        0
