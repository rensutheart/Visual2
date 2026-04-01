(*
    VisUAL2 @ Imperial College London
    Project: A user-friendly ARM emulator in F# and Web Technologies ( Github Electron & Fable Compiler )
    Module: Emulator.Multiply
    Description: Implement ARM multiply instructions
*)

/// emulate ARM multiply instructions
module Multiply
    open CommonData
    open CommonLex
    open Errors
    open Helpers
    open DP

    /// 32-bit multiply operations
    type MulOp = | MUL | MLA

    /// 64-bit (long) multiply operations
    type LongMulOp = | UMULL | UMLAL | SMULL | SMLAL

    /// Division operations
    type DivOp = | SDIV | UDIV

    /// Multiply instruction type
    type Instr =
        /// MUL Rd, Rm, Rs (setFlags)
        | Mul of Rd : RName * Rm : RName * Rs : RName * SetFlags : bool
        /// MLA Rd, Rm, Rs, Rn (setFlags)
        | Mla of Rd : RName * Rm : RName * Rs : RName * Rn : RName * SetFlags : bool
        /// UMULL/UMLAL/SMULL/SMLAL RdLo, RdHi, Rm, Rs (setFlags)
        | LongMul of Op : LongMulOp * RdLo : RName * RdHi : RName * Rm : RName * Rs : RName * SetFlags : bool
        /// SDIV/UDIV Rd, Rn, Rm
        | Div of Op : DivOp * Rd : RName * Rn : RName * Rm : RName

    let mulSpec = {
        InstrC = DP
        Roots = [ "MUL"; "MLA"; "UMULL"; "UMLAL"; "SMULL"; "SMLAL" ]
        Suffixes = [ ""; "S" ]
    }

    let divSpec = {
        InstrC = DP
        Roots = [ "SDIV"; "UDIV" ]
        Suffixes = [ "" ]
    }

    /// map of all possible opcodes recognised
    let opCodes = opCodeExpand mulSpec |> Map.fold (fun acc k v -> Map.add k v acc) (opCodeExpand divSpec)

    /// Parse a multiply instruction
    let parse (ls : LineData) : Parse<Instr> option =
        let parse' (_iClass, (root, suffix, cond)) =
            let (WA la) = ls.LoadAddr
            let operands = ls.Operands.Split(',') |> Array.toList |> List.map (fun s -> s.Trim())
            let setFlags = (suffix = "S")

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
                | "MUL", [ rd; rm; rs ] ->
                    parseRegs [ rd; rm; rs ]
                    |> Result.bind validateNoPC
                    |> Result.bind (fun regs ->
                        let rdR, rmR, rsR = regs.[0], regs.[1], regs.[2]
                        if rdR = rmR then
                            makeParseError "Rd different from Rm" (sprintf "%A = %A" rdR rmR) ""
                        else
                            Ok (Mul(rdR, rmR, rsR, setFlags)))
                | "MLA", [ rd; rm; rs; rn ] ->
                    parseRegs [ rd; rm; rs; rn ]
                    |> Result.bind validateNoPC
                    |> Result.bind (fun regs ->
                        let rdR, rmR, rsR, rnR = regs.[0], regs.[1], regs.[2], regs.[3]
                        if rdR = rmR then
                            makeParseError "Rd different from Rm" (sprintf "%A = %A" rdR rmR) ""
                        else
                            Ok (Mla(rdR, rmR, rsR, rnR, setFlags)))
                | ("UMULL" | "UMLAL" | "SMULL" | "SMLAL"), [ rdLo; rdHi; rm; rs ] ->
                    parseRegs [ rdLo; rdHi; rm; rs ]
                    |> Result.bind validateNoPC
                    |> Result.bind (fun regs ->
                        let rdLoR, rdHiR, rmR, rsR = regs.[0], regs.[1], regs.[2], regs.[3]
                        if rdLoR = rdHiR then
                            makeParseError "RdLo different from RdHi" (sprintf "%A = %A" rdLoR rdHiR) ""
                        elif rdLoR = rmR || rdHiR = rmR then
                            makeParseError "RdLo and RdHi different from Rm" (sprintf "Rm=%A" rmR) ""
                        else
                            let op =
                                match root with
                                | "UMULL" -> UMULL
                                | "UMLAL" -> UMLAL
                                | "SMULL" -> SMULL
                                | "SMLAL" -> SMLAL
                                | _ -> failwithf "What? Unexpected root %s" root
                            Ok (LongMul(op, rdLoR, rdHiR, rmR, rsR, setFlags)))
                | ("SDIV" | "UDIV"), [ rd; rn; rm ] ->
                    parseRegs [ rd; rn; rm ]
                    |> Result.bind validateNoPC
                    |> Result.bind (fun regs ->
                        let rdR, rnR, rmR = regs.[0], regs.[1], regs.[2]
                        let op = match root with | "SDIV" -> SDIV | _ -> UDIV
                        Ok (Div(op, rdR, rnR, rmR)))
                | _, _ ->
                    let expected =
                        match root with
                        | "MUL" -> "MUL Rd, Rm, Rs"
                        | "MLA" -> "MLA Rd, Rm, Rs, Rn"
                        | "SDIV" | "UDIV" -> sprintf "%s Rd, Rn, Rm" root
                        | _ -> sprintf "%s RdLo, RdHi, Rm, Rs" root
                    makeParseError expected (String.concat "," operands) ""

            { pI with PInstr = ins }

        let OPC = ls.OpCode.ToUpper()
        if Map.containsKey OPC opCodes
        then parse' opCodes.[OPC] |> Some
        else None

    /// Parse Active Pattern used by top-level code
    let (|IMatch|_|) = parse

    /// Execute a multiply instruction
    let executeMul instr (dp : DataPath) =
        let getReg r = dp.Regs.[r]
        match instr with
        | Mul(rd, rm, rs, sf) ->
            let result = (getReg rm) * (getReg rs)
            let newFl = { dp.Fl with N = setFlagN result; Z = setFlagZ result }
            let ufl = { toUFlags dp.Fl with F = newFl; NZU = sf }
            Ok ({ setReg rd result dp with Fl = if sf then newFl else dp.Fl }, ufl)

        | Mla(rd, rm, rs, rn, sf) ->
            let result = (getReg rm) * (getReg rs) + (getReg rn)
            let newFl = { dp.Fl with N = setFlagN result; Z = setFlagZ result }
            let ufl = { toUFlags dp.Fl with F = newFl; NZU = sf }
            Ok ({ setReg rd result dp with Fl = if sf then newFl else dp.Fl }, ufl)

        | LongMul(op, rdLo, rdHi, rm, rs, sf) ->
            let isSigned = match op with | SMULL | SMLAL -> true | _ -> false
            let isAccumulate = match op with | UMLAL | SMLAL -> true | _ -> false
            let a, b =
                if isSigned then int64 (int32 (getReg rm)), int64 (int32 (getReg rs))
                else int64 (getReg rm), int64 (getReg rs)
            let product = a * b
            let acc =
                if isAccumulate then
                    ((int64 (getReg rdHi)) <<< 32) ||| (int64 (getReg rdLo) &&& 0xFFFFFFFFL)
                else 0L
            let result = product + acc
            let lo = uint32 (result &&& 0xFFFFFFFFL)
            let hi = uint32 ((result >>> 32) &&& 0xFFFFFFFFL)
            let newFl = { dp.Fl with N = hi > 0x7FFFFFFFu; Z = (lo = 0u && hi = 0u) }
            let ufl = { toUFlags dp.Fl with F = newFl; NZU = sf }
            let dp' = dp |> setReg rdLo lo |> setReg rdHi hi
            Ok ({ dp' with Fl = if sf then newFl else dp.Fl }, ufl)

        | Div(op, rd, rn, rm) ->
            let divisor = getReg rm
            let result =
                if divisor = 0u then
                    0u // ARM division by zero returns 0
                else
                    match op with
                    | SDIV -> uint32 (int32 (getReg rn) / int32 divisor)
                    | UDIV -> (getReg rn) / divisor
            let ufl = toUFlags dp.Fl // no flag changes
            Ok (setReg rd result dp, ufl)
