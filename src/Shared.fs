/// Domain model for Merge Tower Defense.
///
/// Pure data only: no IO, no rendering, no mutation, no clock.
/// Design rule — make illegal states unrepresentable: every validated value
/// has a `private` representation and can only be produced through the smart
/// constructors in this file, so an invalid instance has no way to exist.
module MergeTowerDefense.Shared

// ---------------------------------------------------------------------------
// Identifiers
// ---------------------------------------------------------------------------

/// Opaque tower identifier. It can only be minted through TowerIdGen, so a
/// hand-crafted or duplicated id is unrepresentable.
type TowerId = private TowerId of int

module TowerId =
    let value (TowerId n) = n

/// Monotonic id source, threaded through the game state (pure, no globals).
type TowerIdGen = private TowerIdGen of int

module TowerIdGen =
    let initial = TowerIdGen 1
    let next (TowerIdGen n) = TowerId n, TowerIdGen(n + 1)

type EnemyId = private EnemyId of int

module EnemyId =
    let value (EnemyId n) = n

type EnemyIdGen = private EnemyIdGen of int

module EnemyIdGen =
    let initial = EnemyIdGen 1
    let next (EnemyIdGen n) = EnemyId n, EnemyIdGen(n + 1)

// ---------------------------------------------------------------------------
// Towers
// ---------------------------------------------------------------------------

/// Tower level as an enumeration instead of an int: level 0, level 42 or a
/// negative level simply has no representation, and the merge chain gets a
/// type-level ceiling.
type TowerLevel =
    | Level1
    | Level2
    | Level3
    | Level4
    | Level5

module TowerLevel =
    let maxLevel = Level5

    /// The level a successful merge produces. None at the ceiling.
    let next =
        function
        | Level1 -> Some Level2
        | Level2 -> Some Level3
        | Level3 -> Some Level4
        | Level4 -> Some Level5
        | Level5 -> None

    /// 1-based rank, for stat scaling and UI.
    let rank =
        function
        | Level1 -> 1
        | Level2 -> 2
        | Level3 -> 3
        | Level4 -> 4
        | Level5 -> 5

type TowerType =
    | Archer
    | Cannon
    | Frost

type Tower =
    { Id: TowerId
      Type: TowerType
      Level: TowerLevel }

type TowerStats =
    { Damage: int
      Range: float
      CooldownMs: int }

module Tower =
    let private baseStats =
        function
        | Archer -> { Damage = 4; Range = 3.0; CooldownMs = 600 }
        | Cannon -> { Damage = 10; Range = 2.0; CooldownMs = 1500 }
        | Frost -> { Damage = 2; Range = 2.5; CooldownMs = 900 }

    /// Stats derive from type + level and are never stored, so they can
    /// never disagree with the tower they describe.
    let stats (tower: Tower) =
        let b = baseStats tower.Type
        let r = TowerLevel.rank tower.Level

        { b with
            Damage = b.Damage * pown 2 (r - 1)
            Range = b.Range + 0.25 * float (r - 1) }

    /// Two towers merge iff they are distinct, same type and same level, and
    /// below the ceiling. Returns the level the merged tower would have.
    let canMerge (a: Tower) (b: Tower) =
        if a.Id <> b.Id && a.Type = b.Type && a.Level = b.Level then
            TowerLevel.next a.Level
        else
            None

// ---------------------------------------------------------------------------
// Grid
// ---------------------------------------------------------------------------

/// Validated NxN board size.
type GridSize = private GridSize of int

module GridSize =
    let minSize = 2
    let maxSize = 12

    let tryCreate n =
        if n >= minSize && n <= maxSize then Some(GridSize n) else None

    let value (GridSize n) = n

/// A cell coordinate guaranteed to lie inside the grid it was created for:
/// out-of-bounds coordinates are unrepresentable.
type Coord =
    private
        { Row: int
          Col: int }

module Coord =
    let tryCreate (size: GridSize) row col =
        let n = GridSize.value size

        if row >= 0 && row < n && col >= 0 && col < n then
            Some { Row = row; Col = col }
        else
            None

    let row c = c.Row
    let col c = c.Col

type CellState =
    | Empty
    | Occupied of Tower

/// The board. Absence in the map IS the empty cell — there is no separate
/// "empty" marker that could drift out of sync with the tower map.
type Grid =
    private
        { Size: GridSize
          Towers: Map<Coord, Tower> }

