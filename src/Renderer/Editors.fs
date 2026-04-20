(*
    VisUAL2 @ Imperial College London
    Project: A user-friendly ARM emulator in F# and Web Technologies ( Github Electron & Fable Compiler )
    Module: Renderer.Editors
    Description: Interface with Monaco editor buffers
*)

/// Interface with monaco editor buffers
module Editors

open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.Browser
open Fable.Core
open EEExtensions
open Refs
open Tooltips

open CommonData
open Memory

let editorOptions (readOnly : bool) =
    let vs = Refs.vSettings
    createObj [

                        // User defined settings
                        "theme" ==> vs.EditorTheme
                        "renderWhitespace" ==> vs.EditorRenderWhitespace
                        "fontSize" ==> vs.EditorFontSize
                        "wordWrap" ==> vs.EditorWordWrap

                        // Application defined settings
                        "value" ==> "";
                        "renderIndentGuides" ==> false
                        "fontFamily" ==> "fira-code"
                        "fontWeight" ==> "bold"
                        "language" ==> "arm";
                        "roundedSelection" ==> false;
                        "scrollBeyondLastLine" ==> false;
                        "readOnly" ==> readOnly;
                        "automaticLayout" ==> true;
                        "minimap" ==> createObj [ "enabled" ==> false ];
                        "glyphMargin" ==> true
                        "renderLineHighlight" ==> (if readOnly then "none" else "all")
              ]


let updateEditor tId readOnly =
    if tId <> -1 then
        let eo = editorOptions readOnly
        Refs.editors.[tId]?updateOptions (eo) |> ignore

let setTheme theme =
    window?monaco?editor?setTheme (theme)


let updateAllEditors readOnly =
    Refs.editors
    |> Map.iter (fun tId _ -> if tId = Refs.currentFileTabId then readOnly else false
                              |> updateEditor tId)
    let theme = Refs.vSettings.EditorTheme
    Refs.setFilePaneBackground (
        match theme with
        | "one-light-pro" -> "#EDF9FA"
        | "solarised-light" -> "#fdf6e3"
        | "solarised-dark" -> "#002b36"
        | "one-dark-pro" -> "#1E1E1E"
        | "visual-classic" | _ -> "#272822")
    setTheme (theme) |> ignore
    setCustomCSS "--editor-font-size" (sprintf "%spx" vSettings.EditorFontSize)


/// Timer ID for auto-hiding the reset hint toast
let mutable private resetHintTimerId : float option = None

/// Show a temporary "Reset to edit" toast when user tries to type in read-only editor
let showResetHintToast () =
    let toast = Refs.getHtml "editor-readonly-toast"
    toast.classList.remove ("invisible")
    // Cancel any existing hide timer
    match resetHintTimerId with
    | Some id -> Browser.window.clearTimeout id
    | None -> ()
    // Auto-hide after 2 seconds
    resetHintTimerId <-
        Some (Browser.window.setTimeout (
            (fun () ->
                toast.classList.add ("invisible")
                resetHintTimerId <- None),
            2000, []))

/// Install a document-level keydown listener that shows the reset hint
/// when the user tries to type while the editor is read-only.
let installReadonlyKeyHandler () =
    Browser.document.addEventListener_keydown (fun e ->
        if not (Refs.darkenOverlay.classList.contains "invisible") then
            let key = e.key
            // Only trigger on printable characters, not modifiers/navigation
            if key.Length = 1 || key = "Backspace" || key = "Delete" || key = "Enter" || key = "Tab" then
                showResetHintToast ()
        createObj [])

let mutable decorations : obj list = []
let mutable lineDecorations : obj list = []

/// Breakpoint decoration handles: lineNo -> decoration handle
let mutable bpDecorationHandles : Map<int, obj> = Map.empty

[<Emit "new monaco.Range($0,$1,$2,$3)">]
let monacoRange _ _ _ _ = jsNative

[<Emit "$0.deltaDecorations($1, [
    { range: $2, options: $3},
  ]);">]
let lineDecoration _editor _decorations _range _name = jsNative

