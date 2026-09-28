//-----------------------------------------------------------------------------
// Steam Hammer mod: HUD fixes. Loaded by handjob.cs after the other overlay scripts.
//
// 1. Health / stamina / hunger bars: the numbers are meant to sit inside the
//    small tube at the left end of each bar, but the HUD presets give them a
//    font that is scaled up with the screen, so they spill out of it. After
//    the presets are loaded the bars get a smaller number font. Adjust
//    $SH_Hud::barFontSize if needed (the SH presets use 18). Numbers are bold and centred.
//
// 2. Map: the compass had canHideOnFreelook set, so pressing Tab (freelook)
//    hid it together with the zoom buttons. The compass now stays.
//-----------------------------------------------------------------------------

$SH_Hud::barFontSize = 13;             // number text size inside the bar's end tube
$SH_Hud::barFontType = "Tahoma Bold";   // bold, easier to read (a font the game already uses)

// the bar numbers are drawn with GuiBarProfile: bold and centred in the tube.
// Profile fonts load when the HUD first shows, so this is set right away.
if (isObject(GuiBarProfile))
{
   GuiBarProfile.fontType = $SH_Hud::barFontType;
   GuiBarProfile.justify  = "center";
}

function SH_Hud_applyBars()
{
   if (isObject(GuiBarProfile))
   {
      GuiBarProfile.fontType = $SH_Hud::barFontType;
      GuiBarProfile.justify  = "center";
   }
   %bars = "HealthBar StaminaBar HungerBar HorseHealthBar HorseStaminaBar";
   for (%b = 0; %b < getWordCount(%bars); %b++)
   {
      %bar = getWord(%bars, %b);
      if (!isObject(%bar))
         continue;
      %before = %bar.getFontSize();
      %bar.setFontSize($SH_Hud::barFontSize);
      echo("[SH_Hud] " @ %bar @ ": number font " @ %before @ " -> " @ %bar.getFontSize());
   }
}

package SH_HudOverride
{
   function loadAllGuiProperty(%only_pos)
   {
      Parent::loadAllGuiProperty(%only_pos);
      SH_Hud_applyBars();
   }

   function createGuiMapWindow()
   {
      %wnd = Parent::createGuiMapWindow();
      // the compass is the window's plain GuiBitmapCtrl (the rest are buttons / the map)
      for (%i = 0; %i < %wnd.getCount(); %i++)
      {
         %child = %wnd.getObject(%i);
         if (%child.getClassName() $= "GuiBitmapCtrl")
            %child.canHideOnFreelook = "0";
      }
      return %wnd;
   }
};
activatePackage(SH_HudOverride);
