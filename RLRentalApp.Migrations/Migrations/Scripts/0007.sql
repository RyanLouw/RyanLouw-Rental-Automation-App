-- Backfill existing databases where 0006 may have already run before the
-- default tenants were assigned the bank PDF reference.
UPDATE tenant
SET payment_reference = 'Heino Huur'
WHERE full_name IN ('Default Tenant', 'Demo Tenant - Alex Smith')
  AND (COALESCE(payment_reference, '') = '' OR payment_reference = 'Alex Smith');
