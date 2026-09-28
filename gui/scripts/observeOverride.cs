//-----------------------------------------------------------------------------
// SteamHammer modpack: F3 toggles the terraforming grid (Observe), like LiF;
// F3 again leaves it, and the grid follows the player out of the observed area.
//
// In Life is Feudal F3 is bound to the native toggleObserve(). Our engine has no
// such console function - here "Observe" is ability 178 (data/skill_types.xml,
// entity type "cell"), normally started from the Construction context menu,
// which draws the same TerrainSelection cell grid + ObservePositionDlg legend.
//
// Script cannot start an arbitrary ability directly, but every hotbar cell has
// a native key function HB_Tab<tab>_Slot<slot>. So:
//   * one hotbar cell (tab 0 / slot 0 - the last cell of the last tab) is
//     reserved for Observe by patching the character's HotBar.obj before the
//     engine loads it (only if that cell is empty - a player's own ability in
//     that cell is never overwritten);
//   * F3 fires that cell, or closes Observe if it's already open.
//-----------------------------------------------------------------------------

$SH_Observe::AbilityID = 178;
$SH_Observe::Tab       = 0;
$SH_Observe::Slot      = 0;
$SH_Observe::Key       = "F3";

// Rewrites <tab id="Tab"> <cell id="Slot" type="empty" /> into the Observe
// ability. Returns true if the file now holds Observe in that cell.
function shObservePatchHotBar(%file)
{
   if (!isFile(%file))
      return false;

   %in = new FileObject();
   if (!%in.openForRead(%file))
   {
      %in.delete();
      return false;
   }

   %tabTag  = "<tab id=\"" @ $SH_Observe::Tab @ "\"";
   %cellTag = "<cell id=\"" @ $SH_Observe::Slot @ "\"";
   %abilityAttr = "abilityID=\"" @ $SH_Observe::AbilityID @ "\"";

   %count   = 0;
   %inTab   = false;
   %changed = false;
   %present = false;
   while (!%in.isEOF())
   {
      %line = %in.readLine();

      if (strstr(%line, %tabTag) >= 0)
         %inTab = true;
      else if (strstr(%line, "</tab>") >= 0)
         %inTab = false;
      else if (%inTab && strstr(%line, %cellTag) >= 0)
      {
         if (strstr(%line, %abilityAttr) >= 0)
            %present = true;
         else if (strstr(%line, "type=\"empty\"") >= 0)
         {
            %indent = getSubStr(%line, 0, strstr(%line, "<"));
            %line = %indent @ %cellTag @ " type=\"ability\" " @ %abilityAttr @ " />";
            %changed = true;
            %present = true;
         }
         else
            warn("[SH_Observe] hotbar tab " @ $SH_Observe::Tab @ " slot " @ $SH_Observe::Slot
               @ " is in use - F3 will trigger that instead of Observe (" @ %file @ ")");
      }

      %lines[%count] = %line;
      %count++;
   }
   %in.close();

   if (%changed)
   {
      if (%in.openForWrite(%file))
      {
         for (%i = 0; %i < %count; %i++)
            %in.writeLine(%lines[%i]);
         %in.close();
         echo("[SH_Observe] reserved hotbar tab " @ $SH_Observe::Tab @ " slot " @ $SH_Observe::Slot @ " for Observe in " @ %file);
      }
      else
      {
         warn("[SH_Observe] could not write " @ %file);
         %present = false;
      }
   }

   %in.delete();
   return %present;
}

function shObserveIsOpen()
{
   return isObject(ObservePositionDlg) && ObservePositionDlg.isAwake();
}

function shToggleObserve(%val)
{
   if (!%val)
      return;
   echo("[SH_Observe] " @ $SH_Observe::Key @ " pressed, observe open: " @ shObserveIsOpen());

   // F3 again leaves Observe: close the grid window and end the "observing"
   // ability state (it has no duration, so it would otherwise keep running)
   if (shObserveIsOpen() || $SH_Observe::Active)
   {
      $SH_Observe::Active = false;
      if (shObserveIsOpen())
         CloseObservePosition();
      if (isFunction("CancelPerformingAbility"))
         CancelPerformingAbility();
      echo("[SH_Observe] left observe mode");
      return;
   }

   if (shObserveStart())
   {
      $SH_Observe::Active = true;
      $SH_Observe::Retries = 0;
      $SH_Observe::Opened = false;
      shObserveFollow();
   }
}

