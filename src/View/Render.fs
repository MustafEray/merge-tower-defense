/// Procedural rendering with the PixiJS Graphics API — no external assets.
/// Pure "view = f(model)": every frame the dynamic layers are cleared and
/// redrawn from the current UiModel; nothing in here mutates game state.
/// The enemy lane is drawn from the core's Path geometry, so the picture
/// can never disagree with the simulation.
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
/// towers, enemies, shot tracers, celebration effects, drag ghost, floating
/// gold texts, wave banner. The Text objects are a fixed pool created once —
/// per-frame allocation would churn canvas textures.
type Layers =
    { Static: Graphics
      Overlay: Graphics
      Towers: Graphics
      Enemies: Graphics
      Shots: Graphics
      Effects: Graphics
      Ghost: Graphics
      Floats: Text []
      Banner: Text }

let private floatPoolSize = 10

let createLayers (app: Application) : Layers =
    let make () =
        let g = createGraphics ()
        app.stage.addChild g |> ignore
        g

    let statics = make ()
    let overlay = make ()
    let towers = make ()
    let enemies = make ()
    let shots = make ()
    let effects = make ()
    let ghost = make ()

    let floatStyle =
        [ "fontFamily", box "system-ui, sans-serif"
          "fontSize", box 14
          "fontWeight", box "700"
          "fill", box "#ffd54f"
          "stroke", box "#10131f"
          "strokeThickness", box 3 ]

    let floats =
        [| for _ in 1 .. floatPoolSize ->
               let t = createText "" floatStyle
               t.visible <- false
               t.anchor.x <- 0.5
               t.anchor.y <- 0.5
               app.stage.addChild t |> ignore
               t |]

    let banner =
        createText
            ""
            [ "fontFamily", box "system-ui, sans-serif"
              "fontSize", box 36
              "fontWeight", box "800"
              "fill", box "#e8eaf1"
              "stroke", box "#10131f"
              "strokeThickness", box 6 ]

    banner.visible <- false
    banner.anchor.x <- 0.5
    banner.anchor.y <- 0.5
    app.stage.addChild banner |> ignore

    { Static = statics
      Overlay = overlay
      Towers = towers
      Enemies = enemies
      Shots = shots
      Effects = effects
      Ghost = ghost
      Floats = floats
      Banner = banner }

// ---------------------------------------------------------------------------
// Shared shape helpers
// ---------------------------------------------------------------------------

/// Flat vertex list for Graphics.drawPolygon (see the binding for why obj[]).
let private poly (points: float list) : obj [] =
    points |> List.map box |> List.toArray

// ---------------------------------------------------------------------------
// Static board (drawn once; derived from grid size and path geometry)
// ---------------------------------------------------------------------------

let drawStatic (layout: Layout) (size: GridSize) (path: Path) (layers: Layers) : unit =
    let g = layers.Static
    let n = GridSize.value size
    let cell = layout.CellSize

    // Enemy lane: a thick strip along the actual Path polyline.
    let waypointsPx = Path.waypoints path |> List.map (toPx layout)

    (match waypointsPx with
     | [] -> ()
     | (x0, y0) :: rest ->
         g.lineStyle (laneWidthPx layout, 0x202433, 1.0) |> ignore
         g.moveTo (x0, y0) |> ignore

         for x, y in rest do
             g.lineTo (x, y) |> ignore

         // Centre line on top of the strip.
         g.lineStyle (3.0, 0x3a415f, 1.0) |> ignore
         g.moveTo (x0, y0) |> ignore

         for x, y in rest do
             g.lineTo (x, y) |> ignore)

    // Direction chevrons, sampled along the path at fixed walk distances.
    let total = Path.length path
    let chevronEvery = 1.3

    let chevronCount = int (total / chevronEvery)

    for i in 1 .. chevronCount - 1 do
        let d = float i * chevronEvery
        let px, py = toPx layout (Path.pointAtDistance path d)
        let ax, ay = toPx layout (Path.pointAtDistance path (d + 0.3))
        let dx, dy = ax - px, ay - py
        let len = sqrt (dx * dx + dy * dy)

        if len > 0.0 then
            let ux, uy = dx / len, dy / len
            let vx, vy = -uy, ux // perpendicular

            g
                .lineStyle(2.0, 0x4c557a, 1.0)
                .moveTo(px - ux * 4.0 + vx * 5.0, py - uy * 4.0 + vy * 5.0)
                .lineTo(px + ux * 4.0, py + uy * 4.0)
                .lineTo(px - ux * 4.0 - vx * 5.0, py - uy * 4.0 - vy * 5.0)
            |> ignore

    // Spawn portal at the entry, goal marker at the exit.
    (match waypointsPx with
     | [] -> ()
     | (sx, sy) :: _ ->
         g
             .lineStyle(3.0, 0x66bb6a, 0.9)
             .beginFill(0x1a1d29, 1.0)
             .drawCircle(sx, sy, 13.0)
             .endFill ()
         |> ignore)

    (match List.tryLast waypointsPx with
     | None -> ()
     | Some(gx, gy) ->
         g
             .lineStyle(3.0, 0xef5350, 0.9)
             .beginFill(0x1a1d29, 1.0)
             .drawCircle(gx, gy, 13.0)
             .endFill()
             .lineStyle(0.0, 0, 0.0)
             .beginFill(0xef5350, 0.9)
             .drawCircle(gx, gy, 5.0)
             .endFill ()
         |> ignore)

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
// Shape helpers shared by placed towers and the drag ghost
// ---------------------------------------------------------------------------

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

