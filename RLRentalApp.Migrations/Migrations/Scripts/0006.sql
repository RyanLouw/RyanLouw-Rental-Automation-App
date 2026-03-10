-- =========================================
-- 0006.sql - Reset rental data to one property + one tenant
-- =========================================

-- Clear transactional/domain rows and reset ids.
-- Keep lookup rows such as service_type intact.
TRUNCATE TABLE
    statement_sdt,
    payment,
    service_charge,
    source_file,
    rent_rate,
    lease,
    tenant,
    property
RESTART IDENTITY CASCADE;

-- Insert one property
WITH inserted_property AS (
    INSERT INTO property (name, address_line1, notes, is_active)
    VALUES ('Default Property', '1 Default Street', 'Reset by migration 0006', TRUE)
    RETURNING id
),
inserted_tenant AS (
    INSERT INTO tenant (full_name, email, phone, notes, is_active, current_amount_outstanding, deposit_held)
    VALUES ('Default Tenant', 'louwryan2@gmail.com', '', 'Reset by migration 0006', TRUE, 0.00, 500.00)
    RETURNING id
),
inserted_lease AS (
    INSERT INTO lease (property_id, tenant_id, start_date, end_date, notes)
    SELECT p.id,
           t.id,
           date_trunc('month', CURRENT_DATE)::date,
           NULL,
           'Default active lease'
    FROM inserted_property p
    CROSS JOIN inserted_tenant t
    RETURNING id, property_id, tenant_id
),
inserted_rent AS (
    INSERT INTO rent_rate (lease_id, effective_from, amount, notes)
    SELECT l.id,
           date_trunc('month', CURRENT_DATE)::date,
           500.00,
           'Default rent'
    FROM inserted_lease l
    RETURNING id, lease_id
)
INSERT INTO statement_sdt (property_id, tenant_id, lease_id, entry_date, entry_type, description, amount, source_table, source_id)
SELECT l.property_id,
       l.tenant_id,
       l.id,
       date_trunc('month', CURRENT_DATE)::date,
       'Rent',
       'Rent for statement month',
       500.00,
       'rent_rate',
       r.id
FROM inserted_lease l
JOIN inserted_rent r ON r.lease_id = l.id;

-- Deposit ledger line
INSERT INTO statement_sdt (property_id, tenant_id, lease_id, entry_date, entry_type, description, amount, source_table, source_id)
SELECT l.property_id,
       l.tenant_id,
       l.id,
       date_trunc('month', CURRENT_DATE)::date,
       'Deposit',
       'Deposit required',
       500.00,
       'tenant_deposit',
       t.id
FROM lease l
JOIN tenant t ON t.id = l.tenant_id
WHERE l.end_date IS NULL;
