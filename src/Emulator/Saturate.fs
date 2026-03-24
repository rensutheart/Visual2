(*
    VisUAL2 @ Imperial College London
    Project: A user-friendly ARM emulator in F# and Web Technologies ( Github Electron & Fable Compiler )
    Module: Emulator.Saturate
    Description: Implement ARM saturating arithmetic instructions (QADD, QSUB)
*)

/// emulate ARM saturating arithmetic instructions
module Saturate
    open CommonData
    open CommonLex
    open Errors
    open Helpers

    /// Saturating arithmetic operations
    type SatOp = | QADD | QSUB

    /// Saturating arithmetic instruction type
    type Instr =
        /// QADD/QSUB Rd, Rm, Rn
        | SatArith of Op : SatOp * Rd : RName * Rm : RName * Rn : RName

    let satSpec = {
        InstrC = MEM  // Use MEM class since we return Result<DataPath, ExecuteError> (no flag changes)
        Roots = [ "QADD"; "QSUB" ]
        Suffixes = [ "" ]
    }

    /// map of all possible opcodes recognised
    let opCodes = opCodeExpand satSpec

    /// INT32 bounds for saturation
    let int32Max = 2147483647L   // 0x7FFFFFFF
    let int32Min = -2147483648L  // 0x80000000

    /// Saturate a 64-bit value to signed 32-bit range
    let saturate (value : int64) : uint32 =
        if value > int32Max then 0x7FFFFFFFu
        elif value < int32Min then 0x80000000u
        else uint32 (int32 value)

    /// Parse a saturating arithmetic instruction
    let parse (ls : LineData) : Parse<Instr> option =
        let parse' (_iClass, (root, _suffix, cond)) =
            let (WA la) = ls.LoadAddr
            let operands = ls.Operands.Split(',') |> Array.toList |> List.map (fun s -> s.Trim())

            let pI = {
                PInstr = Error ``Unimplemented parse``
                PLabel = ls.Label |> Option.map (fun lab -> lab, Ok la)
                ISize = 4u
                DSize = Some 0u
                PCond = cond
                POpCode = ls.OpCode
                PStall = 0
            }

            let parseRegs (ops : string list) =
                ops |> List.map parseRegister |> condenseResultList id

            let validateNoPC (regs : RName list) =
                if List.contains R15 regs then
                    makeParseError "registers (not PC/R15)" "R15" ""
                else Ok regs

            let ins =
                match root, operands with
                | ("QADD" | "QSUB"), [ rd; rm; rn ] ->
                    parseRegs [ rd; rm; rn ]
                    |> Result.bind validateNoPC
                    |> Result.bind (fun regs ->
                        let rdR, rmR, rnR = regs.[0], regs.[1], regs.[2]
                        let op = match root with | "QADD" -> QADD | _ -> QSUB
                        Ok (SatArith(op, rdR, rmR, rnR)))
                | _, _ ->
                    makeParseError (sprintf "%s Rd, Rm, Rn" root) (String.concat "," operands) ""

            { pI with PInstr = ins }

        let OPC = ls.OpCode.ToUpper()
        if Map.containsKey OPC opCodes
        then parse' opCodes.[OPC] |> Some
        else None

    /// Parse Active Pattern used by top-level code
    let (|IMatch|_|) = parse

    /// Execute a saturating arithmetic instruction
    /// QADD/QSUB do not affect N, Z, C, V flags (they only set Q flag which is not modelled)
    let executeSat instr (dp : DataPath) =
        let getReg r = dp.Regs.[r]
        match instr with
        | SatArith(op, rd, rm, rn) ->
            let rmVal = int64 (int32 (getReg rm))
            let rnVal = int64 (int32 (getReg rn))
            let result =
                match op with
                | QADD -> rmVal + rnVal
                | QSUB -> rmVal - rnVal
            let saturated = saturate result
            Ok (setReg rd saturated dp)
