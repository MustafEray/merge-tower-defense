/// Procedural sound: every cue is synthesised with a WebAudio oscillator —
/// no audio assets, matching the no-asset rule of the render layer.
///
/// The AudioContext is created lazily and resumed on every beep, so the
/// browser's autoplay policy simply mutes us until the first user gesture.
/// Audio must never crash the game: every call is fenced with try/with.
module MergeTowerDefense.Interop.Audio

open Fable.Core
open Fable.Core.JsInterop
open MergeTowerDefense.Ui

let mutable private ctx: obj = null

[<Emit("new (window.AudioContext || window.webkitAudioContext)()")>]
let private newAudioContext () : obj = jsNative

let private ensureContext () =
    if isNull ctx then ctx <- newAudioContext ()
    ctx?resume () |> ignore
    ctx

/// One synthesised blip: an oscillator gliding from startHz to endHz while
/// its gain decays exponentially to silence.
let private beep (oscType: string) (startHz: float) (endHz: float) (duration: float) (volume: float) : unit =
    try
        let c = ensureContext ()
        let osc = c?createOscillator ()
        let gain = c?createGain ()
        let now: float = !!c?currentTime
        osc?``type`` <- oscType
        osc?frequency?setValueAtTime (startHz, now) |> ignore
        osc?frequency?exponentialRampToValueAtTime (max 1.0 endHz, now + duration) |> ignore
        gain?gain?setValueAtTime (volume, now) |> ignore
        gain?gain?exponentialRampToValueAtTime (0.0001, now + duration) |> ignore
        osc?connect (gain) |> ignore
        gain?connect (c?destination) |> ignore
        osc?start (now) |> ignore
        osc?stop (now + duration) |> ignore
    with _ ->
        ()

let play (cue: SoundCue) : unit =
    match cue with
    | ShootCue -> beep "square" 880.0 660.0 0.05 0.025
    | KillCue -> beep "triangle" 520.0 180.0 0.12 0.06
    | MergeCue -> beep "square" 330.0 880.0 0.18 0.07
    | BuyCue -> beep "sine" 500.0 640.0 0.07 0.05
    | LeakCue -> beep "sawtooth" 180.0 70.0 0.3 0.08
    | WaveStartCue -> beep "square" 392.0 784.0 0.25 0.06
    | WaveClearCue -> beep "triangle" 523.0 1046.0 0.3 0.07
    | LostCue -> beep "sawtooth" 220.0 55.0 0.8 0.1
    | RejectCue -> beep "square" 200.0 140.0 0.08 0.04
