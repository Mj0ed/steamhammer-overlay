//-----------------------------------------------------------------------------
// SteamHammer modpack: Skill Tree ("Crafting / Combat / Minor") relayout.
//
// The stock createSkillsTable() is a NATIVE (engine-compiled) function, so it
// cannot be edited directly. TorqueScript function definitions fully shadow
// native console functions of the same name, so redefining createSkillsTable()
// here replaces the native grid layout with our own left-category-list +
// horizontal-chain-rows layout, while everything else (the window, the
// Crafting/Combat/Minor tabs, the skillcap bar) is left completely untouched
// -- those are built by the original, unmodified gui/forms/skillStatWindow.gui
// and gui/scripts/skills.cs.
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

$SH_SkillTree::CatListWidth   = 240;  // left column: unchanged category list
$SH_SkillTree::RecipeColWidth = 695;  // center column: recipes + requirements -- widest
$SH_SkillTree::RowWidth       = 240;  // right column: mastery/ability info lines, same width as the left menu
$SH_SkillTree::NodeSize       = 100;
$SH_SkillTree::ChainMaxWalk   = 40; // safety cap against a malformed/cyclic chain
$SH_SkillTree::LineIconSize   = 72; // icon size for the 1-line-per-skill/action list
$SH_SkillTree::LineHeight     = 110; // row height for that same list
$SH_SkillTree::TopMargin      = 60; // clears the skillcap bar (SkillBtnPnl/SkillcapBorder) at the top of GuiSkillPanel
$SH_SkillTree::ColHeight      = 665; // 705 - (TopMargin - 20), keeps the same bottom margin as before

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
// uses. Rebuilt every time the window is opened, so edits to the XML show up
// on next open without a script reload.
//-----------------------------------------------------------------------------
function sh_BuildSkillNameCache()
{
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
   }

   sh_BuildSkillHierarchy();
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
   if (!isObject(SkillInfoPanel))
      return -1;

   SkillInfoPanel.init(%skillTypeId);

   %valCtrl = SkillInfoPanel.findObjectByInternalName("ValueCtrl", true);
   if (!isObject(%valCtrl))
      return -1;

   return sh_ParseLeadingNumber(%valCtrl.getText());
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

