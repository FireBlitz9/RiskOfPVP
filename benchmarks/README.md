# Ultra bot benchmarks

## Original release comparison

The original comparison covers RiskOfPVP v1.3.1 (`53b5fc90b4e8191c7f5fc14ee07b63725d0ae8a0`) and v1.3.2 (`167e4a8fe4a8a3d08dff32a4dca5b956ecc1fbf2`). Its results include each expected outcome, every timing and allocation sample, and the SHA-256 hashes of the compiled source inputs. [Original chart](../assets/ultra-benchmarks-1.3.2.png) · [Original raw results](results.json)

- **Decision checks** exercise the shared fire gates, hill goal, movement guards, reaction requests and production item chooser with scripted inputs. The cases were chosen to cover the Ultra changes and common guardrails. The counts apply to this checklist.
- **Reward scoring time** is the median of 28 batches at each offer size, with 900 evaluations per batch. Each build runs in four separate processes, alternating which build runs first. Each workload warms up for 900 evaluations. Both builds receive the same twelve rotating offers per size, the same synthetic catalog, and the same two-player roster. Each batch records wall time and a choice checksum.
- **Allocated memory** is the median managed bytes allocated per offer on the scoring thread in those batches, measured using `GC.GetAllocatedBytesForCurrentThread`. It measures allocation traffic.

The benchmark compiles the real C# rule and reward-selection sources with the repository's existing test adapters. Unity, pathfinding, map physics, actual projectiles, multiplayer frame time, and player win rates are outside this benchmark. Results for another machine may differ. Timing and allocation figures describe warmed reward scoring at the displayed offer sizes.

On Windows with the repository's build dependencies restored, regenerate with:

```powershell
python benchmarks/run.py
python benchmarks/plot.py
```

The runner uses the repository's pinned Roslyn compiler and 64-bit .NET Framework. The figure requires Matplotlib and NumPy. Both published commits must be available locally. Pass `--candidate worktree --output .build/ultra-regression.json` to check current changes without replacing the published figures.

## Tactical sequences

**Ultra V1** is v1.3.1, **Ultra V2** is v1.3.2, and **Ultra V3** is the current candidate. Each runs the same **240 scripted sequences**, with 48 setups per group. Cases run across several decisions. Later setups add second corridor closures, crossfire, hill relocation with travel delays, faster recasts, and changing opponent habits. [Tactics chart](../assets/ultra-tactics-benchmarks.png) · [Raw tactical results](tactical-results.json)

| Group | What passes | Additional measure |
| --- | --- | --- |
| Corner chase | Reach a firing angle by a deadline that at least one route can meet | Seconds stalled |
| Retreat under pressure | Choose cover through a brief loss of sight and follow-up pressure | Seconds of exposed route choices |
| Contested hill lanes | Occupy separate useful lanes after the hill relocates | Distinct in-zone lanes covered |
| Combat timing | Use the sampled opening when ready, avoid early or stale pushes, and retain an escape under pressure | Share of checks passed in the sequence |
| Counter-build over time | Adapt to repeated evasive play, switch to defense against burst, and discard stale evidence | Correct decisions out of four |

All three get the same scripted ground candidates, obstacles, motion, item offers, and clock. Unity's ground graph, raycasts, and skill states are represented by fixture inputs. Hill arrivals use scripted travel times.

- **Ultra V1** compiles v1.3.1's build and hill rules. The harness maps its single preferred position and lateral movement to the nearest scripted ground option. Its build rule receives immediate opponent speed and fixed baseline values for combat evidence.
- **Ultra V2** compiles v1.3.2's engagement, opponent memory, and build rules. The harness reproduces its three-option route score.
- **Ultra V3** compiles the current route scorer, engagement timing, opponent memory, and build rules.

The tactical timing workload runs the scripted controllers for 1, 8, and 24 bots. It reports median and 95th-percentile batch times per tick across 42 batches of 1,800 ticks per bot count and generation. Six process orders balance which generation runs first. Timing includes the harness and isolated decision rules. Unity graph traversal, movement, pathfinding, and rendering are outside scope.

## Reward scoring across generations

The same three generations run the original 27 checks and compile the production C# reward chooser, with simulated inventories and reward offers. Each offer size uses 42 batches of 900 decisions per generation. The charts show median time per offer and median managed memory allocated per offer, using the same rotating offer sets at 24, 96, and 600 pickups. [Reward chart](../assets/ultra-reward-generations.png) · [Raw reward results](reward-generations.json)

Run both three-generation comparisons from the repository root.

```powershell
python -B benchmarks/run_tactics.py
python -B benchmarks/plot_tactics.py
```
