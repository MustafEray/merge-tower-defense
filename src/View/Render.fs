/// Procedural rendering with the PixiJS Graphics API — no external assets.
/// Pure "view = f(model)": every frame the dynamic layers are cleared and
/// redrawn from the current UiModel; nothing in here mutates game state.
module MergeTowerDefense.View.Render

open MergeTowerDefense.Shared
open MergeTowerDefense.State
open MergeTowerDefense.Ui
open MergeTowerDefense.Interop.Pixi

// ---------------------------------------------------------------------------
// Palette (procedural, per tower type and level)
// ---------------------------------------------------------------------------

let private archerShades = [| 0x2e7d32; 0x43a047; 0x66bb6a; 0x81c784; 0xa5d6a7 |]
let private cannonShades = [| 0xef6c00; 0xfb8c00; 0xffa726; 0xffb74d; 0xffcc80 |]
let private frostShades = [| 0x0277bd; 0x039be5; 0x29b6f6; 0x4fc3f7; 0x81d4fa |]

let private towerShade (towerType: TowerType) (rank: int) =
    let shades =
        match towerType with
        | Archer -> archerShades
        | Cannon -> cannonShades
        | Frost -> frostShades

    shades.[rank - 1]

let private towerBaseColor (towerType: TowerType) =
    match towerType with
    | Archer -> 0x66bb6a
    | Cannon -> 0xffa726
    | Frost -> 0x4fc3f7

let private enemyColor (enemyType: EnemyType) =
    match enemyType with
    | Grunt -> 0xb0bec5
    | Runner -> 0xffee58
    | Tank -> 0x8d6e63
    | Boss -> 0xab47bc

// ---------------------------------------------------------------------------
// Layers
// ---------------------------------------------------------------------------

/// Draw order, bottom to top: static board, overlay (highlights + ranges),
/// towers, enemies, drag ghost.
type Layers =
    { Static: Graphics
      Overlay: Graphics
      Towers: Graphics
      Enemies: Graphics
      Ghost: Graphics }

let createLayers (app: Application) : Layers =
    let make () =
        let g = createGraphics ()
        app.stage.addChild g |> ignore
        g

    { Static = make ()
      Overlay = make ()
      Towers = make ()
      Enemies = make ()
      Ghost = make () }

// ---------------------------------------------------------------------------
// Static board (drawn once)
// ---------------------------------------------------------------------------

let drawStatic (layout: Layout) (size: GridSize) (layers: Layers) : unit =
    let g = layers.Static
    let n = GridSize.value size
    let cell = layout.CellSize

    // Enemy lane strip with the demo path line (real path geometry: Phase 3).
    g
        .beginFill(0x202433, 1.0)
        .drawRoundedRect(layout.PathLeft, layout.LaneY - 26.0, layout.PathRight - layout.PathLeft, 52.0, 10.0)
        .endFill ()
    |> ignore

    g
        .lineStyle(3.0, 0x3a415f, 1.0)
        .moveTo(layout.PathLeft + 10.0, layout.LaneY)
        .lineTo(layout.PathRight - 10.0, layout.LaneY)
    |> ignore

    // Direction chevrons along the lane.
    for i in 0 .. 4 do
        let x =
            layout.PathLeft
            + (float i + 0.5) / 5.0 * (layout.PathRight - layout.PathLeft)

        g
            .lineStyle(2.0, 0x4c557a, 1.0)
            .moveTo(x - 4.0, layout.LaneY - 6.0)
            .lineTo(x + 4.0, layout.LaneY)
            .lineTo(x - 4.0, layout.LaneY + 6.0)
        |> ignore

    // Goal marker at the lane exit.
    g
        .lineStyle(0.0, 0, 0.0)
        .beginFill(0xef5350, 0.9)
        .drawRoundedRect(layout.PathRight - 8.0, layout.LaneY - 18.0, 6.0, 36.0, 2.0)
        .endFill ()
    |> ignore

    // Checkerboard grid cells.
    for row in 0 .. n - 1 do
        for col in 0 .. n - 1 do
            let x = layout.GridLeft + float col * cell
            let y = layout.GridTop + float row * cell
            let fill = if (row + col) % 2 = 0 then 0x232738 else 0x1f2333

            g
                .lineStyle(1.0, 0x2f3450, 1.0)
                .beginFill(fill, 1.0)
                .drawRect(x, y, cell, cell)
                .endFill ()
            |> ignore

// ---------------------------------------------------------------------------
// Shared shape helpers
// ---------------------------------------------------------------------------

/// Flat vertex list for Graphics.drawPolygon (see the binding for why obj[]).
let private poly (points: float list) : obj [] =
    points |> List.map box |> List.toArray

/// Draws one tower, fully procedurally: shape encodes the type, size/shade
/// encode the level, and white pips repeat the level for colour-blind
/// readability. Used for both placed towers and the drag ghost.
let private drawTowerShape (g: Graphics) (x: float) (y: float) (tower: Tower) (alpha: float) : unit =
    let rank = TowerLevel.rank tower.Level
    let half = 12.0 + 3.0 * float rank
    let color = towerShade tower.Type rank

    g.lineStyle (0.0, 0, 0.0) |> ignore

    (match tower.Type with
     | Archer -> g.beginFill(color, alpha).drawCircle(x, y, half).endFill ()
     | Cannon ->
         g
             .beginFill(color, alpha)
             .drawRoundedRect(x - half, y - half, half * 2.0, half * 2.0, 6.0)
             .endFill ()
     | Frost ->
         g
             .beginFill(color, alpha)
             .drawPolygon(poly [ x; y - half; x + half; y; x; y + half; x - half; y ])
             .endFill ())
    |> ignore

    for i in 0 .. rank - 1 do
        let pipX = x - float (rank - 1) * 4.0 + float i * 8.0

        g
            .beginFill(0xffffff, 0.9 * alpha)
            .drawCircle(pipX, y + half + 6.0, 2.0)
            .endFill ()
        |> ignore

