//-----------------------------------------------------------------------------
// SteamHammer modpack: Skill Tree ("Crafting / Combat / Minor") relayout.
//
// The stock createSkillsTable() is a NATIVE (engine-compiled) function, so it
// cannot be edited directly. TorqueScript function definitions fully shadow
// native console functions of the same name, so redefining createSkillsTable()
// here replaces the native grid layout with a Life is Feudal style layout
// (skill tree left, selected skill right - see "LiF-style skill window"
// below), while everything else (the window, the Crafting/Combat/Minor tabs,
// the skillcap bar) is left untouched -- those are built by
// gui/forms/skillStatWindow.gui (only names added to the old scrollbar
// decoration, so it can be hidden) and the original gui/scripts/skills.cs.
//
// Per-node unlock/fade/lock visuals are NOT reimplemented here: every skill
// node is still built with the original createSkillItem() + native
// GuiSkillItem::init(%id), exactly as the stock (dead) addSkill() code did,
// so the existing fade-until-unlocked behavior is preserved as-is.
//
// Skill display names are read directly from the data files the design team
// already edits (data/skill_types.xml, data/sh_skill_types.xml, and the
// per-language data/loc/<pack>/data/ overlays), so adding a new skill row to
// sh_skill_types.xml (plus the matching DB entry, as already done today)
// is automatically picked up next time this window is opened -- no hardcoded
// skill lists live in this file.
//-----------------------------------------------------------------------------

// This engine's SimXMLDocument has no elementValue(tag) convenience method --
// reading a named child's text requires push/getText/pop, confirmed against
// the only other working XML use in the codebase (run_once/repairHotbarWindow.cs).
function sh_XmlChildText(%xml, %childTag)
{
   %val = "";
   if (%xml.pushFirstChildElement(%childTag))
   {
      %val = %xml.getText();
      %xml.popElement();
   }
   return %val;
}

//-----------------------------------------------------------------------------
// Data cache: ID -> display Name, read from the same XML the game already
// uses. Built once per session (and again when the language changes): the
// lists here are counted arrays ($SH_RecipeCount[skill] ...), and assigning
// "" to the base name does NOT clear $x[n] entries in TorqueScript, so
// rebuilding on every open appended every recipe / material / item again
// (duplicates in the window). A rebuild deletes the old entries first.
//-----------------------------------------------------------------------------
function sh_BuildSkillNameCache()
{
   %key = $pref::language::pack @ "|" @ $pref::language::rootPath;
   if ($SH_CacheKey !$= "" && $SH_CacheKey $= %key)
      return;
   if ($SH_CacheKey !$= "")
   {
      %vars = "$SH_Skill* $SH_AllSkill* $SH_Recipe* $SH_Req* $SH_Equip* $SH_Obj* $SH_Message* $SH_EffectDesc* $SH_AbilityNameOverride*";
      for (%i = 0; %i < getWordCount(%vars); %i++)
         deleteVariables(getWord(%vars, %i));
   }
   $SH_CacheKey = %key;

   $SH_SkillName = ""; // clears the whole $SH_SkillName[%id] array
   $SH_SkillParent = "";
   $SH_SkillGroup = "";
   $SH_SkillIcon = "";
   $SH_SkillIsModded = "";
   $SH_SkillDescMsgId = "";
   $SH_AllSkillCount = 0;

   sh_BuildEffectDescCache();
   sh_BuildMessageCache();
   sh_BuildObjectInfoCache();
   sh_BuildRequirementCache();
   sh_BuildRecipeCache();

   sh_LoadSkillNamesFromMasterXML("data/skill_types.xml", false);
   sh_LoadSkillNamesFromMasterXML("data/sh_skill_types.xml", true);

   if ($pref::language::pack !$= "")
   {
      %locBase = $pref::language::rootPath @ "/" @ $pref::language::pack @ "/data/";
      sh_LoadSkillNamesFromLocaleXML(%locBase @ "skill_types.xml");
      sh_LoadSkillNamesFromLocaleXML(%locBase @ "sh_skill_types.xml");
      sh_LoadAbilityNamesFromLocaleXML(%locBase @ "skill_types_ability_name.xml");
      sh_LoadObjectNamesFromLocaleXML(%locBase @ "sh_objects_types_Name.xml");
      sh_LoadRecipeNamesFromLocaleXML(%locBase @ "sh_recipe_Name.xml");
      sh_LoadMessagesFromLocaleXML(%locBase @ "cm_messages.xml");
   }

   sh_BuildSkillHierarchy();
   sh_BuildEquipCache();
}

// One-time cache of every <effect id=""><description> in cm_effects.xml, so
// ability lines can show real flavor text instead of just their name.
function sh_BuildEffectDescCache()
{
   $SH_EffectDesc = "";

   %path = "data/cm_effects.xml";
   if (!isFile(%path))
      return;

   %xml = new SimXMLDocument();
   if (!%xml.loadFile(%path))
   {
      %xml.delete();
      return;
   }

   if (%xml.pushChildElement(0))
   {
      if (%xml.pushFirstChildElement("effect"))
      {
         %hasNode = true;
         while (%hasNode)
         {
            %id = %xml.attribute("id");
            %desc = sh_XmlChildText(%xml, "description");
            if (%id !$= "" && %desc !$= "")
               $SH_EffectDesc[%id] = %desc;

            %hasNode = %xml.nextSiblingElement("effect");
         }
      }
   }

   %xml.delete();
}

// One-time cache of every <string id=""> in cm_messages.xml, used to resolve
// a skill's <DescLvl0> message id into its actual description text.
function sh_BuildMessageCache()
{
   $SH_Message = "";

   %path = "data/cm_messages.xml";
   if (!isFile(%path))
      return;

   %xml = new SimXMLDocument();
   if (!%xml.loadFile(%path))
   {
      %xml.delete();
      return;
   }

   if (%xml.pushChildElement(0))
   {
      if (%xml.pushFirstChildElement("string"))
      {
         %hasNode = true;
         while (%hasNode)
         {
            %id = %xml.attribute("id");
            %text = %xml.getText();
            if (%id !$= "" && %text !$= "")
               $SH_Message[%id] = %text;

            %hasNode = %xml.nextSiblingElement("string");
         }
      }
   }

   %xml.delete();
}

// One-time cache of every recipe row in sh_recipe.xml, bucketed by
// SkillTypeID so the center recipe column can list every recipe that
// belongs to a given skill without rescanning the file per-skill. Also
// caches AbilityID -> RecipeID so the ability popup no longer rescans this
// same file on every click.
function sh_BuildRecipeCache()
{
   $SH_RecipeCount = "";
   $SH_RecipeId = "";
   $SH_RecipeName = "";
   $SH_RecipeLvl = "";
   $SH_RecipeResultObjId = "";
   $SH_RecipeQty = "";
   $SH_RecipeToolId = "";
   $SH_RecipeIdForAbility = "";
   $SH_RecipeNameOverride = "";

   %path = "data/sh_recipe.xml";
   if (!isFile(%path))
      return;

   %xml = new SimXMLDocument();
   if (!%xml.loadFile(%path))
   {
      %xml.delete();
      return;
   }

   if (%xml.pushChildElement(0))
   {
      if (%xml.pushFirstChildElement("row"))
      {
         %hasNode = true;
         while (%hasNode)
         {
            %id = sh_XmlChildText(%xml, "ID");
            %abilityId = sh_XmlChildText(%xml, "AbilityID");
            if (%abilityId !$= "" && %id !$= "")
               $SH_RecipeIdForAbility[%abilityId] = %id;

            %skillTypeId = sh_XmlChildText(%xml, "SkillTypeID");
            if (%skillTypeId !$= "")
            {
               %idx = $SH_RecipeCount[%skillTypeId];
               if (%idx $= "")
                  %idx = 0;
               $SH_RecipeId[%skillTypeId, %idx] = %id;
               $SH_RecipeName[%skillTypeId, %idx] = sh_XmlChildText(%xml, "Name");
               $SH_RecipeLvl[%skillTypeId, %idx] = sh_XmlChildText(%xml, "SkillLvl");
               $SH_RecipeResultObjId[%skillTypeId, %idx] = sh_XmlChildText(%xml, "ResultObjectTypeID");
               $SH_RecipeToolId[%skillTypeId, %idx] = sh_XmlChildText(%xml, "StartingToolsID");
               %qty = sh_XmlChildText(%xml, "Quantity");
               $SH_RecipeQty[%skillTypeId, %idx] = (%qty !$= "") ? %qty : 1;
               $SH_RecipeCount[%skillTypeId] = %idx + 1;
            }

            %hasNode = %xml.nextSiblingElement("row");
         }
      }
   }

   %xml.delete();
}

// One-time cache of every <row> in sh_objects_types.xml (Name + FaceImage
// only), keyed by ID. Recipe/material icons are looked up VERY often (every
// recipe card, every requirement row, across every category) -- without
// this cache each lookup re-loaded and linearly rescanned the whole
// ~960-row file from disk, which is what froze/crashed the game on open.
function sh_BuildObjectInfoCache()
{
   $SH_ObjName = "";
   $SH_ObjFace = "";
   $SH_ObjIsTool = "";

   %path = "data/sh_objects_types.xml";
   if (!isFile(%path))
      return;

   %xml = new SimXMLDocument();
   if (!%xml.loadFile(%path))
   {
      %xml.delete();
      return;
   }

   if (%xml.pushChildElement(0))
   {
      if (%xml.pushFirstChildElement("row"))
      {
         %hasNode = true;
         while (%hasNode)
         {
            %id = sh_XmlChildText(%xml, "ID");
            if (%id !$= "")
            {
               $SH_ObjName[%id] = sh_XmlChildText(%xml, "Name");
               $SH_ObjFace[%id] = sh_XmlChildText(%xml, "FaceImage");
               $SH_ObjIsTool[%id] = sh_XmlChildText(%xml, "IsTool");
            }

            %hasNode = %xml.nextSiblingElement("row");
         }
      }
   }

   %xml.delete();
}

// One-time cache of every <row> in sh_recipe_requirement.xml, bucketed by
// RecipeID -- same reasoning as sh_BuildObjectInfoCache above: this file was
// being fully reloaded and linearly rescanned (~960 rows) per recipe.
function sh_BuildRequirementCache()
{
   $SH_ReqCount = "";
   $SH_ReqObjId = "";
   $SH_ReqQty = "";

   %path = "data/sh_recipe_requirement.xml";
   if (!isFile(%path))
      return;

   %xml = new SimXMLDocument();
   if (!%xml.loadFile(%path))
   {
      %xml.delete();
      return;
   }

   if (%xml.pushChildElement(0))
   {
      if (%xml.pushFirstChildElement("row"))
      {
         %hasNode = true;
         while (%hasNode)
         {
            %recipeId = sh_XmlChildText(%xml, "RecipeID");
            if (%recipeId !$= "")
            {
               %idx = $SH_ReqCount[%recipeId];
               if (%idx $= "")
                  %idx = 0;
               $SH_ReqObjId[%recipeId, %idx] = sh_XmlChildText(%xml, "MaterialObjectTypeID");
               $SH_ReqQty[%recipeId, %idx] = sh_XmlChildText(%xml, "Quantity");
               $SH_ReqCount[%recipeId] = %idx + 1;
            }

            %hasNode = %xml.nextSiblingElement("row");
         }
      }
   }

   %xml.delete();
}

