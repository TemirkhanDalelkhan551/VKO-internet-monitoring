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

ALTER TABLE devices ADD COLUMN IF NOT EXISTS lifecycle_status text NOT NULL DEFAULT 'Active';
ALTER TABLE devices ADD COLUMN IF NOT EXISTS replaced_by_device_id uuid NULL;
ALTER TABLE devices ADD COLUMN IF NOT EXISTS retired_at_utc timestamptz NULL;
ALTER TABLE devices ADD COLUMN IF NOT EXISTS retirement_reason text NULL;

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_devices_lifecycle_status' AND conrelid = 'devices'::regclass) THEN
        ALTER TABLE devices ADD CONSTRAINT ck_devices_lifecycle_status
            CHECK (lifecycle_status IN ('Active', 'Replaced', 'Decommissioned'));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_devices_replaced_by' AND conrelid = 'devices'::regclass) THEN
        ALTER TABLE devices ADD CONSTRAINT fk_devices_replaced_by
            FOREIGN KEY (replaced_by_device_id) REFERENCES devices(id);
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS device_lifecycle_history (
    id bigserial PRIMARY KEY,
    device_id uuid NOT NULL REFERENCES devices(id),
    action text NOT NULL CHECK (action IN ('Rebound', 'Replaced', 'Decommissioned')),
    previous_school_id uuid NULL REFERENCES schools(id),
    previous_line_id uuid NULL REFERENCES internet_lines(id),
    current_school_id uuid NULL REFERENCES schools(id),
    current_line_id uuid NULL REFERENCES internet_lines(id),
    replacement_device_id uuid NULL REFERENCES devices(id),
    reason text NOT NULL,
    actor text NOT NULL,
    occurred_at_utc timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_device_lifecycle_history_device_time
    ON device_lifecycle_history (device_id, occurred_at_utc DESC);
