/*
 * Bezel Fragments (WCID 1053975) are now used directly on an item to add a socket, replacing the
 * five skill-specific Bezel Tools (WCIDs 1053976-1053980) and their crafting skill requirement.
 *
 * world-db changed the fragment weenie from WeenieType.Stackable (51) to the new
 * WeenieType.BezelFragment (95), made it usable on a target (ItemUseable/TargetType), and updated
 * its description. The tool weenies' Use text now points players to the fragments. Those
 * template changes only affect NEWLY created items.
 *
 * This script retroactively updates already-persisted biotas (anywhere: player inventories,
 * houses, mail, corpses, etc.) so existing fragments and tools match. Without it, existing
 * fragment stacks still load as plain Stackables and cannot be used on a target.
 *
 * Run the preview SELECT COUNT(*) queries first to see how many rows will be touched.
 * Back up the shard database before applying this.
 */

-- Preview: how many existing Bezel Fragment stacks will be migrated
-- SELECT COUNT(*) FROM biota WHERE weenie_Class_Id = 1053975;

-- Preview: how many existing Bezel Tools will have their Use text updated
-- SELECT COUNT(*) FROM biota WHERE weenie_Class_Id BETWEEN 1053976 AND 1053980;

START TRANSACTION;

/* WeenieType: Stackable (51) -> BezelFragment (95) */
UPDATE biota
SET weenie_Type = 95
WHERE weenie_Class_Id = 1053975;

/* ItemUseable: No -> SourceContainedTargetContained */
INSERT INTO biota_properties_int (object_Id, `type`, value)
SELECT id, 16, 524296 FROM biota WHERE weenie_Class_Id = 1053975
ON DUPLICATE KEY UPDATE value = 524296;

/* TargetType: MeleeWeapon, Armor, Clothing, Jewelry, MissileWeapon, Caster */
INSERT INTO biota_properties_int (object_Id, `type`, value)
SELECT id, 94, 33039 FROM biota WHERE weenie_Class_Id = 1053975
ON DUPLICATE KEY UPDATE value = 33039;

/* ShortDesc */
INSERT INTO biota_properties_string (object_Id, `type`, value)
SELECT id, 15, 'A material obtained from salvaging items with sockets. Use on a weapon, shield, caster, piece of armor, or piece of jewelry to add a socket. Requires a stack of fragments equal to the item''s workmanship, squared.'
FROM biota WHERE weenie_Class_Id = 1053975
ON DUPLICATE KEY UPDATE value = 'A material obtained from salvaging items with sockets. Use on a weapon, shield, caster, piece of armor, or piece of jewelry to add a socket. Requires a stack of fragments equal to the item''s workmanship, squared.';

/* Bezel Tools: Use text */
UPDATE biota_properties_string bstr
INNER JOIN biota ON biota.id = bstr.object_Id
SET bstr.value = 'This tool is no longer needed. Use Bezel Fragments directly on an item to add a socket.'
WHERE bstr.`type` = 14 AND biota.weenie_Class_Id BETWEEN 1053976 AND 1053980;

COMMIT;
