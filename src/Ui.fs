/// Pure UI-layer state: everything the render/HUD layers need that is not
/// core game logic — canvas layout math, pointer-to-cell hit testing, the
/// transient HUD notice, shot flashes and the frame message that feeds
/// injected DeltaTime into the core engine.
///
/// This module stays as pure as Shared/State: no DOM, no PixiJS, no clock.
/// The interop shells (App.fs, Render.fs, Hud.fs) only send UiMsg values and
/// read the resulting UiModel. Since Phase 3 the economy (gold/lives) and
/// waves live in the core engine; this layer no longer holds placeholders.
module MergeTowerDefense.Ui

open MergeTowerDefense.Shared
open MergeTowerDefense.State

// ---------------------------------------------------------------------------
// Canvas layout (pure math, shared by rendering and hit testing)
// ---------------------------------------------------------------------------

/// Pixel geometry of the play field. Derived from GridSize and the Path
/// bounds only, so the renderer and the pointer hit test can never disagree.
/// GridLeft/GridTop is the pixel position of the grid's top-left corner —
/// the origin of the cell-unit coordinate system used by Path and Coord.
type Layout =
    { CanvasWidth: float
      CanvasHeight: float
      GridLeft: float
      GridTop: float
      CellSize: float }

let private cellSizePx = 72.0
/// Half of the visual width of the enemy lane, in cell units.
let private laneHalfCells = 0.45
/// Outer canvas margin, in cell units.
let private marginCells = 0.3

let layoutFor (size: GridSize) (path: Path) : Layout =
    let n = float (GridSize.value size)
    let pMinX, pMinY, pMaxX, pMaxY = Path.bounds path
    let worldMinX = min 0.0 (pMinX - laneHalfCells) - marginCells
    let worldMinY = min 0.0 (pMinY - laneHalfCells) - marginCells
    let worldMaxX = max n (pMaxX + laneHalfCells) + marginCells
    let worldMaxY = max n (pMaxY + laneHalfCells) + marginCells

    { CanvasWidth = (worldMaxX - worldMinX) * cellSizePx
      CanvasHeight = (worldMaxY - worldMinY) * cellSizePx
      GridLeft = -worldMinX * cellSizePx
      GridTop = -worldMinY * cellSizePx
      CellSize = cellSizePx }

/// Cell-unit point (the Path/Coord coordinate system) to canvas pixels.
let toPx (layout: Layout) (point: float * float) : float * float =
    let x, y = point
    layout.GridLeft + x * layout.CellSize, layout.GridTop + y * layout.CellSize

/// Centre of a cell in canvas pixels.
let cellCenter (layout: Layout) (coord: Coord) : float * float = toPx layout (Coord.center coord)

/// Top-left corner of a cell in canvas pixels.
let cellOrigin (layout: Layout) (coord: Coord) : float * float =
    toPx layout (float (Coord.col coord), float (Coord.row coord))

/// Maps a canvas-pixel position to the grid cell under it, if any.
let cellAtPoint (layout: Layout) (size: GridSize) (x: float) (y: float) : Coord option =
    if x < layout.GridLeft || y < layout.GridTop then
        None
    else
        let col = int ((x - layout.GridLeft) / layout.CellSize)
        let row = int ((y - layout.GridTop) / layout.CellSize)
        Coord.tryCreate size row col

/// Visual width of the enemy lane strip in pixels.
let laneWidthPx (layout: Layout) = 2.0 * laneHalfCells * layout.CellSize

// ---------------------------------------------------------------------------
// UI model
// ---------------------------------------------------------------------------

/// A brief tracer for a shot fired this instant (from a tower cell to a
/// target position in cell units), fading over Ttl seconds.
type Shot =
    { FromCell: Coord
      Target: float * float
      Ttl: float }

/// What a transient burst effect depicts. The domain value (enemy/tower kind)
/// is carried rather than a colour, so the palette decision stays in the
/// render layer and never leaks into this pure module.
type EffectKind =
    /// Expanding ring where an enemy of this type was destroyed.
    | KillBurst of EnemyType
    /// Flash celebrating a merge that produced a tower of this type.
    | MergeFlash of TowerType

/// A one-shot animated burst anchored at a cell-unit position, fading over
/// Ttl seconds. Kept in cell units (never pixels) like the rest of this layer.
type Effect =
    { At: float * float
      Ttl: float
      Kind: EffectKind }

type UiModel =
    { Game: GameState
      /// Cell currently under the pointer, if any.
      Hover: Coord option
      /// Raw pointer position in canvas pixels (drives the drag ghost).
      Pointer: (float * float) option
      /// Transient HUD message with its remaining time-to-live in seconds.
      Notice: (string * float) option
      /// Fading shot tracers for the renderer.
      Shots: Shot list
      /// Fading burst effects (kills, merges) for the renderer.
      Effects: Effect list }

type UiMsg =
    /// Forward a message to the core engine untouched.
    | GameMsg of Msg
    /// Pointer moved: hovered cell (if any) and raw canvas position.
    | PointerMoved of Coord option * (float * float) option
    /// HUD "buy tower" button for the given type.
    | Buy of TowerType
    /// HUD restart after a game over.
    | Restart
    /// One render-loop frame worth of injected time.
    | Frame of DeltaTime

let init (size: GridSize) : UiModel =
    { Game = GameState.create size
      Hover = None
      Pointer = None
      Notice = None
      Shots = []
      Effects = [] }

// ---------------------------------------------------------------------------
// HUD-facing helpers
// ---------------------------------------------------------------------------

