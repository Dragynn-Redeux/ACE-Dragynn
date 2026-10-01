/*
 * Opens every capstone dungeon, and the Olthoi Queen's Lair raid, as a private instance for each fellowship, instead of
 * one of its numbered copies (the capstone_instanced_dungeons server property).
 *
 * The server's default for the property is now all of them, but a server that has run since the
 * property was added has its old default (empty) saved in this table, and a saved value wins over the default.
 * This saves the new one. To go back to the copies for a dungeon, take it out with
 * /modifystring capstone_instanced_dungeons <the list without it>, or empty the list for all of them.
 */

INSERT INTO `config_properties_string` (`key`, `value`, `description`)
VALUES (
    'capstone_instanced_dungeons',
    'Glenden Wood Dungeon,Green Mire Grave,Sand Shallow,Manse of Panderlou,Smugglers Hideaway,Halls of the Helm,Colier Mine,Empyrean Garrison,Grievous Vault,Folthid Cellar,Mines of Despair,Beyond the Mines,Gredaline Consulate,Mage Academy,Lugian Mines,Lugian Mines2,Mountain Fortress,Olthoi Queen''s Lair',
    'Comma separated names of capstone dungeons (as they are in the AssignCapstoneDungeon emote) to open as a private instance of the original landblock for each fellowship, instead of one of its numbered copies. Every capstone dungeon, and the Olthoi Queen''s Lair raid, by default. Empty means every dungeon uses its copies.'
)
ON DUPLICATE KEY UPDATE `value` = VALUES(`value`), `description` = VALUES(`description`);