// Base/English data files: <table><row><ID>..</ID><Name>..</Name><abilities>...</abilities></row>...
function sh_LoadSkillNamesFromMasterXML(%path, %isModded)
{
   if (!isFile(%path))
      return;

   %xml = new SimXMLDocument();
   if (!%xml.loadFile(%path))
   {
      %xml.delete();
      return;
   }

   // pushChildElement(0) enters the document's root (<table>) element itself;
   // only THEN can pushFirstChildElement("row") find its named children.
   if (%xml.pushChildElement(0))
   {
      if (%xml.pushFirstChildElement("row"))
      {
         %hasNode = true;
         while (%hasNode)
         {
            %id = trim(sh_XmlChildText(%xml, "ID"));
            %name = sh_XmlChildText(%xml, "Name");
            if (%id !$= "" && %name !$= "")
               $SH_SkillName[%id] = %name;

            if (%id !$= "")
            {
               $SH_SkillParent[%id] = trim(sh_XmlChildText(%xml, "Parent"));
               $SH_SkillGroup[%id] = trim(sh_XmlChildText(%xml, "Group"));
               // XML stores Windows-style backslashes; setBitmap() needs forward slashes
               $SH_SkillIcon[%id] = strreplace(sh_XmlChildText(%xml, "Icon"), "\\", "/");
               $SH_SkillIsModded[%id] = %isModded;
               $SH_SkillDescMsgId[%id] = trim(sh_XmlChildText(%xml, "DescLvl0"));
               $SH_SkillPrimary[%id] = trim(sh_XmlChildText(%xml, "PrimaryStat"));
               $SH_SkillSecondary[%id] = trim(sh_XmlChildText(%xml, "SecondaryStat"));
               for (%ti = 0; %ti < 5; %ti++)
               {
                  %t = getWord("0 30 60 90 100", %ti);
                  $SH_SkillDescLvl[%id, %t] = trim(sh_XmlChildText(%xml, "DescLvl" @ %t));
               }

               $SH_AllSkillId[$SH_AllSkillCount] = %id;
               $SH_AllSkillCount++;

               sh_LoadAbilitiesForCurrentRow(%xml, %id);
            }

            %hasNode = %xml.nextSiblingElement("row");
         }
      }
   }

   %xml.delete();
}

// Buckets every cached skill under its <Parent> id, so the horizontal chain
// can walk modded skills (sh_skill_types.xml) that getChildSkill() never sees.
function sh_BuildSkillHierarchy()
{
   $SH_SkillChildCount = "";

   for (%i = 0; %i < $SH_AllSkillCount; %i++)
   {
      %id = $SH_AllSkillId[%i];
      %parent = $SH_SkillParent[%id];
      if (%parent $= "" || %parent == 0 || %parent == %id)
         continue;

      %n = $SH_SkillChildCount[%parent];
      if (%n $= "")
         %n = 0;

      $SH_SkillChild[%parent, %n] = %id;
      $SH_SkillChildCount[%parent] = %n + 1;
   }
}

// Reads the <abilities><ability lvl="" name="" id=""> children of the row the
// xml doc is CURRENTLY positioned on, and caches them per skill id.
function sh_LoadAbilitiesForCurrentRow(%xml, %skillId)
{
   $SH_SkillAbilityCount[%skillId] = 0;

   if (!%xml.pushFirstChildElement("abilities"))
      return;

   if (%xml.pushFirstChildElement("ability"))
   {
      %idx = 0;
      %hasAbility = true;
      while (%hasAbility)
      {
         %abilityId = %xml.attribute("id");
         %abilityName = %xml.attribute("name");
         %abilityLvl = %xml.attribute("lvl");
         %abilityIcon = sh_XmlChildText(%xml, "icon");

         // dig into <results><fight_effect><effect> for a description lookup
         %effectId = "";
         if (%xml.pushFirstChildElement("results"))
         {
            if (%xml.pushFirstChildElement("fight_effect"))
            {
               %effectId = trim(sh_XmlChildText(%xml, "effect"));
               %xml.popElement();
            }
            %xml.popElement();
         }

         if (%abilityId !$= "" && %abilityName !$= "")
         {
            %lvl = %abilityLvl;
            if (%lvl $= "")
               %lvl = 0;

            $SH_SkillAbilityId[%skillId, %idx] = %abilityId;
            $SH_SkillAbilityName[%skillId, %idx] = %abilityName;
            $SH_SkillAbilityLvl[%skillId, %idx] = %lvl;
            $SH_SkillAbilityIcon[%skillId, %idx] = %abilityIcon;
            $SH_SkillAbilityDesc[%skillId, %idx] = (%effectId !$= "") ? $SH_EffectDesc[%effectId] : "";
            %idx++;
         }

         %hasAbility = %xml.nextSiblingElement("ability");
      }
      $SH_SkillAbilityCount[%skillId] = %idx;

      %xml.popElement(); // undo pushFirstChildElement("ability")
   }

   %xml.popElement(); // undo pushFirstChildElement("abilities")
}

// Language overlay files: <root><strings><string id="X">Name</string>...
function sh_LoadSkillNamesFromLocaleXML(%path)
{
   if (!isFile(%path))
      return;

   %xml = new SimXMLDocument();
   if (!%xml.loadFile(%path))
   {
      %xml.delete();
      return;
   }

   if (%xml.pushChildElement(0))
   {
      if (%xml.pushFirstChildElement("strings"))
      {
         if (%xml.pushFirstChildElement("string"))
         {
            %hasNode = true;
            while (%hasNode)
            {
               %id = %xml.attribute("id");
               %name = %xml.getText();
               // don't let a blank/untranslated locale entry stomp the
               // English fallback that was already loaded
               if (%id !$= "" && %name !$= "")
                  $SH_SkillName[%id] = %name;

               %hasNode = %xml.nextSiblingElement("string");
            }
         }
      }
   }

   %xml.delete();
}

// Localized material/object names: data/loc/<lang>/data/sh_objects_types_Name.xml,
// same <root><strings><string id="objectTypeId">Name</string> format --
// overwrites $SH_ObjName in place so both recipe result names and material
// requirement text (sh_BuildRequirementsText) pick up the translation.
function sh_LoadObjectNamesFromLocaleXML(%path)
{
   if (!isFile(%path))
      return;

   %xml = new SimXMLDocument();
   if (!%xml.loadFile(%path))
   {
      %xml.delete();
      return;
   }

   if (%xml.pushChildElement(0))
   {
      if (%xml.pushFirstChildElement("strings"))
      {
         if (%xml.pushFirstChildElement("string"))
         {
            %hasNode = true;
            while (%hasNode)
            {
               %id = %xml.attribute("id");
               %name = %xml.getText();
               if (%id !$= "" && %name !$= "")
                  $SH_ObjName[%id] = %name;

               %hasNode = %xml.nextSiblingElement("string");
            }
         }
      }
   }

   %xml.delete();
}

// Localized recipe names: data/loc/<lang>/data/sh_recipe_Name.xml, keyed by
// recipe id (sh_recipe.xml's <ID>) -- cached separately from $SH_RecipeName
// (which is bucketed by skillTypeId/idx, not recipe id) and preferred over
// it in sh_CreateRecipeLine when present.
function sh_LoadRecipeNamesFromLocaleXML(%path)
{
   if (!isFile(%path))
      return;

   %xml = new SimXMLDocument();
   if (!%xml.loadFile(%path))
   {
      %xml.delete();
      return;
   }

   if (%xml.pushChildElement(0))
   {
      if (%xml.pushFirstChildElement("strings"))
      {
         if (%xml.pushFirstChildElement("string"))
         {
            %hasNode = true;
            while (%hasNode)
            {
               %id = %xml.attribute("id");
               %name = %xml.getText();
               if (%id !$= "" && %name !$= "")
                  $SH_RecipeNameOverride[%id] = %name;

               %hasNode = %xml.nextSiblingElement("string");
            }
         }
      }
   }

   %xml.delete();
}

// Localized messages (skill tier descriptions): data/loc/<lang>/data/cm_messages.xml
function sh_LoadMessagesFromLocaleXML(%path)
{
   if (!isFile(%path))
      return;
   %xml = new SimXMLDocument();
   if (!%xml.loadFile(%path))
   {
      %xml.delete();
      return;
   }
   if (%xml.pushChildElement(0))
   {
      if (%xml.pushFirstChildElement("strings"))
      {
         if (%xml.pushFirstChildElement("string"))
         {
            %hasNode = true;
            while (%hasNode)
            {
               %id = %xml.attribute("id");
               %text = %xml.getText();
               if (%id !$= "" && %text !$= "")
                  $SH_Message[%id] = %text;
               %hasNode = %xml.nextSiblingElement("string");
            }
         }
      }
   }
   %xml.delete();
}

// Localized ability names: data/loc/<lang>/data/skill_types_ability_name.xml,
// same <root><strings><string id="abilityId">Name</string> format, keyed by
// the <ability id=""> attribute (NOT the owning skill id).
function sh_LoadAbilityNamesFromLocaleXML(%path)
{
   if (!isFile(%path))
      return;

   %xml = new SimXMLDocument();
   if (!%xml.loadFile(%path))
   {
      %xml.delete();
      return;
   }

   if (%xml.pushChildElement(0))
   {
      if (%xml.pushFirstChildElement("strings"))
      {
         if (%xml.pushFirstChildElement("string"))
         {
            %hasNode = true;
            while (%hasNode)
            {
               %id = %xml.attribute("id");
               %name = %xml.getText();
               if (%id !$= "" && %name !$= "")
                  $SH_AbilityNameOverride[%id] = %name;

               %hasNode = %xml.nextSiblingElement("string");
            }
         }
      }
   }

   %xml.delete();
}

function sh_GetSkillName(%id)
{
   if ($SH_SkillName[%id] !$= "")
      return $SH_SkillName[%id];
   return "Skill" SPC %id;
}

// Skill's base (level 0) description, resolved from DescLvl0 -> cm_messages.xml.
function sh_GetSkillDesc(%id)
{
   %msgId = $SH_SkillDescMsgId[%id];
   if (%msgId !$= "" && $SH_Message[%msgId] !$= "")
      return $SH_Message[%msgId];
   return "";
}

