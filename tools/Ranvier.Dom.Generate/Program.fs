module Ranvier.Dom.Generate

open System
open System.IO
open System.IO.Compression
open System.Reflection
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Fable.Core.TS
open Xantham.TypeScript.Wire
open Xantham.TypeScript.Wire.Proto

let fail message =
    invalidOp message

let hashBytes (bytes: byte array) =
    SHA256.HashData (bytes)
    |> Convert.ToHexStringLower

let hashFile path =
    File.ReadAllBytes path |> hashBytes

let str (name: string) (value: JsonElement) =
    value.GetProperty(name).GetString()

let require condition message =
    if not condition then
        fail message

let compilerVersion = "7.1.0-dev.20260902.1"
let compilerRevision = "43a90f4c105bc9db7cb7aa299beddafbabe1d23e"

let catalogueHash =
    "ad2722747e79381c6ec729db17d96c9eb1046d68627099cb63986942563687c8"

let assemblyHash =
    "663021de210d5f4a4ae4b9edbbd71d850343edf1243a16bb322634051ffe0450"

let descriptorHash =
    "40d670b86bdb994b6108ae271cee1441215f04893b6d820e5d39075895f73942"

type Declaration =
    {
        Name: string
        Arity: int
        Handles: string array
    }

let loadCatalogue root =
    let descriptorPath = Path.Combine (root, "xantham", "catalogue.json")
    require (hashFile descriptorPath = descriptorHash) "Descriptor hash mismatch"
    use descriptor = JsonDocument.Parse (File.ReadAllBytes descriptorPath)
    let d = descriptor.RootElement
    require (d.GetProperty("schemaVersion").GetInt32() = 1) "Unsupported descriptor schema"

    require
        (str "packageId" d = "Xantham.Fable.Core.TS"
         && str "packageVersion" d = "0.2.1")
        "Unexpected binding package"

    require (str "catalogue" d = "xantham/declarations.json.br") "Unexpected catalogue path"

    require
        (str "catalogueSha256" d = catalogueHash
         && str "assemblySha256" d = assemblyHash)
        "Descriptor disagrees with release pins"

    let payload = Path.Combine (root, "xantham", "declarations.json.br")
    require (hashFile payload = catalogueHash) "Catalogue hash mismatch"
    require (hashFile typeof<Dom.HTMLInputElement>.Assembly.Location = assemblyHash) "Native binding assembly hash mismatch"
    use input = File.OpenRead payload
    use decoded = new BrotliStream (input, CompressionMode.Decompress)
    use bounded = new MemoryStream ()
    let buffer = Array.zeroCreate<byte> 65536
    let mutable count = decoded.Read (buffer)

    while count > 0 do
        require
            (bounded.Length + int64 count
             <= 128L * 1024L * 1024L)
            "Catalogue exceeds decoded size limit"

        bounded.Write (buffer, 0, count)
        count <- decoded.Read (buffer)

    use document = JsonDocument.Parse (bounded.ToArray ())
    let catalog = document.RootElement
    require (catalog.GetProperty("schemaVersion").GetInt32() = 2) "Unsupported declaration schema"
    let compatibility = catalog.GetProperty ("compatibility")

    for name, expected in
        [
            "contractVersion", 1
            "identityVersion", 5
            "apiVersion", 4
            "inferenceVersion", 2
            "customizationVersion", 1
        ] do
        require (compatibility.GetProperty(name).GetInt32() = expected) $"Unsupported compatibility field {name}"

    let compiler = compatibility.GetProperty ("compiler")

    require
        (str "kind" compiler = "typescript-package"
         && str "version" compiler = compilerVersion
         && str "revision" compiler = compilerRevision
         && compiler.GetProperty("astProtocolVersion").GetInt32() = 8)
        "Unsupported compiler contract"

    require (str "owner" d = str "owner" catalog) "Descriptor owner mismatch"

    let declarations =
        catalog.GetProperty("declarations").EnumerateArray()
        |> Seq.filter (fun declaration -> str "role" declaration = "type")
        |> Seq.map (fun declaration ->
            {
                Name = str "fSharpName" declaration
                Arity = declaration.GetProperty("arity").GetInt32()
                Handles =
                    declaration.GetProperty("handles").EnumerateArray()
                    |> Seq.map _.GetString()
                    |> Seq.toArray
            })
        |> Seq.toArray

    let sources =
        catalog.GetProperty("inputs").EnumerateArray()
        |> Seq.filter (fun source -> str "package" source = "typescript")
        |> Seq.map (fun source -> str "file" source, str "sha256" source)
        |> Seq.toArray

    declarations, sources

let nativeAssembly = typeof<Dom.HTMLInputElement>.Assembly

let nativeType (declaration: Declaration) =
    require (declaration.Name.StartsWith ("Fable.Core.TS.Dom.", StringComparison.Ordinal)) $"Unsupported native module: {declaration.Name}"

    let suffix =
        if declaration.Arity = 0 then
            ""
        else
            "`" + string declaration.Arity

    nativeAssembly.GetType (
        declaration.Name.Replace ("Fable.Core.TS.Dom.", "Fable.Core.TS+Dom+")
        + suffix,
        true
    )

let allProperties (native: Type) =
    Array.append [| native |] (native.GetInterfaces ())
    |> Array.collect _.GetProperties()
    |> Array.distinctBy _.Name

let property native name =
    allProperties native
    |> Array.tryFind (fun p -> p.Name = name)
    |> Option.defaultWith (fun () -> fail $"Native property missing: {native.Name}.{name}")

let typeName (declarations: Declaration array) (native: Type) =
    let rec render (native: Type) =
        if native = typeof<string> then
            "string"
        elif native = typeof<bool> then
            "bool"
        elif native = typeof<float> then
            "float"
        elif
            native.IsGenericType
            && native.GetGenericTypeDefinition () = typedefof<option<_>>
        then
            $"{render (native.GetGenericArguments()[0])} option"
        else
            let definition =
                if native.IsGenericType then
                    native.GetGenericTypeDefinition ()
                else
                    native

            let name =
                (definition.FullName.Split('`')[0]).Replace("Fable.Core.TS+Dom+", "Fable.Core.TS.Dom.")

            let declaration =
                declarations
                |> Array.tryFind (fun d -> d.Name = name)
                |> Option.defaultWith (fun () -> fail $"Unsupported native type shape: {native}")

            require (nativeType declaration = definition) $"Native representation mismatch: {name}"
            let basic = "Dom." + definition.Name.Split('`')[0]

            if native.IsGenericType then
                basic
                + "<"
                + (native.GetGenericArguments ()
                   |> Array.map render
                   |> String.concat ", ")
                + ">"
            else
                basic

    render native

