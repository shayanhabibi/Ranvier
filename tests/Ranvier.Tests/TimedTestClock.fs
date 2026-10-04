module Ranvier.Tests.TimedTestClock

open System
open Ranvier

type private Entry =
    {
        Callback: Action
        mutable Due: float
        mutable Order: int
        mutable Disposed: bool
    }

type ManualClock() =
    inherit TimedClock()
    let entries = ResizeArray<Entry>()
    let mutable now = 0.
    let mutable order = 0
    let mutable advancing = false
    let mutable arms = 0
    member _.TimerCount = entries.Count
    member _.Arms = arms
    override _.NowMilliseconds = now

    override _.CreateTimer(callback) =
        let entry =
            {
                Callback = callback
                Due = infinity
                Order = 0
                Disposed = false
            }

        entries.Add entry

        { new TimedTimer() with
            member _.Arm delay =
                if delay < TimeSpan.Zero then
                    invalidArg (nameof delay) "negative wait"

                if entry.Disposed then
                    raise (ObjectDisposedException "timer")

                order <- order + 1
                arms <- arms + 1
                entry.Order <- order
                entry.Due <- now + delay.TotalMilliseconds

            member _.Disarm() =
                entry.Due <- infinity

            member _.Dispose() =
                entry.Disposed <- true
                entry.Due <- infinity
        }

    member _.AdvanceTo(target) =
        if target < now || advancing then
            invalidArg (nameof target) "invalid clock advance"

        advancing <- true

        try
            let mutable more = true

            while more do
                let mutable next = None

                for entry in entries do
                    if not entry.Disposed && entry.Due <= target then
                        match next with
                        | None -> next <- Some entry
                        | Some current when
                            entry.Due < current.Due
                            || (entry.Due = current.Due
                                && entry.Order < current.Order)
                            ->
                            next <- Some entry
                        | _ -> ()

                match next with
                | None -> more <- false
                | Some entry ->
                    now <- entry.Due
                    entry.Due <- infinity
                    entry.Callback.Invoke ()

            now <- target
        finally
            advancing <- false

    member _.FireStale() =
        for entry in entries do
            entry.Callback.Invoke ()

#if NET8_0_OR_GREATER && !FABLE_COMPILER
type ManualTimeProvider(clock: ManualClock) =
    inherit TimeProvider()
    override _.TimestampFrequency = 1000L

    override _.GetTimestamp() =
        int64 clock.NowMilliseconds

    override _.CreateTimer(callback, state, due, period) =
        if period <> Threading.Timeout.InfiniteTimeSpan then
            invalidArg (nameof period) "one-shot timer expected"

        let timer = clock.CreateTimer (Action (fun () -> callback.Invoke state))

        if due <> Threading.Timeout.InfiniteTimeSpan then
            timer.Arm due

        { new Threading.ITimer with
            member _.Change(delay, interval) =
                if interval <> Threading.Timeout.InfiniteTimeSpan then
                    invalidArg (nameof interval) "one-shot timer expected"

                if delay = Threading.Timeout.InfiniteTimeSpan then
                    timer.Disarm ()
                else
                    timer.Arm delay

                true

            member _.Dispose() =
                timer.Dispose ()

            member _.DisposeAsync() =
                timer.Dispose ()
                Threading.Tasks.ValueTask ()
        }
#endif
