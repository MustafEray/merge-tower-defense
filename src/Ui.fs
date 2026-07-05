/// Pure UI-layer state for Phase 2: everything the render/HUD layers need
/// that is not core game logic — canvas layout math, pointer-to-cell hit
/// testing, HUD placeholders and the frame message that feeds injected
/// DeltaTime into the core engine.
///
/// This module stays as pure as Shared/State: no DOM, no PixiJS, no clock.
/// The interop shells (App.fs, Render.fs, Hud.fs) only send UiMsg values and
/// read the resulting UiModel.
module MergeTowerDefense.Ui

open MergeTowerDefense.Shared
open MergeTowerDefense.State

// ---------------------------------------------------------------------------
// Canvas layout (pure math, shared by rendering and hit testing)
// ---------------------------------------------------------------------------

/// Pixel geometry of the play field. Derived from GridSize only, so the
/// renderer and the pointer hit test can never disagree.
type Layout =
    { CanvasWidth: float
      CanvasHeight: float
      GridLeft: float
      GridTop: float
      CellSize: float
      /// Vertical centre of the enemy lane strip above the grid. The real
      /// path geometry arrives in Phase 3; Phase 2 renders PathProgress on
      /// a straight demo lane.
      LaneY: float
      PathLeft: float
      PathRight: float }

let private cellSizePx = 72.0
let private marginPx = 24.0
let private lanePx = 64.0
let private laneGapPx = 16.0

let layoutFor (size: GridSize) : Layout =
    let n = float (GridSize.value size)
    let gridSpan = n * cellSizePx

    { CanvasWidth = marginPx * 2.0 + gridSpan
      CanvasHeight = marginPx * 2.0 + lanePx + laneGapPx + gridSpan
      GridLeft = marginPx
      GridTop = marginPx + lanePx + laneGapPx
      CellSize = cellSizePx
      LaneY = marginPx + lanePx / 2.0
      PathLeft = marginPx
      PathRight = marginPx + gridSpan }

/// Centre of a cell in canvas pixels.
let cellCenter (layout: Layout) (coord: Coord) : float * float =
    layout.GridLeft + (float (Coord.col coord) + 0.5) * layout.CellSize,
    layout.GridTop + (float (Coord.row coord) + 0.5) * layout.CellSize

/// Top-left corner of a cell in canvas pixels.
let cellOrigin (layout: Layout) (coord: Coord) : float * float =
    layout.GridLeft + float (Coord.col coord) * layout.CellSize,
    layout.GridTop + float (Coord.row coord) * layout.CellSize

/// Maps a canvas-pixel position to the grid cell under it, if any.
let cellAtPoint (layout: Layout) (size: GridSize) (x: float) (y: float) : Coord option =
    if x < layout.GridLeft || y < layout.GridTop then
        None
    else
        let col = int ((x - layout.GridLeft) / layout.CellSize)
        let row = int ((y - layout.GridTop) / layout.CellSize)
        Coord.tryCreate size row col

/// Canvas x of an enemy given its normalised path progress.
let enemyX (layout: Layout) (progress: PathProgress) : float =
    layout.PathLeft + PathProgress.value progress * (layout.PathRight - layout.PathLeft)

// ---------------------------------------------------------------------------
// UI model
// ---------------------------------------------------------------------------

type UiModel =
    { Game: GameState
      /// Cell currently under the pointer, if any.
      Hover: Coord option
      /// Raw pointer position in canvas pixels (drives the drag ghost).
      Pointer: (float * float) option
      /// Placeholder until the Phase 3 economy lands in the core engine.
      Gold: int
      /// Placeholder until the Phase 3 wave scheduler lands in the core.
      Wave: int
      /// Towers bought so far; drives the deterministic purchase type cycle.
      Purchases: int
      /// Accumulated seconds towards the next demo enemy spawn.
      DemoClock: float
      /// Demo enemies spawned so far; drives the enemy type cycle.
      DemoSpawned: int
      /// Transient HUD message with its remaining time-to-live in seconds.
      Notice: (string * float) option }