[<Emit "$0.deltaDecorations($1, [{ range: new monaco.Range(1,1,1,1), options : { } }]);">]
let removeDecorations _editor _decorations =
    jsNative

/// Set of line numbers that currently have execution highlights
let mutable executionHighlightedLines : Set<int> = Set.empty

/// Hover decoration handle (separate from execution decorations)
let mutable private hoverDecoration : obj list = []
/// Disposables for hover event handlers
let mutable private hoverDisposables : obj list = []

/// Mutable reference to the branch arrow SVG overlay element
let mutable private branchArrowOverlay : Browser.HTMLElement option = None

/// Remove any existing branch arrow overlay
let removeBranchArrowOverlay () =
    match branchArrowOverlay with
    | Some el ->
        el.parentNode.removeChild el |> ignore
        branchArrowOverlay <- None
    | None -> ()

// Remove all text decorations associated with an editor (NOT breakpoint decorations)
let removeEditorDecorations tId =
    if tId <> -1 then
        List.iter (fun x -> removeDecorations Refs.editors.[tId] x) decorations
        decorations <- []
        executionHighlightedLines <- Set.empty
    removeBranchArrowOverlay ()

/// Add a breakpoint glyph decoration to a line
let addBreakpointGlyph tId lineNo =
    let editor = Refs.editors.[tId]
    let handle = lineDecoration editor
                    []
                    (monacoRange lineNo 1 lineNo 1)
                    (createObj [
                        "isWholeLine" ==> true
                        "glyphMarginClassName" ==> "editor-glyph-margin-breakpoint"
                        "className" ==> "editor-line-breakpoint"
                        "marginClassName" ==> "editor-line-breakpoint-margin"
                    ])
    bpDecorationHandles <- Map.add lineNo handle bpDecorationHandles

/// Remove a breakpoint glyph decoration from a line
let removeBreakpointGlyph tId lineNo =
    match Map.tryFind lineNo bpDecorationHandles with
    | Some handle ->
        removeDecorations Refs.editors.[tId] handle
        bpDecorationHandles <- Map.remove lineNo bpDecorationHandles
    | None -> ()

/// Remove all breakpoint decorations
let removeAllBreakpointGlyphs tId =
    if tId <> -1 then
        bpDecorationHandles |> Map.iter (fun _ handle ->
            removeDecorations Refs.editors.[tId] handle)
        bpDecorationHandles <- Map.empty

/// Reapply all breakpoint decorations for a tab (e.g. after editor content changes)
let reapplyBreakpointDecorations tId =
    removeAllBreakpointGlyphs tId
    Refs.getBreakpoints tId |> Set.iter (addBreakpointGlyph tId)

/// Toggle a breakpoint on a given line (only if the line contains code)
let toggleBreakpoint tId lineNo =
    let current = Refs.getBreakpoints tId
    if Set.contains lineNo current then
        Refs.breakpoints <- Map.add tId (Set.remove lineNo current) Refs.breakpoints
        removeBreakpointGlyph tId lineNo
    else
        let editor = Refs.editors.[tId]
        let model = editor?getModel ()
        let lineText : string = model?getLineContent (lineNo)
        // Strip comments: /* ... */, //, and ;
        let stripped =
            let mutable s = lineText
            // Remove /* ... */ block comments
            while s.Contains("/*") && s.Contains("*/") do
                let startIdx = s.IndexOf("/*")
                let endIdx = s.IndexOf("*/")
                if endIdx > startIdx then
                    s <- s.[..startIdx-1] + s.[endIdx+2..]
            // Remove // line comment
            match s.IndexOf("//") with
            | i when i >= 0 -> s <- s.[..i-1]
            | _ -> ()
            // Remove ; line comment
            match s.IndexOf(";") with
            | i when i >= 0 -> s <- s.[..i-1]
            | _ -> ()
            s.Trim()
        // A label-only line is a single token starting with a letter, rest alphanumeric/underscore
        let isLabelOnly (s : string) =
            s.Length > 0 &&
            not (s.Contains(" ") || s.Contains("\t")) &&
            System.Char.IsLetter s.[0] &&
            s |> Seq.forall (fun ch -> System.Char.IsLetterOrDigit ch || ch = '_')
        if stripped <> "" && not (isLabelOnly stripped) then
            Refs.breakpoints <- Map.add tId (Set.add lineNo current) Refs.breakpoints
            addBreakpointGlyph tId lineNo

