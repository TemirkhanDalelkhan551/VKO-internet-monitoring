CREATE TABLE IF NOT EXISTS device_inventories (
    device_id uuid PRIMARY KEY REFERENCES devices(id) ON DELETE CASCADE,
    schema_version text NOT NULL,
    fingerprint text NOT NULL,
    snapshot jsonb NOT NULL,
    collected_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL DEFAULT now(),
    changed_at_utc timestamptz
);

CREATE TABLE IF NOT EXISTS device_inventory_changes (
    id bigserial PRIMARY KEY,
    device_id uuid NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
    occurred_at_utc timestamptz NOT NULL,
    fingerprint text NOT NULL,
    summary text NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_device_inventory_changes_device_time
    ON device_inventory_changes(device_id, occurred_at_utc DESC);
