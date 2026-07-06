/// Rendering with the PixiJS API. Pure "view = f(model)": every frame the
/// dynamic layers are cleared and redrawn from the current UiModel; nothing
/// in here mutates game state. The enemy lane is drawn from the core's Path
/// geometry, so the picture can never disagree with the simulation.
///
/// Towers use real art (Kenney's CC0 "Tower Defense" pack, public/towers/ —
/// see public/towers/KENNEY-LICENSE.txt) loaded as Sprites; everything else
/// (grid, path, enemies, range/preview overlays, burst effects) stays
/// procedural Graphics, same as before.
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

/// Displayed height in pixels for a tower sprite at the given rank. Shared
/// between the sprite's scale and its pip placement so the two can never
/// drift apart.
let private towerDisplayHeight (rank: int) = 40.0 + 6.0 * float rank

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
/// tower sprites + their pip decorations, enemies, burst effects, shot
/// tracers, drag ghost, life-lost flash.
type Layers =
    { Static: Graphics
      Overlay: Graphics
      /// Real tower art (Sprite children), cleared and rebuilt every frame.
      TowerSprites: Container
      /// Level pips drawn under/over the sprites; a Graphics layer since
      /// pips are small procedural dots, not art.
      TowerDecor: Graphics
      Enemies: Graphics
      Effects: Graphics
      Shots: Graphics
      /// The drag ghost's Sprite (0 or 1 children), cleared every frame.
      Ghost: Container
      Flash: Graphics }

let createLayers (app: Application) : Layers =
    let makeGraphics () =
        let g = createGraphics ()
        app.stage.addChild g |> ignore
        g

    let makeContainer () =
        let c = createContainer ()
        app.stage.addChild c |> ignore
        c

    { Static = makeGraphics ()
      Overlay = makeGraphics ()
      TowerSprites = makeContainer ()
      TowerDecor = makeGraphics ()
      Enemies = makeGraphics ()
      Effects = makeGraphics ()
      Shots = makeGraphics ()
      Ghost = makeContainer ()
      Flash = makeGraphics () }

// ---------------------------------------------------------------------------
// Tower art (Kenney CC0, public/towers/ — see public/towers/KENNEY-LICENSE.txt)
// ---------------------------------------------------------------------------

type TowerTextures =
    { Archer: Texture
      Cannon: Texture
      Frost: Texture }

/// Loads the three tower textures once at startup. Pixi caches by URL and
/// resolves the image asynchronously, so calling this before the first
/// frame is enough — no explicit await needed.
let loadTowerTextures () : TowerTextures =
    { Archer = loadTexture "/towers/archer.png"
      Cannon = loadTexture "/towers/cannon.png"
      Frost = loadTexture "/towers/frost.png" }

let private textureFor (textures: TowerTextures) (towerType: TowerType) =
    match towerType with
    | Archer -> textures.Archer
    | Cannon -> textures.Cannon
    | Frost -> textures.Frost

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

/// Places one tower's real-art Sprite into `container`, tinted by the same
/// per-rank shade the game has always used (Sprite.tint multiplies the
/// texture's colour, so the art still darkens/lightens by level). Used for
/// both placed towers and the drag ghost.
let private placeTowerSprite
    (textures: TowerTextures)
    (container: Container)
    (x: float)
    (y: float)
    (tower: Tower)
    (alpha: float)
    : unit =
    let rank = TowerLevel.rank tower.Level
    let sprite = createSprite (textureFor textures tower.Type)

    // Native size is whatever the loaded texture reports at scale (1,1);
    // read it before rescaling so both axes stay in proportion.
    let nativeHeight = sprite.height
    let scale = if nativeHeight > 0.0 then towerDisplayHeight rank / nativeHeight else 1.0

    sprite.anchor.x <- 0.5
    sprite.anchor.y <- 0.5
    sprite.scale.x <- scale
    sprite.scale.y <- scale
    sprite.position.x <- x
    sprite.position.y <- y
    sprite.alpha <- alpha
    sprite.tint <- towerShade tower.Type rank

    container.addChild sprite |> ignore

/// White pips below a tower repeat its level for colour-blind readability —
/// kept procedural (small dots) even though the tower body is now real art.
let private drawTowerPips (g: Graphics) (x: float) (y: float) (tower: Tower) (alpha: float) : unit =
    let rank = TowerLevel.rank tower.Level
    let pipY = y + towerDisplayHeight rank / 2.0 + 6.0

    g.lineStyle (0.0, 0, 0.0) |> ignore

    for i in 0 .. rank - 1 do
        let pipX = x - float (rank - 1) * 4.0 + float i * 8.0

        g
            .beginFill(0xffffff, 0.9 * alpha)
            .drawCircle(pipX, pipY, 2.0)
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

    // Frost's chill effect: a pale icy ring around slowed enemies.
    if enemy.Slow > 0.0 then
        g.lineStyle(1.5, 0x81d4fa, 0.8).drawCircle (x, y, 13.0) |> ignore

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

