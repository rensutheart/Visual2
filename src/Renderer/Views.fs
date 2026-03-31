(*
    VisUAL2 @ Imperial College London
    Project: A user-friendly ARM emulator in F# and Web Technologies ( Github Electron & Fable Compiler )
    Module: Renderer.Views
    Description: Display registers, memory or symbol table in Views Panel
*)

/// implement views panel
module Views

open Fable.Core
open Fable.Core.JsInterop
open Fable.Import.Browser
open Refs
open Fable
open ExecutionTop

let maxSymbolWidth = 30
let maxDataSymbolLength = 16


let nameSquash maxW name =
    let nameLen = String.length name
    if nameLen <= maxW then name
    else
        let fp = (float maxW) * 0.65 |> int
        let lp = maxW - (fp + 3)
        name.[0..fp - 1] + "..." + name.[nameLen - lp..nameLen - 1]

let calcDashboardWidth() =
    let w =
        match currentRep, currentView with
        | Bin, _ -> "--dashboard-width-binrep"
        | _, Registers -> "--dashboard-width-init-registers"
        | _, Display -> "--dashboard-width-init-other"
        | _ -> "--dashboard-width-init-other"
        |> getCustomCSS
    printf "Setting width to %s" w
    w |> setDashboardWidth


let setRepresentation rep =
    (// Disable the other button
    representation currentRep).classList.remove("btn-rep-enabled")
    |> ignore

    // Enable the newly pressed button
    let btnNew = representation rep
    btnNew.classList.add ("btn-rep-enabled");

    // Reassign currentRep, new mutability required
    // keep constants defining GUI sizes in CSS
    currentRep <- rep
    calcDashboardWidth()
    // Use smaller font for binary representation so all 32 bits fit
    match rep with
    | Bin -> setCustomCSS "--register-font-size" "13px"
    | _ -> setCustomCSS "--register-font-size" "13pt"
    updateRegisters()

/// Toggle memory direction
let toggleReverseView() =
    reverseDirection <- not reverseDirection
    match reverseDirection with
    | true ->
        reverseViewBtn.classList.add ("btn-byte-active")
        reverseViewBtn.innerHTML <- "Disable Reverse Direction"
    | false ->
        reverseViewBtn.classList.remove ("btn-byte-active")
        reverseViewBtn.innerHTML <- "Enable Reverse Direction"


/// Toggle byte / word view
let toggleByteView() =
    byteView <- not byteView
    match byteView with
    | true ->
        byteViewBtn.classList.add ("btn-byte-active")
        byteViewBtn.innerHTML <- "Disable Byte View"
    | false ->
        byteViewBtn.classList.remove ("btn-byte-active")
        byteViewBtn.innerHTML <- "Enable Byte View"

/// Converts a memory map to a list of lists which are contiguous blocks of memory
let contiguousMemory reverse (mem : Map<uint32, uint32>) =
    Map.toList mem
    |> List.fold (fun state (addr, value) ->
        match state with
        | [] -> [ [ (addr, value) ] ]
        | hd :: tl ->
            match hd with
            | [] -> failwithf "Contiguous memory never starts a new list with no elements"
            | hd' :: _ when fst hd' = addr - 4u ->
                ((addr, value) :: hd) :: tl // Add to current contiguous block
                           | _ :: _ -> [ (addr, value) ] :: state // Non-contiguous, add to new block
    ) []
    |> List.map (if reverse then id else List.rev) // Reverse each list to go back to increasing
    |> if reverse then id else List.rev // Reverse the overall list

/// Converts a list of (uint32 * uint32) to a byte addressed
/// memory list of (uint32 * uint32) which is 4 times longer
/// LITTLE ENDIAN
let lstToBytes (lst : (uint32 * uint32) list) =
    let byteInfo (dat : uint32) =
        let b = dat &&& 0xFFu
        match b with
        | _ when b >= 32u && b <= 126u -> sprintf "'%c'" (char b), b
        | _ -> "", b
    lst
    |> List.collect (fun (addr, value) ->
        [
            addr, value |> byteInfo
            addr + 1u, (value >>> 8) |> byteInfo
            addr + 2u, (value >>> 16) |> byteInfo
            addr + 3u, (value >>> 24) |> byteInfo
        ]
    )

