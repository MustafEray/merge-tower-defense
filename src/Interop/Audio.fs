/// Minimal hand-written Web Audio API bindings — only the surface needed for
/// short procedural blips (oscillator + gain envelope). No audio files: every
/// sound is synthesized on demand, matching the project's asset-free
/// rendering. Interop-only; the pure core never learns this exists.
module MergeTowerDefense.Interop.Audio

open Fable.Core

/// Attempts to construct an AudioContext (falling back to the legacy
/// webkit-prefixed constructor). Returns null if the API is unavailable or
/// construction throws — callers treat null as "no sound this session".
[<Emit("(function(){ try { var C = window.AudioContext || window.webkitAudioContext; return C ? new C() : null; } catch (e) { return null; } })()")>]
let tryCreateContext () : obj = jsNative

/// Browsers start an AudioContext suspended until a user gesture resumes it.
/// Safe to call repeatedly (including outside a gesture, where it silently
/// stays suspended) and on a null context.
[<Emit("(function(ctx){ try { if (ctx && ctx.state === 'suspended') { ctx.resume(); } } catch (e) {} })($0)")>]
let resume (ctx: obj) : unit = jsNative

/// Plays one procedurally synthesized tone: an oscillator of the given
/// waveform and frequency, shaped by a short exponential gain envelope so it
/// doesn't click at the start or end. Every Web Audio failure is caught and
/// swallowed — sound is best-effort polish, never allowed to break the game.
[<Emit("""(function(ctx, freq, kind, durationMs, peakGain){
    try {
        var osc = ctx.createOscillator();
        var gain = ctx.createGain();
        osc.type = kind;
        osc.frequency.setValueAtTime(freq, ctx.currentTime);
        gain.gain.setValueAtTime(0.0001, ctx.currentTime);
        gain.gain.exponentialRampToValueAtTime(peakGain, ctx.currentTime + 0.008);
        gain.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + durationMs / 1000);
        osc.connect(gain);
        gain.connect(ctx.destination);
        osc.start();
        osc.stop(ctx.currentTime + durationMs / 1000 + 0.02);
    } catch (e) {}
})($0, $1, $2, $3, $4)""")>]
let playTone (ctx: obj) (freq: float) (kind: string) (durationMs: float) (peakGain: float) : unit = jsNative
