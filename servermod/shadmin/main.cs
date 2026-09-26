//-----------------------------------------------------------------------------
// SteamHammer server mod: admin commands.
//
// Install: copy shadmin/ into the server's scripts/ and add to the end of
// onStart() in server/scripts/root.cs:
//
//     exec("scripts/shadmin/main.cs");
//
// The client half lives in the overlay (gui/scripts/adminCommands.cs) and
// sends everything through one command, so there is one place where rights are
// checked and one place to audit.
//
// GROUNDING
// ---------
// Every engine call below was checked against sh_server.exe's symbols before
// it was used, because this game is not the game the LiF command references
// describe. What was found:
//
// The engine exposes 535 calls to script through Torque's EngineAPI, and the
// signatures below were read from it rather than guessed:
//
//   Player::inventoryAddItem(itemType, quantity, quality, durability, createdDurability)
//   Player::inventoryRemoveItem(itemid)
//   GameConnection::getCharacterId() -> S32
//   changeSkill(char_id, skill_type_id, add_value)      — a DELTA, not a set
//   changeStrStat / changeAgiStat / changeConStat /
//   changeIntStat / changeWillStat (char_id, value)
//   forceSetWeather(weather) / getCurrentWeather()
//   SetInvulnurable(is_invuln)                          — no target parameter
//   ShapeBase::setDamageLevel(level) / applyDamage(amount, author)
//
// Two corrections worth recording, because both were nearly shipped wrong:
// the weather call is forceSetWeather, not setWeather — searching for the
// obvious name found nothing and a /weather command was briefly dropped as
// impossible. And the skill and stat calls take a character id and are global
// functions, not methods on the player.
//-----------------------------------------------------------------------------

if (!isFile("scripts/shadmin/admins.cs"))
   error("SHAdmin: scripts/shadmin/admins.cs is missing - nobody will be an admin. " @
         "Copy admins.example.cs and list the Steam IDs.");
else
   exec("scripts/shadmin/admins.cs");

$SHAdmin::Version = "1.0.0";

//-------------------------------------------------------------------- rights --
//
// The engine has its own idea of who is a GM and it is the one that counts.
//
//   NetConnection::isGM() -> bool        exposed to script
//   Player::setGM(bool) / Camera::setGM(bool)
//
// and the character load reads `a.IsGM` from the `account` table, so the flag
// lives in the database where a server owner already manages accounts. This
// originally shipped with a hand-kept list of Steam IDs, which worked but was
// a second, parallel notion of "admin" sitting beside the game's own — the
// kind of thing that drifts and then disagrees at the worst moment.
//
// So isGM() is the answer. The Steam ID list stays as a bootstrap: setting
// IsGM needs database access, and somebody has to be able to get in and grant
// it the first time. Leave it empty once the flag is set in the database.

/// The Steam ID the *server* believes this connection belongs to.
///
/// Read from the connection, never from the message: a client can put any
/// value in a command's arguments, and the whole point of checking here is
/// that it cannot put one in this.
function SHAdmin_steamIdOf(%client)
{
   if (!isObject(%client))
      return "";
   if (%client.steamId !$= "")     return %client.steamId;
   if (%client.steamID !$= "")     return %client.steamID;
   if (%client.isMethod("getSteamID")) return %client.getSteamID();
   return "";
}

/// True when the account carries the game's own GM flag.
function SHAdmin_isGM(%client)
{
   if (!isObject(%client) || !%client.isMethod("isGM"))
      return false;
   return %client.isGM();
}

/// True when the Steam ID is in the bootstrap list.
function SHAdmin_isBootstrap(%client)
{
   %id = SHAdmin_steamIdOf(%client);
   if (%id $= "")
      return false;

   %list = isFunction("SHAdmin_list") ? SHAdmin_list() : "";
   for (%i = 0; %i < getWordCount(%list); %i++)
      if (getWord(%list, %i) $= %id)
         return true;
   return false;
}

function SHAdmin_isAdmin(%client)
{
   return SHAdmin_isGM(%client) || SHAdmin_isBootstrap(%client);
}

function SHAdmin_reply(%client, %text)
{
   commandToClient(%client, 'ShAdminReply', %text);
}