/// make an HTML element
/// id = element name
/// css = css class names to add to classlist
/// inner = inner HTML (typically text) for element
let makeElement (id : string) (css : string) (inner : string) =
        let el = document.createElement id
        el.classList.add css
        el.innerHTML <- inner
        el

/// make an HTML element
/// id = element name
/// css = css class names to add to classlist
let makeEl (id : string) (css : string) =
        let el = document.createElement id
        el.classList.add css
        el
/// appends child node after last child in parent node, returns parent
/// operator is left associative
/// child: child node
/// node: parent node.
let (&>>) (node : Node) child =
    node.appendChild child |> ignore
    node

let createDOM (parentID : string) (childList : Node list) =
    let parent = document.createElement parentID
    List.iter (fun ch -> parent &>> ch |> ignore) childList
    parent

let addToDOM (parent : Node) (childList : Node list) =
    List.iter (fun ch -> parent &>> ch |> ignore) childList
    parent

/// Update Memory view based on byteview, memoryMap, symbolMap
/// Creates the html to format the memory table in contiguous blocks
let updateMemoryIfChanged =

    let updateMemory' (currentRep, byteView, reverseView, symbolMap, mem, stkInf) =
        let chWidth = 13
        let memPanelShim = 50
        let onlyIfByte x = if byteView then [ x ] else []
        let invSymbolTypeMap symType =
            symbolMap
            |> Map.toList
            |> List.filter (fun (_, (_, typ)) -> typ = symType)
            |> List.distinctBy (fun (_, (addr, _)) -> addr)
            |> List.map (fun (sym, (addr, _)) -> (addr, sym))
            |> Map.ofList
        let invSymbolMap = invSymbolTypeMap ExecutionTop.DataSymbol
        let invCodeMap = invSymbolTypeMap ExecutionTop.CodeSymbol
        let invStackMap =
            match stkInf with
            | Some(si, sp) -> si |> List.map (fun { SP = sp; Target = target } ->
                                            sp - 4u, match Map.tryFind target invCodeMap with
                                                     | Some s -> "(" + s + ")"
                                                     | None -> sprintf "(%08x)" target)
                               |> Map.ofList, sp
            | _ -> Map.empty, 0u
            |> (fun (map, sp) -> // add SP legend
                    let lab =
                        match sp, Map.tryFind sp map with
                        | 0u, Some sym -> sym
                        | 0u, None -> ""
                        | _, None -> "SP ->"
                        | _, Some sym -> sym + " ->"
                    Map.add sp lab map)


        let lookupSym addr =
                match Map.tryFind addr invSymbolMap, Map.tryFind addr invStackMap with
                | Some sym, _ -> sym
                | option.None, Some sub -> sub
                | _ -> ""


        let makeRow (addr : uint32, (chRep : string, value : uint32)) =

            let tr = makeEl "tr" "tr-head-mem"

            let rowDat =
                [
                    lookupSym addr |> nameSquash maxDataSymbolLength
                    sprintf "0x%X" addr
                    (if byteView then
                        formatterWithWidth 8 currentRep value +
                        (chRep |> function | "" -> "" | chr -> sprintf " %s" chr)
                    else formatter currentRep value)
                ]

            let makeNode txt = makeElement "td" "selectable-text" txt :> Node

            addToDOM tr (List.map makeNode rowDat)

        let makeContig (lst : (uint32 * uint32) list) =

            let table = makeEl "table" "table-striped"

            let makeNode txt = makeElement "th" "th-mem" txt :> Node

            let tr = createDOM "tr" <| List.map makeNode ([ "Symbol"; "Address"; "Value" ])

            let byteSwitcher =
                match byteView with
                | true -> lstToBytes
                | false -> List.map (fun (addr, dat) -> (addr, ("", dat)))

            // Add each row to the table from lst
            let rows =
                lst
                |> byteSwitcher
                |> List.map makeRow

            addToDOM table <| [ tr ] @ rows
            |> ignore

            let li = makeEl "li" "list-group-item"
            li.style.padding <- "0px"

            addToDOM li [ table ]

        memList.innerHTML <- ""

        // Add the new memory list

        mem
        |> contiguousMemory reverseView
        |> List.map makeContig
        |> List.iter (fun html -> memList.appendChild (html) |> ignore)
    updateMemory'
    |> cacheLastWithActionIfChanged

