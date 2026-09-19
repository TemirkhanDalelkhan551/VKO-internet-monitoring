ALTER TABLE device_activation_codes
    ADD COLUMN IF NOT EXISTS revoked_at_utc timestamptz NULL;

CREATE INDEX IF NOT EXISTS ix_device_activation_codes_created
    ON device_activation_codes (created_at_utc DESC);

CREATE TABLE IF NOT EXISTS operational_settings (
    singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
    measurement_windows text[] NOT NULL,
    minimum_download_mbps numeric NOT NULL,
    minimum_upload_mbps numeric NOT NULL,
    maximum_ping_milliseconds numeric NOT NULL,
    maximum_jitter_milliseconds numeric NOT NULL,
    maximum_packet_loss_percent numeric NOT NULL,
    minimum_availability_percent numeric NOT NULL,
    updated_at_utc timestamptz NOT NULL DEFAULT now()
);
