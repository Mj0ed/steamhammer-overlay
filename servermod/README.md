# SteamHammer server mod

Server-side admin commands. **Not part of the overlay** — the overlay patches a
player's client, and everything worth calling an admin command is
server-authoritative. A client that could spawn an item or move a character on
its own would be a cheat, not an admin tool.

## Why this exists at all

STEAM HAMMER ships no admin surface. The Life is Feudal server binary carries
64 `SlashCommands::*` classes; `sh_server.exe` carries none. Its scripts define
23 `serverCmd*` handlers and every one of them acts on `%client.player` — the
caller's own character — with no rights check anywhere. There is nothing to
gate and nothing that reaches another player.

So the powers here are built rather than unlocked.

## Installing

Copy `shadmin/` into the server's `scripts/` directory and add one line to
`server/scripts/root.cs`, at the end of `onStart()`:

    exec("scripts/shadmin/main.cs");

Then list who may use it in `scripts/shadmin/admins.cs` (copy
`admins.example.cs`). Restart the server.

## Rights

**The game has its own GM flag and it is the one that counts.** The engine
exposes `NetConnection::isGM()` and `Player::setGM(bool)`, and the character
load reads `a.IsGM` from the `account` table — so GM lives in the database,
where a server owner already manages accounts.

This mod originally shipped a hand-kept list of Steam IDs instead, which worked
but set up a second notion of "admin" beside the game's own. Two lists that can
disagree will eventually disagree, at the worst possible moment. So the check
is now:

    isGM()  OR  the Steam ID is in admins.cs

The list survives only as a bootstrap: setting `IsGM` needs database access,
and somebody has to be able to get in and grant it the first time. Empty it
once the flag is set in the database.

`/gm player [0|1]` toggles the flag for the current session. It is described as
a session grant rather than a promotion because that is what it is —
`account.IsGM` is what survives a reconnect, and saying otherwise would leave
someone wondering why it was gone tomorrow.

## Commands, and how they map to Life is Feudal

Names and argument order follow LiF wherever the behaviour is the same, so an
admin coming from there can type what they already know.

### Identical to LiF

| command | notes |
| --- | --- |
| `/add type amount quality durability createDurability` | Same five arguments in the same order — the engine call underneath is `inventoryAddItem` with exactly those five. `amount` defaults to 1, the quality/durability tail to 100. |
| `/tptoplayer player` | |
| `/tpplayer player` | |
| `/tptospawn [player]` | |
| `/jumpto x y z` | LiF takes one target argument; this takes coordinates. |
| `/stuck` | |
| `/healself` | |
| `/suicide` | |
| `/invul [0|1]` | Self-only in both, for the same reason: the engine call takes no target. |
| `/weather [type]` | Answers in LiF's words: "Weather on this server now is: …". |

### Changed, and why

| LiF | here | why |
| --- | --- | --- |
| `/kill` (what you are looking at) | `/kill <player>` | The engine gives script no selection, so the target has to be named. |
| `/setmyskill skill value` | `/addmyskill skill delta` | `changeSkill(char_id, skill_type_id, add_value)` moves a skill; there is no setter and **no getter**, so a value cannot be set. |
| `/setplayerskill player skill value` | `/addplayerskill player skill delta` | Same. |
| `/setmystat stat value` | `/addmystat stat delta` | Same — `changeStrStat(char_id, value)` and friends move a stat. |

Typing the LiF name gets an explanation rather than silence, and never gets
different behaviour under a familiar name. That is the whole reason these were
not simply given LiF's names: a command that looks like the one you know and
does something else is worse than one that admits it is different.

### No LiF equivalent

`/who`, `/kick player [reason]`, `/time hour`, `/announce message`.

### Not provided

LiF's `/addobj`, `/addnpc`, `/animal`, `/horse`, `/blueprint`, `/summon`,
`/delobj`, `/guild`, `/quests`, `/stable`, `/claimrule`, `/effect*`,
`/injury`, `/title*`, `/alignment`, `/criminal`, `/jh*` and the rest are absent
because the engine exposes nothing to script that would implement them. They
are not oversights, and a stub that accepted the arguments and did nothing
would be worse than their absence.

## Where the powers come from

Life is Feudal is the fork parent, and its server carries 64 `SlashCommands::*`
classes. STEAM HAMMER's carries **none** — checked with a positive control: 99
`SlashCommands::` strings in `ddctd_cm_yo_server.exe`, zero in both
`sh_server.exe` and `ddctd_sh_server.exe`. The command layer was compiled out
of the fork, so there is nothing to re-enable and nothing to detour to.

What survived is the layer underneath. The engine still exposes **535 calls**
to TorqueScript through Torque's EngineAPI, including everything these
commands need, with full signatures in the binary:

    Player::inventoryAddItem(itemType, quantity, quality, durability, createdDurability)
    Player::inventoryRemoveItem(itemid)
    GameConnection::getCharacterId() -> S32
    changeSkill(char_id, skill_type_id, add_value)
    changeStrStat / changeAgiStat / changeConStat / changeIntStat / changeWillStat(char_id, value)
    forceSetWeather(weather) / getCurrentWeather()
    SetInvulnurable(is_invuln)
    ShapeBase::setDamageLevel(level) / applyDamage(amount, author)

So no detour is needed: the commands are gone, the capabilities are not.

Two things were nearly shipped wrong and are worth recording. The weather call
is `forceSetWeather`, not `setWeather` — searching for the obvious name found
nothing, and `/weather` was briefly dropped as impossible. And `changeSkill`
and the stat calls are **global functions taking a character id**, not methods
on the player object.

## Known limits

`SetInvulnurable(is_invuln)` takes no target parameter, so `/god` can only be
trusted for the caller. Targeting someone else is refused rather than silently
applied to the wrong character.

`changeSkill` adds a delta rather than setting a level, which is why the
command is `/skill` and not `/setskill`.

Everything here is read from the binary's own signatures. Whether each behaves
as expected on a live server is a thing to test on a live server.


## A note on animals

Do not raise `<animalsCount>` above 0 on a 32-bit server without testing it,
and test it somewhere that is allowed to fall over.

It was tried here with `passability_map_size__in_terrain_squares` bounded to 40
(down from the stock 250), on the theory that the bound made it safe. It did
not: the server climbed to 1.83 GB RSS — the 32-bit ceiling is about 1.9 GB —
and stopped answering queries. Reverting to 0 brought it back at 760 MB.

So the bound helps and is not sufficient. Whatever the real fix is, it is not
"bound the passability map and turn animals on".
