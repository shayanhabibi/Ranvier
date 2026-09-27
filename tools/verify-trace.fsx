// Gate 1 (zero Release cost), the Gate 2 sample dumps and the source lint of the graph provenance spec,
// docs/.ai/superpowers/specs/2026-09-27-graph-provenance-design.md section 7.
//
//   dotnet fsi tools/verify-trace.fsx              every check
//   dotnet fsi tools/verify-trace.fsx --lint       the source lint only
//   dotnet fsi tools/verify-trace.fsx --no-counters --no-fable
//   dotnet fsi tools/verify-trace.fsx --baseline   rewrite docs/.ai/public-api-baseline.txt from the merge-base
//
// The counter comparison runs counters.ps1 at the merge-base and at HEAD, and needs an elevated shell.
// Exit code 0 when every check that ran passed, 1 otherwise.

open System
open System.Diagnostics
open System.IO
open System.IO.Compression
open System.Reflection.Emit
open System.Reflection.Metadata
open System.Reflection.Metadata.Ecma335
open System.Reflection.PortableExecutable
open System.Text.Json
open System.Text.RegularExpressions

let args = fsi.CommandLineArgs |> Array.skip 1 |> Set.ofArray
let lintOnly = args.Contains "--lint"
let runCounters = not lintOnly && not (args.Contains "--no-counters")
let runFable = not lintOnly && not (args.Contains "--no-fable")

let root = Path.GetFullPath (Path.Combine (__SOURCE_DIRECTORY__, ".."))
let src = Path.Combine (root, "src", "Ranvier")
let spec = Path.Combine (root, "docs", ".ai", "superpowers", "specs", "2026-09-27-graph-provenance-design.md")
let baselineFile = Path.Combine (root, "docs", ".ai", "public-api-baseline.txt")

let work = Path.Combine (Path.GetTempPath (), $"partas-verify-trace-%d{Environment.ProcessId}")

// ---------------------------------------------------------------------------------------------------------------
// Reporting

let failures = ResizeArray<string> ()

let pass (name: string) (detail: string) = printfn $"PASS  %s{name}%s{detail}"

let fail (name: string) (lines: string seq) =
    let lines = List.ofSeq lines
    failures.Add name
    printfn $"FAIL  %s{name}"

    for l in lines |> List.truncate 40 do
        printfn $"        %s{l}"

    if lines.Length > 40 then
        printfn $"        ... %d{lines.Length - 40} more"

let check (name: string) (violations: string list) (detail: string) =
    if List.isEmpty violations then pass name detail else fail name violations

// ---------------------------------------------------------------------------------------------------------------
// Processes

/// <summary>Runs a process with <c>RanvierTrace</c> and <c>RanvierCounters</c> removed from its environment, plus <c>env</c>.</summary>
let run (dir: string) (env: (string * string) list) (file: string) (arguments: string list) : int * string =
    let info = ProcessStartInfo (file, arguments, WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true)
    info.Environment.Remove "RanvierTrace" |> ignore
    info.Environment.Remove "RanvierCounters" |> ignore

    for (k, v) in env do
        info.Environment[k] <- v

    use p = new Process (StartInfo = info)
    let output = Text.StringBuilder ()
    let append (e: DataReceivedEventArgs) = if not (isNull e.Data) then lock output (fun () -> output.AppendLine e.Data |> ignore)
    p.OutputDataReceived.Add append
    p.ErrorDataReceived.Add append
    p.Start () |> ignore
    p.BeginOutputReadLine ()
    p.BeginErrorReadLine ()
    p.WaitForExit ()
    p.ExitCode, string output

/// <summary>Runs a process, and raises with its output when it exits non-zero.</summary>
let runChecked dir env file arguments =
    let code, output = run dir env file arguments

    if code <> 0 then
        failwith $"%s{file} %s{String.Join (' ', List.toArray arguments)} exited with %d{code}:\n%s{output}"

    output

let git arguments = (runChecked root [] "git" arguments).Trim ()

// ---------------------------------------------------------------------------------------------------------------
// Metadata

let opcodes =
    typeof<OpCodes>.GetFields (Reflection.BindingFlags.Public ||| Reflection.BindingFlags.Static)
    |> Array.map (fun f -> f.GetValue null :?> OpCode)
    |> Array.map (fun o -> int (uint16 o.Value), o)
    |> dict

