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
git clone https://github.com/shayanhabibi/Partas.Signals
cd Partas.Signals
git checkout 915f139
dotnet build -c Release
```

Commit `915f139` is the source revision these docs describe. Later commits on Partas.Signals are not
tracked by Ranvier and may change the API. These steps will switch to the Ranvier repository once it
contains the library source.

Reference the library from your project:

```xml
<ProjectReference Include="path/to/Partas.Signals/src/Partas.Signals/Partas.Signals.fsproj" />
```

Or pack it into a local feed and reference it as a package:

```bash
dotnet pack src/Partas.Signals -c Release -o ./local-feed
dotnet nuget add source ./local-feed --name local
```

:::warning
A build from the Partas.Signals source uses the `Partas.Signals` namespace. Write
`open Partas.Signals` where this guide writes `open Ranvier`. Every other identifier in the guide is
the same.
:::

## Next

Continue with [Getting started](getting-started.md).
