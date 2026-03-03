ALTER TABLE tenant
ADD COLUMN current_amount_outstanding NUMERIC(12,2) NOT NULL DEFAULT 0;