/// <summary>Signature types as text: namespace-qualified names, <c>!n</c>/<c>!!n</c> for generic parameters.</summary>
type SigText(md: MetadataReader) =
    let rec name (h: EntityHandle) =
        match h.Kind with
        | HandleKind.TypeDefinition ->
            let t = md.GetTypeDefinition (TypeDefinitionHandle.op_Explicit h)
            let d = t.GetDeclaringType ()

            let prefix =
                if d.IsNil then
                    let ns = md.GetString t.Namespace
                    if ns = "" then "" else ns + "."
                else
                    name (TypeDefinitionHandle.op_Implicit d) + "+"

            prefix + md.GetString t.Name
        | HandleKind.TypeReference ->
            let t = md.GetTypeReference (TypeReferenceHandle.op_Explicit h)

            let prefix =
                if t.ResolutionScope.Kind = HandleKind.TypeReference then
                    name (EntityHandle.op_Explicit t.ResolutionScope) + "+"
                else
                    let ns = md.GetString t.Namespace
                    if ns = "" then "" else ns + "."

            prefix + md.GetString t.Name
        | _ -> "?"

    member _.Name h = name h

    interface ISimpleTypeProvider<string> with
        member _.GetPrimitiveType(c: PrimitiveTypeCode) = string c
        member _.GetTypeFromDefinition(_, h, _) = name (TypeDefinitionHandle.op_Implicit h)
        member _.GetTypeFromReference(_, h, _) = name (TypeReferenceHandle.op_Implicit h)

    interface ISZArrayTypeProvider<string> with
        member _.GetSZArrayType e = e + "[]"

    interface IConstructedTypeProvider<string> with
        member _.GetArrayType(e, s) = e + "[" + String (',', s.Rank - 1) + "]"
        member _.GetByReferenceType e = e + "&"
        member _.GetGenericInstantiation(g, a) = g + "<" + String.Join (",", a) + ">"
        member _.GetPointerType e = e + "*"

    interface ISignatureTypeProvider<string, unit> with
        member this.GetTypeFromSpecification(r, c, h, _) = (r.GetTypeSpecification h).DecodeSignature (this, c)
        member _.GetGenericTypeParameter(_, i) = $"!%d{i}"
        member _.GetGenericMethodParameter(_, i) = $"!!%d{i}"
        member _.GetFunctionPointerType _ = "fnptr"
        member _.GetModifiedType(_, u, _) = u
        member _.GetPinnedType e = e

/// <summary>An open assembly with text renderings of its tokens.</summary>
type Assembly(dll: string) =
    let stream = File.OpenRead dll
    let pe = new PEReader (stream)
    let md = pe.GetMetadataReader ()
    let names = SigText md
    let sigs = names :> ISignatureTypeProvider<string, unit>

    member _.Metadata = md
    member _.PE = pe

    member _.TypeText(h: EntityHandle) =
        if h.Kind = HandleKind.TypeSpecification then
            (md.GetTypeSpecification (TypeSpecificationHandle.op_Explicit h)).DecodeSignature (sigs, ())
        else
            names.Name h

    member this.Token(h: EntityHandle) : string =
        match h.Kind with
        | HandleKind.MethodDefinition ->
            let m = md.GetMethodDefinition (MethodDefinitionHandle.op_Explicit h)
            names.Name (TypeDefinitionHandle.op_Implicit (m.GetDeclaringType ())) + "::" + md.GetString m.Name
        | HandleKind.MemberReference ->
            let m = md.GetMemberReference (MemberReferenceHandle.op_Explicit h)
            this.TypeText m.Parent + "::" + md.GetString m.Name
        | HandleKind.MethodSpecification ->
            let s = md.GetMethodSpecification (MethodSpecificationHandle.op_Explicit h)
            this.Token s.Method + "<" + String.Join (",", s.DecodeSignature (sigs, ())) + ">"
        | HandleKind.FieldDefinition ->
            let f = md.GetFieldDefinition (FieldDefinitionHandle.op_Explicit h)
            names.Name (TypeDefinitionHandle.op_Implicit (f.GetDeclaringType ())) + "::" + md.GetString f.Name
        | HandleKind.TypeSpecification
        | HandleKind.TypeDefinition
        | HandleKind.TypeReference -> this.TypeText h
        | kind -> string kind

    member _.Signatures = sigs

    /// <summary>Every instruction of every method body, as (method, opcode, operand text).</summary>
    member this.Instructions() =
        [
            for h in md.MethodDefinitions do
                let m = md.GetMethodDefinition h
                let caller = this.Token (MethodDefinitionHandle.op_Implicit h)

                if m.RelativeVirtualAddress <> 0 then
                    let mutable il = (pe.GetMethodBody m.RelativeVirtualAddress).GetILReader ()

                    while il.RemainingBytes > 0 do
                        let first = int (il.ReadByte ())
                        let code = if first = 0xFE then 0xFE00 ||| int (il.ReadByte ()) else first
                        let o = opcodes[code]

                        let operand =
                            match o.OperandType with
                            | OperandType.InlineMethod
                            | OperandType.InlineField
                            | OperandType.InlineType
                            | OperandType.InlineTok -> this.Token (MetadataTokens.EntityHandle (il.ReadInt32 ()))
                            | OperandType.InlineString ->
                                "\"" + md.GetUserString (MetadataTokens.UserStringHandle (il.ReadInt32 () &&& 0xFFFFFF)) + "\""
                            | OperandType.InlineNone -> ""
                            | OperandType.ShortInlineBrTarget
                            | OperandType.ShortInlineI
                            | OperandType.ShortInlineVar -> string (il.ReadSByte ())
                            | OperandType.InlineVar -> string (il.ReadInt16 ())
                            | OperandType.InlineI8 -> string (il.ReadInt64 ())
                            | OperandType.InlineR -> string (il.ReadDouble ())
                            | OperandType.InlineSwitch -> String.Join (",", Array.init (il.ReadInt32 ()) (fun _ -> il.ReadInt32 ()))
                            | _ -> string (il.ReadInt32 ())

                        yield caller, o, operand
        ]

    /// <summary>The <c>AssemblyMetadata</c> key/value pairs.</summary>
    member _.AssemblyMetadata() =
        [
            for h in md.GetAssemblyDefinition().GetCustomAttributes () do
                let a = md.GetCustomAttribute h

                let ctorType =
                    match a.Constructor.Kind with
                    | HandleKind.MemberReference -> names.Name (md.GetMemberReference (MemberReferenceHandle.op_Explicit a.Constructor)).Parent
                    | _ -> ""

                if ctorType = "System.Reflection.AssemblyMetadataAttribute" then
                    let mutable blob = md.GetBlobReader a.Value
                    blob.ReadUInt16 () |> ignore
                    let key = blob.ReadSerializedString ()
                    let value = blob.ReadSerializedString ()
                    yield key, value
        ]

    interface IDisposable with
        member _.Dispose() =
            pe.Dispose ()
            stream.Dispose ()

