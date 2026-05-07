ALTER TABLE tenant
ADD COLUMN IF NOT EXISTS payment_reference VARCHAR(200);

CREATE INDEX IF NOT EXISTS ix_tenant_payment_reference
ON tenant(payment_reference);

-- Backfill seeded/default tenants so the all-renter bank PDF matching has
-- a reference value immediately after this migration is applied.
UPDATE tenant
SET payment_reference = 'Alex Smith'
WHERE full_name = 'Demo Tenant - Alex Smith'
  AND COALESCE(payment_reference, '') = '';
