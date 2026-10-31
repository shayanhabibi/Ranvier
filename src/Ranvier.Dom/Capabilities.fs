namespace Ranvier.Dom

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks

/// <summary>The runtime observation domain; format domains describe support queries.</summary>
[<RequireQualifiedAccess>]
type CapabilityCategory =
    | Abi
    | RendererSetting
    | ImageFormat
    | FontFormat
    | PlatformFacility

/// <summary>A category and normalized nonempty name; use Capabilities.key to validate external input.</summary>
type CapabilityKey =
    {
        Category: CapabilityCategory
        Name: string
    }

/// <summary>An independently observed fact, with Unknown for unobservable or contradictory evidence.</summary>
[<RequireQualifiedAccess>]
type CapabilityState =
    | Yes
    | No
    | Unknown

/// <summary>A fact and the reason supported by its provider's evidence.</summary>
type CapabilityFact =
    {
        State: CapabilityState
        Reason: string
    }

/// <summary>A current observed value; numbers must be finite and name arrays are copied.</summary>
[<RequireQualifiedAccess>]
type CapabilityValue =
    | Boolean of bool
    | Number of float
    | Text of string
    | Names of string array

/// <summary>Independent runtime facts with provider identity and observation mechanism.</summary>
type CapabilityObservation =
    {
        Key: CapabilityKey
        Exported: CapabilityFact
        Implemented: CapabilityFact
        Enabled: CapabilityFact
        Permitted: CapabilityFact
        Available: CapabilityFact
        Value: CapabilityValue option
        ProviderId: string
        Mechanism: string
        Scope: string
    }

/// <summary>A combined snapshot retaining every contribution, including conflicting and unknown facts.</summary>
type CapabilityReport =
    {
        Key: CapabilityKey
        Exported: CapabilityFact
        Implemented: CapabilityFact
        Enabled: CapabilityFact
        Permitted: CapabilityFact
        Available: CapabilityFact
        Value: CapabilityValue option
        Observations: CapabilityObservation array
    }

/// <summary>A named provider; callbacks start on the caller's thread and may complete asynchronously.</summary>
/// <remarks>Callbacks return None for unrecognized keys. Cancellation is cooperative; late results are discarded.</remarks>
type CapabilityProvider =
    {
        Id: string
        KnownKeys: CapabilityKey array
        Observe: CapabilityKey -> CancellationToken -> Task<CapabilityObservation option>
    }

/// <summary>A context or affinity failure that must propagate instead of becoming unknown provider evidence.</summary>
exception CapabilityContextException of detail: string with
    override this.Message = this.detail

/// <summary>An extensible observation registry; each read uses current providers without caching runtime facts.</summary>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type CapabilityService =
    inherit IDisposable
    /// <summary>Returns copied distinct keys in provider and catalogue order.</summary>
    abstract KnownKeys: unit -> CapabilityKey array
    /// <summary>Observes a key; cancellation or disposal prevents publication of pending results.</summary>
    abstract Observe: key: CapabilityKey * cancellationToken: CancellationToken -> Task<CapabilityReport>
    /// <summary>Registers a unique provider ID and returns an idempotent removal handle.</summary>
    abstract Register: provider: CapabilityProvider -> IDisposable