let updateMemory() =
    let stackInfo =
        match runMode with
        | FinishedMode ri
        | RunErrorMode ri
        | ActiveMode(_, ri) ->
            let sp = (fst ri.dpCurrent).Regs.[CommonData.R13]
            Some(ri.StackInfo, sp)
        | _ -> Core.Option.None
    updateMemoryIfChanged (currentRep, byteView, reverseDirection, symbolMap, memoryMap, stackInfo)

/// Update symbol table View using currentRep and symbolMap
let updateSymTableIfChanged =
    let updateSymTable (symbolMap, currentRep) =
        let makeRow ((sym : string), (value, typ) : uint32 * ExecutionTop.SymbolType) =
            let tr = makeEl "tr" "tr-head-sym"
            addToDOM tr [
                makeElement "td" "selectable-text" sym
                makeElement "td" "selectable-text" (formatter currentRep value)
                ]

        let makeGroupHdr typ =
            let symName =
                match typ with
                | DataSymbol -> "Data Symbol"
                | CodeSymbol -> "Code Symbol"
                | CalculatedSymbol -> "EQU Symbol"

            createDOM "tr" [
                makeElement "th" "th-mem" symName
                makeElement "th" "th-mem" "Value"
                ]

        let symTabRows =
            let makeGroupRows (grpTyp, grpSyms) =
                grpSyms
                |> Array.map (fun (sym, addr) -> sym, addr)
                |> Array.sortBy snd
                |> Array.map (fun (sym, addr) -> nameSquash maxSymbolWidth sym, addr)
                |> Array.map makeRow
                |> Array.append [| makeGroupHdr grpTyp |]

            let groupOrder = function
                | (CodeSymbol, _) -> 1
                | (DataSymbol, _) -> 2
                | (CalculatedSymbol, _) -> 3

            symbolMap
            |> Map.toArray
            |> Array.groupBy (fun (_sym, (_addr, typ)) -> typ)
            |> Array.sortBy groupOrder
            |> Array.collect makeGroupRows
            |> Array.toList

        // Clear the old symbol table
        symTable.innerHTML <- ""
        // Add the new one
        addToDOM symTable (symTabRows) |> ignore
    updateSymTable
    |> cacheLastWithActionIfChanged

let updateSymTable() =
    updateSymTableIfChanged (symbolMap, currentRep)

/// Set View to view
let setView view =
    (// Change the active tab
    viewTab currentView).classList.remove("active")
    (viewTab view).classList.add("active")

    (// Change the visibility of the views
    viewView currentView).classList.add("invisible")
    (viewView view).classList.remove("invisible")

    // new mutability again, update the variable
    currentView <- view
    calcDashboardWidth()
    updateMemory()

// ***********************************************************************************
//                       Memory-Mapped Pixel Display
// ***********************************************************************************

