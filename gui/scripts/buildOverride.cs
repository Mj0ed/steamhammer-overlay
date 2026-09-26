//-----------------------------------------------------------------------------
// SteamHammer modpack: construction-site (GuiBuildWindow) counter overrides.
//
// The build window itself (GuiBuildObjectContainer) and its X/Y counters
// (CmGuiDoubleValTextCtrl) are NATIVE engine classes: the engine decides which
// dropped items are accepted and pushes the numbers into the counters with
// setFirstValue()/setSecondValue(). Those native calls never pass through
// script, so this file works *around* them:
//
//   * showBuildWindow() / setBuildName() are script functions the engine calls
//     when the window opens - they are wrapped in a package (so they survive
//     gui/scripts/gui.cs re-exec'ing the stock files) to start a refresh loop.
//   * While the window is open, every counter is re-read and recoloured:
//       below target  -> normal colour (CraftDoubleValProfile white)
//       at target     -> green  (e.g. 10/10)
//       over target   -> green + message 2308 "Cannot add more of this item..."
//   * onChangeItems (fired natively by CmInventory, never implemented by the
//     stock scripts) triggers an immediate refresh instead of waiting a tick.
//
//   * createBuildWindow() gets a 6x2 item grid so materials that are dropped
//     in but not hammered yet are visible and can be dragged back out.
//
// Message text comes from GetMessageIDText(), which reads the cm_messages.xml
// of the language pack the player picked (see languageOverride.cs).
//
// Debug:  $SH_Build::Debug = 1;   logs every hook call with its arguments
//         shBuildProbe();         dumps what each counter exposes to script
//-----------------------------------------------------------------------------

$SH_Build::Slots        = 6;              // createBuildWindow() makes slots "1".."6"
$SH_Build::RefreshMs    = 250;
$SH_Build::ColorNormal  = "255 254 245";  // CraftDoubleValProfile fontColorSEL/NA
$SH_Build::ColorFull    = "84 121 48";    // same green skills.cs uses for "above"
$SH_Build::MsgCannotAdd = 2308;

function shBuildLog(%text)
{
   if ($SH_Build::Debug)
      echo("[SH_Build] " @ %text);
}

function shBuildCounter(%key)
{
   if (!isObject(GuiBuildWindow))
      return 0;
   return GuiBuildWindow.findObjectByInternalName("CraftComponentDoubleVal" @ %key, true);
}

// Returns "cur max" for a counter, or "" when the engine gives script no way
// to read it back (the control only exposes setters - see shBuildProbe()).
function shBuildReadCounter(%ctrl)
{
   if (!isObject(%ctrl))
      return "";

   %text = "";
   if (%ctrl.isMethod("getText"))
      %text = %ctrl.getText();
   if (%text $= "" && %ctrl.isMethod("getValue"))
      %text = %ctrl.getValue();

   %text  = trim(stripMLControlChars(%text));
   %slash = strpos(%text, "/");
   if (%slash < 0)
      return "";

   %cur = trim(getSubStr(%text, 0, %slash));
   %max = trim(getSubStr(%text, %slash + 1, strlen(%text)));
   if (%cur $= "" || %max $= "")
      return "";

   return (%cur + 0) SPC (%max + 0);
}

function shBuildRefresh()
{
   cancel($SH_Build::RefreshSched);

   if (!isObject(GuiBuildWindow) || !GuiBuildWindow.isAwake())
   {
      shBuildLog("window closed - refresh loop stopped");
      return;
   }

   for (%key = 1; %key <= $SH_Build::Slots; %key++)
   {
      %ctrl = shBuildCounter(%key);
      %vals = shBuildReadCounter(%ctrl);
      if (%vals $= "")
         continue;

      %cur = getWord(%vals, 0);
      %max = getWord(%vals, 1);
      if (%max <= 0) // unused slot (0/0)
         continue;

      if (%cur >= %max)
      {
         %ctrl.setFirstColor($SH_Build::ColorFull);
         %ctrl.setSecondColor($SH_Build::ColorFull);
      }
      else
      {
         %ctrl.setFirstColor($SH_Build::ColorNormal);
         %ctrl.setSecondColor($SH_Build::ColorNormal);
      }

      // warn once per overfill amount, reset when it drops back
      if (%cur > %max && $SH_Build::Warned[%key] != %cur)
      {
         $SH_Build::Warned[%key] = %cur;
         shBuildWarnFull();
      }
      else if (%cur <= %max)
         $SH_Build::Warned[%key] = "";

      $SH_Build::Last[%key] = %vals;
   }

   $SH_Build::RefreshSched = schedule($SH_Build::RefreshMs, 0, "shBuildRefresh");
}

function shBuildWarnFull()
{
   %title = isObject(BuildNameCtrl) ? BuildNameCtrl.getText() : "";
   MessageBoxOK(%title, GetMessageIDText($SH_Build::MsgCannotAdd));
}

function shBuildStart()
{
   for (%key = 1; %key <= $SH_Build::Slots; %key++)
   {
      $SH_Build::Warned[%key] = "";
      $SH_Build::Last[%key]   = "";
   }
   cancel($SH_Build::RefreshSched);
   // next tick: the engine fills the counters right after setBuildName()
   $SH_Build::RefreshSched = schedule(0, 0, "shBuildRefresh");
}

function shBuildOnItemsChanged(%src, %a1, %a2, %a3, %a4)
{
   shBuildLog("onChangeItems via " @ %src @ " args=[" @ %a1 @ "][" @ %a2 @ "][" @ %a3 @ "][" @ %a4 @ "]");
   if (isObject(GuiBuildWindow) && GuiBuildWindow.isAwake())
      shBuildRefresh();
}

