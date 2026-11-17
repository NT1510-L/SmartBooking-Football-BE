USE booking_db;

-- Keep completed historical matches out of the new check-in/no-show workflow.
UPDATE bookings SET status = 'Completed'
WHERE status = 'Confirmed' AND endTime <= UTC_TIMESTAMP();

ALTER TABLE bookings
    DROP CHECK ck_booking_cancellation,
    DROP CHECK ck_booking_expiry,
    DROP INDEX uq_booking_occupied_slot,
    DROP COLUMN occupiedStartTime,
    MODIFY status ENUM('PendingPayment','Confirmed','CheckedIn','Completed','Cancelled','Expired','NoShow')
        NOT NULL DEFAULT 'PendingPayment',
    ADD COLUMN checkedInAt DATETIME(6) NULL,
    ADD COLUMN checkedInByUserId BIGINT NULL,
    ADD CONSTRAINT fk_booking_checked_in_by FOREIGN KEY (checkedInByUserId) REFERENCES users (id),
    ADD COLUMN occupiedStartTime DATETIME GENERATED ALWAYS AS (
        CASE WHEN status IN ('PendingPayment','Confirmed','CheckedIn','Completed') THEN startTime ELSE NULL END
    ) STORED,
    ADD CONSTRAINT uq_booking_occupied_slot UNIQUE (pitchId, occupiedStartTime),
    ADD CONSTRAINT ck_booking_cancellation CHECK (
        (status = 'Cancelled' AND cancelledAt IS NOT NULL AND cancelledByUserId IS NOT NULL)
        OR (status <> 'Cancelled' AND cancelledAt IS NULL AND cancelledByUserId IS NULL)
    ),
    ADD CONSTRAINT ck_booking_expiry CHECK (status <> 'PendingPayment' OR expiresAt IS NOT NULL),
    ADD CONSTRAINT ck_booking_check_in CHECK (
        (checkedInAt IS NULL AND checkedInByUserId IS NULL AND status <> 'CheckedIn')
        OR (checkedInAt IS NOT NULL AND checkedInByUserId IS NOT NULL AND status IN ('CheckedIn','Completed'))
    );

-- Zero outstanding balance preserves payments that previously collected the full price.
ALTER TABLE payments
    ADD COLUMN balanceAmountVnd BIGINT NOT NULL DEFAULT 0,
    ADD COLUMN balancePaidAt DATETIME(6) NULL,
    ADD COLUMN balanceCollectedByUserId BIGINT NULL,
    ADD CONSTRAINT fk_payment_balance_collector FOREIGN KEY (balanceCollectedByUserId) REFERENCES users (id),
    ADD CONSTRAINT ck_payment_balance_amount CHECK (balanceAmountVnd >= 0),
    ADD CONSTRAINT ck_payment_balance_collection CHECK (
        (balancePaidAt IS NULL AND balanceCollectedByUserId IS NULL)
        OR (balancePaidAt IS NOT NULL AND balanceCollectedByUserId IS NOT NULL
            AND status = 'Paid' AND balanceAmountVnd > 0)
    );

ALTER TABLE outboxmessages MODIFY eventType
    ENUM('BookingConfirmed','BookingCancelled','BookingNoShow') NOT NULL;

-- Preserve old pitch rows and booking history while retiring capacity above three.
UPDATE pitches SET isActive = FALSE WHERE pitchNumber > 3;
ALTER TABLE pitches ADD CONSTRAINT ck_pitch_capacity
    CHECK (pitchNumber BETWEEN 1 AND 3 OR isActive = FALSE);
