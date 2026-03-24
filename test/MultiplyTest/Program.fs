module MultiplyTest

open CommonData
open ExecutionTop

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

let assertFlagEq (ri: RunInfo) flagName (getFn: Flags -> bool) (expected: bool) testName =
    let actual = getFn (fst ri.dpCurrent).Fl
    if actual = expected then pass testName
    else fail testName (sprintf "Expected %s = %b, got %b" flagName expected actual)

let assertParseError (lines: string list) testName =
    let lim = reLoadProgram lines
    if lim.Errors.Length > 0 then pass testName
    else fail testName "Expected parse error but parsing succeeded"

[<EntryPoint>]
let main _ =
    printfn "=== Multiply Instructions Headless Tests ==="
    printfn ""

    // =========================================================================
    // MUL Tests
    // =========================================================================

    // --- Test 1: MUL basic ---
    printfn "Test 1: MUL basic 3 * 7 = 21"
    let lines1 = [
        "        MOV R0, #3"
        "        MOV R1, #7"
        "        MUL R2, R0, R1"
        "        END"
    ]
    match runProgram lines1 100L with
    | None -> fail "Test 1" "Program failed to run"
    | Some ri ->
        assertRegEq ri R2 21u "MUL 3*7=21"
    printfn ""

    // --- Test 2: MUL by zero ---
    printfn "Test 2: MUL by zero"
    let lines2 = [
        "        MOV R0, #42"
        "        MOV R1, #0"
        "        MUL R2, R0, R1"
        "        END"
    ]
    match runProgram lines2 100L with
    | None -> fail "Test 2" "Program failed to run"
    | Some ri ->
        assertRegEq ri R2 0u "MUL x*0=0"
    printfn ""

    // --- Test 3: MUL by one ---
    printfn "Test 3: MUL by one (identity)"
    let lines3 = [
        "        MOV R0, #255"
        "        MOV R1, #1"
        "        MUL R2, R0, R1"
        "        END"
    ]
    match runProgram lines3 100L with
    | None -> fail "Test 3" "Program failed to run"
    | Some ri ->
        assertRegEq ri R2 255u "MUL x*1=x"
    printfn ""

    // --- Test 4: MUL large values (overflow/truncation to 32 bits) ---
    printfn "Test 4: MUL truncation to 32 bits"
    let lines4 = [
        "        LDR R0, =0x10000"
        "        LDR R1, =0x10000"
        "        MUL R2, R0, R1"
        "        END"
    ]
    match runProgram lines4 100L with
    | None -> fail "Test 4" "Program failed to run"
    | Some ri ->
        // 0x10000 * 0x10000 = 0x100000000, truncated to 32 bits = 0
        assertRegEq ri R2 0u "MUL 0x10000*0x10000 truncates to 0"
    printfn ""

    // --- Test 5: MULS sets N flag ---
    printfn "Test 5: MULS sets N flag for negative result"
    let lines5 = [
        "        LDR R0, =0xFFFFFFFF"
        "        MOV R1, #3"
        "        MULS R2, R0, R1"
        "        END"
    ]
    match runProgram lines5 100L with
    | None -> fail "Test 5" "Program failed to run"
    | Some ri ->
        // 0xFFFFFFFF * 3 = 0x2FFFFFFFD, low 32 = 0xFFFFFFFD
        assertRegEq ri R2 0xFFFFFFFDu "MULS -1*3=-3 (0xFFFFFFFD)"
        assertFlagEq ri "N" (fun f -> f.N) true "N flag set"
        assertFlagEq ri "Z" (fun f -> f.Z) false "Z flag clear"
    printfn ""

    // --- Test 6: MULS sets Z flag ---
    printfn "Test 6: MULS sets Z flag for zero result"
    let lines6 = [
        "        MOV R0, #42"
        "        MOV R1, #0"
        "        MULS R2, R0, R1"
        "        END"
    ]
    match runProgram lines6 100L with
    | None -> fail "Test 6" "Program failed to run"
    | Some ri ->
        assertRegEq ri R2 0u "MULS x*0=0"
        assertFlagEq ri "Z" (fun f -> f.Z) true "Z flag set"
        assertFlagEq ri "N" (fun f -> f.N) false "N flag clear"
    printfn ""

    // --- Test 7: MUL without S does not change flags ---
    printfn "Test 7: MUL without S preserves flags"
    let lines7 = [
        "        MOV R0, #0"
        "        CMP R0, #1"
        "        MOV R1, #5"
        "        MOV R2, #3"
        "        MUL R3, R1, R2"
        "        END"
    ]
    match runProgram lines7 100L with
    | None -> fail "Test 7" "Program failed to run"
    | Some ri ->
        assertRegEq ri R3 15u "MUL 5*3=15"
        // CMP R0, #1 where R0=0: 0-1=-1, so N=1, Z=0
        assertFlagEq ri "N" (fun f -> f.N) true "N flag preserved from CMP"
    printfn ""

    // =========================================================================
    // MLA Tests
    // =========================================================================

    // --- Test 8: MLA basic ---
    printfn "Test 8: MLA basic 3*7+10=31"
    let lines8 = [
        "        MOV R0, #3"
        "        MOV R1, #7"
        "        MOV R2, #10"
        "        MLA R3, R0, R1, R2"
        "        END"
    ]
    match runProgram lines8 100L with
    | None -> fail "Test 8" "Program failed to run"
    | Some ri ->
        assertRegEq ri R3 31u "MLA 3*7+10=31"
    printfn ""

    // --- Test 9: MLA with accumulator zero (same as MUL) ---
    printfn "Test 9: MLA with zero accumulator"
    let lines9 = [
        "        MOV R0, #6"
        "        MOV R1, #8"
        "        MOV R2, #0"
        "        MLA R3, R0, R1, R2"
        "        END"
    ]
    match runProgram lines9 100L with
    | None -> fail "Test 9" "Program failed to run"
    | Some ri ->
        assertRegEq ri R3 48u "MLA 6*8+0=48"
    printfn ""

    // --- Test 10: MLA overflow/truncation ---
    printfn "Test 10: MLA overflow wraps to 32 bits"
    let lines10 = [
        "        LDR R0, =0xFFFFFFFF"
        "        MOV R1, #1"
        "        MOV R2, #2"
        "        MLA R3, R0, R1, R2"
        "        END"
    ]
    match runProgram lines10 100L with
    | None -> fail "Test 10" "Program failed to run"
    | Some ri ->
        // 0xFFFFFFFF * 1 + 2 = 0x100000001, low 32 = 1
        assertRegEq ri R3 1u "MLA 0xFFFFFFFF*1+2 wraps to 1"
    printfn ""

    // --- Test 11: MLAS sets flags ---
    printfn "Test 11: MLAS sets N flag"
    let lines11 = [
        "        LDR R0, =0x80000000"
        "        MOV R1, #1"
        "        MOV R2, #0"
        "        MLAS R3, R0, R1, R2"
        "        END"
    ]
    match runProgram lines11 100L with
    | None -> fail "Test 11" "Program failed to run"
    | Some ri ->
        assertRegEq ri R3 0x80000000u "MLAS 0x80000000*1+0"
        assertFlagEq ri "N" (fun f -> f.N) true "N flag set"
    printfn ""

    // =========================================================================
    // UMULL Tests
    // =========================================================================

    // --- Test 12: UMULL basic ---
    printfn "Test 12: UMULL basic 100*200=20000"
    let lines12 = [
        "        MOV R0, #100"
        "        MOV R1, #200"
        "        UMULL R2, R3, R0, R1"
        "        END"
    ]
    match runProgram lines12 100L with
    | None -> fail "Test 12" "Program failed to run"
    | Some ri ->
        assertRegEq ri R2 20000u "UMULL lo = 20000"
        assertRegEq ri R3 0u "UMULL hi = 0"
    printfn ""

    // --- Test 13: UMULL large (result > 32 bits) ---
    printfn "Test 13: UMULL large result spanning 64 bits"
    let lines13 = [
        "        LDR R0, =0xFFFFFFFF"
        "        LDR R1, =0xFFFFFFFF"
        "        UMULL R2, R3, R0, R1"
        "        END"
    ]
    match runProgram lines13 100L with
    | None -> fail "Test 13" "Program failed to run"
    | Some ri ->
        // 0xFFFFFFFF * 0xFFFFFFFF = 0xFFFFFFFE00000001
        assertRegEq ri R2 0x00000001u "UMULL lo = 0x00000001"
        assertRegEq ri R3 0xFFFFFFFEu "UMULL hi = 0xFFFFFFFE"
    printfn ""

    // --- Test 14: UMULL by zero ---
    printfn "Test 14: UMULL by zero"
    let lines14 = [
        "        LDR R0, =0xFFFFFFFF"
        "        MOV R1, #0"
        "        UMULL R2, R3, R0, R1"
        "        END"
    ]
    match runProgram lines14 100L with
    | None -> fail "Test 14" "Program failed to run"
    | Some ri ->
        assertRegEq ri R2 0u "UMULL lo = 0"
        assertRegEq ri R3 0u "UMULL hi = 0"
    printfn ""

    // --- Test 15: UMULL powers of 2 ---
    printfn "Test 15: UMULL 0x80000000 * 2 = 0x100000000"
    let lines15 = [
        "        LDR R0, =0x80000000"
        "        MOV R1, #2"
        "        UMULL R2, R3, R0, R1"
        "        END"
    ]
    match runProgram lines15 100L with
    | None -> fail "Test 15" "Program failed to run"
    | Some ri ->
        assertRegEq ri R2 0u "UMULL lo = 0"
        assertRegEq ri R3 1u "UMULL hi = 1"
    printfn ""

    // --- Test 16: UMULLS sets Z flag ---
    printfn "Test 16: UMULLS sets Z flag for zero result"
    let lines16 = [
        "        MOV R0, #0"
        "        MOV R1, #100"
        "        UMULLS R2, R3, R0, R1"
        "        END"
    ]
    match runProgram lines16 100L with
    | None -> fail "Test 16" "Program failed to run"
    | Some ri ->
        assertRegEq ri R2 0u "UMULLS lo = 0"
        assertRegEq ri R3 0u "UMULLS hi = 0"
        assertFlagEq ri "Z" (fun f -> f.Z) true "Z flag set"
        assertFlagEq ri "N" (fun f -> f.N) false "N flag clear"
    printfn ""

    // --- Test 17: UMULLS sets N flag ---
    printfn "Test 17: UMULLS sets N flag (bit 63 of result)"
    let lines17 = [
        "        LDR R0, =0xFFFFFFFF"
        "        LDR R1, =0xFFFFFFFF"
        "        UMULLS R2, R3, R0, R1"
        "        END"
    ]
    match runProgram lines17 100L with
    | None -> fail "Test 17" "Program failed to run"
    | Some ri ->
        // hi = 0xFFFFFFFE, bit 31 set => N flag
        assertFlagEq ri "N" (fun f -> f.N) true "N flag set (hi bit 31)"
    printfn ""

    // =========================================================================
    // UMLAL Tests
    // =========================================================================

    // --- Test 18: UMLAL basic accumulate ---
    printfn "Test 18: UMLAL basic accumulate"
    let lines18 = [
        "        MOV R0, #10"
        "        MOV R1, #20"
        "        MOV R2, #100"
        "        MOV R3, #0"
        "        UMLAL R2, R3, R0, R1"
        "        END"
    ]
    match runProgram lines18 100L with
    | None -> fail "Test 18" "Program failed to run"
    | Some ri ->
        // 10*20 = 200, + 100 = 300
        assertRegEq ri R2 300u "UMLAL lo = 300"
        assertRegEq ri R3 0u "UMLAL hi = 0"
    printfn ""

    // --- Test 19: UMLAL with 64-bit accumulator ---
    printfn "Test 19: UMLAL with 64-bit accumulator"
    let lines19 = [
        "        LDR R0, =0x10000"
        "        LDR R1, =0x10000"
        "        LDR R2, =0xFFFFFFFF"
        "        MOV R3, #0"
        "        UMLAL R2, R3, R0, R1"
        "        END"
    ]
    match runProgram lines19 100L with
    | None -> fail "Test 19" "Program failed to run"
    | Some ri ->
        // 0x10000 * 0x10000 = 0x100000000
        // + 0x00000000FFFFFFFF = 0x00000001FFFFFFFF
        assertRegEq ri R2 0xFFFFFFFFu "UMLAL lo = 0xFFFFFFFF"
        assertRegEq ri R3 1u "UMLAL hi = 1"
    printfn ""

    // --- Test 20: UMLAL accumulate overflow into hi ---
    printfn "Test 20: UMLAL accumulate overflow into hi word"
    let lines20 = [
        "        LDR R0, =0xFFFFFFFF"
        "        MOV R1, #1"
        "        MOV R2, #1"
        "        MOV R3, #0"
        "        UMLAL R2, R3, R0, R1"
        "        END"
    ]
    match runProgram lines20 100L with
    | None -> fail "Test 20" "Program failed to run"
    | Some ri ->
        // 0xFFFFFFFF * 1 = 0xFFFFFFFF, + 1 = 0x100000000
        assertRegEq ri R2 0u "UMLAL lo = 0"
        assertRegEq ri R3 1u "UMLAL hi = 1"
    printfn ""

    // =========================================================================
    // SMULL Tests
    // =========================================================================

    // --- Test 21: SMULL positive * positive ---
    printfn "Test 21: SMULL positive * positive"
    let lines21 = [
        "        MOV R0, #100"
        "        MOV R1, #200"
        "        SMULL R2, R3, R0, R1"
        "        END"
    ]
    match runProgram lines21 100L with
    | None -> fail "Test 21" "Program failed to run"
    | Some ri ->
        assertRegEq ri R2 20000u "SMULL lo = 20000"
        assertRegEq ri R3 0u "SMULL hi = 0"
    printfn ""

    // --- Test 22: SMULL negative * positive ---
    printfn "Test 22: SMULL -1 * 3 = -3 (signed 64-bit)"
    let lines22 = [
        "        LDR R0, =0xFFFFFFFF"
        "        MOV R1, #3"
        "        SMULL R2, R3, R0, R1"
        "        END"
    ]
    match runProgram lines22 100L with
    | None -> fail "Test 22" "Program failed to run"
    | Some ri ->
        // -1 * 3 = -3 as signed 64-bit = 0xFFFFFFFFFFFFFFFD
        assertRegEq ri R2 0xFFFFFFFDu "SMULL lo = 0xFFFFFFFD"
        assertRegEq ri R3 0xFFFFFFFFu "SMULL hi = 0xFFFFFFFF"
    printfn ""

    // --- Test 23: SMULL negative * negative ---
    printfn "Test 23: SMULL -1 * -1 = 1 (signed)"
    let lines23 = [
        "        LDR R0, =0xFFFFFFFF"
        "        LDR R1, =0xFFFFFFFF"
        "        SMULL R2, R3, R0, R1"
        "        END"
    ]
    match runProgram lines23 100L with
    | None -> fail "Test 23" "Program failed to run"
    | Some ri ->
        // (-1) * (-1) = 1 as signed 64-bit = 0x0000000000000001
        assertRegEq ri R2 1u "SMULL lo = 1"
        assertRegEq ri R3 0u "SMULL hi = 0"
    printfn ""

    // --- Test 24: SMULL vs UMULL difference ---
    printfn "Test 24: SMULL vs UMULL for 0xFFFFFFFF * 0xFFFFFFFF"
    let lines24 = [
        "        LDR R0, =0xFFFFFFFF"
        "        LDR R1, =0xFFFFFFFF"
        "        UMULL R2, R3, R0, R1"
        "        SMULL R4, R5, R0, R1"
        "        END"
    ]
    match runProgram lines24 100L with
    | None -> fail "Test 24" "Program failed to run"
    | Some ri ->
        // UMULL: 0xFFFFFFFF * 0xFFFFFFFF = 0xFFFFFFFE00000001 (unsigned)
        assertRegEq ri R2 0x00000001u "UMULL lo"
        assertRegEq ri R3 0xFFFFFFFEu "UMULL hi"
        // SMULL: (-1) * (-1) = 1 (signed)
        assertRegEq ri R4 0x00000001u "SMULL lo"
        assertRegEq ri R5 0x00000000u "SMULL hi"
    printfn ""

    // --- Test 25: SMULL large negative * positive ---
    printfn "Test 25: SMULL 0x80000000 * 2 (MIN_INT * 2)"
    let lines25 = [
        "        LDR R0, =0x80000000"
        "        MOV R1, #2"
        "        SMULL R2, R3, R0, R1"
        "        END"
    ]
    match runProgram lines25 100L with
    | None -> fail "Test 25" "Program failed to run"
    | Some ri ->
        // 0x80000000 as signed = -2147483648
        // -2147483648 * 2 = -4294967296 = 0xFFFFFFFF00000000
        assertRegEq ri R2 0x00000000u "SMULL lo = 0"
        assertRegEq ri R3 0xFFFFFFFFu "SMULL hi = 0xFFFFFFFF"
    printfn ""

    // --- Test 26: SMULLS sets N flag ---
    printfn "Test 26: SMULLS sets N flag for negative 64-bit result"
    let lines26 = [
        "        LDR R0, =0xFFFFFFFF"
        "        MOV R1, #3"
        "        SMULLS R2, R3, R0, R1"
        "        END"
    ]
    match runProgram lines26 100L with
    | None -> fail "Test 26" "Program failed to run"
    | Some ri ->
        assertFlagEq ri "N" (fun f -> f.N) true "N flag set (negative 64-bit result)"
        assertFlagEq ri "Z" (fun f -> f.Z) false "Z flag clear"
    printfn ""

    // =========================================================================
    // SMLAL Tests
    // =========================================================================

    // --- Test 27: SMLAL basic ---
    printfn "Test 27: SMLAL basic accumulate"
    let lines27 = [
        "        MOV R0, #10"
        "        MOV R1, #20"
        "        MOV R2, #5"
        "        MOV R3, #0"
        "        SMLAL R2, R3, R0, R1"
        "        END"
    ]
    match runProgram lines27 100L with
    | None -> fail "Test 27" "Program failed to run"
    | Some ri ->
        // 10 * 20 = 200, + 5 = 205
        assertRegEq ri R2 205u "SMLAL lo = 205"
        assertRegEq ri R3 0u "SMLAL hi = 0"
    printfn ""

    // --- Test 28: SMLAL with negative product ---
    printfn "Test 28: SMLAL negative product + positive accumulator"
    let lines28 = [
        "        LDR R0, =0xFFFFFFFF"
        "        MOV R1, #100"
        "        LDR R2, =200"
        "        MOV R3, #0"
        "        SMLAL R2, R3, R0, R1"
        "        END"
    ]
    match runProgram lines28 100L with
    | None -> fail "Test 28" "Program failed to run"
    | Some ri ->
        // (-1) * 100 = -100, as 64-bit = 0xFFFFFFFFFFFFFF9C
        // + 200 (64-bit = 0x00000000000000C8) = 0x0000000000000064 = 100
        assertRegEq ri R2 100u "SMLAL lo = 100"
        assertRegEq ri R3 0u "SMLAL hi = 0"
    printfn ""

    // --- Test 29: SMLAL negative accumulator ---
    printfn "Test 29: SMLAL with negative 64-bit accumulator"
    let lines29 = [
        "        MOV R0, #1"
        "        MOV R1, #1"
        "        LDR R2, =0xFFFFFFFE"
        "        LDR R3, =0xFFFFFFFF"
        "        SMLAL R2, R3, R0, R1"
        "        END"
    ]
    match runProgram lines29 100L with
    | None -> fail "Test 29" "Program failed to run"
    | Some ri ->
        // Accumulator = 0xFFFFFFFFFFFFFFFE = -2
        // Product = 1*1 = 1
        // -2 + 1 = -1 = 0xFFFFFFFFFFFFFFFF
        assertRegEq ri R2 0xFFFFFFFFu "SMLAL lo = 0xFFFFFFFF"
        assertRegEq ri R3 0xFFFFFFFFu "SMLAL hi = 0xFFFFFFFF"
    printfn ""

    // =========================================================================
    // Conditional Execution Tests
    // =========================================================================

    // --- Test 30: Conditional MUL (true) ---
    printfn "Test 30: Conditional MULEQ (condition true)"
    let lines30 = [
        "        MOV R0, #0"
        "        CMP R0, #0"
        "        MOV R1, #5"
        "        MOV R2, #6"
        "        MOV R3, #0"
        "        MULEQ R3, R1, R2"
        "        END"
    ]
    match runProgram lines30 100L with
    | None -> fail "Test 30" "Program failed to run"
    | Some ri ->
        assertRegEq ri R3 30u "MULEQ executes (5*6=30)"
    printfn ""

    // --- Test 31: Conditional MUL (false) ---
    printfn "Test 31: Conditional MULNE (condition false)"
    let lines31 = [
        "        MOV R0, #0"
        "        CMP R0, #0"
        "        MOV R1, #5"
        "        MOV R2, #6"
        "        MOV R3, #99"
        "        MULNE R3, R1, R2"
        "        END"
    ]
    match runProgram lines31 100L with
    | None -> fail "Test 31" "Program failed to run"
    | Some ri ->
        assertRegEq ri R3 99u "MULNE skipped, R3 unchanged"
    printfn ""

    // --- Test 32: Conditional UMULL ---
    printfn "Test 32: Conditional UMULLEQ (condition true)"
    let lines32 = [
        "        MOV R0, #0"
        "        CMP R0, #0"
        "        MOV R1, #10"
        "        MOV R2, #20"
        "        UMULLEQ R4, R5, R1, R2"
        "        END"
    ]
    match runProgram lines32 100L with
    | None -> fail "Test 32" "Program failed to run"
    | Some ri ->
        assertRegEq ri R4 200u "UMULLEQ lo = 200"
        assertRegEq ri R5 0u "UMULLEQ hi = 0"
    printfn ""

    // =========================================================================
    // Parse Error Tests (constraint validation)
    // =========================================================================

    // --- Test 33: MUL Rd == Rm rejected ---
    printfn "Test 33: MUL Rd == Rm rejected"
    let lines33 = [
        "        MUL R0, R0, R1"
        "        END"
    ]
    assertParseError lines33 "MUL Rd==Rm rejected"
    printfn ""

    // --- Test 34: MLA Rd == Rm rejected ---
    printfn "Test 34: MLA Rd == Rm rejected"
    let lines34 = [
        "        MLA R0, R0, R1, R2"
        "        END"
    ]
    assertParseError lines34 "MLA Rd==Rm rejected"
    printfn ""

    // --- Test 35: UMULL RdLo == RdHi rejected ---
    printfn "Test 35: UMULL RdLo == RdHi rejected"
    let lines35 = [
        "        UMULL R0, R0, R1, R2"
        "        END"
    ]
    assertParseError lines35 "UMULL RdLo==RdHi rejected"
    printfn ""

    // --- Test 36: SMULL RdLo == Rm rejected ---
    printfn "Test 36: SMULL RdLo == Rm rejected"
    let lines36 = [
        "        SMULL R1, R2, R1, R3"
        "        END"
    ]
    assertParseError lines36 "SMULL RdLo==Rm rejected"
    printfn ""

    // --- Test 37: SMULL RdHi == Rm rejected ---
    printfn "Test 37: SMULL RdHi == Rm rejected"
    let lines37 = [
        "        SMULL R1, R2, R2, R3"
        "        END"
    ]
    assertParseError lines37 "SMULL RdHi==Rm rejected"
    printfn ""

    // --- Test 38: MUL with PC rejected ---
    printfn "Test 38: MUL with PC rejected"
    let lines38 = [
        "        MUL R15, R1, R2"
        "        END"
    ]
    assertParseError lines38 "MUL with R15/PC rejected"
    printfn ""

    // --- Test 39: MUL wrong operand count ---
    printfn "Test 39: MUL wrong operand count rejected"
    let lines39 = [
        "        MUL R0, R1"
        "        END"
    ]
    assertParseError lines39 "MUL with 2 operands rejected"
    printfn ""

    // --- Test 40: UMULL wrong operand count ---
    printfn "Test 40: UMULL wrong operand count rejected"
    let lines40 = [
        "        UMULL R0, R1, R2"
        "        END"
    ]
    assertParseError lines40 "UMULL with 3 operands rejected"
    printfn ""

    // =========================================================================
    // Edge Cases
    // =========================================================================

    // --- Test 41: MUL max positive * max positive ---
    printfn "Test 41: MUL 0x7FFFFFFF * 0x7FFFFFFF (truncated)"
    let lines41 = [
        "        LDR R0, =0x7FFFFFFF"
        "        LDR R1, =0x7FFFFFFF"
        "        MUL R2, R0, R1"
        "        END"
    ]
    match runProgram lines41 100L with
    | None -> fail "Test 41" "Program failed to run"
    | Some ri ->
        // 0x7FFFFFFF * 0x7FFFFFFF = 0x3FFFFFFF00000001
        // low 32 bits = 0x00000001
        assertRegEq ri R2 0x00000001u "MUL lo bits of 0x7FFFFFFF^2"
    printfn ""

    // --- Test 42: UMULL 0x7FFFFFFF * 0x7FFFFFFF full 64-bit ---
    printfn "Test 42: UMULL 0x7FFFFFFF * 0x7FFFFFFF"
    let lines42 = [
        "        LDR R0, =0x7FFFFFFF"
        "        LDR R1, =0x7FFFFFFF"
        "        UMULL R2, R3, R0, R1"
        "        END"
    ]
    match runProgram lines42 100L with
    | None -> fail "Test 42" "Program failed to run"
    | Some ri ->
        // 0x7FFFFFFF * 0x7FFFFFFF = 0x3FFFFFFF00000001
        assertRegEq ri R2 0x00000001u "UMULL lo = 0x00000001"
        assertRegEq ri R3 0x3FFFFFFFu "UMULL hi = 0x3FFFFFFF"
    printfn ""

    // --- Test 43: SMULL 0x80000000 * 0x80000000 ---
    printfn "Test 43: SMULL MIN_INT * MIN_INT"
    let lines43 = [
        "        LDR R0, =0x80000000"
        "        LDR R1, =0x80000000"
        "        SMULL R2, R3, R0, R1"
        "        END"
    ]
    match runProgram lines43 100L with
    | None -> fail "Test 43" "Program failed to run"
    | Some ri ->
        // 0x80000000 as signed = -2147483648
        // (-2147483648) * (-2147483648) = 4611686018427387904 = 0x4000000000000000
        assertRegEq ri R2 0x00000000u "SMULL lo = 0"
        assertRegEq ri R3 0x40000000u "SMULL hi = 0x40000000"
    printfn ""

    // --- Test 44: UMLAL accumulate with carry propagation ---
    printfn "Test 44: UMLAL carry propagation from lo to hi"
    let lines44 = [
        "        LDR R0, =0x80000000"
        "        MOV R1, #2"
        "        LDR R2, =0x80000000"
        "        MOV R3, #0"
        "        UMLAL R2, R3, R0, R1"
        "        END"
    ]
    match runProgram lines44 100L with
    | None -> fail "Test 44" "Program failed to run"
    | Some ri ->
        // 0x80000000 * 2 = 0x100000000
        // + 0x0000000080000000 = 0x0000000180000000
        assertRegEq ri R2 0x80000000u "UMLAL lo = 0x80000000"
        assertRegEq ri R3 1u "UMLAL hi = 1"
    printfn ""

    // --- Test 45: SMLAL sign matters ---
    printfn "Test 45: SMLAL vs UMLAL difference with signed operands"
    let lines45 = [
        "        LDR R0, =0xFFFFFFFF"
        "        MOV R1, #2"
        "        MOV R2, #0"
        "        MOV R3, #0"
        "        SMLAL R2, R3, R0, R1"
        "        MOV R4, #0"
        "        MOV R5, #0"
        "        UMLAL R4, R5, R0, R1"
        "        END"
    ]
    match runProgram lines45 100L with
    | None -> fail "Test 45" "Program failed to run"
    | Some ri ->
        // SMLAL: signed -1 * 2 = -2 = 0xFFFFFFFFFFFFFFFE
        assertRegEq ri R2 0xFFFFFFFEu "SMLAL lo = 0xFFFFFFFE"
        assertRegEq ri R3 0xFFFFFFFFu "SMLAL hi = 0xFFFFFFFF"
        // UMLAL: unsigned 0xFFFFFFFF * 2 = 0x1FFFFFFFE
        assertRegEq ri R4 0xFFFFFFFEu "UMLAL lo = 0xFFFFFFFE"
        assertRegEq ri R5 0x00000001u "UMLAL hi = 0x00000001"
    printfn ""

    // --- Test 46: MUL commutativity ---
    printfn "Test 46: MUL commutativity (a*b == b*a)"
    let lines46 = [
        "        MOV R0, #13"
        "        MOV R1, #17"
        "        MUL R2, R0, R1"
        "        MUL R3, R1, R0"
        "        END"
    ]
    match runProgram lines46 100L with
    | None -> fail "Test 46" "Program failed to run"
    | Some ri ->
        assertRegEq ri R2 221u "MUL 13*17=221"
        assertRegEq ri R3 221u "MUL 17*13=221 (commutative)"
    printfn ""

    // --- Test 47: Multiple MLA calls (polynomial) ---
    printfn "Test 47: MLA chain for polynomial evaluation"
    let lines47 = [
        "        MOV R0, #3"
        "        MOV R1, #2"
        "        MOV R2, #1"
        "        MLA R3, R0, R0, R1"
        "        MLA R4, R3, R0, R2"
        "        END"
    ]
    match runProgram lines47 100L with
    | None -> fail "Test 47" "Program failed to run"
    | Some ri ->
        // R3 = 3*3+2 = 11
        // R4 = 11*3+1 = 34
        assertRegEq ri R3 11u "MLA R3=3*3+2=11"
        assertRegEq ri R4 34u "MLA R4=11*3+1=34"
    printfn ""

    // --- Test 48: SMULL with 0x80000000 * -1 ---
    printfn "Test 48: SMULL MIN_INT * -1 (overflow edge case)"
    let lines48 = [
        "        LDR R0, =0x80000000"
        "        LDR R1, =0xFFFFFFFF"
        "        SMULL R2, R3, R0, R1"
        "        END"
    ]
    match runProgram lines48 100L with
    | None -> fail "Test 48" "Program failed to run"
    | Some ri ->
        // -2147483648 * (-1) = 2147483648 = 0x0000000080000000
        assertRegEq ri R2 0x80000000u "SMULL lo = 0x80000000"
        assertRegEq ri R3 0x00000000u "SMULL hi = 0x00000000"
    printfn ""

    // --- Test 49: UMULL 1 * 1 ---
    printfn "Test 49: UMULL 1 * 1 = 1"
    let lines49 = [
        "        MOV R0, #1"
        "        MOV R1, #1"
        "        UMULL R2, R3, R0, R1"
        "        END"
    ]
    match runProgram lines49 100L with
    | None -> fail "Test 49" "Program failed to run"
    | Some ri ->
        assertRegEq ri R2 1u "UMULL lo = 1"
        assertRegEq ri R3 0u "UMULL hi = 0"
    printfn ""

    // --- Test 50: SMULLS zero result sets Z ---
    printfn "Test 50: SMULLS zero result"
    let lines50 = [
        "        MOV R0, #0"
        "        MOV R1, #100"
        "        SMULLS R2, R3, R0, R1"
        "        END"
    ]
    match runProgram lines50 100L with
    | None -> fail "Test 50" "Program failed to run"
    | Some ri ->
        assertRegEq ri R2 0u "SMULLS lo = 0"
        assertRegEq ri R3 0u "SMULLS hi = 0"
        assertFlagEq ri "Z" (fun f -> f.Z) true "Z flag set"
        assertFlagEq ri "N" (fun f -> f.N) false "N flag clear"
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