/// Every refusal and every accepted command, with who asked.
///
/// An admin tool without an audit trail is a way to lose an argument about
/// what happened.
function SHAdmin_audit(%client, %verb, %outcome, %detail)
{
   echo("SHAdmin: " @ SHAdmin_steamIdOf(%client) @ " [" @ %client.playerName @ "] " @
        %verb @ " -> " @ %outcome @ (%detail $= "" ? "" : "  " @ %detail));
}

//------------------------------------------------------------------ helpers --

/// Find a connected client by name, partial name, or connection id.
function SHAdmin_findClient(%needle)
{
   if (%needle $= "")
      return 0;

   %lower = strlwr(%needle);
   %exact = 0;
   %partial = 0;
   %partialCount = 0;

   for (%i = 0; %i < ClientGroup.getCount(); %i++)
   {
      %cl = ClientGroup.getObject(%i);
      if (%cl.getId() $= %needle)
         return %cl;

      %name = strlwr(%cl.playerName);
      if (%name $= %lower)
         %exact = %cl;
      else if (strstr(%name, %lower) >= 0)
      {
         %partial = %cl;
         %partialCount++;
      }
   }

   if (isObject(%exact))
      return %exact;
   // One partial match is a match; two is a question, and guessing which
   // player an admin meant is how the wrong person gets kicked.
   if (%partialCount == 1)
      return %partial;
   return %partialCount > 1 ? -1 : 0;
}

/// Resolve an optional target argument to a client, defaulting to the caller.
function SHAdmin_target(%client, %who)
{
   if (%who $= "")
      return %client;
   %found = SHAdmin_findClient(%who);
   if (%found == -1)
   {
      SHAdmin_reply(%client, "More than one player matches \"" @ %who @ "\" - be more specific.");
      return 0;
   }
   if (!isObject(%found))
   {
      SHAdmin_reply(%client, "No player called \"" @ %who @ "\".");
      return 0;
   }
   return %found;
}

//----------------------------------------------------------------- commands --
//
// Names and argument order follow Life is Feudal wherever the behaviour is the
// same, so an admin coming from LiF can type what they already know.

function SHAdmin_who(%client)
{
   SHAdmin_reply(%client, ClientGroup.getCount() @ " connected:");
   for (%i = 0; %i < ClientGroup.getCount(); %i++)
   {
      %cl = ClientGroup.getObject(%i);
      SHAdmin_reply(%client, "  " @ %cl.getId() @ "  " @ %cl.playerName @
                    "  " @ SHAdmin_steamIdOf(%cl) @
                    (SHAdmin_isGM(%cl) ? "  [GM]" :
                     (SHAdmin_isBootstrap(%cl) ? "  [bootstrap admin]" : "")));
   }
}

/// /add type amount quality durability createDurability
///
/// Exactly LiF's signature, because the engine call underneath takes exactly
/// the same five values in the same order:
/// inventoryAddItem(itemType, quantity, quality, durability, createdDurability).
function SHAdmin_add(%client, %type, %amount, %quality, %durability, %created)
{
   if (%type $= "")
   { SHAdmin_reply(%client, "Usage: /add type amount quality durability createDurability"); return; }
   if (!isObject(%client.player))
   { SHAdmin_reply(%client, "You have no character in the world."); return; }

   // LiF defaults when the optional tail is left off.
   if (%amount $= ""     || %amount <= 0) %amount = 1;
   if (%quality $= "")                    %quality = 100;
   if (%durability $= "")                 %durability = 100;
   if (%created $= "")                    %created = %durability;

   %client.player.inventoryAddItem(%type, %amount, %quality, %durability, %created);
   SHAdmin_reply(%client, "Added " @ %amount @ " x type " @ %type @
                 " (q" @ %quality @ " d" @ %durability @ ").");
   SHAdmin_audit(%client, "add", "ok", %type @ " x" @ %amount);
}

function SHAdmin_tptoplayer(%client, %who)
{
   %target = SHAdmin_target(%client, %who);
   if (!isObject(%target)) return;
   if (!isObject(%target.player) || !isObject(%client.player))
   { SHAdmin_reply(%client, "Both characters must be in the world."); return; }

   // Beside them, not inside them: the same position wedges two characters in
   // one another's collision.
   %p = %target.player.getPosition();
   %client.player.setTransform(getWord(%p,0) + 1 SPC getWord(%p,1) SPC getWord(%p,2) SPC "0 0 1 0");
   SHAdmin_reply(%client, "Teleported to " @ %target.playerName @ ".");
   SHAdmin_audit(%client, "tptoplayer", "ok", %target.playerName);
}