/// <summary>Task completion primitives shared by managed and browser capability providers.</summary>
module internal CapabilityTasks =
#if FABLE_COMPILER
    [<Fable.Core.Emit("new Promise($0)")>]
    let private promise (factory: ('T -> unit) -> (exn -> unit) -> unit) : Task<'T> =
        Unchecked.defaultof<_>

    [<Fable.Core.Emit("Promise.all($0)")>]
    let all (values: Task<'T> array) : Task<'T array> =
        Unchecked.defaultof<_>

    [<Fable.Core.Emit("$0.catch($1)")>]
    let private rejection (value: Task<'T>) (reject: exn -> unit) : unit =
        Unchecked.defaultof<_>
#else
    /// <summary>Waits for all provider contributions in their registration order.</summary>
    let all (values: Task<'T> array) : Task<'T array> =
        Task.WhenAll values
#endif

    /// <summary>Publishes at most one result, error or cancellation.</summary>
    type Completion<'T>() =
#if FABLE_COMPILER
        let mutable completed = false
        let mutable resolve = fun (_: 'T) -> ()
        let mutable reject = fun (_: exn) -> ()

        let value =
            promise (fun yes no ->
                resolve <- yes
                reject <- no)

        let finish action =
            if completed then
                false
            else
                completed <- true
                action ()
                true

        member _.Task = value

        member _.TrySetResult item =
            finish (fun () -> resolve item)

        member _.TrySetException error =
            finish (fun () -> reject error)

        member _.TrySetCanceled(token: CancellationToken) =
            finish (fun () ->
                Async.StartAsTask (async { return Unchecked.defaultof<'T> }, cancellationToken = token)
                |> fun cancelled -> rejection cancelled reject)
#else
        let completion =
            TaskCompletionSource<'T>(TaskCreationOptions.RunContinuationsAsynchronously)

        member _.Task = completion.Task

        member _.TrySetResult item =
            completion.TrySetResult item

        member _.TrySetException(error: exn) =
            completion.TrySetException error

        member _.TrySetCanceled token =
            completion.TrySetCanceled token
#endif

    /// <summary>Returns a task already cancelled by the supplied token.</summary>
    let cancelled<'T> token : Task<'T> =
        let result = Completion<'T>()
        result.TrySetCanceled token |> ignore
        result.Task

/// <summary>Creates validated capability keys and pure observation registries.</summary>
[<RequireQualifiedAccess>]
module Capabilities =
    let private text argument (value: string) =
        if isNull value then
            nullArg argument

        if
            value.Length = 0
            || value |> Seq.exists Char.IsControl
        then
            invalidArg argument "Expected a nonempty string without control characters."

        value

    /// <summary>Validates a key; image and font query names use invariant lowercase, while ABI names preserve case.</summary>
    let key category name =
        if isNull (box category) then
            nullArg "category"

        let name = text "name" name

        let normalized =
            match category with
            | CapabilityCategory.ImageFormat
            | CapabilityCategory.FontFormat ->
                if
                    name
                    |> Seq.exists (fun c -> int c > 127 || Char.IsWhiteSpace c)
                then
                    invalidArg "name" "Format names must be ASCII without whitespace."

                let token (value: string) =
                    value
                    |> Seq.forall (fun c ->
                        Char.IsLetterOrDigit c
                        || "!#$%&'*+-.^_`|~".Contains c)

                match category with
                | CapabilityCategory.FontFormat when not (token name) -> invalidArg "name" "Font formats must be individual tokens."
                | CapabilityCategory.ImageFormat ->
                    match name.Split '/' with
                    | [| major; minor |] when
                        major.Length > 0
                        && minor.Length > 0
                        && token major
                        && token minor
                        ->
                        ()
                    | _ -> invalidArg "name" "Image formats require a MIME type without parameters."
                | _ -> ()

                name.ToLowerInvariant ()
            | _ -> name

        {
            Category = category
            Name = normalized
        }

    let private validatedKey (value: CapabilityKey) =
        if isNull (box value) then
            nullArg "key"

        key value.Category value.Name

    /// <summary>Creates an all-unknown contribution with explicit provider provenance.</summary>
    let unknown key providerId mechanism scope reason =
        let fact =
            {
                State = CapabilityState.Unknown
                Reason = reason
            }

        {
            Key = validatedKey key
            Exported = fact
            Implemented = fact
            Enabled = fact
            Permitted = fact
            Available = fact
            Value = None
            ProviderId = text "providerId" providerId
            Mechanism = text "mechanism" mechanism
            Scope = text "scope" scope
        }

    let private copyValue =
        function
        | CapabilityValue.Number value when Double.IsNaN value || Double.IsInfinity value -> invalidArg "value" "Capability numbers must be finite."
        | CapabilityValue.Text value when isNull value -> nullArg "value"
        | CapabilityValue.Names values ->
            if isNull values then
                nullArg "values"

            values
            |> Array.iter (fun name -> text "values" name |> ignore)

            CapabilityValue.Names (Array.copy values)
        | value -> value

    let private copyObservation expectedKey expectedId (value: CapabilityObservation) =
        if isNull (box value) then
            nullArg "observation"

        if
            validatedKey value.Key <> expectedKey
            || value.ProviderId <> expectedId
        then
            invalidArg "observation" "The observation key and provider ID must match the request."

        for fact in
            [
                value.Exported
                value.Implemented
                value.Enabled
                value.Permitted
                value.Available
            ] do
            if isNull (box fact) || isNull fact.Reason then
                invalidArg "observation" "Facts require reasons."

        text "mechanism" value.Mechanism |> ignore
        text "scope" value.Scope |> ignore

        { value with
            Key = expectedKey
            Value = Option.map copyValue value.Value
        }

    let private combine select (observations: CapabilityObservation array) =
        let known =
            observations
            |> Array.map (fun value -> (select value).State)
            |> Array.filter ((<>) CapabilityState.Unknown)
            |> Array.distinct

        match known with
        | [| state |] ->
            {
                State = state
                Reason = "Agreeing observed facts; see contributions."
            }
        | [||] ->
            {
                State = CapabilityState.Unknown
                Reason = "No known observation; see contributions."
            }
        | _ ->
            {
                State = CapabilityState.Unknown
                Reason = "Conflicting observations; both facts are retained."
            }

    let private report key (observations: CapabilityObservation array) : CapabilityReport =
        let values =
            observations
            |> Array.choose _.Value
            |> Array.distinct

        {
            Key = key
            Exported = combine (fun (value: CapabilityObservation) -> value.Exported) observations
            Implemented = combine (fun value -> value.Implemented) observations
            Enabled = combine (fun value -> value.Enabled) observations
            Permitted = combine (fun value -> value.Permitted) observations
            Available = combine (fun value -> value.Available) observations
            Value =
                if values.Length = 1 then
                    Some (copyValue values[0])
                else
                    None
            Observations = Array.copy observations
        }

    let private registerCancellation (token: CancellationToken) action : IDisposable =
#if FABLE_COMPILER
        let cancellable = not (isNull (box token))
#else
        let cancellable = token.CanBeCanceled
#endif
        if cancellable then
            token.Register (Action action) :> IDisposable
        else
            { new IDisposable with
                member _.Dispose() = ()
            }

    /// <summary>Creates an empty registry; disposal cancels pending publication even when providers ignore cancellation.</summary>
    let create () : CapabilityService =
        let gate = obj ()
        let providers = ResizeArray<CapabilityProvider>()
        let lifetime = new CancellationTokenSource ()
        let mutable disposed = false

        let active () =
            if disposed then
                raise (CapabilityContextException "CapabilityService is disposed.")

        let snapshot () =
            lock gate (fun () ->
                active ()
                providers.ToArray ())

        let observeProvider requestKey token provider =
            task {
                try
                    let pending = provider.Observe requestKey token

                    if isNull pending then
                        invalidOp "Provider returned a null task."

                    let! value = pending

                    return
                        value
                        |> Option.map (copyObservation requestKey provider.Id)
                with
#if !FABLE_COMPILER
                | :? ObjectDisposedException as error -> return raise error
#endif
                | CapabilityContextException _ as error -> return raise error
                | :? OperationCanceledException as error when token.IsCancellationRequested -> return raise error
                | error -> return Some (unknown requestKey provider.Id "provider failure" "provider" error.Message)
            }

        { new CapabilityService with
            member _.KnownKeys() =
                snapshot ()
                |> Array.collect _.KnownKeys
                |> Array.distinct

            member _.Register provider =
                if isNull (box provider) then
                    nullArg "provider"

                text "provider.Id" provider.Id |> ignore

                if isNull provider.KnownKeys then
                    nullArg "provider.KnownKeys"

                if isNull (box provider.Observe) then
                    nullArg "provider.Observe"

                let prepared =
                    { provider with
                        KnownKeys =
                            provider.KnownKeys
                            |> Array.map validatedKey
                            |> Array.distinct
                    }

                lock gate (fun () ->
                    active ()

                    if
                        providers
                        |> Seq.exists (fun current -> current.Id = prepared.Id)
                    then
                        invalidArg "provider" "Provider IDs must be unique."

                    providers.Add prepared)

                let mutable removed = false

                { new IDisposable with
                    member _.Dispose() =
                        lock gate (fun () ->
                            if not removed then
                                removed <- true
                                providers.Remove prepared |> ignore)
                }

            member _.Observe(requestKey, token) =
                let requestKey = validatedKey requestKey

                if token.IsCancellationRequested then
                    CapabilityTasks.cancelled<CapabilityReport> token
                else
                    let current = snapshot ()
                    let result = CapabilityTasks.Completion<CapabilityReport>()
                    let linked = new CancellationTokenSource ()
                    let requestCancellation = registerCancellation token linked.Cancel
                    let lifetimeCancellation = registerCancellation lifetime.Token linked.Cancel

                    let cancellation =
                        registerCancellation linked.Token (fun () ->
                            lock gate (fun () ->
                                if disposed then
                                    result.TrySetException (CapabilityContextException "CapabilityService is disposed.")
                                    |> ignore
                                else
                                    result.TrySetCanceled (token) |> ignore))

                    let finish =
                        task {
                            try
                                let! observations =
                                    current
                                    |> Array.map (observeProvider requestKey linked.Token)
                                    |> CapabilityTasks.all

                                let combined = report requestKey (Array.choose id observations)

                                lock gate (fun () ->
                                    if disposed then
                                        result.TrySetException (CapabilityContextException "CapabilityService is disposed.")
                                        |> ignore
                                    elif token.IsCancellationRequested then
                                        result.TrySetCanceled (token) |> ignore
                                    else
                                        result.TrySetResult combined |> ignore)
                            with error ->
                                result.TrySetException error |> ignore

                            cancellation.Dispose ()
                            requestCancellation.Dispose ()
                            lifetimeCancellation.Dispose ()
                            linked.Dispose ()
                        }

                    finish |> ignore
                    result.Task

            member _.Dispose() =
                let invalidate =
                    lock gate (fun () ->
                        if disposed then
                            false
                        else
                            disposed <- true
                            providers.Clear ()
                            true)

                if invalidate then
                    lifetime.Cancel ()
        }
