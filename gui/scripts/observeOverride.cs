//-----------------------------------------------------------------------------
// SteamHammer modpack: F3 toggles the terraforming grid (Observe), like LiF.
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

   if (shObserveIsOpen())
   {
      CloseObservePosition();
      return;
   }

   %fn = "HB_Tab" @ $SH_Observe::Tab @ "_Slot" @ $SH_Observe::Slot;
   if (!isFunction(%fn))
   {
      warn("[SH_Observe] " @ %fn @ "() not found - cannot start Observe");
      return;
   }
   call(%fn, 1);
   call(%fn, 0);
}

function shObserveBindKey()
{
   if (isObject(moveMap))
      moveMap.bind(keyboard, $SH_Observe::Key, shToggleObserve);
}

package SH_ObserveOverride
{
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
