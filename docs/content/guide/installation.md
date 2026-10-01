---
title: Installation
order: 2
---

:::info
Preview — Ranvier is pre-release; its APIs may change.
:::

:::center
|**Target**|Supported|Recommended|
|---:|:---:|:---:|
| **`net10`** | Yes | Yes |
| `net8` | Yes | - |
| `netstandard2.1` | Yes | No |
:::

:::warning
Below .NET9 missing key optimisations in exception handling.
:::

## From NuGet

Ranvier ships as the `Ranvier` NuGet package. Its versions are previews, so pass `--prerelease`:

Each package has a traced-build package appended with `.Traced`.

[//]: # (TODO - see traced)

::::::tabs
:::::tab Core
::::tabs
:::tab NuGet

```bash
dotnet add package Ranvier --prerelease
```

:::

:::tab CPM

```xml
<PackageReference Include="Ranvier" Version="0.1.0-preview.1" />
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
:::tab CPM

```xml
<PackageReference Include="Ranvier.CSharp" Version="0.1.0-preview.1" />
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

:::tab CPM

```xml
<PackageReference Include="Ranvier.Elmish" Version="0.1.0-preview.1" />
```

:::
::::

The MVU bridge, `Mvu`, ships as its own package, `Ranvier.Elmish`, which depends on `Ranvier`; see
[Migrating from Elmish](elmish.md).

:::::
::::::

## Native AOT and trimming

:::warning
Framework requirement: net8.0+
:::

CI/CD tests that `Ranvier`, `Ranvier.CSharp` and `Ranvier.Elmish` are trimmable and AOT-compatible.
Publish with `<PublishAot>true</PublishAot>`.

> The traced builds (`Ranvier.Traced` and the packages that depend on it) make no such claim: their trace types keep the
generated, reflection-based `ToString`.
>
> This is an outstanding issue.

## Next

Continue with [Getting started](getting-started.md).
