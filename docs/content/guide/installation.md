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

## Next

Continue with [Getting started](getting-started.md).
