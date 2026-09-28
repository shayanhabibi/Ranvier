namespace Ranvier.Docs.Maps

#if RANVIER_TRACE
open System
open System.Text
open Ranvier

/// <summary>Recorded trace events as F# source.</summary>
[<RequireQualifiedAccess>]
module Replay =

    /// <summary>The events with every payload replaced by its canonical text.</summary>
    /// <remarks>Folds to the same snapshot as the original events.</remarks>
    let normalise (events: TraceEvent[]) : TraceEvent[] =
        events
        |> Array.map (fun e ->
            if isNull e.Payload then
                e
            else
                { e with
                    Payload = box (TraceModel.payloadText e.Payload)
                })

    /// <summary>A recorded event; <c>kind</c> is a <c>TraceEventKind</c> value.</summary>
    let event seq kind node other arg flag cause (payload: string) : TraceEvent =
        {
            Seq = seq
            Kind = enum<TraceEventKind> kind
            Node = node
            Other = other
            Arg = arg
            Flag = flag
            Cause = cause
            Payload = box payload
        }

    let private quote (text: string) =
        let sb = StringBuilder (text.Length + 2)
        sb.Append '"' |> ignore

        for c in text do
            match c with
            | '"' -> sb.Append "\\\"" |> ignore
            | '\\' -> sb.Append "\\\\" |> ignore
            | '\n' -> sb.Append "\\n" |> ignore
            | '\r' -> sb.Append "\\r" |> ignore
            | '\t' -> sb.Append "\\t" |> ignore
            | c when Char.IsControl c ->
                sb.Append("\\u").Append((int c).ToString "x4")
                |> ignore
            | c -> sb.Append c |> ignore

        sb.Append('"').ToString()

    /// <summary>An F# array literal of <c>Replay.event</c> calls, one event per line, payloads normalised.</summary>
    let literal (events: TraceEvent[]) : string =
        let lines =
            normalise events
            |> Array.map (fun e ->
                let payload =
                    match e.Payload with
                    | :? string as s -> quote s
                    | _ -> "null"

                $"    Replay.event %d{e.Seq} %d{int e.Kind} %d{e.Node} %d{e.Other} %d{e.Arg} %d{e.Flag} %d{e.Cause} %s{payload}")

        "[|\n" + String.concat "\n" lines + "\n|]"
#endif