// Best-effort: reuses the game's own (currently hidden/unused) skill-info
// panel to read back the player's current level for %skillTypeId, since
// there is no other script-exposed accessor for it. Returns -1 if unknown.
function sh_GetCurrentSkillLevel(%skillTypeId)
{
   // level read from the skill's node in the tree (sh_LifNodeLevel)
   if ($SH_LifLevel[%skillTypeId] !$= "")
      return $SH_LifLevel[%skillTypeId];

   if (!isObject(SkillInfoPanel))
      return -1;

   SkillInfoPanel.init(%skillTypeId);

   %valCtrl = SkillInfoPanel.findObjectByInternalName("ValueCtrl", true);
   if (!isObject(%valCtrl))
      return -1;

   %n = sh_ParseLeadingNumber(%valCtrl.getValue());
   if (%n == -1)
      %n = sh_ParseLeadingNumber(%valCtrl.getText());
   return %n;
}

function sh_ParseLeadingNumber(%str)
{
   %len = strlen(%str);
   %num = "";
   for (%i = 0; %i < %len; %i++)
   {
      %ch = getSubStr(%str, %i, 1);
      if (%ch >= "0" && %ch <= "9")
         %num = %num @ %ch;
      else if (%num !$= "")
         break;
   }
   if (%num $= "")
      return -1;
   return %num;
}

//-----------------------------------------------------------------------------
// One-time layout scaffold inside the existing GuiSkillPanel.
//-----------------------------------------------------------------------------

// Hides a named control if it currently exists -- used for base-game
// decoration we don't want in this override but shouldn't delete outright.
function sh_HideIfExists(%ctrl)
{
   if (isObject(%ctrl))
      %ctrl.setVisible(false);
}

// The mod pack named the four stock scroll-bar bitmaps (frame + track, vertical and
// horizontal) in gui/forms/skillStatWindow.gui so they could be hidden. The overlay
// does not ship that file, so the names are given here instead: in the stock form each
// pair is the two controls just before its slider (SkillsVerticalSlider /
// SkillsHorizontalSlider) in the same parent. Does nothing if they are already named.
function sh_NameSkillScrollParts()
{
   sh_NameBeforeSlider(SkillsVerticalSlider,   "SkillsVScrollFrame", "SkillsVScrollTrack");
   sh_NameBeforeSlider(SkillsHorizontalSlider, "SkillsHScrollFrame", "SkillsHScrollTrack");
}

function sh_NameBeforeSlider(%slider, %frameName, %trackName)
{
   if (isObject(%frameName) || !isObject(%slider))
      return;
   %p = %slider.getGroup();
   if (!isObject(%p))
      return;
   for (%i = 0; %i < %p.getCount(); %i++)
      if (%p.getObject(%i).getId() == %slider.getId())
         break;
   if (%i < 2 || %i >= %p.getCount())
      return;
   %p.getObject(%i - 2).setName(%frameName);
   %p.getObject(%i - 1).setName(%trackName);
}

//-----------------------------------------------------------------------------
// Floating popup shown when an ability node is clicked: lists its recipe's
// required materials (icon via sh_objects_types.xml FaceImage + quantity).
//-----------------------------------------------------------------------------
function sh_EnsureRequirementsPopup()
{
   if (isObject(SH_ReqPopup))
      return;

   new GuiControl(SH_ReqPopup)
   {
      profile = "GuiBorderGrayTextureProfile";
      position = "400 130";
      extent = "320 460";
      visible = false;

      new GuiTextCtrl(SH_ReqTitle)
      {
         position = "10 8";
         extent = "250 24";
         profile = "GuiSkillInfoNameProfile";
      };

      new GuiButtonCtrl()
      {
         position = "284 6";
         extent = "26 26";
         text = "X";
         profile = "GuiSkillStatBtnSkilsProfile";
         command = "SH_ReqPopup.setVisible(false);";
      };

      new GuiScrollCtrl(SH_ReqScroll)
      {
         position = "10 40";
         extent = "300 410";
         vScrollBar = "alwaysOn";
         hScrollBar = "alwaysOff";
         profile = "GuiCraftScrollProfile";
         constantThumbHeight = true;
         trackOffset = 8;
         thumbOffset = 16;          // as SH's own craft windows: the thumb is drawn where it is grabbed
         addContentHeight = 29;
         mouseWheelScrollSpeed = 36;  // small wheel steps: smooth scrolling

         new GuiStackControl(SH_ReqStack)
         {
            position = "4 4";
            extent = "280 8";
            minExtent = "8 8";
            profile = "SH_LifClearProfile";
            stackingType = "Vertical";
            changeChildSizeToFit = false;
            padding = 8;
         };
      };
   };

   GuiSkillPanel.add(SH_ReqPopup);
}

function sh_ShowAbilityRequirements(%skillId, %abilityId)
{
   sh_EnsureRequirementsPopup();

   %name = "";
   %count = $SH_SkillAbilityCount[%skillId];
   for (%i = 0; %i < %count; %i++)
   {
      if ($SH_SkillAbilityId[%skillId, %i] == %abilityId)
      {
         %name = $SH_SkillAbilityName[%skillId, %i];
         break;
      }
   }

   SH_ReqTitle.setText(%name);
   SH_ReqStack.clear();

   %recipeId = sh_FindRecipeIdForAbility(%abilityId);
   if (%recipeId != -1)
   {
      sh_PopulateRequirementsFromRecipe(SH_ReqStack, %recipeId);
   }
   else
   {
      %noReq = new GuiTextCtrl()
      {
         extent = "280 30";
         profile = "GuiItemRecipeTextProfile";
         text = "No crafting materials required for this action.";
      };
      SH_ReqStack.add(%noReq);
   }

   SH_ReqStack.updateStack();
   SH_ReqPopup.setVisible(true);
}

// Scans the cached recipe list for one whose AbilityID matches %abilityId.
function sh_FindRecipeIdForAbility(%abilityId)
{
   if ($SH_RecipeIdForAbility[%abilityId] !$= "")
      return $SH_RecipeIdForAbility[%abilityId];
   return -1;
}

// Adds one requirement row (icon + qty + name) per cached material entry
// belonging to %recipeId into %container. Skips a material already added
// for this recipe, in case the source data lists the same one twice.
function sh_PopulateRequirementsFromRecipe(%container, %recipeId)
{
   %count = $SH_ReqCount[%recipeId];
   if (%count $= "" || %count <= 0)
      return;

   for (%i = 0; %i < %count; %i++)
   {
      %objId = $SH_ReqObjId[%recipeId, %i];
      if (%seenObjId[%objId] == true)
         continue;
      %seenObjId[%objId] = true;

      %qty = $SH_ReqQty[%recipeId, %i];
      sh_AddRequirementRow(%container, %objId, %qty);
   }
}

// Looks up a single object type's Name + FaceImage from the object cache.
function sh_LookupObjectInfo(%objId)
{
   return $SH_ObjName[%objId] @ "\t" @ $SH_ObjFace[%objId];
}

function sh_AddRequirementRow(%container, %objId, %qty)
{
   %info = sh_LookupObjectInfo(%objId);
   %name = getField(%info, 0);
   %face = getField(%info, 1);
   if (%name $= "")
      %name = "Item" SPC %objId;

   %icon = new GuiBitmapCtrl()
   {
      position = "0 0";
      extent = "56 56";
      canHit = "false";
      profile = "GuiSkillStatImageProfile";
      imageIndex = getSkillItemPnl();
      centered = true;
   };
   if (%face !$= "")
      %icon.setBitmap(%face);

   %label = new GuiTextCtrl()
   {
      position = "66 16";
      extent = "210 30";
      profile = "GuiItemRecipeTextProfile";
      canHit = false;
      text = %qty @ "x" SPC %name;
   };

   %row = new GuiControl()
   {
      extent = "280 60";
      profile = "SH_LifClearProfile";
   };
   %row.add(%icon);
   %row.add(%label);

   %container.add(%row);
}

function sh_ApplySkillIcon(%node, %skillId)
{
   %iconPath = $SH_SkillIcon[%skillId];
   if (%iconPath !$= "")
      %node.setBitmap(%iconPath);
}

// Prefer our own Parent-based hierarchy (works for modded skills the native
// getChildSkill() doesn't know about); fall back to native if we have no data.
function sh_GetNextChainSkill(%id)
{
   %count = $SH_SkillChildCount[%id];
   if (%count !$= "" && %count > 0)
      return $SH_SkillChild[%id, 0];

   return getChildSkill(%id);
}

//-----------------------------------------------------------------------------
// LiF-style skill window (Steam Hammer colours).
//
//   left : the skill tree - one row per skill chain (parent > child), each
//          skill a node (native GuiSkillItem, so locked skills keep the
//          engine's own faded look) with its level, joined by brass lines
//   right: the selected skill - name, value, primary / secondary stat, the
//          0 / 30 / 60 / 90 / 100 tier bar, the tier's description and
//          abilities, and "Usable items" / "Recipes" tabs with item icons.
//          Clicking a recipe icon opens its materials; clicking an ability
//          opens the materials of its recipe (if it has one).
// Everything is built from the data files (skill_types.xml, sh_recipe.xml,
// sh_equipTypes.xml, sh_objects_types.xml + the language packs), and only for
// the selected skill, so opening the window stays fast.
//-----------------------------------------------------------------------------

$SH_Lif::Top      = 60;     // clears the skillcap bar at the top of GuiSkillPanel
$SH_Lif::Height   = 665;
$SH_Lif::TreeX    = 20;
$SH_Lif::TreeW    = 720;
$SH_Lif::InfoX    = 760;
$SH_Lif::InfoW    = 475;
$SH_Lif::Node     = 96;     // skill node size (picture)
$SH_Lif::NodeGap  = 170;    // node to next node in a chain (two chains side by side fit in the tree)
$SH_Lif::RowH     = 146;
$SH_Lif::Cell     = 72;     // item icon cell in the tabs
$SH_Lif::LockBtn  = 40;     // raise / keep / lower button on each skill node
$SH_Lif::Tiers    = "0 30 60 90 100";

