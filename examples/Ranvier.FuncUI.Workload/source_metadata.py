import hashlib
import json
import platform
import subprocess
from pathlib import Path


def command(arguments, directory):
    return subprocess.check_output(["rtk", "proxy", *arguments], cwd=directory, text=True).strip()


root = Path(__file__).resolve().parents[2]
funcui = root.parent / "Avalonia.FuncUI"
files = [*root.glob("src/**/*.fs"), *root.glob("src/**/*.fsproj"), *root.glob("src/**/*.props"),
         *root.glob("*.props"), *root.glob("*.targets"), *root.glob("global.json"),
         *root.glob("examples/Ranvier.FuncUI.Workload/*.props"),
         *root.glob("examples/Ranvier.FuncUI.Workload/*.fs"), *root.glob("examples/Ranvier.FuncUI.Workload/*.fsproj"),
         *funcui.glob("src/Avalonia.FuncUI/**/*.fs"), *funcui.glob("src/Avalonia.FuncUI/*.fsproj"),
         *funcui.glob("*.props"), *funcui.glob("*.targets"), *funcui.glob("global.json")]
hashes = {}
for file in sorted(files):
    if "bin" not in file.parts and "obj" not in file.parts:
        hashes[str(file.relative_to(root) if file.is_relative_to(root) else Path("../Avalonia.FuncUI") / file.relative_to(funcui))] = hashlib.sha256(file.read_bytes()).hexdigest()
metadata = {
    "RanvierHead": command(["git", "rev-parse", "HEAD"], root),
    "RanvierStatus": command(["git", "status", "--porcelain"], root),
    "FuncUIHead": command(["git", "rev-parse", "HEAD"], funcui),
    "FuncUIStatus": command(["git", "status", "--porcelain"], funcui),
    "Sdk": command(["dotnet", "--version"], root),
    "Cpu": command(["pwsh", "-NoProfile", "-Command", "(Get-CimInstance Win32_Processor).Name"], root),
    "Architecture": platform.machine(),
    "Dependencies": {"Elmish": "5.0.2", "Avalonia": "12.1.0", "Avalonia.FuncUI": "local checkout", "Ranvier.Elmish": "local project, RanvierTrace=false"},
    "SourceSha256": hashes,
    "Command": "dotnet run --project examples/Ranvier.FuncUI.Workload/Workload.fsproj -c Release -p:RanvierTrace=false -- --measure --output examples/Ranvier.FuncUI.Workload/results/raw.json",
    "RecordedInvocation": "rtk proxy dotnet run --project examples/Ranvier.FuncUI.Workload/Workload.fsproj -c Release --no-build -- --measure --output examples/Ranvier.FuncUI.Workload/results/raw.json",
}
output = root / "examples/Ranvier.FuncUI.Workload/results/source.json"
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(metadata, indent=2), encoding="utf-8")
print(f"Recorded {len(hashes)} source hashes in {output}")
