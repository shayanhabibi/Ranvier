module SnapshotBench

let mutable private sink = 0

let private arrayLookup width (snapshot: (int * int) array) =
    let mutable total = 0

    for key in width - 1 .. -1 .. 0 do
        total <-
            total
            + (snapshot
               |> Array.find (fun (candidate, _) -> candidate = key)
               |> snd)

    total

let workload storage phase width =
    let array = Array.init width (fun index -> index, index + 1)
    let list = Array.toList array

    let map (key, value) =
        box key, box value

    let consume (key: obj, value: obj) =
        unbox<int> key + unbox<int> value

    match storage, phase with
    | "list", "adapt" -> fun () -> sink <- list |> List.map map |> List.sumBy consume
    | "array", "adapt" -> fun () -> sink <- array |> Array.map map |> Array.sumBy consume
    | "array-copy", "adapt" ->
        fun () ->
            sink <-
                array
                |> Array.copy
                |> Array.map map
                |> Array.sumBy consume
    | "list", "traverse" ->
        fun () ->
            sink <-
                list
                |> List.sumBy (fun (key, value) -> key + value)
    | "array", "traverse" ->
        fun () ->
            sink <-
                array
                |> Array.sumBy (fun (key, value) -> key + value)
    | "array-copy", "traverse" ->
        fun () ->
            sink <-
                array
                |> Array.copy
                |> Array.sumBy (fun (key, value) -> key + value)
    | "list", "lookup" ->
        fun () ->
            let mutable total = 0

            for key in width - 1 .. -1 .. 0 do
                total <-
                    total
                    + (list
                       |> List.find (fun (candidate, _) -> candidate = key)
                       |> snd)

            sink <- total
    | "array", "lookup" -> fun () -> sink <- arrayLookup width array
    | "array-copy", "lookup" -> fun () -> sink <- arrayLookup width (Array.copy array)
    | _ -> invalidArg "storage" "Unsupported snapshot benchmark"

let verify () =
    for width in [ 1; 16; 256; 1024 ] do
        for storage in [ "list"; "array"; "array-copy" ] do
            for phase in [ "adapt"; "traverse"; "lookup" ] do
                let run = workload storage phase width
                run ()

                let expected =
                    if phase = "lookup" then
                        width * (width + 1) / 2
                    else
                        width * width

                if sink <> expected then
                    failwith "Snapshot benchmark checksum mismatch"

let sample storage phase width iterations =
    let run = workload storage phase width

    for _ in 1..iterations do
        run ()

    sink