function sh_LifProfiles()
{
   if (isObject(SH_LifTitleProfile))
      return;
   new GuiControlProfile(SH_LifTitleProfile : GuiItemRecipeTextProfile) { fontSize = 28; fontColor = "96 58 20"; fontColorNA = "96 58 20"; };
   new GuiControlProfile(SH_LifValueProfile : GuiItemRecipeTextProfile) { fontSize = 20; fontColor = "140 92 36"; justify = "right"; };
   new GuiControlProfile(SH_LifTextProfile : GuiItemRecipeTextProfile) { fontSize = 19; };
   new GuiControlProfile(SH_LifSmallProfile : GuiItemRecipeTextProfile) { fontSize = 16; justify = "center"; };
   new GuiControlProfile(SH_LifPanelProfile : GuiDefaultProfile) { opaque = true; fillColor = "83 71 49 38"; border = 1; borderThickness = 1; borderColor = "120 96 60 160"; };
   new GuiControlProfile(SH_LifLineProfile : GuiDefaultProfile) { opaque = true; fillColor = "150 104 44 255"; };
   new GuiControlProfile(SH_LifLineDimProfile : GuiDefaultProfile) { opaque = true; fillColor = "120 108 88 150"; };
   new GuiControlProfile(SH_LifSelProfile : GuiDefaultProfile) { opaque = false; border = 1; borderThickness = 3; borderColor = "196 136 44 255"; };
   new GuiControlProfile(SH_LifCellProfile : GuiDefaultProfile) { opaque = true; fillColor = "58 42 37 40"; border = 1; borderThickness = 1; borderColor = "120 96 60 200"; };
   // SH's GuiDefaultProfile is an opaque dark fill: click layers and
   // containers over icons must use this fully transparent one instead
   new GuiControlProfile(SH_LifClearProfile : GuiDefaultProfile)
   {
      opaque = false; border = 0;
      fillColor = "0 0 0 0"; fillColorHL = "0 0 0 0"; fillColorSEL = "0 0 0 0"; fillColorNA = "0 0 0 0";
      borderColor = "0 0 0 0"; borderColorHL = "0 0 0 0"; borderColorNA = "0 0 0 0";
   };
   // tier / tab buttons: text on a transparent button, the box behind it shows the state
   new GuiControlProfile(SH_LifButtonProfile : GuiItemRecipeTextProfile)
   {
      fontSize = 19; justify = "center"; opaque = false; border = 0;
      fontColor = "83 71 49"; fontColorHL = "58 42 37"; fontColorSEL = "58 42 37"; fontColorNA = "150 140 120";
   };
   new GuiControlProfile(SH_LifBoxOffProfile : GuiDefaultProfile) { opaque = true; fillColor = "214 200 166 255"; border = 1; borderThickness = 1; borderColor = "120 90 50 255"; };
   new GuiControlProfile(SH_LifBoxOnProfile : GuiDefaultProfile)  { opaque = true; fillColor = "205 150 70 255";  border = 1; borderThickness = 2; borderColor = "96 58 20 255"; };
   // the player's level on each skill node
   new GuiControlProfile(SH_LifLevelProfile : GuiItemRecipeTextProfile)
   {
      fontType = "Tahoma Bold"; fontSize = 18; justify = "center";
      opaque = true; fillColor = "58 42 37 215"; border = 1; borderThickness = 1; borderColor = "196 136 44 255";
      fontColor = "255 240 210"; fontColorNA = "255 240 210";
   };
}

// box behind tier %t (0-4) / tab %t (0-1): brass when selected
function sh_LifMarkBoxes(%prefix, %count, %sel)
{
   for (%i = 0; %i < %count; %i++)
   {
      %box = %prefix @ %i;
      if (isObject(%box))
         %box.setProfile(%i == %sel ? "SH_LifBoxOnProfile" : "SH_LifBoxOffProfile");
   }
}

// Old skill-map decoration (borders, corner swirls, scrollbars): the native
// window code can show it again, so it is hidden every time the window opens.
function sh_LifHideOldDecor()
{
   // the engine's table (run to read the levels) may reset the zoom panel
   GuiSkillPanel.minScale = 100;
   GuiSkillPanel.maxScale = 100;
   GuiSkillPanel.hScrollBar = "AlwaysOff";
   GuiSkillPanel.vScrollBar = "AlwaysOff";
   sh_NameSkillScrollParts();
   sh_HideIfExists(SkillsVScrollFrame);
   sh_HideIfExists(SkillsVScrollTrack);
   sh_HideIfExists(SkillsVerticalSlider);
   sh_HideIfExists(SkillsHScrollFrame);
   sh_HideIfExists(SkillsHScrollTrack);
   sh_HideIfExists(SkillsHorizontalSlider);
   %names = "SkillsDecorLeftBorder SkillsDecorRightBorder SkillsDecorUpBorder SkillsDecorDownBorder SkillsDecorUpRight SkillsDecorDownLeft SkillsDecorDownRight";
   for (%i = 0; %i < getWordCount(%names); %i++)
      sh_HideIfExists(GuiSkillPanel.findObjectByInternalName(getWord(%names, %i), true));
}

function sh_LifStatName(%s)
{
   switch$ (%s)
   {
      case "Str":  return "Strength";
      case "Agi":  return "Agility";
      case "Con":  return "Constitution";
      case "Will": return "Willpower";
      case "Int":  return "Intellect";
   }
   return %s;
}

// Text of the native skill-info panel's value line (e.g. "60.0000000/62.62").
function sh_LifValueText(%skillId)
{
   if ($SH_LifNativeLevel[%skillId] !$= "")
      return $SH_LifNativeLevel[%skillId] @ " / 100";
   if (!isObject(SkillInfoPanel))
      return "";
   SkillInfoPanel.init(%skillId);
   %valCtrl = SkillInfoPanel.findObjectByInternalName("ValueCtrl", true);
   if (!isObject(%valCtrl))
      return "";
   %v = %valCtrl.getValue();
   if (%v $= "")
      %v = %valCtrl.getText();
   if (%v $= "" && $SH_LifLevel[%skillId] !$= "")
      %v = $SH_LifLevel[%skillId];
   return %v;
}

// sh_equipTypes.xml: <object id=""><skillID/><skillAmount/> -> items per skill
function sh_BuildEquipCache()
{
   $SH_EquipCount = "";
   %path = "data/sh_equipTypes.xml";
   if (!isFile(%path))
      return;
   %xml = new SimXMLDocument();
   if (!%xml.loadFile(%path))
   {
      %xml.delete();
      return;
   }
   if (%xml.pushChildElement(0))
   {
      if (%xml.pushFirstChildElement("object"))
      {
         %hasNode = true;
         while (%hasNode)
         {
            %id = %xml.attribute("id");
            %skill = trim(sh_XmlChildText(%xml, "skillID"));
            if (%id !$= "" && %skill !$= "")
            {
               %n = $SH_EquipCount[%skill];
               if (%n $= "")
                  %n = 0;
               %amt = trim(sh_XmlChildText(%xml, "skillAmount"));
               $SH_EquipObj[%skill, %n] = %id;
               $SH_EquipLvl[%skill, %n] = (%amt $= "") ? 0 : %amt;
               $SH_EquipCount[%skill] = %n + 1;
            }
            %hasNode = %xml.nextSiblingElement("object");
         }
      }
   }
   %xml.delete();
}

//-----------------------------------------------------------------------------
// Scaffold inside the existing GuiSkillPanel (built once)
//-----------------------------------------------------------------------------
// Removes our controls (the layout is rebuilt when the window's size changed).
function sh_LifDestroyLayout()
{
   %names = "SH_LifTreeScroll SH_LifInfo SH_ReqPopup";
   for (%i = 0; %i < getWordCount(%names); %i++)
      if (isObject(getWord(%names, %i)))
         getWord(%names, %i).delete();
   for (%i = 0; %i < getWordCount($SH_Lif::DragBars); %i++)
      if (isObject(getWord($SH_Lif::DragBars, %i)))
         getWord($SH_Lif::DragBars, %i).delete();
   $SH_Lif::DragBars = "";
}

// "Character stats" next to Minor: the stock window has no way to the stats
// (Strength, Agility, Constitution, Intellect, Willpower with their up / down /
// pause lock buttons) - they are in the character window opened with P
// (showCharWindow -> showSkillStatDlg(false) -> statsWindow.gui). This button
// closes the skill window and opens that one, so the game's own stats panel -
// which the engine fills and whose lock buttons already work - is used as it is.
// Added to the same horizontal stack as the Craft / Combat / Minor buttons.
function sh_AddCharStatsButton()
{
   if (isObject(ShowCharStatsBtn) || !isObject(ShowMinorSkillBtn))
      return;
   %stack = ShowMinorSkillBtn.getGroup();
   if (!isObject(%stack))
      return;
   %stack.add(new GuiIconButtonCtrl(ShowCharStatsBtn)
   {
      extent = "150 30";
      HorizSizing = "left";
      vertSizing = "center";
      command = "sh_ShowCharStats();";
      profile = "GuiSkillStatBtnSkilsProfile";
      text = "Character stats";
      imageIndex = getStrengthIcon();
      renderBorder = false;
      textMargin = 5;
      autoSize = true;
   });
}

function sh_ShowCharStats()
{
   if (isFunction("closeSkillStatDlg"))
      closeSkillStatDlg();
   showCharWindow(1);
}