function sh_EnsureSkillTreeLayout()
{
   if (isObject(SkillTreeCatScroll))
      return;

   // disable zoom/pan -- our layout is flat, scrolling handles overflow
   GuiSkillPanel.minScale = 100;
   GuiSkillPanel.maxScale = 100;

   // kill GuiZoomPanel's own outer scrollbars -- our inner scroll ctrls handle it.
   // Setting these fields after the control already exists doesn't always
   // trigger a recompute, so also force a resize pass to make it re-evaluate.
   GuiSkillPanel.hScrollBar = "AlwaysOff";
   GuiSkillPanel.vScrollBar = "AlwaysOff";
   GuiSkillPanel.scrollBarThickness = 0;
   %sp = GuiSkillPanel.position;
   %se = GuiSkillPanel.extent;
   GuiSkillPanel.resize(getWord(%sp, 0), getWord(%sp, 1), getWord(%se, 0), getWord(%se, 1));

   // The base skill-map's own outer scrollbar decoration (frame/track/thumb
   // bitmaps, siblings of GuiSkillPanel rather than children of it) is left
   // over from the native zoom/pan map and isn't tied to our layout at all,
   // so disabling GuiSkillPanel's scrollbars above doesn't hide it. Our own
   // 3 column scrollbars (cat/recipe/info) replace it, so just hide these --
   // left alone (not deleted) in case some other window still needs them.
   sh_HideIfExists(SkillsVScrollFrame);
   sh_HideIfExists(SkillsVScrollTrack);
   sh_HideIfExists(SkillsVerticalSlider);
   sh_HideIfExists(SkillsHScrollFrame);
   sh_HideIfExists(SkillsHScrollTrack);
   sh_HideIfExists(SkillsHorizontalSlider);

   // getSkillsBackground()'s native atlas image is the old vertical skill-web
   // parchment, not this layout -- point at a real, swappable file instead.
   if (isObject(SkillsBackground))
   {
      SkillsBackground.imageIndex = -1;
      SkillsBackground.bitmap = "modpack/gui/images/skillTreeBackground";
   }

   %catScroll = new GuiScrollCtrl(SkillTreeCatScroll)
   {
      position = "20" SPC $SH_SkillTree::TopMargin;
      extent = $SH_SkillTree::CatListWidth SPC $SH_SkillTree::ColHeight;
      vScrollBar = "dynamic";
      hScrollBar = "alwaysOff";
      profile = "GuiCraftScrollProfile";
      constantThumbHeight = true;
      trackOffset = 8;
   };

   %catStack = new GuiStackControl(SkillTreeCatStack)
   {
      position = "4 4";
      extent = ($SH_SkillTree::CatListWidth - 20) SPC "8";
      minExtent = "8 8";
      profile = "GuiDefaultProfile";
      stackingType = "Vertical";
      changeChildSizeToFit = false;
      padding = 12;
   };
   %catScroll.add(%catStack);

   %recipeColX = 20 + $SH_SkillTree::CatListWidth + 20;

   %recipeScroll = new GuiScrollCtrl(SkillTreeRecipeScroll)
   {
      position = %recipeColX SPC $SH_SkillTree::TopMargin;
      extent = $SH_SkillTree::RecipeColWidth SPC $SH_SkillTree::ColHeight;
      vScrollBar = "dynamic";
      hScrollBar = "alwaysOff";
      profile = "GuiCraftScrollProfile";
      constantThumbHeight = true;
      trackOffset = 8;
   };

   %recipeStack = new GuiStackControl(SkillTreeRecipeStack)
   {
      position = "4 4";
      extent = ($SH_SkillTree::RecipeColWidth - 20) SPC "8";
      minExtent = "8 8";
      profile = "GuiDefaultProfile";
      stackingType = "Vertical";
      changeChildSizeToFit = false;
      padding = 14;
   };
   %recipeScroll.add(%recipeStack);

   %infoColX = %recipeColX + $SH_SkillTree::RecipeColWidth + 20;

   %infoScroll = new GuiScrollCtrl(SkillTreeInfoScroll)
   {
      position = %infoColX SPC $SH_SkillTree::TopMargin;
      extent = $SH_SkillTree::RowWidth SPC $SH_SkillTree::ColHeight;
      vScrollBar = "dynamic";
      hScrollBar = "alwaysOff";
      profile = "GuiCraftScrollProfile";
      constantThumbHeight = true;
      trackOffset = 8;
   };

   %infoStack = new GuiStackControl(SkillTreeInfoStack)
   {
      position = "4 4";
      extent = ($SH_SkillTree::RowWidth - 20) SPC "8";
      minExtent = "8 8";
      profile = "GuiDefaultProfile";
      stackingType = "Vertical";
      changeChildSizeToFit = false;
      padding = 14;
   };
   %infoScroll.add(%infoStack);

   GuiSkillPanel.add(SkillTreeCatScroll);
   GuiSkillPanel.add(SkillTreeRecipeScroll);
   GuiSkillPanel.add(SkillTreeInfoScroll);

   sh_EnsureRequirementsPopup();
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
      position = "760 110";
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

         new GuiStackControl(SH_ReqStack)
         {
            position = "4 4";
            extent = "8 8";
            minExtent = "8 8";
            profile = "GuiDefaultProfile";
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
      profile = "GuiDefaultProfile";
   };
   %row.add(%icon);
   %row.add(%label);

   %container.add(%row);
}

