USE booking_db;

ALTER TABLE pitches
    DROP CHECK ck_pitch_number,
    MODIFY pitch_number BIGINT NOT NULL,
    ADD CONSTRAINT ck_pitch_number_positive CHECK (pitch_number > 0);
