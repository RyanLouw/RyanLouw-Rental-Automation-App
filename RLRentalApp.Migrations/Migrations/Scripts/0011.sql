-- =========================================
-- 0011.sql - Property tax ledger entries
-- =========================================
-- Tax ledger rows track property-level income and expenses that should be used
-- for owner/tax reporting without appearing on tenant statements.

CREATE TABLE IF NOT EXISTS property_tax_entry (
    id              BIGSERIAL PRIMARY KEY,
    property_id     INTEGER NOT NULL REFERENCES property(id) ON DELETE CASCADE,
    entry_date      DATE NOT NULL,
    description     VARCHAR(300) NOT NULL,
    entry_type      VARCHAR(20) NOT NULL,
    amount          NUMERIC(12,2) NOT NULL,
    source_file_id  BIGINT REFERENCES source_file(id) ON DELETE SET NULL,
    created_at      TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMP NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_property_tax_entry_type CHECK (entry_type IN ('Income', 'Expense')),
    CONSTRAINT ck_property_tax_entry_amount_sign CHECK (
        (entry_type = 'Income' AND amount >= 0)
        OR (entry_type = 'Expense' AND amount <= 0)
    )
);

CREATE INDEX IF NOT EXISTS ix_property_tax_entry_property_date
ON property_tax_entry(property_id, entry_date);

CREATE INDEX IF NOT EXISTS ix_property_tax_entry_source_file
ON property_tax_entry(source_file_id);
