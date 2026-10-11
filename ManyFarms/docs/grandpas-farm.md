# Grandpa's Farm and Immersive Farm 2 as picks (looked into 2026-10-08, nothing built)

The owner, 2026-10-07: "do we not have the farm from stardew valley expanded like grandpa's or whatever?"
The picker ("What kind of farm?") lists the game's own kinds plus every entry in `Data/AdditionalFarms`.
Frontier Farm is such an entry (`FlashShifter.FrontierFarm/FrontierFarm`, map `Farm_FrontierFarm`), so it
is a pick. Grandpa's Farm and Immersive Farm 2 Remastered are not, and cannot be as they are.

## What they are

All three are optional Content Patcher packs on Stardew Valley Expanded's own Nexus page (mod 3753: update
keys `Nexus:3753@GF`, `@IF2R`, `@FF`), each two folders (`[CP] <name>` + `[FTM] <name>`), all 1.15.11, all
requiring `FlashShifter.StardewValleyExpandedCP`. SVE's wiki: they are separate files, and "players cannot
play with all 3 farms at the same time". Neither Grandpa's Farm nor IF2R is in the Squeet Valley Expanded pack.

- **Grandpa's Farm replaces the Standard farm.** Its `content.json` loads `Maps/Farm` from
  `assets/Maps/GrandpasFarm.tbin` with no condition; its other 46 patches (layouts, mist, warp edits in Bus
  Stop, Forest, Backwoods and Woods, the greenhouse dirt fix) are gated `FarmType: Standard`, and it relabels
  the Standard tile's name. No `Data/AdditionalFarms` entry, and no config key to make it a kind of its own
  (keys: MistEffects, SandboxLayout, RemoveFences, RemoveGrassStretch, RemoveLandExpansionPond, GrassCropField,
  HeavyDebris, LargerBridgeToGrandpasShed, LargerGreenhouse, RemoveGreenhouseObjects).
- **IF2R replaces Standard the same way** (`assets/Maps/IF2R.tmx`), and stands down when Grandpa's Farm is
  installed. The original Immersive Farm 2 (zanderb14, Nexus 3561) had a `ReplaceStandardFarm` switch and a
  custom type; FlashShifter's remaster dropped that. No mod re-registers either as an additional farm type.
- The game holds one farm type per save (`Game1.whichFarm`; a mod kind is `Game1.whichModFarm`, its `MapName`
  the map). A `Data/AdditionalFarms` entry needs `ID`, `TooltipStringPath`, `MapName`, `IconTexture` (22x20),
  `WorldMapTexture` (131x61).

## What could be done

1. **As they are: no.** The picker cannot see a map that only replaces `Maps/Farm`.
2. **A small wrapper content pack** of our own that adds an `AdditionalFarms` entry (say `SqueetGrandpasFarm`)
   whose `MapName` is Grandpa's own replaced map. The game would then load Grandpa's layout as a kind of its own,
   so a plot could be started on it. But everything else in Grandpa's Farm is keyed to `FarmType == Standard`
   and the location named `Farm`: its warps, its greenhouse fix, its config layouts, its `[FTM]` spawns and
   SVE's Grandpa's Shed and grove events would all be off or on the main farm. A bare Grandpa's layout, in
   other words; it would need a lab run beside the pack before anyone plays on it.
3. **Full fidelity** means the whole save is a Standard farm with Grandpa's Farm installed, which is what SVE
   supports. Squeet Farm is a Frontier Farm save, so that is a new save, and every player installs the pack.

A plot of a modded kind also shares that kind's map with the main farm when the save is of the same kind
(Stardew Realty's author found Frontier Farm "only half worked" as an expansion for that reason).

Sources: FlashShifter/StardewValleyExpanded on GitHub (each pack's `manifest.json` and `content.json`),
stardewvalleyexpanded.wiki.gg/wiki/Farm_Maps, nexusmods.com/stardewvalley/mods/3753, Content Patcher's
`TokenSaveReader.cs` and stardewvalleywiki.com/Modding:Farm_data. Confirmed from those files; the wrapper's
behaviour is inferred, not played.
