import json
import statistics
import sys
from pathlib import Path


def percentile(values, fraction):
    values = sorted(values)
    return values[min(len(values) - 1, int((len(values) - 1) * fraction))]


def main():
    source = Path(sys.argv[1])
    raw = json.loads(source.read_text(encoding="utf-8"))
    identity = json.loads(source.with_name("source.json").read_text(encoding="utf-8"))
    rows = []
    for tasks in (100, 1000):
        for workload in ("background", "selected-edit", "search-page", "mixed"):
            for variant in ("elmish", "elmish-cached", "ranvier"):
                trials = [t for t in raw["Trials"] if (t["Tasks"], t["Workload"], t["Variant"]) == (tasks, workload, variant)]
                if len(trials) != 8:
                    raise ValueError(f"Expected eight trials: {tasks}/{workload}/{variant}")
                means = [t["ElapsedMilliseconds"] * 1000 / t["Messages"] for t in trials]
                samples = [v for t in trials for v in t["MessageMicroseconds"]]
                counter = next(c for c in raw["Counters"] if (c["Tasks"], c["Workload"], c["Variant"]) == (tasks, workload, variant))
                row = dict(counter)
                row.update(MeanMicroseconds=statistics.median(means), MinMeanMicroseconds=min(means), MaxMeanMicroseconds=max(means),
                           P50Microseconds=percentile(samples, .5), P95Microseconds=percentile(samples, .95),
                           BytesPerMessage=statistics.median(t["AllocatedBytes"] / t["Messages"] for t in trials),
                           Gen0=sum(t["Gen0"] for t in trials), Gen1=sum(t["Gen1"] for t in trials), Gen2=sum(t["Gen2"] for t in trials))
                rows.append(row)
    result = {"TimestampUtc": raw["TimestampUtc"], "Runtime": raw["Runtime"], "OS": raw["OS"], "Rows": rows}
    source.with_name("summary.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    lines = ["# Elmish and Ranvier.Elmish: task dashboard measurements", "", f"Measured {raw['TimestampUtc']} on {raw['OS']}, {raw['Runtime']}; {raw['ProcessorCount']} logical processors; {identity['Cpu']}.", "",
             f"SDK {identity['Sdk']}. Elmish 5.0.2; Avalonia 12.1.0; FuncUI checkout {identity['FuncUIHead']}; local Ranvier HEAD {identity['RanvierHead']} with tracing disabled. Both checkouts can contain uncommitted changes; source.json records status and SHA-256 hashes of the measured sources.", "",
             "Eight rotated trials of 600 messages after two warmups per scenario. The figures below are the median of trial mean microseconds per message; ranges are the minimum and maximum trial means. P95 pools per-message samples. Allocations cover the UI thread only. Counters are from a separate replay.", "",
             "These are headless Avalonia dispatch-and-drain timings, including real controls/templates; they do not measure GPU presentation, desktop frame rate or human input latency. Construction is excluded. See README.md for reproduction and interpretation.", ""]
    for tasks in (100, 1000):
        lines += [f"## {tasks:,} tasks (50 visible rows)", ""]
        for workload in ("background", "selected-edit", "search-page", "mixed"):
            group = [r for r in rows if r["Tasks"] == tasks and r["Workload"] == workload]
            baseline, cached, ranvier = group
            lines += [f"### {workload}", ""]
            for r in group:
                lines.append(f"- **{r['Variant']}**: {r['MeanMicroseconds']:.1f} µs/message (trial means {r['MinMeanMicroseconds']:.1f}–{r['MaxMeanMicroseconds']:.1f}); pooled p95 {r['P95Microseconds']:.1f} µs; {r['BytesPerMessage']:.0f} bytes/message; GC totals {r['Gen0']}/{r['Gen1']}/{r['Gen2']}. View builds: root {r['RootViews']}, section {r['SectionViews']}, row {r['RowViews']}; host patches {r['HostUpdates']}; selector evaluations {r['SelectorEvaluations']}.")
            lines += ["", f"Ranvier's median trial mean is {baseline['MeanMicroseconds']/ranvier['MeanMicroseconds']:.2f}× faster than uncached Elmish and {cached['MeanMicroseconds']/ranvier['MeanMicroseconds']:.2f}× faster than cached Elmish (a ratio below 1 means slower).", ""]
    bg = [r for r in rows if r["Tasks"] == 1000 and r["Workload"] == "background"]
    ordinary, cached, ranvier = bg
    lines += ["## What the counters explain", "",
              f"For 1,000-task background updates, cached Elmish and Ranvier both construct only {ranvier['RowViews']} row views. Cached Elmish still patches its root {cached['HostUpdates']} times; Ranvier performs {ranvier['HostUpdates']} targeted host patches with no root rebuilds. Ranvier evaluates {ranvier['SelectorEvaluations']:,} selectors, so it does not avoid all propagation work. Its median trial mean is {cached['MeanMicroseconds']/ranvier['MeanMicroseconds']:.2f}× faster than cached Elmish and its UI-thread allocation is {100*(1-ranvier['BytesPerMessage']/cached['BytesPerMessage']):.1f}% lower in this scenario.", "",
              "Selected edits update both a visible row and the detail section. Cached Elmish already reuses the other views, and Ranvier performs several independent host patches per message. Search/paging changes membership and builds many controls in every runner. In the mixed trace, a small number of topology changes contributes much of the measured UI work. The counters show equivalent row construction between cached Elmish and Ranvier; the differences include patch scope, selector overhead and each integration's model equality guard, rather than only the message-loop engine.", "",
              "Ranvier has slightly higher observed median times than cached Elmish for selected edits, search/paging and mixed work at both sizes. Several trial ranges overlap, so small median differences are not evidence of a statistically established advantage. The strong background result applies to mostly offscreen edits in this paged dashboard; the uncached baseline spends much more time and allocation rebuilding views.", "",
              "## Shared update function", "", "The pure pass excludes every runner and all UI work; it is reported separately and is not subtracted from dispatch timings.", ""]
    for tasks in (100, 1000):
        for workload in ("background", "selected-edit", "search-page", "mixed"):
            group = [t for t in raw["PureUpdates"] if t["Tasks"] == tasks and t["Workload"] == workload]
            mean = statistics.median(t["ElapsedMilliseconds"] * 1000 / t["Messages"] for t in group)
            lines.append(f"- {tasks} tasks, {workload}: {mean:.2f} µs/message.")
    lines += ["", "## Scope", "", "All variants share the same immutable update and control topology. Cached Elmish is a deliberate stronger baseline than rebuilding every view. Ranvier trades root rebuilding for selector and ownership costs; search/paging changes row membership, while surviving row owners and cached views remain. FuncUI can replace native hosts when a row moves position, requiring cached views to be remounted. Both Elmish runners use FuncUI's conventional structural model guard; Ranvier uses its graph equality policy. This single-machine synthetic workload is not evidence that every Elmish application becomes faster. Raw trials and message samples are preserved in raw.json.", ""]
    source.with_name("report.md").write_text("\n".join(lines), encoding="utf-8")
    print(f"Wrote {len(rows)} summaries and report beside {source}")


if __name__ == "__main__":
    main()
