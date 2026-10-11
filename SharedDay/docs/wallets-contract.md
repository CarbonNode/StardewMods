# Wallets: one shared wallet, or a wallet each (Shared Day 0.1.28)

The owner, 2026-10-07: "wallet settings would be nice, maybe in that main settings popup with the
nice background". The group's owner chooses in the ModRelay launcher (the Stardew Servers
window, Host tab); the hosted farm applies it by itself.

This page is Shared Day's half of that: what it is handed, what it does to every coin, and what
it reports back. The server's and the launcher's halves are in the Modrelayv2 repository
(`docs/turn-tracker.md`).

**Status: built and unit-tested. No lab has been run and nothing here has been played.** The
list at the end is what has to pass in a lab before the farm-host loop is allowed to hand a
running farm a wish.

## Why not the game's own switch

The game changes wallets at the ledger in the Mayor's Manor, host only, overnight
(`Farmer.changeWalletTypeTonight`, read during the night at `Game1.cs` 8203 to 8213;
`ManorHouse.SeparateWallets` and `MergeWallets`, lines 296 to 342, in the 1.6.15 decompile). On a farm that hosts itself nobody can walk to the ledger, and:

- the split divides the pot by **every** claimed farmer, the host PC's own farmer included, so
  that farmer ("HostPC" on Squeet Farm) gets a share nobody can ever spend, as do farmers who
  have left;
- it divides in whole numbers and drops the remainder, so coins vanish;
- the merge adds up in a 32-bit number with no check.

Shared Day never calls either. It does its own arithmetic (`SharedDay.Core/WalletPlan.cs`,
`WalletRules.Plan`), which is pure and unit-tested (`SharedDay.Tests/WalletPlanTests.cs`).

## What Shared Day is handed

Two environment variables, set by the farm-host loop on the game it starts, beside
`SHAREDDAY_WOKEN`. They are read once, when the mod is loaded.

| Variable | Value | Meaning |
| --- | --- | --- |
| `SHAREDDAY_WALLETS_REPORT` | an absolute file path | Where to write what is in force. Unset, or not an absolute path: **no wallet code runs at all.** |
| `SHAREDDAY_WALLETS` | `shared` or `separate` | The owner's choice. Unset or any other value: no wish; Shared Day only reports. Ignored without a report path. |

Everything is inert unless **all** of these hold: `SHAREDDAY_WOKEN` is `1`, this launch loaded
the farm by itself (AutoHost), this game is the host, Shared Day is enabled, and the report path
is an absolute path. A game somebody starts by hand on the host PC has no `SHAREDDAY_WOKEN` and
changes nothing, whatever else is set.

There is no `config.json` field and no Mod Options entry: the launcher is the only place this is
set. (`config.json` holds tokens and is rewritten by the game.)

## When it changes

At one moment only: just after the farm is loaded, once today's turns have been read, before any
player is put into the day. Not on "Try again", not while the farm is up.

