namespace Ranvier.Dom

open System
open System.Threading.Tasks

/// <summary>A WakeLockType, numbered as the native <c>RV_WAKE_LOCK_*</c> values.</summary>
type WakeLockType =
    | Screen = 0

/// <summary>A rejected wake lock request with the engine's exception name: NotAllowedError, or TypeError for a type outside WakeLockType.</summary>
exception WakeLockException of name: string * detail: string with
    override this.Message = this.name + ": " + this.detail

/// <summary>A screen wake lock, held until its owner, the document or the application releases it.</summary>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type WakeLockSentinel =
    /// <summary>Whether the lock has been released; a released sentinel stays released.</summary>
    abstract Released: bool
    abstract Type: WakeLockType
    /// <summary>Releases the lock. The release callbacks run before the task completes; a released sentinel runs none.</summary>
    abstract Release: unit -> Task<unit>
    /// <summary>Runs <c>callback</c> at each release event, in registration order, until the result is disposed.</summary>
    /// <remarks>A lock released because its document was replaced or destroyed runs no callback.</remarks>
    abstract OnRelease: callback: (unit -> unit) -> IDisposable

/// <summary>Screen wake lock requests for one document.</summary>
/// <remarks>
/// A hidden document releases its locks, each with its release event, and rejects requests. A browser provider grants
/// as the browser's permission model decides; a native provider grants as the application's policy decides.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type WakeLockService =
    inherit IDisposable
    /// <summary>Requests a lock of <c>kind</c>; the task completes after the call returns.</summary>
    /// <remarks>
    /// The task faults with WakeLockException when the request is rejected. When <c>signal</c> aborts, the lock is
    /// released; a signal that has already aborted faults the task with AbortedException and requests nothing.
    /// </remarks>
    abstract Request: kind: WakeLockType * signal: AbortSignal option -> Task<WakeLockSentinel>

/// <summary>Argument checks shared by wake lock providers.</summary>
module WakeLockContract =
    /// <summary>The WakeLockType name of <c>kind</c>; a value outside the enumeration keeps its number, which a browser rejects with TypeError.</summary>
    let typeName (kind: WakeLockType) =
        match kind with
        | WakeLockType.Screen -> "screen"
        | other -> string (int other)

    /// <summary>The TypeError a native provider reports for a value outside WakeLockType.</summary>
    let unknownType (kind: WakeLockType) =
        WakeLockException (
            "TypeError",
            "'"
            + typeName kind
            + "' is not a valid enum value of type WakeLockType"
        )

    /// <summary>The signal of a request: <c>Some null</c> reads as None.</summary>
    let signal (value: AbortSignal option) =
        match value with
        | Some signal when not (isNull (box signal)) -> Some signal
        | _ -> None

    /// <summary>Releases <c>sentinel</c> when <c>signal</c> aborts, or at once when it has aborted; disposing the result unregisters.</summary>
    let bind (signal: AbortSignal option) (sentinel: WakeLockSentinel) : IDisposable =
        let release () =
            if not sentinel.Released then
                sentinel.Release () |> ignore

        match signal with
        | Some signal when signal.Aborted ->
            release ()

            { new IDisposable with
                member _.Dispose() = ()
            }
        | Some signal -> signal.OnAbort (fun _ -> release ())
        | None ->
            { new IDisposable with
                member _.Dispose() = ()
            }