// ---------------------------------------------------------------------------------------------------------------
// Gate 1: IL scan

let tracedTypes = set [ "Tracer"; "TraceLog"; "TraceEvent"; "TraceEventKind"; "TraceNodeKind"; "TraceModel"; "ITraced" ]

let private simpleName (qualified: string) =
    let t = qualified.Split("::").[0].Split('<').[0]
    t.Substring (t.LastIndexOfAny [| '.'; '+' |] + 1)

/// <summary>
/// Every traced type defined or referenced by <c>dll</c>, and every <c>call</c>, <c>callvirt</c>, <c>newobj</c>,
/// <c>ldftn</c> or <c>ldvirtftn</c> from outside a traced type to a traced type's member.
/// </summary>
let ilScan (dll: string) : string list =
    use a = new Assembly (dll)
    let md = a.Metadata

    [
        for h in md.TypeDefinitions do
            let n = md.GetString (md.GetTypeDefinition h).Name
            if tracedTypes.Contains n then yield $"type %s{n}"
        for h in md.TypeReferences do
            let n = md.GetString (md.GetTypeReference h).Name
            if tracedTypes.Contains n then yield $"typeref %s{n}"
        for (caller, o, operand) in a.Instructions () do
            if
                (o = OpCodes.Call || o = OpCodes.Callvirt || o = OpCodes.Newobj || o = OpCodes.Ldftn || o = OpCodes.Ldvirtftn)
                && tracedTypes.Contains (simpleName operand)
                && not (tracedTypes.Contains (simpleName caller))
            then
                yield $"%s{caller} %s{o.Name} %s{operand}"
    ]

/// <summary>The untraced residue section 2 permits: the stub <c>Tracer</c> type.</summary>
let permittedResidue = set [ "type Tracer" ]

// ---------------------------------------------------------------------------------------------------------------
// Gate 1: public surface

/// <summary>Public and protected types, members and fields of <c>dll</c>, one sorted line each.</summary>
let publicSurface (dll: string) : string list =
    use a = new Assembly (dll)
    let md = a.Metadata

    let rec visible (t: TypeDefinition) =
        match t.Attributes &&& Reflection.TypeAttributes.VisibilityMask with
        | Reflection.TypeAttributes.Public -> true
        | Reflection.TypeAttributes.NestedPublic
        | Reflection.TypeAttributes.NestedFamily
        | Reflection.TypeAttributes.NestedFamORAssem -> visible (md.GetTypeDefinition (t.GetDeclaringType ()))
        | _ -> false

    let memberVisible (m: Reflection.MethodAttributes) =
        match m &&& Reflection.MethodAttributes.MemberAccessMask with
        | Reflection.MethodAttributes.Public
        | Reflection.MethodAttributes.Family
        | Reflection.MethodAttributes.FamORAssem -> true
        | _ -> false

    [
        for h in md.TypeDefinitions do
            let t = md.GetTypeDefinition h

            if visible t then
                let tn = a.TypeText (TypeDefinitionHandle.op_Implicit h)
                let baseName = if t.BaseType.IsNil then "" else a.TypeText t.BaseType
                yield $"type %s{tn} : %s{baseName}"

                for ih in t.GetInterfaceImplementations () do
                    yield $"type %s{tn} implements %s{a.TypeText (md.GetInterfaceImplementation ih).Interface}"

                for mh in t.GetMethods () do
                    let m = md.GetMethodDefinition mh

                    if memberVisible m.Attributes then
                        let s = m.DecodeSignature (a.Signatures, ())
                        let isStatic = m.Attributes.HasFlag Reflection.MethodAttributes.Static
                        let st = if isStatic then "static " else ""
                        let gen = if s.GenericParameterCount > 0 then $"<%d{s.GenericParameterCount}>" else ""
                        let ps = String.Join (',', s.ParameterTypes)
                        yield $"member %s{tn} %s{st}%s{md.GetString m.Name}%s{gen}(%s{ps}) : %s{s.ReturnType}"

                for fh in t.GetFields () do
                    let f = md.GetFieldDefinition fh

                    match f.Attributes &&& Reflection.FieldAttributes.FieldAccessMask with
                    | Reflection.FieldAttributes.Public
                    | Reflection.FieldAttributes.Family ->
                        yield $"field %s{tn} %s{md.GetString f.Name} : %s{f.DecodeSignature (a.Signatures, ())}"
                    | _ -> ()
    ]
    |> List.sort

