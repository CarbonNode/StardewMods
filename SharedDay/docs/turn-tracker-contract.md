# Turn tracker contract (Shared Day → ModRelay), version 1

Shared Day's host reports the farm's turn state to ModRelay so players can see, from the
launcher or the web, whose turn it is without opening the game. The host is the only sender;
clients never post. Shared Day sends; ModRelay stores the latest state per farm and shows it.

## Host configuration (Shared Day `config.json`)

```json
"TurnTracker": {
  "Url": "https://modrelay.app/turns/<trackerId>",
  "Token": "<secret issued by ModRelay for this tracker>"
}
```

Empty `Url` turns reporting off. The token is a credential: never logged by either side.

## Request

`POST {Url}` with `Authorization: Bearer {Token}` and `Content-Type: application/json`.

Sent on every change of turn state (a farmer joins, leaves, finishes or is skipped; a new
turn starts; the day advances; the farm pauses or resumes), at most once per 5 seconds, and
as a heartbeat every 5 minutes while the host's game has the farm loaded. From 0.1.28 it is
also sent every half hour of game time (21 seconds) while a Relay turn is being played, that
is while an unfinished farmer of the current turn is online: `time` is what a player reads
before joining part way through a turn ([midday-join.md](midday-join.md)), and a clock five
minutes old is seven farm hours behind. A host older than 0.1.28 sends only changes and
heartbeats, so a reader must go by the report's age, never assume it is fresh. A tracker whose
last report was *received* more than 12 minutes ago (by the tracker's own clock, not
`sentUtc`) shows the host as offline. Reports are idempotent snapshots: the newest
`sequence` wins; older or equal ones are ignored. Every post, heartbeats included, carries
a new, higher `sequence` (the host uses `max(previous + 1, now in Unix ms)`, which also
survives the host's clock stepping back), so a heartbeat always refreshes "last received".

```json
{
  "schema": "shared-day.turns/1",
  "sequence": 1791212508123,
  "sentUtc": "2026-10-05T15:01:48Z",
  "modVersion": "0.1.9",
  "farm": { "id": "385772993", "name": "Sunny" },
  "date": { "season": "spring", "day": 13, "year": 1, "absoluteDay": 12, "label": "Spring 13" },
  "mode": "Relay",
  "phase": "Open",
  "turn": 2,
  "time": 1230,
  "furthestTime": 2200,
  "paused": null,
  "farmers": [
    { "id": "-6154521813318541371", "name": "Alice", "status": "Completed", "online": false, "inCurrentTurn": false },
    { "id": "2283117480133497730", "name": "Bob", "status": "Pending", "online": true, "inCurrentTurn": true }
  ]
}
```

| Field | Meaning |
| --- | --- |
| `sequence` | Strictly increasing per farm (Unix milliseconds of the change). |
| `farm.id` | Stable farm identity (the game's unique ID for the save), as a string. |
| `date.absoluteDay` | Days since Spring 1, Year 1 (0-based), for ordering. |
| `mode` | `Relay` (separate turns on the same date) or `Together` (everyone online at once). |
| `phase` | `Open` while the date is being played; `Advancing` during the overnight save. |
| `turn` | Turn (cohort) number on this date, starting at 1; 0 before anyone has played. |
| `time` / `furthestTime` | The game clock as HHMM (e.g. `1230`, up to `2600`), and the furthest time any turn reached today. |
| `paused` | `null`, or a non-empty player-facing sentence when Shared Day has paused the farm to protect the save. |
| `farmers[].id` | Stardew multiplayer ID as a string (64-bit; do not parse as a JS number). |
| `farmers[].status` | `Pending` (still to play today), `Completed` (played today) or `Skipped` (sits today out). |
| `farmers[].online` | Connected to the host right now. |
| `farmers[].inCurrentTurn` | Part of the turn in progress. The game keeps this true for a farmer who finished and left until the next turn starts. |
| `farmers[].away` | Optional, from Shared Day 0.1.28: `true` for a farmer who is `Skipped` only because the farm stopped waiting for them (not seen for `AwayAfterDays` farm days). The moment they connect they are `Pending` again and play today. Absent or `false` otherwise. |
| `farmers[].portrait` | Optional, from Shared Day 0.1.11: the farmer's face as the game draws it, a small PNG as a `data:image/png;base64,` address. See "Portraits". |

"Whose turn is it": in Relay, anyone `Pending` may play; the farm is waiting on the
`Pending` farmers. In Together, the day ends once every non-skipped farmer is `Completed`
and online at the same time. When nobody is `Pending`, the next day starts once the farm saves.

## Portraits

From 0.1.11 the host draws each farmer's small portrait (the one in the game's multiplayer list)
off screen, 32 by 32 pixels with a clear background, and sends it with the farmer as
`"portrait": "data:image/png;base64,..."`, so the launcher can show the farmers' own faces.
`TurnTracker.Portraits: false` in the host's config turns it off. One farmer is drawn a second,
a new face first and then the oldest, so a changed look arrives within about a minute per
farmer; a new face is a change and is reported like any other.

Limits, enforced by both sides: one address is at most 6,000 characters (about 4.4 KB of PNG;
a real face is a few hundred bytes) and all the addresses of a report at most 40,000. Past the
total, later farmers are sent without a face. The receiver keeps a portrait only when it is
exactly a PNG data address (the prefix, base64 that starts with PNG's own first bytes, nothing
else) and drops anything else for that farmer without refusing the report; a receiver older
than the field drops it like any unknown field. A host that cannot draw (no graphics, or a mod
replaced the farmer's renderer) reports without faces and says so once in its log.

## Responses

`204` or `200` on success. From Shared Day 0.1.29 a `200` may carry orders for the host, as JSON:

```json
{ "barred": ["2283117480133497730"], "new_farmers": false }
```

`barred` lists farmer ids (strings, 64-bit) the group has removed from the farm: the host skips
each for today, leaves them off every later roster, disconnects them with "You were removed from
this farm's group.", and once they are offline deletes their farmhand so the cabin is free.
`new_farmers: false` makes the host turn away anyone claiming a cabin as a new farmer; `true`
opens it again. Both are kept in the host's save and applied again on every answer, so the
tracker may repeat them. A `204`, an empty body or a body without either field means no orders; a
host older than 0.1.29 ignores the body. `401` for a missing or wrong token, or an unknown tracker. `409`
if `sequence` is not newer, or if the report is for a different farm than the one the
tracker first heard from (a tracker locks onto its first `farm.id`). `413` above 64 KB.
`400` for invalid JSON, another `schema` or a missing `sequence`; `415` for a content type
other than JSON. The host treats every rejection the same way: one warning in its log, then
it keeps playing; it never retries in a loop, the next change or heartbeat sends a fresh
snapshot. The token is opaque to the host (ModRelay's start with `sdt_`).

## Compatibility

Unknown fields must be ignored by the receiver, so later versions can add them. A breaking
change gets a new `schema` value. Farmer and farm names come from the game and can contain
any Unicode; render them as text, never as HTML. Shared Day must keep working (and keep the
game unblocked) whatever the tracker does: slow, down, or rejecting.

## The host PC on the tracker, and waking it from the game (0.1.31, server of 2026-10-08)

`GET /turns/<id>` carries `host` for a farm that belongs to a group with a host PC:
`{ "name": "HostPC", "state": "asleep" | "starting" | "awake" | "offline", "wake_pending": false, "wake_from_farm": true }`,
and `null` otherwise (an older server leaves the key out). It never carries the host's address
or code: the launcher's link file does that, only while the farm is up. The title-screen hub
reads `state` over the link file's, and lights **Wake the farm** when the host is asleep or
offline and `wake_from_farm` is true (the group's owner put a wake password on the host).

`POST /turns/<id>/wake` with `{ "password": "<the wake password>" }` and no Authorization
(the game has no account) asks the host PC to start:

| Answer | Meaning |
| --- | --- |
| `200 { "ok": true, "state": "asleep", "wake_pending": true }` | The wake is on its way; the host's next check-in starts the game. `state` is as it was: `awake` or `starting` means it was already up and nothing more was asked. |
| `403 password_required` / `wrong_password` | No password, or not the one. The hub keeps its box open. |
| `429 too_many_attempts` | Five wrong from one address, or forty on one group, in ten minutes. |
| `409 no_wake_password` | The host has no wake password: nobody can wake it from a game until the owner sets one in ModRelay. |
| `404 not_found` | No such tracker, a link-only farm, or a group with no host PC. |

The hub shows each answer in the player's words (`HubFarm.ParseWakeAnswer`). The password is
never logged or kept.