let private noticeTtl = 2.5
let shotTtl = 0.12

// Burst effect lifetimes (seconds). Exposed so the renderer can derive each
// effect's animation progress without duplicating the constants.
let killBurstTtl = 0.35
let mergeFlashTtl = 0.45

/// Total lifetime of an effect, keyed by its kind. Progress in the renderer is
/// therefore (duration - remaining) / duration.
let effectDuration =
    function
    | KillBurst _ -> killBurstTtl
    | MergeFlash _ -> mergeFlashTtl

let firstEmptyCell (grid: Grid) : Coord option =
    Grid.coords grid |> List.tryFind (fun c -> Grid.cellAt c grid = Empty)

let canBuy (model: UiModel) : bool =
    match model.Game.Status with
    | Defeated _ -> false
    | Playing _ ->
        model.Game.Interaction = Idle
        && Gold.value model.Game.Gold >= nextTowerCost model.Game
        && (firstEmptyCell model.Game.Grid |> Option.isSome)

/// Turns noteworthy game events into a short HUD message, most important
/// first. Routine noise (plain returns, per-shot events) stays silent.
let private noticeOf (event: GameEvent) : (int * string) option =
    match event with
    | GameOver waves -> Some(100, sprintf "Game over — you survived %d wave(s)." waves)
    | ActionRejected(NotEnoughGold required) -> Some(80, sprintf "Not enough gold (need %d)." required)
    | ActionRejected(MergeAtMaxLevel _) -> Some(80, "Already at max level.")
    | ActionRejected(IncompatibleTarget _) -> Some(80, "Towers must share type and level to merge.")
    | ActionRejected(SpawnCellOccupied _) -> Some(80, "That cell is occupied.")
    | ActionRejected SpawnWhileDragging -> Some(80, "Finish the drag first.")
    | TowersMerged(_, _, result, _) -> Some(70, sprintf "Merged! New tower is level %d." (TowerLevel.rank result.Level))
    | WaveCompleted(wave, bonus) -> Some(60, sprintf "Wave %d cleared! +%d gold." wave bonus)
    | WaveStarted wave -> Some(50, sprintf "Wave %d incoming!" wave)
    | LifeLost remaining -> Some(40, sprintf "An enemy got through! %d lives left." remaining)
    | _ -> None

let private noticeFor (events: GameEvent list) : string option =
    match events |> List.choose noticeOf with
    | [] -> None
    | picks -> picks |> List.maxBy fst |> snd |> Some

// ---------------------------------------------------------------------------
// UI transition function (pure)
// ---------------------------------------------------------------------------

let private applyGame (msg: Msg) (model: UiModel) : UiModel =
    let game, events = update msg model.Game

    let notice =
        match noticeFor events with
        | Some text -> Some(text, noticeTtl)
        | None -> model.Notice

    let newShots =
        events
        |> List.choose (fun event ->
            match event with
            | TowerFired(_, origin, target) ->
                Some
                    { FromCell = origin
                      Target = target
                      Ttl = shotTtl }
            | _ -> None)

    // Kill bursts anchor where the enemy stood: a killed enemy is already gone
    // from `game`, so its last position and type come from the pre-update
    // state. Merge flashes anchor at the merge cell carried by the event.
    let enemyInfo =
        model.Game.Enemies
        |> List.map (fun e -> e.Id, (Enemy.positionOn model.Game.Path e, e.Type))
        |> Map.ofList

    let newEffects =
        events
        |> List.choose (fun event ->
            match event with
            | EnemyKilled(id, _) ->
                enemyInfo
                |> Map.tryFind id
                |> Option.map (fun (at, enemyType) ->
                    { At = at
                      Ttl = killBurstTtl
                      Kind = KillBurst enemyType })
            | TowersMerged(_, _, result, at) ->
                Some
                    { At = Coord.center at
                      Ttl = mergeFlashTtl
                      Kind = MergeFlash result.Type }
            | _ -> None)

    { model with
        Game = game
        Notice = notice
        Shots = newShots @ model.Shots
        Effects = newEffects @ model.Effects }

let updateUi (msg: UiMsg) (model: UiModel) : UiModel =
    match msg with
    | GameMsg gameMsg -> applyGame gameMsg model

    | PointerMoved(hover, pointer) ->
        { model with
            Hover = hover
            Pointer = pointer }

    | Buy towerType ->
        match firstEmptyCell model.Game.Grid with
        | Some cell -> applyGame (BuyTower(towerType, cell)) model
        | None ->
            { model with
                Notice = Some("No empty cell for a new tower.", noticeTtl) }

    | Restart -> init (Grid.size model.Game.Grid)

    | Frame dt ->
        let seconds = DeltaTime.seconds dt

        // 1. Advance the core simulation with the injected time step.
        let model = applyGame (Tick dt) model

        // 2. Fade the transient HUD notice and the shot tracers.
        let notice =
            model.Notice
            |> Option.bind (fun (text, ttl) ->
                let ttl' = ttl - seconds
                if ttl' <= 0.0 then None else Some(text, ttl'))

        let shots =
            model.Shots
            |> List.choose (fun shot ->
                let ttl' = shot.Ttl - seconds
                if ttl' <= 0.0 then None else Some { shot with Ttl = ttl' })

        let effects =
            model.Effects
            |> List.choose (fun effect ->
                let ttl' = effect.Ttl - seconds
                if ttl' <= 0.0 then None else Some { effect with Ttl = ttl' })

        { model with
            Notice = notice
            Shots = shots
            Effects = effects }