let generate compilerRoot policyRoot packageRoot output =
    let declarations, sources = loadCatalogue packageRoot

    use package =
        JsonDocument.Parse (File.ReadAllBytes (Path.Combine (compilerRoot, "node_modules", "typescript", "package.json")))

    require
        (str "version" package.RootElement = compilerVersion
         && str "gitHead" package.RootElement = compilerRevision)
        "Installed compiler version/revision mismatch"

    let exe =
        Tsc.locate compilerRoot
        |> Option.defaultWith (fun () -> fail "Pinned TypeScript native compiler not found; run npm ci in tools/Ranvier.Dom.Generate")

    let libRoot = Path.GetDirectoryName exe

    for relative, expected in sources do
        let path = Path.Combine (libRoot, Path.GetFileName (relative: string))
        require (hashFile path = expected) $"TypeScript declaration input hash mismatch: {relative}"

    use channel = new TscChannel (exe, compilerRoot)
    Api.initialize channel |> ignore

    let program =
        Api.createProgram
            channel
            { CreateProgramParams.Default with
                RootFiles =
                    ValueSome
                        [|
                            DocumentIdentifier.FileName (Path.Combine (libRoot, "lib.dom.d.ts"))
                            DocumentIdentifier.FileName (Path.Combine (libRoot, "lib.esnext.d.ts"))
                        |]
            }

    let project = program.Project.Value.Id
    let snapshot = program.Snapshot

    let resolve name =
        Api.resolveName
            channel
            {
                Snapshot = snapshot
                Project = project
                Name = name
                Location = ValueNone
                File = ValueNone
                Position = ValueNone
                Meaning = SymbolFlags.Type
                ExcludeGlobals = ValueNone
            }
        |> ValueOption.defaultWith (fun () -> fail $"Compiler symbol missing: {name}")

    let catalogueFor (symbol: SymbolResponse) =
        let handles =
            symbol.Declarations
            |> ValueOption.defaultValue [||]
            |> Array.map (fun handle ->
                let parts = handle.Split ('.', 3)
                require (parts.Length = 3) $"Unsupported compiler declaration handle: {handle}"
                let file = Path.GetFileName parts[2]
                $"typescript@{compilerVersion}/lib/{file}#{parts[0]}.{parts[1]}")

        let candidates =
            declarations
            |> Array.filter (fun declaration ->
                declaration.Handles
                |> Array.exists (fun handle -> Array.contains handle handles))

        require (candidates.Length = 1) $"Missing or ambiguous catalogue identity: {symbol.Name}"
        candidates[0]

    let properties (symbol: SymbolResponse) =
        let native =
            Api.getDeclaredTypeOfSymbol
                channel
                {
                    Snapshot = snapshot
                    Project = project
                    Symbol = symbol.Id
                }

        Api.getPropertiesOfType
            channel
            {
                Snapshot = snapshot
                Project = project
                Type = native.Id
            }
        |> ValueOption.defaultValue [||]

    let memberSymbol symbols name =
        symbols
        |> Array.tryFind (fun (symbol: SymbolResponse) -> symbol.Name = name)
        |> Option.defaultWith (fun () -> fail $"Compiler property missing: {name}")

    let readonly (symbol: SymbolResponse) =
        Api.isReadonlySymbol
            channel
            {
                Snapshot = snapshot
                Project = project
                Symbol = symbol.Id
            }

    let tagMap = resolve "HTMLElementTagNameMap"
    catalogueFor tagMap |> nativeType |> ignore
    let tags = properties tagMap
    let eventMap = resolve "HTMLElementEventMap"
    let eventMapNative = catalogueFor eventMap |> nativeType
    let eventSymbol = memberSymbol (properties eventMap) "input"

    let eventType =
        Api.getTypeOfSymbol
            channel
            {
                Snapshot = snapshot
                Project = project
                Symbol = eventSymbol.Id
            }

    let symbolOfType (native: TypeResponse) =
        Api.getSymbolOfType
            channel
            {
                Snapshot = snapshot
                Project = project
                ObjectId = native.Id
            }
        |> ValueOption.defaultWith (fun () -> fail "Unsupported type without declaration identity")

    let eventDeclaration = symbolOfType eventType |> catalogueFor
    let eventNative = property eventMapNative "input"
    require (nativeType eventDeclaration = eventNative.PropertyType) "Event-map native type mismatch"
    let lines = ResizeArray<string>()

    let emit text =
        lines.Add text

    emit "// <auto-generated> tools/Ranvier.Dom.Generate; regenerate instead of editing. </auto-generated>"
    emit "module Ranvier.View.Foundation.Generated.Html"
    emit ""
    emit "open Fable.Core.TS"
    emit "open Ranvier.View.Foundation.Representation"
    emit "open Ranvier.View.Foundation"
    emit ""

    let omissions = ResizeArray<string * string * string>()
    let templateOmissions = ResizeArray<string * string * string>()
    let counts = ResizeArray<string * int * int * int>()
    let programElements = ResizeArray<string>()
    let templateTags = ResizeArray<string * string>()

    let templateProperties =
        System.Collections.Generic.SortedDictionary<string, ResizeArray<string * string * string>>()

    let templatePropertyOwners = System.Collections.Generic.Dictionary<string, Type>()

    let templateGroupEvents =
        System.Collections.Generic.SortedDictionary<string, System.Collections.Generic.SortedDictionary<string, string * string>>()

    let templateAttributes =
        ResizeArray<string * string * string * string * string * string>()

    let nativeGroupName (native: Type) =
        let name = native.Name

        let name =
            if
                name.StartsWith ("HTML", StringComparison.Ordinal)
                && name.Length > 4
            then
                name.Substring 4
            else
                name

        if
            name.EndsWith ("Element", StringComparison.Ordinal)
            && name.Length > 7
        then
            name.Substring (0, name.Length - 7)
        else
            name

    let sharedProperties =
        System.Collections.Generic.SortedDictionary<string, ResizeArray<string>>()

    let emittedProperties = System.Collections.Generic.HashSet<string>()
    let sharedEvents = System.Collections.Generic.SortedDictionary<string, Type>()

    let commonEvents =
        properties eventMap
        |> Array.map _.Name
        |> Set.ofArray

    let voidTags =
        set
            [
                "area"
                "base"
                "br"
                "col"
                "embed"
                "hr"
                "img"
                "input"
                "link"
                "meta"
                "param"
                "source"
                "track"
                "wbr"
            ]

    let excluded =
        set
            [
                "innerHTML"
                "outerHTML"
                "textContent"
                "innerText"
                "outerText"
                "srcdoc"
                "files"
            ]

    let childReplacingSetters =
        set
            [
                "HTMLAnchorElement.text"
                "HTMLOptionElement.text"
                "HTMLTitleElement.text"
                "HTMLScriptElement.text"
                "HTMLOutputElement.value"
                "HTMLOutputElement.defaultValue"
                "HTMLTextAreaElement.defaultValue"
            ]

    let tryType native =
        try
            Some (typeName declarations native)
        with :? InvalidOperationException ->
            None

    let rec primitive (native: Type) =
        native = typeof<string>
        || native = typeof<bool>
        || native = typeof<float>
        || (native.IsGenericType
            && native.GetGenericTypeDefinition () = typedefof<option<_>>
            && primitive (native.GetGenericArguments()[0]))

    let moduleName (tag: string) =
        Char.ToUpperInvariant(tag[0]).ToString()
        + tag.Substring (1)

    let tagExpression tag =
        match tag with
        | "div" -> "Div"
        | "button" -> "Button"
        | "input" -> "Input"
        | "select" -> "Select"
        | "option" -> "Option"
        | _ -> "(HtmlTag \"" + tag + "\")"

    let tagMapNative = nativeType (catalogueFor tagMap)

    for tagSymbol in tags |> Array.sortBy _.Name do
        let tag = tagSymbol.Name

        let tagType =
            Api.getTypeOfSymbol
                channel
                {
                    Snapshot = snapshot
                    Project = project
                    Symbol = tagSymbol.Id
                }

        let symbol = symbolOfType tagType
        let declaration = catalogueFor symbol
        let native = nativeType declaration
        let name = typeName declarations native
        require ((property tagMapNative tag).PropertyType = native) $"Tag-map native type mismatch: {tag}"
        let expression = tagExpression tag

        if voidTags.Contains tag then
            emit $"let ``{tag}`` ops (modifiers: Typed.Modifier<{name}> array) = Typed.element ops {expression} modifiers [||]"

            programElements.Add
                $"    let inline ``{tag}`` (modifiers: Typed.Modifier<{name}> array) : Representation.Program.View = Typed.programElement {expression} modifiers [||]"
        else
            emit $"let ``{tag}`` ops (modifiers: Typed.Modifier<{name}> array) children = Typed.element ops {expression} modifiers children"

            programElements.Add
                $"    let inline ``{tag}`` (modifiers: Typed.Modifier<{name}> array) (children: Representation.Program.View array) : Representation.Program.View = Typed.programElement {expression} modifiers children"

        if voidTags.Contains tag then
            templateTags.Add (
                tag,
                $"    member _.``{tag}``(parts: Part<{name}, 'inst> array) : TemplateView<'inst> = TemplateParts.element {expression} parts [||]"
            )
        else
            templateTags.Add (
                tag,
                $"    member _.``{tag}`` (parts: Part<{name}, 'inst> array) (children: TemplateView<'inst> array) : TemplateView<'inst> = TemplateParts.element {expression} parts children"
            )

        emit ""
        emit $"module {moduleName tag} ="
        let members = properties symbol
        let mutable getters, setters, listeners = 0, 0, 0

        for memberInfo in members |> Array.sortBy _.Name do
            let memberName = memberInfo.Name

            let nativeProperties =
                Array.append [| native |] (native.GetInterfaces ())
                |> Array.collect _.GetProperties()
                |> Array.filter (fun property -> property.Name = memberName)

            if nativeProperties.Length > 0 then
                let own =
                    native.GetProperties ()
                    |> Array.filter (fun property -> property.Name = memberName)

                let candidates =
                    if own.Length > 0 then
                        own
                    else
                        nativeProperties
                        |> Array.filter (fun candidate ->
                            not (
                                nativeProperties
                                |> Array.exists (fun other ->
                                    candidate.DeclaringType <> other.DeclaringType
                                    && candidate.DeclaringType.IsAssignableFrom other.DeclaringType)
                            ))

                let signatures =
                    candidates
                    |> Array.map _.PropertyType
                    |> Array.distinct

                require (signatures.Length = 1) $"Conflicting inherited native property: {native.Name}.{memberName}"

                let nativeProperty =
                    candidates
                    |> Array.tryFind _.CanWrite
                    |> Option.defaultValue candidates[0]

                match tryType nativeProperty.PropertyType with
                | None -> omissions.Add (tag, memberName, $"unsupported type: {nativeProperty.PropertyType}")
                | Some valueType ->
                    let owner = nativeProperty.DeclaringType
                    let ownerName = typeName declarations owner

                    let canSet =
                        not (readonly memberInfo)
                        && nativeProperty.CanWrite
                        && not (excluded.Contains memberName)
                        && primitive nativeProperty.PropertyType

                    if emittedProperties.Add (owner.FullName + "." + memberName) then
                        if not (sharedProperties.ContainsKey owner.Name) then
                            sharedProperties.Add (owner.Name, ResizeArray<string>())

                        let shared = sharedProperties[owner.Name]
                        shared.Add $"        let ``{memberName}`` (node: #{ownerName}) : {valueType} = node.``{memberName}``"

                        if canSet then
                            if childReplacingSetters.Contains (owner.Name + "." + memberName) then
                                templateOmissions.Add (owner.Name, memberName, "setter replaces child nodes")
                            else
                                if not (templateProperties.ContainsKey owner.Name) then
                                    templateProperties.Add (owner.Name, ResizeArray ())
                                    templatePropertyOwners.Add (owner.Name, owner)

                                templateProperties[owner.Name].Add(memberName, ownerName, valueType)

                            shared.Add
                                $"        let {memberName}Static<'native when 'native :> {ownerName}> (value: {valueType}) : Typed.Modifier<'native> ="

                            shared.Add
                                $"            Typed.staticProperty (fun (node: 'native) value -> (node :> {ownerName}).``{memberName}`` <- value) value"

                            shared.Add
                                $"        let {memberName}Reactive<'native when 'native :> {ownerName}> (read: unit -> {valueType}) : Typed.Modifier<'native> ="

                            shared.Add
                                $"            Typed.reactiveProperty (fun (node: 'native) value -> (node :> {ownerName}).``{memberName}`` <- value) read"

                        shared.Add ""

                    let compatibility =
                        (Set.ofList [ "div"; "button"; "input"; "select"; "option" ]).Contains tag
                        && (Set.ofList [ "title"; "value"; "checked"; "files"; "form" ]).Contains memberName

                    let expose =
                        (native.Name <> "HTMLElement" && owner = native)
                        || compatibility

                    if expose then
                        getters <- getters + 1
                        emit $"    let ``{memberName}`` (node: {name}) : {valueType} = node.``{memberName}``"

                        if canSet then
                            setters <- setters + 1
                            emit $"    let {memberName}Static (value: {valueType}) : Typed.Modifier<{name}> ="
                            emit $"        Typed.staticProperty (fun (node: {name}) value -> node.``{memberName}`` <- value) value"
                            emit $"    let {memberName}Reactive (read: unit -> {valueType}) : Typed.Modifier<{name}> ="
                            emit $"        Typed.reactiveProperty (fun (node: {name}) value -> node.``{memberName}`` <- value) read"

                        emit ""

                    if
                        not (readonly memberInfo)
                        && nativeProperty.CanWrite
                    then
                        if excluded.Contains memberName then
                            omissions.Add (tag, memberName, "setter excluded by Ranvier ownership/form policy")
                        elif not (primitive nativeProperty.PropertyType) then
                            omissions.Add (tag, memberName, "setter requires explicit native-object equality/ownership policy")


        let eventMaps =
            Array.append [| native |] (native.GetInterfaces ())
            |> Array.choose (fun candidate ->
                let fullName = $"Fable.Core.TS.Dom.{candidate.Name}EventMap"

                declarations
                |> Array.tryFind (fun d -> d.Name = fullName))
            |> Array.distinctBy _.Name

        let events = System.Collections.Generic.Dictionary<string, Type>()

        for mapDeclaration in eventMaps |> Array.sortBy _.Name do
            let mapNative = nativeType mapDeclaration
            let mapSymbol = resolve mapNative.Name
            require (nativeType (catalogueFor mapSymbol) = mapNative) "Event map identity mismatch"

            for eventInfo in properties mapSymbol |> Array.sortBy _.Name do
                let eventProperty = property mapNative eventInfo.Name

                match tryType eventProperty.PropertyType with
                | None -> omissions.Add (tag, "event:" + eventInfo.Name, $"unsupported event type: {eventProperty.PropertyType}")
                | Some _ ->
                    let response =
                        Api.getTypeOfSymbol
                            channel
                            {
                                Snapshot = snapshot
                                Project = project
                                Symbol = eventInfo.Id
                            }

                    let eventDeclaration = symbolOfType response |> catalogueFor

                    let eventDefinition =
                        if eventProperty.PropertyType.IsGenericType then
                            eventProperty.PropertyType.GetGenericTypeDefinition ()
                        else
                            eventProperty.PropertyType

                    require (nativeType eventDeclaration = eventDefinition) $"Event-map native type mismatch: {eventInfo.Name}"

                    match events.TryGetValue eventInfo.Name with
                    | true, previous ->
                        require (previous = eventProperty.PropertyType) $"Conflicting inherited event type: {native.Name}.{eventInfo.Name}"
                    | _ -> events.Add (eventInfo.Name, eventProperty.PropertyType)

        for event in events |> Seq.sortBy _.Key do
            let eventType = typeName declarations event.Value

            if commonEvents.Contains event.Key then
                match sharedEvents.TryGetValue event.Key with
                | true, previous -> require (previous = event.Value) $"Conflicting common event: {event.Key}"
                | _ -> sharedEvents.Add (event.Key, event.Value)
            else
                let group = nativeGroupName native

                if not (templateGroupEvents.ContainsKey group) then
                    templateGroupEvents.Add (group, System.Collections.Generic.SortedDictionary ())

                match templateGroupEvents[group].TryGetValue event.Key with
                | true, (previous, previousNative) ->
                    require (previous = eventType && previousNative = name) $"Conflicting template event type: {group}.{event.Key}"
                | _ -> templateGroupEvents[group].Add(event.Key, (eventType, name))

            if
                not (commonEvents.Contains event.Key)
                || event.Key = "input"
                || event.Key = "click"
            then
                listeners <- listeners + 1
                emit $"    let on{moduleName event.Key} (handler: {eventType} -> unit) : Typed.Modifier<{name}> ="
                emit $"        Typed.listener \"{event.Key}\" (fun event -> handler (unbox event))"
                emit ""

        require (getters + listeners > 0) $"No usable members for tag: {tag}"
        counts.Add (tag, getters, setters, listeners)

    let shared = ResizeArray<string>()
    shared.Add "module Properties ="

    for owner in sharedProperties do
        shared.Add $"    module {owner.Key} ="
        shared.AddRange owner.Value

    shared.Add "module Events ="

    for event in sharedEvents do
        let eventType = typeName declarations event.Value

        shared.Add
            $"    let on{moduleName event.Key}<'native when 'native :> Dom.HTMLElement> (handler: {eventType} -> unit) : Typed.Modifier<'native> ="

        shared.Add $"        Typed.listener \"{event.Key}\" (fun event -> handler (unbox event))"
        shared.Add ""

    lines.InsertRange (7, shared)

    let policyPath = Path.Combine (policyRoot, "attributes.json")
    use attributePolicy = JsonDocument.Parse (File.ReadAllBytes policyPath)
    require (attributePolicy.RootElement.GetProperty("schemaVersion").GetInt32() = 1) "Unsupported attribute policy schema"
    let attributeCount = ref 0
    emit "module Attrs ="
    emit "    let dataStatic<'native when 'native :> Dom.HTMLElement> suffix value : Typed.Modifier<'native> ="
    emit "        Typed.attribute (Typed.prefixedAttributeName \"data-\" suffix) value"
    emit "    let dataReactive<'native when 'native :> Dom.HTMLElement> suffix read : Typed.Modifier<'native> ="
    emit "        Typed.reactiveAttribute (Typed.prefixedAttributeName \"data-\" suffix) read"
    emit "    let ariaStatic<'native when 'native :> Dom.HTMLElement> suffix value : Typed.Modifier<'native> ="
    emit "        Typed.attribute (Typed.prefixedAttributeName \"aria-\" suffix) value"
    emit "    let ariaReactive<'native when 'native :> Dom.HTMLElement> suffix read : Typed.Modifier<'native> ="
    emit "        Typed.reactiveAttribute (Typed.prefixedAttributeName \"aria-\" suffix) read"
    emit ""
    let groupNames = System.Collections.Generic.HashSet<string>()

    for group in attributePolicy.RootElement.GetProperty("groups").EnumerateArray() do
        let groupName = str "module" group
        require (groupNames.Add groupName) "Duplicate attribute policy module"
        let nativeName = str "native" group
        let symbol = resolve nativeName
        let native = nativeType (catalogueFor symbol)
        require (typeof<Dom.HTMLElement>.IsAssignableFrom native) $"Attribute owner is not an HTML element: {nativeName}"
        let nativeName = typeName declarations native
        let nativeMembers = properties symbol
        let indent = if groupName = "" then "    " else "        "

        if groupName <> "" then
            emit $"    module {groupName} ="

        let helperNames = System.Collections.Generic.HashSet<string>()

        for entry in group.GetProperty("entries").EnumerateArray() do
            let helper = str "helper" entry
            let attribute = str "name" entry
            let kind = str "kind" entry
            require (helperNames.Add helper) "Duplicate attribute helper name"

            require
                (attribute <> "srcdoc"
                 && not (attribute.StartsWith ("on", StringComparison.Ordinal)))
                "Unsafe attribute policy entry"

            require
                (not (String.IsNullOrEmpty attribute)
                 && Char.IsAsciiLetterLower attribute[0]
                 && (attribute
                     |> Seq.forall (fun c ->
                         Char.IsAsciiLetterLower c
                         || (c >= '0' && c <= '9')
                         || c = '-'
                         || c = '_')))
                "Invalid attribute policy name"

            memberSymbol nativeMembers (str "member" entry)
            |> ignore

            property native (str "member" entry) |> ignore

            let staticType, readType, staticFunction, reactiveFunction =
                match kind with
                | "string" -> "string", "string option", "attribute", "reactiveAttribute"
                | "boolean" -> "bool", "bool", "booleanAttribute", "reactiveBooleanAttribute"
                | "ariaBoolean" -> "bool", "bool option", "ariaBooleanAttribute", "reactiveAriaBooleanAttribute"
                | _ -> fail $"Unknown attribute policy kind: {kind}"

            attributeCount.Value <- attributeCount.Value + 1
            templateAttributes.Add (groupName, nativeGroupName native, nativeName, helper, attribute, kind)
            emit $"{indent}let {helper}Static<'native when 'native :> {nativeName}> (value: {staticType}) : Typed.Modifier<'native> ="
            emit $"{indent}    Typed.{staticFunction} \"{attribute}\" value"
            emit $"{indent}let {helper}Reactive<'native when 'native :> {nativeName}> (read: unit -> {readType}) : Typed.Modifier<'native> ="
            emit $"{indent}    Typed.{reactiveFunction} \"{attribute}\" read"
            emit ""

    let authoringPath = Path.Combine (policyRoot, "authoring.json")
    use authoringPolicy = JsonDocument.Parse (File.ReadAllBytes authoringPath)
    require (authoringPolicy.RootElement.GetProperty("schemaVersion").GetInt32() = 1) "Unsupported authoring policy schema"
    let authoringCount = ref 0

    let identifier (value: string) =
        not (String.IsNullOrEmpty value)
        && Char.IsAsciiLetter value[0]
        && (value
            |> Seq.forall (fun c -> Char.IsAsciiLetterOrDigit c || c = '_'))

    let overloads indent helper native valueType staticExpression reactiveExpression =
        let signature argument =
            $"{indent}static member inline ``{helper}``<'native when 'native :> {native}> ({argument}) : Typed.Modifier<'native> ="

        emit (signature $"value: {valueType}")
        emit $"{indent}    {staticExpression}"
        emit (signature $"read: unit -> {valueType}")
        emit $"{indent}    {reactiveExpression}"

        for sourceType in [ "Signal"; "Memo" ] do
            emit (signature $"source: global.Ranvier.{sourceType}<{valueType}>")
            emit $"{indent}    if isNull (box source) then raise (System.ArgumentException (\"A reactive source cannot be null\", \"source\"))"
            emit $"{indent}    let read () = source.Value"
            emit $"{indent}    {reactiveExpression}"

        emit ""

    emit "module Authoring ="

    let selections =
        authoringPolicy.RootElement.GetProperty("attributes").EnumerateArray()
        |> Seq.toArray
        |> Array.groupBy (str "group")
        |> Array.sortBy fst

    require
        (selections
         |> Array.exists (fun (group, _) -> group = ""))
        "Authoring needs global attributes"

    let authoringNames = System.Collections.Generic.HashSet<string>()

    for groupName, entries in selections do
        let group =
            attributePolicy.RootElement.GetProperty("groups").EnumerateArray()
            |> Seq.tryFind (fun group -> str "module" group = groupName)
            |> Option.defaultWith (fun () -> fail $"Unknown authoring attribute group: {groupName}")

        let native =
            nativeType (catalogueFor (resolve (str "native" group)))
            |> typeName declarations

        let indent = if groupName = "" then "        " else "            "

        if groupName = "" then
            emit "    type Attrs ="
        else
            if
                groupName =
                    (selections
                     |> Array.find (fun (name, _) -> name <> "")
                     |> fst)
            then
                emit "    module Attrs ="

            require (identifier groupName) "Invalid authoring group name"
            emit $"        type {groupName} ="

        for selection in entries do
            let helper, name = str "helper" selection, str "name" selection

            require
                (identifier name
                 && authoringNames.Add ($"Attrs.{groupName}.{name}"))
                "Invalid or duplicate authoring helper"

            let entry =
                group.GetProperty("entries").EnumerateArray()
                |> Seq.tryFind (fun entry -> str "helper" entry = helper)
                |> Option.defaultWith (fun () -> fail $"Unknown authoring attribute: {groupName}.{helper}")

            let attribute = str "name" entry

            match str "kind" entry with
            | "string" ->
                overloads
                    indent
                    name
                    native
                    "string"
                    $"Typed.attribute \"{attribute}\" value"
                    $"Typed.reactiveAttribute \"{attribute}\" (fun () -> Some (read ()))"

                overloads
                    indent
                    name
                    native
                    "string option"
                    $"Typed.optionalAttribute \"{attribute}\" value"
                    $"Typed.reactiveAttribute \"{attribute}\" read"
            | "boolean" ->
                overloads
                    indent
                    name
                    native
                    "bool"
                    $"Typed.booleanAttribute \"{attribute}\" value"
                    $"Typed.reactiveBooleanAttribute \"{attribute}\" read"
            | "ariaBoolean" ->
                overloads
                    indent
                    name
                    native
                    "bool"
                    $"Typed.ariaBooleanAttribute \"{attribute}\" value"
                    $"Typed.reactiveAriaBooleanAttribute \"{attribute}\" (fun () -> Some (read ()))"

                overloads
                    indent
                    name
                    native
                    "bool option"
                    $"Typed.optionalAttribute \"{attribute}\" (value |> Option.map (fun enabled -> if enabled then \"true\" else \"false\"))"
                    $"Typed.reactiveAriaBooleanAttribute \"{attribute}\" read"
            | kind -> fail $"Unknown authoring attribute kind: {kind}"

            authoringCount.Value <- authoringCount.Value + 1

    emit "    module Props ="

    for group in authoringPolicy.RootElement.GetProperty("properties").EnumerateArray() do
        let groupName = str "group" group

        require
            (identifier groupName
             && authoringNames.Add ($"Props.{groupName}"))
            "Invalid or duplicate authoring property group"

        let symbol = resolve (str "native" group)
        let native = nativeType (catalogueFor symbol)
        require (typeof<Dom.HTMLElement>.IsAssignableFrom native) "Authoring property owner must be an HTML element"
        let nativeName = typeName declarations native
        let members = properties symbol
        emit $"        type {groupName} ="

        for memberName in
            group.GetProperty("members").EnumerateArray()
            |> Seq.map _.GetString() do
            require
                (identifier memberName
                 && authoringNames.Add ($"Props.{groupName}.{memberName}"))
                "Invalid or duplicate authoring property"

            let memberInfo = memberSymbol members memberName
            let nativeProperty = property native memberName

            require
                (nativeProperty.CanWrite
                 && not (readonly memberInfo)
                 && not (excluded.Contains memberName)
                 && primitive nativeProperty.PropertyType)
                $"Unsupported authoring property: {native.Name}.{memberName}"

            let owner = nativeProperty.DeclaringType
            require (emittedProperties.Contains (owner.FullName + "." + memberName)) "Authoring property has no generated binding"
            let valueType = typeName declarations nativeProperty.PropertyType

            overloads
                "            "
                memberName
                nativeName
                valueType
                $"Properties.{owner.Name}.{memberName}Static value"
                $"Properties.{owner.Name}.{memberName}Reactive read"

            authoringCount.Value <- authoringCount.Value + 1

    emit "module Program ="
    lines.AddRange programElements

    let format =
        { Fantomas.Core.FormatConfig.Default with
            MaxLineLength = 150
            MaxInfixOperatorExpression = 50
            KeepMaxNumberOfBlankLines = 3
            MaxFunctionBindingWidth = 10
            SpaceBeforeUppercaseInvocation = true
        }

    let contents =
        Fantomas.Core.CodeFormatter.FormatDocumentAsync (false, String.concat "\n" lines + "\n", format)
        |> Async.RunSynchronously
        |> fun formatted -> formatted.Code.Replace ("\r\n", "\n")

    let reserved =
        dict
            [
                "TemplateBuilder",
                set
                    [
                        "Attrs"
                        "Props"
                        "Events"
                        "element"
                        "fragment"
                        "text"
                        "from"
                        "isolated"
                        "view"
                        "arm"
                        "matchWith"
                        "branch"
                        "each"
                        "render"
                    ]
                "AttributeBuilder", set [ "attribute"; "data"; "aria" ]
                "PropertyBuilder", set [ "propertyStatic"; "propertyWith"; "propertyFrom" ]
                "EventBuilder", set [ "on" ]
            ]

    let authoringRenames =
        authoringPolicy.RootElement.GetProperty("attributes").EnumerateArray()
        |> Seq.map (fun selection -> (str "group" selection, str "helper" selection), str "name" selection)
        |> dict

    let templateName group helper =
        match authoringRenames.TryGetValue ((group, helper)) with
        | true, name -> name
        | _ -> helper

    let t = ResizeArray<string>()

    let tEmit (line: string) =
        t.Add line

    tEmit "// <auto-generated> tools/Ranvier.Dom.Generate; regenerate instead of editing. </auto-generated>"
    tEmit "[<AutoOpen>]"
    tEmit "module Ranvier.View.Foundation.HtmlTemplate"
    tEmit ""
    tEmit "open Fable.Core.TS"
    tEmit "open Ranvier.View.Foundation.Representation"
    tEmit "open Ranvier.View.Foundation.Template"
    tEmit ""

    let flatOwner (owner: Type) =
        owner.IsAssignableFrom typeof<Dom.HTMLElement>

    let attributeGroups =
        templateAttributes
        |> Seq.filter (fun (policyGroup, _, _, _, _, _) -> policyGroup <> "")
        |> Seq.map (fun (_, group, _, _, _, _) -> group)
        |> Seq.distinct
        |> Seq.toArray

    let propertyGroups =
        templateProperties.Keys
        |> Seq.filter (fun owner -> not (flatOwner templatePropertyOwners[owner]))
        |> Seq.map (fun owner -> nativeGroupName templatePropertyOwners[owner], owner)
        |> Seq.sortBy fst
        |> Seq.toArray

    let eventGroups = templateGroupEvents.Keys |> Seq.toArray

    let unique (scope: string) (names: string seq) =
        let seen = System.Collections.Generic.HashSet<string>()

        for name in names do
            require (seen.Add name) $"Duplicate template member: {scope}.{name}"

    let checkBuilder builder (flat: string array) (groups: string array) =
        unique builder (Seq.append flat groups)

        for name in Seq.append flat groups do
            require (not (reserved[builder].Contains name)) $"Template member takes a reserved name: {builder}.{name}"

    let emitAccessors (kind: string) (groups: string seq) =
        for group in groups do
            tEmit $"    member _.{group}: Groups.{group}{kind}<'inst> = Groups.{group}{kind} ()"

        tEmit ""

    let emitGroup (kind: string) (group: string) (names: string seq) (emitMembers: unit -> unit) =
        unique $"Groups.{group}{kind}" names
        tEmit $"type Groups.{group}{kind}<'inst> with"
        emitMembers ()

    tEmit "module Groups ="

    for kind, groups in
        [
            "Attributes", attributeGroups
            "Properties", Array.map fst propertyGroups
            "Events", eventGroups
        ] do
        for group in groups do
            tEmit $"    [<Sealed>] type {group}{kind}<'inst> internal () = class end"

    tEmit ""
    checkBuilder "TemplateBuilder" (templateTags |> Seq.map fst |> Seq.toArray) [||]
    tEmit "type TemplateBuilder<'inst> with"
    t.AddRange (Seq.map snd templateTags)
    tEmit ""

    let attributeMembers indent (name: string) (native: string) (attribute: string) kind =
        let signature argument =
            $"{indent}member _.``{name}``<'native when 'native :> {native}>({argument}) : Part<'native, 'inst> ="

        let overload argument body =
            tEmit (signature argument)
            tEmit $"{indent}    {body}"

        match kind with
        | "string" ->
            overload "value: string" $"TemplateParts.attribute \"{attribute}\" value"
            overload "value: string option" $"TemplateParts.optionalAttribute \"{attribute}\" value"
            overload "read: 'inst -> string" $"TemplateParts.readAttribute \"{attribute}\" (fun instance -> TemplateParts.present (read instance))"
            overload "read: 'inst -> string option" $"TemplateParts.readAttribute \"{attribute}\" read"

            overload
                "source: Source<'inst, string>"
                $"TemplateParts.sourceAttribute \"{attribute}\" (TemplateParts.mapSource TemplateParts.present source)"

            overload "source: Source<'inst, string option>" $"TemplateParts.sourceAttribute \"{attribute}\" source"
        | "boolean" ->
            overload "value: bool" $"TemplateParts.optionalAttribute \"{attribute}\" (TemplateParts.presence value)"
            overload "read: 'inst -> bool" $"TemplateParts.readAttribute \"{attribute}\" (fun instance -> TemplateParts.presence (read instance))"

            overload
                "source: Source<'inst, bool>"
                $"TemplateParts.sourceAttribute \"{attribute}\" (TemplateParts.mapSource TemplateParts.presence source)"
        | "ariaBoolean" ->
            overload "value: bool" $"TemplateParts.attribute \"{attribute}\" (TemplateParts.ariaText value)"
            overload "value: bool option" $"TemplateParts.optionalAttribute \"{attribute}\" (TemplateParts.ariaOption value)"

            overload
                "read: 'inst -> bool"
                $"TemplateParts.readAttribute \"{attribute}\" (fun instance -> Some (TemplateParts.ariaText (read instance)))"

            overload
                "read: 'inst -> bool option"
                $"TemplateParts.readAttribute \"{attribute}\" (fun instance -> TemplateParts.ariaOption (read instance))"

            overload
                "source: Source<'inst, bool>"
                $"TemplateParts.sourceAttribute \"{attribute}\" (TemplateParts.mapSource (fun value -> Some (TemplateParts.ariaText value)) source)"

            overload
                "source: Source<'inst, bool option>"
                $"TemplateParts.sourceAttribute \"{attribute}\" (TemplateParts.mapSource TemplateParts.ariaOption source)"
        | _ -> fail $"Unknown attribute policy kind: {kind}"

        tEmit ""

    let flatAttributes =
        templateAttributes
        |> Seq.filter (fun (policyGroup, _, _, _, _, _) -> policyGroup = "")
        |> Seq.toArray

    checkBuilder
        "AttributeBuilder"
        (flatAttributes
         |> Array.map (fun (policyGroup, _, _, helper, _, _) -> templateName policyGroup helper))
        attributeGroups

    tEmit "type AttributeBuilder<'inst> with"

    for prefix in [ "data"; "aria" ] do
        let signature argument =
            $"    member _.{prefix}<'native when 'native :> Dom.HTMLElement>(suffix: string, {argument}) : Part<'native, 'inst> ="

        let name = $"(Typed.prefixedAttributeName \"{prefix}-\" suffix)"
        tEmit (signature "value: string")
        tEmit $"        TemplateParts.attribute {name} value"
        tEmit (signature "read: 'inst -> string option")
        tEmit $"        TemplateParts.readAttribute {name} read"
        tEmit (signature "source: Source<'inst, string option>")
        tEmit $"        TemplateParts.sourceAttribute {name} source"
        tEmit ""

    for policyGroup, _, native, helper, attribute, kind in flatAttributes do
        attributeMembers "    " (templateName policyGroup helper) native attribute kind

    emitAccessors "Attributes" attributeGroups

    for group in attributeGroups do
        let entries =
            templateAttributes
            |> Seq.filter (fun (policyGroup, entryGroup, _, _, _, _) -> policyGroup <> "" && entryGroup = group)
            |> Seq.toArray

        let names =
            entries
            |> Array.map (fun (policyGroup, _, _, helper, _, _) -> templateName policyGroup helper)

        emitGroup "Attributes" group names (fun () ->
            for policyGroup, _, native, helper, attribute, kind in entries do
                attributeMembers "    " (templateName policyGroup helper) native attribute kind)

    let propertyMembers (memberName: string) (ownerName: string) (valueType: string) =
        let signature argument =
            $"    member _.``{memberName}``<'native when 'native :> {ownerName}>({argument}) : Part<'native, 'inst> ="

        let write =
            $"(fun (node: 'native) (value: {valueType}) -> (node :> {ownerName}).``{memberName}`` <- value)"

        tEmit (signature $"value: {valueType}")
        tEmit $"        TemplateParts.property {write} value"
        tEmit (signature $"read: 'inst -> {valueType}")
        tEmit $"        TemplateParts.readProperty {write} read"
        tEmit (signature $"source: Source<'inst, {valueType}>")
        tEmit $"        TemplateParts.sourceProperty {write} source"
        tEmit ""

    let flatProperties =
        templateProperties
        |> Seq.filter (fun owner -> flatOwner templatePropertyOwners[owner.Key])
        |> Seq.collect _.Value
        |> Seq.sortBy (fun (memberName, ownerName, _) -> memberName, ownerName)
        |> Seq.toArray

    checkBuilder
        "PropertyBuilder"
        (flatProperties
         |> Array.map (fun (memberName, _, _) -> memberName))
        (propertyGroups |> Array.map fst)

    tEmit "type PropertyBuilder<'inst> with"

    for memberName, ownerName, valueType in flatProperties do
        propertyMembers memberName ownerName valueType

    emitAccessors "Properties" (Array.map fst propertyGroups)

    for group, owner in propertyGroups do
        let entries = templateProperties[owner]

        emitGroup
            "Properties"
            group
            (entries
             |> Seq.map (fun (memberName, _, _) -> memberName))
            (fun () ->
                for memberName, ownerName, valueType in entries do
                    propertyMembers memberName ownerName valueType)

    let eventMember (eventName: string) (native: string) (eventType: string) =
        tEmit
            $"    member _.on{moduleName eventName}<'native when 'native :> {native}>(handler: 'inst -> {eventType} -> unit) : Part<'native, 'inst> ="

        tEmit $"        TemplateParts.event \"{eventName}\" handler"
        tEmit ""

    checkBuilder
        "EventBuilder"
        (sharedEvents.Keys
         |> Seq.map (fun event -> "on" + moduleName event)
         |> Seq.toArray)
        eventGroups

    tEmit "type EventBuilder<'inst> with"

    for event in sharedEvents do
        eventMember event.Key "Dom.HTMLElement" (typeName declarations event.Value)

    emitAccessors "Events" eventGroups

    for group in eventGroups do
        let events = templateGroupEvents[group]

        emitGroup
            "Events"
            group
            (events.Keys
             |> Seq.map (fun event -> "on" + moduleName event))
            (fun () ->
                for event in events do
                    let eventType, native = event.Value
                    eventMember event.Key native eventType)

    let templateContents =
        Fantomas.Core.CodeFormatter.FormatDocumentAsync (false, String.concat "\n" t + "\n", format)
        |> Async.RunSynchronously
        |> fun formatted -> formatted.Code.Replace ("\r\n", "\n")

    let templateOutput =
        Path.Combine (
            Path.GetDirectoryName (Path.GetFullPath output),
            Path.GetFileNameWithoutExtension output
            + "Template.fs"
        )

    let templateEventCount =
        sharedEvents.Count
        + (templateGroupEvents.Values |> Seq.sumBy _.Count)

    let templatePropertyCount = templateProperties.Values |> Seq.sumBy _.Count

    for key in childReplacingSetters do
        require
            (templateOmissions
             |> Seq.exists (fun (owner, memberName, _) -> owner + "." + memberName = key))
            $"Child-replacing setter not found: {key}"

    Directory.CreateDirectory (Path.GetDirectoryName (Path.GetFullPath output))
    |> ignore

    let coverage =
        JsonSerializer.Serialize (
            {|
                package = "Xantham.Fable.Core.TS@0.2.1"
                tags =
                    counts
                    |> Seq.map (fun (tag, getters, setters, events) ->
                        {|
                            tag = tag
                            getters = getters
                            setters = setters
                            events = events
                            isVoid = voidTags.Contains tag
                        |})
                    |> Seq.toArray
                attributePolicySha256 = hashFile policyPath
                attributeCount = attributeCount.Value
                authoringHelperCount = authoringCount.Value
                authoringPolicySha256 = hashFile authoringPath
                sharedPropertyCount = emittedProperties.Count
                sharedEventCount = sharedEvents.Count
                templateTagCount = templateTags.Count
                templateAttributeCount = templateAttributes.Count
                templatePropertyCount = templatePropertyCount
                templateEventCount = templateEventCount
                templatePropertyGroups = propertyGroups.Length
                templateEventGroups = eventGroups.Length
                omissions =
                    omissions
                    |> Seq.map (fun (tag, memberName, reason) ->
                        {|
                            tag = tag
                            memberName = memberName
                            reason = reason
                        |})
                    |> Seq.toArray
                templateOmissions =
                    templateOmissions
                    |> Seq.map (fun (owner, memberName, reason) ->
                        {|
                            owner = owner
                            memberName = memberName
                            reason = reason
                        |})
                    |> Seq.toArray
            |},
            JsonSerializerOptions (WriteIndented = true)
        )

    File.WriteAllText (output, contents, UTF8Encoding (false))
    File.WriteAllText (Path.ChangeExtension (output, ".coverage.json"), coverage + "\n", UTF8Encoding (false))
    File.WriteAllText (templateOutput, templateContents, UTF8Encoding (false))
    printfn "Generated %d HTML tags from Core.TS 0.2.1 catalogue + Wire 0.3.0 + pinned TypeScript; SHA256 %s" counts.Count (hashFile output)

[<EntryPoint>]
let main args =
    try
        let compilerRoot = __SOURCE_DIRECTORY__

        let packageRoot =
            if args.Length > 1 then
                args[1]
            else
                AppContext.BaseDirectory

        let output =
            if args.Length = 0 then
                Path.GetFullPath (Path.Combine (__SOURCE_DIRECTORY__, "../../experiments/Ranvier.View.Foundation/Generated/Html.fs"))
            else
                args[0]

        let policyRoot = if args.Length > 2 then args[2] else compilerRoot

        generate compilerRoot policyRoot packageRoot output
        0
    with error ->
        eprintfn "%s" error.Message
        1
