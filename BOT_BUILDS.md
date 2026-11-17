# Survivor bot builds

Bots spend the same earned rewards as players. These are saved priority plans, not free starting loadouts. The chosen plan persists through deaths and Continue; adding a replacement bot selects a new survivor-appropriate plan. The host sees its name when spawning the bot.

| Plan | Main priorities and target stacks |
|---|---|
| **On-hit** | Soldier's Syringe (6), Tri-Tip Dagger (8), AtG Missile Mk. 1 (2), Ukulele (1), Leeching Seed (2), Sticky Bomb (3). Void priorities include Polylute, Plasma Shrimp and Needletick. A Personal Shield Generator supports shield-dependent procs. |
| **Critical** | Lens-Maker's Glasses (9), Predatory Instincts (2), Harvester's Scythe (2), Soldier's Syringe (4). Later upgrades include Shatterspleen, Laser Scope and 57 Leaf Clover. |
| **Skill caster** | Backup Magazine (3), Crowbar (4), Kjaro's/Runald's Bands (2 each), Hopoo Feather (1), then Alien Head and Hardlight Afterburner. Lysate Cell and Purity are preferred within applicable void/lunar offers. |
| **Bulwark** | Tougher Times (5), Repulsion Armor Plate (4), Medkit (3), Leeching Seed (3), Rose Buckler (2), Razorwire (2). Later healing/defence priorities include Rejuvenation Rack, Aegis, Safer Spaces and Weeping Fungus. |

All plans also value limited movement, healing and defence: Goat Hooves, an Energy Drink, Medkit and Tougher Times. Targets are priorities, not hard inventory caps: after planned stacks are filled, an offered duplicate may still be the best available choice. Weighted can offer a rarity before a plan's Command pacing would prefer it; the bot takes the best available pickup in that actual offer. Equipment is not repeatedly replaced when already held. Native AI-blacklisted items are deprioritized if alternatives are available. Custom catalog items can be chosen as fallbacks.

Commando, Captain, MUL-T, REX and Chef favor On-hit/Critical; Huntress, Bandit and Railgunner favor Critical/On-hit. Artificer, Mercenary, Loader, Seeker, False Son and Acrid favor Skill caster/Bulwark. Engineer favors Bulwark/On-hit. KOTH deliberately assigns some bots the Bulwark plan. Only survivors with a registered AI master appear in the selector.

In Command, basic items are developed first, rare choices are deprioritized for the first eight earned rewards, and certain late upgrades have later priorities. Weighted chooses strictly within its rolled essence; Draft chooses strictly within its shortlist. Host bans, enabled expansions and item-pool switches govern every offer. When the XP alternative is enabled and the current cap permits it, bots spend every fifth ordinary choice reward on a personal level. Legal XP also handles empty choice offers. Random does not use a build chooser and keeps its configured shared leveling. Native corruption can replace items and affects the resulting build normally.

The exact priorities live in `src/BotBuildRules.cs`. All saved item/equipment identifiers are audited against the installed game's content fields. These are PvP adaptations with runtime balance testing still pending, not a claim of optimal competitive builds.

Build research used first-person community recommendations for [Commando on-hit/critical item combinations](https://www.reddit.com/r/riskofrain/comments/tfbpi1/item_and_equipment_tierlist_for_every_survivor_in/) and [Artificer burst/cooldown priorities](https://www.reddit.com/r/riskofrain/comments/1e7bmjh/tips_for_artificer/). Their PvE advice informed the plans; Arena-specific weights, defensive targets and Command pacing are implementation decisions. Current item identity and API behavior are checked using the installed game data and assemblies.