function sh_EnsureSkillTreeLayout()
{
   sh_LifProfiles();
   sh_AddCharStatsButton();
   // layout from the panel's real size (screens / window setups differ)
   %ext = GuiSkillPanel.extent;
   if (isObject(SH_LifTreeScroll) && $SH_Lif::BuiltFor $= %ext)
      return;
   if (isObject(SH_LifTreeScroll))
      sh_LifDestroyLayout();
   $SH_Lif::BuiltFor = %ext;
   %pw = getWord(%ext, 0);
   %ph = getWord(%ext, 1);
   $SH_Lif::Top = 60;                               // below the skillcap bar
   $SH_Lif::Height = %ph - 80;
   $SH_Lif::TreeX = 20;
   %iw = mFloor(%pw * 0.38);
   $SH_Lif::InfoW = (%iw < 360) ? 360 : ((%iw > 560) ? 560 : %iw);
   $SH_Lif::InfoX = %pw - $SH_Lif::InfoW - 20;
   $SH_Lif::TreeW = $SH_Lif::InfoX - 40;

   // flat layout: no zoom/pan, no outer scrollbars
   GuiSkillPanel.minScale = 100;
   GuiSkillPanel.maxScale = 100;
   GuiSkillPanel.hScrollBar = "AlwaysOff";
   GuiSkillPanel.vScrollBar = "AlwaysOff";
   GuiSkillPanel.scrollBarThickness = 0;
   %sp = GuiSkillPanel.position;
   %se = GuiSkillPanel.extent;
   GuiSkillPanel.resize(getWord(%sp, 0), getWord(%sp, 1), getWord(%se, 0), getWord(%se, 1));

   // the old skill map's scrollbar decoration (named in skillStatWindow.gui)
   sh_NameSkillScrollParts();
   sh_HideIfExists(SkillsVScrollFrame);
   sh_HideIfExists(SkillsVScrollTrack);
   sh_HideIfExists(SkillsVerticalSlider);
   sh_HideIfExists(SkillsHScrollFrame);
   sh_HideIfExists(SkillsHScrollTrack);
   sh_HideIfExists(SkillsHorizontalSlider);

   if (isObject(SkillsBackground))
   {
      SkillsBackground.imageIndex = -1;
      SkillsBackground.bitmap = "modpack/gui/images/skillTreeBackground";
   }

   // left: skill tree
   %tree = new GuiScrollCtrl(SH_LifTreeScroll)
   {
      position = $SH_Lif::TreeX SPC $SH_Lif::Top;
      extent = $SH_Lif::TreeW SPC $SH_Lif::Height;
      vScrollBar = "dynamic";
      hScrollBar = "alwaysOff";
      profile = "GuiCraftScrollProfile";
      constantThumbHeight = true;
      trackOffset = 8;
      thumbOffset = 16;          // as SH's own craft windows: the thumb is drawn where it is grabbed
      addContentHeight = 29;
      mouseWheelScrollSpeed = 36;  // small wheel steps: smooth scrolling
   };
   %tree.add(new GuiControl(SH_LifTreeCanvas)
   {
      position = "0 0";
      extent = ($SH_Lif::TreeW - 20) SPC $SH_Lif::Height;
      profile = "SH_LifPanelProfile";
   });
   GuiSkillPanel.add(%tree);
   sh_LifAddDragBar(GuiSkillPanel, %tree, SH_LifTreeCanvas);

   // right: selected skill
   %w = $SH_Lif::InfoW;
   %info = new GuiControl(SH_LifInfo)
   {
      position = $SH_Lif::InfoX SPC $SH_Lif::Top;
      extent = %w SPC $SH_Lif::Height;
      profile = "SH_LifPanelProfile";

      new GuiTextCtrl(SH_LifName)  { position = "16 8";  extent = (%w - 190) SPC "34"; profile = "SH_LifTitleProfile"; };
      new GuiTextCtrl(SH_LifValue) { position = (%w - 176) SPC "14"; extent = "160 26"; profile = "SH_LifValueProfile"; };
      new GuiTextCtrl(SH_LifStats) { position = "16 44"; extent = (%w - 32) SPC "22"; profile = "SH_LifTextProfile"; };
      new GuiMLTextCtrl(SH_LifReq) { position = "16 64"; extent = (%w - 32) SPC "16"; profile = "SH_LifTextProfile"; canHit = false; };
      new GuiControl(SH_LifTierLine) { position = "40 99"; extent = (%w - 80) SPC "3"; profile = "SH_LifLineProfile"; };
   };
   %step = mFloor((%w - 32 - 56) / 4);
   for (%t = 0; %t < 5; %t++)
   {
      %info.add(new GuiControl("SH_LifTierBox" @ %t)
      {
         position = (16 + %t * %step) SPC "80";
         extent = "56 40";
         profile = "SH_LifBoxOffProfile";
      });
      %info.add(new GuiButtonCtrl("SH_LifTier" @ %t)
      {
         position = (16 + %t * %step) SPC "80";
         extent = "56 40";
         text = getWord($SH_Lif::Tiers, %t);
         profile = "SH_LifButtonProfile";
         buttonType = "RadioButton";
         groupNum = 501;
         command = "sh_LifSelectTier(" @ %t @ ");";
      });
   }
   %body = new GuiScrollCtrl(SH_LifBodyScroll)
   {
      position = "8 130";
      extent = (%w - 16) SPC ($SH_Lif::Height - 138);
      vScrollBar = "dynamic";
      hScrollBar = "alwaysOff";
      profile = "GuiCraftScrollProfile";
      constantThumbHeight = true;
      trackOffset = 8;
      thumbOffset = 16;          // as SH's own craft windows: the thumb is drawn where it is grabbed
      addContentHeight = 29;
      mouseWheelScrollSpeed = 36;  // small wheel steps: smooth scrolling
   };
   %body.add(new GuiStackControl(SH_LifBody)
   {
      position = "4 4";
      extent = (%w - 44) SPC "8";
      minExtent = "8 8";
      profile = "SH_LifClearProfile";
      stackingType = "Vertical";
      changeChildSizeToFit = false;
      padding = 8;
   });
   %info.add(%body);
   sh_LifAddDragBar(%info, %body, SH_LifBody);
   GuiSkillPanel.add(%info);

   sh_EnsureRequirementsPopup();
}

//-----------------------------------------------------------------------------
// Scroll bar dragging: the scroll bars sit inside the old skill map's zoom
// panel, which takes the mouse drag, so dragging the lamp did nothing (only
// the wheel worked). An invisible mouse control over each scroll bar scrolls
// its list to where the bar is pressed / dragged.
//-----------------------------------------------------------------------------
function sh_LifAddDragBar(%parent, %scroll, %content)
{
   %p = %scroll.position;
   %e = %scroll.extent;
   %bar = new GuiMouseEventCtrl()
   {
      position = (getWord(%p, 0) + getWord(%e, 0) - 28) SPC getWord(%p, 1);
      extent = "28" SPC getWord(%e, 1);
      profile = "SH_LifClearProfile";
      lockMouse = true;
      class = "SH_LifDragBar";
   };
   %bar.scroll = %scroll;
   %bar.content = %content;
   %parent.add(%bar);
   $SH_Lif::DragBars = trim($SH_Lif::DragBars SPC %bar.getId());
}

function SH_LifDragBar::onMouseDown(%this, %modifier, %point, %clicks)
{
   %this.dragTo();
}

function SH_LifDragBar::onMouseDragged(%this, %modifier, %point, %clicks)
{
   %this.dragTo();
}

function SH_LifDragBar::dragTo(%this)
{
   // cursor position relative to the bar (the callback's point may be local or global)
   %y = getWord(Canvas.getCursorPos(), 1) - getWord(%this.getGlobalPosition(), 1);
   %h = getWord(%this.extent, 1);
   %f = (%y - 20) / (%h - 40);                    // ends of the track: top / bottom
   if (%f < 0)
      %f = 0;
   if (%f > 1)
      %f = 1;
   %max = getWord(%this.content.extent, 1) - getWord(%this.scroll.extent, 1);
   if (%max < 0)
      %max = 0;
   %this.scroll.setScrollPosition(0, mFloor(%f * %max));
}

//-----------------------------------------------------------------------------
// Tree
//-----------------------------------------------------------------------------

// Top-level skills of %group in the engine's order, then modded / new skills
// the engine does not list (sh_skill_types.xml ones only when they have
// abilities, skill_types.xml ones only from ID 81 - see the skill rework).
function sh_LifRoots(%group)
{
   %list = "";
   for (%b = getFirstBaseSkill(%group); %b !$= "" && %b != -1; %b = getNextBaseSkill(%group))
      %list = %list SPC %b;
   for (%s = getFirstSecondSkill(%group); %s !$= "" && %s != -1; %s = getNextSecondSkill(%group))
      %list = %list SPC %s;
   for (%i = 0; %i < $SH_AllSkillCount; %i++)
   {
      %id = $SH_AllSkillId[%i];
      if (!$SH_SkillIsModded[%id] && %id < 81)
         continue;
      if ($SH_SkillIsModded[%id] && !($SH_SkillAbilityCount[%id] > 0))
         continue;
      if ($SH_SkillGroup[%id] !$= "" && $SH_SkillGroup[%id] !$= %group)
         continue;
      %list = %list SPC %id;
   }
   // drop duplicates and children (they are drawn in their parent's chain)
   %out = "";
   for (%i = 0; %i < getWordCount(%list); %i++)
   {
      %id = getWord(%list, %i);
      if (%seen[%id])
         continue;
      %seen[%id] = true;
      %p = $SH_SkillParent[%id];
      if (%p !$= "" && %p != 0 && %p != %id && $SH_SkillName[%p] !$= "")
         continue;
      %out = %out SPC %id;
   }
   return trim(%out);
}

function sh_LifChain(%root)
{
   %chain = %root;
   %prev = %root;
   %child = sh_GetNextChainSkill(%root);
   for (%walked = 0; %child !$= "" && %child != -1 && %child != %prev && %walked < 8; %walked++)
   {
      %chain = %chain SPC %child;
      %prev = %child;
      %child = sh_GetNextChainSkill(%child);
   }
   return %chain;
}

function sh_LifBuildTree(%group)
{
   SH_LifTreeCanvas.clear();
   $SH_LifFirst = "";
   deleteVariables("$SH_LifNodePos*");        // arrays: assigning "" would not clear them
   deleteVariables("$SH_LifLevel*");
   deleteVariables("$SH_LifBadge*");
   deleteVariables("$SH_LifLockOf*");
   %roots = sh_LifRoots(%group);
   %n = getWordCount(%roots);
   %maxLen = 1;
   for (%i = 0; %i < %n; %i++)
   {
      %chain[%i] = sh_LifChain(getWord(%roots, %i));
      if (getWordCount(%chain[%i]) > %maxLen)
         %maxLen = getWordCount(%chain[%i]);
   }
   // one tree per row, rows pushed together in a brick pattern: every second
   // row is shifted right by half a node gap, so its skills sit between the
   // names of the row above (as in the user's mock-up). Two columns only when
   // one column cannot fit the page (Combat).
   // sizes from the space the window has (screens and window sizes differ):
   // skill pictures as large as the rows allow (64-96 px), else two columns
   %cols = 1;
   %rows = (%n > 0) ? %n : 1;
   %node = mFloor(($SH_Lif::Height - 48) / %rows) - 6;
   if (%node < 64 && %n > 1)
   {
      %cols = 2;
      %rows = mCeil(%n / 2);
      %node = mFloor(($SH_Lif::Height - 48) / %rows) - 6;
   }
   // ... and narrow enough for the longest chain in its column
   %maxNode = mFloor((($SH_Lif::TreeW - 40) / %cols - 46 - (%maxLen - 0.5) * 40) / (%maxLen + 0.5));
   if (%node > %maxNode)
      %node = %maxNode;
   if (%node > 96)
      %node = 96;
   if (%node < 56)
      %node = 56;
   $SH_Lif::Node = %node;
   $SH_Lif::LockBtn = mFloor(%node * 0.45);        // click area of the raise / keep / lower button
   $SH_Lif::RowH = %node + 6;
   %slotW = mFloor(($SH_Lif::TreeW - 40) / %cols);
   // chain width = (len - 1) * gap + node, plus the brick shift of half a gap
   %gap = mFloor((%slotW - 30 - $SH_Lif::Node - 16) / (%maxLen - 1 + 0.5));
   $SH_Lif::NodeGap = (%gap > 180) ? 180 : %gap;
   %shift = mFloor($SH_Lif::NodeGap / 2) + 16;

   %h = 48 + %rows * $SH_Lif::RowH;                // room for level + name under the last row
   $SH_Lif::LabelW = 2 * (%shift - mFloor($SH_Lif::Node / 2)) - 8;   // names stay clear of the next row
   if (%h < $SH_Lif::Height)
      %h = $SH_Lif::Height;
   SH_LifTreeCanvas.resize(0, 0, $SH_Lif::TreeW - 20, %h);

   SH_LifTreeCanvas.add(new GuiControl(SH_LifSelFrame)
   {
      position = "0 0";
      extent = ($SH_Lif::Node + 12) SPC ($SH_Lif::Node + 12);
      profile = "SH_LifSelProfile";
      visible = false;
   });

   for (%i = 0; %i < %n; %i++)
   {
      %col = %i % %cols;
      %row = mFloor(%i / %cols);
      %x = 30 + %col * %slotW + ((%row % 2) ? %shift : 0);
      %y = 10 + %row * $SH_Lif::RowH;
      %len = getWordCount(%chain[%i]);
      for (%k = 0; %k < %len; %k++)
      {
         %id = getWord(%chain[%i], %k);
         %nx = %x + %k * $SH_Lif::NodeGap;
         if (%k > 0)
         {
            %locked = sh_LifIsLocked(%id);
            SH_LifTreeCanvas.add(new GuiControl()
            {
               position = (%nx - $SH_Lif::NodeGap + $SH_Lif::Node) SPC (%y + mFloor($SH_Lif::Node / 2) - 2);
               extent = ($SH_Lif::NodeGap - $SH_Lif::Node) SPC "4";
               profile = %locked ? "SH_LifLineDimProfile" : "SH_LifLineProfile";
            });
         }
         sh_LifAddNode(%id, %nx, %y);
         if ($SH_LifFirst $= "")
            $SH_LifFirst = %id;
      }
   }
}

