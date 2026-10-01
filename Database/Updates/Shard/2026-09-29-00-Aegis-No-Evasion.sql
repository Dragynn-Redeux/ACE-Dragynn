/*
 * Aegis (WCID 1051128) now prevents evasion: while active, weapon attacks can't be evaded or land as
 * glancing blows, and every hit (not just full hits) takes 50% less damage and restores stamina and
 * mana equal to 10% of the damage prevented.
 *
 * world-db replaced the Aegis weenie's description. That template change only affects NEWLY
 * created items.
 *
 * This script retroactively updates already-persisted Aegis biotas (player inventories, houses,
 * mail, corpses, etc.) so existing copies match. Without it, existing copies keep the old
 * description. It supersedes the description set by 2026-09-27-00-Aegis-Rework.sql, so it is
 * safe to run whether or not that script has already been applied.
 *
 * Run the preview SELECT COUNT(*) query first to see how many rows will be touched.
 * Back up the shard database before applying this.
 */

-- Preview: how many existing Aegis items will be updated
-- SELECT COUNT(*) FROM biota WHERE weenie_Class_Id = 1051128;

START TRANSACTION;

/* Use text */
INSERT INTO biota_properties_string (object_Id, `type`, value)
SELECT id, 14, 'Use to reduce the damage you take from weapon attacks by 50%, for 10 seconds. While active, you cannot evade weapon attacks and they never land as glancing blows. Each hit you take restores stamina and mana equal to 10% of the damage prevented.\n\nCost: Mana equal to your character level.\n\nRequired Focus: Spellsword, Warrior, or Sorcerer.'
FROM biota WHERE weenie_Class_Id = 1051128
ON DUPLICATE KEY UPDATE value = 'Use to reduce the damage you take from weapon attacks by 50%, for 10 seconds. While active, you cannot evade weapon attacks and they never land as glancing blows. Each hit you take restores stamina and mana equal to 10% of the damage prevented.\n\nCost: Mana equal to your character level.\n\nRequired Focus: Spellsword, Warrior, or Sorcerer.';

COMMIT;
