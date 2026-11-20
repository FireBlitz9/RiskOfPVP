# Ultra bot benchmarks

The previous build is RiskOfPVP v1.3.1 at commit `53b5fc90b4e8191c7f5fc14ee07b63725d0ae8a0`. The upgraded build is v1.3.2. The [raw results](results.json) include each expected outcome, every timing and allocation sample, and the SHA-256 hashes of the compiled source inputs.

- **Decision checks** exercise the shared fire gates, hill goal, movement guards, reaction requests and production item chooser with scripted inputs. The cases were chosen to cover the Ultra changes and common guardrails. The counts apply to this checklist.
- **Reward scoring time** is the median of 28 batches at each offer size, with 900 evaluations per batch. Each build runs in four separate processes, alternating which build runs first. Each workload warms up for 900 evaluations. Both builds receive the same twelve rotating offers per size, the same synthetic catalog, and the same two-player roster. Each batch records wall time and a choice checksum.
- **Allocated memory** is the median managed bytes allocated per offer on the scoring thread in those batches, measured using `GC.GetAllocatedBytesForCurrentThread`. It measures allocation traffic.

The benchmark compiles the real C# rule and reward-selection sources with the repository's existing test adapters. Unity, pathfinding, map physics, actual projectiles, multiplayer frame time, and player win rates are outside this benchmark. Results for another machine may differ. Timing and allocation figures describe warmed reward scoring at the displayed offer sizes.

On Windows with the repository's build dependencies restored, regenerate with:

```powershell
python benchmarks/run.py
python benchmarks/plot.py
```

The runner uses the repository's pinned Roslyn compiler and 64-bit .NET Framework. The figure requires Matplotlib and NumPy. The previous commit must be available locally.
