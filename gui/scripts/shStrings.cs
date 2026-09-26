//-----------------------------------------------------------------------------
// SteamHammer overlay: the handful of strings our own scripts need.
//
// The mod pack these scripts came from added five entries to
// data/cm_messages.xml (2308, and 2400-2403) and shipped that file plus eleven
// localised copies. We do not: cm_messages.xml is the game's own message table,
// 2,624 strings of shipped content, and republishing all of it to add five
// would be republishing the game — the one thing this channel promises not to
// do. Three and a half megabytes of someone else's text for five lines is also
// simply a bad trade.
//
// So GetMessageIDText() is wrapped instead. Ask for an id the game already has
// and you get the game's own text, in the player's own language, exactly as
// before. Ask for one of ours and you get ours. Nothing is overwritten and no
// shipped file is touched.
//
// The cost is honest and worth stating: our five strings are English only. If
// they ever need translating, they get translated here, in a file we own.
//-----------------------------------------------------------------------------

function shStringsInit()
{
   // Keyed by message id, so the override is a single lookup.
   $SH_Text[2308] = "Cannot add more of this item to container!";
   $SH_Text[2400] = "Language";
   $SH_Text[2401] = "Select Language";
   $SH_Text[2402] = "Language changed";
   $SH_Text[2403] = "Please restart the game for the new language to take effect.";
}

shStringsInit();

package SH_Strings
{
   function GetMessageIDText(%id)
   {
      // The game first: an id it knows is never shadowed by ours, so a future
      // patch that fills one of these in wins automatically.
      %shipped = Parent::GetMessageIDText(%id);
      if (%shipped !$= "")
         return %shipped;

      if ($SH_Text[%id] !$= "")
         return $SH_Text[%id];

      return %shipped;
   }
};
activatePackage(SH_Strings);
