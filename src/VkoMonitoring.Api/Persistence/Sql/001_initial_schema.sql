CREATE TABLE IF NOT EXISTS schools (
    id uuid PRIMARY KEY,
    name text NOT NULL,
    district_city text,
    address text,
    responsible_name text,
    responsible_position text,
    responsible_phone text,
    responsible_email text,
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    updated_at_utc timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS internet_lines (
    id uuid PRIMARY KEY,
    school_id uuid NOT NULL REFERENCES schools(id),
    name text NOT NULL,
    provider_name text,
    connection_type text,
    contracted_download_mbps numeric(12, 3),
    contracted_upload_mbps numeric(12, 3),
    contract_number text,
    contract_date date,
    status text NOT NULL DEFAULT 'Primary',
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    updated_at_utc timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_internet_lines_status CHECK (status IN ('Primary', 'Backup', 'Disabled'))
);

CREATE TABLE IF NOT EXISTS devices (
    id uuid PRIMARY KEY,
    school_id uuid NOT NULL REFERENCES schools(id),
    line_id uuid NOT NULL REFERENCES internet_lines(id),
    device_identifier text NOT NULL UNIQUE,
    name text NOT NULL,
    room text,
    connection_type text,
    registered_at_utc timestamptz NOT NULL DEFAULT now(),
    last_seen_at_utc timestamptz,
    agent_version text,
    token_hash bytea,
    is_blocked boolean NOT NULL DEFAULT false
);

ALTER TABLE devices ADD COLUMN IF NOT EXISTS token_hash bytea;

CREATE TABLE IF NOT EXISTS device_activation_codes (
    id uuid PRIMARY KEY,
    code_hash bytea NOT NULL UNIQUE,
    school_id uuid NOT NULL REFERENCES schools(id),
    line_id uuid NOT NULL REFERENCES internet_lines(id),
    expires_at_utc timestamptz NOT NULL,
    used_at_utc timestamptz,
    used_by_device_id uuid REFERENCES devices(id),
    created_at_utc timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_device_activation_codes_expires_at
    ON device_activation_codes(expires_at_utc);

CREATE TABLE IF NOT EXISTS measurements (
    event_id uuid PRIMARY KEY,
    school_id uuid NOT NULL REFERENCES schools(id),
    device_id uuid NOT NULL REFERENCES devices(id),
    line_id uuid NOT NULL REFERENCES internet_lines(id),
    measured_at_utc timestamptz NOT NULL,
    download_mbps numeric(12, 3),
    upload_mbps numeric(12, 3),
    ping_milliseconds numeric(12, 3),
    jitter_milliseconds numeric(12, 3),
    packet_loss_percent numeric(6, 3),
    connection_status text NOT NULL,
    failure_kind text NOT NULL,
    failure_reason text,
    agent_version text NOT NULL,
    duration_milliseconds bigint NOT NULL DEFAULT 0,
    external_ip_address text,
    network_connection_type text NOT NULL DEFAULT 'Unknown',
    measurement_server text,
    threshold_download_mbps numeric(12, 3) NOT NULL,
    threshold_upload_mbps numeric(12, 3) NOT NULL,
    threshold_ping_milliseconds numeric(12, 3) NOT NULL,
    threshold_jitter_milliseconds numeric(12, 3) NOT NULL,
    threshold_packet_loss_percent numeric(6, 3) NOT NULL,
    received_at_utc timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_measurements_status CHECK (connection_status IN ('Online', 'Degraded', 'Offline'))
);

ALTER TABLE measurements ADD COLUMN IF NOT EXISTS duration_milliseconds bigint NOT NULL DEFAULT 0;
ALTER TABLE measurements ADD COLUMN IF NOT EXISTS external_ip_address text;
ALTER TABLE measurements ADD COLUMN IF NOT EXISTS network_connection_type text NOT NULL DEFAULT 'Unknown';
ALTER TABLE measurements ADD COLUMN IF NOT EXISTS measurement_server text;

CREATE INDEX IF NOT EXISTS ix_measurements_device_measured_at
    ON measurements(device_id, measured_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_measurements_school_measured_at
    ON measurements(school_id, measured_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_measurements_line_measured_at
    ON measurements(line_id, measured_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_devices_school_id ON devices(school_id);
CREATE INDEX IF NOT EXISTS ix_devices_line_id ON devices(line_id);

CREATE SEQUENCE IF NOT EXISTS incident_number_sequence START WITH 1;

CREATE TABLE IF NOT EXISTS incidents (
    id uuid PRIMARY KEY,
    incident_number bigint NOT NULL DEFAULT nextval('incident_number_sequence') UNIQUE,
    school_id uuid NOT NULL REFERENCES schools(id),
    line_id uuid NOT NULL REFERENCES internet_lines(id),
    source text NOT NULL,
    problem_type text NOT NULL,
    status text NOT NULL,
    title text NOT NULL,
    description text NOT NULL,
    started_at_utc timestamptz NOT NULL,
    detected_at_utc timestamptz NOT NULL,
    sent_to_provider_at_utc timestamptz,
    recovered_at_utc timestamptz,
    closed_at_utc timestamptz,
    latest_measurement_event_id uuid REFERENCES measurements(event_id),
    assigned_to text,
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    updated_at_utc timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_incidents_source CHECK (source IN ('Automatic', 'Manual')),
    CONSTRAINT ck_incidents_status CHECK (
        status IN ('New', 'SentToProvider', 'InProgress', 'WaitingForInformation', 'Resolved', 'Closed'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_incidents_one_open_per_line
    ON incidents(line_id)
    WHERE status NOT IN ('Resolved', 'Closed');

CREATE INDEX IF NOT EXISTS ix_incidents_school_started_at
    ON incidents(school_id, started_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_incidents_status_started_at
    ON incidents(status, started_at_utc DESC);

CREATE TABLE IF NOT EXISTS incident_history (
    id bigserial PRIMARY KEY,
    incident_id uuid NOT NULL REFERENCES incidents(id) ON DELETE CASCADE,
    occurred_at_utc timestamptz NOT NULL,
    action text NOT NULL,
    previous_status text,
    new_status text,
    comment text,
    actor text,
    CONSTRAINT ck_incident_history_previous_status CHECK (
        previous_status IS NULL OR previous_status IN (
            'New', 'SentToProvider', 'InProgress', 'WaitingForInformation', 'Resolved', 'Closed')),
    CONSTRAINT ck_incident_history_new_status CHECK (
        new_status IS NULL OR new_status IN (
            'New', 'SentToProvider', 'InProgress', 'WaitingForInformation', 'Resolved', 'Closed'))
);

CREATE INDEX IF NOT EXISTS ix_incident_history_incident_time
    ON incident_history(incident_id, occurred_at_utc);
