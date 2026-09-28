//-----------------------------------------------------------------------------
// Steam Hammer mod: chat tabs (client side of servermod/shinfo/sh_chatInfo.cs).
//
// - Tab counters: the number is part of each tab's text ("Global 0"), which
//   sh_client.exe never fills in. The server now sends the players online and
//   the player's guild members online; the tabs show "System" (no number),
//   "Global <players online>", "Guild <guild members online>".
// - GM chat: lines written by an online GM get a bold red name and bold green
//   text. sh_client.exe writes each line as
//     <spush><color:2d2b29>NAME: <spop><spush><color:RRGGBB>message
//   so the name/colour tags of GM lines are rewritten after they arrive.
// Loaded by handjob.cs after the other overlay scripts.
//-----------------------------------------------------------------------------

$SH_Chat::gmNameColor = "cc2222";
$SH_Chat::gmTextColor = "33bb33";
$SH_Chat::gmFont      = "Tahoma Bold:18";   // chat text is Tahoma 18 (GuiChatTabBookProfile)

function SH_Chat_find(%obj, %class, %out)
{
   if (%obj.getClassName() $= %class)
      %out = %out SPC %obj.getId();
   if (%obj.isMethod("getCount"))
      for (%i = 0; %i < %obj.getCount(); %i++)
         %out = SH_Chat_find(%obj.getObject(%i), %class, %out);
   return %out;
}

// "Global 0" -> "Global" (drops a trailing number)
function SH_Chat_base(%text)
{
   %n = getWordCount(%text);
   %last = getWord(%text, %n - 1);
   if (%n > 1 && %last $= (%last + 0))
      return getWords(%text, 0, %n - 2);
   return %text;
}

function SH_Chat_setTabText(%tab, %text)
{
   if (%tab.text $= %text)
      return;
   %tab.text = %text;
   if (%tab.isMethod("setText"))
      %tab.setText(%text);
}

function SH_Chat_updateTabs()
{
   if (!isObject(PlayGui))
      return;
   %tabs = trim(SH_Chat_find(PlayGui, "CmGuiChatTab", ""));
   // order as created by the game: System, Global, Guild
   for (%i = 0; %i < getWordCount(%tabs); %i++)
   {
      %tab = getWord(%tabs, %i);
      %base = SH_Chat_base(%tab.text);
      if (%i == 0)      SH_Chat_setTabText(%tab, %base);
      else if (%i == 1) SH_Chat_setTabText(%tab, %base SPC ($SH_Chat::global $= "" ? 0 : $SH_Chat::global));
      else if (%i == 2) SH_Chat_setTabText(%tab, %base SPC ($SH_Chat::guild  $= "" ? 0 : $SH_Chat::guild));
   }
}

// rewrite the lines of the GMs in one chat text
function SH_Chat_styleGM(%text)
{
   for (%g = 0; %g < getFieldCount($SH_Chat::gms); %g++)
   {
      %name = getField($SH_Chat::gms, %g);
      if (%name $= "")
         continue;
      %find = "<spush><color:2d2b29>" @ %name @ ": <spop><spush><color:";
      %repl = "<spush><font:" @ $SH_Chat::gmFont @ "><color:" @ $SH_Chat::gmNameColor @ ">" @ %name
            @ ": <spop><spush><font:" @ $SH_Chat::gmFont @ "><color:" @ $SH_Chat::gmTextColor @ ">";
      %out = "";
      // one-time log if a GM's line is in the chat in a form this does not recognise
      if (!$SH_Chat::formatLogged && strpos(%text, %name @ ":") >= 0 && strpos(%text, %find) < 0
          && strpos(%text, "<color:" @ $SH_Chat::gmNameColor @ ">" @ %name) < 0)
      {
         $SH_Chat::formatLogged = true;
         %at = strpos(%text, %name @ ":");
         %from = (%at > 60) ? %at - 60 : 0;
         echo("[SH_Chat] GM line not matched, raw text: " @ getSubStr(%text, %from, strlen(%name) + 100));
      }
      while ((%p = strpos(%text, %find)) >= 0)
      {
         // skip the original message colour "RRGGBB>"
         %out = %out @ getSubStr(%text, 0, %p) @ %repl;
         %text = getSubStr(%text, %p + strlen(%find) + 7, strlen(%text));
      }
      %text = %out @ %text;
   }
   return %text;
}

