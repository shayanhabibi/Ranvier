namespace Ranvier.Dom

open System

/// <summary>The unit a text segmenter splits text into.</summary>
[<RequireQualifiedAccess>]
type SegmentGranularity =
    /// <summary>Extended grapheme clusters: what a reader sees as one character.</summary>
    | Grapheme
    /// <summary>Words, with the spaces and punctuation between them as their own segments.</summary>
    | Word
    /// <summary>Sentences.</summary>
    | Sentence
    /// <summary>Spans between line-break opportunities (CSS <c>line-break: normal</c>).</summary>
    | Line

/// <summary>One segment of a text, as <c>Intl.Segmenter</c> reports it.</summary>
type TextSegment =
    {
        /// <summary>The offset of the segment in the text, in UTF-16 code units.</summary>
        Index: int
        Segment: string
        /// <summary>For words, whether the segment is a word rather than spaces or punctuation; false otherwise.</summary>
        IsWordLike: bool
    }

/// <summary>The segments of a text and the locale that segmented it.</summary>
type TextSegmentation =
    {
        Segments: TextSegment array
        /// <summary>The locale the provider used, as it reports it.</summary>
        Locale: string
    }

/// <summary>The result of a segmentation request.</summary>
[<RequireQualifiedAccess>]
type SegmentationResult =
    | Segmented of segmentation: TextSegmentation
    /// <summary>The provider has no segmenter for the granularity, such as line breaks in a browser without one.</summary>
    | NotSupported of granularity: SegmentGranularity

/// <summary>
/// Splits text into graphemes, words, sentences or line-break opportunities with the Unicode rules and dictionaries of
/// one engine. Every word, sentence and line boundary is a grapheme boundary.
/// </summary>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type TextSegmenter =
    inherit IDisposable
    /// <summary>
    /// The segments of <c>text</c>; <c>locale</c> is a BCP 47 language tag, or <c>None</c> for the provider's default.
    /// A tag that is not well formed raises a RangeError.
    /// </summary>
    abstract Segment: text: string * granularity: SegmentGranularity * locale: string option -> SegmentationResult

/// <summary>Argument validation shared by text segmenter providers.</summary>
module TextSegmentationContract =
    /// <summary>Raises ArgumentNullException for a null text.</summary>
    let text (value: string) =
        if isNull value then
            nullArg "text"

        value