// all HotBarCell controls under %obj (space separated ids)
function shObserveFindCells(%obj, %out)
{
   if (%obj.getClassName() $= "HotBarCell")
      %out = %out SPC %obj.getId();
   if (%obj.isMethod("getCount"))
      for (%i = 0; %i < %obj.getCount(); %i++)
         %out = shObserveFindCells(%obj.getObject(%i), %out);
   return %out;
}

// Starts Observe. Calling the engine's hotbar key function HB_Tab0_Slot0 from
// script stopped the script silently (seen in the log: nothing after the
// call), so the hotbar cell holding Observe is activated directly instead.
function shObserveStart()
{
   $SH_Observe::WaitUntil = getSimTime() + 1500;   // time for the server to start it
   %cells = isObject(PlayGui) ? trim(shObserveFindCells(PlayGui, "")) : "";
   if (!$SH_Observe::CellDumped && getWordCount(%cells) > 0)
   {
      $SH_Observe::CellDumped = true;
      echo("[SH_Observe] " @ getWordCount(%cells) @ " hotbar cells; the first one:");
      getWord(%cells, 0).dump();
   }
   for (%i = 0; %i < getWordCount(%cells); %i++)
   {
      %c = getWord(%cells, %i);
      %ab = %c.abilityID;
      if (%ab $= "" && %c.isMethod("getAbilityID"))
         %ab = %c.getAbilityID();
      if (%ab $= "" && %c.isMethod("getPossibleAbility"))
         %ab = %c.getPossibleAbility();
      if (%ab == $SH_Observe::AbilityID && %c.isMethod("Activate"))
      {
         echo("[SH_Observe] activating hotbar cell " @ %c @ " (ability " @ %ab @ ")");
         %c.Activate();
         return true;
      }
   }
   warn("[SH_Observe] no hotbar cell with Observe (" @ $SH_Observe::AbilityID @ ") could be activated - see the cell dump above");
   return false;
}

// While Observe is on: the game ends it when the player walks out of the
// observed area - start it again at the player's new position, so the grid
// follows the player. F3 or the window's own close button end it for good
// ($SH_Observe::Active = false). Gives up after 3 failed restarts (e.g. the
// ability cannot be used where the player is now).
function shObserveFollow()
{
   cancel($SH_Observe::FollowEvent);
   if (!$SH_Observe::Active)
      return;
   if (shObserveIsOpen())
   {
      $SH_Observe::Retries = 0;
      $SH_Observe::Opened = true;
   }
   else if (getSimTime() >= $SH_Observe::WaitUntil)
   {
      // Observe did not open: never leave the character "busy" with a started
      // Observe ability (that blocks the right-click menu) - cancel it first
      if (IsPerformingAbility())
         CancelPerformingAbility();
      $SH_Observe::Retries++;
      if (!$SH_Observe::Opened || $SH_Observe::Retries > 3)
      {
         warn("[SH_Observe] Observe did not open here - stopped (look at the ground and press " @ $SH_Observe::Key @ " again)");
         $SH_Observe::Active = false;
         return;
      }
      echo("[SH_Observe] left the observed area - moving Observe with the player");
      shObserveStart();
   }
   $SH_Observe::FollowEvent = schedule(500, 0, shObserveFollow);
}

function shObserveBindKey()
{
   if (isObject(moveMap))
   {
      moveMap.bind(keyboard, $SH_Observe::Key, shToggleObserve);
      echo("[SH_Observe] " @ $SH_Observe::Key @ " bound to: " @ moveMap.getCommand(keyboard, $SH_Observe::Key));
   }
   else
      warn("[SH_Observe] moveMap does not exist yet");
}

package SH_ObserveOverride
{
   // the Observe window's close button / X call this: the player ends Observe
   // (when the game itself closes it on leaving the area, this is not called)
   function CloseObservePosition()
   {
      $SH_Observe::Active = false;
      Parent::CloseObservePosition();
   }

   // hud_presets.cs: loads $HotBarPath/HotBar.obj into the native hotbar
   function loadHotBarData()
   {
      shObservePatchHotBar($HotBarPath @ "HotBar.obj");
      Parent::loadHotBarData();
   }

   // moveMap can be rebuilt from data/bindings.cs; re-apply F3 whenever the
   // game view wakes so the binding always exists in-game.
   function PlayGui::onWake(%this)
   {
      Parent::onWake(%this);
      shObserveBindKey();
   }
};
activatePackage(SH_ObserveOverride);

shObserveBindKey();
