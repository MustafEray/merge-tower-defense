/// The game state machine for Merge Tower Defense.
///
/// Every change to the game flows through the single pure function
///     update : Msg -> GameState -> GameState * GameEvent list
/// No mutation, no IO, no clock: time arrives as a DeltaTime message and the
/// UI layer (added in Phase 2) only sends Msg values and renders GameEvents.
module MergeTowerDefense.State

open MergeTowerDefense.Shared

// ---------------------------------------------------------------------------
// Interaction state machine (drag & drop)
// ---------------------------------------------------------------------------

/// An in-flight drag gesture. The dragged tower has been lifted OFF the grid
/// and lives only here — it cannot exist in two places at once, and a drag
/// without a tower is unrepresentable.
type DragState =
    { Origin: Coord
      Tower: Tower }

type Interaction =
    | Idle
    | Dragging of DragState

// ---------------------------------------------------------------------------
// Game state
// ---------------------------------------------------------------------------

type GameState =
    { Grid: Grid
      Interaction: Interaction
      Enemies: Enemy list
      TowerIds: TowerIdGen
      EnemyIds: EnemyIdGen }

module GameState =
    let create (size: GridSize) =
        { Grid = Grid.create size
          Interaction = Idle
          Enemies = []
          TowerIds = TowerIdGen.initial
          EnemyIds = EnemyIdGen.initial }

// ---------------------------------------------------------------------------
// Messages, events, rejections
// ---------------------------------------------------------------------------

/// Why an action was refused. Rejections never mutate state; they only show
/// up as ActionRejected events so the UI can explain itself.
type RejectReason =
    | NotDragging
    | AlreadyDragging
    | OriginEmpty of Coord
    | IncompatibleTarget of Coord
    | MergeAtMaxLevel of Coord
    | SpawnCellOccupied of Coord
    | SpawnWhileDragging
    | UnknownEnemy of EnemyId

/// Facts about what a transition did — the UI renders these; tests assert on
/// them. Events describe the past, so they carry the concrete values.
type GameEvent =
    | DragBegan of tower: Tower * origin: Coord
    | TowerMoved of tower: Tower * origin: Coord * target: Coord
    | TowersMerged of dragged: Tower * absorbed: Tower * result: Tower * at: Coord
    | TowerReturned of tower: Tower * origin: Coord
    | TowerSpawned of tower: Tower * at: Coord
    | EnemySpawned of Enemy
    | EnemyReachedGoal of EnemyId
    | EnemyDamaged of EnemyId * remaining: Health
    | EnemyKilled of EnemyId
    | ActionRejected of RejectReason

/// Everything the outside world (UI, wave scheduler, tests) may ask of the
/// game. Drag & drop arrives as three separate gestures.
type Msg =
    | StartDrag of Coord
    | Drop of Coord
    | CancelDrag
    | SpawnTower of TowerType * Coord
    | SpawnEnemy of EnemyType
    | AdvanceEnemies of DeltaTime
    | HitEnemy of EnemyId * Damage

// ---------------------------------------------------------------------------
// Drop preview (pure derivation for UI highlighting)
// ---------------------------------------------------------------------------

/// What dropping on a given cell would do right now.
type DropPreview =
    | MoveHere
    | MergeHere of TowerLevel
    | ReturnToOrigin
    | Blocked

/// Pure preview for hover highlights while dragging. Consistency with the
/// actual Drop transition is asserted by the test suite.
let previewDrop (target: Coord) (state: GameState) : DropPreview option =
    match state.Interaction with
    | Idle -> None
    | Dragging drag ->
        if target = drag.Origin then
            Some ReturnToOrigin
        else
            match Grid.cellAt target state.Grid with
            | Empty -> Some MoveHere
            | Occupied other ->
                match Tower.canMerge drag.Tower other with
                | Some level -> Some(MergeHere level)
                | None -> Some Blocked

// ---------------------------------------------------------------------------
// Transition function
// ---------------------------------------------------------------------------

/// Places a tower on a cell that the surrounding transition has just proven
/// empty (a freshly lifted drag origin, or a cell that cellAt/tryLift
/// reported empty within the same pure transition). The failure branch is
/// unreachable: SpawnTower is rejected mid-drag and every other change goes
/// through this same update function, so nothing can occupy the cell in
/// between.
let private placeOnEmpty (coord: Coord) (tower: Tower) (grid: Grid) : Grid =
    match Grid.tryPlace coord tower grid with
    | Some grid' -> grid'
    | None -> failwith "unreachable: cell was proven empty within this transition"

