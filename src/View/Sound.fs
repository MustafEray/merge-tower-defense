/// Maps pure Ui.SoundCue values to procedurally synthesized Web Audio tones.
/// The only module that knows both what a cue means and how to play it —
/// mirrors the Render/Hud split for graphics. Owns the single AudioContext
/// for the app's lifetime, isolated in its own DOM-free-of-Pixi/React corner.
module MergeTowerDefense.View.Sound

open MergeTowerDefense.Shared
open MergeTowerDefense.Ui
open MergeTowerDefense.Interop.Audio

let mutable private ctx: obj option = None

/// Creates (once) and resumes the AudioContext. Call this from real user
/// gesture handlers (pointerdown, a HUD button click) — browsers refuse to
/// start audio otherwise. Safe to call every dispatch: a call that doesn't
/// originate from a gesture just leaves the context suspended.
let unlock () : unit =
    match ctx with
    | Some c -> resume c
    | None ->
        match tryCreateContext () with
        | null -> ()
        | c ->
            ctx <- Some c
            resume c

let private play (freq: float) (kind: string) (durationMs: float) (peakGain: float) : unit =
    match ctx with
    | Some c -> playTone c freq kind durationMs peakGain
    | None -> ()

let private towerPitch =
    function
    | Archer -> 880.0
    | Cannon -> 220.0
    | Frost -> 660.0

let private towerWave =
    function
    | Archer -> "triangle"
    | Cannon -> "square"
    | Frost -> "sine"

let private enemyPitch =
    function
    | Grunt -> 440.0
    | Runner -> 520.0
    | Tank -> 260.0
    | Boss -> 160.0

/// Plays the tone for a single cue raised by the pure Ui layer this dispatch.
let playCue (cue: SoundCue) : unit =
    match cue with
    | ShootSound towerType -> play (towerPitch towerType) (towerWave towerType) 70.0 0.05
    | KillSound enemyType -> play (enemyPitch enemyType * 0.6) "sawtooth" 130.0 0.09
    | MergeSound towerType -> play (towerPitch towerType * 1.5) "triangle" 220.0 0.11
    | BuySound -> play 660.0 "square" 90.0 0.06
    | WaveStartSound -> play 330.0 "sine" 260.0 0.08
    | LifeLostSound -> play 140.0 "sawtooth" 220.0 0.12
    | GameOverSound -> play 110.0 "sawtooth" 500.0 0.14