/// VGA 256-colour palette: index -> (R, G, B)
let vgaPalette = [|
    (0, 0, 0); (0, 2, 170); (20, 170, 0); (0, 170, 170); (170, 0, 3); (170, 0, 170); (170, 85, 0); (170, 170, 170)
    (85, 85, 85); (85, 85, 255); (85, 255, 85); (85, 255, 255); (255, 85, 85); (253, 85, 255); (255, 255, 85); (255, 255, 255)
    (0, 0, 0); (16, 16, 16); (32, 32, 32); (53, 53, 53); (69, 69, 69); (85, 85, 85); (101, 101, 101); (117, 117, 117)
    (138, 138, 138); (154, 154, 154); (170, 170, 170); (186, 186, 186); (202, 202, 202); (223, 223, 223); (239, 239, 239); (255, 255, 255)
    (0, 4, 255); (65, 4, 255); (130, 3, 255); (190, 2, 255); (253, 0, 255); (254, 0, 190); (255, 0, 130); (255, 0, 65)
    (255, 0, 8); (255, 65, 5); (255, 130, 0); (255, 190, 0); (255, 255, 0); (190, 255, 0); (130, 255, 0); (65, 255, 1)
    (36, 255, 0); (34, 255, 66); (29, 255, 130); (18, 255, 190); (0, 255, 255); (0, 190, 255); (1, 130, 255); (0, 65, 255)
    (130, 130, 255); (158, 130, 255); (190, 130, 255); (223, 130, 255); (253, 130, 255); (254, 130, 223); (255, 130, 190); (255, 130, 158)
    (255, 130, 130); (255, 158, 130); (255, 190, 130); (255, 223, 130); (255, 255, 130); (223, 255, 130); (190, 255, 130); (158, 255, 130)
    (130, 255, 130); (130, 255, 158); (130, 255, 190); (130, 255, 223); (130, 255, 255); (130, 223, 255); (130, 190, 255); (130, 158, 255)
    (186, 186, 255); (202, 186, 255); (223, 186, 255); (239, 186, 255); (254, 186, 255); (254, 186, 239); (255, 186, 223); (255, 186, 202)
    (255, 186, 186); (255, 202, 186); (255, 223, 186); (255, 239, 186); (255, 255, 186); (239, 255, 186); (223, 255, 186); (202, 255, 187)
    (186, 255, 186); (186, 255, 202); (186, 255, 223); (186, 255, 239); (186, 255, 255); (186, 239, 255); (186, 223, 255); (186, 202, 255)
    (1, 1, 113); (28, 1, 113); (57, 1, 113); (85, 0, 113); (113, 0, 113); (113, 0, 85); (113, 0, 57); (113, 0, 28)
    (113, 0, 1); (113, 28, 1); (113, 57, 0); (113, 85, 0); (113, 113, 0); (85, 113, 0); (57, 113, 0); (28, 113, 0)
    (9, 113, 0); (9, 113, 28); (6, 113, 57); (3, 113, 85); (0, 113, 113); (0, 85, 113); (0, 57, 113); (0, 28, 113)
    (57, 57, 113); (69, 57, 113); (85, 57, 113); (97, 57, 113); (113, 57, 113); (113, 57, 97); (113, 57, 85); (113, 57, 69)
    (113, 57, 57); (113, 69, 57); (113, 85, 57); (113, 97, 57); (113, 113, 57); (97, 113, 57); (85, 113, 57); (69, 113, 58)
    (57, 113, 57); (57, 113, 69); (57, 113, 85); (57, 113, 97); (57, 113, 113); (57, 97, 113); (57, 85, 113); (57, 69, 114)
    (81, 81, 113); (89, 81, 113); (97, 81, 113); (105, 81, 113); (113, 81, 113); (113, 81, 105); (113, 81, 97); (113, 81, 89)
    (113, 81, 81); (113, 89, 81); (113, 97, 81); (113, 105, 81); (113, 113, 81); (105, 113, 81); (97, 113, 81); (89, 113, 81)
    (81, 113, 81); (81, 113, 90); (81, 113, 97); (81, 113, 105); (81, 113, 113); (81, 105, 113); (81, 97, 113); (81, 89, 113)
    (0, 0, 66); (17, 0, 65); (32, 0, 65); (49, 0, 65); (65, 0, 65); (65, 0, 50); (65, 0, 32); (65, 0, 16)
    (65, 0, 0); (65, 16, 0); (65, 32, 0); (65, 49, 0); (65, 65, 0); (49, 65, 0); (32, 65, 0); (16, 65, 0)
    (3, 65, 0); (3, 65, 16); (2, 65, 32); (1, 65, 49); (0, 65, 65); (0, 49, 65); (0, 32, 65); (0, 16, 65)
    (32, 32, 65); (40, 32, 65); (49, 32, 65); (57, 32, 65); (65, 32, 65); (65, 32, 57); (65, 32, 49); (65, 32, 40)
    (65, 32, 32); (65, 40, 32); (65, 49, 32); (65, 57, 33); (65, 65, 32); (57, 65, 32); (49, 65, 32); (40, 65, 32)
    (32, 65, 32); (32, 65, 40); (32, 65, 49); (32, 65, 57); (32, 65, 65); (32, 57, 65); (32, 49, 65); (32, 40, 65)
    (45, 45, 65); (49, 45, 65); (53, 45, 65); (61, 45, 65); (65, 45, 65); (65, 45, 61); (65, 45, 53); (65, 45, 49)
    (65, 45, 45); (65, 49, 45); (65, 53, 45); (65, 61, 45); (65, 65, 45); (61, 65, 45); (53, 65, 45); (49, 65, 45)
    (45, 65, 45); (45, 65, 49); (45, 65, 53); (45, 65, 61); (45, 65, 65); (45, 61, 65); (45, 53, 65); (45, 49, 65)
    (0, 0, 0); (0, 0, 0); (0, 0, 0); (0, 0, 0); (0, 0, 0); (0, 0, 0); (0, 0, 0); (0, 0, 0)
|]

