# 1.3.1

- Extra High and Ultra bots dodge, jump in combat and resume sprinting between compatible attacks, including sprint resets after recovery.
- Ultra coordinates survivor-specific setup, burst and finishing attacks using actual skill activations and cooldowns. Charged and channelled skills keep their normal hold/release handling.
- Combat movement checks landing support, obstacles, sprint stopping distance and the KOTH boundary. Native traversal and custom survivor skill chains remain available.
- Bot actions stay host-only and use normal movement and skill inputs. No new dependencies.

# 1.3.0

- Renamed the RiskOfOptions entry to RiskOfPVP, retaining saved configuration and the in-game mode identity.
- Added General > Bots with Low, Medium, High, Extra High and Ultra intelligence, a shared decision interval and FFA-only Chase mode. Medium remains the default.
- Restored native survivor attack-driver priority and fixed discarded waypoint destinations. Travel no longer doubles as a primary attack. Bots require an aimed hostile target before firing.
- Enabled native cliff checks, added bounded edge probes and retained validated navigation jumps. All bot tiers seek and hold KOTH objectives.
- Reworked fixed builds to avoid destructive void conversions. Extra High and Ultra choose adaptive builds; Ultra reads nearby opponents' actual items and equipment to choose counters.
- Cached catalog/loadout data and moved bot reward decisions into the shared tactical budget. Incompatible offers are banked without rerolls and cannot trap dead bots.

# 1.2.1

- Preserved LookingGlass and other tooltip overrides when a personal-level icon is reused for a normal item.
- Restored the icon's prior image visibility when releasing the level artwork.

# 1.2.0

- Reorganized settings into General, FFA, Teams, KOTH and Controls, without numbered category names.
- Moved shared combat, respawn, protection and pickup-pool rules into General. Added a combined item/equipment blacklist and survivor-specific bans and stack limits.
- Added optional void rescue using the game's teleport system and the player's last safe ground position. Combat and protection edits apply live from the host.
- Replaced reward-event toggles with amount sliders; zero disables an event. Random and Weighted now share one set of rarity weights. Draft keeps the configured number of choices inside every essence.
- Kept timed levelling exclusive to Random. Added a themed personal-level icon to the native inventory bar and Tab display.
- Migrated old saved rules and local preferences. Old mode blacklists merge into General using the strictest restriction; shared values inherit the previous default mode.

# 1.1.12

- Removed managed-DLL loading, whole-process assembly scans and UniverseLib cache patches.
- In Arena, Aerolt now initializes its menu through its own API only when its configured menu key is pressed. The first deliberate open may still pause during Aerolt's native initialization.
- Removed obsolete reflection-scanning helpers. PvP rules, item rewards, bots and DropIn support are unchanged.

# 1.1.11

- Rewrote the README with a short overview, features, setup and controls.
- Removed development reports and internal setup documents from the playable package. They remain in the source download.
- Cleaned up stale installation instructions and release notes. Gameplay is unchanged from 1.1.10.

# 1.1.10

- Reworked optional Aerolt startup scanning to resolve types in smaller steps, addressing the recorded freeze after spawning.
- Changed the package name to RiskOfPVP. The in-game mode, settings and saved configuration remain RiskOfArena.

# 1.1.9

- Added optional Aerolt startup and DropIn catch-up support.
- Added fair catch-up rewards for mid-game players and manually spawned bots.
- Balanced humans and bots together when players join, leave or change bot slots.

# Earlier RiskOfArena releases

- Added FFA, Teams, King of the Hill, map voting and survivor bots.
- Added Command, Weighted, Draft and Random rewards, personal XP and growing level caps.
- Added item blacklists and stack limits, equipment recycling and the Arena reward scrapper.
- Improved multiplayer voting, respawns, pickup messages and survivor compatibility.
