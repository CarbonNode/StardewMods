# SharedDay coordination API

This .NET 6 library has no Stardew Valley or SMAPI dependency. It tracks the required
roster, session participation, completion, and one overnight transition per game date.
It does not save the farm, reset the clock, authenticate messages, or change game state.
Call it only on the host's game thread.

## Host lifecycle

1. For a new farm day, use `DayCoordinator.Create(saveId, absoluteDay, roster, mode)`.
   `saveId` is the stable farm identity and `absoluteDay` is the game's absolute date
   (e.g. `Game1.Date.TotalDays`). The roster contains actual human farmers, excluding
   a dedicated server's unattended host. At least one required farmer is mandatory.
2. For an existing checkpoint, use `CheckpointRecovery.Restore(expectedSaveId,
   actualSavedDay, journal, clock, nextRoster)` and adopt its returned `Coordinator`
   and `Clock` together. It validates the farm identity and the saved journal/budget
   dates before reconciling either. Missing budgets and inconsistent pairs are rejected.
   Do not admit play after a rejected reconciliation. Restore makes everyone offline;
   reconnects repopulate presence. The lower-level `DayCoordinator.Restore` and
   `ReconcileSavedDay` APIs do not validate the companion machine budget on their own.
3. Register a player's connection with `Join(farmerId)`. A `StartSession` result means
   all prior session participants finished or were skipped; a relay adapter may start
   this session at morning. `JoinCurrentSession` and `ResumeSession` preserve the
   current clock. Since 0.1.29 an unfinished participant who disconnects (call
   `Disconnect(farmerId, timeOfDay)`) keeps a clock of their own and holds nobody else's:
   back alone they get `ResumeOwnClock` (set the clock to their `TimeReached`); back while
   somebody else plays they get `ChooseJoinOrWait` and are in no turn until `JoinNow` or
   `Wait`; call `PromoteWaiter()` often, it hands a waiting farmer their own clock once the
   turn in progress has ended. `Observer` means the player finished or was skipped and must
   remain unable to act. After each committed day, `NightsOwedFrom` names the first date
   whose night ran without that farmer; clear it with `AcknowledgeNightsShown` once shown.
4. Call `Complete(farmerId)` for a connected participant's authenticated completion
   action. Call `Disconnect(farmerId)` on disconnect; this never completes a day.
   A host may explicitly `Skip(farmerId)`. Unknown farmer IDs are rejected, keeping
   this day's roster fixed. Roster changes apply at the next committed day.
5. When `IsReadyToAdvance` is true, `BeginAdvance()` returns a stable transition token
   and sets `Phase` to `Advancing`. Stage that journal inside the farm data **before**
   invoking overnight; it must become durable with the world checkpoint it describes.
   An adapter without a durable pre-transition checkpoint rolls back to the previous
   matching checkpoint if it crashes before the overnight save. Retry calls return the same token
   and must not launch another overnight operation. Read `Phase` before deciding
   whether the adapter should initiate that operation.
6. Only after the game durably saves exactly the next date, call
   `AcknowledgeSavedNextDay(token, actualSavedDay, nextRoster)`. This starts the next
   roster with pending statuses. Connected farmers automatically enter its first
   session. A `DayStarted` event alone does not prove a durable save.

`Relay` readiness requires all roster members to be completed or skipped. `Together`
also requires every non-skipped member to be connected. Merely changing presence or
mode never advances the date. The readiness decision is latched by `BeginAdvance`;
later disconnects do not cancel an overnight transition already in progress.

## Durability and recovery

`ExportJournal()` returns an independent DTO with schema version, farm identity,
absolute day, monotonically increasing revision, mode, phase, session number, roster,
session participants, and transition token. `System.Text.Json` can serialize it.
Online presence is transient. Snapshot collections are read-only copies.

Persist the journal together with the farm state it describes, such as inside the
host farmer's saved `modData`. A separately saved newer journal could claim completion
for world actions lost in a crash; the coordinator cannot detect that from a date.
Callers must distinguish in-memory completion from confirmed durable checkpoints.

Persist `CalendarClock.ExportState()` in that same checkpoint. Its high-water minute
tracks how far the shared calendar day has progressed, even when the visible clock
rewinds for another player. Never reconstruct a missing budget from the displayed
clock: a displayed morning may follow a completed afternoon. Checkpoint recovery
requires the budget and journal to identify the same original day and resets both
only when an advancing journal is paired with a durably saved next day.

On restart:

| Saved date | Journal phase | Result |
| --- | --- | --- |
| Same date | Open | `SameDay`; retain saved progress |
| Same date | Advancing | `RecoveredPendingAdvance`; reopen the day, retaining its token |
| Exactly next date | Advancing | `FinalizedAdvance`; reset the roster for that date |
| Any other mismatch | Any | Throw `DayCoordinationException`; require matching recovery data |

The adapter must inspect the durable game's actual date, not an in-memory or proposed
date. Acknowledgements with the wrong token, same date, skipped date, or a repeated old
token are rejected. An invalid new roster leaves the current transition untouched.

## Transport and authority

The core does not know network identities. The host adapter must derive the acting
farmer ID from the authenticated message sender and must never trust a client-supplied
farmer ID. Only the host may invoke `Skip` or `SetMode`; clients receive read-only
snapshots. Persist accepted mutations and broadcast the resulting snapshot/revision.
