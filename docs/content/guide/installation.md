---
title: Installation
order: 2
---

:::info
Preview — Ranvier is pre-release; its APIs may change.
:::

Ranvier targets `net10.0`, `net8.0` and `netstandard2.1`. The `netstandard2.1` build serves runtimes
such as Unity and Mono.

## From NuGet

Ranvier ships as the `Ranvier` NuGet package. Its versions are previews, so pass `--prerelease`:

```bash
dotnet add package Ranvier --prerelease
```

or, with central package management:

```xml
<PackageReference Include="Ranvier" Version="0.1.0-preview.1" />
```

The MVU bridge, `Mvu`, ships as its own package, `Ranvier.Elmish`, which depends on `Ranvier`; see
[Migrating from Elmish](elmish.md). C# projects use `Ranvier.CSharp`; see [C#](csharp.md).

| Package | Namespace | Contents |
| --- | --- | --- |
| `Ranvier` | `Ranvier` | The reactive graph, projections, boundaries and editable values. |
| `Ranvier.CSharp` | `Ranvier.CSharp` | Delegate-based factories, extension methods, `ReactiveBindings` and `ReactiveCommand`. |
| `Ranvier.Elmish` | `Ranvier.Elmish` | `Mvu`, an Elmish-style model read through selector memos. |

Each has a traced build under the same name with `.Traced` appended.

Then open the namespace:

```fsharp
open Ranvier
```

`open Ranvier` brings the `Api` module (`createSignal`, `createMemo`, `createEffect`, ...) and the
`GraphExtensions` module (`Graph.Run`) into scope. Both are `AutoOpen`.

## From source

To build against unreleased changes, build from source and reference the library project directly. You
need the .NET 10 SDK.

```bash
git clone https://github.com/shayanhabibi/Ranvier
cd Ranvier
dotnet build src/Ranvier -c Release
```

Reference the library from your project:

```xml
<ProjectReference Include="path/to/Ranvier/src/Ranvier/Ranvier.fsproj" />
```

Or pack it into a local feed and reference it as a package:

```bash
dotnet pack src/Ranvier -c Release -o ./local-feed
dotnet nuget add source ./local-feed --name local
```

`dotnet pack src/Ranvier -c Release -p:RanvierTrace=true -o ./local-feed` packs the traced build as
`Ranvier.Traced`; see [Tracing](tracing.md#from-nuget) for switching between the two.

## Native AOT and trimming

On `net8.0` and `net10.0`, `Ranvier`, `Ranvier.CSharp` and `Ranvier.Elmish` are marked trimmable and AOT-compatible.
An app that publishes with `<PublishAot>true</PublishAot>` gets no IL2xxx or IL3xxx warnings from any of them, and CI
publishes a smoke app with all three assemblies rooted to keep it that way. The traced builds (`Ranvier.Traced` and the
packages that depend on it) make no such claim: their trace types keep the generated, reflection-based `ToString`.

A projection's exception messages include the key as its `string` text. For a primitive or `string` key, the
exception keeps its documented type and message under Native AOT. A key of an F# record or union type prints
through that type's generated `ToString`, which uses FSharp.Core's reflective formatter; compile the key's
assembly with `<OtherFlags>$(OtherFlags) --reflectionfree</OtherFlags>`, or override `ToString` on the key type, to keep that
message under Native AOT.

## Next

Continue with [Getting started](getting-started.md).
