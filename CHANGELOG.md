# 1.3.6

- Bot decision intervals can go down to 0.05 seconds. New configs default to 0.1 seconds and saved intervals carry over.
- Added a live host setting for one to three tactical decisions per physics tick, with a default of two. Ready bots take turns, and pursuit and rewards have their own slice of the existing soft CPU budget.

# 1.3.5

- Ultra tracks hostile positions across the map and searches the connected navigation graph for routes around walls and elevation changes. It can pursue a hidden opponent while reserving attacks for clear sightlines.
- In group fights, Ultra looks for an angle on its target where terrain covers it from other attackers. It reserves a short retreat for credible lethal pressure and keeps the KOTH zone as its priority.
- The reward picker shortcut works while movement or other unrelated keys are held. Configured modifiers and the double-tap close gesture still apply.
- Remote clients reconcile bot nameplates as body, team and roster data arrive, so the first life uses the bot's name.

# 1.3.4

- Fixed an Ultra firing deadlock: movement and utility drivers could keep hitscan aim pointing sideways, preventing the firing gate from ever opening. Target aim now updates while the bot turns, including while its other skills recharge.
- Ultra evaluates its four equipped skills using registered configuration and observed casts, attack emissions and hits. Damage, proc coefficient, hit count, physical range, projectile speed, falloff and charge timing inform attack and build choices. Available attacks replace fixed survivor combo orders; native scope, traversal and other multistate chains retain their input handling.
- Opponent movement, skill use and hit outcomes gradually adjust Ultra's aim, reaction and evasive movement. Active skill threats and observed recovery windows inform cover, counterattacks and defensive purchases. Learning persists through respawns within the match.
- Added local collision feedback to obstacle recovery. A/D oscillation without forward progress triggers bounded wall-tangent probes and a committed escape heading, with floor, landing and KOTH checks.
- Learning is host-only, indexes registered state data in small batches and shares the bot decision budget. It does not load assemblies or execute skills for discovery. No new dependencies or multiplayer protocol changes.
- Added a ten-second aim-to-fire regression, production learning lifecycle tests, physical wall-recovery tests and skill range/charge/healing checks. Live map and multiplayer testing remain necessary.

# 1.3.3

- Ultra sustains primary fire while other abilities recharge, and can fire alongside abilities using independent state machines. Shared-state charges and native recovery remain protected.
- Fixed combat routing that selected the bot's current node, discarded progress during goal changes, or repeatedly returned to recently reached points. Route commitment and bounded obstacle/ground checks keep pursuit moving.
- Dodges retain forward or cover movement. Reaching a firing position no longer means waiting motionless for the next tactical decision.
- Ultra remembers sustained damage and defeats across respawns, builds defensive layers and sustain, and keeps balancing these against offensive investment. Host reward rules, bans and item limits still apply.
- Reserved native escapes become available during incoming attacks. Dodge probes are cached and tactical work retains the shared budget.
- Added production-controller and graph-route regression tests. Historical benchmark figures are retained and identified as results from before this update.

# 1.3.2

- Ultra adapts its spacing, defensive timing, and item choices to individual opponents. It uses the survivor's equipped skills and recent damage trades to evaluate builds.
- Bots use native AI drivers and the host's eligible item pool, including DLC content. Ultra can use opponent positions for routing and requires line of sight for new attacks.
- Added an offline comparison of v1.3.1 and v1.3.2 to the description, covering scripted decision checks, reward scoring time, and managed allocations.
- Updated the public credits to Clay Templar and MaTtHeBEST4444.

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
