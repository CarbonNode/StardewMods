# Joining part way through a turn: the rule and the words

The owner, 2026-10-07: "im confused what happens if someone is half way through day and someone
else joins server", then "i think it should warn about a midday join (you are sacrificing time x
amount of time so far)", "because if you join when they are offline your clock starts at
beginning".

This page is the one place the rule and its wording are written down. Shared Day (the title-screen
hub, `SharedDay.Core/MiddayJoin.cs`) and the ModRelay launcher (the Stardew home page) both
follow it, word for word.

## The rule, as the code has it

`DayCoordinator.Join` (`SharedDay.Core/DayCoordinator.cs`) decides where a farmer who connects
is put. In Relay, a turn is one run of the clock from 6:00 AM, shared by everyone in it.

| The turn in progress has... | The joiner gets | `JoinDisposition` |
| --- | --- | --- |
| nobody unfinished **playing** in it (everyone in it finished, left, or nobody has played yet) | a turn of their own, at 6:00 AM | `StartSession` |
| an unfinished farmer who is in the farm now | that turn, at its clock | `JoinCurrentSession` |

Since 0.1.29 a farmer who **left a turn unfinished** holds nobody: their turn is parked at the
clock they left it at (`TimeReached`), and the next joiner gets a morning of their own. So the
owner's last sentence is true: joining while the other farmer is offline starts at 6:00 AM. (Until
0.1.28 the leaver's turn kept its clock for everyone, `ResumeSession`; that disposition remains
only for a journal written before 0.1.29, which has no parked clock.)

The leaver's own return:

| They come back and... | They get | `JoinDisposition` |
| --- | --- | --- |
| nobody is playing | their parked clock back, on the tile they left, in their own turn | `ResumeOwnClock` |
| somebody is playing | a question on the farm: *Join now* (that turn, at its clock) or *Wait* (held until it ends, then their parked clock) | `ChooseJoinOrWait` |

Two more facts:

- A farmer who already played or sits the day out only watches, and loses nothing.
- A machine counts no hour twice: the resumed clock replays hours the farm already granted.

Together mode has no turns to lose: playing at the same time is the point. Nothing is said.

## When a warning is shown

All of these must hold:

1. The farm is in Relay, its date is open (not saving the night) and it is not paused.
2. The farm's tracker report has a clock (`time`, HHMM from 600 to 2600) later than 6:00 AM.
3. The report is not stale: the tracker calls the host online (it heard from it in the last 12
   minutes). A stale report or an offline host shows nothing.
4. The turn in progress has at least one farmer with `status: "Pending"`, `inCurrentTurn: true`
   **and `online: true`** (0.1.29: a farmer who left holds nobody).
5. This player's own farmer is `Pending` and **not** `inCurrentTurn`, or is one the farm stopped
   waiting for (`Skipped` with `away: true`: joining turns them `Pending` again and puts them into
   the turn in progress), or the player has no farmer yet.
6. This PC is not the farm's own unattended host.

"This player's farmer" is the farmer the tracker's `members` list ties to the player's ModRelay
account. When no farmer is tied to them, every farmer that no account has claimed and that is not
in the farm right now could be theirs; if one of those has played, sits out or holds the turn,
the warning says "If you have not played today" instead of stating it.

A player whose own farmer is `Pending` and `inCurrentTurn` is told they resume, and is not warned:
"Your turn is still open. Join and you carry on where you left it." with nobody playing, or, with
somebody playing, "Your turn is still open. Alice is playing now: the farm asks whether you join
that turn at its clock or wait and carry on where you left yours." (the tracker does not know the
player's own parked clock, so no time is named for it).

A farmer the farm stopped waiting for (away for `AwayAfterDays` days) is reported as `Skipped`
with `away: true` (0.1.28 on; a farmer skipped on purpose has no such mark). Coming back puts
them straight into the turn in progress, at its clock, so they get the same warning. A report
from an older host has no mark, and such a farmer is then not warned before the join; the line in
chat after the join still tells them.