// Native CmInventory fires "onChangeItems"; the stock scripts never define it,
// so cover the global and every namespace it could plausibly be sent to.
function onChangeItems(%a1, %a2, %a3, %a4)                                   { shBuildOnItemsChanged("global", %a1, %a2, %a3, %a4); }
function CmInventory::onChangeItems(%this, %a1, %a2, %a3, %a4)               { shBuildOnItemsChanged("CmInventory", %a1, %a2, %a3, %a4); }
function GuiInventoryContainer::onChangeItems(%this, %a1, %a2, %a3, %a4)     { shBuildOnItemsChanged("GuiInventoryContainer", %a1, %a2, %a3, %a4); }
function GuiBuildObjectContainer::onChangeItems(%this, %a1, %a2, %a3, %a4)   { shBuildOnItemsChanged("GuiBuildObjectContainer", %a1, %a2, %a3, %a4); }

// The site's material container ("object_inventory", type 4) was 0x0 cells and
// the stock window had no GuiItemContainerCtrl, so dropped-but-not-hammered
// items had nowhere to render and could not be dragged back out. The native
// container maps items onto children named GuiTileBitmap<N> (same pattern as
// authorityContainerWindow.gui createCells()); keep these in sync with
// ContainerSizeX/Y of object_inventory in data/sh_objects_types.xml.
$SH_Build::GridX    = 6;
$SH_Build::GridY    = 2;
$SH_Build::CellSize = 56;
$SH_Build::CellStep = 58;
$SH_Build::GridTop  = 445; // below the requirement counters
$SH_Build::GridGrow = 130; // extra window height for the grid

function shBuildAddItemGrid(%wnd)
{
   if (!isObject(%wnd) || isObject(%wnd.findObjectByInternalName("SH_BuildItemGrid", true)))
      return;

   %gridW = $SH_Build::GridX * $SH_Build::CellStep;
   %gridH = $SH_Build::GridY * $SH_Build::CellStep;
   %wndW  = getWord(%wnd.extent, 0);
   %wndH  = getWord(%wnd.extent, 1) + $SH_Build::GridGrow;

   %wnd.minExtent = %wndW SPC %wndH;
   %wnd.maxExtent = %wndW SPC %wndH;
   %wnd.setExtent(%wndW, %wndH);

   // Build button moves down below the grid
   %btn = %wnd.findObjectByInternalName("BuildButton", true);
   if (isObject(%btn))
      %btn.setPosition(getWord(%btn.position, 0), getWord(%btn.position, 1) + $SH_Build::GridGrow);

   %grid = new GuiItemContainerCtrl()
   {
      position = mFloor((%wndW - %gridW) / 2) SPC $SH_Build::GridTop;
      extent = %gridW SPC %gridH;
      horizSizing = "right";
      vertSizing = "bottom";
      visible = "1";
      active = "1";
      isContainer = "1";
      internalName = "SH_BuildItemGrid";
   };

   for (%i = 0; %i < $SH_Build::GridX * $SH_Build::GridY; %i++)
   {
      %grid.add(new GuiBitmapCtrl()
      {
         position = (%i % $SH_Build::GridX) * $SH_Build::CellStep SPC mFloor(%i / $SH_Build::GridX) * $SH_Build::CellStep;
         extent = $SH_Build::CellSize SPC $SH_Build::CellSize;
         canHit = "false";
         visible = "true";
         profile = "CraftBaseProfile";
         imageIndex = getCellNormal();
         internalName = "GuiTileBitmap" @ %i;
      });
   }

   %wnd.add(%grid);
   shBuildLog("item grid added: " @ $SH_Build::GridX @ "x" @ $SH_Build::GridY);
}

package SH_BuildOverride
{
   function createBuildWindow()
   {
      %wnd = Parent::createBuildWindow();
      shBuildAddItemGrid(%wnd);
      return %wnd;
   }

   function showBuildWindow()
   {
      %wnd = Parent::showBuildWindow();
      shBuildLog("showBuildWindow -> " @ %wnd);
      shBuildStart();
      return %wnd;
   }

   function setBuildName(%name)
   {
      Parent::setBuildName(%name);
      shBuildLog("setBuildName(" @ %name @ ")");
      shBuildStart();
   }
};
activatePackage(SH_BuildOverride);

// Console helper: open a construction site, then run shBuildProbe(); and send
// the console.log lines starting with [SH_Build] back to whoever maintains this.
function shBuildProbe()
{
   if (!isObject(GuiBuildWindow))
   {
      echo("[SH_Build] GuiBuildWindow does not exist - open a construction site first");
      return;
   }

   for (%key = 1; %key <= $SH_Build::Slots; %key++)
   {
      %ctrl = shBuildCounter(%key);
      if (!isObject(%ctrl))
      {
         echo("[SH_Build] slot " @ %key @ ": counter not found");
         continue;
      }

      %text = %ctrl.isMethod("getText")  ? %ctrl.getText()  : "<no getText>";
      %val  = %ctrl.isMethod("getValue") ? %ctrl.getValue() : "<no getValue>";
      echo("[SH_Build] slot " @ %key @ ": class=" @ %ctrl.getClassName()
         @ " getText=[" @ %text @ "] getValue=[" @ %val @ "]"
         @ " parsed=[" @ shBuildReadCounter(%ctrl) @ "]");
   }
   %ctrl = shBuildCounter(1);
   if (isObject(%ctrl))
      %ctrl.dump();
}
