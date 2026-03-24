(*
    VisUAL2 @ Imperial College London
    Project: A user-friendly ARM emulator in F# and Web Technologies ( Github Electron & Fable Compiler )
    Module: Emulator.Branch
    Description: Implement ARM branch instructions
*)

/// emulate ARM branch instructions
module Branch
    open CommonData
    open CommonLex
    open Expressions
    open Errors
    open Helpers

    type Instr =
        | B of uint32
        | BL of uint32
        | BX of RName
        | BLX of RName
        | END

    let branchTarget = function | B t | BL t -> [ t ] | BX _ | BLX _ -> [] | END -> []

    // Branch instructions have no suffixes
    let branchSpec = {
        InstrC = BRANCH
        Roots = [ "B"; "BL"; "BX"; "BLX"; "END" ]
        Suffixes = [ "" ]
    }

    /// map of all possible opcodes recognised
    let opCodes = opCodeExpand branchSpec

    let parse (ls : LineData) : Parse<Instr> option =
        let parse' (_iClass, (root, suffix, cond)) =
            let (WA la) = ls.LoadAddr // address this instruction is loaded into memory
            match root with
            | "BX" | "BLX" ->
                match parseRegister (ls.Operands.Trim()) with
                | Ok rn when rn = R15 ->
                    makeParseError "register (not PC)" ls.Operands ""
                | Ok rn ->
                    (if root = "BX" then BX rn else BLX rn) |> Ok
                | Error e -> Error e
            | _ ->
                let target = parseEvalNumericExpression ls.SymTab ls.Operands
                match root, target with
                | "B", Ok t -> B t |> Ok
                | "BL", Ok t -> BL t |> Ok
                | "B", Error t
                | "BL", Error t -> Error t
                | "END", _ when ls.Operands = "" -> Ok END
                | "END", _ -> makeParseError "END (no operands)" ls.Operands "list#pseudo-instructions-and-directives"
                | _ -> failwithf "What? Unexpected root in Branch.parse'%s'" root
            |> fun ins ->
                copyParse ls ins cond
                |> (fun pa -> { pa with PStall = match root with | "B" | "BL" | "BX" | "BLX" -> 2 | _ -> 0 })
        let OPC = ls.OpCode.ToUpper()
        if Map.containsKey OPC opCodes // lookup opcode to see if it is known
        then parse' opCodes.[OPC] |> Some
        else None

    /// Parse Active Pattern used by top-level code
    let (|IMatch|_|) = parse


    /// Execute a Branch instruction
    let executeBranch ins cpuData =
        let nxt = getPC cpuData + 4u // Address of the next instruction
        match ins with
        | B addr ->
            cpuData
            |> setReg R15 addr
            |> Ok
        | BL addr ->
            cpuData
            |> setReg R15 addr
            |> setReg R14 nxt
            |> Ok
        | BX rm ->
            let addr = cpuData.Regs.[rm] &&& 0xFFFFFFFEu // mask off Thumb bit
            cpuData
            |> setReg R15 addr
            |> Ok
        | BLX rm ->
            let addr = cpuData.Regs.[rm] &&& 0xFFFFFFFEu // mask off Thumb bit
            cpuData
            |> setReg R15 addr
            |> setReg R14 nxt
            |> Ok
        | END ->
            EXIT |> Error


