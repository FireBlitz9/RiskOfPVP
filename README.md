# RiskOfPVP

Turn Risk of Rain 2 into a PvP arena. Fight everyone, split into red and blue teams, or compete over a moving capture point. Vote for a map, build your survivor with item rewards, and respawn to get back into the fight.

## Features

- **FFA, Teams and King of the Hill**, with separate settings for each mode.
- Map and mode voting before each match.
- **Command, Weighted, Draft and Random** item rewards.
- Adjustable starting rewards and rewards for kills, deaths or timed intervals.
- An XP choice instead of an item, with a level cap that rises over time.
- Item blacklists, stack limits, equipment recycling and a reward scrapper.
- Survivor bots, configurable respawns, spawn protection and anti-one-shot protection.
- DLC and custom item support through the game's catalogs.

Arena removes monsters, chests, shrines and teleporters. Void Fields, Void Locus and Planetarium have no void fog in Arena. The optional reward scrapper remains available. Normal gamemodes keep their usual rules.

## Getting started

1. Install the mod and its dependencies on every player's computer. Everyone needs the same version.
2. Launch modded and select **RiskOfArena**, next to Monsoon in character selection.
3. Use **Map Vote** and the mode buttons to vote. Open **Match settings** to set up the match, then ready up.

The in-game mode and Mod Options entry are called **RiskOfArena**. When upgrading, remove or disable older copies, including older RiskOfArena installations, and restart the game. Keep only one copy enabled.

One-person multiplayer lobbies work for practice or playing against bots.

## Modes

| Mode | Objective |
|---|---|
| **FFA** | Reach the kill goal before anyone else. |
| **Teams** | Red versus Blue, with shared kills and friendly fire disabled. |
| **King of the Hill** | Charge the Void Focus, then follow it to its next location. |

KOTH defaults to three captures to win. At the time limit, completed captures and then partial progress decide the winner. Capture goal, charge time, radius and relocation time are configurable.

At match end, **Continue** keeps builds and scores and extends the goal. **Play Again** starts a fresh match on the same map and mode. Both need a majority of human players. **Back to Lobby** is host-only.

## Items and leveling

| Reward mode | How it works |
|---|---|
| **Command** | Choose an essence, then a pickup from it. |
| **Weighted** | Roll an essence, then choose its pickup. Each class's chance is editable. |
| **Draft** | Choose from a fixed shortlist for each essence. |
| **Random** | Receive a random pickup automatically. |

Choice modes can offer **+1 Level** instead of an item. The host sets how the level cap grows. Random can level everyone at a shared interval, every 60 seconds by default.

Rewards show the usual pickup notification. Falling into the void never grants death or kill rewards. Item selectors show icons and full descriptions, and respect enabled DLCs, host bans and each player's stack limits.

## Scrapper and bots

The optional Arena scrapper has a marker and an outline visible through walls. Scrap items or equipment for reward points. Weighted, Draft and Random preserve the scrapped pickup's rarity or equipment class; Command keeps unrestricted choices. Item-removal effects still apply.

Set a starting bot count in Match settings, or use **Esc > Arena bots** during a match. Bot controls are host-only. Bots earn rewards, respawn, join balanced teams and choose builds in choice modes. Inspect their inventories on the normal inventory scoreboard, usually **Tab**. Available survivors depend on registered survivor AI.

## Settings and controls

Use **Match settings** in character selection or **Settings > Mod Options > RiskOfArena**. General holds map rules; FFA, Teams and KOTH have independent gameplay profiles. Controls holds your keybinds, nametags, overhead arrows and menu preferences.

Gameplay rules come from the host. Clients can view them but cannot edit them. Personal controls stay local. Most rule changes apply next match; item restrictions and the scrapper toggle also update live.

| Default control | Action |
|---|---|
| **F2** | Open or close rewards |
| **F3** | Arena scoreboard |
| **F4** | Map vote |
| **Double-tap W** | Close the item or essence menu and bank the reward |

Double-tap W requires two presses and releases. Holding W when a menu opens will not dismiss it. **Back** also banks rewards. Reopening a menu does not reroll them.

## Compatibility and notes

RiskOfOptions and R2API Teams are required and included in the dependency list. Aerolt, DropIn, LookingGlass and larger-lobby mods are optional. DropIn supplies mid-game joining; Arena supplies catch-up rewards and team balancing. Teams and KOTH support expanded rosters; FFA has a finite combat-team limit.

The startup integration targets Aerolt 4.3.1 with UniverseLib 1.5.1. DropIn integration was checked against DropinMultiplayer 4.2.1. Custom items need valid catalog entries; custom tier behavior can vary. Mods replacing teams, respawns or progression may need compatibility work. Split-screen reward selection is not supported.

Built against Risk of Rain 2 **1.5.0#1224**. Automated checks pass. The latest Aerolt startup change and large-lobby combinations still need fresh multiplayer testing.

By **claytemplar**. MIT license.

[Building from source](BUILDING.md)
