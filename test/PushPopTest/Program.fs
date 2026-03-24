module PushPopTest

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

let pass testName = printfn "  PASS: %s" testName
let fail testName msg = printfn "  FAIL: %s - %s" testName msg

let assertRegEq (ri: RunInfo) (rn: RName) (expected: uint32) testName =
    let actual = (fst ri.dpCurrent).Regs.[rn]
    if actual = expected then pass testName
    else fail testName (sprintf "Expected %s = 0x%08X, got 0x%08X" (sprintf "%A" rn) expected actual)

let assertMemEq (ri: RunInfo) (addr: uint32) (expected: uint32) testName =
    let mm = (fst ri.dpCurrent).MM
    match Map.tryFind (WA addr) mm with
    | Some (Dat v) when v = expected -> pass testName
    | Some (Dat v) -> fail testName (sprintf "Expected [0x%08X] = 0x%08X, got 0x%08X" addr expected v)
    | Some CodeSpace -> fail testName (sprintf "Address 0x%08X contains CodeSpace, not data" addr)
    | None -> fail testName (sprintf "Address 0x%08X not in memory map" addr)

[<EntryPoint>]
let main _ =
    printfn "=== PUSH/POP Headless Tests ==="
    printfn ""

    // --- Test 1: Basic PUSH ---
    printfn "Test 1: Basic PUSH {R0, R1, R2}"
    let lines1 = [
        "        MOV R0, #0x11"
        "        MOV R1, #0x22"
        "        MOV R2, #0x33"
        "        PUSH {R0, R1, R2}"
    ]
    match runProgram lines1 100L with
    | None -> fail "parse" "Program did not parse"
    | Some ri ->
        let initSP = initStackPointer
        // PUSH = STMDB SP!, {R0, R1, R2}
        // SP decremented by 3*4=12 before storing
        // Stores at: SP-12 (lowest reg), SP-8, SP-4 (highest reg)
        assertRegEq ri R13 (initSP - 12u) "SP decremented by 12"
        assertMemEq ri (initSP - 12u) 0x11u "R0 at [SP-12]"
        assertMemEq ri (initSP - 8u) 0x22u "R1 at [SP-8]"
        assertMemEq ri (initSP - 4u) 0x33u "R2 at [SP-4]"
    printfn ""

    // --- Test 2: Basic POP ---
    printfn "Test 2: PUSH then POP {R3, R4, R5}"
    let lines2 = [
        "        MOV R0, #0xAA"
        "        MOV R1, #0xBB"
        "        MOV R2, #0xCC"
        "        PUSH {R0, R1, R2}"
        "        MOV R3, #0"
        "        MOV R4, #0"
        "        MOV R5, #0"
        "        POP {R3, R4, R5}"
    ]
    match runProgram lines2 100L with
    | None -> fail "parse" "Program did not parse"
    | Some ri ->
        // After PUSH + POP, SP should be back to initial
        assertRegEq ri R13 initStackPointer "SP restored"
        assertRegEq ri R3 0xAAu "R3 got R0's value"
        assertRegEq ri R4 0xBBu "R4 got R1's value"
        assertRegEq ri R5 0xCCu "R5 got R2's value"
    printfn ""

    // --- Test 3: PUSH/POP with LR/PC ---
    printfn "Test 3: PUSH {R4, LR} then POP {R4, PC}"
    let lines3 = [
        "        MOV R4, #0x42"
        "        BL subroutine"
        "        B done"
        "subroutine"
        "        PUSH {R4, LR}"
        "        MOV R4, #0x99"
        "        POP {R4, LR}"
        "        MOV PC, LR"
        "done"
    ]
    match runProgram lines3 100L with
    | None -> fail "parse" "Program did not parse"
    | Some ri ->
        assertRegEq ri R4 0x42u "R4 preserved across subroutine"
    printfn ""

    // --- Test 4: PUSH single register ---
    printfn "Test 4: PUSH {R7}"
    let lines4 = [
        "        MOV R7, #0x77"
        "        PUSH {R7}"
    ]
    match runProgram lines4 100L with
    | None -> fail "parse" "Program did not parse"
    | Some ri ->
        assertRegEq ri R13 (initStackPointer - 4u) "SP decremented by 4"
        assertMemEq ri (initStackPointer - 4u) 0x77u "R7 at [SP-4]"
    printfn ""

    // --- Test 5: PUSH register range ---
    printfn "Test 5: PUSH {R0-R3}"
    let lines5 = [
        "        MOV R0, #1"
        "        MOV R1, #2"
        "        MOV R2, #3"
        "        MOV R3, #4"
        "        PUSH {R0-R3}"
    ]
    match runProgram lines5 100L with
    | None -> fail "parse" "Program did not parse"
    | Some ri ->
        assertRegEq ri R13 (initStackPointer - 16u) "SP decremented by 16"
        assertMemEq ri (initStackPointer - 16u) 1u "R0 at lowest"
        assertMemEq ri (initStackPointer - 12u) 2u "R1 next"
        assertMemEq ri (initStackPointer - 8u) 3u "R2 next"
        assertMemEq ri (initStackPointer - 4u) 4u "R3 at highest"
    printfn ""

    // --- Test 6: Conditional PUSH ---
    printfn "Test 6: Conditional PUSHEQ (taken)"
    let lines6 = [
        "        MOV R0, #5"
        "        CMP R0, #5"
        "        PUSHEQ {R0}"
    ]
    match runProgram lines6 100L with
    | None -> fail "parse" "Program did not parse"
    | Some ri ->
        assertRegEq ri R13 (initStackPointer - 4u) "SP decremented (cond taken)"
        assertMemEq ri (initStackPointer - 4u) 5u "R0 stored"
    printfn ""

    printfn "Test 6b: Conditional PUSHEQ (not taken)"
    let lines6b = [
        "        MOV R0, #5"
        "        CMP R0, #3"
        "        PUSHEQ {R0}"
    ]
    match runProgram lines6b 100L with
    | None -> fail "parse" "Program did not parse"
    | Some ri ->
        assertRegEq ri R13 initStackPointer "SP unchanged (cond not taken)"
    printfn ""

    // --- Test 7: Parse error - empty register list ---
    printfn "Test 7: PUSH {} should fail to parse"
    let lines7 = [
        "        PUSH {}"
    ]
    match runProgram lines7 100L with
    | None -> pass "empty list rejected"
    | Some _ -> fail "parse" "Should have rejected empty register list"
    printfn ""

    // --- Test 8: Parse error - SP in register list ---
    printfn "Test 8: PUSH {R0, SP} should fail to parse"
    let lines8 = [
        "        PUSH {R0, SP}"
    ]
    match runProgram lines8 100L with
    | None -> pass "SP in reglist rejected"
    | Some _ -> fail "parse" "Should have rejected SP in register list"
    printfn ""

    printfn "=== Tests Complete ==="
    0
