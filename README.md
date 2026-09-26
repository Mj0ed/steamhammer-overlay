# STEAM HAMMER overlay

The client scripts the launcher applies over a STEAM HAMMER install, and the
server-side admin mod that goes beside it.

Private on purpose. It carries our own work only — no game content — but it is
where fixes are developed before they reach players, and a broken overlay
reaches everyone at once.

```
gui/scripts/       client scripts, applied by the launcher over an install
gui/images/        art that belongs to those scripts
handjob.cs         the launcher's own hook; it loads everything in gui/scripts
servermod/         server-side admin commands — NOT part of the overlay
```

## How a change reaches players

1. Branch, change, open a pull request.
2. It is reviewed and approved by the repository owner.
3. Once merged to `main`, the overlay is published from the launcher repo's
   tooling (`shl-publish push --channel overlay`) and every launcher picks it
   up on its next start.

**Nothing reaches `main` without the owner merging it**, and that is enforced
by access rather than by branch rules: contributors have **read** access, so
they cannot push to this repository at all. Work happens in a fork and arrives
as a pull request from it.

That matters because an overlay is applied automatically by every launcher on
its next start. A mistake on `main` is a mistake on every installation, with no
step in between where anyone would notice.

(GitHub's branch-protection and ruleset features need a paid plan on a private
repository. Read-only collaborators achieve the same outcome here — a
contributor with read access has no way to write to `main`, protected or not.
If this repository ever becomes public, or the account moves to Pro, add a
ruleset requiring one approving review and it becomes belt and braces.)

## What does not belong here

Game files. The overlay channel publishes our own work and the three repaired
data exports, and nothing else — `scripts/` alone is 463 files of shipped
TorqueScript, and publishing it would be publishing the game.

If a fix needs a change to a shipped file, say so in the pull request and it
will be handled as a patch applied at install time rather than as a copy of the
file.

## The server mod

`servermod/` is installed on a server, not on a player's machine, and is not
part of what the launcher applies. Its README covers installation, the admin
list, and how the commands map to Life is Feudal's.
