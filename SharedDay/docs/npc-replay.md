# NPC morning replay

`NpcMorningReplay` addresses a specific problem with resetting the clock: a villager's late-day location, path controller and schedule cursor otherwise remain at the end of the previous cohort's turn.

The helper records each villager's actual 6 AM location, pose, original schedule routes and movement bookkeeping once per real date. A new Relay cohort receives fresh copies of that movement state. The baseline is stored in the host farmer's `modData` (`CarbonNode.SharedDay/NpcMorning`), so it travels in the same farm checkpoint rather than a separate, potentially newer file. A same-date reload reuses the recorded morning; it must never infer morning positions from a midday save's loaded NPCs.

## Progress that must remain

The replay does not restore farmer records, friendship, gifts, quest state, inventories, consumed dialogue, marriage progress or invisibility from the baseline. Villagers removed during the day remain removed, and newly introduced villagers are not deleted. Replaying movement does not recreate the morning's character collection.

In particular, it does not call `NPC.dayUpdate`, `NPC.resetForNewDay`, or `NPC.OnDayStarted`. Inspection of the installed Stardew 1.6.15.24356 assembly showed that those methods also change daily flags, clear marriage dialogue or run marriage duties. Calling them merely to rewind movement would repeat or erase progress.

The original schedule is copied rather than selected again using the current friendship/quest state. Schedule route stacks are copied on capture and on every replay, preserving their traversal order; the engine consumes those stacks as NPCs walk. No route-end callback is fired just to stop yesterday's cohort's animation.

World traversal is deduplicated by object reference. Vanilla has two distinct Mister Qi instances, in the Club and QiNutRoom. Duplicate-name instances with no schedule, controller or queued movement are left untouched because they do not need schedule replay. A duplicate name with movement fails explicitly; name alone cannot identify which moving instance belongs to a recorded baseline.

## Integration order

1. In Relay mode, on the host's fresh `SaveLoaded`, call `EnsureMorningBaseline()` while the clock is still 600 and outside events. On a midday checkpoint load, first require `HasCurrentMorning`: the game loader's temporary 6 AM clock does not make the NPCs a valid morning baseline.
2. On each real `DayStarted` in Relay mode, call `EnsureMorningBaseline()` to capture the new date. Calling it twice on the same date does not replace the original baseline. Together mode does not require NPC replay support because it never rewinds.
3. For a new Relay cohort, set the host clock to 600, then call `RestoreMorning()` before accepting play. Do not call it for a midday join or an unfinished reconnect.
4. Route exceptions into the adapter's held/error state. Do not continue with a partially understood NPC schedule.
5. Before switching from Together to Relay, require an existing baseline or capture a genuine 6 AM baseline before changing modes. A failed capture leaves Together mode unchanged. A future-dated baseline is a save mismatch and must never be replaced silently.

## Boundaries and validation

This helper is not a general midday NPC save system. It does not reconstruct an in-flight path controller after a cold reload in the middle of an unfinished cohort. Raw game serialization ignores schedule cursors, queued routes and several dialogue/movement fields; these require separate recovery work. Friendship's public `GiftsToday`, `GiftsThisWeek`, `TalkedToToday`, `Points` and `LastGiftDate` properties are serializable in the inspected assembly, but their full multiplayer round trip still needs runtime evidence.

The current baseline capture stops if a villager already has an active path controller, queued route or sprite animation at the observed morning. Such state can contain callbacks that cannot be preserved by saving coordinates alone. A farm with that behavior needs explicit support; discarding it would silently alter NPC behavior. First enabling the mod away from 6 AM also stops instead of inventing a morning baseline.

An unstarted `goingToDoEndOfRouteAnimation` flag alone is different from an active animation. The installed 1.6.15 game can leave Willy with that pending flag at the real Spring 3 morning without a controller, animation frames or active route-end behavior. The snapshot preserves the flag, route-end names/messages and a copy of the animation metadata, without executing a callback. Actual started/finishing behavior still stops capture. These are optional schema-1 additions: older accepted snapshots had no pending animation and retain false/null defaults.

The [seven-day engine regression](evidence/2026-10-05-engine-week.json) verifies this Spring 3 state before reload and compares Willy's captured morning position and pending metadata in later cohorts through seven native saves. It also checks that friendship/gift counters and wallets survive the rewind. This is morning-state evidence; afternoon path execution and the special cases below remain separate tests.

Runtime acceptance must compare NPC locations and route behavior at multiple times in both cohorts, then verify that a gift, conversation, friendship gain and quest completed in the first cohort remain completed after replay. Include married villagers, Robin during construction, island visitors, festivals and custom NPCs before claiming support for those cases. An ordinary-town result does not establish those special cases.