/// <summary>Surface lines the untraced build may add to the baseline: <c>Trace.named</c> (section 5.1).</summary>
let permittedAdditions =
    set
        [
            "type Ranvier.Trace : System.Object"
            "member Ranvier.Trace static named<1>(String,Microsoft.FSharp.Core.FSharpFunc`2<Microsoft.FSharp.Core.Unit,!!0>) : !!0"
        ]

// ---------------------------------------------------------------------------------------------------------------
// Gate 1: IL equality of the named sample

let private closureLine = Regex @"@\d+(-\d+)?"

/// <summary>Every method and instruction of <c>dll</c>, with closure line numbers removed.</summary>
let ilListing (dll: string) : string list =
    use a = new Assembly (dll)

    [
        for (caller, o, operand) in a.Instructions () do
            yield $"%s{caller}  %s{o.Name} %s{operand}"
    ]
    |> List.map (fun l -> closureLine.Replace (l, "@"))

// ---------------------------------------------------------------------------------------------------------------
// Lint

let stripComment (line: string) =
    let i = line.IndexOf "//"
    if i >= 0 then line.Substring (0, i) else line

let hookShape = Regex @"^\s*(do\s+)?Tracer\.[A-Z]\w*\s*(\(.*\)|[a-z_]\w*(\s+[a-z_]\w*)*)\s*$"

let forbiddenInArgs =
    [
        @"\.Value\b"
        @"\bTryValue\b"
        @"\bEnsureCurrent\b"
        @"\bUpdateIfNecessary\b"
        @"\bTrack\b"
        @"\bNextId\b"
        @"\bToString\b"
        @"\bEquals\b"
        @"%A"
        @"\bsprintf\b"
        @"\bStackTrace\b"
        @"\bError\b"
        @"\bnew\s"
        @"\b(Signal|AsyncSource|Memo|Effect|AsyncMemo|Boundary|Owner|Projection|Lookup)\s*(<[^>]*>)?\s*\("
    ]
    |> List.map Regex

let engineFiles = [ "Core.fs"; "Projections.fs"; "Combinators.fs"; "Api.fs" ]

let declarationStart =
    Regex @"^(let|val|member|abstract|default|override|interface|static member|static val|new|\[<)\b"

let typeHeader =
    Regex @"^\s*(type|and)\s+(\[<[^>]*>\]\s*)?(internal\s+|private\s+)?(\[<[^>]*>\]\s*)?(\w+)"

/// <summary>The (file, type) rows of the spec's Appendix A.</summary>
let appendixA () =
    let text = File.ReadAllText spec
    let start = text.IndexOf "## Appendix A"

    text.Substring(start).Split ('\n')
    |> Array.choose (fun l ->
        let m = Regex.Match (l, @"^\|\s*`([^`]+\.fs)`\s*\|\s*`([^`]+)`\s*\|")
        if m.Success then Some (m.Groups[1].Value, m.Groups[2].Value) else None)
    |> set

/// <summary>Hook-site violations in the engine files, and the (file, type) of each <c>#if RANVIER_TRACE</c> block.</summary>
let lintEngine () : string list * Set<string * string> =
    let violations = ResizeArray ()
    let blocks = ResizeArray ()

    for file in engineFiles do
        let lines = File.ReadAllLines (Path.Combine (src, file))
        let mutable enclosing = "<none>"
        let mutable i = 0

        while i < lines.Length do
            let line = lines[i]
            let header = typeHeader.Match line

            if header.Success && not (line.TrimStart().StartsWith "//") then
                enclosing <- header.Groups[5].Value

            if line.Trim () = "#if RANVIER_TRACE" then
                blocks.Add (file, enclosing)
                let body = ResizeArray ()
                i <- i + 1

                while i < lines.Length && not (lines[i].Trim().StartsWith "#e") do
                    body.Add (i + 1, lines[i])
                    i <- i + 1

                if i < lines.Length && lines[i].Trim () = "#else" then
                    violations.Add $"%s{file}:%d{i + 1}: a #if RANVIER_TRACE block in an engine file has an #else"

                let code = body |> Seq.filter (fun (_, l) -> (stripComment l).Trim () <> "") |> Seq.toList

                match code with
                | [] -> violations.Add $"%s{file}: an empty #if RANVIER_TRACE block in %s{enclosing}"
                | _ ->
                    let indent = code |> List.map (fun (_, l) -> l.Length - l.TrimStart().Length) |> List.min

                    for (n, l) in code do
                        if l.Length - l.TrimStart().Length = indent && not (declarationStart.IsMatch (l.Trim ())) then
                            violations.Add $"%s{file}:%d{n}: a #if RANVIER_TRACE block holds a statement: %s{l.Trim ()}"
            else
                let code = stripComment line

                if code.Contains "Tracer" then
                    if not (hookShape.IsMatch code) then
                        violations.Add $"%s{file}:%d{i + 1}: Tracer outside a single-statement hook: %s{code.Trim ()}"
                    else
                        let hook = code.Substring (code.IndexOf "Tracer.")

                        for r in forbiddenInArgs do
                            if r.IsMatch hook then
                                violations.Add $"%s{file}:%d{i + 1}: a forbidden hook argument (%O{r}): %s{code.Trim ()}"

            i <- i + 1

    List.ofSeq violations, set blocks