function SHAdmin_tpplayer(%client, %who)
{
   %target = SHAdmin_target(%client, %who);
   if (!isObject(%target)) return;
   if (!isObject(%target.player) || !isObject(%client.player))
   { SHAdmin_reply(%client, "Both characters must be in the world."); return; }

   %p = %client.player.getPosition();
   %target.player.setTransform(getWord(%p,0) + 1 SPC getWord(%p,1) SPC getWord(%p,2) SPC "0 0 1 0");
   SHAdmin_reply(%client, "Teleported " @ %target.playerName @ " to you.");
   SHAdmin_reply(%target, "An admin teleported you.");
   SHAdmin_audit(%client, "tpplayer", "ok", %target.playerName);
}

function SHAdmin_tptospawn(%client, %who)
{
   %target = SHAdmin_target(%client, %who);
   if (!isObject(%target)) return;
   commandToClient(%target, 'ShAdminReply', "Returning you to spawn.");
   serverCmdrespawnClient(%target);
   SHAdmin_reply(%client, "Sent " @ %target.playerName @ " to spawn.");
   SHAdmin_audit(%client, "tptospawn", "ok", %target.playerName);
}

function SHAdmin_jumpto(%client, %x, %y, %z)
{
   if (%x $= "" || %y $= "" || %z $= "")
   { SHAdmin_reply(%client, "Usage: /jumpto x y z"); return; }
   if (!isObject(%client.player))
   { SHAdmin_reply(%client, "You have no character in the world."); return; }
   %client.player.setTransform(%x SPC %y SPC %z SPC "0 0 1 0");
   SHAdmin_reply(%client, "Jumped to " @ %x SPC %y SPC %z @ ".");
   SHAdmin_audit(%client, "jumpto", "ok", %x SPC %y SPC %z);
}

function SHAdmin_stuck(%client)
{
   if (!isObject(%client.player))
   { SHAdmin_reply(%client, "You have no character in the world."); return; }
   %p = %client.player.getPosition();
   // Straight up: the cheapest way out of geometry, and it cannot put anyone
   // somewhere they could not already stand.
   %client.player.setTransform(getWord(%p,0) SPC getWord(%p,1) SPC getWord(%p,2) + 3 SPC "0 0 1 0");
   SHAdmin_reply(%client, "Nudged you upwards.");
}

function SHAdmin_healself(%client)
{
   if (!isObject(%client.player))
   { SHAdmin_reply(%client, "You have no character in the world."); return; }
   %client.player.setDamageLevel(0);
   // The stock handlers are per body part and the engine gives script no list
   // of them, so this sweeps a generous range rather than guessing one.
   for (%part = 0; %part <= 12; %part++)
   {
      %client.player.healWound(%part, 1);
      %client.player.removeBleeding(%part);
      %client.player.removeFracture(%part);
   }
   SHAdmin_reply(%client, "Healed.");
   SHAdmin_audit(%client, "healself", "ok", "");
}

function SHAdmin_suicide(%client)
{
   if (!isObject(%client.player))
   { SHAdmin_reply(%client, "You have no character in the world."); return; }
   %client.player.setDamageLevel(10000);
   SHAdmin_reply(%client, "You killed your own character.");
   SHAdmin_audit(%client, "suicide", "ok", "");
}

/// LiF kills whatever you are looking at. This engine gives script no
/// selection, so the target is named instead — a deviation, and the help text
/// says so rather than leaving an admin wondering why nothing died.
function SHAdmin_kill(%client, %who)
{
   if (%who $= "")
   { SHAdmin_reply(%client, "Usage: /kill <player>  (LiF kills what you look at; this engine has no selection in script)"); return; }
   %target = SHAdmin_target(%client, %who);
   if (!isObject(%target)) return;
   if (!isObject(%target.player))
   { SHAdmin_reply(%client, %target.playerName @ " has no character in the world."); return; }

   %target.player.setDamageLevel(10000);
   SHAdmin_reply(%client, "Killed " @ %target.playerName @ ".");
   SHAdmin_reply(%target, "An admin killed your character.");
   SHAdmin_audit(%client, "kill", "ok", %target.playerName);
}