The launcher uses these same rules and prints these same sentences, word for word
(`joinOutlookText` in `packages/protocol/src/turns.ts` of CarbonNode/Modrelayv2; both test suites
pin the strings). One difference in when: the game's hub shows nothing until the farm is up and
reporting. (The launcher's "left a turn unfinished" sentence is 0.1.28's rule and no longer holds
a joiner's clock; it is to be dropped there too.)

## The arithmetic

A game day runs 6:00 AM to 2:00 AM, 20 hours (1,200 minutes). The game writes the clock as HHMM
and counts on past midnight: 2400 is 12:00 AM, 2530 is 1:30 AM, 2600 is 2:00 AM.

- Minutes gone = (HH - 6) x 60 + MM, never less than 0 or more than 1,200.
- A clock is believed only when it is from 600 to 2600 with MM under 60.
- A span is written "8 h 10 min", "8 h" or "40 min". A clock is written "2:10 PM".

`DayClock` in `SharedDay.Core/MiddayJoin.cs`, tested in `SharedDay.Tests/MiddayJoinTests.cs`.

## How old the clock is

The clock in a report is the clock when the report was sent. Ten minutes on the farm are seven
seconds here, so a report five minutes old is seven farm hours behind.

- From 0.1.28 the host reports every half hour of game time (21 seconds) while a Relay turn is
  being played, as well as on every change and every 5 minutes (`TrackerCadence`).
- A report up to **60 seconds** old is worded with "about".
- An older report (an older host, or a clock that was held) is never shown as the time now. It is
  worded as the least it can be: "The farm last said it was 2:10 PM, 4 min ago, and it is later
  than that now", and "at least 8 h 10 min".
- When nobody is playing nothing is asked (0.1.29): the joiner gets a morning of their own.

The report's age is the tracker's `server_time` minus `updated_at`, plus the time since the
answer arrived.

## The words

`{who}` is every unfinished farmer of the turn, `{now}` those of them in the farm: "Alice",
"Alice and Bob", "Alice, Bob and 2 more".

**Somebody is playing, fresh report**

> Alice is playing now and it is about 2:10 PM on the farm. Join now and your day starts there:
> about 8 h 10 min of today's 20 hours are already gone. Wait until Alice has finished and you
> start at 6:00 AM.

**Somebody is playing, older report**

> Alice is playing now. The farm last said it was 2:10 PM, 4 min ago, and it is later than that
> now. Join now and your day starts at the farm's time, not at 6:00 AM: at least 8 h 10 min of
> today's 20 hours are already gone. Wait until Alice has finished and you start at 6:00 AM.

**The unfinished farmers have all left** (0.1.28 only; from 0.1.29 nothing is asked, see above)

**The player cannot be placed** (the middle sentence changes, in all three)

> ... If you have not played today, joining now starts your day there: ...

**The player resumes their own turn** (not a warning; one line, no question)

> Your turn is still open. Join and you carry on where you left it.

> Your turn is still open. Alice is playing now: the farm asks whether you join that turn at its
> clock or wait and carry on where you left yours.

With more than one farmer: "Alice and Cleo **are** playing now", "Wait until Alice and Cleo
**have** finished".

## In the game

Shared Day's hub on the title screen asks before it connects. The box is titled **Join part way
through the day?**, shows the sentence, and has two answers: **Wait** (first; it is where a
controller starts, and Escape or B means the same) and **Join now**. Wait leaves the player on the
title screen, in the hub, having joined nothing. The launcher's warning is not a blocker either:
Play still works.

After the host has put a farmer into a turn that was already running, it sends that farmer one
line in chat, with its own clock at that moment:

> You joined at 2:10 PM: 8 h 10 min of today's 20 hours had already gone. The clock is shared, so
> your day ends at 2:00 AM with everyone else's.

A farmer coming back to their own unfinished turn reads instead:

> You are back in your turn. It is 2:10 PM, where you left it.

(From 0.1.29. A journal from before it, with no parked clock, still reads "You are back in your
turn. It is 2:10 PM, where the turn had got to.")

## On the farm: join now or wait (0.1.29)

A farmer back with a parked turn while somebody else plays is held where they stand and asked by
Shared Day's own popup (it opens by itself; F8 brings it back):

> Bob is playing Spring 13 now and it is 9:00 AM on the farm. You left your turn at 1:40 PM. Join
> Bob now, or wait and carry on at your own time?

with the answers **Join Bob now (9:00 AM)**, **Wait for Bob, then carry on at 1:40 PM** and
**Decide later**. Waiting reads "You are waiting for Bob to finish Spring 13. Then you carry on at
1:40 PM, where you left your turn." and still offers **Join Bob now instead (9:00 AM)**. Once the
turn in progress has ended the waiter is told "You are back in your turn. It is 1:40 PM, where you
left it." and is put back on the tile they left. The answers travel as fix requests (`joinnow`,
`wait`) to the host, which alone moves anyone.

The line is a `note` message from the host to that one player. A player whose Shared Day is older
than 0.1.28 shows nothing; a host older than 0.1.28 sends nothing.

## Not played yet

The 0.1.28 rule and sentences were unit-tested and played in the LAN labs on 2026-10-07
(docs/build-status.md, 0.1.28: `midday` and `overlap`). The 0.1.29 rule (a leaver holds nobody,
the choice on the farm) is unit-tested and has its lab steps written (`return-choice`,
`exact-resume`), not run; see build-status.md, 0.1.29.
