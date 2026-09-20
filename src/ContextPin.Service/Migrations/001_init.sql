-- 001_init.sql
--
-- The four tables the manifest API needs. Applied by MigrationRunner, tracked by
-- filename in schema_migrations (which the runner creates itself, not this file).
--
-- IF NOT EXISTS on every statement is defensive, not load-bearing: the tracking
-- table is what actually prevents this file from running twice. It stays cheap
-- insurance against a manually-touched database rather than the mechanism relied
-- on for correctness.

-- A versioned, content-hashed bundle of rules. Immutable once created: a content
-- change always produces a new row with a new version and hash, never an UPDATE
-- to an existing one, so anything that cites a rule_set_id by id keeps meaning
-- the same thing forever.
CREATE TABLE IF NOT EXISTS rule_sets (
    id           UUID PRIMARY KEY,
    version      TEXT NOT NULL,
    content_hash TEXT NOT NULL,
    status       TEXT NOT NULL,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (version)
);

-- One rule within a rule set. rule_id is the stable, human-meaningful identifier
-- (e.g. "P7"); id is the row's own surrogate key.
CREATE TABLE IF NOT EXISTS rules (
    id          UUID PRIMARY KEY,
    rule_set_id UUID NOT NULL REFERENCES rule_sets(id) ON DELETE CASCADE,
    rule_id     TEXT NOT NULL,
    title       TEXT NOT NULL,
    content     TEXT NOT NULL,
    severity    TEXT NOT NULL DEFAULT 'info',
    sort_order  INT NOT NULL DEFAULT 0,
    UNIQUE (rule_set_id, rule_id)
);

-- Which rule_set a given (owner, repo, channel) resolves to right now. This is a
-- pointer, updated in place via upsert — the rule_set it points at is what's
-- immutable, not this row.
CREATE TABLE IF NOT EXISTS repo_pins (
    id          UUID PRIMARY KEY,
    owner       TEXT NOT NULL,
    repo        TEXT NOT NULL,
    channel     TEXT NOT NULL,
    rule_set_id UUID NOT NULL REFERENCES rule_sets(id),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (owner, repo, channel)
);

-- A reported violation of a rule in a consuming repo at a given commit. Append-only.
CREATE TABLE IF NOT EXISTS findings (
    id               UUID PRIMARY KEY,
    owner            TEXT NOT NULL,
    repo             TEXT NOT NULL,
    commit_sha       TEXT NOT NULL,
    rule_id          TEXT NOT NULL,
    rule_set_version TEXT NOT NULL,
    severity         TEXT NOT NULL,
    message          TEXT NOT NULL,
    reported_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_findings_owner_repo ON findings (owner, repo);
