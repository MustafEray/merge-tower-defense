/// Pure UI-layer state: everything the render/HUD layers need that is not
/// core game logic — canvas layout math, pointer-to-cell hit testing, the
/// transient HUD notice, shot tracers, juice effects, the wave banner and
/// the frame message that feeds injected DeltaTime into the core engine.
///
/// This module stays as pure as Shared/State: no DOM, no PixiJS, no clock,
/// no audio API. Sounds leave this layer as data — updateUi returns the
/// SoundCue list a transition earned, and the impure shell decides how to
/// play them (mirroring how update returns GameEvents).
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

/// Transient, purely visual celebrations. Positions are in cell units.
type EffectKind =
    | KillBurst
    | MergeRing
    | SpawnRing
    | LeakFlash
    | GoldFloat of string

type Effect =
    { Kind: EffectKind
      Pos: float * float
      /// Seconds since the effect was born; pruned past its duration.
      Age: float }

/// How long each effect kind lives, in seconds.
let effectDuration =
    function
    | KillBurst -> 0.45
    | MergeRing -> 0.5
    | SpawnRing -> 0.4
    | LeakFlash -> 0.5
    | GoldFloat _ -> 0.9

/// Sounds a transition earned. Pure data; the shell synthesises them.
type SoundCue =
    | ShootCue
    | KillCue
    | MergeCue
    | BuyCue
    | LeakCue
    | WaveStartCue
    | WaveClearCue
    | LostCue
    | RejectCue

/// Colour family of the HUD notice line.
type NoticeKind =
    | Info
    | Good
    | Bad

type UiModel =
    { Game: GameState
      /// Cell currently under the pointer, if any.
      Hover: Coord option
      /// Raw pointer position in canvas pixels (drives the drag ghost).
      Pointer: (float * float) option
      /// Transient HUD message: text, colour family, remaining seconds.
      Notice: (string * NoticeKind * float) option
      /// Fading shot tracers for the renderer.
      Shots: Shot list
      /// Fading celebration effects for the renderer.
      Effects: Effect list
      /// "Wave N" banner: wave number and its age in seconds.
      Banner: (int * float) option
      /// Accumulated play-session seconds, for ambient pulses.
      Clock: float
      /// Suppresses all sound cues while set.
      Muted: bool }

type UiMsg =
    /// Forward a message to the core engine untouched.
    | GameMsg of Msg
    /// Pointer moved: hovered cell (if any) and raw canvas position.
    | PointerMoved of Coord option * (float * float) option
    /// HUD "buy tower" button for the given type.
    | Buy of TowerType
    /// HUD restart after a game over.
    | Restart
    /// HUD/keyboard mute toggle.
    | ToggleMute
    /// One render-loop frame worth of injected time.
    | Frame of DeltaTime

let init (size: GridSize) : UiModel =
    { Game = GameState.create size
      Hover = None
      Pointer = None
      Notice = None
      Shots = []
      Effects = []
      Banner = None
      Clock = 0.0
      Muted = false }

// ---------------------------------------------------------------------------
// HUD-facing helpers
// ---------------------------------------------------------------------------

let private noticeTtl = 2.5
let shotTtl = 0.12
let bannerDuration = 1.6

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
let private noticeOf (event: GameEvent) : (int * NoticeKind * string) option =
    match event with
    | GameOver waves -> Some(100, Bad, sprintf "Game over — you survived %d wave(s)." waves)
    | ActionRejected(NotEnoughGold required) -> Some(80, Bad, sprintf "Not enough gold (need %d)." required)
    | ActionRejected(MergeAtMaxLevel _) -> Some(80, Bad, "Already at max level.")
    | ActionRejected(IncompatibleTarget _) -> Some(80, Bad, "Towers must share type and level to merge.")
    | ActionRejected(SpawnCellOccupied _) -> Some(80, Bad, "That cell is occupied.")
    | ActionRejected SpawnWhileDragging -> Some(80, Bad, "Finish the drag first.")
    | ActionRejected WaveAlreadyRunning -> Some(80, Bad, "A wave is already running.")
    | TowersMerged(_, _, result, _) ->
        Some(70, Good, sprintf "Merged! New tower is level %d." (TowerLevel.rank result.Level))
    | WaveCompleted(wave, bonus) -> Some(60, Good, sprintf "Wave %d cleared! +%d gold." wave bonus)
    | WaveStarted wave -> Some(50, Info, sprintf "Wave %d incoming!" wave)
    | LifeLost remaining -> Some(40, Bad, sprintf "An enemy got through! %d lives left." remaining)
    | _ -> None

let private noticeFor (events: GameEvent list) : (string * NoticeKind) option =
    match events |> List.choose noticeOf with
    | [] -> None
    | picks ->
        let _, kind, text = picks |> List.maxBy (fun (priority, _, _) -> priority)
        Some(text, kind)

