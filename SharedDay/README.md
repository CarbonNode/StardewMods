# Shared Day

by **squeetsqub**

**Turn-based co-op for Stardew Valley 1.6.** Everybody plays the same farm day, one after another. Alice waters the crops in the morning and finishes her day; Bob logs on in the evening and gets his own 6:00 AM of the same date, with Alice's work already done. When everyone has played (or been skipped), the night runs and the farm moves on together.

> **BETA:** in active testing, still working out bugs. Back up your save first. Made with AI assistance.

Download: [Nexus Mods](https://www.nexusmods.com/stardewvalley/mods/53757) · the repo's [Releases](https://github.com/CarbonNode/StardewMods/releases)

## How it plays

- **Relay mode** (default): each farmer takes a turn on the current date, starting at 6:00 AM, in a shared, live world. The date advances once every farmer on today's roster has finished or sits the day out.
- **Together mode:** everyone on the roster has to be online and finished before the day ends.
- **Finish my day:** press F8 (or go to bed) and choose *Finish my day*.
- **The turn board:** a strip of farmer faces with a tick (played), an empty box (still to play) or a cross (sitting out). Click it for the menu; drag it to any corner.
- **Leave mid-turn:** an unfinished turn is parked at the clock and tile you left, and you carry on there when you return.
- **Machines don't run twice:** machines only count the hours the day has not already reached.
- **Villagers start their morning again:** each new turn puts every villager back at their stored 6:00 AM place and schedule, without touching friendship, gifts, quests or dialogue ([docs/npc-replay.md](docs/npc-replay.md)).
- **Skip someone:** the host can skip an offline farmer, and the others can vote to skip. Farmers offline for 3 days (configurable) sit days out automatically.
- **Also:** festivals, nights credited to absent farmers, *Save my turn*, sticky notes, optional Discord/Slack webhooks and a one-click Join button.

## Troubleshooting menu (0.1.35)

Press F8 (or click the turn board) and open **Troubleshooting**:

- **Give \<farmer\> a fresh day:** puts one farmer back to 6:00 AM in their own bed, in a turn of their own, while everyone else carries on.
- **Restart today for everyone:** everyone back to 6:00 AM in bed; finished players play again.

Both reset the villagers to their stored morning. What was done today stays done. The host always sees the menu; other players see it only on a farm with a farm password. Console: `shared_day fresh_day <name>`, `shared_day restart_day`.

## Install

1. Install SMAPI 4.1 or later.
2. Unzip Shared Day into `Mods`. **The host and every player need it, the same version.**
3. Launch once so `Mods/SharedDay/config.json` exists, then set `"Enabled": true` on every PC. Shared Day does nothing until you do.
4. Host a co-op farm as usual, ideally a new or backed-up one.

Generic Mod Config Menu is optional.

## Configuration

`Mods/SharedDay/config.json`, see `SharedDay.Mod/ModConfig.cs` for every field:

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `false` | Switches the mod on. Every PC. |
| `Mode` | `Relay` | Or `Together`. |
| `IncludeHost` | `true` | Whether the host's own farmer takes turns. `false` for a PC that only hosts. |
| `RequiredPlayerIds` | empty | Empty = every farmer who has claimed a cabin. |
| `AwayAfterDays` | `3` | Days offline before a farmer sits days out automatically. `0` waits for ever. |
| `MenuKey` | `F8` | Opens the Shared Day menu. |
| `Notifications.WebhookUrl` | empty | Discord/Slack posts from the host. |
| `JoinServer` | empty | Address, invite code and label for the title-screen Join button. |
| `AutoHost` | off | An unattended host PC that loads the farm by itself. |
| `Bypass.Salt` / `Bypass.Hash` | empty | The farm password, host only, stored as a salted SHA-256 hash. |
| `TurnTracker` | empty | ModRelay launcher extras only. Leave empty otherwise. |

Host console: `shared_day status | skip | reopen | unskip | fresh_day | restart_day | bar | unbar | free_cabin | mode Relay|Together | checkpoint | retry`.

## Known limits

- Third-party mods with their own daily timers, or that assume one continuous clock and one save per day, may misbehave.
- The same-date checkpoint saves only while no remote farmer is connected; until then, a host crash rolls back to the last save. A dated ZIP backup is kept under `Mods/SharedDay/backups/`.
- Married villagers, weddings, construction and passing out mid-turn are less tested.
- JunimoServer and other dedicated-server setups need their own testing ([docs](docs/)).

## Code

`SharedDay.Core` is the game-independent coordinator (rosters, turns, parked clocks, skips, restarts); its API is described in [SharedDay.Core/README.md](SharedDay.Core/README.md). `SharedDay.Mod` wires it into the game with SMAPI and Harmony. Build and test instructions are in the [root README](../README.md).