let editorLineDecorate editor number decoration (rangeOpt : (int * int) option) =
    let model = editor?getModel ()
    let lineWidth = model?getLineMaxColumn (number)
    let posStart = match rangeOpt with | None -> 1 | Some(n, _) -> n
    let posEnd = match rangeOpt with | None -> lineWidth | Some(_, n) -> n
    let newDecs = lineDecoration editor
                    decorations
                    (monacoRange number posStart number posEnd)
                    decoration
    decorations <- List.append decorations [ newDecs ]

// highlight a particular line
let highlightLine tId number className =
    executionHighlightedLines <- Set.add number executionHighlightedLines
    editorLineDecorate
        Refs.editors.[tId]
        number
        (createObj [
            "isWholeLine" ==> true
            "isTrusted" ==> true
            "className" ==> className
            "marginClassName" ==> (className + "-margin")
        ])
        None

let highlightGlyph tId number glyphClassName =
    editorLineDecorate
        Refs.editors.[tId]
        number
        (createObj [
            "isWholeLine" ==> true
            "glyphMarginClassName" ==> glyphClassName
        ])
        None

let highlightNextInstruction tId number =
    if number > 0 then highlightGlyph tId number "editor-glyph-margin-arrow"

/// Draw a branch arrow on the right side of the editor from srcLine to destLine
let drawBranchArrow tId srcLine destLine =
    removeBranchArrowOverlay ()
    if tId < 0 || srcLine <= 0 || destLine <= 0 then ()
    else
        let editor = Refs.editors.[tId]
        let domNode : Browser.HTMLElement = editor?getDomNode ()
        if isNull (unbox domNode) then ()
        else
            let scrollTop : float = editor?getScrollTop ()
            let srcTop : float = editor?getTopForLineNumber (srcLine)
            let destTop : float = editor?getTopForLineNumber (destLine)
            let lineHeight : float =
                let opts = editor?getConfiguration ()
                let fontInfo = opts?fontInfo
                fontInfo?lineHeight
            // srcY = bottom edge of source line if branching down, top edge if up
            // destY = top edge of dest line if branching down, bottom edge if up
            let branchDown = destLine > srcLine
            let srcY =
                if branchDown then srcTop - scrollTop + lineHeight
                else srcTop - scrollTop
            let destY =
                if branchDown then destTop - scrollTop
                else destTop - scrollTop + lineHeight
            let layoutInfo = editor?getLayoutInfo ()
            let editorWidth : float = layoutInfo?width
            let minimapWidth : float = layoutInfo?minimapWidth
            let verticalScrollbarWidth : float = layoutInfo?verticalScrollbarWidth
            // Position the SVG line at the right edge of the code area
            let lineX = editorWidth - minimapWidth - verticalScrollbarWidth - 12.0
            let svgWidth = 24.0
            let pad = 6.0
            let minY = min srcY destY - pad
            let maxY = max srcY destY + pad
            let svgHeight = maxY - minY
            let svg = Browser.document.createElementNS ("http://www.w3.org/2000/svg", "svg")
            svg.setAttribute ("width", sprintf "%.0f" svgWidth)
            svg.setAttribute ("height", sprintf "%.0f" svgHeight)
            (svg :?> Browser.HTMLElement).style.position <- "absolute"
            (svg :?> Browser.HTMLElement).style.left <- sprintf "%.0fpx" (lineX - svgWidth / 2.0)
            (svg :?> Browser.HTMLElement).style.top <- sprintf "%.0fpx" minY
            (svg :?> Browser.HTMLElement).style.pointerEvents <- "none"
            (svg :?> Browser.HTMLElement).style.zIndex <- "5"
            let localSrcY = srcY - minY
            let localDestY = destY - minY
            let cx = svgWidth / 2.0
            // Straight line from source edge to destination edge
            let line = Browser.document.createElementNS ("http://www.w3.org/2000/svg", "line")
            line.setAttribute ("x1", sprintf "%.1f" cx)
            line.setAttribute ("y1", sprintf "%.1f" localSrcY)
            line.setAttribute ("x2", sprintf "%.1f" cx)
            line.setAttribute ("y2", sprintf "%.1f" localDestY)
            line.setAttribute ("stroke", "#3C7770")
            line.setAttribute ("stroke-width", "1.5")
            // Arrowhead pointing at destination
            let arrowSize = 5.0
            let arrowDir = if branchDown then -1.0 else 1.0
            let arrowHead = Browser.document.createElementNS ("http://www.w3.org/2000/svg", "polygon")
            let arrowPoints =
                sprintf "%.1f,%.1f %.1f,%.1f %.1f,%.1f"
                    cx localDestY
                    (cx - arrowSize) (localDestY + arrowDir * arrowSize)
                    (cx + arrowSize) (localDestY + arrowDir * arrowSize)
            arrowHead.setAttribute ("points", arrowPoints)
            arrowHead.setAttribute ("fill", "#3C7770")
            svg.appendChild line |> ignore
            svg.appendChild arrowHead |> ignore
            // Find the editor's overflow-guard container to append the SVG
            let overflowGuard = domNode.querySelector ".overflow-guard"
            if not (isNull (unbox overflowGuard)) then
                overflowGuard.appendChild svg |> ignore
                branchArrowOverlay <- Some (svg :?> Browser.HTMLElement)

