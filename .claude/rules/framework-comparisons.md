## Framework comparisons

Every comparison with Solid — a benchmark, a behaviour probe, or a claim in prose — is measured
against Solid 2, including a release candidate when Solid 2 has no stable release. Solid 1.x
results are history: cite them only as earlier measurements, never as Solid's current behaviour.

Build Solid 2 with its native compiler, `@solidjs/compiler`, at the same version as `solid-js`
and `@solidjs/web`. Its Babel preset can lag the runtime: `babel-preset-solid@2.0.0-rc.2` emits
`$$click` delegated handlers that the 2.0.0-rc.14 runtime does not read, so clicks never fire.

Solid 2 batches writes. A benchmark or probe calls `flush()` after each write it times or
observes, so the DOM work falls inside the measurement.
