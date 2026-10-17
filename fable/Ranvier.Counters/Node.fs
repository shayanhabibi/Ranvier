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

/// <summary>
/// A young-generation collection. Installed code stays in place; a full collection deoptimises code embedding freed
/// objects.
/// </summary>
[<Emit("globalThis.gc({ type: 'minor' })")>]
let collectYoung () : unit = jsNative

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

let private codeSpaces =
    set
        [
            "code_space"
            "code_large_object_space"
            "trusted_space"
            "trusted_large_object_space"
        ]

/// <summary>
/// Bytes in use in the heap spaces holding bytecode and machine code. The total grows whenever V8 compiles a
/// function, at any tier.
/// </summary>
let codeHeapUsed () =
    let mutable total = 0.0

    for space in heapSpaceStatistics () do
        if codeSpaces.Contains (spaceName space) then
            total <- total + spaceUsed space

    total

[<Import("setFlagsFromString", "node:v8")>]
let private setFlags (flags: string) : unit = jsNative

/// <summary>
/// Enables or disables tier-up to Sparkplug, Maglev and TurboFan. While disabled, every function keeps its
/// current tier or drops to the interpreter on deoptimisation.
/// </summary>
let setTierUp (enabled: bool) =
    let prefix = if enabled then "--" else "--no-"

    for compiler in [ "sparkplug"; "maglev"; "turbofan" ] do
        setFlags (prefix + compiler)

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
/// Writes <c>line</c> and a newline to the file descriptor <c>fd</c>, unbuffered.
/// </summary>
let writeLine (fd: int) (line: string) =
    writeSync (fd, line + "\n")

/// <summary>
/// Blocks until the file descriptor <c>fd</c> yields a newline or ends.
/// </summary>
let waitLine (fd: int) =
    let buffer = allocBuffer 1
    let mutable finished = false

    while not finished do
        let read =
            try
                readSync (fd, buffer, 0, 1, null)
            with error when errorCode error = "EAGAIN" ->
                -1

        if read = 0 || (read = 1 && firstByte buffer = 10) then
            finished <- true

[<Import("devNull", "node:os")>]
let private devNull: string = jsNative

[<Import("openSync", "node:fs")>]
let private openSync (path: string, flags: string) : int = jsNative

[<Import("closeSync", "node:fs")>]
let private closeSync (fd: int) : unit = jsNative

/// <summary>
/// Compiles <c>writeLine</c> and <c>waitLine</c> by exchanging one line with the null device.
/// </summary>
let compileLines () =
    let fd = openSync (devNull, "r+")

    try
        writeLine fd ""
        waitLine fd
    finally
        closeSync fd

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
