/// The three DOM touchpoints the composition root needs. Kept deliberately
/// tiny; everything else goes through Pixi or React.
module MergeTowerDefense.Interop.Dom

open Fable.Core

[<Emit("document.getElementById($0)")>]
let getElementById (id: string) : obj = jsNative

[<Emit("$0.appendChild($1)")>]
let appendChild (parent: obj) (child: obj) : unit = jsNative

[<Emit("globalThis")>]
let globalThis: obj = jsNative

[<Emit("window.addEventListener('keydown', $0)")>]
let onKeyDown (handler: obj -> unit) : unit = jsNative
