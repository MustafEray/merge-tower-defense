/// React HUD layer: gold / wave / lives / enemy readouts, per-type buy
/// buttons, the transient notice line and the game-over panel. Lives in its
/// own DOM root (#hud-root), completely isolated from the Pixi canvas — it
/// only receives UiModel snapshots and emits UiMsg values through dispatch.
module MergeTowerDefense.View.Hud

open MergeTowerDefense.Shared
open MergeTowerDefense.State
open MergeTowerDefense.Ui
open MergeTowerDefense.Interop.React

let private stat (id: string) (label: string) (value: string) =
    div
        [ "className", box "hud-stat" ]
        [ span [ "className", box "hud-stat-label" ] [ str label ]
          span [ "className", box "hud-stat-value"; "id", box id ] [ str value ] ]

/// Like `stat`, but flags the whole row with the low-lives warning style.
let private statWarn (id: string) (label: string) (value: string) (warn: bool) =
    div
        [ "className", box (if warn then "hud-stat hud-stat-danger" else "hud-stat") ]
        [ span [ "className", box "hud-stat-label" ] [ str label ]
          span [ "className", box "hud-stat-value"; "id", box id ] [ str value ] ]

let private waveLabel (game: GameState) =
    match game.Status with
    | Defeated waves -> sprintf "%d survived" waves
    | Playing _ ->
        match game.Wave.Phase with
        | BetweenWaves seconds -> sprintf "%d — next in %.0fs" game.Wave.Number (ceil seconds)
        | Spawning _
        | WaveActive -> string game.Wave.Number

let private livesLabel (game: GameState) =
    match game.Status with
    | Playing lives -> string (Lives.value lives)
    | Defeated _ -> "0"

let private buyButton (model: UiModel) (dispatch: UiMsg -> unit) (towerType: TowerType) =
    let name = string towerType

    button
        [ "id", box (sprintf "buy-%s" (name.ToLowerInvariant()))
          "className", box (sprintf "hud-buy hud-buy-%s" (name.ToLowerInvariant()))
          "disabled", box (not (canBuy model))
          "onClick", box (fun (_: obj) -> dispatch (Buy towerType)) ]
        [ span [ "className", box "hud-buy-glyph" ] []
          span [ "className", box "hud-buy-label" ] [ str name ]
          span [ "className", box "hud-buy-cost" ] [ str (sprintf "%dg" (nextTowerCost model.Game)) ] ]

let private muteButton (model: UiModel) (dispatch: UiMsg -> unit) =
    button
        [ "id", box "mute-toggle"
          "className", box "hud-mute"
          "onClick", box (fun (_: obj) -> dispatch ToggleMute) ]
        [ str (if model.Muted then "Sound: Off" else "Sound: On") ]

let view (model: UiModel) (dispatch: UiMsg -> unit) =
    let gameOver =
        match model.Game.Status with
        | Defeated waves ->
            div
                [ "className", box "hud-gameover"; "id", box "hud-gameover" ]
                [ span [] [ str (sprintf "Game Over — you survived %d wave(s)." waves) ]
                  button
                      [ "id", box "restart"
                        "className", box "hud-restart"
                        "onClick", box (fun (_: obj) -> dispatch Restart) ]
                      [ str "Restart" ] ]
        | Playing _ -> nothing

    div
        [ "className", box "hud" ]
        [ div
              [ "className", box "hud-header" ]
              [ h1 [ "className", box "hud-title" ] [ str "Merge Tower Defense" ]
                muteButton model dispatch ]
          div
              [ "className", box "hud-stats" ]
              [ stat "hud-gold" "Gold" (string (Gold.value model.Game.Gold))
                stat "hud-wave" "Wave" (waveLabel model.Game)
                statWarn "hud-lives" "Lives" (livesLabel model.Game) (isLowLives model)
                stat "hud-enemies" "Enemies" (string (List.length model.Game.Enemies)) ]
          div
              [ "className", box "hud-shop" ]
              [ buyButton model dispatch Archer
                buyButton model dispatch Cannon
                buyButton model dispatch Frost ]
          gameOver
          div
              [ "className", box "hud-notice"; "id", box "hud-notice" ]
              [ match model.Notice with
                | Some(text, _) -> str text
                | None -> str "Drag two matching towers together to merge them." ] ]
