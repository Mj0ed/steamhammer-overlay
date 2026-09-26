# Contributing

## Before you write anything

Say what you are fixing, in an issue or in the pull request. Two people have
already written the same override twice here.

## Ground rules

**Check the engine before you use it.** This game is a fork of Life is Feudal
with the command layer compiled out: `ddctd_cm_yo_server.exe` carries 99
`SlashCommands::` strings, `sh_server.exe` carries none. What survived is the
layer underneath — 535 calls exposed to script through Torque's EngineAPI, with
their signatures in the binary:

    strings -a sh_server.exe | grep -oP "(?<=EngineAPI: Engine not initialized when calling ).*"

A call that is not in that list is not callable from script, whatever a Life is
Feudal reference says about it. Several were nearly used here under the wrong
name — the weather call is `forceSetWeather`, not `setWeather`, and `changeSkill`
is a global taking a character id rather than a method on the player.

**Package, don't replace.** Overriding a stock function means wrapping it:

    package MyThing { function stockFunction() { Parent::stockFunction(); } };
    activatePackage(MyThing);

`gui/scripts/gui.cs` re-execs the stock files, so a plain redefinition is
silently undone. Every override here is a package for that reason.

**No game files.** Do not add a copy of a shipped script, image or data file.
If a fix needs one changed, describe the change and it will be applied as a
patch at install time.

**Say what you have not verified.** A comment saying "argument order inferred,
untested on a live server" is worth more than a confident one that turns out to
be wrong. Several comments here say exactly that.

## Pull requests

You have read access, so you cannot push branches here. Fork the repository,
push your branch to your fork, and open the pull request from there:

    gh repo fork Mj0ed/steamhammer-overlay --clone
    git switch -c fix/build-counters
    # ... change, commit ...
    git push -u origin fix/build-counters
    gh pr create --repo Mj0ed/steamhammer-overlay

Every pull request is reviewed and merged by the owner. Nothing reaches `main`
any other way.

Keep a pull request to one subject. An overlay that changes five unrelated
things is one that cannot be reverted when one of them turns out to be wrong,
and every launcher will already have applied it.
