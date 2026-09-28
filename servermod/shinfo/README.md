# SteamHammer server info (shinfo)

The server side of three overlay features. **Not part of the overlay** — like
`shadmin`, it is installed on a server.

| client script | what it needs from here |
| --- | --- |
| `gui/scripts/chatOverride.cs` | players online, guild members online, and who is in GM mode — every 5 s (`SH_ChatInfo`) |
| `gui/scripts/skillTreeOverride.cs` | the player's own skill levels when the skill window opens (`SH_SkillLevels`) |
| `gui/scripts/characterOverride.cs` | not this script: the database trigger in `servermod/sql/sh_acribian_trigger.sql` |

Without it the client scripts still load: the chat tabs keep their stock text,
GM lines are not styled, and the skill window shows no level badges (it logs a
5 s timeout).

## Installing

Copy `shinfo/` into the server's `scripts/` directory and add one line at the
end of `onStart()` in `server/scripts/root.cs`, next to the `shadmin` one:

    exec("scripts/shinfo/sh_chatInfo.cs");

Restart the server.

For Acribian characters, also run `servermod/sql/sh_acribian_trigger.sql` once
in the server's game database.

## GM styling and rights

GM mode is switched on in game with `/GM <password>`. None of the server-side
`isGM()` methods reported it in testing, so the client tells the server when its
GM label is showing (`serverCmdSH_GMMode`).

That report is a switch, not a right — any client can send it from its console.
It is accepted only for a player the server already trusts:
`SHAdmin_isAdmin()` when `shadmin` is installed, otherwise `account.IsGM` in the
database (read with the same join the engine uses when it loads a character).
So a GM who knows the `/GM` password but whose account is not flagged gets no
styling. Set `IsGM` on the account, or list the Steam ID in `shadmin`.

## What reads the database

- `SELECT SkillTypeID, SkillAmount FROM skills WHERE CharacterID = <the caller's own character>`
  — `SkillAmount` has 7 decimals (150000000 = 15). A client can only ask for its
  own character's levels.
- `SELECT a.IsGM FROM character c JOIN account a ON a.ID = c.AccountID WHERE c.ID = <the caller's own character>`

Both are read-only.

## Not verified

- The rights check on the GM report is new in this form; the mod pack version
  trusted the client. Run it on a test server with one flagged and one
  unflagged account before relying on it.
- `getGuildID` is looked up on the connection and then on the player; which of
  the two SH exposes decides whether the Guild count works.
