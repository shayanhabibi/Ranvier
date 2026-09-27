---
title: Installation
order: 2
---

:::info
Preview — Ranvier is pre-release; APIs follow Partas.Signals and may change.
:::

Ranvier targets `net10.0`.

## From NuGet (once published)

Ranvier will ship as the `Ranvier` NuGet package. It has not been published yet. Once it is:

```bash
dotnet add package Ranvier
```

or, with central package management:

```xml
<PackageReference Include="Ranvier" />
```

Then open the namespace:

```fsharp
open Ranvier
```

`open Ranvier` brings the `Api` module (`createSignal`, `createMemo`, `createEffect`, ...) and the
`GraphExtensions` module (`Graph.Run`) into scope. Both are `AutoOpen`.

## From source (today)

Until the first release, build from source and reference the library project directly. You need the
.NET 10 SDK.

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

## Next

Continue with [Getting started](getting-started.md).
