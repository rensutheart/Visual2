module HalfWordTest

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
let mutable assertions = 0

let pass testName =
    printfn "  PASS: %s" testName
    assertions <- assertions + 1
let fail testName msg =
    printfn "  FAIL: %s - %s" testName msg
    failures <- failures + 1
    assertions <- assertions + 1

let assertRegEq (ri: RunInfo) (rn: RName) (expected: uint32) testName =
    let actual = (fst ri.dpCurrent).Regs.[rn]
    if actual = expected then pass testName
    else fail testName (sprintf "Expected %s = 0x%08X, got 0x%08X" (sprintf "%A" rn) expected actual)

let assertParseError (lines: string list) testName =
    let lim = reLoadProgram lines
    if lim.Errors.Length > 0 then pass testName
    else fail testName "Expected parse error but parsing succeeded"

[<EntryPoint>]
let main _ =
    printfn "=== Half-Word Memory Instructions Headless Tests ==="
    printfn ""

    // --- Test 1: STRH + LDRH basic store/load ---
    printfn "Test 1: STRH + LDRH basic store/load half-word"
    let lines1 = [
        "        MOV R0, #0x100"
        "        LDR R1, =0xABCD"
        "        STRH R1, [R0]"
        "        LDRH R2, [R0]"
        "        END"
    ]
    match runProgram lines1 100L with
    | None -> fail "Test 1" "Program failed to run"
    | Some ri ->
        assertRegEq ri R2 0x0000ABCDu "LDRH loads zero-extended half-word"
    printfn ""

    // --- Test 2: LDRH zero-extension (high bit of half-word set) ---
    printfn "Test 2: LDRH zero-extends (high bit of half-word set)"
    let lines2 = [
        "        MOV R0, #0x100"
        "        LDR R1, =0xFFFF8000"
        "        STR R1, [R0]"
        "        LDRH R2, [R0]"
        "        END"
    ]
    match runProgram lines2 100L with
    | None -> fail "Test 2" "Program failed to run"
    | Some ri ->
        assertRegEq ri R2 0x00008000u "LDRH zero-extends 0x8000 to 0x00008000"
    printfn ""

    // --- Test 3: LDRSH sign-extension (negative) ---
    printfn "Test 3: LDRSH sign-extends negative half-word"
    let lines3 = [
        "        MOV R0, #0x100"
        "        LDR R1, =0xFFFF8000"
        "        STR R1, [R0]"
        "        LDRSH R2, [R0]"
        "        END"
    ]
    match runProgram lines3 100L with
    | None -> fail "Test 3" "Program failed to run"
    | Some ri ->
        assertRegEq ri R2 0xFFFF8000u "LDRSH sign-extends 0x8000 to 0xFFFF8000"
    printfn ""

    // --- Test 4: LDRSH sign-extension (positive) ---
    printfn "Test 4: LDRSH with positive half-word (no sign extension)"
    let lines4 = [
        "        MOV R0, #0x100"
        "        LDR R1, =0x7F00"
        "        STRH R1, [R0]"
        "        LDRSH R2, [R0]"
        "        END"
    ]
    match runProgram lines4 100L with
    | None -> fail "Test 4" "Program failed to run"
    | Some ri ->
        assertRegEq ri R2 0x00007F00u "LDRSH positive half-word stays 0x00007F00"
    printfn ""

    // --- Test 5: LDRSB sign-extension (negative byte) ---
    printfn "Test 5: LDRSB sign-extends negative byte"
    let lines5 = [
        "        MOV R0, #0x100"
        "        MOV R1, #0xFF"
        "        STRB R1, [R0]"
        "        LDRSB R2, [R0]"
        "        END"
    ]
    match runProgram lines5 100L with
    | None -> fail "Test 5" "Program failed to run"
    | Some ri ->
        assertRegEq ri R2 0xFFFFFFFFu "LDRSB sign-extends 0xFF to 0xFFFFFFFF"
    printfn ""

    // --- Test 6: LDRSB positive byte ---
    printfn "Test 6: LDRSB with positive byte (no sign extension)"
    let lines6 = [
        "        MOV R0, #0x100"
        "        MOV R1, #0x7F"
        "        STRB R1, [R0]"
        "        LDRSB R2, [R0]"
        "        END"
    ]
    match runProgram lines6 100L with
    | None -> fail "Test 6" "Program failed to run"
    | Some ri ->
        assertRegEq ri R2 0x0000007Fu "LDRSB positive byte stays 0x0000007F"
    printfn ""

    // --- Test 7: STRH + LDRH upper half of word ---
    printfn "Test 7: STRH/LDRH at offset +2 (upper half of word)"
    let lines7 = [
        "        MOV R0, #0x100"
        "        MOV R1, #0"
        "        STR R1, [R0]"
        "        LDR R2, =0x1234"
        "        STRH R2, [R0, #2]"
        "        LDRH R3, [R0, #2]"
        "        LDRH R4, [R0]"
        "        END"
    ]
    match runProgram lines7 100L with
    | None -> fail "Test 7" "Program failed to run"
    | Some ri ->
        assertRegEq ri R3 0x00001234u "LDRH at +2 loads upper half = 0x1234"
        assertRegEq ri R4 0x00000000u "LDRH at +0 loads lower half = 0x0000"
    printfn ""

    // --- Test 8: LDRH with pre-indexed addressing ---
    printfn "Test 8: LDRH with pre-indexed addressing"
    let lines8 = [
        "        MOV R0, #0x100"
        "        LDR R1, =0x5678"
        "        STRH R1, [R0, #4]"
        "        MOV R3, #0x100"
        "        LDRH R2, [R3, #4]!"
        "        END"
    ]
    match runProgram lines8 100L with
    | None -> fail "Test 8" "Program failed to run"
    | Some ri ->
        assertRegEq ri R2 0x00005678u "LDRH pre-indexed loads correct value"
        assertRegEq ri R3 0x00000104u "Base register updated by pre-index"
    printfn ""

    // --- Test 9: LDRH with post-indexed addressing ---
    printfn "Test 9: LDRH with post-indexed addressing"
    let lines9 = [
        "        MOV R0, #0x100"
        "        LDR R1, =0x9ABC"
        "        STRH R1, [R0]"
        "        MOV R3, #0x100"
        "        LDRH R2, [R3], #4"
        "        END"
    ]
    match runProgram lines9 100L with
    | None -> fail "Test 9" "Program failed to run"
    | Some ri ->
        assertRegEq ri R2 0x00009ABCu "LDRH post-indexed loads from original addr"
        assertRegEq ri R3 0x00000104u "Base register updated by post-index"
    printfn ""

    // --- Test 10: STRH with register offset ---
    printfn "Test 10: STRH/LDRH with register offset"
    let lines10 = [
        "        MOV R0, #0x100"
        "        MOV R1, #6"
        "        LDR R2, =0xDEAD"
        "        STRH R2, [R0, R1]"
        "        LDRH R3, [R0, R1]"
        "        END"
    ]
    match runProgram lines10 100L with
    | None -> fail "Test 10" "Program failed to run"
    | Some ri ->
        assertRegEq ri R3 0x0000DEADu "LDRH with register offset loads correctly"
    printfn ""

    // --- Test 11: STRSH should be rejected (parse error) ---
    printfn "Test 11: STRSH should be rejected as invalid"
    let lines11 = [
        "        MOV R0, #0x100"
        "        STRSH R1, [R0]"
        "        END"
    ]
    assertParseError lines11 "STRSH produces parse error"
    printfn ""

    // --- Test 12: STRSB should be rejected (parse error) ---
    printfn "Test 12: STRSB should be rejected as invalid"
    let lines12 = [
        "        MOV R0, #0x100"
        "        STRSB R1, [R0]"
        "        END"
    ]
    assertParseError lines12 "STRSB produces parse error"
    printfn ""

    // --- Test 13: Conditional LDRHEQ (condition true) ---
    printfn "Test 13: Conditional LDRHEQ (condition true)"
    let lines13 = [
        "        MOV R0, #0x100"
        "        LDR R1, =0x4242"
        "        STRH R1, [R0]"
        "        MOV R2, #0"
        "        CMP R2, #0"
        "        MOV R3, #0"
        "        LDRHEQ R3, [R0]"
        "        END"
    ]
    match runProgram lines13 100L with
    | None -> fail "Test 13" "Program failed to run"
    | Some ri ->
        assertRegEq ri R3 0x00004242u "LDRHEQ executes when Z=1"
    printfn ""

    // --- Test 14: Conditional LDRHNE (condition false, should not execute) ---
    printfn "Test 14: Conditional LDRHNE (condition false)"
    let lines14 = [
        "        MOV R0, #0x100"
        "        LDR R1, =0x4242"
        "        STRH R1, [R0]"
        "        MOV R2, #0"
        "        CMP R2, #0"
        "        MOV R3, #0xFF"
        "        LDRHNE R3, [R0]"
        "        END"
    ]
    match runProgram lines14 100L with
    | None -> fail "Test 14" "Program failed to run"
    | Some ri ->
        assertRegEq ri R3 0x000000FFu "LDRHNE skipped when Z=1, R3 unchanged"
    printfn ""

    // --- Test 15: LDRSH from DCD data ---
    printfn "Test 15: LDRSH from DCD data with negative half-word"
    let lines15 = [
        "        LDR R0, =mydata"
        "        LDRSH R1, [R0]"
        "        LDRSH R2, [R0, #2]"
        "        END"
        "mydata  DCD 0x0000FFEE"
    ]
    match runProgram lines15 100L with
    | None -> fail "Test 15" "Program failed to run"
    | Some ri ->
        assertRegEq ri R1 0xFFFFFFEEu "LDRSH of 0xFFEE sign-extends to 0xFFFFFFEE"
        assertRegEq ri R2 0x00000000u "LDRSH of 0x0000 stays 0x00000000"
    printfn ""

    // --- Test 16: LDRSB from various byte positions ---
    printfn "Test 16: LDRSB from different byte positions in a word"
    let lines16 = [
        "        LDR R0, =mydata2"
        "        LDRSB R1, [R0]"
        "        LDRSB R2, [R0, #1]"
        "        LDRSB R3, [R0, #2]"
        "        LDRSB R4, [R0, #3]"
        "        END"
        "mydata2 DCD 0xFF007F80"
    ]
    match runProgram lines16 100L with
    | None -> fail "Test 16" "Program failed to run"
    | Some ri ->
        // 0xFF007F80 in little-endian memory:
        // byte 0 (addr+0): 0x80
        // byte 1 (addr+1): 0x7F
        // byte 2 (addr+2): 0x00
        // byte 3 (addr+3): 0xFF
        assertRegEq ri R1 0xFFFFFF80u "LDRSB byte 0 (0x80) -> 0xFFFFFF80"
        assertRegEq ri R2 0x0000007Fu "LDRSB byte 1 (0x7F) -> 0x0000007F"
        assertRegEq ri R3 0x00000000u "LDRSB byte 2 (0x00) -> 0x00000000"
        assertRegEq ri R4 0xFFFFFFFFu "LDRSB byte 3 (0xFF) -> 0xFFFFFFFF"
    printfn ""

    // --- Summary ---
    printfn "=== SUMMARY ==="
    printfn "Assertions: %d" assertions
    printfn "Failures: %d" failures
    if failures = 0 then
        printfn "ALL TESTS PASSED"
    else
        printfn "SOME TESTS FAILED"
    failures
