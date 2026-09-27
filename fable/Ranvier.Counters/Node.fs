/// <summary>
/// The Node.js primitives the harness measures and signals with.
/// </summary>
module FableCounters.Node

open Fable.Core

/// <summary>
/// A full collection. Needs <c>node --expose-gc</c>.
/// </summary>
[<Emit("globalThis.gc()")>]
let collect () : unit = jsNative

[<Emit("typeof globalThis.gc === 'function'")>]
let canCollect () : bool = jsNative

[<Emit("process.memoryUsage().heapUsed")>]
let heapUsed () : float = jsNative

[<Import("getHeapSpaceStatistics", "node:v8")>]
let private heapSpaceStatistics () : obj[] = jsNative

[<Emit("$0.space_name")>]
let private spaceName (space: obj) : string = jsNative

[<Emit("$0.space_used_size")>]
let private spaceUsed (space: obj) : float = jsNative

/// <summary>
/// The heap spaces holding JavaScript objects. <c>process.memoryUsage().heapUsed</c>
/// also counts the code and trusted spaces, which grow whenever V8 compiles or
/// optimises a function.
/// </summary>
let private objectSpaces =
    set [ "new_space"; "old_space"; "new_large_object_space"; "large_object_space" ]

/// <summary>
/// Bytes in use in the heap spaces holding JavaScript objects.
/// </summary>
let objectHeapUsed () =
    let mutable total = 0.0

    for space in heapSpaceStatistics () do
        if objectSpaces.Contains (spaceName space) then
            total <- total + spaceUsed space

    total

[<Import("GCProfiler", "node:v8")>]
let private gcProfilerType: obj = jsNative

[<Emit("new $0()")>]
let private construct (t: obj) : obj = jsNative

[<Emit("$0.start()")>]
let private startProfiler (profiler: obj) : unit = jsNative

[<Emit("$0.stop().statistics.length")>]
let private stopProfiler (profiler: obj) : int = jsNative

/// <summary>
/// Starts recording garbage collections. The returned function stops the
/// recording and returns the number of collections since the start.
/// </summary>
let recordCollections () : unit -> int =
    let profiler = construct gcProfilerType
    startProfiler profiler
    fun () -> stopProfiler profiler

[<Import("readSync", "node:fs")>]
let private readSync (fd: int, buffer: obj, offset: int, length: int, position: obj) : int = jsNative

[<Import("writeSync", "node:fs")>]
let private writeSync (fd: int, text: string) : unit = jsNative

[<Import("writeFileSync", "node:fs")>]
let writeFile (path: string, text: string) : unit = jsNative

[<Emit("Buffer.alloc($0)")>]
let private allocBuffer (size: int) : obj = jsNative

[<Emit("$0[0]")>]
let private firstByte (buffer: obj) : int = jsNative

[<Emit("$0.code")>]
let private errorCode (error: exn) : string = jsNative

/// <summary>
/// Writes <c>line</c> and a newline to standard output, unbuffered.
/// </summary>
let writeLine (line: string) =
    writeSync (1, line + "\n")

/// <summary>
/// Blocks until standard input yields a newline or ends.
/// </summary>
let waitLine () =
    let buffer = allocBuffer 1
    let mutable finished = false

    while not finished do
        let read =
            try
                readSync (0, buffer, 0, 1, null)
            with error when errorCode error = "EAGAIN" ->
                -1

        if read = 0 || (read = 1 && firstByte buffer = 10) then
            finished <- true

/// <summary>
/// Blocks the thread for <c>milliseconds</c>, which switches it out.
/// </summary>
[<Emit("Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, $0)")>]
let sleep (milliseconds: int) : unit = jsNative

[<Emit("process.pid")>]
let processId () : int = jsNative

[<Emit("process.version")>]
let version () : string = jsNative

[<Emit("JSON.stringify($0)")>]
let toJson (value: obj) : string = jsNative