/// <summary>
/// Violations in <c>Trace.fs</c> and <c>TraceEvents.fs</c>: tables keyed by engine objects, formatting, forbidden
/// calls, and a <c>Tracer</c> member outside <c>#if RANVIER_TRACE</c> that is not a <c>Conditional</c> hook with an
/// empty untraced body.
/// </summary>
let lintTraceFiles () : string list =
    let keyed =
        Regex @"(Dictionary|HashSet|ConditionalWeakTable)\s*<\s*(INode|ISource|IComputation|IScheduled|Owner|Graph|ITraced|obj)\b"

    let formatting = Regex @"\bsprintf\b|\$""|String\.Format|%A|\.ToString\s*\("
    let forbidden = forbiddenInArgs |> List.filter (fun r -> string r <> @"\bnew\s")
    let runStatus = Regex @"\bRunStatus\.Error\b|^\s*\|\s*Error\s*=\s*\d+\s*$"

    [
        for file in [ "Trace.fs"; "TraceEvents.fs" ] do
            let lines = File.ReadAllLines (Path.Combine (src, file))
            let mutable depth = 0
            let mutable conditional = false

            for n in 0 .. lines.Length - 1 do
                let code = stripComment lines[n]
                let trimmed = code.Trim ()

                if trimmed.StartsWith "#if" then depth <- depth + 1
                elif trimmed = "#endif" then depth <- depth - 1

                if keyed.IsMatch code then
                    yield $"%s{file}:%d{n + 1}: a table keyed by engine objects: %s{trimmed}"

                if formatting.IsMatch code then
                    yield $"%s{file}:%d{n + 1}: formatting outside TraceModel and TraceApi: %s{trimmed}"

                // A `RunStatus` case names a run status, not a node's `Error` member.
                let body = runStatus.Replace (code, "")

                for r in forbidden do
                    if r.IsMatch body then
                        yield $"%s{file}:%d{n + 1}: forbidden in a Tracer body (%O{r}): %s{trimmed}"

                if file = "Trace.fs" then
                    if trimmed.StartsWith "[<Conditional(\"RANVIER_TRACE\")>]" then
                        conditional <- true
                    elif trimmed.StartsWith "static member" then
                        if depth = 0 && not conditional then
                            yield $"%s{file}:%d{n + 1}: an untraced Tracer member that is not a Conditional hook: %s{trimmed}"

                        if conditional then
                            let next = if n + 1 < lines.Length then lines[n + 1].Trim () else ""

                            if next <> "#if RANVIER_TRACE" then
                                yield $"%s{file}:%d{n + 1}: a hook body must open with #if RANVIER_TRACE: %s{trimmed}"

                        conditional <- false
    ]

// ---------------------------------------------------------------------------------------------------------------
// Fable

let traceImport = Regex @"from\s+""[^""]*/Trace\.fs\.js"""

/// <summary>JS modules other than <c>Trace.fs.js</c> that import <c>Trace.fs.js</c> or name a <c>Tracer_</c> member.</summary>
let fableScan (outDir: string) : string list =
    [
        for file in Directory.EnumerateFiles (outDir, "*.js", SearchOption.AllDirectories) do
            let name = Path.GetFileName file

            if name <> "Trace.fs.js" && not (file.Contains "fable_modules") then
                let text = File.ReadAllText file

                if traceImport.IsMatch text || text.Contains "Tracer_" then
                    yield Path.GetRelativePath (outDir, file)
    ]

let fable (project: string) (outDir: string) (env: (string * string) list) (extra: string list) =
    runChecked root env "dotnet" ([ "fable"; project; "-e"; ".fs.js"; "-o"; outDir; "--noCache"; "--configuration"; "Release" ] @ extra)
    |> ignore

// ---------------------------------------------------------------------------------------------------------------
// Counters

type CounterRow =
    {
        Key: string
        Bytes: float
        Objects: float
        Counters: Map<string, float>
        Instructions: float option
        Spread: float
    }

