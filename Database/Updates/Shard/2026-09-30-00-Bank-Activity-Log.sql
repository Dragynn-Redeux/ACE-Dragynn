/*
 * The bank log behind /bank log: each account's recent deposits, withdrawals and salvage combines, by any of its
 * characters, by hand or by /bank command. The server keeps the newest 100 entries per account.
 */

CREATE TABLE IF NOT EXISTS `bank_activity_log` (
  `id` INT UNSIGNED NOT NULL AUTO_INCREMENT,
  `account_id` INT UNSIGNED NOT NULL,
  `character_id` INT UNSIGNED NOT NULL,
  `character_name` VARCHAR(50) NOT NULL,
  `action` VARCHAR(20) NOT NULL,
  `details` VARCHAR(500) NOT NULL,
  `created_at_utc` DATETIME(6) NOT NULL,
  PRIMARY KEY (`id`),
  KEY `idx_bank_activity_log_account_id` (`account_id`, `id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