let private cueOf (event: GameEvent) : SoundCue option =
    match event with
    | TowerFired _ -> Some ShootCue
    | EnemyKilled _ -> Some KillCue
    | TowersMerged _ -> Some MergeCue
    | TowerBought _
    | TowerSpawned _ -> Some BuyCue
    | LifeLost _ -> Some LeakCue
    | WaveStarted _ -> Some WaveStartCue
    | WaveCompleted _ -> Some WaveClearCue
    | GameOver _ -> Some LostCue
    | ActionRejected(NotEnoughGold _)
    | ActionRejected(MergeAtMaxLevel _)
    | ActionRejected(IncompatibleTarget _)
    | ActionRejected(SpawnCellOccupied _)
    | ActionRejected WaveAlreadyRunning -> Some RejectCue
    | _ -> None

// ---------------------------------------------------------------------------
// UI transition function (pure)
// ---------------------------------------------------------------------------

/// Runs a core message, harvesting notices, sound cues, shot tracers,
/// celebration effects and the wave banner from the emitted events. Kill
/// positions come from the pre-transition state (the dead are gone after).
let private applyGame (msg: Msg) (model: UiModel) : UiModel * SoundCue list =
    let positionsBefore =
        model.Game.Enemies
        |> List.map (fun e -> e.Id, Enemy.positionOn model.Game.Path e)
        |> Map.ofList

    let goalPos =
        match List.tryLast (Path.waypoints model.Game.Path) with
        | Some p -> p
        | None -> 0.0, 0.0 // unreachable: a Path always has ≥ 2 waypoints

    let game, events = update msg model.Game

    let notice =
        match noticeFor events with
        | Some(text, kind) -> Some(text, kind, noticeTtl)
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

    let born kind pos = { Kind = kind; Pos = pos; Age = 0.0 }

    let newEffects =
        events
        |> List.collect (fun event ->
            match event with
            | EnemyKilled(id, bounty) ->
                match Map.tryFind id positionsBefore with
                | Some pos -> [ born KillBurst pos; born (GoldFloat(sprintf "+%d" bounty)) pos ]
                | None -> []
            | TowersMerged(_, _, _, at) -> [ born MergeRing (Coord.center at) ]
            | TowerBought(_, at, cost) ->
                [ born SpawnRing (Coord.center at)
                  born (GoldFloat(sprintf "-%d" cost)) (Coord.center at) ]
            | TowerSpawned(_, at) -> [ born SpawnRing (Coord.center at) ]
            | LifeLost _ -> [ born LeakFlash goalPos ]
            | WaveCompleted(_, bonus) -> [ born (GoldFloat(sprintf "+%d" bonus)) goalPos ]
            | _ -> [])

    let banner =
        events
        |> List.tryPick (fun event ->
            match event with
            | WaveStarted wave -> Some(wave, 0.0)
            | _ -> None)
        |> Option.orElse model.Banner

    let cues =
        if model.Muted then
            []
        else
            events |> List.choose cueOf |> List.distinct

    { model with
        Game = game
        Notice = notice
        Shots = newShots @ model.Shots
        Effects = newEffects @ model.Effects
        Banner = banner },
    cues

let updateUi (msg: UiMsg) (model: UiModel) : UiModel * SoundCue list =
    match msg with
    | GameMsg gameMsg -> applyGame gameMsg model

    | PointerMoved(hover, pointer) ->
        { model with
            Hover = hover
            Pointer = pointer },
        []

    | Buy towerType ->
        match firstEmptyCell model.Game.Grid with
        | Some cell -> applyGame (BuyTower(towerType, cell)) model
        | None ->
            { model with
                Notice = Some("No empty cell for a new tower.", Bad, noticeTtl) },
            (if model.Muted then [] else [ RejectCue ])

    | Restart -> init (Grid.size model.Game.Grid), []

    | ToggleMute -> { model with Muted = not model.Muted }, []

    | Frame dt ->
        let seconds = DeltaTime.seconds dt

        // 1. Advance the core simulation with the injected time step.
        let model, cues = applyGame (Tick dt) model

        // 2. Age and prune all transient visuals.
        let notice =
            model.Notice
            |> Option.bind (fun (text, kind, ttl) ->
                let ttl' = ttl - seconds
                if ttl' <= 0.0 then None else Some(text, kind, ttl'))

        let shots =
            model.Shots
            |> List.choose (fun shot ->
                let ttl' = shot.Ttl - seconds
                if ttl' <= 0.0 then None else Some { shot with Ttl = ttl' })

        let effects =
            model.Effects
            |> List.choose (fun effect ->
                let age' = effect.Age + seconds
                if age' >= effectDuration effect.Kind then None else Some { effect with Age = age' })

        let banner =
            model.Banner
            |> Option.bind (fun (wave, age) ->
                let age' = age + seconds
                if age' >= bannerDuration then None else Some(wave, age'))

        { model with
            Notice = notice
            Shots = shots
            Effects = effects
            Banner = banner
            Clock = model.Clock + seconds },
        cues