/// Translucent attack-range disc for a tower standing (or previewed) at the
/// given canvas position.
let private drawRange (g: Graphics) (layout: Layout) (x: float) (y: float) (tower: Tower) : unit =
    let stats = Tower.stats tower
    let radius = stats.Range * layout.CellSize
    let color = towerBaseColor tower.Type

    g
        .lineStyle(1.5, color, 0.35)
        .beginFill(color, 0.07)
        .drawCircle(x, y, radius)
        .endFill ()
    |> ignore

let private drawEnemy (g: Graphics) (x: float) (y: float) (enemy: Enemy) : unit =
    let color = enemyColor enemy.Type
    g.lineStyle (0.0, 0, 0.0) |> ignore

    (match enemy.Type with
     | Grunt -> g.beginFill(color, 1.0).drawCircle(x, y, 9.0).endFill ()
     | Runner ->
         g
             .beginFill(color, 1.0)
             .drawPolygon(poly [ x; y - 9.0; x + 8.0; y + 7.0; x - 8.0; y + 7.0 ])
             .endFill ()
     | Tank ->
         g
             .beginFill(color, 1.0)
             .drawRoundedRect(x - 10.0, y - 10.0, 20.0, 20.0, 4.0)
             .endFill ()
     | Boss ->
         g
             .beginFill(color, 1.0)
             .drawCircle(x, y, 15.0)
             .endFill()
             .lineStyle(2.0, 0xe1bee7, 1.0)
             .drawCircle(x, y, 19.0))
    |> ignore

    // Health bar: current / type base health.
    let fraction =
        float (Health.value enemy.Health) / float (EnemyType.baseHealth enemy.Type)

    let barWidth = 26.0
    let barY = y - 24.0

    g
        .lineStyle(0.0, 0, 0.0)
        .beginFill(0x000000, 0.55)
        .drawRect(x - barWidth / 2.0, barY, barWidth, 4.0)
        .endFill ()
    |> ignore

    let barColor =
        if fraction > 0.5 then 0x66bb6a
        elif fraction > 0.25 then 0xffa726
        else 0xef5350

    g
        .beginFill(barColor, 1.0)
        .drawRect(x - barWidth / 2.0, barY, barWidth * fraction, 4.0)
        .endFill ()
    |> ignore

// ---------------------------------------------------------------------------
// Per-frame dynamic drawing
// ---------------------------------------------------------------------------

let private previewColor (preview: DropPreview) =
    match preview with
    | MergeHere _ -> 0x66bb6a
    | MoveHere -> 0x42a5f5
    | ReturnToOrigin -> 0x90a4ae
    | Blocked -> 0xef5350

let drawFrame (layout: Layout) (model: UiModel) (layers: Layers) : unit =
    let overlay = layers.Overlay
    overlay.clear () |> ignore
    layers.Towers.clear () |> ignore
    layers.Enemies.clear () |> ignore
    layers.Ghost.clear () |> ignore

    let cell = layout.CellSize

    // Drag feedback: origin outline + drop preview highlight on the hovered
    // cell, colour-coded by what previewDrop says would happen.
    (match model.Game.Interaction with
     | Dragging drag ->
         let ox, oy = cellOrigin layout drag.Origin

         overlay
             .lineStyle(2.0, 0xffffff, 0.25)
             .drawRect(ox + 2.0, oy + 2.0, cell - 4.0, cell - 4.0)
         |> ignore

         match model.Hover with
         | Some target ->
             match previewDrop target model.Game with
             | Some preview ->
                 let hx, hy = cellOrigin layout target

                 overlay
                     .lineStyle(0.0, 0, 0.0)
                     .beginFill(previewColor preview, 0.28)
                     .drawRect(hx + 2.0, hy + 2.0, cell - 4.0, cell - 4.0)
                     .endFill ()
                 |> ignore

                 // Range preview of the tower as it would stand after the
                 // drop (merged towers show their upgraded range).
                 let cx, cy = cellCenter layout target

                 match preview with
                 | MoveHere -> drawRange overlay layout cx cy drag.Tower
                 | MergeHere level -> drawRange overlay layout cx cy { drag.Tower with Level = level }
                 | ReturnToOrigin
                 | Blocked -> ()
             | None -> ()
         | None -> ()
     | Idle ->
         // Idle hover over a tower: visualise its attack range.
         match model.Hover with
         | Some coord ->
             match Grid.cellAt coord model.Game.Grid with
             | Occupied tower ->
                 let cx, cy = cellCenter layout coord
                 drawRange overlay layout cx cy tower
             | Empty -> ()
         | None -> ())

    // Towers on the board.
    for coord, tower in Grid.towers model.Game.Grid do
        let x, y = cellCenter layout coord
        drawTowerShape layers.Towers x y tower 1.0

    // Enemies on the demo lane.
    for enemy in model.Game.Enemies do
        drawEnemy layers.Enemies (enemyX layout enemy.Progress) layout.LaneY enemy

    // Drag ghost follows the raw pointer position.
    match model.Game.Interaction, model.Pointer with
    | Dragging drag, Some(px, py) -> drawTowerShape layers.Ghost px py drag.Tower 0.6
    | _ -> ()
