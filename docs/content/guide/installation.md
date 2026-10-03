---
title: Installation
order: 2
---

:::info
Preview — Ranvier is pre-release; its APIs may change.
:::

Choose the package for your language, then continue with [Getting started](getting-started.md).

## Supported targets

:::center
|**Target**|Supported|Recommended|
|---:|:---:|:---:|
| **`net10`** | Yes | Yes |
| `net8` | Yes | - |
| `netstandard2.1` | Yes | No |
:::

:::tip Prefer .NET 10
The [pending-throw measurements](../concepts/suspension.md#what-a-throw-costs) document runtime-specific
exception costs. They are measurements of that path, not a guarantee of faster application workloads.
:::

## From NuGet

Install `Ranvier` for F#, `Ranvier.CSharp` for C#, or `Ranvier.Elmish` for the MVU bridge.
Versions are previews, so pass `--prerelease`.

::::::tabs
:::::tab Core
::::tabs
:::tab NuGet

```bash
dotnet add package Ranvier --prerelease
```

:::

:::tab Package reference

```xml
<PackageReference Include="Ranvier" Version="0.1.0-preview.4" />
```

:::

::::
:::::
:::::tab C#


::::tabs
:::tab NuGet

```bash
dotnet add package Ranvier.CSharp --prerelease
```
:::
:::tab Package reference

```xml
<PackageReference Include="Ranvier.CSharp" Version="0.1.0-preview.4" />
```
:::
::::
C# projects use `Ranvier.CSharp`; see [C#](csharp.md).

:::::
:::::tab Elmish
::::tabs
:::tab NuGet

```bash
dotnet add package Ranvier.Elmish --prerelease
```

:::

:::tab Package reference

```xml
<PackageReference Include="Ranvier.Elmish" Version="0.1.0-preview.4" />
```

:::
::::

The MVU bridge, `Mvu`, ships as its own package, `Ranvier.Elmish`, which depends on `Ranvier`; see
[Migrating from Elmish](elmish.md).

:::::
::::::

## Native AOT and trimming

The untraced `Ranvier`, `Ranvier.CSharp` and `Ranvier.Elmish` packages support trimming and Native AOT.
CI publishes smoke applications on .NET 10 for `linux-x64` and treats trim/AOT warnings as errors.
That validates this configuration; test your own deployment target and dependencies. Publish with
`<PublishAot>true</PublishAot>`.

:::warning Traced builds and AOT
Traced packages retain reflection-based `ToString` implementations in their trace types. Their
trimming and AOT compatibility remains an outstanding issue.
:::

## Traced packages

Each package has a `.Traced` variant for diagnostics. See [Tracing](tracing.md#from-nuget) for
switching between traced and untraced builds.

## Next

Continue with [Getting started](getting-started.md).
