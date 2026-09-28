//-----------------------------------------------------------------------------
// SteamHammer overlay: Female and Acribian characters in the character creator,
// and the voice list labels.
//
// NEEDS CONTENT THE OVERLAY DOES NOT SHIP. Female and Acribian characters use
// new player models (art/ModelsSH/3D/Mobiles/Characters/sh_female.dts and
// sh_acribian_male.dts), their player datablocks (art/datablocks/player.cs) and
// their parts in data/cm_customisation.xml; the female voices are extra
// art/sound/voices/*_Female_*.ogg files. None of that is in this repository -
// it is game content. So each part below switches itself on only when its model
// is installed, and on a stock install the creator behaves exactly as before.
//
// Female: stock gui/forms/createCharacterWindow.gui turned the Female button
// into a push button that only shows "Female characters will be available
// later" (CharacterWomenErrorMessage), and gui/scripts/createCharacterWindow.cs
// has the female handler commented out. Here the Female button becomes a normal
// radio button in the same group as Male, and selecting it calls the engine's
// GenderFemalePressed(), as the commented-out stock handler did.
//
// Acribian, without touching sh_client.exe: the engine's
// CreateCharacterNextPressed() refuses faction 2 (Acribians) with message 5021
// before it builds the create request. So when Acribians are selected, Next
// sends the character as faction 1 with race 2 as a marker (SH has race
// selection disabled and always uses race 1), and the database trigger in
// servermod/sql/sh_acribian_trigger.sql (BEFORE INSERT on `character`) turns
// Fraction=1/Race=2 back into Fraction=2 (Acribian) / Race=1 in the same insert.
// The marker values must match the trigger. Without the trigger on the server
// an Acribian comes out as a Technocrat - the client cannot tell.
//
// Voices: stock createCharacterWindow::InitVoices() labels the entries
// "Voice 2" @ %i - "Voice 20", "Voice 21", ... Here: "Voice 1", "Voice 2", ...
//-----------------------------------------------------------------------------
$SH_Char::FactionVictorian   = 1;
$SH_Char::FactionAcribian    = 2;
$SH_Char::RaceDefault        = 1;   // Auriunian - what setCharacterEur() selects
$SH_Char::RaceAcribianMarker = 2;   // Jorgrithian - never chosen by players in SH

$SH_Char::FemaleReady   = isFile("art/ModelsSH/3D/Mobiles/Characters/sh_female.dts");
$SH_Char::AcribianReady = isFile("art/ModelsSH/3D/Mobiles/Characters/sh_acribian_male.dts");

package SH_CharacterOverride
{
   function createCharacterFemaleBut::onStateChanged(%this, %state)
   {
      if (%state == 1 && $SH_Char::FemaleReady)
      {
         GenderFemalePressed();
         setCharacterEur();
      }
   }

   function createCharacterWindow::OnNextBtn()
   {
      if (!$SH_Char::AcribianReady || GetFraction() != $SH_Char::FactionAcribian)
      {
         Parent::OnNextBtn();
         return;
      }

      FractionChangePressed($SH_Char::FactionVictorian);
      RaceChangePressed($SH_Char::RaceAcribianMarker);
      CreateCharacterNextPressed();

      // Still in the creator (e.g. the name was rejected): restore the
      // Acribian selection so the player sees what they chose.
      if (isObject(createCharacterWindow) && createCharacterWindow.isAwake())
      {
         RaceChangePressed($SH_Char::RaceDefault);
         FractionChangePressed($SH_Char::FactionAcribian);
      }
   }

   function createComboStackSex(%combo_stack_ctrl, %group_num)
   {
      Parent::createComboStackSex(%combo_stack_ctrl, %group_num);

      if ($SH_Char::FemaleReady && isObject(createCharacterFemaleBut))
      {
         createCharacterFemaleBut.command    = "";
         createCharacterFemaleBut.groupNum   = createCharacterMaleBut.groupNum;
         createCharacterFemaleBut.buttonType = "RadioButton";
      }
   }

   // Same as the stock function but for the label ("Voice 2" @ %i gave "Voice 20").
   function createCharacterWindow::InitVoices()
   {
      createCharacterVoicePopUpMenu.clear();
      %numOfItems = GetNumberOfVoices();
      for (%i = 0; %i < %numOfItems; %i++)
         createCharacterVoicePopUpMenu.add("Voice " @ (%i + 1), %i);
      createCharacterVoicePopUpMenu.setSelected(0);
   }
};
activatePackage(SH_CharacterOverride);