/// <summary>The Ranvier rows of a counters.ps1 JSON report, keyed "target/scenario".</summary>
let counterRows (json: string) : Map<string, CounterRow> * string =
    use doc = JsonDocument.Parse (File.ReadAllText json)
    let r = doc.RootElement
    let header = r.GetProperty "Header"

    let ranvierTrace =
        match header.TryGetProperty "RanvierTrace" with
        | true, v -> v.GetString ()
        | _ -> "absent"

    let counters (e: JsonElement) =
        [ for c in e.EnumerateArray () -> c.GetProperty("Name").GetString (), c.GetProperty("Value").GetDouble () ]
        |> Map.ofList

    let spreads =
        [
            for s in r.GetProperty("Calibration").GetProperty("Spreads").EnumerateArray () do
                if s.GetProperty("Engine").GetString () = "Ranvier" && s.GetProperty("Source").GetString () = "InstructionRetired" then
                    let median = s.GetProperty("Median").GetDouble ()
                    let spread = (s.GetProperty("Max").GetDouble () - s.GetProperty("Min").GetDouble ()) / max median 1.0
                    yield s.GetProperty("Scenario").GetString (), spread
        ]
        |> Map.ofList

    let rows =
        [
            for row in r.GetProperty("Rows").EnumerateArray () do
                if row.GetProperty("Engine").GetString () = "Ranvier" then
                    let scenario = row.GetProperty("Scenario").GetString ()

                    let instructions =
                        [ for p in row.GetProperty("Pmc").EnumerateArray () -> p.GetProperty("Name").GetString (), p.GetProperty("Value").GetDouble () ]
                        |> List.tryFind (fun (n, _) -> n = "InstructionRetired")
                        |> Option.map snd

                    yield
                        {
                            Key = "net/" + scenario
                            Bytes = row.GetProperty("BytesPerOp").GetDouble ()
                            Objects = row.GetProperty("ObjectsPerOp").GetDouble ()
                            Counters = counters (row.GetProperty "Counters")
                            Instructions = instructions
                            Spread = spreads |> Map.tryFind scenario |> Option.defaultValue 0.0
                        }

            match r.TryGetProperty "Fable" with
            | true, f when f.ValueKind = JsonValueKind.Object ->
                for row in f.GetProperty("Rows").EnumerateArray () do
                    if row.GetProperty("Engine").GetString () = "Ranvier" then
                        let objects =
                            match row.GetProperty "ObjectsPerOp" with
                            | o when o.ValueKind = JsonValueKind.Number -> o.GetDouble ()
                            | _ -> nan

                        yield
                            {
                                Key = "fable/" + row.GetProperty("Scenario").GetString ()
                                Bytes = nan
                                Objects = objects
                                Counters = counters (row.GetProperty "Counters")
                                Instructions = None
                                Spread = 0.0
                            }
            | _ -> ()
        ]

    (rows |> List.map (fun x -> x.Key, x) |> Map.ofList), ranvierTrace

/// <summary>
/// Differences between a base and a head report: bytes/op, objects/op and library counters exactly;
/// <c>InstructionRetired</c> within max(0.5%, the scenario's calibration spread).
/// </summary>
let compareCounters (baseRows: Map<string, CounterRow>) (headRows: Map<string, CounterRow>) : string list =
    let same (a: float) (b: float) = (Double.IsNaN a && Double.IsNaN b) || a = b

    [
        for KeyValue (key, h) in headRows do
            match baseRows.TryFind key with
            | None -> ()
            | Some b ->
                if not (same b.Bytes h.Bytes) then
                    yield $"%s{key}: bytes/op %g{b.Bytes} -> %g{h.Bytes}"

                if not (same b.Objects h.Objects) then
                    yield $"%s{key}: objects/op %g{b.Objects} -> %g{h.Objects}"

                if b.Counters <> h.Counters then
                    yield $"%s{key}: library counters %A{b.Counters} -> %A{h.Counters}"

                match b.Instructions, h.Instructions with
                | Some bi, Some hi ->
                    let tolerance = max 0.005 (max b.Spread h.Spread)
                    let delta = abs (hi - bi) / max bi 1.0

                    if delta > tolerance then
                        yield $"%s{key}: instr/op %.1f{bi} -> %.1f{hi} (%.2f{100.0 * delta}%% > %.2f{100.0 * tolerance}%%)"
                | _ -> ()
    ]

let isElevated () =
    OperatingSystem.IsWindows ()
    && (let identity = Security.Principal.WindowsIdentity.GetCurrent ()
        Security.Principal.WindowsPrincipal(identity).IsInRole Security.Principal.WindowsBuiltInRole.Administrator)

// ---------------------------------------------------------------------------------------------------------------
// Checks

let lint () =
    let violations, blocks = lintEngine ()
    let appendix = appendixA ()

    let unlisted =
        blocks - appendix
        |> Seq.map (fun (f, t) -> $"%s{f}: a #if RANVIER_TRACE block in %s{t} is not listed in spec Appendix A")
        |> List.ofSeq

    let pending = appendix - blocks |> Seq.map (fun (f, t) -> $"%s{f}/%s{t}") |> List.ofSeq

    let detail =
        if pending.IsEmpty then
            ""
        else
            $" (Appendix A rows without a block yet: %s{String.Join (',', pending)})"

    check "lint: hook sites and #if RANVIER_TRACE blocks" (violations @ unlisted) detail
    check "lint: Trace.fs and TraceEvents.fs" (lintTraceFiles ()) ""

let buildLibrary (traced: bool) =
    let out = Path.Combine (work, (if traced then "traced" else "untraced"))

    runChecked root [] "dotnet" [ "build"; "src/Ranvier"; "-c"; "Release"; $"-p:RanvierTrace=%b{traced}"; "-o"; out; "-warnaserror" ]
    |> ignore

    Path.Combine (out, "Ranvier.dll")