function SH_Chat_styleTabs()
{
   if (!isObject(PlayGui) || $SH_Chat::gms $= "")
      return;
   %tabs = trim(SH_Chat_find(PlayGui, "CmGuiChatTab", ""));
   for (%i = 0; %i < getWordCount(%tabs); %i++)
   {
      // a tab has two text panes (normal and split view), both named ChatTabMLText
      %texts = trim(SH_Chat_find(getWord(%tabs, %i), "GuiMLTextCtrl", ""));
      for (%j = 0; %j < getWordCount(%texts); %j++)
      {
         %ml = getWord(%texts, %j);
         if (%ml.internalName !$= "ChatTabMLText")
            continue;
         %old = %ml.getText();
         if (strlen(%old) == %ml.shLastLen)
            continue;                            // nothing new since last time
         %new = SH_Chat_styleGM(%old);
         if (%new !$= %old)
         {
            %ml.setText(%new);
            %scroll = %ml.getParent();           // each pane sits in its own scroll
            if (isObject(%scroll) && %scroll.isMethod("scrollToBottom"))
               %scroll.scrollToBottom();
         }
         %ml.shLastLen = strlen(%ml.getText());
      }
   }
}

function SH_Chat_tick()
{
   cancel($SH_Chat::event);
   SH_Chat_updateTabs();
   SH_Chat_styleTabs();
   SH_Chat_reportGM();
   $SH_Chat::event = schedule(1000, 0, SH_Chat_tick);
}

// GM mode is shown by the engine's red "GM" label (gmBar, updateGmIndication);
// the server cannot see it on the connection, so the client tells the server
// (scripts/server/sh_chatInfo.cs) - on change, and every 10 s while on (reconnects)
function SH_Chat_reportGM()
{
   if (!isObject(ServerConnection))
      return;
   %gm = (isObject(gmBar) && gmBar.isVisible()) ? 1 : 0;
   $SH_Chat::gmTicks++;
   if (%gm != $SH_Chat::iAmGM || (%gm && $SH_Chat::gmTicks >= 10))
   {
      if (%gm != $SH_Chat::iAmGM)
         echo("[SH_Chat] GM mode " @ (%gm ? "on" : "off") @ " - telling the server");
      $SH_Chat::iAmGM = %gm;
      $SH_Chat::gmTicks = 0;
      commandToServer('SH_GMMode', %gm);
   }
}

// sent by the server every 5 s: players online, guild members online, GM names (tab separated)
function clientCmdSH_ChatInfo(%online, %guild, %gms)
{
   $SH_Chat::global = %online;
   $SH_Chat::guild  = %guild;
   if ($SH_Chat::gms !$= %gms)
   {
      // GM list changed (someone switched GM mode on): restyle the lines
      // already in the chat too, not only new ones
      $SH_Chat::gms = %gms;
      SH_Chat_forgetStyled();
      echo("[SH_Chat] GMs online: " @ (%gms $= "" ? "none" : strreplace(%gms, "\t", ", ")));
   }
   if (!isEventPending($SH_Chat::event))
      SH_Chat_tick();
}

function SH_Chat_forgetStyled()
{
   if (!isObject(PlayGui))
      return;
   %texts = trim(SH_Chat_find(PlayGui, "GuiMLTextCtrl", ""));
   for (%i = 0; %i < getWordCount(%texts); %i++)
      getWord(%texts, %i).shLastLen = "";
}