It does **not** run inside Shared Day's own `SaveLoaded` handler. The change writes a full save,
and a save raises `Saving` and `Saved` for every mod on the farm; from inside `SaveLoaded` that
would reach mods that have not had their own `SaveLoaded` or `DayStarted` yet, and a mod that
keeps its state in memory would write nothing into the save that carries the money. So
`ModEntry.WalletsWhenSettled` waits on the host's update tick for `DayStarted`, then one second
of ordinary ticks, then a free screen (no event, no menu but Shared Day's own), which is where
every other same-date save is made. While it waits no farmhand is put into the day
(`pendingJoins` is not served). A farm that stays busy for a minute keeps its wallets for this
start: `held: "busy"`. A setting changed while the farm is awake is not seen by the running game; it reaches the
next game the loop starts.

The steps, when there is a wish:

1. The ledger's own pending change (`changeWalletTypeTonight`) is cleared, so the game cannot
   flip the wallets tonight on top of ModRelay's choice.
2. Every claimed farmer is read with their role (below) and the plan is worked out.
3. Already as wished: nothing changes, whatever any wallet holds. The report is written.
4. A hold (below): nothing changes. The report is written with `held`.
5. The money as it stands is remembered in memory.
6. The plan is applied.
7. The farm is read back: the mode is the wish, every wallet reads what the plan said, and the
   total of every coin is exactly what it was. Otherwise the remembered state is put back and
   `held` is `failed`.
8. The change is written into the save's own data
   (`Game1.MasterPlayer.modData["CarbonNode.SharedDay/Wallets"]`).
9. The farm is saved at once (`WorldCheckpoint.Save`, the same same-date save as always). If the
   save fails, the remembered state is put back and `held` is `failed`.
10. The report is written.

The money, the mode and the record of the change are in one save. A game that dies before that
save leaves the old save, and the next start makes the same change from the same numbers.

## Every coin

**Who is what**

- An empty cabin (nobody has made a farmer there) is never counted.
- **The host farmer** is the host PC's own farmer. It is left out of a split only when the
  host's `IncludeHost` is `false`. On a host whose farmer plays, it is a player like any other.
- **Away** is Shared Day's own mark: a farmer who has not connected for `AwayAfterDays` farm
  days, whom the farm no longer waits for. With `AwayAfterDays` 0 nobody is ever Away.
- **Players** are the claimed farmers who are neither the left-out host farmer nor Away.

**One shared wallet to a wallet each (a split)**

- The pot is the farm's shared money. With *n* players, each gets floor(pot / *n*).
- The coins left over (fewer than *n*) stay in the host farmer's wallet (`leftover`). None is dropped.
- The left-out host farmer's wallet is exactly the leftover. A host farmer who plays gets a share plus the leftover.
- An Away farmer's wallet is 0. They are named, with 0, in the preview the owner reads first.
- No players at all: nothing changes, `held: "players"`.
- Every empty cabin's entry is set to the game's own 500g, so a farmer made later starts with what
  the game gives a new farmer, not with a stale copy of the old pot. (With one shared wallet the
  game's load copies the pot into every cabin's entry, `SaveGame.cs` 1028 to 1036.)
- Each offline farmer's wallet is then tied to the host's (`WorldCheckpoint.BindOfflineWallets`),
  without which the save would write that farmer's old number back.

**A wallet each to one shared wallet (a merge)**

- The pot is the sum of every claimed farmer's wallet: players, Away farmers and the host farmer.
- It is added up in 64 bits. More than 2,147,483,647: nothing changes, `held: "too_much"`.
- A merge needs no players (nothing is shared out, so nobody can be left out).

**A wallet each already in force**

- Nothing moves, ever. Money only moves when the mode changes, and the owner has read who gets
  what before choosing that.
- Whatever the host farmer holds (the coins a split could not divide, or anything earned by
  somebody sitting at the host PC, or shipped as the host) stays in that wallet. The report shows
  it as `leftover`, and it goes into the pot if the farm changes back to one shared wallet.
- An earlier draft shared that money out among the players at every start. It was dropped before
  release: it moved a wallet's whole content with no preview, and somebody who plays at the host
  PC as its farmer would have had their earnings handed to the others.

**A farmer made after the change** gets nothing from Shared Day and costs nobody anything. With
one shared wallet they use the pot. With a wallet each the game gives them 500g of their own.

**Holds** (nothing changes; `held` says why)

| `held` | When |
| --- | --- |
| `shipping` | Any item in the farm's shared shipping bin, any farmer's own bin, or any mini-shipping bin (either of its two inventories). The game pays the bins its mode selects (`Farm.getShippingBin`), so items in the other kind would never be paid. A morning load has empty bins, so this clears at the first start after the farm day ends. |
| `players` | Somebody other than the host is already connected, or a split has no players to share with. |
| `too_much` | A merge would put more in one wallet than the game can hold. |
| `busy` | The farm was showing an event or a menu for a minute after the load, so no save could be made. It tries again at the next start. |
| `failed` | The farm could not be read or counted, the check after the change did not add up, or the save failed. The money was put back exactly as it was. |

**Never twice.** The change is to a state, not a step: it happens only when what is in force
differs from the wish. After a change the next start has nothing to do.

**Never on an untouched farm.** With no wish no variable is handed over and nothing is read.

## What players are told in the game

One line in chat, to that player only (a `note` message; a Shared Day older than 0.1.28 shows
nothing), the first time they are on the farm after a change. Who has been told is kept in the
save with the change.

- "This farm changed to a wallet each on Spring 12. You received 4,210g of the 12,630g that was shared."
- "This farm changed to a wallet each on Spring 12. You received none of the 12,630g that was
  shared, because the farm was not waiting for you then." (an Away farmer)
- "This farm changed to one shared wallet on Spring 12. Your 4,210g went into it; it holds 12,630g."

And, each time they join while a wish is held: "This farm is changing to a wallet each. It
happens the next time the farm starts."

The host's log says the same in full: the mode before and after, one line per farmer with their
role and both numbers, the leftover, the empty cabins set to 500g, and the total before and after.

## The report file

Written to `SHAREDDAY_WALLETS_REPORT` as a temporary file beside it and then moved over it, so a
reader never sees half a file. Written after the load, after every same-date save and after
every night's save, so that while the farm is asleep it matches the save on disk. Writing it
changes nothing in the game.

```json
{
  "schema": "shared-day.wallets/1",
  "writtenUtc": "2026-10-07T21:04:24Z",
  "modVersion": "0.1.28",
  "farmId": "450922254",
  "farmName": "Squeet Farm",
  "inForce": "separate",
  "wanted": "separate",
  "held": null,
  "dateLabel": "Spring 12",
  "absoluteDay": 11,
  "pot": null,
  "leftover": 1,
  "farmers": [
    { "id": "-6154521813318541371", "name": "HostPC", "role": "host", "money": 1 },
    { "id": "2283117480133497730", "name": "Alice", "role": "player", "money": 6315 },
    { "id": "77", "name": "Bob", "role": "player", "money": 6315 }
  ],
  "preview": {
    "to": "shared", "pot": 12631, "leftover": 0,
    "farmers": [
      { "id": "-6154521813318541371", "name": "HostPC", "gets": 1 },
      { "id": "2283117480133497730", "name": "Alice", "gets": 6315 },
      { "id": "77", "name": "Bob", "gets": 6315 }
    ]
  },
  "lastChange": {
    "id": "0b6f1c0e5a4f4d3c9b1a2e3f4a5b6c7d", "from": "shared", "to": "separate",
    "dateLabel": "Spring 12", "absoluteDay": 11, "utc": "2026-10-07T21:04:24Z",
    "pot": 12631, "leftover": 1,
    "farmers": [
      { "id": "-6154521813318541371", "name": "HostPC", "before": 0, "after": 1 },
      { "id": "2283117480133497730", "name": "Alice", "before": 0, "after": 6315 },
      { "id": "77", "name": "Bob", "before": 0, "after": 6315 }
    ]
  }
}
```

| Field | Meaning |
| --- | --- |
| `inForce` | `shared` or `separate`: what the save uses. Never the wish. |
| `wanted` | The wish this game was handed, or `null`. |
| `held` | Why this start did not apply the wish, or `null`. One of the words above; a reader must show a word it does not know as a plain "not changed". |
| `pot` | The shared money. `null` with a wallet each. |
| `leftover` | Coins in the wallet of a host farmer nobody plays. They stay there, and go into the pot at a merge. 0 with one shared wallet. |
| `farmers[].id` | The game's 64-bit farmer ID as a string. |
| `farmers[].role` | `player`, `host` (a host farmer nobody plays) or `away`. A host farmer who plays is `player`. |
| `farmers[].money` | That farmer's wallet. `null` with one shared wallet. |
| `preview` | What changing to the **other** mode would do right now. `null` when that change would be held (nobody to share with, or too much for one wallet). |
| `preview.farmers[].gets` | To a wallet each: what that farmer's wallet would hold. For a host farmer nobody plays that is exactly `preview.leftover`, the coins that cannot be split; a reader says that farmer "gets nothing" and names the leftover on its own line. To one shared wallet: what that farmer would put in. |
| `preview.pot` | To a wallet each: the pot that would be shared. To one shared wallet: what the pot would hold. |
| `lastChange` | The last change Shared Day made on this save, from the save itself. `null` when it never made one. `before` is 0 for everyone when the change was from one shared wallet. |

The preview is worked out from the same numbers a change would use, at the moment the report is
written. The real amounts are worked out again when the change happens.

Balances are in this file for every farmer. In the game, players with a wallet each cannot see
each other's money; whoever shows this report decides who sees them.

## What has to be proved in a lab

Run on 2026-10-07 on lab farms: items 1 to 10, 12 to 14 and 16 passed (docs/build-status.md,
0.1.28). Still owed: 11 (what Automate does), 15 beside the whole pack's mods, 17 and 18 end to
end, and every item again on a copy of the live save. Each item has a step in the drivers (`tests/integration/README.md`,
"Wallet labs"); the case and step are named so a result can be read against this list.

| # | What must be seen | Driver step |
| --- | --- | --- |
| 1 | A split on a farm with a host farmer, two players and an empty cabin: each player gets floor(pot / 2), the host farmer holds only the remainder, the total is identical. | `run-lab` `split`: "a wallet each: the two players share the pot..." (run it on a copy of the real save as well, by hand) |
| 2 | After that split, a cold load reads the same wallets. | `split`: "a cold load reads the same wallets..." (it reads each farmer's wallet from the file before Shared Day touches anything) |
| 3 | A merge: the pot is the sum of every wallet including the host farmer's and an Away farmer's, and survives a cold load. | `merge`: first and second steps |
| 4 | Starting twice with the same wish: the second start changes nothing and writes no new change. | `split` and `merge`: second steps |
| 5 | The game killed after the change, before the save completes: the save is unchanged and the next start gives the same result. | `crash` with `-Cold` |
| 6 | An item in the shared bin, a personal bin and a mini-shipping bin each hold with `shipping`, money untouched; after the night it applies at the next start. | `hold`: three steps; `night`: first three steps |
| 7 | A farmer made after a split starts with exactly 500g. | `split`: "a farmer made after the split..." (the cabin is claimed the way the driver claims its fixtures; a real new player is still to be watched by hand) |
| 8 | A farmer made after a merge sees the pot and no money is added or removed. | `merge`: third step |
| 9 | A real second game that was offline during the split connects, sees its own wallet, can spend from it, and the host's record follows. | `run-lan -Scenario wallets` |
| 10 | Overnight after a split: an offline player's shipping pays that player once. | `night`: last step |
| 11 | Money that lands on the host farmer with a wallet each (Automate or anything else that ships as the host) stays there, is reported as `leftover`, and no start moves it. | `split`: "money that landed on the host farmer stays there..." **What Automate itself does has to be watched in a pack lab; no step can assert it.** |
| 12 | A game started by hand with `SHAREDDAY_WALLETS` set but no `SHAREDDAY_WOKEN` changes nothing. | `hold`: "a game the loop did not start..." |
| 13 | No variables at all: no report file, and the save's money fields are the same before and after a load. | `hold`: "with no variables..." |
| 14 | `changeWalletTypeTonight` set, then a start with a wish: the game's own overnight toggle does not fire. | `split` first step (cleared at load); `night`: third and fourth steps (through a real night) |
| 15 | The save made for the change (on the first quiet update tick after the load, never inside `SaveLoaded`) succeeds on a morning load, and beside the whole pack's mods, whose own save data is the same before and after. | `split` first step; run it again with `-ExtraModsPath` on the pack |
| 16 | The report file is whole JSON at every moment it is read; the loop sends `null` when it is missing. | `split`: last step (the loop's half is in its own tests) |
| 17 | End to end on the lab host: the owner sets the mode in the launcher, the loop hands it over at the next wake, the launcher shows pending and then in force with who got what; a non-owner gets 403. | **By hand**, once the server, the loop and the launcher are all in place. No Shared Day driver can reach those. |
| 18 | An Away farmer's 0 at a split is shown by name in the launcher's preview before the owner confirms. | `hold`: first step proves the report carries it; the launcher showing it is by hand. |

## Risks the owner should know

- **Away farmers get nothing at a split.** A player back from a break finds an empty wallet. They
  are named in the preview, and with a wallet each any player can send them money at the Mayor's
  ledger. Including everyone instead is one rule in `WalletRules.Split`.
- **Somebody who plays at the host PC gets no share at a split.** On Squeet Farm somebody does sit
  down at the host PC now and then (the seat key) and plays as "HostPC". With `IncludeHost`
  false that farmer is the host PC's, gets no share, and the preview says so by name. What they
  earn afterwards stays in that wallet.
- **Money on the host farmer stays there** with a wallet each (the coins a split could not divide,
  anything shipped as the host). Nobody can spend it unless somebody plays at the host PC; it
  returns to everyone if the farm changes back to one shared wallet.
- **Held for a long time.** A farm that always goes to sleep with something in a shipping bin
  stays held until a farm day ends.
- **A change made by hand is undone.** If somebody uses the ledger or a debug command while a
  wish is set, the next start the loop makes puts the wallets back to the wish.