/// Draws one enemy. `pulse` is a small size multiplier derived from walked
/// distance, giving a cheap "marching" wobble without any per-enemy state.
let private drawEnemy (g: Graphics) (x: float) (y: float) (enemy: Enemy) (pulse: float) : unit =
    let color = enemyColor enemy.Type
    g.lineStyle (0.0, 0, 0.0) |> ignore

    (match enemy.Type with
     | Grunt -> g.beginFill(color, 1.0).drawCircle(x, y, 9.0 * pulse).endFill ()
     | Runner ->
         let r = 9.0 * pulse

         g
             .beginFill(color, 1.0)
             .drawPolygon(poly [ x; y - r; x + r * 0.9; y + r * 0.8; x - r * 0.9; y + r * 0.8 ])
             .endFill ()
     | Tank ->
         let half = 10.0 * pulse

         g
             .beginFill(color, 1.0)
             .drawRoundedRect(x - half, y - half, half * 2.0, half * 2.0, 4.0)
             .endFill ()
     | Boss ->
         g
             .beginFill(color, 1.0)
             .drawCircle(x, y, 15.0 * pulse)
             .endFill()
             .lineStyle(2.0, 0xe1bee7, 1.0)
             .drawCircle(x, y, 19.0 * pulse))
    |> ignore

    // Frost's slow debuff: an icy ring around the victim.
    (match enemy.Slow with
     | Some _ ->
         g
             .lineStyle(2.0, 0x81d4fa, 0.8)
             .drawCircle(x, y, 13.0 * pulse)
             .lineStyle (0.0, 0, 0.0)
         |> ignore
     | None -> ())

    // Health bar: current versus the type's unscaled base (waves scale
    // health up, so late-wave enemies can show a "over-full" bar clamped
    // to the bar width).
    let fraction =
        min 1.0 (float (Health.value enemy.Health) / float (EnemyType.baseHealth enemy.Type))

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
    layers.Shots.clear () |> ignore
    layers.Effects.clear () |> ignore
    layers.Ghost.clear () |> ignore

    let cell = layout.CellSize
    let path = model.Game.Path

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

    // Enemies along the path, with a subtle march wobble derived from the
    // distance they have walked (pure function of progress — no state).
    for enemy in model.Game.Enemies do
        let x, y = toPx layout (Enemy.positionOn path enemy)

        let pulse =
            1.0
            + 0.05 * sin (PathProgress.value enemy.Progress * Path.length path * 8.0)

        drawEnemy layers.Enemies x y enemy pulse

    // Shot tracers, fading with their remaining ttl.
    for shot in model.Shots do
        let fx, fy = cellCenter layout shot.FromCell
        let tx, ty = toPx layout shot.Target
        let alpha = 0.9 * (shot.Ttl / shotTtl)

        layers.Shots
            .lineStyle(2.0, 0xfff59d, alpha)
            .moveTo(fx, fy)
            .lineTo(tx, ty)
            .lineStyle(0.0, 0, 0.0)
            .beginFill(0xfff59d, alpha)
            .drawCircle(tx, ty, 3.5)
            .endFill ()
        |> ignore

    // Spawn portal pulse (ambient, driven by the pure UI clock).
    (match Path.waypoints path with
     | (sx, sy) :: _ ->
         let px, py = toPx layout (sx, sy)
         let throb = sin (model.Clock * 3.0)

         layers.Effects
             .lineStyle(2.0, 0x66bb6a, 0.3 + 0.2 * throb)
             .drawCircle(px, py, 16.0 + 2.0 * throb)
             .lineStyle (0.0, 0, 0.0)
         |> ignore
     | [] -> ())

    // Celebration effects (rings, bursts, leak flashes).
    for effect in model.Effects do
        let t = effect.Age / effectDuration effect.Kind
        let x, y = toPx layout effect.Pos
        let fade = max 0.0 (1.0 - t)
        let g = layers.Effects

        match effect.Kind with
        | KillBurst ->
            g.lineStyle (2.5, 0xffcc80, 0.9 * fade) |> ignore
            g.drawCircle (x, y, 4.0 + t * 18.0) |> ignore
            g.lineStyle (0.0, 0, 0.0) |> ignore

            for i in 0 .. 5 do
                let angle = float i / 6.0 * 6.28318
                let r = 6.0 + t * 22.0

                g
                    .beginFill(0xffe0b2, 0.8 * fade)
                    .drawCircle(x + cos angle * r, y + sin angle * r, 2.0)
                    .endFill ()
                |> ignore
        | MergeRing ->
            g
                .lineStyle(3.0, 0x66bb6a, fade)
                .drawCircle(x, y, 6.0 + t * layout.CellSize * 0.65)
                .lineStyle (0.0, 0, 0.0)
            |> ignore
        | SpawnRing ->
            g
                .lineStyle(2.5, 0x42a5f5, fade)
                .drawCircle(x, y, 4.0 + t * layout.CellSize * 0.45)
                .lineStyle (0.0, 0, 0.0)
            |> ignore
        | LeakFlash ->
            g
                .lineStyle(3.0, 0xef5350, fade)
                .drawCircle(x, y, 10.0 + t * 26.0)
                .lineStyle (0.0, 0, 0.0)
            |> ignore
        | GoldFloat _ -> () // handled by the text pool below

    // Floating gold texts, assigned to the fixed Text pool.
    let floats =
        model.Effects
        |> List.choose (fun e ->
            match e.Kind with
            | GoldFloat text -> Some(text, e.Pos, e.Age, effectDuration e.Kind)
            | _ -> None)

    layers.Floats
    |> Array.iteri (fun i t ->
        match List.tryItem i floats with
        | Some(text, pos, age, duration) ->
            let x, y = toPx layout pos

            if t.text <> text then t.text <- text
            t.position.x <- x
            t.position.y <- y - 12.0 - age * 26.0
            t.alpha <- max 0.0 (1.0 - age / duration)
            t.visible <- true
        | None -> t.visible <- false)

    // Wave banner, centred over the grid, fading in and out.
    (match model.Banner with
     | Some(wave, age) ->
         let n = float (GridSize.value (Grid.size model.Game.Grid))
         let x, y = toPx layout (n / 2.0, n * 0.42)
         let text = sprintf "Wave %d" wave

         if layers.Banner.text <> text then layers.Banner.text <- text
         layers.Banner.position.x <- x
         layers.Banner.position.y <- y
         layers.Banner.alpha <- max 0.0 (min (age / 0.25) (min 1.0 ((bannerDuration - age) / 0.5)))
         layers.Banner.visible <- true
     | None -> layers.Banner.visible <- false)

    // Drag ghost follows the raw pointer position.
    match model.Game.Interaction, model.Pointer with
    | Dragging drag, Some(px, py) -> drawTowerShape layers.Ghost px py drag.Tower 0.6
    | _ -> ()
