/// Composition root — the only impure module in the application. It wires
/// the pure core (Shared/State/Ui) to PixiJS (canvas, ticker, pointer
/// events) and React (HUD), and owns the single mutable model reference of
/// the hand-rolled MVU loop.
module MergeTowerDefense.App

open Fable.Core.JsInterop
open MergeTowerDefense.Shared
open MergeTowerDefense.State
open MergeTowerDefense.Ui
open MergeTowerDefense.Interop
open MergeTowerDefense.Interop.Pixi
open MergeTowerDefense.View

let private gridSide = 5

let private start () =
    let size =
        match GridSize.tryCreate gridSide with
        | Some s -> s
        // Unreachable: gridSide is a compile-time constant within
        // GridSize.minSize..maxSize.
        | None -> failwith "unreachable: gridSide is a valid grid size"

    let layout = layoutFor size

    // --- PixiJS application (WebGL with automatic canvas fallback) --------
    let app =
        createApplication
            [ "width", box layout.CanvasWidth
              "height", box layout.CanvasHeight
              "background", box 0x141724
              "antialias", box true ]

    app.ticker.maxFPS <- 60.0
    Dom.appendChild (Dom.getElementById "game-root") app.view

    let layers = Render.createLayers app
    Render.drawStatic layout size layers

    // --- React HUD in its own DOM root -------------------------------------
    let hudRoot = React.createRoot (Dom.getElementById "hud-root")

    // --- MVU loop -----------------------------------------------------------
    // The one mutable cell of the app. Every change flows through the pure
    // Ui.updateUi; Pixi redraws from the model each frame, React re-renders
    // only when a HUD-visible value actually changes.
    let mutable model = init size

    let hudProjection (m: UiModel) =
        m.Gold, m.Wave, List.length m.Game.Enemies, Option.map fst m.Notice, canBuy m, m.Purchases

    let rec dispatch (msg: UiMsg) : unit =
        let before = hudProjection model
        model <- updateUi msg model

        if hudProjection model <> before then
            hudRoot.render (Hud.view model dispatch)

    // --- pointer/touch → Msg ------------------------------------------------
    let stage = app.stage
    stage.eventMode <- "static"
    stage.hitArea <- createRectangle 0.0 0.0 layout.CanvasWidth layout.CanvasHeight
    stage.cursor <- "pointer"

    let cellUnder (event: obj) =
        let x, y = pointerPosition event
        cellAtPoint layout size x y, (x, y)

    stage.on (
        "pointerdown",
        fun event ->
            match fst (cellUnder event) with
            | Some coord -> dispatch (GameMsg(StartDrag coord))
            | None -> ()
    )
    |> ignore

    stage.on (
        "pointermove",
        fun event ->
            let cell, pos = cellUnder event
            dispatch (PointerMoved(cell, Some pos))
    )
    |> ignore

    stage.on (
        "pointerup",
        fun event ->
            match model.Game.Interaction with
            | Dragging _ ->
                match fst (cellUnder event) with
                | Some coord -> dispatch (GameMsg(Drop coord))
                | None -> dispatch (GameMsg CancelDrag)
            | Idle -> ()
    )
    |> ignore

    stage.on (
        "pointerupoutside",
        fun _ ->
            match model.Game.Interaction with
            | Dragging _ -> dispatch (GameMsg CancelDrag)
            | Idle -> ()
    )
    |> ignore

    // --- main loop ----------------------------------------------------------
    // Real elapsed milliseconds from the ticker, converted to a validated
    // DeltaTime and injected into the core: movement is time-based, never
    // frame-based.
    app.ticker.add (fun _ ->
        match DeltaTime.tryCreate (app.ticker.deltaMS / 1000.0) with
        | Some dt -> dispatch (Frame dt)
        | None -> ()

        Render.drawFrame layout model layers)
    |> ignore

    hudRoot.render (Hud.view model dispatch)

    // Debug hook for scripts/verify-e2e.mjs (test-only, reads the model).
    Dom.globalThis?__MTD_DEBUG <-
        fun () ->
            createObj
                [ "gold", box model.Gold
                  "enemies", box (List.length model.Game.Enemies)
                  "dragging",
                  box (
                      match model.Game.Interaction with
                      | Dragging _ -> true
                      | Idle -> false
                  )
                  "towers",
                  box (
                      Grid.towers model.Game.Grid
                      |> List.map (fun (coord, tower) ->
                          createObj
                              [ "row", box (Coord.row coord)
                                "col", box (Coord.col coord)
                                "type", box (string tower.Type)
                                "level", box (TowerLevel.rank tower.Level) ])
                      |> List.toArray
                  ) ]

do start ()
