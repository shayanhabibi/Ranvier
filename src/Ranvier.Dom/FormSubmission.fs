namespace Ranvier.Dom

open System

/// <summary>A form submission an application receives in place of the navigation it requests.</summary>
type FormSubmission =
    {
        /// <summary><c>GET</c> or <c>POST</c>.</summary>
        Method: string
        /// <summary>The request URL; a GET carries the entry list in its query.</summary>
        Url: string
        /// <summary>The submitter's <c>formtarget</c>, the form's <c>target</c> or the base target, such as <c>_blank</c>, or "" without one.</summary>
        Target: string
        /// <summary>
        /// None for a GET; <c>application/x-www-form-urlencoded</c>, <c>text/plain</c> or
        /// <c>multipart/form-data; boundary=...</c> for a POST.
        /// </summary>
        ContentType: string option
        /// <summary>The encoded entry list of a POST, empty for a GET. A multipart File part carries its headers and no file bytes.</summary>
        Body: byte array
        Form: HTMLFormElement
        Submitter: HTMLElement option
    }

/// <summary>Reports the GET and POST submissions of one document's forms, which keep the document in place.</summary>
/// <remarks>
/// A submission reaches the handlers from a later task, after its submit and formdata events and interactive
/// validation have run; a later submission of a form replaces its pending one. A <c>dialog</c> method submission
/// closes its dialog as usual. A submission with no handler is dropped. Disposal restores the platform behaviour:
/// a native host drops submissions, and a browser document navigates.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type FormSubmissionService =
    inherit IDisposable
    /// <summary>Adds <c>handler</c> for each later submission, until the result is disposed.</summary>
    abstract Subscribe: handler: (FormSubmission -> unit) -> IDisposable

/// <summary>Argument validation and handler dispatch shared by FormSubmission providers.</summary>
module FormSubmissionContract =
    /// <summary>Raises ArgumentNullException for a null handler.</summary>
    let handler (value: FormSubmission -> unit) =
        if isNull (box value) then
            nullArg "handler"

        value

    /// <summary>The handler list of a provider: subscription, removal and delivery in subscription order.</summary>
    type Handlers() =
        let mutable handlers: (int * (FormSubmission -> unit)) list = []
        let mutable next = 0

        /// <summary>Adds <c>handler</c>; disposing the result removes it.</summary>
        member _.Add(handler: FormSubmission -> unit) : IDisposable =
            let id = next
            next <- next + 1
            handlers <- handlers @ [ id, handler ]

            { new IDisposable with
                member _.Dispose() =
                    handlers <-
                        handlers
                        |> List.filter (fun (other, _) -> other <> id)
            }

        /// <summary>Calls each handler subscribed when delivery starts.</summary>
        member _.Deliver(submission: FormSubmission) =
            for _, handler in handlers do
                handler submission

        member _.Clear() =
            handlers <- []