module Grid =
    let create size = { Size = size; Towers = Map.empty }

    let size grid = grid.Size

    let cellAt coord grid =
        match Map.tryFind coord grid.Towers with
        | Some tower -> Occupied tower
        | None -> Empty

    /// All coordinates of the board, row-major.
    let coords grid =
        let n = GridSize.value grid.Size

        [ for r in 0 .. n - 1 do
              for c in 0 .. n - 1 -> { Row = r; Col = c } ]

    /// All towers on the board with their coordinates.
    let towers grid = Map.toList grid.Towers

    let towerCount grid = Map.count grid.Towers

    let isFull grid =
        let n = GridSize.value grid.Size
        Map.count grid.Towers = n * n

    /// Place a tower on an EMPTY cell. An occupied target yields None, so
    /// silently overwriting (losing) a tower is unrepresentable.
    let tryPlace coord tower grid =
        match cellAt coord grid with
        | Occupied _ -> None
        | Empty ->
            Some
                { grid with
                    Towers = Map.add coord tower grid.Towers }

    /// Lift a tower off the board, returning it together with the grid that
    /// no longer contains it. Lifting from an empty cell yields None.
    let tryLift coord grid =
        match Map.tryFind coord grid.Towers with
        | Some tower ->
            Some(
                tower,
                { grid with
                    Towers = Map.remove coord grid.Towers }
            )
        | None -> None

// ---------------------------------------------------------------------------
// Combat primitives
// ---------------------------------------------------------------------------

/// Strictly positive damage.
type Damage = private Damage of int

module Damage =
    let tryCreate n = if n > 0 then Some(Damage n) else None
    let value (Damage n) = n

/// Strictly positive hit points. "Alive with zero or negative HP" has no
/// representation; death is an explicit outcome of applyDamage, not a flag.
type Health = private Health of int

module Health =
    let tryCreate n = if n > 0 then Some(Health n) else None
    let value (Health n) = n

type AttackResult =
    | Survived of Health
    | Killed

module AttackResult =
    let ofDamage (Damage dmg) (Health hp) =
        let remaining = hp - dmg
        if remaining > 0 then Survived(Health remaining) else Killed

// ---------------------------------------------------------------------------
// Time and movement
// ---------------------------------------------------------------------------

/// A positive, finite time step in seconds. Time is always injected from the
/// outside; the core never reads a clock.
type DeltaTime = private DeltaTime of float

module DeltaTime =
    let tryCreate seconds =
        if seconds > 0.0 && not (System.Double.IsNaN seconds) && seconds < infinity then
            Some(DeltaTime seconds)
        else
            None

    let seconds (DeltaTime s) = s

/// Normalised position along the enemy path, always within [0, 1).
/// Reaching the end is not a state — it is the ReachedGoal transition — so
/// "an enemy standing beyond the exit" is unrepresentable.
type PathProgress = private PathProgress of float

module PathProgress =
    let start = PathProgress 0.0
    let value (PathProgress p) = p

type MoveResult =
    | Moved of PathProgress
    | ReachedGoal

// ---------------------------------------------------------------------------
// Enemies
// ---------------------------------------------------------------------------

type EnemyType =
    | Grunt
    | Runner
    | Tank
    | Boss

module EnemyType =
    /// Base hit points per type. Values must stay strictly positive: they
    /// feed the private Health constructor in Enemy.spawn.
    let baseHealth =
        function
        | Grunt -> 20
        | Runner -> 12
        | Tank -> 60
        | Boss -> 250

    /// Path fraction travelled per second.
    let speed =
        function
        | Grunt -> 0.08
        | Runner -> 0.16
        | Tank -> 0.05
        | Boss -> 0.03

type Enemy =
    { Id: EnemyId
      Type: EnemyType
      Health: Health
      Progress: PathProgress }

module Enemy =
    /// Spawns at the path start with full, type-defined health.
    let spawn (gen: EnemyIdGen) (enemyType: EnemyType) : Enemy * EnemyIdGen =
        let id, gen' = EnemyIdGen.next gen

        { Id = id
          Type = enemyType
          Health = Health(EnemyType.baseHealth enemyType)
          Progress = PathProgress.start },
        gen'

    /// Pure movement step; the caller decides what ReachedGoal means.
    let advance (dt: DeltaTime) (enemy: Enemy) : MoveResult =
        let (PathProgress p) = enemy.Progress
        let p' = p + EnemyType.speed enemy.Type * DeltaTime.seconds dt
        if p' >= 1.0 then ReachedGoal else Moved(PathProgress p')
