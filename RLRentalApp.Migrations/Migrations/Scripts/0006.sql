ALTER TABLE tenant
ADD COLUMN IF NOT EXISTS payment_reference VARCHAR(200);

CREATE INDEX IF NOT EXISTS ix_tenant_payment_reference
ON tenant(payment_reference);
