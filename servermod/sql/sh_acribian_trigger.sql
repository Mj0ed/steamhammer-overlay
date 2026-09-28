-- SteamHammer: Acribian character creation (client side: gui/scripts/characterOverride.cs).
--
-- sh_client.exe refuses to send faction 2 (Acribians), so the client sends an
-- Acribian as Fraction 1 + Race 2 - a marker: SH has race selection disabled and
-- never uses race 2. This trigger turns that back into Fraction 2 / Race 1 in the
-- same INSERT, so the row is never stored as a Technocrat and the server's own
-- character list (SELECT ... Fraction FROM `character`) already reads it as Acribian.
-- No engine call can change a character's faction afterwards, so this is the place.
--
-- Run it once in the server's game database (the one sh_server.exe uses, e.g. sh_1):
--     mysql -u <user> -p <database> < sh_acribian_trigger.sql
-- Safe to run again (DROP ... IF EXISTS first).
--
-- MySQL applies the SET assignments left to right on NEW, so the last one sees
-- the new Fraction. Tested on the mod pack's server; not tested on MariaDB.
DROP TRIGGER IF EXISTS `sh_acribian_on_create`;
CREATE TRIGGER `sh_acribian_on_create` BEFORE INSERT ON `character` FOR EACH ROW
  SET NEW.`RaceID`   = IF(NEW.`Fraction` = 1 AND NEW.`Race` = 2, 1, NEW.`RaceID`),
      NEW.`Fraction` = IF(NEW.`Fraction` = 1 AND NEW.`Race` = 2, 2, NEW.`Fraction`),
      NEW.`Race`     = IF(NEW.`Fraction` = 2 AND NEW.`Race` = 2, 1, NEW.`Race`);
