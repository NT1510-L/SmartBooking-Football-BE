CREATE DATABASE IF NOT EXISTS booking_db
    CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
USE booking_db;

CREATE TABLE branches (
    id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(150) NOT NULL,
    address VARCHAR(255) NOT NULL,
    area VARCHAR(120) NOT NULL,
    opening_hour TINYINT UNSIGNED NOT NULL,
    closing_hour TINYINT UNSIGNED NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    CONSTRAINT ck_branch_hours CHECK (opening_hour < closing_hour AND closing_hour <= 24),
    INDEX ix_branches_area_name (area, name)
) ENGINE=InnoDB;

CREATE TABLE users (
    id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    email VARCHAR(254) NOT NULL,
    password_hash VARCHAR(512) NOT NULL,
    full_name VARCHAR(150) NOT NULL,
    role ENUM('User', 'Manager', 'Admin') NOT NULL DEFAULT 'User',
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    CONSTRAINT uq_users_email UNIQUE (email)
) ENGINE=InnoDB;

CREATE TABLE branch_managers (
    manager_id BIGINT NOT NULL PRIMARY KEY,
    branch_id BIGINT NOT NULL,
    CONSTRAINT uq_branch_managers_branch UNIQUE (branch_id),
    CONSTRAINT fk_branch_managers_user FOREIGN KEY (manager_id) REFERENCES users (id),
    CONSTRAINT fk_branch_managers_branch FOREIGN KEY (branch_id) REFERENCES branches (id)
) ENGINE=InnoDB;

CREATE TABLE pitches (
    id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    branch_id BIGINT NOT NULL,
    pitch_number TINYINT UNSIGNED NOT NULL,
    name VARCHAR(120) NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    CONSTRAINT ck_pitch_number CHECK (pitch_number BETWEEN 1 AND 3),
    CONSTRAINT uq_pitch_branch_number UNIQUE (branch_id, pitch_number),
    CONSTRAINT fk_pitch_branch FOREIGN KEY (branch_id) REFERENCES branches (id)
) ENGINE=InnoDB;

CREATE TABLE bookings (
    id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    pitch_id BIGINT NOT NULL,
    user_id BIGINT NOT NULL,
    start_at_utc DATETIME NOT NULL,
    end_at_utc DATETIME GENERATED ALWAYS AS (start_at_utc + INTERVAL 1 HOUR) STORED,
    status ENUM('Confirmed', 'Cancelled', 'Completed') NOT NULL DEFAULT 'Confirmed',
    price_vnd BIGINT NOT NULL,
    pricing_policy_id BIGINT NOT NULL,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    cancelled_at_utc DATETIME(6) NULL,
    cancelled_by_user_id BIGINT NULL,
    occupied_start_at_utc DATETIME GENERATED ALWAYS AS (
        CASE WHEN status IN ('Confirmed', 'Completed') THEN start_at_utc ELSE NULL END
    ) STORED,
    CONSTRAINT ck_booking_slot_hour CHECK (MINUTE(start_at_utc) = 0 AND SECOND(start_at_utc) = 0),
    CONSTRAINT ck_booking_price CHECK (price_vnd >= 0),
    CONSTRAINT ck_booking_cancellation CHECK (
        (status = 'Cancelled' AND cancelled_at_utc IS NOT NULL AND cancelled_by_user_id IS NOT NULL)
        OR (status IN ('Confirmed', 'Completed') AND cancelled_at_utc IS NULL AND cancelled_by_user_id IS NULL)
    ),
    CONSTRAINT uq_booking_occupied_slot UNIQUE (pitch_id, occupied_start_at_utc),
    CONSTRAINT fk_booking_pitch FOREIGN KEY (pitch_id) REFERENCES pitches (id),
    CONSTRAINT fk_booking_user FOREIGN KEY (user_id) REFERENCES users (id),
    CONSTRAINT fk_booking_cancelled_by FOREIGN KEY (cancelled_by_user_id) REFERENCES users (id),
    INDEX ix_bookings_pitch_start (pitch_id, start_at_utc),
    INDEX ix_bookings_user_start (user_id, start_at_utc),
    INDEX ix_bookings_completion (status, end_at_utc)
) ENGINE=InnoDB;

CREATE TABLE outbox_messages (
    event_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    booking_id BIGINT NOT NULL,
    event_type ENUM('BookingConfirmed', 'BookingCancelled') NOT NULL,
    payload JSON NOT NULL,
    occurred_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    published_at_utc DATETIME(6) NULL,
    CONSTRAINT fk_outbox_booking FOREIGN KEY (booking_id) REFERENCES bookings (id),
    INDEX ix_outbox_pending (published_at_utc, occurred_at_utc, event_id)
) ENGINE=InnoDB;

CREATE TABLE notifications (
    id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    user_id BIGINT NOT NULL,
    booking_id BIGINT NOT NULL,
    event_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    message VARCHAR(500) NOT NULL,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    read_at_utc DATETIME(6) NULL,
    CONSTRAINT uq_notification_event_user UNIQUE (event_id, user_id),
    CONSTRAINT fk_notification_user FOREIGN KEY (user_id) REFERENCES users (id),
    CONSTRAINT fk_notification_booking FOREIGN KEY (booking_id) REFERENCES bookings (id),
    INDEX ix_notifications_user_created (user_id, created_at_utc)
) ENGINE=InnoDB;
