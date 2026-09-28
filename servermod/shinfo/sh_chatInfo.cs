//-----------------------------------------------------------------------------
// Steam Hammer mod: chat tab counts, GM names and skill levels (server side).
//
// Every 5 seconds each connected player is sent:
//   - the number of players online          (shown on the Global tab)
//   - the number of their guild online       (shown on the Guild tab)
//   - the names of the players in GM mode    (their chat lines are styled
//                                            bold red name / bold green text)
// The client side is gui/scripts/chatOverride.cs; skill levels go to gui/scripts/skillTreeOverride.cs.
// Installed on the server - see servermod/shinfo/README.md.
//-----------------------------------------------------------------------------

$SH_ChatInfo::period = 5000;    // ms (GM mode switched on shows within 5 s)

function SH_ChatInfo_guildOf(%client)
{
   if (%client.isMethod("getGuildID"))
      return %client.getGuildID();
   %pl = %client.getControlObject();
   if (isObject(%pl) && %pl.isMethod("getGuildID"))
      return %pl.getGuildID();
   return 0;
}

function SH_ChatInfo_nameOf(%client)
{
   %pl = %client.getControlObject();
   if (isObject(%pl) && %pl.isMethod("getShapeName"))
      return %pl.getShapeName();
   return "";
}

// GM mode (switched on with /GM <password>): the client reports its red "GM"
// label (chatOverride.cs, SH_Chat_reportGM) - none of the server-side isGM()
// methods showed it in testing; they are still checked as well.
//
// The report is only a switch, never a right: any client can send
// commandToServer('SH_GMMode', 1) from its console. It is accepted only for a
// player the server already trusts - the admin mod's rights (SHAdmin_isAdmin,
// if servermod/shadmin is installed), else account.IsGM in the database, read
// with the same join the engine uses when it loads a character. A GM who only
// knows the /GM password but whose account is not flagged is not styled.
function serverCmdSH_GMMode(%client, %on)
{
   if (!%on)
   {
      %client.shGMMode = 0;
      SH_ChatInfo_tick();                 // tell everyone now, not in up to 5 s
      return;
   }
   if (isFunction("SHAdmin_isAdmin") && SHAdmin_isAdmin(%client))
   {
      %client.shGMMode = 1;
      SH_ChatInfo_tick();
      return;
   }
   %cid = %client.isMethod("getCharacterId") ? %client.getCharacterId() + 0 : 0;
   if (%cid <= 0)
      return;
   dbi.Select(%client, "SH_GMModeReply",
      "SELECT a.IsGM FROM `character` c JOIN `account` a ON a.ID = c.AccountID WHERE c.ID = " @ %cid);
}

function GameConnection::SH_GMModeReply(%this, %rs)
{
   %ok = isObject(%rs) && %rs.nextRecord() && %rs.getFieldValue("IsGM") > 0;
   if (isObject(%rs))
   {
      dbi.remove(%rs);
      %rs.delete();
   }
   if (!%ok)
   {
      echo("[SH_ChatInfo] GM mode report from " @ SH_ChatInfo_nameOf(%this) @ " ignored - account is not a GM");
      return;
   }
   %this.shGMMode = 1;
   SH_ChatInfo_tick();
}

function SH_ChatInfo_isGM(%client)
{
   if (%client.shGMMode)
      return true;
   if (%client.isMethod("isGM") && %client.isGM())
      return true;
   %cam = %client.camera;
   if (!isObject(%cam) && %client.isMethod("getCameraObject"))
      %cam = %client.getCameraObject();
   if (isObject(%cam) && %cam.isMethod("isGM") && %cam.isGM())
      return true;
   %pl = %client.getControlObject();
   if (isObject(%pl) && %pl.isMethod("isGM") && %pl.isGM())
      return true;
   return false;
}

function SH_ChatInfo_tick()
{
   cancel($SH_ChatInfo::event);
   %online = 0; %gms = "";
   %n = ClientGroup.getCount();
   for (%i = 0; %i < %n; %i++)
   {
      %c = ClientGroup.getObject(%i);
      if (!isObject(%c.getControlObject()))
         continue;                           // still loading / character selection
      %online++;
      %g = SH_ChatInfo_guildOf(%c);
      %guild[%i] = %g;
      if (%g > 0)
         %inGuild[%g]++;
      %isGM = SH_ChatInfo_isGM(%c);
      if (%isGM != %c.shWasGM)
      {
         %c.shWasGM = %isGM;
         echo("[SH_ChatInfo] " @ SH_ChatInfo_nameOf(%c) @ (%isGM ? " is now in GM mode" : " left GM mode"));
      }
      if (%isGM)
      {
         %nm = SH_ChatInfo_nameOf(%c);
         if (%nm !$= "")
            %gms = %gms $= "" ? %nm : %gms TAB %nm;
      }
   }
   for (%i = 0; %i < %n; %i++)
   {
      %c = ClientGroup.getObject(%i);
      if (!isObject(%c.getControlObject()))
         continue;
      %g = %guild[%i];
      commandToClient(%c, 'SH_ChatInfo', %online, %g > 0 ? %inGuild[%g] : 0, %gms);
   }
   $SH_ChatInfo::event = schedule($SH_ChatInfo::period, 0, SH_ChatInfo_tick);
}

$SH_ChatInfo::event = schedule($SH_ChatInfo::period, 0, SH_ChatInfo_tick);

//-----------------------------------------------------------------------------
// Skill levels for the client's skill window (gui/scripts/skillTreeOverride.cs):
// the client asks when the window opens; the player's rows of the skills table
// are read (SkillAmount has 7 decimals: 150000000 = 15) and sent back as
// "skillId level skillId level ..." in parts of 10 skills, the last part
// flagged. The skill window could not read them on its own.
//-----------------------------------------------------------------------------
function serverCmdSH_SkillLevels(%client)
{
   if (!%client.isMethod("getCharacterId"))
      return;
   %cid = %client.getCharacterId() + 0;
   if (%cid <= 0)
      return;
   dbi.Select(%client, "SH_SkillLevelsReply",
      "SELECT `SkillTypeID`, `SkillAmount` FROM `skills` WHERE `CharacterID` = " @ %cid);
}

function GameConnection::SH_SkillLevelsReply(%this, %rs)
{
   if (!isObject(%rs))
   {
      error("[SH_Skills] skill levels query: no record set (" @ %rs @ ")");
      return;
   }
   %out = "";
   %n = 0;
   while (%rs.nextRecord())
   {
      %lvl = mFloor(%rs.getFieldValue("SkillAmount") / 100000) / 100;   // 2 decimals
      %out = %out SPC %rs.getFieldValue("SkillTypeID") SPC %lvl;
      %n++;
      if (%n % 10 == 0)
      {
         commandToClient(%this, 'SH_SkillLevels', trim(%out), 0);
         %out = "";
      }
   }
   commandToClient(%this, 'SH_SkillLevels', trim(%out), 1);
   dbi.remove(%rs);                       // release the record set (the mod pack version leaked one per window opening)
   %rs.delete();
   if (!$SH_Skills::logged)
   {
      $SH_Skills::logged = true;
      echo("[SH_Skills] sent " @ %n @ " skill levels to " @ SH_ChatInfo_nameOf(%this));
   }
}
