-- Naming cleanup: lower camel case for all business columns.
-- Date values are stored in UTC by the application, but their names stay simple.
USE booking_db;

ALTER TABLE users
    RENAME COLUMN password_hash TO passwordHash,
    RENAME COLUMN full_name TO fullName,
    RENAME COLUMN is_active TO isActive,
    RENAME COLUMN created_at_utc TO createdAt;

ALTER TABLE branches DROP CHECK ck_branch_hours;
ALTER TABLE branches
    RENAME COLUMN opening_hour TO openingHour,
    RENAME COLUMN closing_hour TO closingHour,
    RENAME COLUMN is_active TO isActive;
ALTER TABLE branches ADD CONSTRAINT ck_branch_hours CHECK (openingHour < closingHour AND closingHour <= 24);

ALTER TABLE pitches DROP CHECK ck_pitch_number_positive;
ALTER TABLE pitches
    RENAME COLUMN branch_id TO branchId,
    RENAME COLUMN pitch_number TO pitchNumber,
    RENAME COLUMN is_active TO isActive;
ALTER TABLE pitches ADD CONSTRAINT ck_pitch_number_positive CHECK (pitchNumber > 0);

ALTER TABLE branch_managers
    RENAME COLUMN manager_id TO managerId,
    RENAME COLUMN branch_id TO branchId;
RENAME TABLE branch_managers TO branchManagers;

ALTER TABLE bookings
    DROP CHECK ck_booking_slot_hour,
    DROP CHECK ck_booking_price,
    DROP CHECK ck_booking_cancellation,
    DROP CHECK ck_booking_expiry,
    DROP INDEX uq_booking_occupied_slot,
    DROP INDEX ix_bookings_completion,
    DROP COLUMN occupied_start_at_utc,
    DROP COLUMN end_at_utc;
ALTER TABLE bookings
    RENAME COLUMN pitch_id TO pitchId,
    RENAME COLUMN user_id TO userId,
    RENAME COLUMN start_at_utc TO startTime,
    RENAME COLUMN price_vnd TO priceVnd,
    RENAME COLUMN pricing_policy_id TO pricingPolicyId,
    RENAME COLUMN created_at_utc TO createdAt,
    RENAME COLUMN cancelled_at_utc TO cancelledAt,
    RENAME COLUMN cancelled_by_user_id TO cancelledByUserId,
    RENAME COLUMN expires_at_utc TO expiresAt;
ALTER TABLE bookings
    ADD COLUMN endTime DATETIME GENERATED ALWAYS AS (startTime + INTERVAL 1 HOUR) STORED,
    ADD COLUMN occupiedStartTime DATETIME GENERATED ALWAYS AS (
        CASE WHEN status IN ('PendingPayment','Confirmed','Completed') THEN startTime ELSE NULL END
    ) STORED,
    ADD CONSTRAINT uq_booking_occupied_slot UNIQUE (pitchId, occupiedStartTime),
    ADD INDEX ix_bookings_completion (status, endTime),
    ADD CONSTRAINT ck_booking_slot_hour CHECK (MINUTE(startTime) = 0 AND SECOND(startTime) = 0),
    ADD CONSTRAINT ck_booking_price CHECK (priceVnd >= 0),
    ADD CONSTRAINT ck_booking_cancellation CHECK (
        (status = 'Cancelled' AND cancelledAt IS NOT NULL AND cancelledByUserId IS NOT NULL)
        OR (status <> 'Cancelled' AND cancelledAt IS NULL AND cancelledByUserId IS NULL)
    ),
    ADD CONSTRAINT ck_booking_expiry CHECK (status <> 'PendingPayment' OR expiresAt IS NOT NULL);

ALTER TABLE payments DROP CHECK ck_payment_amount;
ALTER TABLE payments
    RENAME COLUMN booking_id TO bookingId,
    RENAME COLUMN amount_vnd TO amountVnd,
    RENAME COLUMN paid_at_utc TO paidAt;
ALTER TABLE payments ADD CONSTRAINT ck_payment_amount CHECK (amountVnd > 0);

ALTER TABLE payment_receipts
    RENAME COLUMN sepay_id TO sepayId,
    RENAME COLUMN payment_id TO paymentId,
    RENAME COLUMN amount_vnd TO amountVnd,
    RENAME COLUMN received_at_utc TO receivedAt;
RENAME TABLE payment_receipts TO paymentReceipts;

ALTER TABLE outbox_messages
    RENAME COLUMN event_id TO eventId,
    RENAME COLUMN booking_id TO bookingId,
    RENAME COLUMN event_type TO eventType,
    RENAME COLUMN occurred_at_utc TO occurredAt,
    RENAME COLUMN published_at_utc TO publishedAt;
RENAME TABLE outbox_messages TO outboxMessages;

ALTER TABLE notifications
    RENAME COLUMN user_id TO userId,
    RENAME COLUMN booking_id TO bookingId,
    RENAME COLUMN event_id TO eventId,
    RENAME COLUMN created_at_utc TO createdAt,
    RENAME COLUMN read_at_utc TO readAt;

USE pricing_db;
ALTER TABLE pricing_policies
    DROP CHECK ck_pricing_prices,
    DROP CHECK ck_pricing_peak_hours;
ALTER TABLE pricing_policies
    RENAME COLUMN branch_id TO branchId,
    RENAME COLUMN effective_from TO effectiveFrom,
    RENAME COLUMN regular_price_vnd TO regularPriceVnd,
    RENAME COLUMN peak_price_vnd TO peakPriceVnd,
    RENAME COLUMN peak_start_hour TO peakStartHour,
    RENAME COLUMN peak_end_hour TO peakEndHour,
    RENAME COLUMN created_at_utc TO createdAt;
ALTER TABLE pricing_policies
    ADD CONSTRAINT ck_pricing_prices CHECK (regularPriceVnd >= 0 AND peakPriceVnd >= regularPriceVnd),
    ADD CONSTRAINT ck_pricing_peak_hours CHECK (peakStartHour < peakEndHour AND peakEndHour <= 24);
RENAME TABLE pricing_policies TO pricingPolicies;