let private removeHoverDecoration tId =
    if tId <> -1 && not (List.isEmpty hoverDecoration) then
        List.iter (fun x -> removeDecorations Refs.editors.[tId] x) hoverDecoration
        hoverDecoration <- []

let private installHoverHighlight tId =
    if tId <> -1 then
        let editor = Refs.editors.[tId]
        let d1 =
            editor?onMouseMove (fun (e : obj) ->
                let target = e?target
                let position = target?position
                if Refs.isUndefined position || isNull (unbox position) then
                    removeHoverDecoration tId
                else
                    let lineNo : int = position?lineNumber
                    if Set.contains lineNo executionHighlightedLines then
                        removeHoverDecoration tId
                    else
                        removeHoverDecoration tId
                        let newDec = lineDecoration editor
                                        []
                                        (monacoRange lineNo 1 lineNo 1)
                                        (createObj [
                                            "isWholeLine" ==> true
                                            "className" ==> "editor-line-hover"
                                        ])
                        hoverDecoration <- [newDec]
                createObj [])
        let d2 =
            editor?onMouseLeave (fun _ ->
                removeHoverDecoration tId
                createObj [])
        hoverDisposables <- [d1; d2]

let private removeHoverHighlight tId =
    List.iter (fun (d : obj) -> d?dispose () |> ignore) hoverDisposables
    hoverDisposables <- []
    removeHoverDecoration tId

// Disable the editor and tab selection during execution
let disableEditors() =
    Refs.fileTabMenu.classList.add ("disabled-click")
    Refs.fileTabMenu.onclick <- (fun _ ->
        showVexAlert ("Cannot change tabs during execution")
        createObj []
    )
    updateEditor Refs.currentFileTabId true
    installHoverHighlight Refs.currentFileTabId
    Refs.darkenOverlay.classList.remove ("invisible")
    Refs.darkenOverlay.classList.add ([| "disabled-click" |])

// Enable the editor once execution has completed
let enableEditors() =
    Refs.fileTabMenu.classList.remove ("disabled-click")
    Refs.fileTabMenu.onclick <- (fun _ -> createObj [])
    updateEditor Refs.currentFileTabId false
    removeHoverHighlight Refs.currentFileTabId
    Refs.darkenOverlay.classList.add ([| "invisible" |])
    // Hide the reset hint toast if visible
    let hint = Refs.getHtml "editor-readonly-toast"
    hint.classList.add ("invisible")

