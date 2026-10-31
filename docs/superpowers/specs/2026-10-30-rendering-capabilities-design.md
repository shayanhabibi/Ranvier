# H07 rendering capability observations

Tracking: https://github.com/ranvier-org/Ranvier.WebCore/issues/112.
This is the complete proposed contract for root review, before behavior implementation.

## Intent and scope

Shared applications need current capability observations without equating an IDL declaration,
an exported function or a build option with a usable feature. H07 supplies a shared, extensible
observation module and native/browser adapters. Its selected scope covers current ABI feature
groups, renderer settings, image/font support queries and every platform/stub facility described
by the current native profile. It remains upstream of G01/G04/R01/B06/B09/B10.

Discovery is passive. It never changes settings, starts playback, requests permission, opens a
device, acquires a GPU/context, installs platform callbacks or enables a deferred subsystem.
Format support means the provider's format-support query result; it does not certify decoding,
encoding quality or rendering arbitrary data. Unobservable facts remain explicitly unknown.

## Shared interface

The proposed new shared file is `src/Ranvier.Dom/Capabilities.fs`, also packed as Fable source.
`src/Fable.Ranvier.Dom/BrowserCapabilities.fs` supplies the browser adapter. All public data is
strongly typed and AOT-safe, with no reflection dispatch, arbitrary objects or dynamic JSON.

- `CapabilityCategory`: ABI, RendererSetting, ImageFormat, FontFormat, PlatformFacility.
- `CapabilityKey`: category plus a nonempty name. NUL/control characters are rejected. ABI
  names retain their case; MIME/font names use their category's ASCII normalization rules.
- `CapabilityFact`: Yes, No or Unknown, with a reason and source observations.
- `CapabilityValue`: Boolean, finite Number, Text or copied Names; absent when unobservable.
- `CapabilityObservation`: key, independent Exported/Implemented/Enabled/Permitted/Available
  facts, optional current value, provider identity and mechanism/scope provenance.
- `CapabilityProvider`: stable ID, known keys and a callback accepting a key/cancellation token
  and returning a task with an optional observation.
- `CapabilityService`: `KnownKeys()`, `Observe(key, cancellationToken)` returning a task,
  `Register(provider)` returning an owned disposable registration, and disposal.

An injected pure registry implements the same interface used by adapters and deterministic tests.
Registration order is stable; duplicate provider IDs are rejected. Contributions are combined per
dimension: agreeing known facts remain known, all-Unknown contributions produce Unknown, and
contradictory known facts produce Unknown with conflict evidence. Both sides of a contradiction
and their provenance are retained; absence is never silently converted to No.
No dimension is promoted from another dimension. In particular, Exported=Yes never establishes
Implemented=Yes, and implemented/enabled/permitted facts never manufacture Available=Yes.
Unknown keys return an all-unknown observation, rather than a false unsupported claim.

Snapshot arrays and Names values are copied. Reads use the current provider set and current
runtime queries, without a persistent truth cache. A retained snapshot is historical data.
Registration disposal removes that provider; service disposal invalidates later operations.

## Errors, cancellation and ownership

Malformed keys/provider records fail before callbacks or native calls. After argument validation,
pre-cancellation completes as cancellation without invoking providers; a disposed or inactive
target fails explicitly. A cancellation after work starts prevents publication of a late report.
Provider failures become unknown observations carrying the provider failure reason, allowing
other providers to report; lifecycle/affinity failures are not swallowed into Unknown.

Native adapters borrow a live host and validate its creating thread before reading native state.
Synchronous native observations complete directly. Provider callbacks start on that thread; pure
aggregation may complete elsewhere, but the native adapter explicitly dispatches validation and
publication through the existing `WebCoreHost.PostToOwner` queue, drained by `Cycle`. It does not
assume a captured SynchronizationContext. The existing `Disposed` event invalidates pending tasks
immediately, without waiting for a loop that has stopped. Late completion checks invalidation
before queuing and before publishing. Cancellation and disposal therefore cannot publish a late
report or hang waiting for a destroyed host. Callers keep the host loop running and do not block
its thread awaiting an incomplete task. No native handles are returned or retained by queries.
Browser adapters borrow the actual document realm and reject old providers after document
replacement. No global Window substitution is permitted.

