-- Be defensive for existing databases where 0006 may not have completed or
-- may have run before the default tenant references were correct.
ALTER TABLE tenant
ADD COLUMN IF NOT EXISTS payment_reference VARCHAR(200);

CREATE INDEX IF NOT EXISTS ix_tenant_payment_reference
ON tenant(payment_reference);

UPDATE tenant
SET payment_reference = 'Heino Huur'
WHERE full_name IN ('Default Tenant', 'Demo Tenant - Alex Smith')
  AND (COALESCE(payment_reference, '') = '' OR payment_reference = 'Alex Smith');
