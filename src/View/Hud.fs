/// React HUD layer: gold / wave / enemy readouts, the buy button and the
/// transient notice line. Lives in its own DOM root (#hud-root), completely
/// isolated from the Pixi canvas — it only receives UiModel snapshots and
/// emits UiMsg values through dispatch.
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

let view (model: UiModel) (dispatch: UiMsg -> unit) =
    let buyLabel =
        sprintf "Buy %s Tower (%d gold)" (string (nextPurchaseType model)) towerCost

    div
        [ "className", box "hud" ]
        [ h1 [ "className", box "hud-title" ] [ str "Merge Tower Defense" ]
          div
              [ "className", box "hud-stats" ]
              [ stat "hud-gold" "Gold" (string model.Gold)
                stat "hud-wave" "Wave" (string model.Wave)
                stat "hud-enemies" "Enemies" (string (List.length model.Game.Enemies)) ]
          button
              [ "id", box "buy-tower"
                "className", box "hud-buy"
                "disabled", box (not (canBuy model))
                "onClick", box (fun (_: obj) -> dispatch BuyTower) ]
              [ str buyLabel ]
          div
              [ "className", box "hud-notice"; "id", box "hud-notice" ]
              [ match model.Notice with
                | Some(text, _) -> str text
                | None -> str "Drag two matching towers together to merge them." ] ]