/// Read a single byte from the memory map (little-endian word storage)
let readMemByte (mem : Map<uint32, uint32>) (byteAddr : uint32) =
    let wordAddr = byteAddr &&& 0xFFFFFFFCu
    let byteOffset = int (byteAddr &&& 3u)
    let wordVal = match Map.tryFind wordAddr mem with | Some v -> v | None -> 0u
    int ((wordVal >>> (byteOffset * 8)) &&& 0xFFu)

/// Canvas pixel size
let displayCanvasSize = 320.0

/// Render the pixel grid onto the display canvas
let renderDisplay () =
    let canvas = displayCanvas
    let gridSize = displayGridSize
    let totalPixels = gridSize * gridSize
    let cellSize = displayCanvasSize / (float gridSize)
    // Set canvas size
    canvas.width <- displayCanvasSize
    canvas.height <- displayCanvasSize
    let ctx : CanvasRenderingContext2D = unbox(canvas?getContext("2d"))
    // Clear
    ctx.clearRect(0.0, 0.0, displayCanvasSize, displayCanvasSize)
    // Draw each pixel
    for i in 0 .. totalPixels - 1 do
        let byteAddr = displayBaseAddress + (uint32 i)
        let paletteIdx = readMemByte memoryMap byteAddr
        let idx = if paletteIdx >= 0 && paletteIdx < 256 then paletteIdx else 0
        let (r, g, b) = vgaPalette.[idx]
        let row = i / gridSize
        let col = i % gridSize
        let x = (float col) * cellSize
        let y = (float row) * cellSize
        ctx?fillStyle <- sprintf "rgb(%d,%d,%d)" r g b
        ctx.fillRect(x, y, cellSize, cellSize)

/// Update the display view if display mode is active
let updateDisplay () =
    if displayModeActive then
        renderDisplay()

/// Toggle display mode on/off and update UI visibility
let toggleDisplayMode () =
    displayModeActive <- displayToggle.``checked``
    let canvasContainer = getHtml "display-canvas-container"
    if displayModeActive then
        displayMessage.classList.add("invisible")
        canvasContainer.classList.remove("invisible")
        renderDisplay()
    else
        displayMessage.classList.remove("invisible")
        canvasContainer.classList.add("invisible")

/// Clear the display memory region (set all pixels to 0/black) and re-render
let clearDisplayMemory () =
    let gridSize = displayGridSize
    let totalPixels = gridSize * gridSize
    // Remove all display memory keys from the memoryMap
    let keysToRemove =
        [ for i in 0 .. (totalPixels / 4) do
            yield displayBaseAddress + (uint32 (i * 4)) ]
    let mutable mm = memoryMap
    for k in keysToRemove do
        mm <- Map.remove k mm
    memoryMap <- mm
    if displayModeActive then
        renderDisplay()