//-----------------------------------------------------------------------------
// Left-hand category button: icon (native, unlock-aware) + localized name.
//-----------------------------------------------------------------------------
function sh_CreateCategoryButton(%catListPanel, %skillId, %isFirst)
{
   %row = new GuiBitmapButtonCtrl()
   {
      horizSizing = "width";
      extent = ($SH_SkillTree::CatListWidth - 20) SPC "76";
      position = "0 0";
      profile = "GuiSkillStatBtnSkilsProfile";
      buttonType = "RadioButton";
      groupNum = 500;
      defaultState = %isFirst;
      bitmapMode = "Slice9";
      sliceNine = "10 10 10 10";
      command = "sh_ShowSkillCategory(" @ %skillId @ ");";

      new GuiTextCtrl()
      {
         position = "84 0";
         extent = "130 76";
         vertSizing = "center";
         profile = "GuiItemRecipeTextProfile";
         canHit = false;
         text = sh_GetSkillName(%skillId);
      };
   };

   %icon = createSkillItem("0 0", true);
   %icon.position = "10 10";
   %icon.extent = "56 56";
   %icon.canHit = false;
   %icon.init(%skillId);

   %iconPath = $SH_SkillIcon[%skillId];
   if (%iconPath !$= "")
      %icon.setBitmap(%iconPath);

   %row.add(%icon);

   %catListPanel.add(%row);
   return %row;
}

//-----------------------------------------------------------------------------
// Right-hand row: one vertical list of "icon | name | description" lines per
// category -- the mastery itself, its chained masteries, and every ability,
// each on its own full-width line.
//-----------------------------------------------------------------------------
function sh_CreateSkillChainRow(%rowsStack, %skillId)
{
   %lineStack = new GuiStackControl()
   {
      horizSizing = "width";
      extent = ($SH_SkillTree::RowWidth - 20) SPC "8";
      minExtent = "8 8";
      profile = "GuiDefaultProfile";
      stackingType = "Vertical";
      changeChildSizeToFit = false;
      padding = 10;
   };

   sh_AddSkillChainNodes(%lineStack, %skillId);
   %lineStack.updateStack();

   %rowsStack.add(%lineStack);
   $SH_SkillRow[%skillId] = %lineStack;
   $SH_CategoryId[$SH_CategoryCount] = %skillId;
   $SH_CategoryCount++;
   return %lineStack;
}

function sh_AddSkillChainNodes(%lineStack, %rootId)
{
   sh_CreateSkillLine(%lineStack, %rootId);
   sh_AddAbilityLines(%lineStack, %rootId);

   %prev = %rootId;
   %child = sh_GetNextChainSkill(%rootId);
   %walked = 0;
   while (%child !$= "" && %child != -1 && %child != %prev && %walked < $SH_SkillTree::ChainMaxWalk)
   {
      sh_CreateSkillLine(%lineStack, %child);
      sh_AddAbilityLines(%lineStack, %child);

      %prev = %child;
      %child = sh_GetNextChainSkill(%child);
      %walked++;
   }
}

// One full-width "icon | name / required level / description" line, shared
// by mastery headers and abilities alike, right-justified to match the info
// column's alignment. %iconCtrl must already be a constructed but un-added
// GUI control (native GuiSkillItem for masteries, plain bitmap for
// abilities). %reqLvl is optional -- pass "" to omit the level line.
function sh_CreateActionLine(%iconCtrl, %name, %desc, %command, %reqLvl)
{
   %iconCtrl.position = "8 8";
   %iconCtrl.extent = $SH_SkillTree::LineIconSize SPC $SH_SkillTree::LineIconSize;

   %text = %name;
   if (%reqLvl !$= "" && %reqLvl > 0)
      %text = %text @ "\nRequires Skill Level" SPC %reqLvl;
   if (%desc !$= "")
      %text = %text @ " - " @ %desc;

   %textX = 16 + $SH_SkillTree::LineIconSize;
   %textW = ($SH_SkillTree::RowWidth - 20) - %textX - 10;

   %textLbl = new GuiMLTextCtrl()
   {
      position = %textX SPC "8";
      extent = %textW SPC ($SH_SkillTree::LineHeight - 16);
      horizSizing = "width";
      profile = "GuiItemRecipeTextProfile";
      canHit = false;
      justify = "right";
      text = %text;
   };

   %line = new GuiBitmapButtonCtrl()
   {
      horizSizing = "width";
      extent = ($SH_SkillTree::RowWidth - 20) SPC $SH_SkillTree::LineHeight;
      profile = "GuiSkillItemProfile";
      buttonType = "PushButton";
      bitmapMode = "Slice9";
      sliceNine = "0 0 0 0";
   };
   if (%command !$= "")
      %line.command = %command;

   %line.add(%iconCtrl);
   %line.add(%textLbl);

   return %line;
}

