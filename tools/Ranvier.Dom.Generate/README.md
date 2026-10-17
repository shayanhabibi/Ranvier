# HTML DOM generator

`authoring.json` selects a small overloaded modifier surface under `Html.Authoring`: ten attributes from the validated attribute policy and four writable primitive properties from Wire/catalogue/native member facts. Values, readers, signals and memos use inline static methods. String/ARIA attributes also accept optional sources for removal. Coverage records the selection count and policy hash. Unsafe, readonly, missing and unsupported selected properties fail generation before output is written.

The generated `Html.Program` module supplies inline constructors bound to `Program.View`. See the [view authoring example](../../experiments/Ranvier.View.Foundation/README.md) for component setup, text, props and modifiers.

Run from the repository root:

```powershell
rtk npm ci --prefix tools/Ranvier.Dom.Generate --ignore-scripts
rtk dotnet run --project tools/Ranvier.Dom.Generate/Ranvier.Dom.Generate.fsproj -c Release
rtk proxy python tools/Ranvier.Dom.Generate/tests.py
rtk proxy python tools/Ranvier.Dom.Generate/check-types.py
```

The tool's own project restores published Cloudsmith `Xantham.Fable.Core.TS` 0.2.1 and `Xantham.TypeScript.Wire` 0.3.0. Its npm lock pins TypeScript 7.1.0-dev.20260902.1, revision `43a90f4c105bc9db7cb7aa299beddafbabe1d23e`. Generation reads installed packages; it never reads a sibling Xantham checkout. Output is formatted by pinned `Fantomas.Core` 7.0.5 and written as UTF-8 with LF endings. An optional first argument changes the output file, and a second argument selects a directory containing `xantham/catalogue.json` and `xantham/declarations.json.br`, useful for failure tests. An optional third argument, positional after that package root, selects a policy directory holding `attributes.json` and `authoring.json`; it defaults to the tool directory, and `tests.py` uses it to generate from mutated policies.

`.gitattributes` checks out `tools/Ranvier.Dom.Generate/*.json` with LF endings, so the policy hashes recorded in coverage reproduce on every checkout. `.fantomasignore` lists `experiments/Ranvier.View.Foundation/Generated/`: the generator's pinned Fantomas 7.0.5 output is authoritative, and the repository's Fantomas in the pre-commit hook skips the generated files.

The generator checks descriptor/package/schema identity, the pinned Brotli payload and native assembly hashes, compatibility versions, compiler version/revision and every TypeScript library input hash from the catalogue. Brotli decoding is bounded at 128 MiB. Wire resolves the tag and event maps, members and readonly flags. Compiler declaration handles are normalized to the catalogue's package/version/file/position/kind handles, then resolved to one existing native binding. The native assembly supplies the final F# representation of selected properties, including optional references. Each reflected named type must resolve through the catalogue; unsupported shapes and missing/ambiguous identities stop generation.

Generation covers all 112 entries in the pinned `HTMLElementTagNameMap`. Constructors preserve native element types and accept modifier and child arrays; void constructors take no children. Bound Program constructors inline the typed element path. Shared inherited properties live under `Html.Properties.<native declaring type>` and common listeners under `Html.Events`. Tag modules expose their native-declared members and element-specific events, plus original five-tag compatibility helpers. `Generated/Html.fs` is approximately 590 KB.

