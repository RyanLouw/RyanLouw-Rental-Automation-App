-- =========================================
-- 0003.sql - Seed dummy data for testing UI
-- =========================================

-- Property (one active test property)
INSERT INTO property (name, address_line1, notes, is_active)
SELECT 'Demo Property - Green Villa', '12 Test Street, Cape Town', 'Seeded test property', TRUE
WHERE NOT EXISTS (
    SELECT 1 FROM property WHERE name = 'Demo Property - Green Villa'
);

-- Tenant with opening outstanding balance (added in 0002)
INSERT INTO tenant (full_name, email, phone, notes, is_active, current_amount_outstanding)
SELECT 'Demo Tenant - Alex Smith', 'alex.smith@example.com', '+27-82-000-0000', 'Seeded test tenant', TRUE, 1750.00
WHERE NOT EXISTS (
    SELECT 1 FROM tenant WHERE full_name = 'Demo Tenant - Alex Smith'
);

-- Active lease for demo property + tenant
INSERT INTO lease (property_id, tenant_id, start_date, end_date, notes)
SELECT p.id, t.id, (date_trunc('month', CURRENT_DATE) - INTERVAL '3 months')::date, NULL, 'Seeded active lease'
FROM property p
JOIN tenant t ON t.full_name = 'Demo Tenant - Alex Smith'
WHERE p.name = 'Demo Property - Green Villa'
AND NOT EXISTS (
    SELECT 1
    FROM lease l
    WHERE l.property_id = p.id
      AND l.tenant_id = t.id
      AND l.end_date IS NULL
);

-- Current rent rate for active lease
INSERT INTO rent_rate (lease_id, effective_from, amount, notes)
SELECT l.id, date_trunc('month', CURRENT_DATE)::date, 8500.00, 'Seeded current rent'
FROM lease l
JOIN property p ON p.id = l.property_id
JOIN tenant t ON t.id = l.tenant_id
WHERE p.name = 'Demo Property - Green Villa'
  AND t.full_name = 'Demo Tenant - Alex Smith'
  AND l.end_date IS NULL
  AND NOT EXISTS (
      SELECT 1 FROM rent_rate rr
      WHERE rr.lease_id = l.id
        AND rr.effective_from = date_trunc('month', CURRENT_DATE)::date
  );

-- Utility/service charges for current month
INSERT INTO service_charge (lease_id, service_type_id, billing_period, amount, notes)
SELECT l.id, st.id, date_trunc('month', CURRENT_DATE)::date, x.amount, x.notes
FROM lease l
JOIN property p ON p.id = l.property_id
JOIN tenant t ON t.id = l.tenant_id
JOIN LATERAL (
    VALUES
        ('Water', 640.00::numeric, 'Seeded water charge'),
        ('Electricity', 920.00::numeric, 'Seeded electricity charge'),
        ('Refuse', 280.00::numeric, 'Seeded refuse charge')
) AS x(service_name, amount, notes) ON TRUE
JOIN service_type st ON st.name = x.service_name
WHERE p.name = 'Demo Property - Green Villa'
  AND t.full_name = 'Demo Tenant - Alex Smith'
  AND l.end_date IS NULL
  AND NOT EXISTS (
      SELECT 1
      FROM service_charge sc
      WHERE sc.lease_id = l.id
        AND sc.service_type_id = st.id
        AND sc.billing_period = date_trunc('month', CURRENT_DATE)::date
  );

-- Payments for current month
INSERT INTO payment (lease_id, paid_on, amount, payer_name, reference, matched_to_period, notes)
SELECT l.id, x.paid_on, x.amount, 'Alex Smith', x.reference, date_trunc('month', CURRENT_DATE)::date, x.notes
FROM lease l
JOIN property p ON p.id = l.property_id
JOIN tenant t ON t.id = l.tenant_id
JOIN LATERAL (
    VALUES
        ((date_trunc('month', CURRENT_DATE) + INTERVAL '3 days')::date, 4000.00::numeric, 'DEMO-PAY-001', 'Seeded partial payment 1'),
        ((date_trunc('month', CURRENT_DATE) + INTERVAL '16 days')::date, 2500.00::numeric, 'DEMO-PAY-002', 'Seeded partial payment 2')
) AS x(paid_on, amount, reference, notes) ON TRUE
WHERE p.name = 'Demo Property - Green Villa'
  AND t.full_name = 'Demo Tenant - Alex Smith'
  AND l.end_date IS NULL
  AND NOT EXISTS (
      SELECT 1
      FROM payment py
      WHERE py.lease_id = l.id
        AND py.reference = x.reference
  );
