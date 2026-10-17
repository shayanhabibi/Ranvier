import argparse
import gzip
import io
import json
import os
import pathlib
import re
import shutil
import subprocess
import tarfile
import tempfile


def run(command, cwd, log):
    environment = dict(os.environ, RanvierCounters="false", RanvierTrace="false")
    result = subprocess.run(["rtk", "proxy", *command], cwd=cwd, env=environment, stdout=log, stderr=subprocess.STDOUT)
    if result.returncode:
        raise RuntimeError(f"Command exited {result.returncode}: {command}; see {log.name}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--work", type=pathlib.Path, default=pathlib.Path(tempfile.gettempdir()) / "ranvier-instruction-erosions-reproduction")
    parser.add_argument("--repeat", type=int, default=3)
    parser.add_argument("--trace", action="store_true")
    args = parser.parse_args()
    repo = pathlib.Path.cwd()
    scripts = pathlib.Path(__file__).parent
    work = args.work.resolve()
    work.mkdir(parents=True, exist_ok=True)
    variants = ["59da758-parent", "59da758", "878f294", "2ef3876", "no-guard"]
    for name, commit in zip(variants, ["59da758^", "59da758", "878f294", "2ef3876"]):
        target = work / name
        if target.exists():
            raise RuntimeError(f"Use a new --work directory; snapshot already exists: {target}")
        target.mkdir()
        roots = subprocess.check_output(["rtk", "proxy", "git", "ls-tree", "--name-only", commit], cwd=repo, text=True).splitlines()
        paths = ["src", "fable/Ranvier.Counters", "bench/Ranvier.Counters"]
        paths += [p for p in ["Directory.Build.props", "Directory.Build.targets", "global.json", "NuGet.Config", ".config"] if p in roots]
        archive = subprocess.check_output(["rtk", "proxy", "git", "archive", commit, *paths], cwd=repo)
        with tarfile.open(fileobj=io.BytesIO(archive)) as contents:
            contents.extractall(target, filter="data")
        (target / "package.json").write_text('{"type":"module"}\n', encoding="utf-8")
        with (work / f"build-{name}.log").open("w", encoding="utf-8") as log:
            run(["dotnet", "tool", "restore"], target, log)
            run(["dotnet", "fable", "fable/Ranvier.Counters", "-e", ".fs.js", "-o", "fable/Ranvier.Counters/output/plain", "--noCache", "--configuration", "Release"], target, log)
        print(f"Built {commit}", flush=True)

    bundle = work / "2ef3876/fable/Ranvier.Counters/output/plain"
    target = work / "no-guard"
    shutil.copytree(bundle, target)
    (target / "package.json").write_text('{"type":"module"}\n', encoding="utf-8")
    core = target / "src/Ranvier/Core.fs.js"
    original = core.read_text(encoding="utf-8")
    pattern = r"export function Graph__AssertOnGraphThread_Z721C83C5\(this\$, operation\) \{.*?\n\}"
    changed, count = re.subn(pattern, "export function Graph__AssertOnGraphThread_Z721C83C5(this$, operation) {}", original, flags=re.S)
    assert count == 1
    core.write_text(changed, encoding="utf-8")

    for repeat in range(args.repeat):
        for scenario in ["cutoff", "chain", "update", "dispose", "create"]:
            for name in (variants if repeat % 2 == 0 else list(reversed(variants))):
                bundle = work / name
                if name != "no-guard":
                    bundle /= "fable/Ranvier.Counters/output/plain"
                output = work / f"timing-{name}-{scenario}-{repeat}.json"
                scale = "4" if scenario == "cutoff" else "1"
                with (work / "measurement.log").open("a", encoding="utf-8") as log:
                    run(["node", "--expose-gc", "--single-threaded", str(scripts / "measure.mjs"), str(bundle), scenario, str(output), scale], repo, log)
                print(name, scenario, repeat, flush=True)

    if args.trace:
        summaries = []
        for name in ["878f294", "2ef3876"]:
            bundle = work / name / "fable/Ranvier.Counters/output/plain"
            for scale, warmup in [(1, 10), (8, 40)]:
                path = work / f"trace-{name}-create-scale{scale}-warm{warmup}.log"
                with path.open("w", encoding="utf-8") as log:
                    run(["node", "--expose-gc", "--single-threaded", "--trace-opt", "--trace-deopt", str(scripts / "audit-regions.mjs"), str(bundle), "create", str(scale), str(warmup)], repo, log)
                regions = []
                text = path.read_text(encoding="utf-8")
                for match in re.finditer(r"MEASURE-BEGIN ([^\n]+)\n(.*?)MEASURE-END ([^\n]+)", text, re.S):
                    events = [line for line in match[2].splitlines() if any(k in line for k in ["compiling method", "bailout", "marking "])]
                    regions.append({"region": match[1], "end": match[3], "compilations": sum("compiling method" in e for e in events), "bailouts": sum("bailout" in e for e in events), "events": events})
                summaries.append({"file": path.name, "regions": regions})
                path.with_suffix(".log.gz").write_bytes(gzip.compress(text.encode(), mtime=0))
        (work / "region-audit-summary.json").write_text(json.dumps(summaries, indent=2), encoding="utf-8")
    print(f"Results: {work}", flush=True)


if __name__ == "__main__":
    main()