// The engine's node (skills.cs createSkillItem) is laid out in percentages of a
// wide 124x100 slot, with its brass frame drawn "centered" at its original
// small size - in a square node the picture was about a third of the node,
// barely visible. Same controls and internal names (GuiSkillItem::init fills
// them: picture, faded look, raise / keep / lower state), laid out for a
// square node: frame stretched over it, picture in the frame's opening.
function sh_LifCreateSkillItem()
{
   %gui = new GuiSkillItem()
   {
      Enabled = "1";
      Profile = "GuiSkillItemProfile";
      position = "0 0";
      extent = "100 100";
      MinExtent = "8 8";
      canSave = "1";
      Visible = "1";
      canHit = true;

      new GuiBitmapCtrl()
      {
         position = "18% 18%";
         extent = "64% 64%";
         visible = "true";
         internalName = "SkillImage";
      };

      new GuiBitmapCtrl()
      {
         position = "2% 2%";
         extent = "96% 96%";
         canHit = "false";
         visible = "true";
         profile = "GuiSkillStatImageProfile";
         imageIndex = getSkillItemPnl();
         centered = false;
         checkAlpha = false;
         internalName = "SkillBackground";
      };
   };

   %gui.add(new GuiBitmapCtrl()
   {
      position = "0% 0%";
      extent = "100% 100%";
      canHit = "false";
      visible = "false";
      profile = "GuiSkillStatImageProfile";
      internalName = "ActiveBorder";
      imageIndex = getSkillItemActiveBorder();
   });

   %gui.add(new GuiSkillLockButton()
   {
      position = "70% 70%";
      extent = "30% 30%";
      profile = "GuiSkillStatImageProfile";
      imageIndex = getSkillBtnStatus();
      internalName = "LockBtn";
   });
   return %gui;
}

function sh_LifAddNode(%id, %x, %y)
{
   %s = $SH_Lif::Node;
   %b = $SH_Lif::LockBtn;
   // native skill node: picture, locked/faded look, and the engine's own
   // raise / keep / lower button (GuiSkillLockButton "LockBtn"), which init()
   // binds to the skill and which sends the change to the server itself
   %icon = sh_LifCreateSkillItem();
   %icon.position = %x SPC %y;
   %icon.extent = %s SPC %s;
   // must stay hittable: in this engine a control with canHit = false also
   // blocks its children, i.e. the raise / keep / lower button
   %icon.canHit = true;
   %icon.init(%id);
   sh_ApplySkillIcon(%icon, %id);
   SH_LifTreeCanvas.add(%icon);
   %lock = %icon.findObjectByInternalName("LockBtn", false);
   if (isObject(%lock))
   {
      // in the bottom-right corner (sh_LifCreateSkillItem); the engine draws its
      // icon at a fixed small size, so a bigger click area on top presses it
      %lock.tooltipprofile = "GuiToolTipProfile";
      %lock.tooltip = "Raise / keep / lower this skill";
      $SH_LifLockOf[%id] = %lock;
   }

   %lvl = sh_LifNodeLevel(%icon, %id);
   $SH_LifLevel[%id] = %lvl;
   %label = sh_GetSkillName(%id);
   // the player's level in this skill, under the picture (between picture and
   // name); filled in when the server's answer arrives (clientCmdSH_SkillLevels)
   %badge = new GuiTextCtrl()
   {
      position = (%x + mFloor(%s / 2) - 18) SPC (%y + %s + 1);
      extent = "36 22";
      profile = "SH_LifLevelProfile";
      text = %lvl;
      canHit = false;
      visible = (%lvl !$= "");
   };
   SH_LifTreeCanvas.add(%badge);
   $SH_LifBadge[%id] = %badge;
   %lw = $SH_Lif::LabelW;
   SH_LifTreeCanvas.add(new GuiTextCtrl()
   {
      position = (%x + mFloor(%s / 2) - mFloor(%lw / 2)) SPC (%y + %s + 24);             // name under the level
      extent = %lw SPC "20";
      profile = "SH_LifSmallProfile";
      text = %label;
      canHit = false;
   });

   // click layer to select the skill - everything except the lock button corner
   %cmd = "sh_LifSelectSkill(" @ %id @ ");";
   SH_LifTreeCanvas.add(new GuiBitmapButtonCtrl()
   {
      position = %x SPC %y;
      extent = %s SPC (%s - %b);
      profile = "SH_LifClearProfile";
      tooltipprofile = "GuiToolTipProfile";
      tooltip = sh_GetSkillName(%id);
      command = %cmd;
   });
   SH_LifTreeCanvas.add(new GuiBitmapButtonCtrl()
   {
      position = %x SPC (%y + %s - %b);
      extent = (%s - %b) SPC %b;
      profile = "SH_LifClearProfile";
      tooltipprofile = "GuiToolTipProfile";
      tooltip = sh_GetSkillName(%id);
      command = %cmd;
   });
   // big click area over the raise / keep / lower button (bottom-right corner):
   // presses the engine's own button, which sends the change to the server
   if (isObject(%lock))
      SH_LifTreeCanvas.add(new GuiBitmapButtonCtrl()
      {
         position = (%x + %s - %b) SPC (%y + %s - %b);
         extent = %b SPC %b;
         profile = "SH_LifClearProfile";
         tooltipprofile = "GuiToolTipProfile";
         tooltip = "Raise / keep / lower this skill";
         command = "sh_LifPressLock(" @ %id @ ");";
      });
   $SH_LifNodePos[%id] = %x SPC %y;
}

function sh_LifPressLock(%id)
{
   %lock = $SH_LifLockOf[%id];
   if (isObject(%lock))
      %lock.performClick();
}

// Skill level for a node's label: the native node's getValue() (after init),
// else the skill-info panel's value field. Returns "" when neither gives a
// number. The raw values are written to the log once per session.
function sh_LifNodeLevel(%node, %id)
{
   // read from the engine's own level label (sh_LifProbeEnd)
   if ($SH_LifNativeLevel[%id] !$= "")
      return mFloor($SH_LifNativeLevel[%id]);
   %raw = %node.getValue();
   %val = SkillInfoPanel.findObjectByInternalName("ValueCtrl", true);
   if (isObject(SkillInfoPanel) && isObject(%val))
   {
      SkillInfoPanel.init(%id);
      %raw2 = %val.getValue();
   }
   if (!$SH_LifValuesLogged)
   {
      $SH_LifValuesLogged = true;
      echo("[SH_Lif] skill " @ %id @ " node getValue='" @ %raw @ "' info ValueCtrl getValue='" @ %raw2 @ "'");
   }
   %n = sh_ParseLeadingNumber(%raw);
   if (%n $= "" || %n == -1)
      %n = sh_ParseLeadingNumber(%raw2);
   return (%n $= "" || %n == -1) ? "" : %n;
}

//-----------------------------------------------------------------------------
// Skill tree rule (same as the server's data): a skill opens when its parent
// is at 30; the third skill of a chain also needs the first at 60
// (e.g. Cooking & Household 60 + Tailoring 30 -> Alchemy).
//-----------------------------------------------------------------------------
function sh_LifParentOf(%id)
{
   %p = $SH_SkillParent[%id];
   return (%p $= "" || %p == 0 || %p == %id) ? "" : %p;
}

function sh_LifIsLocked(%id)
{
   %p = sh_LifParentOf(%id);
   if (%p $= "")
      return false;
   if (sh_GetCurrentSkillLevel(%p) < 30)
      return true;
   %g = sh_LifParentOf(%p);
   return (%g !$= "" && sh_GetCurrentSkillLevel(%g) < 60);
}

// "Opens at: Tailoring 30" / "Opens at: Cooking & Household 60, Tailoring 30"
function sh_LifUnlockText(%id)
{
   %p = sh_LifParentOf(%id);
   if (%p $= "")
      return "";
   %t = sh_GetSkillName(%p) SPC 30;
   %g = sh_LifParentOf(%p);
   if (%g !$= "")
      %t = sh_GetSkillName(%g) SPC 60 @ ", " @ %t;
   return (sh_LifIsLocked(%id) ? "Opens at: " : "Opened by: ") @ %t;
}

//-----------------------------------------------------------------------------
// Selected skill
//-----------------------------------------------------------------------------
function sh_LifSelectSkill(%id)
{
   $SH_LifSel = %id;
   SH_ReqPopup.setVisible(false);
   if ($SH_LifNodePos[%id] !$= "")
   {
      SH_LifSelFrame.position = (getWord($SH_LifNodePos[%id], 0) - 6) SPC (getWord($SH_LifNodePos[%id], 1) - 6);
      SH_LifSelFrame.setVisible(true);
   }
   SH_LifName.setText(sh_GetSkillName(%id));
   SH_LifValue.setText(sh_LifValueText(%id));
   %stats = "";
   if ($SH_SkillPrimary[%id] !$= "")
      %stats = GetMessageIDText(1378) @ ": " @ sh_LifStatName($SH_SkillPrimary[%id]);
   if ($SH_SkillSecondary[%id] !$= "")
      %stats = %stats @ "      " @ GetMessageIDText(1379) @ ": " @ sh_LifStatName($SH_SkillSecondary[%id]);
   SH_LifStats.setText(%stats);
   %req = sh_LifUnlockText(%id);
   SH_LifReq.setText(%req $= "" ? "" : "<font:" @ $GlobalTextFontName @ ":15><color:" @ (sh_LifIsLocked(%id) ? "A03020" : "4A7A2A") @ ">" @ %req);

   // tab: recipes for skills that have some, usable items otherwise
   $SH_LifTab = ($SH_RecipeCount[%id] > 0) ? 1 : 0;
   $SH_LifRecipe = "";
   SH_LifTier0.setStateOn(true);
   sh_LifSelectTier(0);
}

