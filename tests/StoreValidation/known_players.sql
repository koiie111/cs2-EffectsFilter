CREATE TABLE IF NOT EXISTS `vip_test_known_players` (
  `steam_id64` BIGINT UNSIGNED NOT NULL,
  `first_seen_at` DATETIME NOT NULL,
  `source` ENUM('seed','live') NOT NULL,
  `viptest_hint_shown_at` DATETIME NULL COMMENT 'старому игроку один раз показали подсказку про !viptest',
  PRIMARY KEY (`steam_id64`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
