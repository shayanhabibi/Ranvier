/// <summary>
/// The worker's measurements, as the JSON the driver reads.
/// </summary>
namespace CounterBench

type CounterValue = { Name: string; Value: int64 }

/// <summary>
/// One measured region: <c>Ops</c> operations of a case, bracketed by the markers
/// numbered <c>Region</c>.
/// </summary>
type Measurement =
    {
        Ops: int
        Region: int
        /// <summary>
        /// <c>GC.GetAllocatedBytesForCurrentThread</c> after the operations minus
        /// before them.
        /// </summary>
        Bytes: int64
        /// <summary>
        /// Gen-0 collections during the region. Zero when <c>NoGcRegion</c> is true.
        /// </summary>
        Gen0Collections: int
        /// <summary>
        /// True when the region ran inside <c>GC.TryStartNoGCRegion</c> for its whole
        /// length.
        /// </summary>
        NoGcRegion: bool
        /// <summary>
        /// <c>Ranvier.Counters.Snapshot()</c> after the region. Empty when
        /// the library was built without <c>RanvierCounters</c>.
        /// </summary>
        Counters: CounterValue[]
    }

type CaseRun =
    {
        Scenario: string
        Engine: string
        Unit: string
        /// <summary>
        /// The measurement at N, then the measurement at 2N.
        /// </summary>
        Measurements: Measurement[]
    }

type WorkerRun =
    {
        ProcessId: int
        /// <summary>
        /// The native id of the thread every region ran on.
        /// </summary>
        ThreadId: int
        CountersCompiled: bool
        Cases: CaseRun[]
    }
