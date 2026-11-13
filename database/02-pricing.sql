CREATE DATABASE IF NOT EXISTS pricing_db
    CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
USE pricing_db;

CREATE TABLE pricing_policies (
    id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    branch_id BIGINT NOT NULL,
    effective_from DATE NOT NULL,
    regular_price_vnd BIGINT NOT NULL,
    peak_price_vnd BIGINT NOT NULL,
    peak_start_hour TINYINT UNSIGNED NOT NULL,
    peak_end_hour TINYINT UNSIGNED NOT NULL,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    CONSTRAINT uq_pricing_branch_effective UNIQUE (branch_id, effective_from),
    CONSTRAINT ck_pricing_prices CHECK (regular_price_vnd >= 0 AND peak_price_vnd >= regular_price_vnd),
    CONSTRAINT ck_pricing_peak_hours CHECK (peak_start_hour < peak_end_hour AND peak_end_hour <= 24)
) ENGINE=InnoDB;