/// /invul [0|1] — LiF's name, arguments and self-only scope.
///
/// SetInvulnurable(is_invuln) takes no target, which is exactly why LiF's
/// command is self-only too. The spelling is the engine's, not ours.
function SHAdmin_invul(%client, %value)
{
   if (%value $= "")
      %value = %client.shInvul ? 0 : 1;
   %on = (%value != 0);
   %client.shInvul = %on;
   SetInvulnurable(%on ? 1 : 0);
   SHAdmin_reply(%client, "Invulnerability " @ (%on ? "on" : "off") @ ".");
   SHAdmin_audit(%client, "invul", "ok", %on ? "on" : "off");
}

function SHAdmin_weather(%client, %type)
{
   if (%type $= "")
   {
      SHAdmin_reply(%client, "Weather on this server now is: " @ getCurrentWeather());
      return;
   }
   forceSetWeather(%type);
   SHAdmin_reply(%client, "Weather on this server now is: " @ %type);
   SHAdmin_audit(%client, "weather", "ok", %type);
}

/// changeSkill(char_id, skill_type_id, add_value) moves a skill; there is no
/// setter and no getter, so there is no way to offer LiF's /setmyskill.
function SHAdmin_addskill(%client, %target, %skill, %delta)
{
   if (%skill $= "" || %delta $= "")
   { SHAdmin_reply(%client, "Usage: /addmyskill skill delta"); return; }
   %charId = %target.getCharacterId();
   if (%charId <= 0)
   { SHAdmin_reply(%client, %target.playerName @ " has no character id yet."); return; }

   changeSkill(%charId, %skill, %delta);
   SHAdmin_reply(%client, "Moved skill " @ %skill @ " by " @ %delta @ " for " @ %target.playerName @ ".");
   SHAdmin_audit(%client, "addskill", "ok", %skill @ "+" @ %delta @ " -> " @ %target.playerName);
}

function SHAdmin_addmystat(%client, %which, %delta)
{
   if (%which $= "" || %delta $= "")
   { SHAdmin_reply(%client, "Usage: /addmystat <str|agi|con|int|will> delta"); return; }
   %charId = %client.getCharacterId();
   if (%charId <= 0)
   { SHAdmin_reply(%client, "You have no character id yet."); return; }

   switch$ (strlwr(%which))
   {
      case "str":  changeStrStat(%charId, %delta);
      case "agi":  changeAgiStat(%charId, %delta);
      case "con":  changeConStat(%charId, %delta);
      case "int":  changeIntStat(%charId, %delta);
      case "will": changeWillStat(%charId, %delta);
      default:
         SHAdmin_reply(%client, "Unknown stat - use str, agi, con, int or will.");
         return;
   }
   SHAdmin_reply(%client, "Moved " @ %which @ " by " @ %delta @ ".");
   SHAdmin_audit(%client, "addmystat", "ok", %which @ "+" @ %delta);
}

/// Grant or revoke the game's own GM flag for this session.
///
/// setGM(bool) is a live toggle on the connection; `account.IsGM` in the
/// database is what survives a reconnect. So this is deliberately described as
/// a session grant, not a promotion — saying otherwise would have someone
/// wondering why it was gone tomorrow.
function SHAdmin_gm(%client, %who, %value)
{
   %target = SHAdmin_target(%client, %who);
   if (!isObject(%target)) return;

   %on = (%value $= "") ? !SHAdmin_isGM(%target) : (%value != 0);

   if (%target.isMethod("setGM"))
      %target.setGM(%on);
   else if (isObject(%target.player) && %target.player.isMethod("setGM"))
      %target.player.setGM(%on);
   else
   { SHAdmin_reply(%client, "This build exposes no setGM on the connection."); return; }

   SHAdmin_reply(%client, "GM " @ (%on ? "granted to " : "revoked from ") @ %target.playerName @
                 " for this session. Set account.IsGM in the database to make it last.");
   if (%target != %client)
      SHAdmin_reply(%target, "An admin " @ (%on ? "granted" : "revoked") @ " your GM rights for this session.");
   SHAdmin_audit(%client, "gm", "ok", %target.playerName @ " " @ (%on ? "on" : "off"));
}

