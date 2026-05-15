-- ============================
-- 0001.sql - Initial Schema
-- ============================
-- This migration can run against older development databases that already
-- contain some of the base tables but do not have FluentMigrator version rows.
-- Keep the DDL idempotent so the runner can baseline those databases instead
-- of failing with "relation already exists".

-- ============================
-- Property
-- ============================
CREATE TABLE IF NOT EXISTS property (
    id              SERIAL PRIMARY KEY,
    name            VARCHAR(200) NOT NULL,
    address_line1   VARCHAR(300),
    address_line2   VARCHAR(300),
    notes           TEXT,
    is_active       BOOLEAN NOT NULL DEFAULT TRUE,
    created_at      TIMESTAMP NOT NULL DEFAULT NOW()
);

-- ============================
-- Tenant
-- ============================
CREATE TABLE IF NOT EXISTS tenant (
    id          SERIAL PRIMARY KEY,
    full_name   VARCHAR(200) NOT NULL,
    email       VARCHAR(200),
    phone       VARCHAR(50),
    notes       TEXT,
    is_active   BOOLEAN NOT NULL DEFAULT TRUE,
    created_at  TIMESTAMP NOT NULL DEFAULT NOW()
);

-- ============================
-- Lease (Tenant occupancy)
-- ============================
CREATE TABLE IF NOT EXISTS lease (
    id              SERIAL PRIMARY KEY,
    property_id     INTEGER NOT NULL REFERENCES property(id) ON DELETE CASCADE,
    tenant_id       INTEGER NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    start_date      DATE NOT NULL,
    end_date        DATE,
    notes           TEXT,
    created_at      TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_lease_property ON lease(property_id);
CREATE INDEX IF NOT EXISTS ix_lease_tenant ON lease(tenant_id);

-- ============================
-- RentRate (rent changes over time)
-- ============================
CREATE TABLE IF NOT EXISTS rent_rate (
    id              SERIAL PRIMARY KEY,
    lease_id        INTEGER NOT NULL REFERENCES lease(id) ON DELETE CASCADE,
    effective_from  DATE NOT NULL,
    amount          NUMERIC(12,2) NOT NULL,
    notes           TEXT
);

CREATE INDEX IF NOT EXISTS ix_rent_rate_lease ON rent_rate(lease_id);

-- ============================
-- ServiceType (Lookup)
-- ============================
CREATE TABLE IF NOT EXISTS service_type (
    id          SERIAL PRIMARY KEY,
    name        VARCHAR(100) NOT NULL,
    is_utility  BOOLEAN NOT NULL DEFAULT TRUE
);

-- Seed service types without duplicating them if this script is baselining an
-- existing database that was created before migrations were version-tracked.
INSERT INTO service_type (name, is_utility)
SELECT seed.name, seed.is_utility
FROM (
    VALUES
        ('Water', TRUE),
        ('Electricity', TRUE),
        ('Sanitation', TRUE),
        ('Refuse', TRUE),
        ('Body Corporate', FALSE),
        ('Other', FALSE)
) AS seed(name, is_utility)
WHERE NOT EXISTS (
    SELECT 1
    FROM service_type st
    WHERE LOWER(st.name) = LOWER(seed.name)
);

-- ============================
-- SourceFile (Uploaded documents)
-- ============================
CREATE TABLE IF NOT EXISTS source_file (
    id              BIGSERIAL PRIMARY KEY,
    file_name       VARCHAR(300) NOT NULL,
    content_type    VARCHAR(100),
    storage_path    VARCHAR(500),
    file_hash       VARCHAR(100),
    file_kind       VARCHAR(50),
    parse_status    VARCHAR(50) DEFAULT 'Pending',
    parsed_text     TEXT,
    parse_error     TEXT,
    uploaded_on     TIMESTAMP NOT NULL DEFAULT NOW()
);

-- ============================
-- ServiceCharge
-- ============================
CREATE TABLE IF NOT EXISTS service_charge (
    id              BIGSERIAL PRIMARY KEY,
    lease_id        INTEGER NOT NULL REFERENCES lease(id) ON DELETE CASCADE,
    service_type_id INTEGER NOT NULL REFERENCES service_type(id),
    billing_period  DATE NOT NULL, -- first day of month
    amount          NUMERIC(12,2) NOT NULL,
    source_file_id  BIGINT REFERENCES source_file(id),
    notes           TEXT
);

CREATE INDEX IF NOT EXISTS ix_service_charge_lease ON service_charge(lease_id);
CREATE INDEX IF NOT EXISTS ix_service_charge_period ON service_charge(billing_period);

-- ============================
-- Payment
-- ============================
CREATE TABLE IF NOT EXISTS payment (
    id                  BIGSERIAL PRIMARY KEY,
    lease_id            INTEGER NOT NULL REFERENCES lease(id) ON DELETE CASCADE,
    paid_on             DATE NOT NULL,
    amount              NUMERIC(12,2) NOT NULL,
    payer_name          VARCHAR(200),
    reference           VARCHAR(200),
    matched_to_period   DATE,
    source_file_id      BIGINT REFERENCES source_file(id),
    notes               TEXT,
    created_at          TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_payment_lease ON payment(lease_id);
CREATE INDEX IF NOT EXISTS ix_payment_date ON payment(paid_on);

-- ============================
-- Prevent multiple active leases per property
-- ============================
CREATE UNIQUE INDEX IF NOT EXISTS ux_active_lease_per_property
ON lease(property_id)
WHERE end_date IS NULL;