function sh_LifSelectTier(%t)
{
   $SH_LifTier = %t;
   sh_LifMarkBoxes("SH_LifTierBox", 5, %t);
   sh_LifFillBody();
}

function sh_LifSelectTab(%tab)
{
   $SH_LifTab = %tab;
   sh_LifFillBody();
}

function sh_LifFillBody()
{
   %id = $SH_LifSel;
   if (%id $= "")
      return;
   SH_LifBody.clear();
   SH_ReqPopup.setVisible(false);
   %w = $SH_Lif::InfoW - 44;
   %lo = getWord($SH_Lif::Tiers, $SH_LifTier);
   %hi = ($SH_LifTier < 4) ? getWord($SH_Lif::Tiers, $SH_LifTier + 1) - 1 : 100;
   %cur = sh_GetCurrentSkillLevel(%id);

   // tier description
   %msg = $SH_SkillDescLvl[%id, %lo];
   %desc = (%msg !$= "") ? $SH_Message[%msg] : "";
   %descH = 24 * (mCeil(strlen(%desc) * 9 / %w) + getRecordCount(%desc) - 1);
   if (%desc !$= "")
      SH_LifBody.add(new GuiMLTextCtrl()
      {
         extent = %w SPC %descH;
         profile = "SH_LifTextProfile";
         text = %desc;
         canHit = false;
      });

   // abilities opened in this tier
   %count = $SH_SkillAbilityCount[%id];
   for (%i = 0; %i < %count; %i++)
   {
      %lvl = $SH_SkillAbilityLvl[%id, %i];
      if (%lvl < %lo || %lvl > %hi)
         continue;
      %abilityId = $SH_SkillAbilityId[%id, %i];
      %name = $SH_SkillAbilityName[%id, %i];
      if ($SH_AbilityNameOverride[%abilityId] !$= "")
         %name = $SH_AbilityNameOverride[%abilityId];
      if (%shownAbility[%name])
         continue;                     // SH has some abilities twice under one name (Cut Down)
      %shownAbility[%name] = true;
      %locked = (%cur != -1 && %lvl > %cur);
      %text = (%locked ? "<color:9A8F78>" : "<color:534731>") @ "•  " @ %name;
      if (%lvl > %lo)
         %text = %text @ "  (" @ %lvl @ ")";
      %line = new GuiControl() { extent = %w SPC "28"; profile = "SH_LifClearProfile"; };
      %line.add(new GuiMLTextCtrl() { position = "10 3"; extent = (%w - 10) SPC "24"; profile = "SH_LifTextProfile"; text = %text; canHit = false; });
      %line.add(new GuiBitmapButtonCtrl()
      {
         position = "0 0";
         extent = %w SPC "28";
         profile = "SH_LifClearProfile";
         command = "sh_ShowAbilityRequirements(" @ %id @ "," @ %abilityId @ ");";
      });
      SH_LifBody.add(%line);
   }

   // tabs
   %tabs = new GuiControl() { extent = %w SPC "44"; profile = "SH_LifClearProfile"; };
   %tw = mFloor((%w - 8) / 2);
   %tabs.add(new GuiControl(SH_LifTabBox0) { position = "0 6"; extent = %tw SPC "36"; profile = "SH_LifBoxOffProfile"; });
   %tabs.add(new GuiControl(SH_LifTabBox1) { position = (%tw + 8) SPC "6"; extent = %tw SPC "36"; profile = "SH_LifBoxOffProfile"; });
   %tabs.add(new GuiButtonCtrl(SH_LifTabItems)
   {
      position = "0 6"; extent = %tw SPC "36"; text = "Usable items";
      profile = "SH_LifButtonProfile"; buttonType = "RadioButton"; groupNum = 502;
      command = "sh_LifSelectTab(0);";
   });
   %tabs.add(new GuiButtonCtrl(SH_LifTabRecipes)
   {
      position = (%tw + 8) SPC "6"; extent = %tw SPC "36"; text = "Recipes";
      profile = "SH_LifButtonProfile"; buttonType = "RadioButton"; groupNum = 502;
      command = "sh_LifSelectTab(1);";
   });
   SH_LifBody.add(%tabs);
   if ($SH_LifTab == 1)
      SH_LifTabRecipes.setStateOn(true);
   else
      SH_LifTabItems.setStateOn(true);
   sh_LifMarkBoxes("SH_LifTabBox", 2, $SH_LifTab);

   // icon grid for the tab
   %n = 0;
   if ($SH_LifTab == 1)
   {
      %rc = $SH_RecipeCount[%id];
      for (%i = 0; %i < %rc; %i++)
      {
         %res = $SH_RecipeResultObjId[%id, %i];
         %lvl = $SH_RecipeLvl[%id, %i];
         if ($SH_ObjName[%res] $= "" || %lvl < %lo || %lvl > %hi)
            continue;          // LiF leftovers (result not in SH) and other tiers
         if (%seenRes[%res])
            continue;          // one icon per item: its details list every recipe (station) that makes it
         %seenRes[%res] = true;
         %rid = $SH_RecipeId[%id, %i];
         %name = $SH_RecipeNameOverride[%rid];
         if (%name $= "")
            %name = $SH_RecipeName[%id, %i];
         if (%name $= "")
            %name = $SH_ObjName[%res];
         %cellObj[%n] = %res;
         %cellName[%n] = %name @ ((%lvl > 0) ? " (" @ %lvl @ ")" : "");
         %cellCmd[%n] = "sh_LifShowRecipe(" @ %id @ "," @ %i @ ");";
         %cellIdx[%n] = %i;
         %n++;
      }
   }
   else
   {
      %ec = $SH_EquipCount[%id];
      for (%i = 0; %i < %ec; %i++)
      {
         %obj = $SH_EquipObj[%id, %i];
         %lvl = $SH_EquipLvl[%id, %i];
         if ($SH_ObjName[%obj] $= "" || %lvl < %lo || %lvl > %hi || %seenObj[%obj])
            continue;
         %seenObj[%obj] = true;
         %cellObj[%n] = %obj;
         %cellName[%n] = $SH_ObjName[%obj] @ ((%lvl > 0) ? " (" @ %lvl @ ")" : "");
         %cellCmd[%n] = "";
         %n++;
      }
   }

   %c = $SH_Lif::Cell;
   %perRow = mFloor((%w + 6) / (%c + 6));
   %rows = mCeil(%n / %perRow);
   %grid = new GuiControl() { extent = %w SPC ((%rows > 0) ? %rows * (%c + 6) : 30); profile = "SH_LifClearProfile"; };
   if (%n == 0)
      %grid.add(new GuiTextCtrl() { position = "4 4"; extent = %w SPC "24"; profile = "SH_LifTextProfile"; text = "Nothing at this level."; canHit = false; });
   for (%k = 0; %k < %n; %k++)
   {
      %cx = (%k % %perRow) * (%c + 6);
      %cy = mFloor(%k / %perRow) * (%c + 6);
      %selCell = ($SH_LifTab == 1 && $SH_LifRecipe $= %id SPC %cellIdx[%k]);
      %cell = new GuiControl() { position = %cx SPC %cy; extent = %c SPC %c; profile = %selCell ? "SH_LifBoxOnProfile" : "SH_LifCellProfile"; };
      %face = $SH_ObjFace[%cellObj[%k]];
      if (%face !$= "")
      {
         %img = new GuiBitmapCtrl() { position = "4 4"; extent = (%c - 8) SPC (%c - 8); profile = "SH_LifClearProfile"; canHit = false; };
         %img.setBitmap(%face);
         %cell.add(%img);
      }
      else // no icon in the SH data: show the name
         %cell.add(new GuiMLTextCtrl() { position = "3 3"; extent = (%c - 6) SPC (%c - 6); profile = "SH_LifSmallProfile"; text = "<font:" @ $GlobalTextFontName @ ":13>" @ %cellName[%k]; canHit = false; });
      %cell.add(new GuiBitmapButtonCtrl()
      {
         position = "0 0";
         extent = %c SPC %c;
         profile = "SH_LifClearProfile";
         tooltipprofile = "GuiToolTipProfile";
         tooltip = %cellName[%k];
         command = %cellCmd[%k];
      });
      %grid.add(%cell);
   }
   SH_LifBody.add(%grid);

   // details of the clicked recipe (when it is in this skill, tier and tab)
   if ($SH_LifTab == 1 && getWord($SH_LifRecipe, 0) $= %id)
   {
      %ri = getWord($SH_LifRecipe, 1);
      %rl = $SH_RecipeLvl[%id, %ri];
      if (%rl >= %lo && %rl <= %hi)
         sh_LifAddRecipeInfo(%id, %ri, %w);
   }
   SH_LifBody.updateStack();
}

// Recipe materials in the popup (level, station / tool, materials).
// Clicking a recipe icon: its details are shown under the icons (right panel).
function sh_LifShowRecipe(%skillId, %idx)
{
   $SH_LifRecipe = %skillId SPC %idx;
   sh_LifFillBody();
   if (isObject(SH_LifRecipeInfo))
      SH_LifBodyScroll.scrollToObject(SH_LifRecipeInfo);
}