// Mastery/skill header line -- native icon (unlock-aware, same as the stock
// grid used), name, and its level-0 description.
function sh_CreateSkillLine(%lineStack, %skillId)
{
   %icon = createSkillItem("56,45% 47%", true);
   %icon.init(%skillId);
   sh_ApplySkillIcon(%icon, %skillId);

   %line = sh_CreateActionLine(%icon, sh_GetSkillName(%skillId), sh_GetSkillDesc(%skillId), "", "");
   %lineStack.add(%line);
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

// Appends one line per <ability> belonging to %skillId, faded + padlocked if
// the player's current level in %skillId is too low.
function sh_AddAbilityLines(%lineStack, %skillId)
{
   %count = $SH_SkillAbilityCount[%skillId];
   if (%count $= "" || %count <= 0)
      return;

   %curLvl = sh_GetCurrentSkillLevel(%skillId);

   for (%i = 0; %i < %count; %i++)
      sh_CreateAbilityLine(%lineStack, %skillId, %i, %curLvl);
}

function sh_CreateAbilityLine(%lineStack, %skillId, %idx, %curLvl)
{
   %abilityId = $SH_SkillAbilityId[%skillId, %idx];
   %name = $SH_SkillAbilityName[%skillId, %idx];
   if ($SH_AbilityNameOverride[%abilityId] !$= "")
      %name = $SH_AbilityNameOverride[%abilityId];
   %lvl = $SH_SkillAbilityLvl[%skillId, %idx];
   %icon = $SH_SkillAbilityIcon[%skillId, %idx];
   %desc = $SH_SkillAbilityDesc[%skillId, %idx];

   %locked = (%curLvl != -1 && %lvl !$= "" && %lvl > %curLvl);
   %command = "sh_ShowAbilityRequirements(" @ %skillId @ "," @ %abilityId @ ");";

   %iconCtrl = new GuiBitmapCtrl()
   {
      canHit = false;
      profile = "GuiSkillStatImageProfile";
      imageIndex = getSkillItemPnl();
      centered = true;
   };
   if (%icon !$= "")
      %iconCtrl.setBitmap(%icon);

   %line = sh_CreateActionLine(%iconCtrl, %name, %desc, %command, %lvl);

   if (%locked)
   {
      %lock = new GuiBitmapCtrl()
      {
         position = (8 + $SH_SkillTree::LineIconSize - 26) SPC (8 + $SH_SkillTree::LineIconSize - 26);
         extent = "28 28";
         canHit = false;
         profile = "GuiSkillStatImageProfile";
         imageIndex = getSkillBtnStatus();
      };
      %line.add(%lock);
      %line.opacity = 0.4;
   }

   %lineStack.add(%line);
}

// Builds an empty, unattached requirement-list stack for one requirements
// card, inset by %pad on all sides.
function sh_BuildRequirementCardStack(%cardWidth, %pad)
{
   return new GuiStackControl()
   {
      position = %pad SPC %pad;
      extent = (%cardWidth - (%pad * 2)) SPC "8";
      minExtent = "8 8";
      profile = "GuiDefaultProfile";
      stackingType = "Vertical";
      changeChildSizeToFit = false;
      padding = 8;
   };
}

// Adds one icon + text row (material/tool + quantity) to a requirement stack.
function sh_AddRequirementCardRow(%stack, %objId, %qty, %cardWidth, %pad, %rowH, %rowIconSize)
{
   %rname = $SH_ObjName[%objId];
   if (%rname $= "")
      %rname = "Item" SPC %objId;
   %rface = $SH_ObjFace[%objId];
   %entry = ($SH_ObjIsTool[%objId] == 1) ? %rname : (%qty @ "x" SPC %rname);

   %rowIcon = new GuiBitmapCtrl()
   {
      position = "0 4";
      extent = %rowIconSize SPC %rowIconSize;
      canHit = false;
      profile = "GuiSkillStatImageProfile";
      imageIndex = getSkillItemPnl();
      centered = true;
   };
   if (%rface !$= "")
      %rowIcon.setBitmap(%rface);

   %rowLbl = new GuiMLTextCtrl()
   {
      position = (14 + %rowIconSize) SPC "6";
      extent = (%cardWidth - (%pad * 2) - (14 + %rowIconSize)) SPC %rowH;
      horizSizing = "width";
      profile = "GuiItemRecipeTextProfile";
      canHit = false;
      text = %entry;
   };

   %reqRow = new GuiControl()
   {
      horizSizing = "width";
      extent = (%cardWidth - (%pad * 2)) SPC %rowH;
      profile = "GuiDefaultProfile";
   };
   %reqRow.add(%rowIcon);
   %reqRow.add(%rowLbl);
   %stack.add(%reqRow);
}

// Adds the "No materials required." fallback row to a requirement stack.
function sh_AddNoMaterialsRow(%stack, %cardWidth, %pad, %rowH)
{
   %reqRow = new GuiMLTextCtrl()
   {
      horizSizing = "width";
      extent = (%cardWidth - (%pad * 2)) SPC %rowH;
      profile = "GuiItemRecipeTextProfile";
      canHit = false;
      text = "No materials required.";
   };
   %stack.add(%reqRow);
}

// One recipe row, split into side-by-side equal-height cards: left is the
// recipe itself (icon + item name, plus required tool if any), right is a
// bordered card listing up to 5 required materials/tools with its own icon
// per line. If a recipe has MORE than 5 unique materials, a second
// requirements card is added to the right of the first (up to 10 total) --
// both cards stay right-aligned to the row's right edge, and the recipe
// panel narrows/shifts to make room rather than overlapping or scrolling.
// The recipe card is stretched to match the requirements card(s) height.
// Tool requirements (IsTool=1) skip the "Nx" quantity prefix -- for those,
// the number is how many times the tool is used/hit, not how many the
// player must supply. The tool itself is never repeated as a requirement
// row since it's already shown on the recipe side. A thin divider line
// follows each row to visually separate it from the next recipe.
function sh_CreateRecipeLine(%container, %skillId, %idx)
{
   %recipeId = $SH_RecipeId[%skillId, %idx];
   %objId = $SH_RecipeResultObjId[%skillId, %idx];
   %face = $SH_ObjFace[%objId];

   %name = $SH_RecipeNameOverride[%recipeId];
   if (%name $= "")
      %name = $SH_RecipeName[%skillId, %idx];
   if (%name $= "")
      %name = $SH_ObjName[%objId];
   if (%name $= "")
      %name = "Recipe" SPC %recipeId;

   %toolId = $SH_RecipeToolId[%skillId, %idx];
   %toolName = (%toolId !$= "" && %toolId != 0) ? $SH_ObjName[%toolId] : "";
   %headerText = (%toolName !$= "") ? (%name @ " (Tool: " @ %toolName @ ")") : %name;

   %cardW = $SH_SkillTree::RecipeColWidth - 20;
   %iconSize = 80;
   %rowH = 36;
   %rowIconSize = 28;
   %maxReqRows = 5;
   %gap = 24;
   %pad = 16;
   %leftWDefault = mFloor(%cardW * 0.4);
   %leftWMin = %iconSize + (%pad * 2) + 10;

   %reqCount = $SH_ReqCount[%recipeId];
   if (%reqCount $= "")
      %reqCount = 0;

   if (%toolId !$= "" && %toolId != 0)
      %seenObjId[%toolId] = true; // already shown on the recipe side -- don't repeat it as a requirement row

   // Collect unique requirement entries (up to 2 cards' worth) before laying
   // out any controls, since the layout depends on how many are found.
   %uniqueCount = 0;
   for (%i = 0; %i < %reqCount && %uniqueCount < (%maxReqRows * 2); %i++)
   {
      %reqObjId = $SH_ReqObjId[%recipeId, %i];
      if (%seenObjId[%reqObjId] == true)
         continue; // duplicate material row, or the tool -- only show it once (or not at all)
      %seenObjId[%reqObjId] = true;

      %uObjId[%uniqueCount] = %reqObjId;
      %uQty[%uniqueCount] = $SH_ReqQty[%recipeId, %i];
      %uniqueCount++;
   }

   // Most recipes fit in one requirements card; if more than 5 unique
   // materials exist, a second card is added to the right of the first and
   // the recipe panel narrows to make room -- both cards stay right-aligned
   // to the row's right edge.
   %numReqCards = (%uniqueCount > %maxReqRows) ? 2 : 1;

   if (%numReqCards == 1)
   {
      %leftW = %leftWDefault;
      %reqCardW = %cardW - %leftW - %gap;
      %card1X = %leftW + %gap;
      %card2X = -1;
   }
   else
   {
      %reqAreaW = %cardW - %leftWMin - (%gap * 2);
      %reqCardW = mFloor(%reqAreaW / 2);
      %card2X = %cardW - %reqCardW;
      %card1X = %card2X - %gap - %reqCardW;
      %leftW = %card1X - %gap;
   }

   %card1Count = (%uniqueCount < %maxReqRows) ? %uniqueCount : %maxReqRows;
   %card2Count = (%numReqCards == 2) ? (%uniqueCount - %maxReqRows) : 0;

   %stack1 = sh_BuildRequirementCardStack(%reqCardW, %pad);
   for (%i = 0; %i < %card1Count; %i++)
      sh_AddRequirementCardRow(%stack1, %uObjId[%i], %uQty[%i], %reqCardW, %pad, %rowH, %rowIconSize);
   if (%card1Count <= 0)
      sh_AddNoMaterialsRow(%stack1, %reqCardW, %pad, %rowH);
   %stack1.updateStack();
   %h1 = getWord(%stack1.extent, 1);

   %h2 = 0;
   if (%numReqCards == 2)
   {
      %stack2 = sh_BuildRequirementCardStack(%reqCardW, %pad);
      for (%i = 0; %i < %card2Count; %i++)
         sh_AddRequirementCardRow(%stack2, %uObjId[%maxReqRows + %i], %uQty[%maxReqRows + %i], %reqCardW, %pad, %rowH, %rowIconSize);
      %stack2.updateStack();
      %h2 = getWord(%stack2.extent, 1);
   }

   %minLeftH = %iconSize + 60;
   %boxH = %minLeftH;
   if (%h1 + (%pad * 2) > %boxH)
      %boxH = %h1 + (%pad * 2);
   if (%numReqCards == 2 && (%h2 + (%pad * 2) > %boxH))
      %boxH = %h2 + (%pad * 2);

   // Left panel: the recipe itself (icon + name/tool), stretched to match
   // the requirements card's height so both sides line up evenly.
   %recipePanel = new GuiControl()
   {
      position = "0 0";
      extent = %leftW SPC %boxH;
      profile = "GuiBorderGrayTextureProfile";
   };

   %icon = new GuiBitmapCtrl()
   {
      position = ((%leftW - %iconSize) / 2) SPC "10";
      extent = %iconSize SPC %iconSize;
      canHit = false;
      profile = "GuiSkillStatImageProfile";
      imageIndex = getSkillItemPnl();
      centered = true;
   };
   if (%face !$= "")
      %icon.setBitmap(%face);

   %headerLbl = new GuiMLTextCtrl()
   {
      position = %pad SPC (%iconSize + 18);
      extent = (%leftW - (%pad * 2)) SPC (%boxH - %iconSize - 26);
      horizSizing = "width";
      profile = "GuiItemRecipeTextProfile";
      canHit = false;
      justify = "center";
      text = %headerText;
   };

   %recipePanel.add(%icon);
   %recipePanel.add(%headerLbl);

   // Right panel(s): the requirements card(s), same height as the recipe
   // side, right-aligned to the row's right edge.
   %reqCard1 = new GuiControl()
   {
      position = %card1X SPC "0";
      extent = %reqCardW SPC %boxH;
      profile = "GuiBorderGrayTextureProfile";
   };
   %reqCard1.add(%stack1);

   %row = new GuiControl()
   {
      horizSizing = "width";
      extent = %cardW SPC %boxH;
      profile = "GuiDefaultProfile";
   };
   %row.add(%recipePanel);
   %row.add(%reqCard1);

   if (%numReqCards == 2)
   {
      %reqCard2 = new GuiControl()
      {
         position = %card2X SPC "0";
         extent = %reqCardW SPC %boxH;
         profile = "GuiBorderGrayTextureProfile";
      };
      %reqCard2.add(%stack2);
      %row.add(%reqCard2);
   }

   %container.add(%row);

   // Thin divider line so each recipe row is visually separated from the next.
   %divider = new GuiControl()
   {
      horizSizing = "width";
      extent = %cardW SPC "2";
      profile = "GuiBorderGrayTextureProfile";
   };
   %container.add(%divider);
}

// Adds one card per recipe tagged with %skillId's own SkillTypeID -- purely
// data-driven off $SH_RecipeCount, so a new sh_recipe.xml row is picked up
// automatically next time the window opens, no code changes required.
function sh_AddRecipeLinesForSkill(%container, %skillId)
{
   %count = $SH_RecipeCount[%skillId];
   if (%count $= "" || %count <= 0)
      return;

   for (%i = 0; %i < %count; %i++)
      sh_CreateRecipeLine(%container, %skillId, %i);
}

// Center column, one per category: walks the SAME parent/child skill chain
// the info column uses (sh_GetNextChainSkill), so a recipe whose SkillTypeID
// matches ANY skill in that chain -- not just the top-level category id --
// automatically surfaces here too, mirroring how new chained/modded skills
// already get picked up without hardcoding.
function sh_CreateRecipeColumn(%recipeStack, %rootId)
{
   %wrap = new GuiStackControl()
   {
      horizSizing = "width";
      extent = ($SH_SkillTree::RecipeColWidth - 20) SPC "8";
      minExtent = "8 8";
      profile = "GuiDefaultProfile";
      stackingType = "Vertical";
      changeChildSizeToFit = false;
      padding = 10;
   };

   sh_AddRecipeLinesForSkill(%wrap, %rootId);
   %visited[%rootId] = true;

   %prev = %rootId;
   %child = sh_GetNextChainSkill(%rootId);
   %walked = 0;
   while (%child !$= "" && %child != -1 && %child != %prev && %walked < $SH_SkillTree::ChainMaxWalk)
   {
      if (%visited[%child] == true)
         break; // cycled back to an already-added skill -- stop, don't re-add its recipes

      sh_AddRecipeLinesForSkill(%wrap, %child);
      %visited[%child] = true;
      %prev = %child;
      %child = sh_GetNextChainSkill(%child);
      %walked++;
   }

   %wrap.updateStack();
   %recipeStack.add(%wrap);
   $SH_SkillRecipeCol[%rootId] = %wrap;
   return %wrap;
}

// Hides every OTHER category's chain row so only the clicked one shows --
// clicking the left list now filters instead of just scrolling a long list.
function sh_ShowSkillCategory(%skillId)
{
   for (%i = 0; %i < $SH_CategoryCount; %i++)
   {
      %id = $SH_CategoryId[%i];
      if (isObject($SH_SkillRow[%id]))
         $SH_SkillRow[%id].setVisible(%id == %skillId);
      if (isObject($SH_SkillRecipeCol[%id]))
         $SH_SkillRecipeCol[%id].setVisible(%id == %skillId);
   }

   if (isObject(SkillTreeInfoStack))
      SkillTreeInfoStack.updateStack();
   if (isObject(SkillTreeRecipeStack))
      SkillTreeRecipeStack.updateStack();

   if (isObject(SkillTreeInfoScroll) && isObject($SH_SkillRow[%skillId]))
      SkillTreeInfoScroll.scrollToObject($SH_SkillRow[%skillId]);
   if (isObject(SkillTreeRecipeScroll) && isObject($SH_SkillRecipeCol[%skillId]))
      SkillTreeRecipeScroll.scrollToObject($SH_SkillRecipeCol[%skillId]);
}

//-----------------------------------------------------------------------------
// Override of the (native) createSkillsTable(). Called by the existing,
// unmodified showCraftSkill() / showCombatSkill() / showMinorSkill().
//-----------------------------------------------------------------------------
function createSkillsTable()
{
   if (!isObject(GuiSkillPanel))
      return;

   sh_BuildSkillNameCache();
   sh_EnsureSkillTreeLayout();

   SkillTreeCatStack.clear();
   SkillTreeRecipeStack.clear();
   SkillTreeInfoStack.clear();
   $SH_SkillRow = ""; // clear stale row references from a previous tab
   $SH_SkillRecipeCol = "";
   $SH_SeenSkill = "";
   $SH_CategoryId = "";
   $SH_CategoryCount = 0;

   if (isObject(SH_ReqPopup))
      SH_ReqPopup.setVisible(false);

   %group = $pref::Skills::curGroup;
   %isFirst = true;
   %firstCategoryId = "";

   for (%base = getFirstBaseSkill(%group); %base !$= "" && %base != -1; %base = getNextBaseSkill(%group))
   {
      sh_CreateCategoryButton(SkillTreeCatStack, %base, %isFirst);
      sh_CreateSkillChainRow(SkillTreeInfoStack, %base);
      sh_CreateRecipeColumn(SkillTreeRecipeStack, %base);
      $SH_SeenSkill[%base] = true;
      if (%isFirst)
         %firstCategoryId = %base;
      %isFirst = false;
   }

   for (%sec = getFirstSecondSkill(%group); %sec !$= "" && %sec != -1; %sec = getNextSecondSkill(%group))
   {
      sh_CreateCategoryButton(SkillTreeCatStack, %sec, %isFirst);
      sh_CreateSkillChainRow(SkillTreeInfoStack, %sec);
      sh_CreateRecipeColumn(SkillTreeRecipeStack, %sec);
      $SH_SeenSkill[%sec] = true;
      if (%isFirst)
         %firstCategoryId = %sec;
      %isFirst = false;
   }

   // supplement with any top-level (Parent==0) MODDED (sh_skill_types.xml)
   // skills the native enumeration above doesn't know about. Deliberately
   // excludes skill_types.xml (base game) rows the native code didn't
   // return -- those are dead/legacy entries (e.g. ID 8 "Old skill"), not
   // missing content, and shouldn't be resurrected here.
   for (%i = 0; %i < $SH_AllSkillCount; %i++)
   {
      %id = $SH_AllSkillId[%i];
      %parent = $SH_SkillParent[%id];
      %skillGroup = $SH_SkillGroup[%id];

      if (!$SH_SkillIsModded[%id])
         continue;
      if ($SH_SeenSkill[%id])
         continue;
      if (%parent !$= "" && %parent != 0)
         continue; // not top-level -- it'll show up as a chain child instead
      if (%skillGroup !$= "" && %skillGroup !$= %group)
         continue;

      sh_CreateCategoryButton(SkillTreeCatStack, %id, %isFirst);
      sh_CreateSkillChainRow(SkillTreeInfoStack, %id);
      sh_CreateRecipeColumn(SkillTreeRecipeStack, %id);
      $SH_SeenSkill[%id] = true;
      if (%isFirst)
         %firstCategoryId = %id;
      %isFirst = false;
   }

   SkillTreeCatStack.updateStack();
   SkillTreeRecipeStack.updateStack();
   SkillTreeInfoStack.updateStack();

   if (%firstCategoryId !$= "")
      sh_ShowSkillCategory(%firstCategoryId);
}
