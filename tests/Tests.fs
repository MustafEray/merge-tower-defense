/// Pure unit tests for the Phase 1 core state engine.
///
/// Zero-dependency mini test runner so the suite compiles with any Fable
/// toolchain (including the dotnet-free fable-compiler-js path) and runs
/// under plain node. The two mutable counters below are the only mutation in
/// the whole repository, and they live outside the game core.
module MergeTowerDefense.Tests

open MergeTowerDefense.Shared
open MergeTowerDefense.State
open MergeTowerDefense.Ui

let mutable private passed = 0
let mutable private failed = 0

let private check (name: string) (condition: bool) =
    if condition then
        passed <- passed + 1
    else
        failed <- failed + 1
        printfn "FAIL  %s" name

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

let private gridSize n =
    match GridSize.tryCreate n with
    | Some s -> s
    | None -> failwith "test setup: invalid grid size"

let private coordIn (size: GridSize) r c =
    match Coord.tryCreate size r c with
    | Some coord -> coord
    | None -> failwith "test setup: invalid coord"

let private size5 = gridSize 5
let private at r c = coordIn size5 r c

/// Fold a message list through update, collecting every emitted event.
let private run (msgs: Msg list) (initial: GameState) : GameState * GameEvent list =
    msgs
    |> List.fold
        (fun (state, log) msg ->
            let state', events = update msg state
            state', log @ events)
        (initial, [])

let private fresh () = GameState.create size5

let private hasReject (reason: RejectReason) (events: GameEvent list) =
    events |> List.contains (ActionRejected reason)

let private dmg n =
    match Damage.tryCreate n with
    | Some d -> d
    | None -> failwith "test setup: invalid damage"

let private dt seconds =
    match DeltaTime.tryCreate seconds with
    | Some d -> d
    | None -> failwith "test setup: invalid delta time"

// ---------------------------------------------------------------------------
// Smart constructors: illegal values have no representation
// ---------------------------------------------------------------------------

let private testSmartConstructors () =
    check "GridSize rejects 1" (GridSize.tryCreate 1 = None)
    check "GridSize rejects 0" (GridSize.tryCreate 0 = None)
    check "GridSize rejects 13" (GridSize.tryCreate 13 = None)
    check "GridSize accepts 5" (GridSize.tryCreate 5 |> Option.isSome)

    check "Coord rejects negative row" (Coord.tryCreate size5 -1 0 = None)
    check "Coord rejects col out of bounds" (Coord.tryCreate size5 0 5 = None)
    check "Coord accepts corner" (Coord.tryCreate size5 4 4 |> Option.isSome)

    check "Damage rejects 0" (Damage.tryCreate 0 = None)
    check "Damage rejects negative" (Damage.tryCreate -3 = None)
    check "Damage accepts positive" (Damage.tryCreate 7 |> Option.isSome)

    check "Health rejects 0" (Health.tryCreate 0 = None)
    check "Health accepts positive" (Health.tryCreate 10 |> Option.isSome)

    check "DeltaTime rejects 0" (DeltaTime.tryCreate 0.0 = None)
    check "DeltaTime rejects negative" (DeltaTime.tryCreate -0.5 = None)
    check "DeltaTime rejects nan" (DeltaTime.tryCreate nan = None)
    check "DeltaTime rejects infinity" (DeltaTime.tryCreate infinity = None)
    check "DeltaTime accepts 0.016" (DeltaTime.tryCreate 0.016 |> Option.isSome)

    check "TowerLevel.next tops out at Level5" (TowerLevel.next Level5 = None)
    check "TowerLevel.next Level1 = Level2" (TowerLevel.next Level1 = Some Level2)

// ---------------------------------------------------------------------------
// Spawning towers / cell occupancy
// ---------------------------------------------------------------------------

let private testSpawning () =
    let state, events = fresh () |> run [ SpawnTower(Archer, at 0 0) ]

    check "spawn fills the cell"
        (match Grid.cellAt (at 0 0) state.Grid with
         | Occupied t -> t.Type = Archer && t.Level = Level1
         | Empty -> false)

    check "spawn emits TowerSpawned"
        (match events with
         | [ TowerSpawned(t, c) ] -> t.Type = Archer && c = at 0 0
         | _ -> false)

    let state2, events2 = state |> run [ SpawnTower(Cannon, at 0 0) ]
    check "spawn on occupied cell is rejected" (hasReject (SpawnCellOccupied(at 0 0)) events2)
    check "rejected spawn leaves state untouched" (state2 = state)

    check "grid reports a single tower" (Grid.towerCount state.Grid = 1)
    check "fresh grid is not full" (Grid.isFull state.Grid = false)

    let ids =
        [ SpawnTower(Archer, at 1 0); SpawnTower(Archer, at 1 1) ]
        |> fun msgs -> run msgs state
        |> fun (s, _) -> Grid.towers s.Grid |> List.map (fun (_, t) -> TowerId.value t.Id)

    check "tower ids are unique" (List.distinct ids = ids && List.length ids = 3)

// ---------------------------------------------------------------------------
// Drag state machine
// ---------------------------------------------------------------------------

let private testDragMachine () =
    let base_, _ = fresh () |> run [ SpawnTower(Archer, at 2 2) ]

    // StartDrag on empty cell
    let s, evs = base_ |> run [ StartDrag(at 0 0) ]
    check "drag from empty cell is rejected" (hasReject (OriginEmpty(at 0 0)) evs)
    check "rejected drag keeps state Idle" (s.Interaction = Idle)

    // StartDrag on occupied cell lifts the tower off the grid
    let dragging, evs = base_ |> run [ StartDrag(at 2 2) ]

    check "drag lifts tower into DragState"
        (match dragging.Interaction with
         | Dragging d -> d.Origin = at 2 2 && d.Tower.Type = Archer
         | Idle -> false)

    check "drag origin becomes empty" (Grid.cellAt (at 2 2) dragging.Grid = Empty)
    check "drag emits DragBegan"
        (match evs with
         | [ DragBegan(t, c) ] -> t.Type = Archer && c = at 2 2
         | _ -> false)

    // Second StartDrag while dragging
    let _, evs = dragging |> run [ StartDrag(at 2 2) ]
    check "drag while dragging is rejected" (hasReject AlreadyDragging evs)

    // Drop / Cancel with no drag in flight
    let _, evs = base_ |> run [ Drop(at 0 0) ]
    check "drop while idle is rejected" (hasReject NotDragging evs)
    let _, evs = base_ |> run [ CancelDrag ]
    check "cancel while idle is rejected" (hasReject NotDragging evs)

    // Spawning is frozen during a drag (keeps the origin provably empty)
    let s, evs = dragging |> run [ SpawnTower(Cannon, at 2 2) ]
    check "spawn during drag is rejected" (hasReject SpawnWhileDragging evs)
    check "spawn during drag changes nothing" (s = dragging)

    // Cancel restores the tower to its origin
    let s, evs = dragging |> run [ CancelDrag ]
    check "cancel returns tower to origin"
        (match Grid.cellAt (at 2 2) s.Grid with
         | Occupied t -> t.Type = Archer
         | Empty -> false)
    check "cancel goes back to Idle" (s.Interaction = Idle)
    check "cancel emits TowerReturned"
        (match evs with
         | [ TowerReturned(_, c) ] -> c = at 2 2
         | _ -> false)

    // Drop on the origin itself is a plain return
    let s, evs = dragging |> run [ Drop(at 2 2) ]
    check "drop on origin returns tower"
        (Grid.cellAt (at 2 2) s.Grid <> Empty && s.Interaction = Idle)
    check "drop on origin emits TowerReturned"
        (match evs with
         | [ TowerReturned _ ] -> true
         | _ -> false)

    // Drop on an empty cell moves the tower
    let s, evs = dragging |> run [ Drop(at 4 4) ]
    check "drop on empty cell moves tower"
        (Grid.cellAt (at 2 2) s.Grid = Empty
         && (match Grid.cellAt (at 4 4) s.Grid with
             | Occupied t -> t.Type = Archer
             | Empty -> false))
    check "move emits TowerMoved"
        (match evs with
         | [ TowerMoved(_, o, t) ] -> o = at 2 2 && t = at 4 4
         | _ -> false)
    check "move keeps exactly one tower" (Grid.towerCount s.Grid = 1)

// ---------------------------------------------------------------------------
// Merging
// ---------------------------------------------------------------------------

let private testMerging () =
    // Same type + same level → merge into next level
    let s, evs =
        fresh ()
        |> run
            [ SpawnTower(Archer, at 0 0)
              SpawnTower(Archer, at 0 1)
              StartDrag(at 0 0)
              Drop(at 0 1) ]

    check "merge yields Level2 tower at target"
        (match Grid.cellAt (at 0 1) s.Grid with
         | Occupied t -> t.Type = Archer && t.Level = Level2
         | Empty -> false)

    check "merge leaves origin empty" (Grid.cellAt (at 0 0) s.Grid = Empty)
    check "merge leaves exactly one tower" (Grid.towerCount s.Grid = 1)
    check "merge returns to Idle" (s.Interaction = Idle)

    check "merge emits TowersMerged with fresh id"
        (evs
         |> List.exists (fun e ->
             match e with
             | TowersMerged(a, b, result, c) ->
                 c = at 0 1
                 && result.Level = Level2
                 && result.Id <> a.Id
                 && result.Id <> b.Id
             | _ -> false))

    // Different types never merge
    let s, evs =
        fresh ()
        |> run
            [ SpawnTower(Archer, at 0 0)
              SpawnTower(Cannon, at 0 1)
              StartDrag(at 0 0)
              Drop(at 0 1) ]

    check "different types do not merge" (hasReject (IncompatibleTarget(at 0 1)) evs)
    check "rejected merge returns tower to origin"
        (match Grid.cellAt (at 0 0) s.Grid with
         | Occupied t -> t.Type = Archer
         | Empty -> false)
    check "rejected merge keeps both towers" (Grid.towerCount s.Grid = 2)

    // Different levels never merge
    let s, evs =
        fresh ()
        |> run
            [ SpawnTower(Frost, at 0 0)
              SpawnTower(Frost, at 0 1)
              SpawnTower(Frost, at 1 0)
              StartDrag(at 0 0)
              Drop(at 0 1) // Frost Level2 at (0,1)
              StartDrag(at 1 0)
              Drop(at 0 1) ] // Level1 onto Level2 → reject

    check "different levels do not merge" (hasReject (IncompatibleTarget(at 0 1)) evs)
    check "level mismatch keeps both towers" (Grid.towerCount s.Grid = 2)

// ---------------------------------------------------------------------------
// Max-level ceiling, reached only through legitimate merge chains
// ---------------------------------------------------------------------------

/// Keep merging any mergeable pair on the grid until none is left.
let rec private mergeDown (state: GameState) : GameState =
    let towers = Grid.towers state.Grid

    let pair =
        towers
        |> List.tryPick (fun (c1, t1) ->
            towers
            |> List.tryPick (fun (c2, t2) ->
                if c1 <> c2 && Tower.canMerge t1 t2 |> Option.isSome then
                    Some(c1, c2)
                else
                    None))

    match pair with
    | None -> state
    | Some(a, b) ->
        let state', _ = run [ StartDrag a; Drop b ] state
        mergeDown state'

let private testMaxLevel () =
    // 32 Level1 archers on an 8x8 board merge down to two Level5 towers.
    let size8 = gridSize 8
    let coord8 r c = coordIn size8 r c

    let spawns =
        [ for i in 0 .. 31 -> SpawnTower(Archer, coord8 (i / 8) (i % 8)) ]

    let filled, _ = GameState.create size8 |> run spawns
    let merged = mergeDown filled

    let levels =
        Grid.towers merged.Grid |> List.map (fun (_, t) -> t.Level)

    check "merge chain reduces 32 towers to 2" (List.length levels = 2)
    check "merge chain reaches Level5" (levels = [ Level5; Level5 ])

    // Two max-level towers refuse to merge and the drag resolves cleanly.
    match Grid.towers merged.Grid |> List.map fst with
    | [ a; b ] ->
        let s, evs = merged |> run [ StartDrag a; Drop b ]
        check "max-level merge is rejected" (hasReject (MergeAtMaxLevel b) evs)
        check "max-level towers both survive" (Grid.towerCount s.Grid = 2)
        check "max-level reject returns to Idle" (s.Interaction = Idle)
        check "max-level reject returns tower to origin" (Grid.cellAt a s.Grid <> Empty)
    | _ -> check "expected exactly two towers after mergeDown" false

// ---------------------------------------------------------------------------
// previewDrop stays consistent with the real Drop transition
// ---------------------------------------------------------------------------

let private testPreviewConsistency () =
    let state, _ =
        fresh ()
        |> run
            [ SpawnTower(Archer, at 0 0) // dragged
              SpawnTower(Archer, at 0 1) // merge partner
              SpawnTower(Cannon, at 0 2) // incompatible
              StartDrag(at 0 0) ]

    check "preview is None while idle" (previewDrop (at 0 0) (fresh ()) = None)

    let classifyPreview target =
        match previewDrop target state with
        | Some MoveHere -> "move"
        | Some(MergeHere _) -> "merge"
        | Some ReturnToOrigin -> "return"
        | Some Blocked -> "blocked"
        | None -> "none"

    let classifyDrop target =
        let _, evs = update (Drop target) state

        if evs |> List.exists (fun e -> match e with TowersMerged _ -> true | _ -> false) then "merge"
        elif evs |> List.exists (fun e -> match e with TowerMoved _ -> true | _ -> false) then "move"
        elif evs |> List.exists (fun e -> match e with ActionRejected _ -> true | _ -> false) then "blocked"
        else "return"

    let allAgree =
        Grid.coords state.Grid
        |> List.forall (fun c -> classifyPreview c = classifyDrop c)

    check "previewDrop matches update on every cell" allAgree
    check "preview flags merge target" (classifyPreview (at 0 1) = "merge")
    check "preview flags blocked target" (classifyPreview (at 0 2) = "blocked")
    check "preview flags origin return" (classifyPreview (at 0 0) = "return")

// ---------------------------------------------------------------------------
// Enemies
// ---------------------------------------------------------------------------

let private testEnemies () =
    let state, evs = fresh () |> run [ SpawnEnemy Grunt; SpawnEnemy Runner ]

    check "spawned enemies are tracked" (List.length state.Enemies = 2)
    check "enemies spawn at path start"
        (state.Enemies
         |> List.forall (fun e -> PathProgress.value e.Progress = 0.0))
    check "spawn emits EnemySpawned"
        (evs |> List.forall (fun e -> match e with EnemySpawned _ -> true | _ -> false))

    // Small step: everyone advances, nobody exits
    let s, evs = state |> run [ AdvanceEnemies(dt 1.0) ]
    check "small step keeps all enemies" (List.length s.Enemies = 2 && evs = [])
    check "small step moves every enemy"
        (s.Enemies |> List.forall (fun e -> PathProgress.value e.Progress > 0.0))
    check "faster type is further along"
        (match s.Enemies with
         | [ grunt; runner ] -> PathProgress.value runner.Progress > PathProgress.value grunt.Progress
         | _ -> false)

    // Huge step: everyone reaches the goal and leaves the field
    let s, evs = state |> run [ AdvanceEnemies(dt 1000.0) ]
    check "reaching the goal removes enemies" (s.Enemies = [])
    check "each exit emits EnemyReachedGoal"
        (List.length evs = 2
         && evs |> List.forall (fun e -> match e with EnemyReachedGoal _ -> true | _ -> false))

    // Damage: survive, then die
    let grunt = List.head state.Enemies // 20 hp

    let s, evs = state |> run [ HitEnemy(grunt.Id, dmg 15) ]
    check "wounded enemy survives with reduced hp"
        (match evs with
         | [ EnemyDamaged(id, remaining) ] -> id = grunt.Id && Health.value remaining = 5
         | _ -> false)
    check "wounded enemy stays on the field" (List.length s.Enemies = 2)

    let s2, evs = s |> run [ HitEnemy(grunt.Id, dmg 5) ]
    check "lethal damage kills"
        (match evs with
         | [ EnemyKilled id ] -> id = grunt.Id
         | _ -> false)
    check "killed enemy is removed" (List.length s2.Enemies = 1)

    let _, evs = s2 |> run [ HitEnemy(grunt.Id, dmg 1) ]
    check "hitting a dead enemy is rejected" (hasReject (UnknownEnemy grunt.Id) evs)

    check "overkill also kills"
        (match state |> run [ HitEnemy(grunt.Id, dmg 9999) ] with
         | _, [ EnemyKilled _ ] -> true
         | _ -> false)

// ---------------------------------------------------------------------------
// Immutability spot checks
// ---------------------------------------------------------------------------

let private testImmutability () =
    let before = fresh ()
    let after, _ = before |> run [ SpawnTower(Archer, at 0 0); SpawnEnemy Boss ]

    check "update never mutates the old state"
        (Grid.towerCount before.Grid = 0
         && before.Enemies = []
         && Grid.towerCount after.Grid = 1
         && List.length after.Enemies = 1)

    let dragging, _ = after |> run [ StartDrag(at 0 0) ]
    check "lifting does not mutate the previous grid"
        (Grid.cellAt (at 0 0) after.Grid <> Empty
         && Grid.cellAt (at 0 0) dragging.Grid = Empty)

// ---------------------------------------------------------------------------
// Derived stats
// ---------------------------------------------------------------------------

let private testStats () =
    let idA, gen = TowerIdGen.next TowerIdGen.initial
    let idB, _ = TowerIdGen.next gen
    let archer1 = { Id = idA; Type = Archer; Level = Level1 }
    let archer3 = { Id = idB; Type = Archer; Level = Level3 }

    check "stats scale with level"
        ((Tower.stats archer3).Damage = 4 * (Tower.stats archer1).Damage)
    check "range grows with level"
        ((Tower.stats archer3).Range > (Tower.stats archer1).Range)
    check "same tower cannot merge with itself" (Tower.canMerge archer1 archer1 = None)

// ---------------------------------------------------------------------------
// UI layer (Phase 2): layout math, hit testing, HUD transitions
// ---------------------------------------------------------------------------

let private uiLayoutTests () =
    let layout = layoutFor size5

    // cellAtPoint must be the exact inverse of cellCenter on every cell.
    let allRoundTrip =
        GameState.create size5
        |> fun s -> Grid.coords s.Grid
        |> List.forall (fun coord ->
            let x, y = cellCenter layout coord
            cellAtPoint layout size5 x y = Some coord)

    check "cellAtPoint inverts cellCenter on every cell" allRoundTrip
    check "point left of grid maps to no cell" (cellAtPoint layout size5 (layout.GridLeft - 5.0) layout.GridTop = None)
    check "point above grid maps to no cell" (cellAtPoint layout size5 layout.GridLeft (layout.GridTop - 5.0) = None)
    check "point past last cell maps to no cell"
        (cellAtPoint layout size5 (layout.GridLeft + 5.0 * layout.CellSize + 1.0) (layout.GridTop + 1.0) = None)

    check "enemy at path start renders at lane left"
        (let e, _ = Enemy.spawn EnemyIdGen.initial Grunt
         enemyX layout e.Progress = layout.PathLeft)

let private uiHudTests () =
    let model = init size5

    // Buying: deterministic type cycle, first empty cell, gold decrement.
    let m1 = updateUi BuyTower model
    check "buy places a tower on the first empty cell"
        (match Grid.cellAt (at 0 0) m1.Game.Grid with
         | Occupied t -> t.Type = Archer && t.Level = Level1
         | Empty -> false)
    check "buy deducts gold" (m1.Gold = model.Gold - towerCost)

    let m2 = updateUi BuyTower m1
    check "second buy cycles to the next type"
        (match Grid.cellAt (at 0 1) m2.Game.Grid with
         | Occupied t -> t.Type = Cannon
         | Empty -> false)

    check "buy without enough gold is a no-op"
        (let poor = { m2 with Gold = towerCost - 1 }
         updateUi BuyTower poor = poor)

    check "cannot buy while dragging"
        (let dragging = updateUi (GameMsg(StartDrag(at 0 0))) m2
         canBuy dragging = false)

    // Frame: advances enemies with injected time and runs the demo spawner.
    let stepped =
        updateUi (Frame(dt 1.0)) { model with Game = fst (update (SpawnEnemy Grunt) model.Game) }

    check "frame advances enemy progress"
        (match stepped.Game.Enemies with
         | [ e ] -> PathProgress.value e.Progress > 0.0
         | _ -> false)

    let afterDemo =
        [ 1 .. 4 ] |> List.fold (fun m _ -> updateUi (Frame(dt 1.0)) m) model

    check "demo spawner emits an enemy after its period"
        (List.length afterDemo.Game.Enemies >= 1)

    // Notices: set by noteworthy events, silent otherwise, and they expire.
    let mismatch =
        [ GameMsg(SpawnTower(Archer, at 3 0))
          GameMsg(SpawnTower(Cannon, at 3 1))
          GameMsg(StartDrag(at 3 0))
          GameMsg(Drop(at 3 1)) ]
        |> List.fold (fun m msg -> updateUi msg m) model

    check "incompatible merge raises a HUD notice" (mismatch.Notice |> Option.isSome)
    check "notice expires after its time-to-live"
        (let faded = [ 1 .. 4 ] |> List.fold (fun m _ -> updateUi (Frame(dt 1.0)) m) mismatch
         faded.Notice = None)

    check "plain pointer movement raises no notice"
        ((updateUi (PointerMoved(Some(at 1 1), Some(10.0, 10.0))) model).Notice = None)

// ---------------------------------------------------------------------------
// Entry point
// ---------------------------------------------------------------------------

[<EntryPoint>]
let main _argv =
    testSmartConstructors ()
    testSpawning ()
    testDragMachine ()
    testMerging ()
    testMaxLevel ()
    testPreviewConsistency ()
    testEnemies ()
    testImmutability ()
    testStats ()
    uiLayoutTests ()
    uiHudTests ()

    printfn ""
    printfn "%d passed, %d failed" passed failed

    if failed > 0 then
        failwith "test suite failed"

    0
