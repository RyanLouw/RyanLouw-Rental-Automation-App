-- ============================
-- 0001.sql - Initial Schema
-- ============================


-- ============================
-- Property
-- ============================
CREATE TABLE property (
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
CREATE TABLE tenant (
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
CREATE TABLE lease (
    id              SERIAL PRIMARY KEY,
    property_id     INTEGER NOT NULL REFERENCES property(id) ON DELETE CASCADE,
    tenant_id       INTEGER NOT NULL REFERENCES tenant(id) ON DELETE CASCADE,
    start_date      DATE NOT NULL,
    end_date        DATE,
    notes           TEXT,
    created_at      TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX ix_lease_property ON lease(property_id);
CREATE INDEX ix_lease_tenant ON lease(tenant_id);

-- ============================
-- RentRate (rent changes over time)
-- ============================
CREATE TABLE rent_rate (
    id              SERIAL PRIMARY KEY,
    lease_id        INTEGER NOT NULL REFERENCES lease(id) ON DELETE CASCADE,
    effective_from  DATE NOT NULL,
    amount          NUMERIC(12,2) NOT NULL,
    notes           TEXT
);

CREATE INDEX ix_rent_rate_lease ON rent_rate(lease_id);

-- ============================
-- ServiceType (Lookup)
-- ============================
CREATE TABLE service_type (
    id          SERIAL PRIMARY KEY,
    name        VARCHAR(100) NOT NULL,
    is_utility  BOOLEAN NOT NULL DEFAULT TRUE
);

-- Seed service types
INSERT INTO service_type (name, is_utility) VALUES
('Water', TRUE),
('Electricity', TRUE),
('Sanitation', TRUE),
('Refuse', TRUE),
('Body Corporate', FALSE),
('Other', FALSE);

-- ============================
-- SourceFile (Uploaded documents)
-- ============================
CREATE TABLE source_file (
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
CREATE TABLE service_charge (
    id              BIGSERIAL PRIMARY KEY,
    lease_id        INTEGER NOT NULL REFERENCES lease(id) ON DELETE CASCADE,
    service_type_id INTEGER NOT NULL REFERENCES service_type(id),
    billing_period  DATE NOT NULL, -- first day of month
    amount          NUMERIC(12,2) NOT NULL,
    source_file_id  BIGINT REFERENCES source_file(id),
    notes           TEXT
);

CREATE INDEX ix_service_charge_lease ON service_charge(lease_id);
CREATE INDEX ix_service_charge_period ON service_charge(billing_period);

-- ============================
-- Payment
-- ============================
CREATE TABLE payment (
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

CREATE INDEX ix_payment_lease ON payment(lease_id);
CREATE INDEX ix_payment_date ON payment(paid_on);

-- ============================
-- Prevent multiple active leases per property
-- ============================
CREATE UNIQUE INDEX ux_active_lease_per_property
ON lease(property_id)
WHERE end_date IS NULL;