/*
 * Phalanx (WCID 1051123) was reworked: while active, full hits received deal 30% less damage
 * (including spells and hazards), block/parry chance is increased by 25-50%
 * based on shield size, and blocks, parries, and shields work from all angles. It still requires
 * an equipped shield or two-handed weapon.
 *
 * Evasive Stance (WCID 1051114) had its flat evade/resist chance lowered from 30% to 25%.
 *
 * world-db replaced both weenies' descriptions. That template change only affects NEWLY
 * created items.
 *
 * This script retroactively updates already-persisted Phalanx and Evasive Stance biotas (player
 * inventories, houses, mail, corpses, etc.) so existing copies match. Without it, existing copies
 * keep the old descriptions.
 *
 * Run the preview SELECT COUNT(*) queries first to see how many rows will be touched.
 * Back up the shard database before applying this.
 */

-- Preview: how many existing Phalanx / Evasive Stance items will be updated
-- SELECT COUNT(*) FROM biota WHERE weenie_Class_Id = 1051123;
-- SELECT COUNT(*) FROM biota WHERE weenie_Class_Id = 1051114;

START TRANSACTION;

/* Phalanx - Use text */
INSERT INTO biota_properties_string (object_Id, `type`, value)
SELECT id, 14, 'Use this item to move into a defensive stance, and use again to lower your guard. While active:\n\n-Full hits you receive deal 30% less damage, including spells and hazards. Glancing blows and partially resisted spells are unaffected.\n-Your shield, blocks, and parries protect you from all angles, and you cannot be sneak attacked.\n\nYour chance to block or parry is increased, based on shield size:\n-Covenant: 50%\n-Tower: 45%\n-Large: 40%\n-Standard: 35%\n-Small/Buckler: 30%\n-Two-Handed Weapon: 25%\n\nCost: Your attacks and spells cost 25% more stamina and mana while Phalanx is active.\n\nRequires an equipped shield or two-handed weapon.\n\nRequired Focus: Warrior'
FROM biota WHERE weenie_Class_Id = 1051123
ON DUPLICATE KEY UPDATE value = 'Use this item to move into a defensive stance, and use again to lower your guard. While active:\n\n-Full hits you receive deal 30% less damage, including spells and hazards. Glancing blows and partially resisted spells are unaffected.\n-Your shield, blocks, and parries protect you from all angles, and you cannot be sneak attacked.\n\nYour chance to block or parry is increased, based on shield size:\n-Covenant: 50%\n-Tower: 45%\n-Large: 40%\n-Standard: 35%\n-Small/Buckler: 30%\n-Two-Handed Weapon: 25%\n\nCost: Your attacks and spells cost 25% more stamina and mana while Phalanx is active.\n\nRequires an equipped shield or two-handed weapon.\n\nRequired Focus: Warrior';

/* Evasive Stance - Use text */
INSERT INTO biota_properties_string (object_Id, `type`, value)
SELECT id, 14, 'Use this item to move into an evasive stance, and use again to drop from the stance. While active, you have a flat 25% chance to fully evade any attack or fully resist any spell, independent of your defense skill.\n\nCooldown: Only triggers if you drop out of Evasive Stance from running out of stamina.\n\nCost: Stamina costs of your attacks are increased, scaling against your Run or Jump skill. If Run or Jump is specialized, the lowest possible stamina penalty is +25%, otherwise it is +50%.\n\nRequired Focus: Archer, Vagabond, or Blademaster.'
FROM biota WHERE weenie_Class_Id = 1051114
ON DUPLICATE KEY UPDATE value = 'Use this item to move into an evasive stance, and use again to drop from the stance. While active, you have a flat 25% chance to fully evade any attack or fully resist any spell, independent of your defense skill.\n\nCooldown: Only triggers if you drop out of Evasive Stance from running out of stamina.\n\nCost: Stamina costs of your attacks are increased, scaling against your Run or Jump skill. If Run or Jump is specialized, the lowest possible stamina penalty is +25%, otherwise it is +50%.\n\nRequired Focus: Archer, Vagabond, or Blademaster.';

COMMIT;
