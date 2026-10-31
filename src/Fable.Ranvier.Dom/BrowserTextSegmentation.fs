namespace Ranvier.Dom

open System
open Fable.Core
open Fable.Core.JsInterop

/// <summary>Text segmentation with the browser's Intl.Segmenter, and Intl.v8BreakIterator for line breaks.</summary>
module BrowserTextSegmentation =
    [<Emit("typeof Intl.v8BreakIterator === 'function'")>]
    let private hasLineIterator () : bool = jsNative

    [<Emit("new Intl.Segmenter($0, { granularity: $1 })")>]
    let private segmenter (locale: string option) (granularity: string) : obj = jsNative

    [<Emit("Array.from($0.segment($1), s => [s.index, s.segment, s.isWordLike === true])")>]
    let private segments (segmenter: obj) (text: string) : (int * string * bool) array = jsNative

    [<Emit("$0.resolvedOptions().locale")>]
    let private resolvedLocale (segmenter: obj) : string = jsNative

    [<Emit("((locale, text) => { const it = new Intl.v8BreakIterator(locale === undefined ? [] : [locale], { type: 'line' }); it.adoptText(text); const out = []; for (let end = it.next(); end !== -1; end = it.next()) out.push(end); return [out, it.resolvedOptions().locale]; })($0, $1)")>]
    let private lineEnds (locale: string option) (text: string) : int array * string = jsNative

    [<Emit("Array.from(new Intl.Segmenter($0, { granularity: 'grapheme' }).segment($1), s => s.index)")>]
    let private graphemeStarts (locale: string option) (text: string) : int array = jsNative

    /// <summary>Joins segments that start inside a grapheme cluster with the segment before them.</summary>
    let private onGraphemes (locale: string option) (text: string) (items: TextSegment array) =
        let boundaries = graphemeStarts locale text |> Set.ofArray
        let joined = ResizeArray<TextSegment>()

        for item in items do
            if
                joined.Count > 0
                && not (boundaries.Contains item.Index)
            then
                let last = joined[joined.Count - 1]

                joined[joined.Count - 1] <-
                    { last with
                        Segment = last.Segment + item.Segment
                        IsWordLike = last.IsWordLike || item.IsWordLike
                    }
            else
                joined.Add item

        joined.ToArray ()

    /// <summary>A text segmenter over the browser's Intl.Segmenter.</summary>
    let create () : TextSegmenter =
        let disposed = ref false

        let check () =
            if disposed.Value then
                raise (ObjectDisposedException (nameof TextSegmenter))

        { new TextSegmenter with
            member _.Segment(text, granularity, locale) =
                check ()
                let text = TextSegmentationContract.text text

                match granularity with
                | SegmentGranularity.Line when not (hasLineIterator ()) -> SegmentationResult.NotSupported granularity
                | SegmentGranularity.Line ->
                    // The locale is validated as Intl.Segmenter validates it.
                    segmenter locale "grapheme" |> ignore
                    let ends, resolved = lineEnds locale text

                    let starts =
                        Array.append [| 0 |] ends
                        |> Array.take ends.Length

                    let items =
                        Array.map2
                            (fun (start: int) (finish: int) ->
                                {
                                    Index = start
                                    Segment = text.Substring (start, finish - start)
                                    IsWordLike = false
                                })
                            starts
                            ends

                    SegmentationResult.Segmented
                        {
                            Segments = onGraphemes locale text items
                            Locale = resolved
                        }
                | _ ->
                    let name =
                        match granularity with
                        | SegmentGranularity.Grapheme -> "grapheme"
                        | SegmentGranularity.Word -> "word"
                        | _ -> "sentence"

                    let instance = segmenter locale name

                    let items =
                        segments instance text
                        |> Array.map (fun (index, segment, wordLike) ->
                            {
                                Index = index
                                Segment = segment
                                IsWordLike = granularity = SegmentGranularity.Word && wordLike
                            })

                    SegmentationResult.Segmented
                        {
                            Segments =
                                if granularity = SegmentGranularity.Grapheme then
                                    items
                                else
                                    onGraphemes locale text items
                            Locale = resolvedLocale instance
                        }

            member _.Dispose() =
                disposed.Value <- true
        }
