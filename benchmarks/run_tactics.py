"""Run the same deterministic decision sequences on Ultra V1, V2 and V3."""

import argparse
import hashlib
import io
from itertools import permutations
import json
import math
from pathlib import Path
import statistics
import subprocess
import tempfile
from zipfile import ZipFile

from run import (
    BENCHMARKS, ROOT, PARTS as REWARD_PARTS, command, revision,
    compile_runner as compile_rewards, read_run as read_rewards, summarize_rewards,
)


V1_COMMIT = "53b5fc90b4e8191c7f5fc14ee07b63725d0ae8a0"
V2_COMMIT = "167e4a8fe4a8a3d08dff32a4dca5b956ecc1fbf2"
RULES = ["src/" + name + ".cs" for name in
         ("ArenaRules", "PickupRules", "BotTacticsRules", "BotBuildRules")]
HARNESS = BENCHMARKS / "TacticalBenchmarks.cs"


def compile_runner(compiler, source, files, program, generation):
    command([str(compiler), "/nologo", "/optimize+", "/langversion:latest", "/platform:x64",
             f"/out:{program}", f"/define:ULTRA_V{generation}", str(HARNESS),
             *(str(source / path) for path in files)])


def read_run(program, order):
    rows, samples = [], []
    output = command([str(program), str(order)], stdout=subprocess.PIPE, text=True)
    for line in output.splitlines():
        kind, *data = line.split("\t")
        if kind == "CASE":
            group, seed, success, metric = data
            rows.append(dict(group=group, seed=int(seed), success=success == "1", metric=float(metric)))
        elif kind == "PERF":
            bots, batch, ms, ticks, checksum = data
            samples.append(dict(bots=int(bots), batch=int(batch), ms=float(ms), ticks=int(ticks), checksum=int(checksum)))
    if len(rows) != 240 or len(samples) != 21 or len({(r["group"], r["seed"]) for r in rows}) != len(rows):
        raise RuntimeError(f"Incomplete or duplicate results from {program}")
    return rows, samples


def summarize(version):
    grouped = {}
    for row in version["cases"]:
        grouped.setdefault(row["group"], []).append(row)
    version["summary"] = {name: {
        "passed": sum(r["success"] for r in rows), "total": len(rows),
        "mean_metric": statistics.mean(r["metric"] for r in rows),
    } for name, rows in grouped.items()}
    version["timing"] = {}
    for count in (1, 8, 24):
        batches = sorted(s["ms"] * 1000 / s["ticks"] for s in version["samples"] if s["bots"] == count)
        version["timing"][count] = {
            "median_us_per_tick": statistics.median(batches),
            "p95_us_per_tick": batches[math.ceil(len(batches) * .95) - 1],
            "batches": len(batches),
        }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=BENCHMARKS / "tactical-results.json")
    parser.add_argument("--rewards-output", type=Path, default=BENCHMARKS / "reward-generations.json")
    args = parser.parse_args()
    compiler = ROOT / ".build/deps/compiler/tasks/net472/csc.exe"
    if not compiler.is_file():
        parser.error("Restore the pinned Roslyn compiler with build.ps1 first.")
    with tempfile.TemporaryDirectory(prefix="tactical-bench-", dir=ROOT / ".build") as scratch_name:
        scratch = Path(scratch_name)
        adaptive = [*RULES, "src/BotAdaptation.cs"]
        variants = {
            "Ultra V1": (scratch / "v1", RULES, V1_COMMIT),
            "Ultra V2": (scratch / "v2", adaptive, V2_COMMIT),
            "Ultra V3": (ROOT, [*adaptive, "src/BotBattlePlan.cs"], "working tree"),
        }
        results, rewards = {}, {}
        programs, reward_programs = {}, {}
        for generation, (label, (root, files, source)) in enumerate(variants.items(), start=1):
            reward_files = list(REWARD_PARTS)
            if generation >= 2:
                reward_files.append("src/BotAdaptation.cs")
            if generation == 3:
                reward_files.append("src/BotBattlePlan.cs")
            if source != "working tree":
                archive_paths = sorted(set(files) | set(reward_files))
                with ZipFile(io.BytesIO(command(["git", "archive", "--format=zip", source, *archive_paths], stdout=subprocess.PIPE))) as archive:
                    archive.extractall(root)
            programs[label] = scratch / f"ultra-v{generation}.exe"
            reward_programs[label] = scratch / f"rewards-v{generation}.exe"
            compile_runner(compiler, root, files, programs[label], generation)
            compile_rewards(compiler, root, reward_programs[label], generation >= 2)
            results[label] = {"source": source, "files": revision(root, files), "samples": []}
            rewards[label] = {"source": source, "files": revision(root, reward_files), "samples": []}
        for order, names in enumerate(permutations(variants)):
            for name in names:
                rows, samples = read_run(programs[name], order)
                version = results[name]
                if "cases" in version and rows != version["cases"]:
                    raise ValueError(f"Nondeterministic decisions for {name}")
                version["cases"] = rows
                version["samples"].extend(dict(run=order, **s) for s in samples)
            for name in names:
                cases, samples, runtime = read_rewards(reward_programs[name], order)
                version = rewards[name]
                if "scenarios" in version and cases != version["scenarios"]:
                    raise ValueError(f"Nondeterministic reward results for {name}")
                version["scenarios"] = cases
                version["samples"].extend(dict(run=order, **s) for s in samples)
        case_ids = [(r["group"], r["seed"]) for r in results["Ultra V1"]["cases"]]
        for name, version in results.items():
            if [(r["group"], r["seed"]) for r in version["cases"]] != case_ids:
                raise ValueError(f"The matched scenario IDs differ for {name}.")
            summarize(version)
        reward_ids = [(r["group"], r["label"]) for r in rewards["Ultra V1"]["scenarios"]]
        for name, version in rewards.items():
            if [(r["group"], r["label"]) for r in version["scenarios"]] != reward_ids:
                raise ValueError(f"The matched reward scenarios differ for {name}.")
            summarize_rewards(version)
        args.output.write_text(json.dumps({
            "harness_sha256": hashlib.sha256(HARNESS.read_bytes()).hexdigest(),
            "versions": results,
        }, indent=2) + "\n", encoding="utf-8", newline="\n")
        args.rewards_output.write_text(json.dumps({
            "harness_sha256": hashlib.sha256((BENCHMARKS / "UltraBenchmarks.cs").read_bytes()).hexdigest(),
            "runtime": runtime,
            "versions": rewards,
        }, indent=2) + "\n", encoding="utf-8", newline="\n")
        for label, version in results.items():
            print(label)
            for name, metrics in version["summary"].items():
                print(f"  {name}: {metrics['passed']}/{metrics['total']}, mean {metrics['mean_metric']:.3f}")
            for bots, performance in version["timing"].items():
                print(f"  {bots} bots: {performance['median_us_per_tick']:.3f} median, {performance['p95_us_per_tick']:.3f} P95 us/tick")
            reward_data = rewards[label]
            print(f"  Original suite: {sum(c['passed'] for c in reward_data['scenarios'])}/{len(reward_data['scenarios'])}")


if __name__ == "__main__":
    main()