/// One transient burst, animated by how far through its lifetime it is:
/// `grow` climbs 0→1 as the effect ages, `fade` (its complement) drives alpha.
let private drawEffect (g: Graphics) (layout: Layout) (effect: Effect) : unit =
    let x, y = toPx layout effect.At
    let duration = effectDuration effect.Kind
    let fade = max 0.0 (effect.Ttl / duration) // 1 at spawn → 0 at expiry
    let grow = 1.0 - fade

    match effect.Kind with
    | KillBurst enemyType ->
        // Expanding ring plus a ring of outward-flung sparks.
        let color = enemyColor enemyType
        let radius = layout.CellSize * (0.15 + 0.35 * grow)

        g.lineStyle(2.5, color, 0.85 * fade).drawCircle (x, y, radius)
        |> ignore

        for k in 0 .. 5 do
            let angle = float k / 6.0 * 2.0 * System.Math.PI
            let d = radius + 4.0

            g
                .lineStyle(0.0, 0, 0.0)
                .beginFill(color, 0.85 * fade)
                .drawCircle(x + cos angle * d, y + sin angle * d, 2.5 * fade)
                .endFill ()
            |> ignore

    | MergeFlash towerType ->
        // Bright expanding ring with a white sparkle cross at its heart.
        let color = towerBaseColor towerType
        let radius = layout.CellSize * (0.2 + 0.5 * grow)

        g.lineStyle(3.0, color, 0.9 * fade).drawCircle (x, y, radius)
        |> ignore

        let s = layout.CellSize * 0.3 * fade

        g
            .lineStyle(2.0, 0xffffff, 0.9 * fade)
            .moveTo(x - s, y)
            .lineTo(x + s, y)
            .moveTo(x, y - s)
            .lineTo(x, y + s)
        |> ignore

    | SpawnPop towerType ->
        // Small, quick ring pop marking a freshly placed tower.
        let color = towerBaseColor towerType
        let radius = layout.CellSize * (0.1 + 0.3 * grow)

        g.lineStyle(2.0, color, 0.8 * fade).drawCircle (x, y, radius)
        |> ignore

/// Full-canvas red vignette that flashes when a life is lost, fading with the
/// model's remaining LifeFlash seconds.
let private drawLifeFlash (layout: Layout) (lifeFlash: float) (g: Graphics) : unit =
    if lifeFlash > 0.0 then
        let alpha = 0.35 * (lifeFlash / lifeFlashTtl)

        g
            .beginFill(0xef5350, alpha)
            .drawRect(0.0, 0.0, layout.CanvasWidth, layout.CanvasHeight)
            .endFill ()
        |> ignore

let drawFrame (textures: TowerTextures) (layout: Layout) (model: UiModel) (layers: Layers) : unit =
    let overlay = layers.Overlay
    overlay.clear () |> ignore
    layers.TowerSprites.removeChildren () |> ignore
    layers.TowerDecor.clear () |> ignore
    layers.Enemies.clear () |> ignore
    layers.Effects.clear () |> ignore
    layers.Shots.clear () |> ignore
    layers.Ghost.removeChildren () |> ignore
    layers.Flash.clear () |> ignore

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
        placeTowerSprite textures layers.TowerSprites x y tower 1.0
        drawTowerPips layers.TowerDecor x y tower 1.0

    // Enemies along the path.
    for enemy in model.Game.Enemies do
        let x, y = toPx layout (Enemy.positionOn path enemy)
        drawEnemy layers.Enemies x y enemy

    // Burst effects (kills, merges), fading with their remaining ttl.
    for effect in model.Effects do
        drawEffect layers.Effects layout effect

    // Shot tracers, fading with their remaining ttl. A Cannon shot also
    // shows its splash radius at the impact point, so the mechanic that
    // sets it apart from Archer/Frost is actually visible.
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

        match Grid.cellAt shot.FromCell model.Game.Grid with
        | Occupied tower when (Tower.stats tower).SplashRadius > 0.0 ->
            let radiusPx = (Tower.stats tower).SplashRadius * layout.CellSize

            layers.Shots
                .lineStyle(1.5, 0xffa726, 0.6 * alpha)
                .beginFill(0xffa726, 0.12 * alpha)
                .drawCircle(tx, ty, radiusPx)
                .endFill ()
            |> ignore
        | _ -> ()

    // Drag ghost follows the raw pointer position.
    match model.Game.Interaction, model.Pointer with
    | Dragging drag, Some(px, py) ->
        placeTowerSprite textures layers.Ghost px py drag.Tower 0.6
        drawTowerPips layers.TowerDecor px py drag.Tower 0.6
    | _ -> ()

    // Full-canvas flash on top of everything when a life was just lost.
    drawLifeFlash layout model.LifeFlash layers.Flash
