-- Run in vip_global only if C6 has not already created these tables.
CREATE TABLE IF NOT EXISTS `player_fx_prefs` (
  `steam_id64` BIGINT UNSIGNED NOT NULL,
  `hide_trails` TINYINT(1) NOT NULL DEFAULT 0,
  `hide_auras` TINYINT(1) NOT NULL DEFAULT 0,
  `hide_c4` TINYINT(1) NOT NULL DEFAULT 0,
  `hide_kill` TINYINT(1) NOT NULL DEFAULT 0,
  `hint_shown_at` DATETIME NULL COMMENT 'когда показали подсказку о !fx (один раз на игрока)',
  `created_at` TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`steam_id64`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- !fx: события для метрики «уникальные игроки/день, открывшие !fx, и что скрыли».
CREATE TABLE IF NOT EXISTS `player_fx_events` (
  `id` BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `steam_id64` BIGINT UNSIGNED NOT NULL,
  `server_id` INT NOT NULL,
  `event` ENUM('menu_open','toggle','hide_all','show_all','first_hint') NOT NULL,
  `category` ENUM('trails','auras','c4','kill','all') NULL,
  `hidden` TINYINT(1) NULL COMMENT 'состояние после переключения',
  `created_at` DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (`id`),
  KEY `idx_player_fx_events_time` (`created_at`, `event`),
  KEY `idx_player_fx_events_player` (`steam_id64`, `created_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
