module BxBlxTest

open CommonData
open ExecutionTop

/// Helper to run a program and return the final RunInfo
let runProgram (lines: string list) maxSteps =
    let lim = reLoadProgram lines
    if lim.Errors.Length > 0 then
        printfn "PARSE ERRORS:"
        lim.Errors |> List.iter (fun (e, line, opc) -> printfn "  Line %d (%s): %A" line opc e)
        None
    else
        let ri = getRunInfoFromImageWithInits NoBreak lim initialRegMap {N=false;C=false;Z=false;V=false} Map.empty lim.Mem
        let result = asmStep maxSteps ri
        Some result

let mutable failures = 0

let pass testName = printfn "  PASS: %s" testName
let fail testName msg =
    printfn "  FAIL: %s - %s" testName msg
    failures <- failures + 1

let assertRegEq (ri: RunInfo) (rn: RName) (expected: uint32) testName =
    let actual = (fst ri.dpCurrent).Regs.[rn]
    if actual = expected then pass testName
    else fail testName (sprintf "Expected %s = 0x%08X, got 0x%08X" (sprintf "%A" rn) expected actual)

let assertState (ri: RunInfo) (expected: ProgState) testName =
    if ri.State = expected then pass testName
    else fail testName (sprintf "Expected state %A, got %A" expected ri.State)

[<EntryPoint>]
let main _ =
    printfn "=== BX/BLX Headless Tests ==="
    printfn ""

    // --- Test 1: Basic BX - branch to address in register ---
    printfn "Test 1: BX LR (return from subroutine)"
    let lines1 = [
        "        MOV R0, #0"
        "        BL sub1"
        "        MOV R0, #0x42"
        "        B done"
        "sub1"
        "        MOV R1, #0x99"
        "        BX LR"
        "done"
    ]
    match runProgram lines1 100L with
    | None -> fail "parse" "Program did not parse"
    | Some ri ->
        assertRegEq ri R0 0x42u "R0 = 0x42 (continued after BL)"
        assertRegEq ri R1 0x99u "R1 = 0x99 (set in subroutine)"
    printfn ""

    // --- Test 2: BLX - branch with link to register ---
    printfn "Test 2: BLX Rm (call subroutine via register)"
    let lines2 = [
        "        ADR R4, sub2"
        "        MOV R0, #0"
        "        BLX R4"
        "        MOV R0, #0x55"
        "        B done2"
        "sub2"
        "        MOV R1, #0xAA"
        "        BX LR"
        "done2"
    ]
    match runProgram lines2 100L with
    | None -> fail "parse" "Program did not parse"
    | Some ri ->
        assertRegEq ri R0 0x55u "R0 = 0x55 (continued after BLX)"
        assertRegEq ri R1 0xAAu "R1 = 0xAA (set in subroutine)"
    printfn ""

    // --- Test 3: BLX sets LR correctly ---
    printfn "Test 3: BLX sets LR to return address"
    let lines3 = [
        "        ADR R4, sub3"
        "        BLX R4"
        "        B done3"
        "sub3"
        "        MOV R5, LR"
        "        BX LR"
        "done3"
    ]
    match runProgram lines3 100L with
    | None -> fail "parse" "Program did not parse"
    | Some ri ->
        // BLX is at address 4 (second instruction), LR should be 8 (next instruction)
        assertRegEq ri R5 8u "LR was set to return address (8)"
    printfn ""

    // --- Test 4: Conditional BX ---
    printfn "Test 4: Conditional BXEQ (taken)"
    let lines4 = [
        "        MOV R0, #5"
        "        CMP R0, #5"
        "        ADR R4, target4"
        "        BXEQ R4"
        "        MOV R1, #0xFF"
        "        B done4"
        "target4"
        "        MOV R1, #0x42"
        "done4"
    ]
    match runProgram lines4 100L with
    | None -> fail "parse" "Program did not parse"
    | Some ri ->
        assertRegEq ri R1 0x42u "R1 = 0x42 (branch taken)"
    printfn ""

    printfn "Test 4b: Conditional BXEQ (not taken)"
    let lines4b = [
        "        MOV R0, #5"
        "        CMP R0, #3"
        "        ADR R4, target4b"
        "        BXEQ R4"
        "        MOV R1, #0xFF"
        "        B done4b"
        "target4b"
        "        MOV R1, #0x42"
        "done4b"
    ]
    match runProgram lines4b 100L with
    | None -> fail "parse" "Program did not parse"
    | Some ri ->
        assertRegEq ri R1 0xFFu "R1 = 0xFF (branch not taken)"
    printfn ""

    // --- Test 5: BX with Thumb bit set (should be masked off) ---
    printfn "Test 5: BX with bit 0 set (Thumb bit masked)"
    let lines5 = [
        "        ADR R4, target5"
        "        ORR R4, R4, #1"
        "        BX R4"
        "        MOV R1, #0xFF"
        "        B done5"
        "target5"
        "        MOV R1, #0x42"
        "done5"
    ]
    match runProgram lines5 100L with
    | None -> fail "parse" "Program did not parse"
    | Some ri ->
        assertRegEq ri R1 0x42u "R1 = 0x42 (Thumb bit masked off)"
    printfn ""

    // --- Test 6: Nested BLX calls ---
    printfn "Test 6: Nested BLX calls with PUSH/POP"
    let lines6 = [
        "        ADR R4, outer"
        "        BLX R4"
        "        B done6"
        "outer"
        "        PUSH {R4, LR}"
        "        MOV R0, #0x11"
        "        ADR R4, inner"
        "        BLX R4"
        "        MOV R0, #0x22"
        "        POP {R4, LR}"
        "        BX LR"
        "inner"
        "        MOV R2, #0x33"
        "        BX LR"
        "done6"
    ]
    match runProgram lines6 100L with
    | None -> fail "parse" "Program did not parse"
    | Some ri ->
        assertRegEq ri R0 0x22u "R0 = 0x22 (set after inner call)"
        assertRegEq ri R2 0x33u "R2 = 0x33 (set in inner)"
    printfn ""

    // --- Test 7: BX PC should be rejected ---
    printfn "Test 7: BX PC should fail to parse"
    let lines7 = [
        "        BX PC"
    ]
    match runProgram lines7 100L with
    | None -> pass "BX PC rejected"
    | Some _ -> fail "parse" "Should have rejected BX PC"
    printfn ""

    // --- Test 8: BLX PC should be rejected ---
    printfn "Test 8: BLX PC should fail to parse"
    let lines8 = [
        "        BLX PC"
    ]
    match runProgram lines8 100L with
    | None -> pass "BLX PC rejected"
    | Some _ -> fail "parse" "Should have rejected BLX PC"
    printfn ""

    // --- Test 9: BX with invalid operand ---
    printfn "Test 9: BX #5 should fail to parse"
    let lines9 = [
        "        BX #5"
    ]
    match runProgram lines9 100L with
    | None -> pass "BX #5 rejected"
    | Some _ -> fail "parse" "Should have rejected BX #5"
    printfn ""

    printfn ""
    printfn "=== Tests Complete: %d failures ===" failures
    failures
