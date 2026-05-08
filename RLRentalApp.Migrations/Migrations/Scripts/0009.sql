-- =========================================
-- 0009.sql - Clear rental data for real data entry
-- =========================================
-- This intentionally removes demo/test rental records so the database can start
-- clean for real property, tenant, lease, rent, service, payment, and source-file data.
--
-- Keep lookup/security/version tables intact:
--   - service_type stays populated for Water/Electricity/etc.
--   - ASP.NET identity/auth tables are not touched.
--   - FluentMigrator VersionInfo is not touched.

TRUNCATE TABLE
    statement_sdt,
    payment,
    service_charge,
    rent_rate,
    lease,
    tenant,
    property,
    source_file
RESTART IDENTITY CASCADE;