Font-format queries on native hosts must use the current native document for each query, not a
captured document wrapper. Existing C03 CSS queries are reused only after confirming that route;
otherwise the new native getter will perform the current-document query directly.

## Native introspection

Owner: this lane, in `C:/wk/native-capabilities`, starting frozen Native18
`9b6ad4445dcce0124008d1033eb7469355be4cf2`. Only host ABI/header, a bounded catalogue/helper,
managed declarations and producer tests change. `C:/wk/src` remains root-owned and frozen.

The proposed additive C ABI has bounded category/name enumeration and read-only fact/value
queries. Facts are an out-parameter (-1 Unknown,0 No,1 Yes), separate from ordinary status codes.
Value kinds are Absent=0,Boolean=1,Number=2,Text=3. Boolean uses the numeric getter with exactly
0/1, retaining a distinct kind from Number0. Empty text remains Text with length0, not Absent.
The shared Names case is for registry contributions/catalogues; the native wire enumerates names
with count/name_at rather than encoding an array in text. Wrong-kind numeric/text getters return
unsupported status and leave every output untouched. UTF-8 lengths and required capacity count
exact bytes, excluding any terminator; getters copy no NUL, following the existing string ABI.
Size queries report the required length; undersized buffers remain untouched.
Unknown well-formed names succeed with Unknown; invalid enums, null outputs, wrong thread and
invalid hosts use existing ABI error conventions. No live host state is changed by a query.

The exact proposed entry points are:

- `rv_capability_count(category)` and `rv_capability_name_at(category,index,buffer,capacity,length)`.
- `rv_host_capability_fact(host,category,name,dimension,fact)`.
- `rv_host_capability_value_kind(host,category,name,kind)`.
- `rv_host_capability_value_number(host,category,name,number)`.
- `rv_host_capability_value_text(host,category,name,buffer,capacity,length)`.

`rv_abi_has("capabilities")` advertises this introspection entry surface. ABI advertisements
remain separate from feature behavior. For legacy `rv_abi_has`,0 is not sufficient evidence of
Implemented=No: arbitrary names are Unknown, and a recognized feature becomes No only when an
authoritative compiled-out/stub fact supports it. `dom:<Interface>` reports entry-surface presence,
not that every possible behavior of the interface is usable.

The ABI catalogue includes all existing advertised host groups and generated `dom:<Interface>`
names from the canonical model; a narrow catalogue generator/check never edits the DOM generator
or its canonical/generated output. The final SVG19 model must be retained after forward integration.

Renderer setting/value coverage is the complete set currently set by host creation or supported
host rendering controls: script enabled, VM-entry guard, scroll animation, accelerated and forced
compositing/current mode, automatic image loading, media enabled, datalist, download attribute,
font-face metric overrides, anchor positioning, device scale and viewport/paint size. Relevant
font-loading/WebGL/WebGPU settings are observed when present; compiled-out settings are identified
as such instead of calling absent getters. Actual getters read current Page settings/host fields.

Build/stub observations cover current `config/common,slim,gpu,mac,wpe.cmake` facilities: native
compositing/presentation; ANGLE/GPU; video/audio/media stream/recorder/codecs; WebGL/WebGPU/WebXR;
network/resource callbacks; crypto (digest support distinguished from disabled algorithms);
SQLite/IndexedDB/cache/service/shared workers/WebSocket/WebTransport/filesystem/push; inspector
and remote inspector; JSC inspector/JIT/WebAssembly; accessibility/UIA/notifications; clipboard,
drag/drop, popups, tooltip, input/IME/frame/scroll; and port facilities explicitly disabled by WPE.
The catalogue is generated/checked against current profile switches so unknown future switches
are reported rather than silently interpreted as enabled. Compile flags come from the loaded
producer's compiled configuration, never an OS-name inference or an unchecked external manifest.

