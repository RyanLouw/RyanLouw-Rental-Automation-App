-- =========================================
-- 0011.sql - Tax transaction proof rows
-- =========================================
-- Tax rows store owner-facing money in/out records and the Google Drive proof
-- document uploaded for each captured transaction.

CREATE TABLE IF NOT EXISTS tax_transaction (
    id                 BIGSERIAL PRIMARY KEY,
    property_id        INTEGER NOT NULL REFERENCES property(id) ON DELETE CASCADE,
    transaction_date   DATE NOT NULL,
    entry_kind         VARCHAR(10) NOT NULL CHECK (entry_kind IN ('KREDIT', 'DEBIT')),
    amount             NUMERIC(12,2) NOT NULL CHECK (amount >= 0),
    description        VARCHAR(300) NOT NULL,
    proof_file_name    VARCHAR(255) NOT NULL,
    proof_drive_file_id VARCHAR(150) NOT NULL,
    proof_drive_link   TEXT NOT NULL,
    drive_folder_path  TEXT NOT NULL,
    created_at         TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at         TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_tax_transaction_property_date
ON tax_transaction(property_id, transaction_date);

CREATE INDEX IF NOT EXISTS ix_tax_transaction_date
ON tax_transaction(transaction_date);
