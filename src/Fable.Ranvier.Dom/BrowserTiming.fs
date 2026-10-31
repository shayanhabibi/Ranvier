namespace Ranvier.Dom

open System
open System.Collections.Generic
open Fable.Core
open Fable.Core.JsInterop

/// <summary>User timing backed by the active document realm's Performance object.</summary>
module Timing =
    [<Emit("$0.defaultView != null && $0.defaultView.document === $0 && typeof $0.defaultView.performance?.mark === 'function'")>]
    let private current (document: Document) : bool = jsNative

    [<Emit("$0.defaultView.performance")>]
    let private performance (document: Document) : obj = jsNative

    [<Emit("$0.now()")>]
    let private now (target: obj) : float = jsNative

    [<Emit("$0.timeOrigin")>]
    let private origin (target: obj) : float = jsNative

    [<Emit("$0.mark($1, $2)")>]
    let private mark (target: obj) (name: string) (options: obj) : obj = jsNative

    [<Emit("$0.measure($1, $2)")>]
    let private measure (target: obj) (name: string) (options: obj) : obj = jsNative

    [<Emit("$0.getEntries().filter(e => e.entryType === 'mark' || e.entryType === 'measure')")>]
    let private entries (target: obj) : obj array = jsNative

    [<Emit("$0.clearMarks()")>]
    let private clearMarks (target: obj) : unit = jsNative

    [<Emit("$0.clearMarks($1)")>]
    let private clearMarksNamed (target: obj) (name: string) : unit = jsNative

    [<Emit("$0.clearMeasures()")>]
    let private clearMeasures (target: obj) : unit = jsNative

    [<Emit("$0.clearMeasures($1)")>]
    let private clearMeasuresNamed (target: obj) (name: string) : unit = jsNative

    [<Emit("$0[$1]")>]
    let private field (target: obj) (name: string) : obj = jsNative

    [<Emit("$0 == null ? 'null' : Array.isArray($0) ? 'array' : typeof $0")>]
    let internal detailKind (value: obj) : string = jsNative

    [<Emit("Object.keys($0)")>]
    let private keys (value: obj) : string array = jsNative

    [<Emit("Object.fromEntries($0)")>]
    let private objectFields (values: (string * obj) array) : obj = jsNative

    [<Emit("Object.prototype.toString.call($0) === '[object Object]'")>]
    let internal plainObject (value: obj) : bool = jsNative

    let rec internal toRaw =
        function
        | TimingDetail.Null -> null
        | TimingDetail.Boolean value -> box value
        | TimingDetail.Number value -> box value
        | TimingDetail.Text value -> box value
        | TimingDetail.Array values -> values |> Array.map toRaw |> box
        | TimingDetail.Object fields ->
            fields
            |> Array.map (fun (key, value) -> key, toRaw value)
            |> objectFields

    let internal fromRaw value =
        let active = HashSet<obj>(HashIdentity.Reference)

        let container value action =
            if not (active.Add value) then
                TimingContract.error "DataCloneError" "A performance entry detail contains a cycle"

            try
                action ()
            finally
                active.Remove value |> ignore

        let rec copy depth value =
            if depth > 256 then
                TimingContract.error "DataCloneError" "A performance entry detail exceeds 256 levels"

            match detailKind value with
            | "null" -> TimingDetail.Null
            | "boolean" -> TimingDetail.Boolean (unbox value)
            | "number" -> TimingDetail.Number (unbox value)
            | "string" -> TimingDetail.Text (unbox value)
            | "array" ->
                container value (fun () ->
                    TimingDetail.Array (
                        unbox<obj array> value
                        |> Array.map (copy (depth + 1))
                    ))
            | "object" when plainObject value ->
                container value (fun () ->
                    keys value
                    |> Array.map (fun key -> key, copy (depth + 1) (field value key))
                    |> TimingDetail.Object)
            | _ -> TimingContract.error "DataCloneError" "The performance entry has an unsupported detail value"

        copy 0 value

    let private snapshot value : TimingEntry =
        {
            Name = unbox (field value "name")
            Kind =
                if unbox<string>(field value "entryType") = "mark" then
                    TimingEntryKind.Mark
                else
                    TimingEntryKind.Measure
            StartTime = unbox (field value "startTime")
            Duration = unbox (field value "duration")
            Detail = fromRaw (field value "detail")
        }

    let private endpoint =
        function
        | TimingEndpoint.Omitted -> invalidOp "An omitted endpoint has no timestamp"
        | TimingEndpoint.Timestamp value -> box value
        | TimingEndpoint.Mark value -> box value

    /// <summary>Creates a borrowed provider; disposal preserves the realm's user entries.</summary>
    /// <param name="document">The current active document whose Window owns the timing domain.</param>
    let forDocument (document: Document) : TimingService =
        if isNull (box document) then
            nullArg (nameof document)

        if not (current document) then
            raise (NotSupportedException "User timing requires an active document Window")

        let target = performance document
        let mutable disposed = false

        let run action =
            if disposed then
                TimingContract.error "InvalidStateError" "TimingService is disposed"

            if not (current document) then
                raise (InvalidOperationException "The timing document is no longer active")

            try
                action ()
            with
            | TimingException _ -> reraise ()
            | error -> TimingContract.error (unbox<string>(field (box error) "name")) error.Message

        { new TimingService with
            member _.TimeOrigin = run (fun () -> origin target)

            member _.Now() =
                run (fun () -> now target)

            member _.Mark(name, options) =
                run (fun () ->
                    TimingContract.markName name |> ignore
                    TimingContract.markOptions options
                    let detail = TimingContract.copyDetail options.Detail |> toRaw

                    let fields =
                        [
                            yield "detail", detail
                            match options.StartTime with
                            | Some value -> yield "startTime", box (TimingContract.timestamp value)
                            | None -> ()
                        ]

                    mark target name (createObj fields) |> snapshot)

            member _.Measure(name, options) =
                run (fun () ->
                    TimingContract.name name |> ignore
                    TimingContract.measureOptions options
                    let detail = TimingContract.copyDetail options.Detail |> toRaw

                    let fields =
                        [
                            yield "detail", detail
                            match options.Start with
                            | TimingEndpoint.Omitted -> ()
                            | value -> yield "start", endpoint value
                            match options.End with
                            | TimingEndpoint.Omitted -> ()
                            | value -> yield "end", endpoint value
                            match options.Duration with
                            | Some value -> yield "duration", box value
                            | None -> ()
                        ]

                    let fields =
                        if
                            options.Start = TimingEndpoint.Omitted
                            && options.End = TimingEndpoint.Omitted
                        then
                            ("start", box 0.) :: fields
                        else
                            fields

                    measure target name (createObj fields) |> snapshot)

            member _.GetEntries() =
                run (fun () -> entries target |> Array.map snapshot)

            member _.ClearMarks() =
                run (fun () -> clearMarks target)

            member _.ClearMarks(name: string) =
                run (fun () -> clearMarksNamed target (TimingContract.name name))

            member _.ClearMeasures() =
                run (fun () -> clearMeasures target)

            member _.ClearMeasures(name: string) =
                run (fun () -> clearMeasuresNamed target (TimingContract.name name))

            member _.Dispose() =
                disposed <- true
        }