type UiMsg =
    /// Forward a message to the core engine untouched.
    | GameMsg of Msg
    /// Pointer moved: hovered cell (if any) and raw canvas position.
    | PointerMoved of Coord option * (float * float) option
    /// HUD "buy tower" button.
    | BuyTower
    /// One render-loop frame worth of injected time.
    | Frame of DeltaTime

let init (size: GridSize) : UiModel =
    { Game = GameState.create size
      Hover = None
      Pointer = None
      Gold = 100
      Wave = 1
      Purchases = 0
      DemoClock = 0.0
      DemoSpawned = 0
      Notice = None }

// ---------------------------------------------------------------------------
// HUD-facing helpers
// ---------------------------------------------------------------------------

let towerCost = 20

let private purchaseCycle = [| Archer; Cannon; Frost |]
let private demoCycle = [| Grunt; Runner; Tank; Boss |]

/// Seconds between demo enemy spawns. Phase 2 scaffolding only: the real
/// wave scheduler replaces this in Phase 3.
let demoSpawnPeriod = 3.0

let private noticeTtl = 2.5

let firstEmptyCell (grid: Grid) : Coord option =
    Grid.coords grid |> List.tryFind (fun c -> Grid.cellAt c grid = Empty)

let nextPurchaseType (model: UiModel) : TowerType =
    purchaseCycle.[model.Purchases % purchaseCycle.Length]

let canBuy (model: UiModel) : bool =
    model.Gold >= towerCost
    && model.Game.Interaction = Idle
    && (firstEmptyCell model.Game.Grid |> Option.isSome)

/// Turns noteworthy game events into a short HUD message. Routine gesture
/// noise (plain returns, clicks on empty cells) stays silent on purpose.
let private noticeFor (events: GameEvent list) : string option =
    events
    |> List.tryPick (fun event ->
        match event with
        | TowersMerged(_, _, result, _) ->
            Some(sprintf "Merged! New tower is level %d." (TowerLevel.rank result.Level))
        | ActionRejected(MergeAtMaxLevel _) -> Some "Already at max level."
        | ActionRejected(IncompatibleTarget _) -> Some "Towers must share type and level to merge."
        | ActionRejected(SpawnCellOccupied _) -> Some "That cell is occupied."
        | ActionRejected SpawnWhileDragging -> Some "Finish the drag before buying."
        | _ -> None)

// ---------------------------------------------------------------------------
// UI transition function (pure)
// ---------------------------------------------------------------------------

let private applyGame (msg: Msg) (model: UiModel) : UiModel =
    let game, events = update msg model.Game

    let notice =
        match noticeFor events with
        | Some text -> Some(text, noticeTtl)
        | None -> model.Notice

    { model with Game = game; Notice = notice }

let updateUi (msg: UiMsg) (model: UiModel) : UiModel =
    match msg with
    | GameMsg gameMsg -> applyGame gameMsg model

    | PointerMoved(hover, pointer) ->
        { model with
            Hover = hover
            Pointer = pointer }

    | BuyTower ->
        if not (canBuy model) then
            model
        else
            match firstEmptyCell model.Game.Grid with
            | None -> model
            | Some cell ->
                let model' = applyGame (SpawnTower(nextPurchaseType model, cell)) model

                { model' with
                    Gold = model'.Gold - towerCost
                    Purchases = model'.Purchases + 1 }

    | Frame dt ->
        let seconds = DeltaTime.seconds dt

        // 1. Advance the core simulation with the injected time step.
        let model = applyGame (AdvanceEnemies dt) model

        // 2. Demo enemy spawner (Phase 2 scaffolding, see demoSpawnPeriod).
        let clock = model.DemoClock + seconds

        let model =
            if clock >= demoSpawnPeriod then
                let enemyType = demoCycle.[model.DemoSpawned % demoCycle.Length]

                applyGame
                    (SpawnEnemy enemyType)
                    { model with
                        DemoClock = clock - demoSpawnPeriod
                        DemoSpawned = model.DemoSpawned + 1 }
            else
                { model with DemoClock = clock }

        // 3. Let the transient HUD notice fade out.
        let notice =
            model.Notice
            |> Option.bind (fun (text, ttl) ->
                let ttl' = ttl - seconds
                if ttl' <= 0.0 then None else Some(text, ttl'))

        { model with Notice = notice }
