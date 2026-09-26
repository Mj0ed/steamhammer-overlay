//-----------------------------------------------------------------------------
// Adds a "Language" option to the Esc menu (gui/scripts/mainMenuGui.cs), letting
// players switch $pref::language::pack in-game instead of editing prefs.cs.
//-----------------------------------------------------------------------------

// Endonyms - each language's own name for itself, so these are NOT looked up
// via GetMessageIDText/cm_messages.xml and never change with the UI language.
// Written as literal UTF-8: TorqueScript's \x escape only takes two hex digits
// and emits a raw (non-UTF-8) byte, so "\xf1"/"\x65e5" rendered as boxes/garbage.
// Keep this file saved as UTF-8.
function shLanguageEndonym(%code)
{
   switch$(%code)
   {
      case "":      return "English";
      case "de":    return "Deutsch";
      case "es":    return "Español";
      case "fr":    return "Français";
      case "it":    return "Italiano";
      case "jp":    return "日本語";
      case "kr":    return "한국어";
      case "pl":    return "Polski";
      case "pt-br": return "Português (Brasil)";
      case "ru":    return "Русский";
      case "tr":    return "Türkçe";
      case "zh-cn": return "简体中文";
      default:      return %code;
   }
}

// The Esc-menu caption font (PT Serif Bold outside kr/jp/zh-cn, see
// gui/profiles/profiles.cs) has no CJK/Hangul glyphs. Tahoma is what the game
// itself switches to for those packs (Windows font-links it to CJK fonts), so
// the language list uses it for every entry to render all scripts uniformly.
singleton GuiControlProfile(SH_LanguageButtonProfile : EscMenuButtonProfile)
{
   fontType = "Tahoma";
   fontSize = 24;
   fontColor   = "65 50 30";
   fontColorHL = "174 84 50";
   fontColorSEL = "174 84 50";
};

singleton GuiControlProfile(SH_LanguageCurrentProfile : SH_LanguageButtonProfile)
{
   fontColor = "84 121 48";
};

singleton GuiControlProfile(SH_LanguageTitleProfile : GuiBaseTextProfile)
{
   fontType = $GlobalCaptionFontName;
   fontSize = 28;
   fontColor = "65 50 30";
   justify = "center";
};

$SH_LanguageCount   = 12;
$SH_LanguageCode[0]  = "";
$SH_LanguageCode[1]  = "de";
$SH_LanguageCode[2]  = "es";
$SH_LanguageCode[3]  = "fr";
$SH_LanguageCode[4]  = "it";
$SH_LanguageCode[5]  = "jp";
$SH_LanguageCode[6]  = "kr";
$SH_LanguageCode[7]  = "pl";
$SH_LanguageCode[8]  = "pt-br";
$SH_LanguageCode[9]  = "ru";
$SH_LanguageCode[10] = "tr";
$SH_LanguageCode[11] = "zh-cn";

// Locale packs are only loaded at engine startup, so a switch needs a restart.
function shApplyLanguageSelection(%code)
{
   Canvas.popDialog(SH_LanguageDlg);

   if ($pref::language::pack $= %code)
      return;

   $pref::language::pack = %code;
   export("$pref::*", "data/prefs.cs", False);

   MessageBoxOK(GetMessageIDText(2402), GetMessageIDText(2403));
}

function shBuildLanguageDlg()
{
   if (isObject(SH_LanguageDlg))
      SH_LanguageDlg.delete();

   // layout: frame margin | title | 12 languages | gap | Back | frame margin
   %rowH    = 38;
   %margin  = 45;   // keeps text clear of menu_esc.png's corner pieces
   %titleH  = 40;
   %listTop = %margin + %titleH + 10;
   %backTop = %listTop + ($SH_LanguageCount * %rowH) + 12;
   %panelW  = 320;
   %panelH  = %backTop + %rowH + %margin;
   %btnW    = %panelW - (2 * %margin);

   %dlg = new GuiControl(SH_LanguageDlg)
   {
      profile = "GuiOverlayProfile";
      position = "0 0";
      extent = "100% 100%";
      horizSizing = "center";
      vertSizing = "center";
      isContainer = "1";
   };

   %panel = new GuiBitmapCtrl()
   {
      position = (0 - (%panelW / 2)) SPC (0 - (%panelH / 2));
      extent = %panelW SPC %panelH;
      horizSizing = "center";
      vertSizing = "center";
      profile = "GuiDefaultProfile";
      bitmap = "gui/images/menu_esc.png";
      isContainer = "1";
   };
   %dlg.add(%panel);

   %title = new GuiTextCtrl()
   {
      text = GetMessageIDText(2401); // Select Language
      position = %margin SPC %margin;
      extent = %btnW SPC %titleH;
      profile = "SH_LanguageTitleProfile";
   };
   %panel.add(%title);

   for (%i = 0; %i < $SH_LanguageCount; %i++)
   {
      %code = $SH_LanguageCode[%i];
      %btn = new GuiButtonCtrl()
      {
         text = shLanguageEndonym(%code);
         position = %margin SPC (%listTop + %i * %rowH);
         extent = %btnW SPC (%rowH - 4);
         // current language shown in green
         profile = ($pref::language::pack $= %code) ? "SH_LanguageCurrentProfile" : "SH_LanguageButtonProfile";
         buttonType = "PushButton";
         command = "shApplyLanguageSelection(\"" @ %code @ "\");";
      };
      %panel.add(%btn);
   }

   %closeBtn = new GuiButtonCtrl()
   {
      text = GetMessageIDText(1137); // Back
      position = %margin SPC %backTop;
      extent = %btnW SPC (%rowH - 4);
      profile = "SH_LanguageTitleProfile";
      buttonType = "PushButton";
      accelerator = "escape";
      command = "Canvas.popDialog(SH_LanguageDlg);";
   };
   %panel.add(%closeBtn);
}

