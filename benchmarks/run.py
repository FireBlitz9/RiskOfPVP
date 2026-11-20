"""Compile both Ultra implementations against the repository's offline adapters."""

import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import platform
import statistics
import subprocess
import tempfile
from zipfile import ZipFile


BENCHMARKS = Path(__file__).resolve().parent
ROOT = BENCHMARKS.parent
BASELINE = "53b5fc90b4e8191c7f5fc14ee07b63725d0ae8a0"
PARTS = [
    "src/ArenaRules.cs", "src/PickupRules.cs", "src/BotTacticsRules.cs",
    "src/BotBuildRules.cs", "src/BotActionRules.cs", "src/ItemPool.cs",
    "src/ArenaBotBuilds.cs", "src/ArenaBotActions.cs",
    "tests/ItemPoolTests.cs", "tests/BotActionsTests.cs",
]


def command(args, **kwargs):
    return subprocess.run(args, cwd=ROOT, check=True, **kwargs).stdout


def compile_runner(compiler, root, program, upgraded):
    files = [root / part for part in PARTS]
    flags = ["/define:UPGRADED"] if upgraded else []
    if upgraded:
        files.append(root / "src" / "BotAdaptation.cs")
    command([str(compiler), "/nologo", "/optimize+", "/langversion:latest",
             "/main:UltraBenchmarks", "/platform:x64", f"/out:{program}",
             *flags, str(BENCHMARKS / "UltraBenchmarks.cs"), *map(str, files)])


def read_run(program, order):
    output = command([str(program), str(order)], stdout=subprocess.PIPE, text=True)
    samples, scenarios = [], []
    runtime = None
    for line in output.splitlines():
        kind, *values = line.split("\t")
        if kind == "CHECK":
            group, label, result = values
            scenarios.append({"group": group, "label": label, "passed": result == "1"})
        elif kind == "PERF":
            size, batch, ms, allocated, iterations, checksum = values
            samples.append(dict(offer_size=int(size), batch=int(batch), elapsed_ms=float(ms),
                                allocated_bytes=int(allocated), iterations=int(iterations), checksum=int(checksum)))
        elif kind == "RUNTIME":
            runtime = values[0]
    if len(samples) != 21 or not scenarios or not runtime:
        raise ValueError(f"Incomplete benchmark output from {program}")
    return scenarios, samples, runtime


def revision(root, parts):
    return {part: hashlib.sha256((root / part).read_bytes()).hexdigest() for part in parts}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", default=BASELINE)
    parser.add_argument("--output", type=Path, default=BENCHMARKS / "results.json")
    args = parser.parse_args()
    compiler = ROOT / ".build/deps/compiler/tasks/net472/csc.exe"
    if not compiler.is_file():
        parser.error("Roslyn was not found. Restore the repository build dependencies with build.ps1 first.")
    baseline = command(["git", "rev-parse", f"{args.baseline}^{{commit}}"], capture_output=True, text=True).strip()
    with tempfile.TemporaryDirectory(prefix="ultra-bench-", dir=ROOT / ".build") as temp:
        scratch = Path(temp)
        original = scratch / "previous"
        archive = command(["git", "archive", "--format=zip", baseline, *PARTS], capture_output=True)
        with ZipFile(io.BytesIO(archive)) as files:
            files.extractall(original)
        versions = {
            "previous": {"source": baseline, "files": revision(original, PARTS), "samples": []},
            "upgraded": {"source": "v1.3.2", "files": revision(ROOT, [*PARTS, "src/BotAdaptation.cs"]), "samples": []},
        }
        compile_runner(compiler, original, scratch / "previous.exe", False)
        compile_runner(compiler, ROOT, scratch / "upgraded.exe", True)
        for order in range(4):
            names = list(versions) if order % 2 == 0 else list(reversed(versions))
            for name in names:
                cases, samples, runtime = read_run(scratch / f"{name}.exe", order)
                version = versions[name]
                if "scenarios" in version and cases != version["scenarios"]:
                    raise ValueError(f"Nondeterministic scenario results in {name}")
                version["scenarios"] = cases
                version["samples"].extend(dict(run=order, **sample) for sample in samples)
        if [(c["group"], c["label"]) for c in versions["previous"]["scenarios"]] != [
                (c["group"], c["label"]) for c in versions["upgraded"]["scenarios"]]:
            raise ValueError("Scenario definitions differ between generations")
        for version in versions.values():
            version["metrics"] = {}
            for size in (24, 96, 600):
                samples = [s for s in version["samples"] if s["offer_size"] == size]
                version["metrics"][size] = {
                    "median_us": statistics.median(s["elapsed_ms"] * 1000 / s["iterations"] for s in samples),
                    "median_bytes": statistics.median(s["allocated_bytes"] / s["iterations"] for s in samples),
                    "batches": len(samples), "offers": sum(s["iterations"] for s in samples),
                }
        result = {
            "workload": "Production Ultra rules and reward chooser with deterministic catalog and inventory adapters",
            "platform": platform.platform(), "cpu": os.environ.get("PROCESSOR_IDENTIFIER", platform.processor()),
            "runtime": runtime, "versions": versions,
            "harness_sha256": hashlib.sha256((BENCHMARKS / "UltraBenchmarks.cs").read_bytes()).hexdigest(),
        }
        args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
