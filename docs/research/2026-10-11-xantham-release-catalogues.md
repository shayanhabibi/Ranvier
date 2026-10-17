# Xantham catalogue release evidence — 2026-10-11

Historical snapshot of Core.TS 0.2.0. The [Core.TS 0.2.1 integration](2026-10-12-catalogue-dom-generation.md) supersedes its availability conclusion and records the now-shipped catalogue, pinned generator and verification.

## Finding

The current public Cloudsmith release supports generating and consuming **explicit declaration catalogue files**, including Brotli and portable compatibility. However, the published `Xantham.Fable.Core.TS` **0.2.0 nupkg does not contain a catalogue or descriptor**. Package discovery and a shipped Core.TS catalogue cannot currently be treated as verified released inputs for Ranvier. This conclusion comes from inspecting the downloaded package ZIP, not from an old GitHub release page or the local Xantham checkout.

The feed is Cloudsmith, not nuget.org: [service index](https://nuget.cloudsmith.io/shayanhabibi/xantham/v3/index.json). The release commit's [publish workflow](https://github.com/shayanhabibi/Xantham/blob/500a04a8e67d05d8b0fa481d248aeca1e2e32ee1/.github/workflows/push_master.yml) explicitly publishes verified artifacts there. The [package inventory](https://api.cloudsmith.io/v1/packages/shayanhabibi/xantham/?page_size=100) records the latest uploads as completed on 2026-10-10, approximately 18:20–18:21 UTC. Recheck this feed before implementation because a subsequent publish could change the result.

## Pinned published artifacts

All four downloaded nuspecs identify repository commit [`500a04a8e67d05d8b0fa481d248aeca1e2e32ee1`](https://github.com/shayanhabibi/Xantham/commit/500a04a8e67d05d8b0fa481d248aeca1e2e32ee1). Versions below are the highest published versions in the corresponding feed indexes, also corroborated by the feed search service and inventory.

| Package | Version | Target frameworks | Download inspected |
| --- | --- | --- | --- |
| Xantham.Generator | 0.2.0 | net10.0 | [nupkg](https://dl.cloudsmith.io/public/shayanhabibi/xantham/nuget/xantham.generator/0.2.0/xantham.generator.0.2.0.nupkg) |
| Xantham.TypeScript.Wire | 0.3.0 | net10.0, net8.0, netstandard2.1 | [nupkg](https://dl.cloudsmith.io/public/shayanhabibi/xantham/nuget/xantham.typescript.wire/0.3.0/xantham.typescript.wire.0.3.0.nupkg) |
| Xantham.Fable.Core.TS | 0.2.0 | net8.0 | [nupkg](https://dl.cloudsmith.io/public/shayanhabibi/xantham/nuget/xantham.fable.core.ts/0.2.0/xantham.fable.core.ts.0.2.0.nupkg) |
| Xantham.Fable.Core | 0.2.0 | net8.0, netstandard2.1 | [nupkg](https://dl.cloudsmith.io/public/shayanhabibi/xantham/nuget/xantham.fable.core/0.2.0/xantham.fable.core.0.2.0.nupkg) |

The precise package IDs include `Fable` and `TypeScript`; `Core.TS`, `Core`, and `Wire` are shorthand here. Generator 0.2.0 depends on Wire 0.3.0. Core.TS 0.2.0 depends on Core 0.2.0 and Fable.Core 5.2.0. No dependency files were changed in this investigation.

Core.TS's complete ZIP entry list is:

```text
_rels/.rels
Xantham.Fable.Core.TS.nuspec
README.md
xantham-logo_64.png
lib/net8.0/Xantham.Fable.Core.TS.dll
lib/net8.0/Xantham.Fable.Core.TS.xml
[Content_Types].xml
package/services/metadata/core-properties/nuget.psmdcp
```

Consequently there is no published package path/schema/metadata to inspect for a Core.TS catalogue payload. The release [Core.TS project](https://github.com/shayanhabibi/Xantham/blob/500a04a8e67d05d8b0fa481d248aeca1e2e32ee1/src/Xantham.Fable.Core.TS/Xantham.Fable.Core.TS.fsproj) also contains no catalogue packing items. Generator, Wire and Core likewise have no catalogue-like ZIP entries. Core includes its Fable sources under `fable/`; those are not a declaration catalogue.

For reproducibility, downloaded nupkg SHA-256 values were:

```text
Generator  d8c45be1ab234e2d4b18918fa15cdcadd0a824aa03179d1a07bfcd706619bcb7
Wire       fc2d451b0137dc73308aa8b3b7b2c3e47c09d8a7376f16e9e61378ac291eba24
Core.TS    4ada0917e2ee5032fe34b686bbd798184aa09548aaa7adc40a7413db77761b0c
Core       5adb69235adce769ef4c891e55a62f8e4e81378dba4b624cbc9779a485937c84
```

## Catalogue format the released generator can produce

This is a description of the **released generator's format**, not a claim that the binding nupkg ships an instance. The authoritative [DeclarationCatalog source](https://github.com/shayanhabibi/Xantham/blob/500a04a8e67d05d8b0fa481d248aeca1e2e32ee1/src/Xantham.Generator/DeclarationCatalog.fs) emits camelCase JSON, `schemaVersion: 2`, with:

| Field | Meaning and planning use |
| --- | --- |
| `declarations[].identity` | Stable hashed declaration identity; not merely a TypeScript short name. |
| `handles` | Normalized declaration handles tied to source package/version/file and declaration position/kind. |
| `sources`, root `inputs` | Source provenance: `package`, `version`, `file`, `sha256`, `manifestSha256`. These describe declaration inputs, not the binding NuGet package identity. |
| `role` | Identity role, including aliases, structural/generated roles; useful for interpreting multiple related entries. |
| `fSharpName` | Final qualified destination name, including its module path. This is the namespace/module binding fact an adapter should preserve rather than invent. |
| `arity`, `constraints` | F# generic parameter count and canonical constraints; loader requires a constraint entry per parameter. |
| `api` | Authentication fingerprint of the canonical generated API, not serialized member signatures or a recoverable type AST. |
| `owner`, root `owner`, `owners` | Declaration ownership and owner dependency graph; not an authenticated NuGet package resolver. |
| `compiler`, `generator`, `inferenceProfile` | Producer fingerprints/profile metadata. |
| `compatibility` | Explicit schema-2 compatibility contract and compiler identity. |
| optional `variants` | Customization identity, API/base-API fingerprints, contract-change flag and profile. |

The released [compatibility implementation](https://github.com/shayanhabibi/Xantham/blob/500a04a8e67d05d8b0fa481d248aeca1e2e32ee1/src/Xantham.Generator/CatalogCompatibility.fs) uses contract 1, identity 5, API 4, inference 2 and customization 1. Its verified TypeScript-package compiler identity contains exact version, full git revision and AST protocol version; a binary fallback also requires the executable fingerprint. Schema 1 remains stricter about compiler/generator hashes. Ownership, input hashes, API, arity, constraints and customization checks still apply: package version equality alone is insufficient.

Do not interpret this format as a complete DOM member database. `api` is a fingerprint; it cannot reconstruct member names, overloads, parameter or return types. A catalogue could supply authenticated identity/name/arity/provenance facts while declaration parsing, compiler semantics or another structured source supplies members. This published package contains no catalogue whose coverage could be counted or sampled.

## Inputs and module placement

The released [Core.TS generation configuration](https://github.com/shayanhabibi/Xantham/blob/500a04a8e67d05d8b0fa481d248aeca1e2e32ee1/tools/fable-core-ts-input/xantham.json) requests `lib: ["esnext", "dom"]`, ships `typescript/lib`, and places output under `Fable.Core.TS.Es` and `Fable.Core.TS.Dom` (`Es` auto-open; `Dom` not auto-open). The [input entry](https://github.com/shayanhabibi/Xantham/blob/500a04a8e67d05d8b0fa481d248aeca1e2e32ee1/tools/fable-core-ts-input/entry.d.ts) is `export {};`. The release [repository compiler pin](https://github.com/shayanhabibi/Xantham/blob/500a04a8e67d05d8b0fa481d248aeca1e2e32ee1/package.json) is `typescript` **7.1.0-dev.20260902.1**. The package README describes esnext/DOM inputs, but the nupkg has no catalogue source manifest independently attesting exact input bytes/compiler revision. Treat the repository pin as build-input evidence, not a recovered payload field.

## Transport, discovery and public API boundary

The release [configuration model](https://github.com/shayanhabibi/Xantham/blob/500a04a8e67d05d8b0fa481d248aeca1e2e32ee1/src/Xantham.Generator/Model.fs) exposes `DeclarationCatalog`, `DeclarationCatalogCompression` and **`DeclarationReferences: string list`**. Generation can enable `declarationCatalog` with Brotli compression. References are filesystem paths relative to the input package or absolute; the loader resolves those paths with `Path.Combine`/`GetFullPath`.

The [Brotli transport](https://github.com/shayanhabibi/Xantham/blob/500a04a8e67d05d8b0fa481d248aeca1e2e32ee1/src/Xantham.Generator/CatalogTransport.fs) is an **internal** module, recognizes `.br`, parses JSON through bounded decompression, and caps decoded JSON at 128 MiB. `DeclarationCatalog.Catalog` and its declaration/source/owner records are public, and `DeclarationCatalog.apply` participates in generation. The authenticated `load` function is private; validation/application helpers are internal. The inspected released catalogue implementation offers **no public standalone NuGet catalogue discovery/resolver API**: neither package-ID references nor restored-project package resolution are implemented by this file-reference path.

[Generator 0.2.0 changelog](https://github.com/shayanhabibi/Xantham/blob/500a04a8e67d05d8b0fa481d248aeca1e2e32ee1/src/Xantham.Generator/CHANGELOG.md) confirms portable compatibility and Brotli transport shipped. [Issue #110](https://github.com/shayanhabibi/Xantham/issues/110) proposes `xantham/catalogue.json` and `xantham/declarations.json.br`, project-based discovery and a packing helper. Those proposed paths are **not present in the inspected published artifacts**. Although the issue closed on 2026-10-11, its closure has no linked closing commit in the timeline; [PR #114](https://github.com/shayanhabibi/Xantham/pull/114) explicitly distinguishes NuGet discovery/packing as a separate implementation slice. Issue status is therefore not evidence that those artifacts or APIs shipped.

## Consequence for Ranvier planning

Retain an adapter boundary that can consume authoritative Xantham declaration identity/name/arity/provenance when a catalogue-bearing package becomes available. Do not commit to a package path, descriptor schema, complete DOM member coverage or public resolver that the current artifacts do not supply. The immediate evidence gate is a new published nupkg containing its descriptor/payload, followed by schema/provenance validation and a small DOM identity sample against the actual binding assembly. If the user has a newer or different feed artifact, inspect that exact nupkg and supersede this snapshot; do not conflate a local generated catalogue with a released package.