let ilGate () =
    let untraced = buildLibrary false
    let traced = buildLibrary true
    let residue = ilScan untraced |> List.filter (fun f -> not (permittedResidue.Contains f))
    check "gate 1: untraced IL references no trace type or hook" residue ""

    let hooks = ilScan traced |> List.filter (fun f -> f.Contains "::")

    if hooks.IsEmpty then
        fail "gate 1: positive control, traced IL calls hooks" [ "the traced build reports no hook call" ]
    else
        pass "gate 1: positive control, traced IL calls hooks" $" (%d{hooks.Length} call sites)"

    let metadata (dll: string) =
        use a = new Assembly (dll)
        a.AssemblyMetadata ()

    check
        "gate 1: assembly metadata matches the build"
        [
            if metadata untraced |> List.exists (fun (k, _) -> k = "RanvierTrace") then
                yield "the untraced build carries AssemblyMetadata(RanvierTrace)"
            if not (metadata traced |> List.contains ("RanvierTrace", "true")) then
                yield "the traced build lacks AssemblyMetadata(RanvierTrace, true)"
        ]
        ""

let packGate () =
    let out = Path.Combine (work, "pack")

    runChecked root [] "dotnet" [ "pack"; "src/Ranvier"; "-c"; "Release"; "-p:RanvierTrace=false"; "-o"; out ]
    |> ignore

    let nupkg = Directory.GetFiles (out, "Ranvier.*.nupkg") |> Array.exactlyOne
    let dll = Path.Combine (out, "packed", "Ranvier.dll")
    Directory.CreateDirectory (Path.GetDirectoryName dll) |> ignore

    do
        use zip = ZipFile.OpenRead nupkg

        let entry =
            zip.Entries |> Seq.find (fun e -> e.FullName.EndsWith "/Ranvier.dll" && e.FullName.StartsWith "lib/")

        entry.ExtractToFile (dll, true)

    let metadata =
        use a = new Assembly (dll)
        a.AssemblyMetadata ()

    let residue = ilScan dll |> List.filter (fun f -> not (permittedResidue.Contains f))

    check
        "gate 1: packed DLL is untraced"
        [
            if metadata |> List.exists (fun (k, _) -> k = "RanvierTrace") then
                yield "the packed DLL carries AssemblyMetadata(RanvierTrace)"
            yield! residue
        ]
        ""

    let surface = publicSurface dll |> set
    let baseline = File.ReadAllLines baselineFile |> Array.filter (fun l -> l <> "") |> set
    let added = surface - baseline - permittedAdditions |> Seq.map (fun l -> "added:   " + l)
    let removed = baseline - surface |> Seq.map (fun l -> "removed: " + l)
    check "gate 1: packed public surface equals the baseline plus Trace.named" (List.ofSeq (Seq.append added removed)) ""

    let (debugCode, _) =
        run root [] "dotnet" [ "pack"; "src/Ranvier"; "-c"; "Debug"; "-o"; Path.Combine (work, "pack-debug") ]

    check "gate 1: a traced pack fails" [ if debugCode = 0 then yield "dotnet pack -c Debug succeeded" ] ""

let namedProject =
    """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RanvierTrace>false</RanvierTrace>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
  <PropertyGroup Condition="'$(NamedSample)' == 'true'">
    <DefineConstants>$(DefineConstants);NAMED</DefineConstants>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="NamedZeroCost.fs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="%SRC%" />
  </ItemGroup>
</Project>
"""

let namedGate () =
    let dir = Path.Combine (work, "named")
    Directory.CreateDirectory dir |> ignore
    let project = Path.Combine (dir, "NamedZeroCost.fsproj")
    File.WriteAllText (project, namedProject.Replace ("%SRC%", Path.Combine (src, "Ranvier.fsproj")))
    File.Copy (Path.Combine (root, "samples", "named-zero-cost.fsx"), Path.Combine (dir, "NamedZeroCost.fs"), true)

    let build (named: bool) =
        let out = Path.Combine (work, (if named then "named-with" else "named-without"))

        runChecked dir [] "dotnet" [ "build"; project; "-c"; "Release"; "-p:RanvierTrace=false"; $"-p:NamedSample=%b{named}"; "-o"; out ]
        |> ignore

        ilListing (Path.Combine (out, "NamedZeroCost.dll"))

    let without = build false
    let withNamed = build true

    let diff =
        if without = withNamed then
            []
        else
            let a = set without
            let b = set withNamed
            [ for l in a - b -> "without only: " + l ] @ [ for l in b - a -> "named only:   " + l ]

    check "gate 1: Trace.named sample IL equals the sample without it" diff ""

    if runFable then
        let js (named: bool) =
            let out = Path.Combine (work, (if named then "named-js-with" else "named-js-without"))
            fable project out [] (if named then [ "--define"; "NAMED" ] else [])
            File.ReadAllText (Path.Combine (out, "NamedZeroCost.fs.js"))

        let a = js false
        let b = js true

        check
            "gate 1: Trace.named sample JS equals the sample without it"
            [
                if a <> b then
                    yield "NamedZeroCost.fs.js differs:"
                    yield! (b.Split '\n' |> Array.filter (fun l -> not (a.Contains l)) |> Array.map (fun l -> "named only: " + l.TrimEnd ()))
            ]
            ""

