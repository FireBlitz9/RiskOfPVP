# Building RiskOfPVP

Use Windows, a local Risk of Rain 2 installation, and .NET Framework 4.7.2 or newer. Build against the same game version you intend to play. The current source was compiled against Risk of Rain 2 1.5.0#1224.

From the repository root, run:

```powershell
.\build.ps1 -GameDirectory 'C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2' -RunTests
.\pack.ps1
```

The build script downloads pinned compiler and mod references into `.build/deps`, reads game assemblies from your installation, and writes `dist/ArenaPvP.dll`. It does not install the mod or alter the game. `ArenaPvP.csproj` is available for IDE use; `build.ps1` is the supported build entry point.

The package script checks dependency pins and the DLL/manifest version match, then creates a playable ZIP beside the repository directory. Game assemblies, downloaded dependencies, symbols, test executables and source are excluded from that ZIP.

## Development checks

`-RunTests` runs deterministic gameplay-rule tests, tests against the actual reward-pool source with a catalog adapter, BepInEx settings migration/transport tests, incremental reflection-scan tests and a metadata audit of native game hooks. Optional Aerolt/UniverseLib/DropIn contract auditing is in `tests/AeroltContractAudit.cs` and requires separate copies of those optional DLLs.

These checks do not run Unity or replace multiplayer playtesting. Test changes in a separate mod-manager profile. Only one RiskOfPVP/RiskOfArena plugin copy should be enabled, and every player must use the same version.

## Release versions

When preparing a new release, update all three version strings in `src/Plugin.cs`, `manifest.json`, and the relevant changelog entry together. Keep upstream dependency versions unchanged unless their references and compatibility are deliberately updated. Attach playable ZIPs to GitHub Releases rather than committing compiled files.

The package is named RiskOfPVP. The runtime plugin GUID `com.fireb.arenapvp`, in-game name RiskOfArena and saved configuration identity are retained for existing players.