/// <summary>
/// Decorate a line with an error indication and set up a hover message.
/// Distinct message lines must be elements of markdownLst.
/// markdownLst: string list - list of markdown paragraphs.
/// tId: int - tab identifier.
/// lineNumber: int - line to decorate, starting at 1.
/// hoverLst: hover attached to line.
/// gHoverLst: hover attached to margin glyph.</summary>
let makeErrorInEditor tId lineNumber (hoverLst : string list) (gHoverLst : string list) =
    let makeMarkDown textLst =
        textLst
        |> List.toArray
        |> Array.map (fun txt -> createObj [ "isTrusted" ==> true; "value" ==> txt ])
    // decorate the line
    editorLineDecorate
        Refs.editors.[tId]
        lineNumber
        (createObj [
            "isWholeLine" ==> true
            "isTrusted" ==> true
            "inlineClassName" ==> "editor-line-error"
            "hoverMessage" ==> makeMarkDown hoverLst
         ])
        None
    // decorate the margin
    editorLineDecorate
        Refs.editors.[tId]
        lineNumber
        (createObj [
            "isWholeLine" ==> true
            "isTrusted" ==> true
            "glyphMarginClassName" ==> "editor-glyph-margin-error"
            "glyphMarginHoverMessage" ==> makeMarkDown gHoverLst
            "overviewRuler" ==> createObj [ "position" ==> 4 ]
        ])
        None

let revealLineInWindow tId (lineNumber : int) =
    Refs.editors.[tId]?revealLineInCenterIfOutsideViewport (lineNumber) |> ignore

//*************************************************************************************
//                              EDITOR CONTENT WIDGETS
//*************************************************************************************

type MemDirection = | MemRead | MemWrite

/// find editor Horizontal char position after end of code (ignoring comment)
let findCodeEnd (lineCol : int) =
    let tabSize = 6
    match Refs.currentTabText() with
    | None -> 0
    | Some text ->
        if text.Length <= lineCol then
            0
        else
            let line = text.[lineCol]
            match String.splitRemoveEmptyEntries [| ';' |] line |> Array.toList with
            | s :: _ -> (s.Length / tabSize) * tabSize + (if s.Length % tabSize > 0 then tabSize else 0)
            | [] -> 0


