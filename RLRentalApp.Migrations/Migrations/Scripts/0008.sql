-- Seed extra active renter/property scenarios for testing one-bank-PDF matching.
-- These references are visible in the sample FNB statement PDF used during testing:
-- Cornish = paid full rent + services, M De Villiers = paid rent only, Powerball = paid less than rent.
ALTER TABLE tenant
ADD COLUMN IF NOT EXISTS payment_reference VARCHAR(200);

CREATE INDEX IF NOT EXISTS ix_tenant_payment_reference
ON tenant(payment_reference);

WITH demo_data(property_name, address_line1) AS (
    VALUES
        ('Payment Demo - Cornish Happy', '161 Cornish Street'),
        ('Payment Demo - De Villiers Rent Only', '24 De Villiers Avenue'),
        ('Payment Demo - Powerball Short Rent', '22 Powerball Road')
)
INSERT INTO property (name, address_line1, notes, is_active)
SELECT property_name, address_line1, 'Seeded payment matching scenario', TRUE
FROM demo_data
WHERE NOT EXISTS (SELECT 1 FROM property p WHERE p.name = demo_data.property_name);

WITH demo_data(tenant_name, tenant_email, tenant_phone, payment_reference) AS (
    VALUES
        ('W I Cornish', 'cornish@example.com', '+27-82-000-0101', 'Cornish'),
        ('M De Villiers', 'devilliers@example.com', '+27-82-000-0102', 'M De Villiers'),
        ('Powerball Demo Tenant', 'powerball@example.com', '+27-82-000-0103', 'Powerball')
)
INSERT INTO tenant (full_name, email, phone, payment_reference, notes, is_active, current_amount_outstanding)
SELECT tenant_name, tenant_email, tenant_phone, payment_reference, 'Seeded payment matching scenario', TRUE, 0.00
FROM demo_data
WHERE NOT EXISTS (SELECT 1 FROM tenant t WHERE t.full_name = demo_data.tenant_name);

WITH demo_data(tenant_name, payment_reference) AS (
    VALUES
        ('W I Cornish', 'Cornish'),
        ('M De Villiers', 'M De Villiers'),
        ('Powerball Demo Tenant', 'Powerball')
)
UPDATE tenant t
SET payment_reference = d.payment_reference
FROM demo_data d
WHERE t.full_name = d.tenant_name
  AND COALESCE(t.payment_reference, '') = '';

WITH demo_data(property_name, tenant_name) AS (
    VALUES
        ('Payment Demo - Cornish Happy', 'W I Cornish'),
        ('Payment Demo - De Villiers Rent Only', 'M De Villiers'),
        ('Payment Demo - Powerball Short Rent', 'Powerball Demo Tenant')
)
INSERT INTO lease (property_id, tenant_id, start_date, end_date, notes)
SELECT p.id, t.id, date_trunc('month', CURRENT_DATE)::date, NULL, 'Seeded active payment matching lease'
FROM demo_data d
JOIN property p ON p.name = d.property_name
JOIN tenant t ON t.full_name = d.tenant_name
WHERE NOT EXISTS (
    SELECT 1
    FROM lease l
    WHERE l.property_id = p.id
      AND l.tenant_id = t.id
      AND l.end_date IS NULL
);

WITH demo_data(property_name, tenant_name, rent_amount) AS (
    VALUES
        ('Payment Demo - Cornish Happy', 'W I Cornish', 10000.00::numeric),
        ('Payment Demo - De Villiers Rent Only', 'M De Villiers', 11500.00::numeric),
        ('Payment Demo - Powerball Short Rent', 'Powerball Demo Tenant', 500.00::numeric)
), inserted_rent AS (
    INSERT INTO rent_rate (lease_id, effective_from, amount, notes)
    SELECT l.id, date_trunc('month', CURRENT_DATE)::date, d.rent_amount, 'Seeded rent for payment matching scenario'
    FROM demo_data d
    JOIN property p ON p.name = d.property_name
    JOIN tenant t ON t.full_name = d.tenant_name
    JOIN lease l ON l.property_id = p.id AND l.tenant_id = t.id AND l.end_date IS NULL
    WHERE NOT EXISTS (
        SELECT 1 FROM rent_rate rr
        WHERE rr.lease_id = l.id
          AND rr.effective_from = date_trunc('month', CURRENT_DATE)::date
    )
    RETURNING id, lease_id, effective_from, amount
)
INSERT INTO statement_sdt (property_id, tenant_id, lease_id, entry_date, entry_type, description, amount, source_table, source_id)
SELECT l.property_id, l.tenant_id, rr.lease_id, rr.effective_from, 'Rent', 'Rent for statement month', rr.amount, 'rent_rate', rr.id
FROM inserted_rent rr
JOIN lease l ON l.id = rr.lease_id
ON CONFLICT (source_table, source_id) DO NOTHING;

WITH demo_data(property_name, tenant_name, service_name, service_amount) AS (
    VALUES
        ('Payment Demo - Cornish Happy', 'W I Cornish', 'Water', 1200.00::numeric),
        ('Payment Demo - De Villiers Rent Only', 'M De Villiers', 'Electricity', 900.00::numeric),
        ('Payment Demo - Powerball Short Rent', 'Powerball Demo Tenant', 'Other', 100.00::numeric)
), inserted_services AS (
    INSERT INTO service_charge (lease_id, service_type_id, billing_period, amount, notes)
    SELECT l.id, st.id, date_trunc('month', CURRENT_DATE)::date, d.service_amount, 'Seeded service charge for payment matching scenario'
    FROM demo_data d
    JOIN property p ON p.name = d.property_name
    JOIN tenant t ON t.full_name = d.tenant_name
    JOIN lease l ON l.property_id = p.id AND l.tenant_id = t.id AND l.end_date IS NULL
    JOIN service_type st ON LOWER(st.name) = LOWER(d.service_name)
    WHERE NOT EXISTS (
        SELECT 1
        FROM service_charge sc
        WHERE sc.lease_id = l.id
          AND sc.service_type_id = st.id
          AND sc.billing_period = date_trunc('month', CURRENT_DATE)::date
    )
    RETURNING id, lease_id, billing_period, amount
)
INSERT INTO statement_sdt (property_id, tenant_id, lease_id, entry_date, entry_type, description, amount, source_table, source_id)
SELECT l.property_id, l.tenant_id, sc.lease_id, sc.billing_period, 'Service', CONCAT(COALESCE(st.name, 'Service'), ' charge'), sc.amount, 'service_charge', sc.id
FROM inserted_services sc
JOIN lease l ON l.id = sc.lease_id
JOIN service_charge svc ON svc.id = sc.id
JOIN service_type st ON st.id = svc.service_type_id
ON CONFLICT (source_table, source_id) DO NOTHING;