let update (msg: Msg) (state: GameState) : GameState * GameEvent list =
    match msg, state.Interaction with

    // -- drag & drop / merge ------------------------------------------------

    | StartDrag origin, Idle ->
        match Grid.tryLift origin state.Grid with
        | None -> state, [ ActionRejected(OriginEmpty origin) ]
        | Some(tower, grid) ->
            { state with
                Grid = grid
                Interaction = Dragging { Origin = origin; Tower = tower } },
            [ DragBegan(tower, origin) ]

    | StartDrag _, Dragging _ -> state, [ ActionRejected AlreadyDragging ]

    | Drop _, Idle -> state, [ ActionRejected NotDragging ]

    | Drop target, Dragging drag when target = drag.Origin ->
        { state with
            Grid = placeOnEmpty drag.Origin drag.Tower state.Grid
            Interaction = Idle },
        [ TowerReturned(drag.Tower, drag.Origin) ]

    | Drop target, Dragging drag ->
        match Grid.tryLift target state.Grid with
        | None ->
            // Target cell is empty: plain move.
            { state with
                Grid = placeOnEmpty target drag.Tower state.Grid
                Interaction = Idle },
            [ TowerMoved(drag.Tower, drag.Origin, target) ]
        | Some(targetTower, gridWithoutTarget) ->
            match Tower.canMerge drag.Tower targetTower with
            | Some mergedLevel ->
                let mergedId, towerIds = TowerIdGen.next state.TowerIds

                let merged =
                    { Id = mergedId
                      Type = targetTower.Type
                      Level = mergedLevel }

                { state with
                    Grid = placeOnEmpty target merged gridWithoutTarget
                    Interaction = Idle
                    TowerIds = towerIds },
                [ TowersMerged(drag.Tower, targetTower, merged, target) ]
            | None ->
                let reason =
                    if drag.Tower.Type = targetTower.Type && drag.Tower.Level = targetTower.Level then
                        MergeAtMaxLevel target
                    else
                        IncompatibleTarget target

                { state with
                    Grid = placeOnEmpty drag.Origin drag.Tower state.Grid
                    Interaction = Idle },
                [ ActionRejected reason; TowerReturned(drag.Tower, drag.Origin) ]

    | CancelDrag, Dragging drag ->
        { state with
            Grid = placeOnEmpty drag.Origin drag.Tower state.Grid
            Interaction = Idle },
        [ TowerReturned(drag.Tower, drag.Origin) ]

    | CancelDrag, Idle -> state, [ ActionRejected NotDragging ]

    // -- tower spawning -----------------------------------------------------

    // Rejected mid-drag: this is what keeps the drag origin provably empty
    // for the whole gesture (see placeOnEmpty).
    | SpawnTower _, Dragging _ -> state, [ ActionRejected SpawnWhileDragging ]

    | SpawnTower(towerType, coord), Idle ->
        let id, towerIds = TowerIdGen.next state.TowerIds

        let tower =
            { Id = id
              Type = towerType
              Level = Level1 }

        match Grid.tryPlace coord tower state.Grid with
        | Some grid ->
            { state with
                Grid = grid
                TowerIds = towerIds },
            [ TowerSpawned(tower, coord) ]
        | None -> state, [ ActionRejected(SpawnCellOccupied coord) ]

    // -- enemies (independent of the drag gesture) --------------------------

    | SpawnEnemy enemyType, _ ->
        let enemy, enemyIds = Enemy.spawn state.EnemyIds enemyType

        { state with
            Enemies = state.Enemies @ [ enemy ]
            EnemyIds = enemyIds },
        [ EnemySpawned enemy ]

    | AdvanceEnemies dt, _ ->
        let step (survivors, events) enemy =
            match Enemy.advance dt enemy with
            | Moved progress -> { enemy with Progress = progress } :: survivors, events
            | ReachedGoal -> survivors, EnemyReachedGoal enemy.Id :: events

        let survivorsRev, eventsRev = List.fold step ([], []) state.Enemies

        { state with
            Enemies = List.rev survivorsRev },
        List.rev eventsRev

    | HitEnemy(enemyId, damage), _ ->
        match state.Enemies |> List.tryFind (fun e -> e.Id = enemyId) with
        | None -> state, [ ActionRejected(UnknownEnemy enemyId) ]
        | Some enemy ->
            match AttackResult.ofDamage damage enemy.Health with
            | Survived remaining ->
                let enemies =
                    state.Enemies
                    |> List.map (fun e -> if e.Id = enemyId then { e with Health = remaining } else e)

                { state with Enemies = enemies }, [ EnemyDamaged(enemyId, remaining) ]
            | Killed ->
                { state with
                    Enemies = state.Enemies |> List.filter (fun e -> e.Id <> enemyId) },
                [ EnemyKilled enemyId ]