// Recipe details: name, level, station / tool, materials with their icons.
function sh_LifAddRecipeInfo(%skillId, %idx, %w)
{
   %rid = $SH_RecipeId[%skillId, %idx];
   %res = $SH_RecipeResultObjId[%skillId, %idx];
   %name = $SH_RecipeNameOverride[%rid];
   if (%name $= "")
      %name = $SH_RecipeName[%skillId, %idx];
   if (%name $= "")
      %name = $SH_ObjName[%res];

   %box = new GuiStackControl(SH_LifRecipeInfo)
   {
      extent = %w SPC "8";
      minExtent = "8 8";
      profile = "SH_LifClearProfile";
      stackingType = "Vertical";
      changeChildSizeToFit = false;
      padding = 4;
   };
   %box.add(new GuiControl() { extent = %w SPC "2"; profile = "SH_LifLineProfile"; });
   %head = new GuiControl() { extent = %w SPC "64"; profile = "SH_LifClearProfile"; };
   %img = new GuiBitmapCtrl() { position = "0 4"; extent = "56 56"; profile = "SH_LifClearProfile"; canHit = false; };
   if ($SH_ObjFace[%res] !$= "")
      %img.setBitmap($SH_ObjFace[%res]);
   %head.add(%img);
   %head.add(new GuiMLTextCtrl() { position = "66 18"; extent = (%w - 66) SPC "30"; profile = "SH_LifTextProfile"; text = "<font:" @ $GlobalTextFontName @ ":22><color:603A14>" @ %name; canHit = false; });
   %box.add(%head);

   // every recipe of this skill that makes this item (usually one per station:
   // campfire / small power hammer / big power hammer), each recipe once
   %ways = 0;
   %rc = $SH_RecipeCount[%skillId];
   for (%j = 0; %j < %rc; %j++)
   {
      %jid = $SH_RecipeId[%skillId, %j];
      if ($SH_RecipeResultObjId[%skillId, %j] !$= %res || %seenRid[%jid])
         continue;
      %seenRid[%jid] = true;
      %ways++;
      %way[%ways] = %j;
   }
   for (%v = 1; %v <= %ways; %v++)
   {
      %j = %way[%v];
      %vid = $SH_RecipeId[%skillId, %j];
      %count = $SH_ReqCount[%vid];
      // the station: StartingToolsID, else the requirement marked as a tool
      // (its number is how often it is used, not an amount to bring)
      %tool = $SH_RecipeToolId[%skillId, %j];
      if (%tool $= "" || %tool == 0)
      {
         %tool = "";
         for (%i = 0; %i < %count && %tool $= ""; %i++)
            if ($SH_ObjIsTool[$SH_ReqObjId[%vid, %i]] == 1)
               %tool = $SH_ReqObjId[%vid, %i];
      }
      %line = (%ways > 1 ? %v @ ".  " : "") @ ((%tool !$= "" && $SH_ObjName[%tool] !$= "") ? $SH_ObjName[%tool] : "By hand");
      %line = %line @ "   |   Skill level " @ $SH_RecipeLvl[%skillId, %j] @ "   |   Makes " @ $SH_RecipeQty[%skillId, %j] @ "x";
      %box.add(new GuiMLTextCtrl() { extent = %w SPC "26"; profile = "SH_LifTextProfile"; text = "<color:603A14>" @ %line; canHit = false; });

      if (%count $= "" || %count <= 0)
         %box.add(new GuiTextCtrl() { extent = %w SPC "26"; profile = "SH_LifTextProfile"; text = "   No materials required."; canHit = false; });
      for (%i = 0; %i < %count; %i++)
      {
         %obj = $SH_ReqObjId[%vid, %i];
         if (%seen[%v, %obj] || %obj == %tool)
            continue;
         %seen[%v, %obj] = true;
         %mname = ($SH_ObjName[%obj] !$= "") ? $SH_ObjName[%obj] : "Item" SPC %obj;
         %row = new GuiControl() { extent = %w SPC "40"; profile = "SH_LifClearProfile"; };
         %mi = new GuiBitmapCtrl() { position = "10 2"; extent = "36 36"; profile = "SH_LifClearProfile"; canHit = false; };
         if ($SH_ObjFace[%obj] !$= "")
            %mi.setBitmap($SH_ObjFace[%obj]);
         %row.add(%mi);
         %qty = ($SH_ObjIsTool[%obj] == 1) ? "" : $SH_ReqQty[%vid, %i] @ "x  ";
         %row.add(new GuiTextCtrl() { position = "56 8"; extent = (%w - 56) SPC "26"; profile = "SH_LifTextProfile"; text = %qty @ %mname; canHit = false; });
         %box.add(%row);
      }
   }
   %box.updateStack();
   SH_LifBody.add(%box);
}

//-----------------------------------------------------------------------------
// The player's level of each skill of the current tab: the engine's own
// createSkillsTable() builds a node (createSkillItem) and a level label
// (createSkillLevel -> SkillValTxt) per skill and places it with
// getSkillItemPosition(skill id); those calls are recorded (package below),
// the labels read, and everything the engine added is removed again.
// sh_LifProbeBegin(); Parent::createSkillsTable(); sh_LifProbeEnd();
//-----------------------------------------------------------------------------
function sh_LifProbeBegin()
{
   deleteVariables("$SH_LifNativeLevel*");
   deleteVariables("$SH_LifProbe*");
   %before = "";
   for (%i = 0; %i < GuiSkillPanel.getCount(); %i++)
      %before = %before SPC GuiSkillPanel.getObject(%i).getId();
   $SH_LifProbeBefore = %before @ " ";
   $SH_LifProbeItemN = 0;
   $SH_LifProbeLvlN = 0;
   $SH_LifProbeIds = "";
   $SH_LifProbing = true;
}

function sh_LifProbeEnd()
{
   $SH_LifProbing = false;
   %ids = $SH_LifProbeIds;
   for (%i = 1; %i <= $SH_LifProbeLvlN; %i++)
   {
      %txt = $SH_LifProbeLvl[%i].findObjectByInternalName("SkillValTxt", true);
      %val = isObject(%txt) ? trim(%txt.getText()) : "";
      %id = getWord(%ids, %i - 1);
      if (%id !$= "" && %val !$= "")
         $SH_LifNativeLevel[%id] = %val;
   }
   if (!$SH_LifNativeLogged)
   {
      $SH_LifNativeLogged = true;
      echo("[SH_Lif] engine table: " @ $SH_LifProbeItemN @ " nodes, " @ $SH_LifProbeLvlN @ " level labels, skill ids '" @ %ids @ "'");
      for (%i = 1; %i <= $SH_LifProbeLvlN && %i <= 6; %i++)
      {
         %txt = $SH_LifProbeLvl[%i].findObjectByInternalName("SkillValTxt", true);
         echo("[SH_Lif]   label " @ %i @ ": skill " @ getWord(%ids, %i - 1) @ " text '" @ (isObject(%txt) ? %txt.getText() : "(none)") @ "'");
      }
   }

   // remove what the engine added (our own controls and the window's named
   // decoration stay; they are re-created / re-hidden right after)
   for (%i = GuiSkillPanel.getCount() - 1; %i >= 0; %i--)
   {
      %o = GuiSkillPanel.getObject(%i);
      if (strpos($SH_LifProbeBefore, " " @ %o.getId() @ " ") >= 0)
         continue;
      if (strpos(%o.getName(), "SH_") == 0 || %o.internalName !$= "")
         continue;
      %o.delete();
   }
   for (%i = 1; %i <= $SH_LifProbeItemN; %i++)
      if (isObject($SH_LifProbeItem[%i]))
         $SH_LifProbeItem[%i].delete();
   for (%i = 1; %i <= $SH_LifProbeLvlN; %i++)
      if (isObject($SH_LifProbeLvl[%i]))
         $SH_LifProbeLvl[%i].delete();
}

//-----------------------------------------------------------------------------
// Skill levels from the server (scripts/server/sh_chatInfo.cs reads the
// player's rows of the skills table): "skillId level skillId level ...",
// possibly in several parts; %last = 1 on the final part.
//-----------------------------------------------------------------------------
function sh_LifLevelsCheck()
{
   if ($SH_LifLevelsAnswered < $SH_LifLevelsAsked)
      warn("[SH_Lif] no skill levels from the server after 5 s - update the server with the Server Mod Pack (scripts/server/sh_chatInfo.cs)");
}

function clientCmdSH_SkillLevels(%pairs, %last)
{
   $SH_LifLevelsAnswered = getSimTime();
   if ($SH_LifLevelsPending)
      deleteVariables("$SH_LifNativeLevel*");
   $SH_LifLevelsPending = false;
   for (%i = 0; %i + 1 < getWordCount(%pairs); %i += 2)
      $SH_LifNativeLevel[getWord(%pairs, %i)] = getWord(%pairs, %i + 1);
   if (!%last)
      return;
   $SH_LifLevelsPending = true;               // the next answer starts a new list
   if (!$SH_LifLevelsLogged)
   {
      $SH_LifLevelsLogged = true;
      echo("[SH_Lif] skill levels from the server, e.g. " @ getWords(%pairs, 0, 9));
   }
   // update the open window: badges, cached levels, the selected skill's header
   deleteVariables("$SH_LifLevel*");
   for (%i = 0; %i < $SH_AllSkillCount; %i++)
   {
      %id = $SH_AllSkillId[%i];
      %b = $SH_LifBadge[%id];
      if (!isObject(%b) || $SH_LifNativeLevel[%id] $= "")
         continue;
      $SH_LifLevel[%id] = mFloor($SH_LifNativeLevel[%id]);
      %b.setText($SH_LifLevel[%id]);
      %b.setVisible(true);
   }
   if ($SH_LifSel !$= "" && isObject(SH_LifValue) && $SH_LifNativeLevel[$SH_LifSel] !$= "")
      SH_LifValue.setText($SH_LifNativeLevel[$SH_LifSel] @ " / 100");
}

//-----------------------------------------------------------------------------
// Override of the (native) createSkillsTable(). Called by the existing,
// unmodified showCraftSkill() / showCombatSkill() / showMinorSkill().
// In a package, so Parent::createSkillsTable() still reaches the engine's own
// version: it is run first (sh_LifProbeBegin / sh_LifProbeEnd) only to read the player's
// level of every skill, which the engine writes into its level labels
// (createSkillLevel() -> SkillValTxt) and does not expose any other way.
//-----------------------------------------------------------------------------
package SH_LifSkillTable
{
function createSkillsTable()
{
   if (!isObject(GuiSkillPanel))
      return;

   sh_BuildSkillNameCache();
   // the player's skill levels come from the server (scripts/server/sh_chatInfo.cs
   // reads the skills table); running the engine's own table to read them crashed
   // the client, so sh_LifProbeBegin / End are no longer used
   if (isObject(ServerConnection))
      commandToServer('SH_SkillLevels');
   // no answer = the server does not have the current scripts/server/sh_chatInfo.cs
   $SH_LifLevelsAsked = getSimTime();
   cancel($SH_LifLevelsWatch);
   $SH_LifLevelsWatch = schedule(5000, 0, sh_LifLevelsCheck);
   sh_EnsureSkillTreeLayout();
   sh_LifHideOldDecor();
   if (isObject(SH_ReqPopup))
      SH_ReqPopup.setVisible(false);

   %group = $pref::Skills::curGroup;
   sh_LifBuildTree(%group);

   // keep the selected skill when it is in this tab, else the first one
   %sel = $SH_LifSel;
   if (%sel $= "" || $SH_LifNodePos[%sel] $= "" || !sh_LifInGroup(%sel, %group))
      %sel = $SH_LifFirst;
   if (%sel !$= "")
      sh_LifSelectSkill(%sel);
}

};
activatePackage(SH_LifSkillTable);

function sh_LifInGroup(%id, %group)
{
   %roots = sh_LifRoots(%group);
   for (%i = 0; %i < getWordCount(%roots); %i++)
   {
      %chain = sh_LifChain(getWord(%roots, %i));
      for (%k = 0; %k < getWordCount(%chain); %k++)
         if (getWord(%chain, %k) $= %id)
            return true;
   }
   return false;
}