The same command writes a second output beside the first, named after it with a `Template` suffix: `Generated/HtmlTemplate.fs`, the template catalogue, approximately 400 KB. It declares `[<AutoOpen>] module Ranvier.View.Foundation.HtmlTemplate` and is written from the tags, attribute policy, properties and events resolved for `Html.fs`: one member per tag on `TemplateBuilder`, policy attributes (with the `authoring.json` renames, so `className`) plus `data` and `aria` on `AttributeBuilder`, the settable primitive properties of `Html.fs` on `PropertyBuilder` except the setters that replace child nodes (listed under `templateOmissions` in coverage), and events on `EventBuilder`. Members of other native types sit under group marker classes in a nested `Groups` module (`t.Attrs.Button`, `t.Props.Input`, `t.Events.Video`). Each member is one call into the handwritten `TemplateParts` module of `Template.fs`. The [spec clarifications](../../docs/superpowers/specs/2026-10-13-component-templates-design.md#generated-catalogue-clarifications) give the naming, grouping and overload rules. Generation stops before writing either output when a member takes a handwritten builder member's name, a group name equals a flat member name of its builder, or a group holds two members of one name. Coverage records the catalogue under six keys: `templateTagCount` (112), `templateAttributeCount` (34), `templatePropertyCount` (402), `templatePropertyGroups` (59), `templateEventCount` (132: 108 shared, 24 grouped) and `templateEventGroups` (3).

```fsharp
Html.article Tree.ops
    [| Html.Properties.HTMLElement.titleStatic "Article"
      Html.Events.onClick (fun event -> printfn "%f" event.clientX) |]
    [| Html.img Tree.ops [| Html.Img.altStatic "Illustration" |] |]
```

Shared modifiers constrain their native type, so input properties cannot be used on a div. Getters preserve supported primitives, options, named native references and closed native generics. Setters support writable primitives and optional primitives. Native-object setters require explicit equality/ownership policy. Unsupported arrays, callbacks and unmapped aliases/unions are recorded in `Generated/Html.coverage.json`; they are not widened to `obj`. Methods and SVG are outside this scope.

Files is writable upstream but getter-only by Ranvier policy; form is readonly upstream. `innerHTML`, `outerHTML`, `textContent`, `innerText`, `outerText` and iframe `srcdoc` receive no setters. The template catalogue also omits `text` on anchor, option, title and script, `value` and `defaultValue` on output, and `defaultValue` on textarea, whose setters replace child nodes. Generated helpers delegate ownership/scheduling to handwritten `Typed` operations. Templates mount children into content; selects apply modifiers after options. HTML snapshots reject raw-text/context-sensitive elements without explicit serialization policy and preserve initial newlines in pre/textarea. These distinctions follow the [HTML syntax model](https://html.spec.whatwg.org/multipage/syntax.html); legacy param retains void compatibility. Complete portable property metadata remains separate work.

Common attribute authoring is generated under `Html.Attrs` from the versioned `attributes.json` policy: 34 named attributes plus static/reactive `data-*` and `aria-*` helpers. Wire and the published catalogue validate native owner/member identities; Ranvier chooses attribute names and semantics. Coverage records the policy hash. Boolean attributes use presence/removal; ARIA booleans retain literal `false`. Optional string readers remove an attribute on `None`. All reactive helpers reuse the existing binding/queue ownership path, and HTML snapshots serialize their current values with escaping.

```fsharp
Html.button Tree.ops
    [| Html.Attrs.classStatic "primary"
      Html.Attrs.Button.disabledReactive (fun () -> busy.Value)
      Html.Attrs.ariaExpandedReactive (fun () -> Some expanded.Value)
      Html.Attrs.dataReactive "state" (fun () -> state.Value) |]
    [| Tree.ops.Text "Open" |]
```

Attributes and live properties are distinct. `Html.Attrs.Input.valueReactive` changes the default value attribute; use `Html.Input.valueReactive` for the live value. The same distinction applies to checked/defaultChecked and selected/defaultSelected. Event-handler attributes and srcdoc are rejected. This policy is a curated subset, not complete portable property metadata.

The tool remains a pinned adapter, not a general TypeScript-to-F# mapper or complete catalogue authenticator. Tests verify deterministic code/coverage, malformed-policy and corrupt-input rejection; compiler checks cover inherited constraints, properties/events/attributes, readonly and void-child failures.

Published artifact evidence inspected on 2026-10-11:

| Artifact | SHA-256 |
| --- | --- |
| [Core.TS 0.2.1 nupkg](https://dl.cloudsmith.io/public/shayanhabibi/xantham/nuget/Xantham.Fable.Core.TS/0.2.1/Xantham.Fable.Core.TS.0.2.1.nupkg) | `f6d5cc77a76e14f503e93998c56496757a1fb9343f5df8e1dcf29fdc775e97ad` |
| `xantham/catalogue.json` | `40d670b86bdb994b6108ae271cee1441215f04893b6d820e5d39075895f73942` |
| `xantham/declarations.json.br` | `ad2722747e79381c6ec729db17d96c9eb1046d68627099cb63986942563687c8` |
| `lib/net8.0/Xantham.Fable.Core.TS.dll` | `663021de210d5f4a4ae4b9edbbd71d850343edf1243a16bb322634051ffe0450` |
| Wire 0.3.0 nupkg | `fc2d451b0137dc73308aa8b3b7b2c3e47c09d8a7376f16e9e61378ac291eba24` |
| Compiler `lib/lib.dom.d.ts` | `d6b1eba8496bdd0eed6fc8a685768fe01b2da4a0388b5fe7df558290bffcf32f` |

Core.TS nuspec repository commit: `0d9626867755b04d294c4313cd8a1ec299ccf543`. The descriptor is schema 1, declarations schema 2, with 4,062 declarations, contract 1 / identity 5 / API 4 / inference 2 / customization 1 / AST protocol 8. Generator 0.2.0 remains published but is unnecessary for this tool. Its exported assembly API was inspected in SageFs: no public standalone package catalogue loader/resolver is available. Explicit pinned paths and the narrow owned reader avoid relying on that absent API.
