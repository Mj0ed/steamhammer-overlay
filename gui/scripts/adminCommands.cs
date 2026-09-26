//-----------------------------------------------------------------------------
// SteamHammer overlay: slash commands.
//
// WHERE THIS HOOKS
// ----------------
// The engine calls cmChatSendLocalMessageToServer() natively for every chat
// line a player sends — the name is in sh_client.exe, and scripts/client/client.cs
// is where it lands. Wrapping it in a package means a line starting with "/" is
// handled here and never reaches the chat channel, and everything else is passed
// straight through untouched.
//
// WHAT THIS GAME ACTUALLY HAS
// ---------------------------
// STEAM HAMMER is not Life is Feudal. The LiF server binary carries 64
// SlashCommands::* classes; sh_server.exe carries none, and its scripts define
// 23 serverCmd* handlers — every one of which acts on %client.player, the
// caller's own character, with no GM-rights check anywhere. There is no
// server-side admin surface to call.
//
// So the commands here divide in two, and the split is deliberate:
//
//   * LOCAL    — done entirely in the client. These always work.
//   * SELF     — wrap a serverCmd* the stock server already has. They affect
//                you and nobody else, because that is all those handlers do.
//   * MOD      — need the SteamHammer server mod (servermod/). Without it the
//                server simply has no handler and the command says so rather
//                than failing silently.
//
// Anything that acts on another player, spawns an item, or moves a character
// is in the third group by necessity: those are server-authoritative, and no
// amount of client script can fake them. A client that could would be a cheat,
// not an admin tool.
//-----------------------------------------------------------------------------

$SH_Cmd::Prefix      = "/";
$SH_Cmd::Count       = 0;
$SH_Cmd::ModPresent  = 0;   // set by clientCmdShAdminHello when the mod answers

/// Register one command. %needs is "local", "self" or "mod".
function shCmdAdd(%name, %handler, %args, %needs, %help)
{
   %i = $SH_Cmd::Count;
   $SH_Cmd::Name[%i]    = strlwr(%name);
   $SH_Cmd::Handler[%i] = %handler;
   $SH_Cmd::Args[%i]    = %args;
   $SH_Cmd::Needs[%i]   = %needs;
   $SH_Cmd::Help[%i]    = %help;
   $SH_Cmd::Count       = %i + 1;
}

function shCmdFind(%name)
{
   %name = strlwr(%name);
   for (%i = 0; %i < $SH_Cmd::Count; %i++)
      if ($SH_Cmd::Name[%i] $= %name)
         return %i;
   return -1;
}

/// Everything the player sees comes through here, so chat stays the one place
/// a command can answer.
function shCmdEcho(%text)
{
   // onChatMessage is what the stock HUD renders; echo() alone only reaches
   // the console, which a player never sees.
   if (isFunction("onChatMessage"))
      onChatMessage("<color:E8B96A>" @ %text, 0);
   echo("[SH] " @ %text);
}

function shCmdHelp(%which)
{
   if (%which !$= "")
   {
      %i = shCmdFind(%which);
      if (%i < 0)
      {
         shCmdEcho("No such command: /" @ %which);
         return;
      }
      shCmdEcho("/" @ $SH_Cmd::Name[%i] SPC $SH_Cmd::Args[%i]);
      shCmdEcho("   " @ $SH_Cmd::Help[%i]);
      if ($SH_Cmd::Needs[%i] $= "mod")
         shCmdEcho("   Needs the SteamHammer server mod.");
      return;
   }

   shCmdEcho("Commands — /help <name> for detail");
   for (%i = 0; %i < $SH_Cmd::Count; %i++)
   {
      %mark = "";
      if ($SH_Cmd::Needs[%i] $= "mod")
         %mark = $SH_Cmd::ModPresent ? "" : "  (needs the server mod)";
      shCmdEcho("  /" @ $SH_Cmd::Name[%i] SPC $SH_Cmd::Args[%i] @ %mark);
   }
}

/// Dispatch one already-stripped command line. Returns true if it was handled.
function shCmdDispatch(%line)
{
   %line = trim(%line);
   if (%line $= "")
      return false;

   %name = getWord(%line, 0);
   %rest = trim(getWords(%line, 1));

   %i = shCmdFind(%name);
   if (%i < 0)
   {
      shCmdEcho("Unknown command: /" @ %name @ "   (try /help)");
      return true;   // handled: it is still not chat
   }

   if ($SH_Cmd::Needs[%i] $= "mod" && !$SH_Cmd::ModPresent)
   {
      shCmdEcho("/" @ %name @ " needs the SteamHammer server mod, and this server does not have it.");
      return true;
   }

   call($SH_Cmd::Handler[%i], %rest);
   return true;
}

