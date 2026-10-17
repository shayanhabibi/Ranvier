import json
import pathlib
import statistics
import subprocess
import sys

before, after, destination = map(pathlib.Path, sys.argv[1:])
destination.mkdir(parents=True, exist_ok=True)
harness = pathlib.Path(__file__).with_name('measure.mjs')
results = []
for repeat in range(3):
    for scenario in ['cutoff', 'chain', 'update', 'dispose', 'create']:
        variants = [('before', before), ('after', after)]
        if repeat % 2:
            variants.reverse()
        for label, directory in variants:
            output = destination / f'{label}-{scenario}-{repeat}.json'
            command = ['rtk', 'proxy', 'node', '--expose-gc', '--single-threaded', str(harness), str(directory.resolve()), scenario, str(output.resolve()), '4' if scenario == 'cutoff' else '1']
            run = subprocess.run(command, capture_output=True, text=True)
            if run.returncode:
                raise RuntimeError(run.stdout + run.stderr)
            data = json.loads(output.read_text())
            results.append({'label': label, 'repeat': repeat, **data})
            print(label, repeat, scenario, data['summary'], flush=True)
            (destination / 'samples.json').write_text(json.dumps(results, indent=2))
summary = []
for scenario in ['cutoff', 'chain', 'update', 'dispose', 'create']:
    rows = {}
    for label in ['before', 'after']:
        subset = [r for r in results if r['label'] == label and r['scenario'] == scenario]
        rows[label] = {engine: statistics.median(r['summary'][engine] for r in subset) for engine in ['Ranvier', 'Fable.Ripple']}
    summary.append({'scenario': scenario, **rows, 'ratio': rows['after']['Ranvier']/rows['before']['Ranvier'], 'normalizedRatio': (rows['after']['Ranvier']/rows['after']['Fable.Ripple'])/(rows['before']['Ranvier']/rows['before']['Fable.Ripple'])})
(destination / 'summary.json').write_text(json.dumps(summary, indent=2))
print(json.dumps(summary, indent=2), flush=True)
