//-----------------------------------------------------------------------------
// Steam Hammer mod: "Disconnect" in the Esc menu returns to character
// selection on the same server instead of the main menu.
//
// The Esc-menu button calls cmShowDisconnectMessage() (in the exe), which opens
// a confirmation box through MessageIDBoxDlg() with "disconnect();" on its
// confirm button. While that box is being created, the confirm callback gets
// SH_Disconnect_confirmed() in front of it, so only a confirmed Disconnect is
// remembered - Cancel changes nothing. The normal disconnect cleanup then runs
// and, instead of showing the main menu, the client rejoins the server it was
// on (same address and password), which opens the character list.
// Disconnects for any other reason (errors, kicks, time-outs) still go to the
// main menu. Loaded by handjob.cs after the other overlay scripts.
//-----------------------------------------------------------------------------

$SH_Disconnect::window = 60000;   // ms: the confirmed disconnect must finish within this time

function SH_Disconnect_confirmed()
{
   $SH_Disconnect::confirmedAt = getRealTime();
}

function SH_Disconnect_wrap(%callback)
{
   if (strstr(%callback, "disconnect") >= 0)
      return "SH_Disconnect_confirmed();" @ %callback;
   return %callback;
}

package SH_DisconnectOverride
{
   function cmShowDisconnectMessage()
   {
      $SH_Disconnect::asking = true;
      Parent::cmShowDisconnectMessage();
      $SH_Disconnect::asking = false;
   }

   function MessageIDBoxDlg(%dialog, %title, %message, %btn1Callback, %btn1Caption, %btn2Callback, %btn2Caption, %btn3Callback, %btn3Caption)
   {
      if ($SH_Disconnect::asking)
      {
         %btn1Callback = SH_Disconnect_wrap(%btn1Callback);
         %btn2Callback = SH_Disconnect_wrap(%btn2Callback);
         %btn3Callback = SH_Disconnect_wrap(%btn3Callback);
      }
      return Parent::MessageIDBoxDlg(%dialog, %title, %message, %btn1Callback, %btn1Caption, %btn2Callback, %btn2Caption, %btn3Callback, %btn3Caption);
   }

   function _disconnectedCleanupFinalDone(%needQuit)
   {
      // a locally hosted server (Yo's cphGameServer / cphMariaDB) shuts down first:
      // the parent reschedules this function until it is gone, keep the flag until then
      if (isObject(cphGameServer) || isObject(cphMariaDB))
         return Parent::_disconnectedCleanupFinalDone(%needQuit);
      %asked = $SH_Disconnect::confirmedAt !$= "" && getRealTime() - $SH_Disconnect::confirmedAt < $SH_Disconnect::window;
      $SH_Disconnect::confirmedAt = "";
      if (!%needQuit && %asked && $JoinGameAddress !$= "")
      {
         echo("[SH_Disconnect] returning to character selection on " @ $JoinGameAddress);
         joinToRemoteServer($JoinGameAddress, $JoinGamePassword);
         return;
      }
      Parent::_disconnectedCleanupFinalDone(%needQuit);
   }
};
activatePackage(SH_DisconnectOverride);
