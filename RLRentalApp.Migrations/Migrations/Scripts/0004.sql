-- =========================================
-- 0004.sql - Statement ledger table (statement_sdt)
-- =========================================

CREATE TABLE IF NOT EXISTS statement_sdt (
    id              BIGSERIAL PRIMARY KEY,
    property_id     INTEGER NOT NULL REFERENCES property(id) ON DELETE CASCADE,
    tenant_id       INTEGER NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    lease_id        INTEGER NOT NULL REFERENCES lease(id) ON DELETE CASCADE,
    entry_date      DATE NOT NULL,
    entry_type      VARCHAR(50) NOT NULL,
    description     VARCHAR(300) NOT NULL,
    amount          NUMERIC(12,2) NOT NULL,
    source_table    VARCHAR(50) NOT NULL,
    source_id       BIGINT NOT NULL,
    created_at      TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_statement_sdt_source
ON statement_sdt(source_table, source_id);

CREATE INDEX IF NOT EXISTS ix_statement_sdt_lease_date
ON statement_sdt(lease_id, entry_date);

CREATE INDEX IF NOT EXISTS ix_statement_sdt_property_tenant_date
ON statement_sdt(property_id, tenant_id, entry_date);

-- Backfill from existing rows
INSERT INTO statement_sdt (property_id, tenant_id, lease_id, entry_date, entry_type, description, amount, source_table, source_id)
SELECT l.property_id,
       l.tenant_id,
       rr.lease_id,
       rr.effective_from,
       'Rent',
       'Rent for statement month',
       rr.amount,
       'rent_rate',
       rr.id
FROM rent_rate rr
JOIN lease l ON l.id = rr.lease_id
ON CONFLICT (source_table, source_id) DO NOTHING;

INSERT INTO statement_sdt (property_id, tenant_id, lease_id, entry_date, entry_type, description, amount, source_table, source_id)
SELECT l.property_id,
       l.tenant_id,
       sc.lease_id,
       sc.billing_period,
       'Service',
       CONCAT(COALESCE(st.name, 'Service'), ' charge'),
       sc.amount,
       'service_charge',
       sc.id
FROM service_charge sc
JOIN lease l ON l.id = sc.lease_id
LEFT JOIN service_type st ON st.id = sc.service_type_id
ON CONFLICT (source_table, source_id) DO NOTHING;

INSERT INTO statement_sdt (property_id, tenant_id, lease_id, entry_date, entry_type, description, amount, source_table, source_id)
SELECT l.property_id,
       l.tenant_id,
       p.lease_id,
       p.paid_on,
       'Payment',
       COALESCE(NULLIF(p.reference, ''), 'Payment received'),
       -p.amount,
       'payment',
       p.id
FROM payment p
JOIN lease l ON l.id = p.lease_id
ON CONFLICT (source_table, source_id) DO NOTHING;
