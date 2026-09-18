CREATE TABLE IF NOT EXISTS monitoring_users (
    id uuid PRIMARY KEY, login text NOT NULL UNIQUE, display_name text NOT NULL,
    password_hash text NOT NULL, role text NOT NULL,
    school_id uuid REFERENCES schools(id), district_city text, provider_name text,
    is_blocked boolean NOT NULL DEFAULT false, security_version integer NOT NULL DEFAULT 1,
    failed_login_count integer NOT NULL DEFAULT 0, locked_until_utc timestamptz,
    created_at_utc timestamptz NOT NULL DEFAULT now(), updated_at_utc timestamptz NOT NULL DEFAULT now(),
    CHECK (role IN ('School','District','Regional','Provider','Administrator')),
    CHECK (role <> 'School' OR school_id IS NOT NULL),
    CHECK (role <> 'District' OR district_city IS NOT NULL),
    CHECK (role <> 'Provider' OR provider_name IS NOT NULL)
);
CREATE TABLE IF NOT EXISTS user_sessions (
    token_hash bytea PRIMARY KEY, user_id uuid NOT NULL REFERENCES monitoring_users(id),
    security_version integer NOT NULL, expires_at_utc timestamptz NOT NULL,
    created_at_utc timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_user_sessions_user ON user_sessions(user_id);
CREATE INDEX IF NOT EXISTS ix_user_sessions_expiry ON user_sessions(expires_at_utc);
CREATE TABLE IF NOT EXISTS audit_events (
    id bigserial PRIMARY KEY, started_at_utc timestamptz NOT NULL DEFAULT now(),
    completed_at_utc timestamptz, user_id uuid REFERENCES monitoring_users(id), actor text NOT NULL,
    action text NOT NULL, path text NOT NULL, status_code integer, client_ip text
);
CREATE INDEX IF NOT EXISTS ix_audit_events_started ON audit_events(started_at_utc DESC);
