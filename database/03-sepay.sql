USE booking_db;

ALTER TABLE bookings
    DROP INDEX uq_booking_occupied_slot,
    DROP COLUMN occupied_start_at_utc,
    MODIFY status ENUM('Confirmed','Cancelled','Completed','PendingPayment','Expired') NOT NULL DEFAULT 'Confirmed',
    ADD COLUMN expires_at_utc DATETIME(6) NULL,
    ADD COLUMN occupied_start_at_utc DATETIME GENERATED ALWAYS AS (
        CASE WHEN status IN ('PendingPayment','Confirmed','Completed') THEN start_at_utc ELSE NULL END
    ) STORED,
    ADD CONSTRAINT uq_booking_occupied_slot UNIQUE (pitch_id, occupied_start_at_utc),
    ADD INDEX ix_bookings_expiry (status, expires_at_utc);

ALTER TABLE bookings
    DROP CHECK ck_booking_cancellation,
    ADD CONSTRAINT ck_booking_cancellation CHECK (
        (status = 'Cancelled' AND cancelled_at_utc IS NOT NULL AND cancelled_by_user_id IS NOT NULL)
        OR (status <> 'Cancelled' AND cancelled_at_utc IS NULL AND cancelled_by_user_id IS NULL)
    ),
    ADD CONSTRAINT ck_booking_expiry CHECK (status <> 'PendingPayment' OR expires_at_utc IS NOT NULL);

CREATE TABLE payments (
    id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    booking_id BIGINT NOT NULL UNIQUE,
    code VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL UNIQUE,
    amount_vnd BIGINT NOT NULL,
    status ENUM('Pending','Paid','Expired','Cancelled') NOT NULL DEFAULT 'Pending',
    paid_at_utc DATETIME(6) NULL,
    CONSTRAINT ck_payment_amount CHECK (amount_vnd > 0),
    CONSTRAINT fk_payment_booking FOREIGN KEY (booking_id) REFERENCES bookings (id)
) ENGINE=InnoDB;

CREATE TABLE payment_receipts (
    sepay_id BIGINT NOT NULL PRIMARY KEY,
    payment_id BIGINT NOT NULL,
    amount_vnd BIGINT NOT NULL,
    outcome ENUM('Accepted','AmountMismatch','Closed') NOT NULL,
    received_at_utc DATETIME(6) NOT NULL,
    CONSTRAINT fk_receipt_payment FOREIGN KEY (payment_id) REFERENCES payments (id)
) ENGINE=InnoDB;