Live callback registration is reported as a current value/enablement observation; it does not
certify the callback's external implementation or permission. Hardware readiness and application
permission remain Unknown unless an actual provider supplies those facts. A compiled-in Cocoa
video implementation with `mediaEnabled=false` differs from compiled-out Windows/Linux media.

Native image queries call exported `MIMETypeRegistry` format-support methods rather than an
extension table. Native font-format queries reuse C03 `font-format(...)`: the pinned parser's
`CSSSupportsParser.cpp:179-188` calls `FontCustomPlatformData::supportsFormat`. This avoids a new
font subsystem or unexported symbol. If another required getter is unexported, send root the exact
source/export request before changing anything in WebCore.

## Browser observations

The browser adapter queries the active document realm. API/property presence is only Exported.
Image support uses the actual realm's `ImageDecoder.isTypeSupported` when available; its result
is specifically the format-support query, not a decode/render guarantee. If that query is absent,
HTML image support is not guessed from extensions: unsupported observations remain Unknown.
Font formats/technologies use actual CSS supports queries with an observable-query capability
check. Known MIME/font queries include PNG/JPEG/GIF/BMP/ICO/WebP/AVIF/JXL/SVG and TrueType/OpenType/
WOFF/WOFF2/collections/variation forms, while well-formed future names can be queried or extended.

Native-only build settings/ABI are explicitly unobservable on a browser. Permission Policy and
the Permissions API may provide passive policy observations; unsupported permission descriptors,
prompt states and unavailable APIs remain Unknown rather than granted/denied guesses. WebGL/WebGPU
presence cannot establish availability because discovery does not create contexts/request adapters.
Future providers can add configuration-specific media, notification, wake-lock and device facts.

## Verification and delivery

1. Deterministic shared registry tests cover independent facts, conflicts, unknown/recognized
   names, malformed input, copied snapshots, registration order/disposal, cancellation and late
   provider completion. Prove pure logic in an owned SageFs session before persisting it.
2. Native producer probes enumerate every catalogue entry and compare exact ABI/model/profile
   coverage; query live settings before/after an explicitly separate existing setter in tests;
   verify media/stub negatives, image/font query results, UTF-8 buffers, null/error/affinity and
   teardown. Query paths themselves must not mutate state or allocate GPU/platform resources.
3. Actual Chromium and native JIT/real NativeAOT run the shared fixture plus context/disposal/
   cancellation/registration tests. Compare support-query outputs with their real provider APIs.
   Hash all native runtime and declared managed assets before each run and verify unchanged after.
4. Forward-merge final NativeSVG19/ABI27 into Native20 and final managed SVG21/Views11 into the
   managed H07 trees. Preserve every prior DOM/engine fix and run appropriate final integrated
   full conformance, normal/traced tests, all configured analyzers, FCS/API, formatting/XML/comment
   and package-consumer gates once. No unrelated H02 reruns or paused feature enablement.
5. Allocation follows root's fresh06:37:40UTC check: Native20/ABI28, Core22/Views0.1.12/backendalpha12,
   separately committed only after this full plan review. Native runtime versions are independent
   of managed surface versions. PRs/official publication/platform consumption remain distinct from
   API-group acceptance; issue112 stays open until the complete selected scope is audited.

## Remaining implementation details

Exact callback/member names and C++ compile guards are checked while implementing against the
frozen producer; no future facility is inferred from a declaration. Browser settings/permission
facts lacking a passive observation are intentionally Unknown. Font-format and MIME-support
queries do not become a claim that arbitrary content decodes or renders. No other scope decision
or missing renderer/profile category is deferred to a later group.
