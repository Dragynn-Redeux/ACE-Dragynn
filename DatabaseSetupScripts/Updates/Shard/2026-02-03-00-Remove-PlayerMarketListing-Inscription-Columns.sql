-- Removes no-longer-used market listing fields.
-- These were previously used for (temporary) remaining-time text.
-- Remaining time is now calculated at serialization time.

ALTER TABLE `player_market_listings`
  DROP COLUMN `Inscription`,
  DROP COLUMN `OriginalInscription`;
