namespace CounterBench

/// <summary>A finite per-operation differential; unavailable metrics are omitted.</summary>
type Metric = { Name: string; Value: float }

/// <summary>A named version, warning, or unavailable-baseline reason.</summary>
type Label = { Name: string; Value: string }

/// <summary>One scenario and engine, with flags for collection and compilation in either measured region.</summary>
type ScenarioFigures =
    {
        Scenario: string
        Engine: string
        Gc: bool
        /// <summary>V8 compiled inside either region. Always false for .NET, and in files that predate the field.</summary>
        Compiled: bool
        Metrics: Metric[]
    }

/// <summary>One process of a side (<c>base</c> or <c>head</c>) and runtime (<c>node</c> or <c>dotnet</c>).</summary>
type ProcessFigures =
    {
        Side: string
        Commit: string
        Runtime: string
        Process: int
        Scenarios: ScenarioFigures[]
    }

/// <summary>Alternating process measurements and separate instrumented library counters.</summary>
type AbRun =
    {
        Leg: string
        Machine: string
        Base: string
        Head: string
        Rounds: int
        Versions: Label[]
        NoBaseline: Label[]
        Processes: ProcessFigures[]
        Counters: ProcessFigures[]
    }