function SHAdmin_kick(%client, %who, %reason)
{
   %target = SHAdmin_target(%client, %who);
   if (!isObject(%target)) return;
   if (%target == %client)
   { SHAdmin_reply(%client, "Kicking yourself is not a command."); return; }
   if (SHAdmin_isAdmin(%target))
   { SHAdmin_reply(%client, %target.playerName @ " is an admin. Take them out of admins.cs first."); return; }

   if (%reason $= "") %reason = "Kicked by an admin.";
   SHAdmin_audit(%client, "kick", "ok", %target.playerName @ ": " @ %reason);
   SHAdmin_reply(%client, "Kicked " @ %target.playerName @ ".");
   %target.disconnect(%reason);
}

function SHAdmin_time(%client, %hour)
{
   if (%hour $= "" || %hour < 0 || %hour > 24)
   { SHAdmin_reply(%client, "Usage: /time <0-24>"); return; }
   if (!isObject(TimeOfDay))
   { SHAdmin_reply(%client, "This level has no TimeOfDay object."); return; }
   TimeOfDay.setTimeOfDay(%hour / 24);
   SHAdmin_reply(%client, "Time of day set to " @ %hour @ ".");
   SHAdmin_audit(%client, "time", "ok", %hour);
}

function SHAdmin_say(%client, %message)
{
   if (%message $= "")
   { SHAdmin_reply(%client, "Usage: /announce <message>"); return; }
   for (%i = 0; %i < ClientGroup.getCount(); %i++)
      SHAdmin_reply(ClientGroup.getObject(%i), "[Admin] " @ %message);
   SHAdmin_audit(%client, "say", "ok", %message);
}

//------------------------------------------------------------------ the door --

/// The single entry point. One command, one rights check.
function serverCmdshAdmin(%client, %verb, %a, %b, %c, %d, %e)
{
   if (!SHAdmin_isAdmin(%client))
   {
      SHAdmin_audit(%client, %verb, "REFUSED", "not an admin");
      SHAdmin_reply(%client, "You are not permitted to use admin commands.");
      return;
   }

   switch$ (strlwr(%verb))
   {
      case "who":            SHAdmin_who(%client);
      case "add":            SHAdmin_add(%client, %a, %b, %c, %d, %e);
      case "tptoplayer":     SHAdmin_tptoplayer(%client, %a);
      case "tpplayer":       SHAdmin_tpplayer(%client, %a);
      case "tptospawn":      SHAdmin_tptospawn(%client, %a);
      case "jumpto":         SHAdmin_jumpto(%client, %a, %b, %c);
      case "stuck":          SHAdmin_stuck(%client);
      case "healself":       SHAdmin_healself(%client);
      case "suicide":        SHAdmin_suicide(%client);
      case "kill":           SHAdmin_kill(%client, %a);
      case "invul":          SHAdmin_invul(%client, %a);
      case "weather":        SHAdmin_weather(%client, %a);
      case "addmyskill":     SHAdmin_addskill(%client, %client, %a, %b);
      case "addplayerskill": SHAdmin_addskill(%client, SHAdmin_target(%client, %a), %b, %c);
      case "addmystat":      SHAdmin_addmystat(%client, %a, %b);
      case "gm":             SHAdmin_gm(%client, %a, %b);
      case "kick":           SHAdmin_kick(%client, %a, %b);
      case "time":           SHAdmin_time(%client, %a);
      case "say":            SHAdmin_say(%client, %a SPC %b SPC %c SPC %d SPC %e);
      default:
         SHAdmin_reply(%client, "Unknown admin command: " @ %verb);
         SHAdmin_audit(%client, %verb, "unknown", "");
   }
}

/// Tell the client we are here, so its /help can stop saying "needs the mod".
package SHAdmin
{
   function GameConnection::onConnect(%client, %name)
   {
      Parent::onConnect(%client, %name);
      commandToClient(%client, 'ShAdminHello', $SHAdmin::Version);
   }
};
activatePackage(SHAdmin);

echo("SHAdmin " @ $SHAdmin::Version @ " loaded - " @
     getWordCount(isFunction("SHAdmin_list") ? SHAdmin_list() : "") @ " admin(s)");
