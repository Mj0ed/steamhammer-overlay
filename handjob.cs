//-----------------------------------------------------------------------------
// STEAM HAMMER local-server auto-join / connection probe.
//
// Vanilla client main.cs ends with exec("handjob.cs") and ships no such file,
// so this is a script hook that modifies nothing Steam would overwrite. It runs
// after scripts/root.cs, i.e. after parseArgs()/onStart().
//
// It waits for the main menu, joins $SHJoin::address once, and writes every
// state change to $SHJoin::eventLog so a join can be checked without watching
// the window.
//
// Off unless the launcher asks for a join. It writes data/join.cfg.cs with
// $SHJoin::enabled = 1 when a player picks a server; launched any other way the
// hook stays out of the way and the game's own menus — including its server
// browser — work normally.
//
// Opt out entirely: launch with -nojoin, or create data/nojoin.
// Console: shJoinNow();  shJoinStop();
// Optional overrides go in data/join.cfg.cs, e.g.
//     $SHJoin::autoPlay = 1;   // also pick character slot 0 and press Play
//-----------------------------------------------------------------------------

$SHJoin::enabled    = 0;      // set to 1 by data/join.cfg.cs, never by default
$SHJoin::address    = "127.0.0.1:28000";
$SHJoin::password   = "";
$SHJoin::menuSettle = 3000;   // ms the main menu must be up before joining
$SHJoin::tickMs     = 1000;
$SHJoin::eventLog   = "data/join_events.log";
$SHJoin::autoPlay   = 0;      // 1 = also pick a character and press Play
$SHJoin::charSlot   = 0;
$SHJoin::netDebug   = 0;      // 1 = start the engine's NETDEBUG session at char select

if (isFile("data/join.cfg.cs"))
   exec("data/join.cfg.cs");

function SHJoin_emit(%event, %detail)
{
   %line = "SHJOIN" TAB %event TAB getSimTime() TAB %detail;
   echo(%line);
   %f = new FileObject();
   if (%f.openForAppend($SHJoin::eventLog))
   {
      %f.writeLine(%line);
      %f.close();
   }
   %f.delete();
}

function SHJoin_menuUp()
{
   return isObject(MainMenuMultiplayerWindow) && MainMenuMultiplayerWindow.isAwake();
}

function shJoinNow()
{
   $SHJoin::state = "connecting";
   SHJoin_emit("CONNECTING", $SHJoin::address);
   joinToRemoteServer($SHJoin::address, $SHJoin::password);
}

function shJoinStop()
{
   $SHJoin::state = "stopped";
   SHJoin_emit("STOPPED", "by console");
}

function SHJoin_netDebugStart()
{
   // Starts the engine's NETDEBUG session locally AND, via
   // commandToServer('NetDebugSessionStart'), on the server. Both ends then
   // print per-event "Packed/Unpacked <class> with <n> bits" lines, which is
   // the bit-accurate trace we need to line the two streams up.
   SHJoin_emit("NETDEBUG", "debugNetStart");
   debugNetStart("shjoin");
}

function SHJoin_play()
{
   SHJoin_emit("PLAY", "slot" SPC $SHJoin::charSlot);
   CharSelectionSlotPressed($SHJoin::charSlot);
   schedule(750, 0, "CharSelectionPlayPressed");
}

function SHJoin_tick()
{
   if ($SHJoin::state $= "stopped" || $SHJoin::state $= "connecting")
      return;

   if (!SHJoin_menuUp())
      $SHJoin::menuSeenAt = 0;
   else if (!$SHJoin::menuSeenAt)
      $SHJoin::menuSeenAt = getSimTime();
   else if (getSimTime() - $SHJoin::menuSeenAt >= $SHJoin::menuSettle)
   {
      shJoinNow();
      return;
   }

   schedule($SHJoin::tickMs, 0, "SHJoin_tick");
}

// Report the milestones that show how far the handshake got.
package SHJoin
{
   function peerCmdConnectionEstablished(%server)
   {
      SHJoin_emit("PEER_ESTABLISHED", %server SPC %server.getName());
      Parent::peerCmdConnectionEstablished(%server);
   }

   function selectCharacterDlg::onWake(%this)
   {
      SHJoin_emit("CHAR_SELECT", "needCreateChar=" @ %this.needCreateChar);
      Parent::onWake(%this);

      if ($SHJoin::netDebug)
         schedule(400, 0, "SHJoin_netDebugStart");

      if ($SHJoin::autoPlay && !%this.needCreateChar)
         schedule(1500, 0, "SHJoin_play");
   }

   function GameConnection::initialControlSet(%this)
   {
      SHJoin_emit("INGAME", $SHJoin::address);
      Parent::initialControlSet(%this);
   }

   function _disconnectWithMessageBox(%title, %message)
   {
      SHJoin_emit("DISCONNECT", %title SPC "-" SPC %message);
      Parent::_disconnectWithMessageBox(%title, %message);
   }
};

// Nothing to do unless a join was actually requested. Auto-connecting on a
// plain launch takes over the main menu three seconds in, which is what made
// the in-game server browser unusable.
$SHJoin::optOut = !$SHJoin::enabled || isFile("data/nojoin");
for (%i = 1; %i < $Game::argc; %i++)
   if ($Game::argv[%i] $= "-nojoin")
      $SHJoin::optOut = true;

if ($SHJoin::optOut)
   echo("SHJoin: idle (no join requested) - the game's own menus are in charge");
else
{
   activatePackage(SHJoin);
   $SHJoin::state = "waitmenu";
   $SHJoin::menuSeenAt = 0;
   SHJoin_emit("SESSION_START", $SHJoin::address);
   schedule($SHJoin::tickMs, 0, "SHJoin_tick");
}

//-----------------------------------------------------------------------------
// Overlay scripts.
//
// Loaded from here rather than from main.cs on purpose. main.cs is the game's
// own file: adding four exec lines to it would mean publishing a shipped script
// through a channel that promises not to republish the game, and it would have
// to be kept in step with every future patch to it. This file is already ours
// and the engine already runs it, so it is the natural place to hang our own
// scripts.
//
// Order matters. shStrings first, because the others ask it for text; the rest
// are independent.
//-----------------------------------------------------------------------------

function SH_execOverlayScript(%path)
{
   if (!isFile(%path))
   {
      // A partial overlay is not a crash: say which piece is missing and carry
      // on, so one absent file cannot take the whole client down with it.
      echo("SH: overlay script missing, skipped: " @ %path);
      return;
   }
   exec(%path);
}

SH_execOverlayScript("gui/scripts/shStrings.cs");
SH_execOverlayScript("gui/scripts/skillTreeOverride.cs");
SH_execOverlayScript("gui/scripts/languageOverride.cs");
SH_execOverlayScript("gui/scripts/buildOverride.cs");
SH_execOverlayScript("gui/scripts/observeOverride.cs");
SH_execOverlayScript("gui/scripts/adminCommands.cs");
SH_execOverlayScript("gui/scripts/characterOverride.cs");