package SH_Commands
{
   function cmChatSendLocalMessageToServer(%position, %message)
   {
      if (getSubStr(%message, 0, 1) $= $SH_Cmd::Prefix)
      {
         shCmdDispatch(getSubStr(%message, 1, strlen(%message)));
         return;   // never reaches the chat channel
      }
      Parent::cmChatSendLocalMessageToServer(%position, %message);
   }
};
activatePackage(SH_Commands);

// The mod announces itself on connect; until it does, mod commands say so.
function clientCmdShAdminHello(%version)
{
   $SH_Cmd::ModPresent = 1;
   $SH_Cmd::ModVersion = %version;
   shCmdEcho("SteamHammer server mod " @ %version @ " detected — admin commands available.");
}

// ---------------------------------------------------------------- LOCAL ----

function shCmdFov(%args)
{
   %v = getWord(%args, 0);
   if (%v $= "") { shCmdEcho("Field of view is " @ $Pref::Player::CurrentFOV); return; }
   if (%v < 5 || %v > 120) { shCmdEcho("Field of view must be between 5 and 120."); return; }
   $Pref::Player::CurrentFOV = %v;
   setFOV(%v);
   shCmdEcho("Field of view set to " @ %v);
}

function shCmdMetrics(%args)
{
   if (isFunction("metricsOn")) { metricsOn(); shCmdEcho("Metrics toggled (also F2)."); }
   else shCmdEcho("This build has no metrics overlay.");
}

function shCmdObserve(%args)
{
   if (isFunction("shObserveToggle")) shObserveToggle();
   else shCmdEcho("The observe override is not loaded.");
}

function shCmdWhere(%args)
{
   if (!isObject(ServerConnection) || !isObject(ServerConnection.getControlObject()))
   { shCmdEcho("Not in a world."); return; }
   shCmdEcho("Position: " @ ServerConnection.getControlObject().getPosition());
}

function shCmdVersion(%args)
{
   shCmdEcho("Server mod: " @ ($SH_Cmd::ModPresent ? $SH_Cmd::ModVersion : "not present"));
}

// ------------------------------------------------------------------ MOD ----
//
// One wire format: the mod receives a verb and its arguments and does the
// rights check server-side, where it cannot be bypassed.

function shCmdMod(%verb, %args)
{
   commandToServer('shAdmin', %verb, getWord(%args, 0), getWord(%args, 1),
                              getWord(%args, 2), getWord(%args, 3), getWord(%args, 4));
}

// -- items -------------------------------------------------------------------
// /add takes exactly the five parameters LiF's does, in the same order,
// because the engine call underneath takes exactly the same five:
// inventoryAddItem(itemType, quantity, quality, durability, createdDurability).
function shCmdAddItem(%args)     { shCmdMod("add", %args); }

// -- teleport ----------------------------------------------------------------
function shCmdTpToPlayer(%args)  { shCmdMod("tptoplayer", %args); }
function shCmdTpPlayer(%args)    { shCmdMod("tpplayer", %args); }
function shCmdTpToSpawn(%args)   { shCmdMod("tptospawn", %args); }
function shCmdJumpTo(%args)      { shCmdMod("jumpto", %args); }
function shCmdStuck(%args)       { shCmdMod("stuck", %args); }

// -- self --------------------------------------------------------------------
function shCmdHealSelf(%args)    { shCmdMod("healself", %args); }
function shCmdSuicide(%args)     { shCmdMod("suicide", %args); }
function shCmdInvul(%args)       { shCmdMod("invul", %args); }
function shCmdKill(%args)        { shCmdMod("kill", %args); }

// -- world -------------------------------------------------------------------
function shCmdWeather(%args)     { shCmdMod("weather", %args); }

// -- skills and stats --------------------------------------------------------
//
// Named "add", not "set", and that difference is deliberate rather than
// careless. LiF's /setmyskill sets a skill to a value; this engine exposes
// changeSkill(char_id, skill_type_id, add_value) and no getter for the current
// level, so a value cannot be set — only moved by a delta. Borrowing LiF's
// name for something that behaves differently would be the worst of both
// worlds: familiar, and wrong.
//
// The LiF names are registered anyway, below, so that typing one explains the
// difference instead of silently doing something else.
function shCmdAddMySkill(%args)      { shCmdMod("addmyskill", %args); }
function shCmdAddPlayerSkill(%args)  { shCmdMod("addplayerskill", %args); }
function shCmdAddMyStat(%args)       { shCmdMod("addmystat", %args); }

function shCmdSetSkillNote(%args)
{
   shCmdEcho("This engine has no way to set a skill to a value: it exposes only");
   shCmdEcho("changeSkill(char, skill, delta) and no getter to read the current");
   shCmdEcho("level. Use /addmyskill <skill> <delta> or /addplayerskill <player> <skill> <delta>.");
}

