#load "../../src/Ranvier.Dom/Capabilities.fs"

open System
open System.Threading
open System.Threading.Tasks
open Ranvier.Dom

let mutable checks = 0

let equal expected actual =
    checks <- checks + 1

    if expected <> actual then
        failwithf "Expected %A, got %A" expected actual

let key = Capabilities.key CapabilityCategory.PlatformFacility "test"

let observation id state =
    { Capabilities.unknown key id "fixture" "pure" "unobserved" with
        Implemented =
            {
                State = state
                Reason = "fixture fact"
            }
    }

let provider id state =
    {
        Id = id
        KnownKeys = [| key |]
        Observe = fun _ _ -> Task.FromResult (Some (observation id state))
    }

let get (task: Task<'T>) =
    task.GetAwaiter().GetResult()

let observe (service: CapabilityService) =
    service.Observe (key, CancellationToken.None)
    |> get

let run () =
    use service = Capabilities.create ()
    equal CapabilityState.Unknown (observe service).Implemented.State
    use yes = service.Register (provider "yes" CapabilityState.Yes)
    equal CapabilityState.Yes (observe service).Implemented.State
    equal CapabilityState.Unknown (observe service).Exported.State
    use no = service.Register (provider "no" CapabilityState.No)
    let conflict = observe service
    equal CapabilityState.Unknown conflict.Implemented.State

    equal
        [| CapabilityState.Yes; CapabilityState.No |]
        (conflict.Observations
         |> Array.map _.Implemented.State)

    no.Dispose ()
    equal CapabilityState.Yes (observe service).Implemented.State
    equal [| key |] (service.KnownKeys ())
    let keys = service.KnownKeys ()
    keys[0] <- Capabilities.key CapabilityCategory.Abi "changed"
    equal [| key |] (service.KnownKeys ())
    equal "image/png" (Capabilities.key CapabilityCategory.ImageFormat "IMAGE/PNG").Name
    equal "dom:Window" (Capabilities.key CapabilityCategory.Abi "dom:Window").Name

    for category, name in
        [
            CapabilityCategory.FontFormat, "woff)or(font-format(truetype"
            CapabilityCategory.ImageFormat, "image/png;bad"
        ] do
        try
            Capabilities.key category name |> ignore
            failwith "Expected a format token error"
        with :? ArgumentException ->
            checks <- checks + 1

    use cancelled = new CancellationTokenSource ()
    cancelled.Cancel ()

    try
        service.Observe (key, cancelled.Token)
        |> get
        |> ignore

        failwith "Expected cancellation"
    with :? OperationCanceledException ->
        checks <- checks + 1

    use pendingService = Capabilities.create ()
    let pending = TaskCompletionSource<CapabilityObservation option>()

    use registration =
        pendingService.Register
            {
                Id = "pending"
                KnownKeys = [| key |]
                Observe = fun _ _ -> pending.Task
            }

    use cancellation = new CancellationTokenSource ()
    let request = pendingService.Observe (key, cancellation.Token)
    cancellation.Cancel ()
    equal true request.IsCompleted

    try
        get request |> ignore
        failwith "Expected cancellation"
    with :? OperationCanceledException ->
        checks <- checks + 1

    pending.SetResult (Some (observation "pending" CapabilityState.Yes))
    equal true request.IsCanceled

    let late = TaskCompletionSource<CapabilityObservation option>()
    use disposingService = Capabilities.create ()

    use lateRegistration =
        disposingService.Register
            {
                Id = "late"
                KnownKeys = [| key |]
                Observe = fun _ _ -> late.Task
            }

    let disposedRequest = disposingService.Observe (key, CancellationToken.None)
    disposingService.Dispose ()
    equal true disposedRequest.IsCompleted

    try
        get disposedRequest |> ignore
        failwith "Expected disposal"
    with CapabilityContextException _ ->
        checks <- checks + 1

    late.SetResult (Some (observation "late" CapabilityState.Yes))
    equal true disposedRequest.IsFaulted
    use valuesService = Capabilities.create ()
    let names = [| "original" |]

    use namesProvider =
        valuesService.Register
            {
                Id = "names"
                KnownKeys = [| key |]
                Observe =
                    fun _ _ ->
                        Task.FromResult (
                            Some
                                { observation "names" CapabilityState.Unknown with
                                    Value = Some (CapabilityValue.Names names)
                                }
                        )
            }

    let valuesReport = observe valuesService
    names[0] <- "changed"
    equal (Some (CapabilityValue.Names [| "original" |])) valuesReport.Value
    equal (Some (CapabilityValue.Names [| "original" |])) valuesReport.Observations[0].Value

    match valuesReport.Value with
    | Some (CapabilityValue.Names copied) -> copied[0] <- "caller change"
    | _ -> failwith "Expected names"

    equal (Some (CapabilityValue.Names [| "original" |])) valuesReport.Observations[0].Value
    use failureService = Capabilities.create ()

    use failed =
        failureService.Register
            {
                Id = "failure"
                KnownKeys = [||]
                Observe = fun _ _ -> failwith "provider failed"
            }

    let failure = observe failureService
    equal CapabilityState.Unknown failure.Implemented.State
    equal true (failure.Observations[0].Implemented.Reason.Contains "provider failed")

    try
        service.Register (provider "yes" CapabilityState.No)
        |> ignore

        failwith "Expected duplicate ID error"
    with :? ArgumentException ->
        checks <- checks + 1

    for name in [ ""; "invalid\000name"; "invalid\nname" ] do
        try
            Capabilities.key CapabilityCategory.Abi name
            |> ignore

            failwith "Expected malformed key error"
        with :? ArgumentException ->
            checks <- checks + 1

    for value in
        [
            None
            Some (CapabilityValue.Boolean false)
            Some (CapabilityValue.Number 0.)
            Some (CapabilityValue.Text "")
        ] do
        use valueService = Capabilities.create ()

        use entry =
            valueService.Register
                {
                    Id = "value"
                    KnownKeys = [| key |]
                    Observe =
                        fun _ _ ->
                            Task.FromResult (
                                Some
                                    { observation "value" CapabilityState.Unknown with
                                        Value = value
                                    }
                            )
                }

        equal value (observe valueService).Value

    printfn "CAPABILITY_REGISTRY_CHECKS=%d" checks

run ()
