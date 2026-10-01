/*
 * Opens the Shattered Source (Xarabydun Researcher Halls, the residence 22FE and the boss room 21FE) as a private instance
 * for each fellowship, like the other capstone dungeons (the capstone_instanced_dungeons server property).
 *
 * A server that has a list of names saved for the property (2026-09-27-00-Capstone-Instanced-Dungeons.sql) gets the dungeon
 * added to it. A saved * already has every dungeon, and an empty value is every dungeon using its copies, so neither is changed;
 * nor is a server with nothing saved, whose default is *. To take it out again, /modifystring capstone_instanced_dungeons
 * <the list without it>.
 */

UPDATE `config_properties_string`
SET `value` = CONCAT(`value`, ',Xarabydun Researcher Halls')
WHERE `key` = 'capstone_instanced_dungeons'
  AND TRIM(`value`) <> ''
  AND `value` NOT LIKE '%*%'
  AND `value` NOT LIKE '%Xarabydun Researcher Halls%';