/// Make execution tooltip info for the given instruction and line v, dp before instruction dp.
/// Does nothing if opcode is not documented with execution tooltip
let toolTipInfo (v : int, orientation : string)
                (dp : DataPath)
                ({ Cond = cond; InsExec = instruction; InsOpCode = opc } : ParseTop.CondInstr) =
    match Helpers.condExecute cond dp, instruction with
    | false, _ -> ()
    | true, ParseTop.IMEM ins ->
        match Memory.executeMem ins dp with
        | Error _ -> ()
        | Ok res ->
            let TROWS s =
                (List.map (fun s -> s |> toDOM |> TD) >> TROW) s
            let memStackInfo (ins : Memory.InstrMemMult) (dir : MemDirection) (dp : DataPath) =
                let sp = dp.Regs.[ins.Rn]
                let offLst, increment = Memory.offsetList (sp |> int32) ins.suff ins.rList ins.WB (dir = MemRead)
                let locs = List.zip ins.rList offLst
                let makeRegRow (rn : RName, ol : uint32) =
                    [
                        rn.ToString()
                        (match dir with | MemRead -> "\u2190" | MemWrite -> "\u2192")
                        (sprintf "Mem<sub>32</sub>[0x%08X]" ol)
                        (match dir with
                            | MemRead -> Map.tryFind (WA ol) dp.MM |> (function | Some(Dat x) -> x | _ -> 0u)
                            | MemWrite -> dp.Regs.[rn])
                        |> (fun x -> if abs (int x) < 10000 then sprintf "(%d)" x else sprintf "(0x%08X)" x)
                    ]
                let regRows =
                    locs
                    |> List.map (makeRegRow >> TROWS)
                (findCodeEnd v, "Stack"), TABLE [] [
                    DIV [] [
                        TROWS [ sprintf "Pointer (%s)" (ins.Rn.ToString()); sprintf "0x%08X" sp ]
                        TROWS [ "Increment"; increment |> sprintf "%d" ]
                    ]
                    DIV [ "tooltip-stack-regs-" + tippyTheme() + "-theme" ] regRows ]


            let memPointerInfo (ins : Memory.InstrMemSingle) (dir : MemDirection) (dp : DataPath) =
                let baseAddrU =
                    let rb = dp.Regs.[ins.Rb]
                    match ins.Rb with | R15 -> rb + 8u | _ -> rb
                let baseAddr = int32 baseAddrU
                let offset = (ins.MAddr dp baseAddr |> uint32) - baseAddrU |> int32
                let ea = match ins.MemMode with | Memory.PreIndex | Memory.NoIndex -> (baseAddrU + uint32 offset) | _ -> baseAddrU
                let mData = (match ins.MemSize with | MWord -> Memory.getDataMemWord | MByte -> Memory.getDataMemByte | MHalf -> Memory.getDataMemHalfWord | MSignedHalf -> Memory.getDataMemSignedHalfWord | MSignedByte -> Memory.getDataMemSignedByte) ea dp
                let isIncr = match ins.MemMode with | Memory.NoIndex -> false | _ -> true
                (findCodeEnd v, "Pointer"), TABLE [] [
                    TROWS [ sprintf "Base (%s)" (ins.Rb.ToString()); sprintf "0x%08X" baseAddrU ]
                    TROWS [ "Address"; ea |> sprintf "0x%08X" ]
                    TROWS <| if isIncr then [] else [ "Offset"; (offset |> sprintf "%+d") ]
                    TROWS [ "Increment"; (if isIncr then offset else 0) |> (fun n -> sprintf "%+d" n) ]
                    TROWS [ "Data"; match ins.LSType with
                                    | LOAD -> match mData with | Ok dat -> dat | _ -> 0u
                                    | STORE -> dp.Regs.[ins.Rd]
                                    |> fun d ->
                                        match ins.MemSize with
                                        | MWord -> sprintf "0x%08X" d
                                        | MByte -> sprintf "0x%02X" ((uint32 d) % 256u)
                                        | MHalf | MSignedHalf -> sprintf "0x%04X" ((uint32 d) % 65536u)
                                        | MSignedByte -> sprintf "0x%02X" ((uint32 d) % 256u) ]
                    ]

            let makeTip memInfo =
                let (hOffset, label), tipDom = memInfo dp
                makeEditorInfoButton Tooltips.lineTipsClickable (hOffset, (v + 1), orientation) label tipDom
            match ins with
            | Memory.LDR ins -> makeTip <| memPointerInfo ins MemRead
            | Memory.STR ins -> makeTip <| memPointerInfo ins MemWrite
            | Memory.LDM ins -> makeTip <| memStackInfo ins MemRead
            | Memory.STM ins -> makeTip <| memStackInfo ins MemWrite
            | _ -> ()
    | true, ParseTop.IDP(exec, op2) ->
        let alu = ExecutionTop.isArithmeticOpCode opc
        let pos = findCodeEnd v, v, orientation
        match exec dp with
        | Error _ -> ()
        | Ok(dp', uF') ->
            match op2 with
            | DP.Op2.NumberLiteral _
            | DP.Op2.RegisterWithShift(_, _, 0u) -> ()
            | DP.Op2.RegisterWithShift(rn, shiftT, shiftAmt) ->
                    makeShiftTooltip pos (dp, dp', uF') rn (Some shiftT, alu) shiftAmt op2
                    
            | DP.Op2.RegisterWithRegisterShift(rn, shiftT, sRn) ->
                    makeShiftTooltip pos (dp, dp', uF') rn (Some shiftT, alu) (dp.Regs.[sRn] % 32u) op2
                    
            | DP.Op2.RegisterWithRRX rn -> makeShiftTooltip pos (dp, dp', uF') rn (None, alu) 1u op2
    | _ -> ()

