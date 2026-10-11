# Many Farms

**Several farms in one Stardew Valley co-op game.** Each player picks a farm when they join, or starts a new one on any land type, and a Farmstead Totem set down in the world takes anyone to any farm.

> **BETA:** in active testing, still working out bugs. Back up your save first. Made with AI assistance; the in-game map and farmstead art are AI-generated.

Download: the optional file on [Nexus Mods](https://www.nexusmods.com/stardewvalley/mods/53757?tab=files) · the repo's [Releases](https://github.com/CarbonNode/StardewMods/releases)

Many Farms is standalone. It does not need [Shared Day](../SharedDay/README.md), and Shared Day does not need it, but together they make a turn-based farm where every player has land of their own.

![Where to?](docs/screens/where-to-own-farm.png)

## What players get

- **Eight spare farm plots.** Pick an existing farm when you join, or start a new one: Standard, Riverland, Forest, Hill-top, Wilderness, Four Corners, Beach, Meadowlands, or a modded farm type.
- **A house on your farm** whose door opens into your own cabin rooms (same bed, chests and fridge). Robin builds house upgrades there.
- **Each farm works like a farm:** its own cave, greenhouse, shipping bin, pet bowl, weeds, forage, crops, lightning rods and that land type's extras.
- **The Farmstead Totem** (15 wood + 10 fiber; anyone can craft and use one) opens *Where to?*, a pixel-art map of the valley with every farm and its farmers' faces. Click one to travel there.
- **Roads lead home:** walking home from the Bus Stop, Forest or Backwoods (or a Farm warp totem) brings you to your own farm. Each player can switch this off.
- **Still shared, as in any co-op game:** money, the Community Center, festivals, the night's shipping and Grandpa's evaluation ([docs/grandpas-farm.md](docs/grandpas-farm.md)).

## Install

1. Unzip Many Farms into `Mods` for the host and every player, the same version.
2. The host sets `"Enabled": true` in `Mods/ManyFarms/config.json`. It does nothing until then.

**Do not remove Many Farms from a save that has extra farms.** The game drops locations it does not know, and those farms would go with it. Switching it off is safe; deleting it is not.

Host console: `many_farms list | kinds | start | assign | totem | place | houses`.

## Code

`ManyFarms.Core` holds the game-independent rules (farm slots, map layout, menu wording, house upgrades); `ManyFarms.Mod` is the SMAPI mod and its `assets/`. `art/` has the scripts that cut the generated farmstead images down to map size. Build and test instructions are in the [root README](../README.md).
