-- =========================================
-- 0010.sql - Manual admin statement rows
-- =========================================
-- Manual rows are used for ad-hoc bills/credits/notes such as damage repairs,
-- noise complaints, owner adjustments, and other items that do not come from a PDF.

CREATE TABLE IF NOT EXISTS manual_statement_entry (
    id              BIGSERIAL PRIMARY KEY,
    lease_id        INTEGER NOT NULL REFERENCES lease(id) ON DELETE CASCADE,
    entry_date      DATE NOT NULL,
    entry_type      VARCHAR(50) NOT NULL DEFAULT 'Manual',
    description     VARCHAR(300) NOT NULL,
    amount          NUMERIC(12,2) NOT NULL DEFAULT 0,
    notes           TEXT,
    created_at      TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_manual_statement_entry_lease_date
ON manual_statement_entry(lease_id, entry_date);
