//-----------------------------------------------------------------------------
// Steam Hammer mod: combat mouse input.
//
// Left mouse (mouseFire, trigger 0 = attack) and right mouse (altTrigger,
// trigger 1 = block) had a 200 ms repeat guard in freelook that was also
// restarted by the button RELEASE, so a quick click right after letting go
// was swallowed together with its release - about half of fast punches never
// reached the server (seen in console.log). The guard now only counts from
// the last press. Everything else is the original SH logic
// (scripts/client/default.bindCommands.cs). Loaded by handjob.cs.
//-----------------------------------------------------------------------------

$SH_Combat::guard = 120;   // ms between two presses of the same button

package SH_CombatOverride
{
   function mouseFire(%val)
   {
      if (!$cmFreelookMode
         || (%val && (getSimTime() - $SH_Combat::press0) > $SH_Combat::guard)
         || (!%val && ($mvTriggerCount0 % 2) != 0))
      {
         if (%val)
            $SH_Combat::press0 = getSimTime();
         $mouseActionTime0 = getSimTime();
         $mvTriggerCount0++;
         if ($volleyInit && %val)
         {
            InitVolley();
            $volleyInit = false;
         }
         if (!isWarState())
            Lif_ContextMenu_OnPressedDefault(%val);
      }
   }

   function altTrigger(%val)
   {
      if (!$cmFreelookMode
         || (%val && (getSimTime() - $SH_Combat::press1) > $SH_Combat::guard)
         || (!%val && ($mvTriggerCount1 % 2) != 0))
      {
         if (%val)
            $SH_Combat::press1 = getSimTime();
         $mouseActionTime1 = getSimTime();
         $mvTriggerCount1++;
         if (!isWarState())
            Lif_ContextMenu_OnPressedContext(%val);
      }
   }
};
activatePackage(SH_CombatOverride);