function shCmdSetStatNote(%args)
{
   shCmdEcho("Same as skills: stats can only be moved by a delta on this engine.");
   shCmdEcho("Use /addmystat <str|agi|con|int|will> <delta>.");
}

// -- no LiF equivalent -------------------------------------------------------
function shCmdWho(%args)         { shCmdMod("who", %args); }
function shCmdGm(%args)          { shCmdMod("gm", %args); }
function shCmdKick(%args)        { shCmdMod("kick", %args); }
function shCmdTime(%args)        { shCmdMod("time", %args); }
function shCmdAnnounce(%args)    { commandToServer('shAdmin', "say", %args); }

// The reply channel: the mod sends results back as chat so there is one place
// to look, whatever the command was.
function clientCmdShAdminReply(%text) { shCmdEcho(%text); }

// -------------------------------------------------------------- registry ----
//
// Names and argument order follow Life is Feudal wherever the behaviour is the
// same, so an admin coming from LiF can type what they already know. Where
// this engine cannot do what LiF's command did, the name is NOT reused — see
// the skill and stat commands above.

// -- local, no server needed --
shCmdAdd("help",     "shCmdHelp",     "[command]",  "local", "List commands, or explain one.");
shCmdAdd("fov",      "shCmdFov",      "[5-120]",    "local", "Show or set the field of view.");
shCmdAdd("metrics",  "shCmdMetrics",  "",           "local", "Toggle the performance overlay (F2).");
shCmdAdd("observe",  "shCmdObserve",  "",           "local", "Toggle the terraforming grid (F3).");
shCmdAdd("where",    "shCmdWhere",    "",           "local", "Print your position.");
shCmdAdd("version",  "shCmdVersion",  "",           "local", "Show the server mod version.");

// -- same name, same arguments, same behaviour as LiF --
shCmdAdd("add",          "shCmdAddItem",     "type amount quality durability createDurability", "mod", "Spawn item(s) into your inventory.");
shCmdAdd("tptoplayer",   "shCmdTpToPlayer",  "player",        "mod", "Teleport yourself to a player.");
shCmdAdd("tpplayer",     "shCmdTpPlayer",    "player",        "mod", "Teleport a player to you.");
shCmdAdd("tptospawn",    "shCmdTpToSpawn",   "[player]",      "mod", "Send yourself, or a player, to spawn.");
shCmdAdd("jumpto",       "shCmdJumpTo",      "x y z",         "mod", "Teleport to coordinates.");
shCmdAdd("stuck",        "shCmdStuck",       "",              "mod", "Free your character when stuck.");
shCmdAdd("healself",     "shCmdHealSelf",    "",              "mod", "Fully heal your own character.");
shCmdAdd("suicide",      "shCmdSuicide",     "",              "mod", "Kill your own character.");
shCmdAdd("kill",         "shCmdKill",        "<player>",      "mod", "Kill a player. LiF kills what you look at; this engine gives script no selection, so name them.");
shCmdAdd("invul",        "shCmdInvul",       "[0|1]",         "mod", "Set invulnerability on yourself.");
shCmdAdd("weather",      "shCmdWeather",     "[type]",        "mod", "Show or set the weather.");

// -- LiF has these, but this engine cannot match the behaviour --
shCmdAdd("addmyskill",     "shCmdAddMySkill",     "skill delta",        "mod", "Move one of your skills by a delta.");
shCmdAdd("addplayerskill", "shCmdAddPlayerSkill", "player skill delta", "mod", "Move a player's skill by a delta.");
shCmdAdd("addmystat",      "shCmdAddMyStat",      "stat delta",         "mod", "Move one of your stats by a delta.");
shCmdAdd("setmyskill",     "shCmdSetSkillNote",   "-- see /addmyskill",     "local", "Not available: this engine cannot set a skill, only move it.");
shCmdAdd("setplayerskill", "shCmdSetSkillNote",   "-- see /addplayerskill", "local", "Not available: this engine cannot set a skill, only move it.");
shCmdAdd("setmystat",      "shCmdSetStatNote",    "-- see /addmystat",      "local", "Not available: this engine cannot set a stat, only move it.");

// -- no LiF equivalent --
shCmdAdd("who",      "shCmdWho",      "",                  "mod", "List who is online, with their ids and rights.");
shCmdAdd("gm",       "shCmdGm",       "player [0|1]",      "mod", "Grant or revoke GM for this session (account.IsGM makes it last).");
shCmdAdd("kick",     "shCmdKick",     "player [reason]",   "mod", "Disconnect a player.");
shCmdAdd("time",     "shCmdTime",     "hour",              "mod", "Set the time of day.");
shCmdAdd("announce", "shCmdAnnounce", "message",           "mod", "Send a message to everyone.");

echo("[SH] slash commands ready - type /help in chat");