function showLanguageOptions()
{
   shBuildLanguageDlg();
   Canvas.pushDialog(SH_LanguageDlg);
}

// Adds MainMenuGuiLanguageBtn to the existing Esc-menu panel (gui/forms/mainMenuGui.gui)
// the first time the menu is built, shifting the lower buttons down to make room.
function shEnsureLanguageMenuButton()
{
   if (isObject(MainMenuGuiLanguageBtn) || !isObject(MainMenuGuiBg))
      return;

   new GuiButtonCtrl(MainMenuGuiLanguageBtn)
   {
      text = GetMessageIDText(2400); // Language
      imageIndex = getEscMenuButton(1);
      horizSizing = "right";
      vertSizing = "bottom";
      position = "120 330";
      extent = "177 74";
      minExtent = "8 2";
      profile = "EscMenuButtonProfile";
      groupNum = "-1";
      buttonType = "PushButton";
      useMouseEvents = "0";
      visible = "1";
      active = "1";
      command = "hideEscMenu(); showLanguageOptions();";
      tooltipProfile = "GuiToolTipProfile";
      hovertime = "1000";
      isContainer = "0";
      canSave = "1";
      canSaveDynamicFields = "0";
   };
   MainMenuGuiBg.add(MainMenuGuiLanguageBtn);

   // Keep the stock 431x730 panel (stretching it distorts menu_esc.png) and
   // re-space all nine buttons evenly between the stock Help (y=50) and Back (~y=600).
   MainMenuGuiBg.extent = "431 730";
   %order = "MainMenuGuiHelpBtn MainMenuGuiControlsBtn MainMenuGuiVideoBtn MainMenuGuiAudioBtn"
        SPC "MainMenuGuiLanguageBtn MainMenuGuiPlayersBtn MainMenuGuiDisconnectBtn"
        SPC "MainMenuGuiExitBtn MainMenuGuiBackBtn";
   %y    = 50;
   %step = 68;
   for (%i = 0; %i < getWordCount(%order); %i++)
   {
      %btn = getWord(%order, %i);
      if (isObject(%btn))
         %btn.position = "120" SPC %y;
      %y += %step;
   }
}

// Full override of gui/scripts/mainMenuGui.cs::toggleEscMenu - last definition wins.
function toggleEscMenu(%show, %onlyOptions)
{
   shEnsureLanguageMenuButton();

   if(%show)
   {
      Canvas.pushDialog(MainMenuGui, 99); // under console

      // show/hide buttons
      MainMenuGuiHelpBtn.setVisible(!%onlyOptions);
      MainMenuGuiExitBtn.setVisible(!%onlyOptions);

      %isConnected = (isObject("ServerConnection") && isObject("PlayGui") && PlayGui.isAwake());
      MainMenuGuiPlayersBtn.setVisible(%isConnected);

      MainMenuGuiDisconnectBtn.setVisible(%isConnected && !%onlyOptions);

      // esc state
      $cmContextMenuMode = 1;
      fadeScreen(true);

      // cursor
      MainMenuGui.oldCursorShown = Canvas.isCursorShown();
      showCursor();
   }
   else
   {
      Canvas.popDialog(MainMenuGui);

      // esc state
      $cmContextMenuMode = 0;
      fadeScreen(false);

      // cursor
      if(!MainMenuGui.oldCursorShown)
         hideCursor();
   }
}