let fableGate () =
    let untraced = Path.Combine (work, "fable-untraced")
    let traced = Path.Combine (work, "fable-traced")
    fable "fable/Ranvier.Fable" untraced [] []
    fable "fable/Ranvier.Fable" traced [ ("RanvierTrace", "true") ] []
    check "gate 1: untraced Fable JS imports nothing from Trace.fs.js" (fableScan untraced) ""

    match fableScan traced with
    | [] -> fail "gate 1: positive control, traced Fable JS imports Trace.fs.js" [ "no traced module imports Trace.fs.js" ]
    | modules -> pass "gate 1: positive control, traced Fable JS imports Trace.fs.js" $" (%s{String.Join (',', modules)})"

/// <summary>
/// Runs <c>samples/trace-sample.fsx</c> 5 times in <c>jsonl</c> mode against the traced Release build, and passes when
/// every dump is byte-identical to the first.
/// </summary>
let sampleGate () =
    let name = "gate 2: trace-sample.fsx dumps are byte-identical over 5 runs"
    let traced = buildLibrary true
    let dir = Path.Combine (work, "sample")
    Directory.CreateDirectory dir |> ignore
    let copy = Path.Combine (dir, "trace-sample.fsx")
    let source = File.ReadAllText (Path.Combine (root, "samples", "trace-sample.fsx"))
    let reference = "#r \"../src/Ranvier/bin/Release/net10.0/Ranvier.dll\""

    if not (source.Contains reference) then
        failwith $"samples/trace-sample.fsx no longer holds the line %s{reference}"

    File.WriteAllText (copy, source.Replace (reference, $"#r @\"%s{traced}\""))
    let dumps = [ for _ in 1..5 -> runChecked dir [] "dotnet" [ "fsi"; copy; "jsonl" ] ]

    check
        name
        [
            for i, d in List.indexed dumps do
                if d <> dumps.Head then
                    yield $"run %d{i + 1} differs from run 1"
            if not (dumps.Head.StartsWith "{\"schema\":1") then
                yield "the dump does not start with a schema 1 header"
        ]
        $" (%d{dumps.Head.Split('\n').Length - 1} lines)"

let countersGate () =
    if not (isElevated ()) then
        fail "gate 1: counters, merge-base vs HEAD" [ "processor counters need an elevated shell; run again as administrator or pass --no-counters" ]
    else
        let mergeBase = git [ "merge-base"; "HEAD"; "main" ]
        let baseTree = Path.Combine (work, "base")
        git [ "worktree"; "add"; "--detach"; baseTree; mergeBase ] |> ignore

        try
            let report (tree: string) (out: string) =
                runChecked tree [] "pwsh" [ "-NoProfile"; "-File"; Path.Combine (tree, "counters.ps1"); "-Out"; out ]
                |> ignore

                Directory.GetFiles (out, "*.json") |> Array.exactlyOne |> counterRows

            let baseRows, baseTrace = report baseTree (Path.Combine (work, "counters-base"))
            let headRows, headTrace = report root (Path.Combine (work, "counters-head"))

            check
                $"gate 1: counters, merge-base %s{mergeBase.Substring (0, 7)} vs HEAD"
                [
                    if headTrace <> "false" then
                        yield $"the HEAD report header has RanvierTrace=%s{headTrace}"
                    if baseTrace <> "false" && baseTrace <> "absent" then
                        yield $"the merge-base report header has RanvierTrace=%s{baseTrace}"
                    yield! compareCounters baseRows headRows
                ]
                $" (%d{headRows.Count} Ranvier rows; merge-base header RanvierTrace=%s{baseTrace})"
        finally
            run root [] "git" [ "worktree"; "remove"; "--force"; baseTree ] |> ignore

// ---------------------------------------------------------------------------------------------------------------

let step (name: string) (f: unit -> unit) =
    try
        f ()
    with e ->
        fail name [ yield! e.Message.Split '\n' |> Array.map (fun l -> l.TrimEnd ()) ]

Directory.CreateDirectory work |> ignore

if args.Contains "--baseline" then
    let mergeBase = git [ "merge-base"; "HEAD"; "main" ]
    let baseTree = Path.Combine (work, "base")
    git [ "worktree"; "add"; "--detach"; baseTree; mergeBase ] |> ignore
    let out = Path.Combine (work, "baseline")

    try
        runChecked baseTree [] "dotnet" [ "build"; "src/Ranvier"; "-c"; "Release"; "-o"; out ] |> ignore
    finally
        run root [] "git" [ "worktree"; "remove"; "--force"; baseTree ] |> ignore

    let surface = publicSurface (Path.Combine (out, "Ranvier.dll"))
    File.WriteAllText (baselineFile, String.Join ("\r\n", surface) + "\r\n")
    printfn $"verify-trace: wrote %d{surface.Length} lines to %s{baselineFile}"
    exit 0

printfn $"verify-trace: %s{root}"

step "lint" lint

if not lintOnly then
    step "gate 1: IL scan" ilGate
    step "gate 1: pack" packGate
    step "gate 1: Trace.named sample" namedGate
    step "gate 2: trace-sample.fsx" sampleGate

    if runFable then
        step "gate 1: Fable scan" fableGate

    if runCounters then
        step "gate 1: counters" countersGate

if failures.Count = 0 then
    printfn "verify-trace: every check passed"
    try Directory.Delete (work, true) with _ -> ()
    exit 0
else
    printfn $"verify-trace: %d{failures.Count} check(s) failed; build outputs kept in %s{work}"
    exit 1
