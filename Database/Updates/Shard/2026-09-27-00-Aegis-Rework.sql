/*
 * Aegis (WCID 1051128) was reworked into a weapon damage reduction ability: for 10 seconds, damage
 * taken from full hits by weapon attacks is reduced by 50%, and each full hit restores stamina and
 * mana equal to 10% of the damage prevented. Glancing blows and evades are unaffected. It costs
 * mana equal to character level.
 *
 * world-db replaced the Aegis weenie's description. That template change only affects NEWLY
 * created items.
 *
 * This script retroactively updates already-persisted Aegis biotas (player inventories, houses,
 * mail, corpses, etc.) so existing copies match. Without it, existing copies keep the old
 * description.
 *
 * Run the preview SELECT COUNT(*) query first to see how many rows will be touched.
 * Back up the shard database before applying this.
 */

-- Preview: how many existing Aegis items will be updated
-- SELECT COUNT(*) FROM biota WHERE weenie_Class_Id = 1051128;

START TRANSACTION;

/* Use text */
INSERT INTO biota_properties_string (object_Id, `type`, value)
SELECT id, 14, 'Use to reduce the damage of full hits you receive from weapon attacks by 50%, for 10 seconds. Each full hit also restores stamina and mana equal to 10% of the damage prevented. Glancing blows and evaded attacks are unaffected.\n\nCost: Mana equal to your character level.\n\nRequired Focus: Spellsword, Warrior, or Sorcerer.'
FROM biota WHERE weenie_Class_Id = 1051128
ON DUPLICATE KEY UPDATE value = 'Use to reduce the damage of full hits you receive from weapon attacks by 50%, for 10 seconds. Each full hit also restores stamina and mana equal to 10% of the damage prevented. Glancing blows and evaded attacks are unaffected.\n\nCost: Mana equal to your character level.\n\nRequired Focus: Spellsword, Warrior, or Sorcerer.';

COMMIT;
