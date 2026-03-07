using Microsoft.EntityFrameworkCore;
using RLRentalApp.Models;
using RLRentalApp.Web.Data;
using System.Data;
using System.Data.Common;

namespace RLRentalApp.Web.DataAccess;

public class PropertyDashboardDataAccess : IPropertyDashboardDataAccess
{
    private readonly AuthDbContext _authDbContext;

    public PropertyDashboardDataAccess(AuthDbContext authDbContext)
    {
        _authDbContext = authDbContext;
    }

    public async Task<List<PropertyOptionVm>> LoadPropertiesAsync()
    {
        var connection = _authDbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, name, COALESCE(address_line1, ''), is_active
            FROM property
            ORDER BY name;";

        var result = new List<PropertyOptionVm>();
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            result.Add(new PropertyOptionVm
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                AddressLine1 = reader.GetString(2),
                IsActive = reader.GetBoolean(3)
            });
        }

        return result;
    }

    public async Task<PropertyOptionVm?> LoadPropertyAsync(int propertyId)
    {
        var connection = _authDbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, name, COALESCE(address_line1, ''), is_active
            FROM property
            WHERE id = @id
            LIMIT 1;";

        var parameter = cmd.CreateParameter();
        parameter.ParameterName = "@id";
        parameter.Value = propertyId;
        cmd.Parameters.Add(parameter);

        await using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new PropertyOptionVm
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            AddressLine1 = reader.GetString(2),
            IsActive = reader.GetBoolean(3)
        };
    }

    public async Task<ActiveLeaseDataModel?> LoadActiveLeaseAsync(int propertyId)
    {
        var connection = _authDbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT l.id, l.tenant_id, t.full_name, l.start_date
            FROM lease l
            INNER JOIN tenant t ON t.id = l.tenant_id
            WHERE l.property_id = @propertyId AND l.end_date IS NULL
            ORDER BY l.start_date DESC
            LIMIT 1;";

        var parameter = cmd.CreateParameter();
        parameter.ParameterName = "@propertyId";
        parameter.Value = propertyId;
        cmd.Parameters.Add(parameter);

        await using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new ActiveLeaseDataModel
        {
            LeaseId = reader.GetInt32(0),
            TenantId = reader.GetInt32(1),
            TenantName = reader.GetString(2),
            StartDate = reader.GetDateTime(3)
        };
    }

    public async Task<decimal> LoadOpeningOutstandingAsync(int tenantId)
    {
        var connection = _authDbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT COALESCE(current_amount_outstanding, 0)
            FROM tenant
            WHERE id = @tenantId
            LIMIT 1;";

        var parameter = cmd.CreateParameter();
        parameter.ParameterName = "@tenantId";
        parameter.Value = tenantId;
        cmd.Parameters.Add(parameter);

        var value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? 0m : Convert.ToDecimal(value);
    }

    public async Task<decimal?> LoadLatestRentAsync(int leaseId, DateTime asOfDate)
    {
        var connection = _authDbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT amount
            FROM rent_rate
            WHERE lease_id = @leaseId
              AND effective_from <= @asOfDate
            ORDER BY effective_from DESC
            LIMIT 1;";

        var parameter = cmd.CreateParameter();
        parameter.ParameterName = "@leaseId";
        parameter.Value = leaseId;
        cmd.Parameters.Add(parameter);
        AddParameter(cmd, "@asOfDate", asOfDate.Date);

        var value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? null : Convert.ToDecimal(value);
    }

    public async Task<StatementSnapshotDataModel> LoadStatementSnapshotAsync(int leaseId, DateTime monthStart)
    {
        var connection = _authDbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);

        var normalizedMonthStart = new DateTime(monthStart.Year, monthStart.Month, 1);
        var nextMonthStart = normalizedMonthStart.AddMonths(1);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT
                COALESCE(SUM(CASE WHEN entry_date < @nextMonthStart THEN amount ELSE 0 END), 0) AS amount_through_month,
                COALESCE(SUM(CASE WHEN entry_date >= @monthStart AND entry_date < @nextMonthStart AND entry_type = 'Service' THEN amount ELSE 0 END), 0) AS service_total,
                COALESCE(SUM(CASE WHEN entry_date >= @monthStart AND entry_date < @nextMonthStart AND entry_type = 'Payment' THEN ABS(amount) ELSE 0 END), 0) AS payment_total
            FROM statement_sdt
            WHERE lease_id = @leaseId;";

        AddParameter(cmd, "@leaseId", leaseId);
        AddParameter(cmd, "@monthStart", normalizedMonthStart.Date);
        AddParameter(cmd, "@nextMonthStart", nextMonthStart.Date);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return new StatementSnapshotDataModel();
        }

        return new StatementSnapshotDataModel
        {
            AmountThroughMonth = reader.IsDBNull(0) ? 0m : reader.GetDecimal(0),
            CurrentMonthServiceTotal = reader.IsDBNull(1) ? 0m : reader.GetDecimal(1),
            CurrentMonthPaymentTotal = reader.IsDBNull(2) ? 0m : reader.GetDecimal(2)
        };
    }

    public async Task<decimal> LoadStatementAmountBeforeDateAsync(int leaseId, DateTime beforeDateExclusive)
    {
        var connection = _authDbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT COALESCE(SUM(amount), 0)
            FROM statement_sdt
            WHERE lease_id = @leaseId
              AND entry_date < @beforeDateExclusive;";

        AddParameter(cmd, "@leaseId", leaseId);
        AddParameter(cmd, "@beforeDateExclusive", beforeDateExclusive.Date);

        var value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? 0m : Convert.ToDecimal(value);
    }

    public async Task<List<StatementEntryDataModel>> LoadMonthEntriesAsync(int leaseId, DateTime monthStart)
    {
        var connection = _authDbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, entry_date, entry_type, description, amount, source_table
            FROM statement_sdt
            WHERE lease_id = @leaseId
              AND date_trunc('month', entry_date) = date_trunc('month', @monthStart)
            ORDER BY entry_date, entry_type;";

        var parameter = cmd.CreateParameter();
        parameter.ParameterName = "@leaseId";
        parameter.Value = leaseId;
        cmd.Parameters.Add(parameter);
        AddParameter(cmd, "@monthStart", monthStart.Date);

        var entries = new List<StatementEntryDataModel>();
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            entries.Add(new StatementEntryDataModel
            {
                StatementEntryId = reader.GetInt64(0),
                EntryDate = reader.GetDateTime(1),
                EntryType = reader.GetString(2),
                Description = reader.GetString(3),
                Amount = reader.GetDecimal(4),
                SourceTable = reader.GetString(5)
            });
        }

        return entries;
    }






    public async Task<UpdateStatementEntryResultVm> UpdateStatementEntryAsync(int leaseId, long statementEntryId, DateTime entryDate, decimal amount, string description)
    {
        var connection = _authDbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);
        await using var tx = await connection.BeginTransactionAsync();

        try
        {
            await using var fetchCmd = connection.CreateCommand();
            fetchCmd.Transaction = tx;
            fetchCmd.CommandText = @"
                SELECT source_table, source_id
                FROM statement_sdt
                WHERE id = @statementEntryId
                  AND lease_id = @leaseId
                LIMIT 1;";

            AddParameter(fetchCmd, "@statementEntryId", statementEntryId);
            AddParameter(fetchCmd, "@leaseId", leaseId);

            await using var reader = await fetchCmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return new UpdateStatementEntryResultVm { Success = false, Message = "Statement row not found for this property/lease." };
            }

            var sourceTable = reader.GetString(0);
            var sourceId = reader.GetInt64(1);
            await reader.CloseAsync();

            var normalizedAmount = amount;

            if (string.Equals(sourceTable, "payment", StringComparison.OrdinalIgnoreCase))
            {
                var paymentAmount = Math.Abs(amount);
                normalizedAmount = -paymentAmount;

                await using var paymentCmd = connection.CreateCommand();
                paymentCmd.Transaction = tx;
                paymentCmd.CommandText = @"
                    UPDATE payment
                    SET paid_on = @entryDate,
                        amount = @paymentAmount,
                        reference = @reference
                    WHERE id = @sourceId
                      AND lease_id = @leaseId;";

                AddParameter(paymentCmd, "@entryDate", entryDate.Date);
                AddParameter(paymentCmd, "@paymentAmount", paymentAmount);
                AddParameter(paymentCmd, "@reference", description);
                AddParameter(paymentCmd, "@sourceId", sourceId);
                AddParameter(paymentCmd, "@leaseId", leaseId);
                await paymentCmd.ExecuteNonQueryAsync();
            }
            else if (string.Equals(sourceTable, "service_charge", StringComparison.OrdinalIgnoreCase))
            {
                await using var serviceCmd = connection.CreateCommand();
                serviceCmd.Transaction = tx;
                serviceCmd.CommandText = @"
                    UPDATE service_charge
                    SET billing_period = @entryDate,
                        amount = @amount
                    WHERE id = @sourceId
                      AND lease_id = @leaseId;";

                AddParameter(serviceCmd, "@entryDate", entryDate.Date);
                AddParameter(serviceCmd, "@amount", amount);
                AddParameter(serviceCmd, "@sourceId", sourceId);
                AddParameter(serviceCmd, "@leaseId", leaseId);
                await serviceCmd.ExecuteNonQueryAsync();
            }
            else if (string.Equals(sourceTable, "rent_rate", StringComparison.OrdinalIgnoreCase))
            {
                await using var rentCmd = connection.CreateCommand();
                rentCmd.Transaction = tx;
                rentCmd.CommandText = @"
                    UPDATE rent_rate
                    SET effective_from = @entryDate,
                        amount = @amount,
                        notes = @notes
                    WHERE id = @sourceId
                      AND lease_id = @leaseId;";

                AddParameter(rentCmd, "@entryDate", new DateTime(entryDate.Year, entryDate.Month, 1));
                AddParameter(rentCmd, "@amount", amount);
                AddParameter(rentCmd, "@notes", "Updated from statement editor");
                AddParameter(rentCmd, "@sourceId", sourceId);
                AddParameter(rentCmd, "@leaseId", leaseId);
                await rentCmd.ExecuteNonQueryAsync();
            }
            else if (string.Equals(sourceTable, "tenant_deposit", StringComparison.OrdinalIgnoreCase))
            {
                // synthetic source; update ledger only
            }
            else
            {
                return new UpdateStatementEntryResultVm { Success = false, Message = "This row type is not editable yet." };
            }

            await using var ledgerCmd = connection.CreateCommand();
            ledgerCmd.Transaction = tx;
            ledgerCmd.CommandText = @"
                UPDATE statement_sdt
                SET entry_date = @entryDate,
                    description = @description,
                    amount = @amount,
                    updated_at = NOW()
                WHERE id = @statementEntryId
                  AND lease_id = @leaseId;";

            AddParameter(ledgerCmd, "@entryDate", entryDate.Date);
            AddParameter(ledgerCmd, "@description", description);
            AddParameter(ledgerCmd, "@amount", normalizedAmount);
            AddParameter(ledgerCmd, "@statementEntryId", statementEntryId);
            AddParameter(ledgerCmd, "@leaseId", leaseId);
            await ledgerCmd.ExecuteNonQueryAsync();

            await tx.CommitAsync();
            return new UpdateStatementEntryResultVm { Success = true, Message = "Statement row updated." };
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return new UpdateStatementEntryResultVm { Success = false, Message = $"Could not update statement row: {ex.Message}" };
        }
    }


    public async Task<int> UpsertRentRateAsync(int leaseId, DateTime effectiveFrom, decimal amount, string notes)
    {
        var connection = _authDbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);

        long? rentRateId = null;

        await using (var updateCmd = connection.CreateCommand())
        {
            updateCmd.CommandText = @"
                UPDATE rent_rate
                SET amount = @amount,
                    notes = @notes
                WHERE lease_id = @leaseId
                  AND effective_from = @effectiveFrom
                RETURNING id;";

            AddParameter(updateCmd, "@leaseId", leaseId);
            AddParameter(updateCmd, "@effectiveFrom", effectiveFrom.Date);
            AddParameter(updateCmd, "@amount", amount);
            AddParameter(updateCmd, "@notes", notes);

            var updatedId = await updateCmd.ExecuteScalarAsync();
            if (updatedId is not null and not DBNull)
            {
                rentRateId = Convert.ToInt64(updatedId);
            }
        }

        if (!rentRateId.HasValue)
        {
            await using var insertCmd = connection.CreateCommand();
            insertCmd.CommandText = @"
                INSERT INTO rent_rate (lease_id, effective_from, amount, notes)
                VALUES (@leaseId, @effectiveFrom, @amount, @notes)
                RETURNING id;";

            AddParameter(insertCmd, "@leaseId", leaseId);
            AddParameter(insertCmd, "@effectiveFrom", effectiveFrom.Date);
            AddParameter(insertCmd, "@amount", amount);
            AddParameter(insertCmd, "@notes", notes);

            var insertedId = await insertCmd.ExecuteScalarAsync();
            if (insertedId is not null and not DBNull)
            {
                rentRateId = Convert.ToInt64(insertedId);
            }
        }

        if (!rentRateId.HasValue)
        {
            return 0;
        }

        await UpsertStatementSdtEntryAsync(
            connection,
            leaseId,
            effectiveFrom.Date,
            "Rent",
            "Rent for statement month",
            amount,
            "rent_rate",
            rentRateId.Value);

        return 1;
    }

    public async Task<int> InsertServiceChargesAsync(int leaseId, List<ServiceChargeInsertDataModel> charges)
    {
        if (charges.Count == 0)
        {
            return 0;
        }

        var connection = _authDbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);

        var inserted = 0;

        foreach (var charge in charges)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO service_charge (lease_id, service_type_id, billing_period, amount, notes)
                SELECT @leaseId, st.id, @billingPeriod, @amount, @notes
                FROM service_type st
                WHERE LOWER(st.name) = LOWER(@serviceTypeName)
                LIMIT 1
                RETURNING id;";

            AddParameter(cmd, "@leaseId", leaseId);
            AddParameter(cmd, "@billingPeriod", charge.BillingPeriod.Date);
            AddParameter(cmd, "@amount", charge.Amount);
            AddParameter(cmd, "@notes", charge.Notes);
            AddParameter(cmd, "@serviceTypeName", charge.ServiceTypeName);

            var insertedId = await cmd.ExecuteScalarAsync();
            if (insertedId is null || insertedId is DBNull)
            {
                continue;
            }

            inserted += 1;
            await UpsertStatementSdtEntryAsync(
                connection,
                leaseId,
                charge.BillingPeriod.Date,
                "Service",
                $"{charge.ServiceTypeName} charge",
                charge.Amount,
                "service_charge",
                Convert.ToInt64(insertedId));
        }

        return inserted;
    }

    public async Task<bool> PaymentExistsAsync(int leaseId, DateTime paidOn, decimal amount)
    {
        var connection = _authDbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT 1
            FROM payment
            WHERE lease_id = @leaseId
              AND paid_on = @paidOn
              AND amount = @amount
            LIMIT 1;";

        AddParameter(cmd, "@leaseId", leaseId);
        AddParameter(cmd, "@paidOn", paidOn.Date);
        AddParameter(cmd, "@amount", amount);

        var exists = await cmd.ExecuteScalarAsync();
        return exists is not null and not DBNull;
    }

    public async Task<int> InsertPaymentsAsync(int leaseId, List<PaymentInsertDataModel> payments)
    {
        if (payments.Count == 0)
        {
            return 0;
        }

        var connection = _authDbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);

        var inserted = 0;

        foreach (var payment in payments)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO payment (lease_id, paid_on, amount, reference, notes)
                VALUES (@leaseId, @paidOn, @amount, @reference, @notes)
                RETURNING id;";

            AddParameter(cmd, "@leaseId", leaseId);
            AddParameter(cmd, "@paidOn", payment.PaidOn.Date);
            AddParameter(cmd, "@amount", payment.Amount);
            AddParameter(cmd, "@reference", payment.Reference);
            AddParameter(cmd, "@notes", payment.Notes);

            var insertedId = await cmd.ExecuteScalarAsync();
            if (insertedId is null || insertedId is DBNull)
            {
                continue;
            }

            inserted += 1;
            await UpsertStatementSdtEntryAsync(
                connection,
                leaseId,
                payment.PaidOn.Date,
                "Payment",
                string.IsNullOrWhiteSpace(payment.Reference) ? "Payment received" : payment.Reference,
                -payment.Amount,
                "payment",
                Convert.ToInt64(insertedId));
        }

        return inserted;
    }

    private static async Task UpsertStatementSdtEntryAsync(
        DbConnection connection,
        int leaseId,
        DateTime entryDate,
        string entryType,
        string description,
        decimal amount,
        string sourceTable,
        long sourceId)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO statement_sdt (property_id, tenant_id, lease_id, entry_date, entry_type, description, amount, source_table, source_id)
            SELECT l.property_id, l.tenant_id, l.id, @entryDate, @entryType, @description, @amount, @sourceTable, @sourceId
            FROM lease l
            WHERE l.id = @leaseId
            ON CONFLICT (source_table, source_id)
            DO UPDATE SET
                entry_date = EXCLUDED.entry_date,
                entry_type = EXCLUDED.entry_type,
                description = EXCLUDED.description,
                amount = EXCLUDED.amount,
                updated_at = NOW();";

        AddParameter(cmd, "@leaseId", leaseId);
        AddParameter(cmd, "@entryDate", entryDate.Date);
        AddParameter(cmd, "@entryType", entryType);
        AddParameter(cmd, "@description", description);
        AddParameter(cmd, "@amount", amount);
        AddParameter(cmd, "@sourceTable", sourceTable);
        AddParameter(cmd, "@sourceId", sourceId);

        await cmd.ExecuteNonQueryAsync();
    }

    private static void AddParameter(DbCommand cmd, string name, object? value)
    {
        var parameter = cmd.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(parameter);
    }

    private static async Task EnsureConnectionOpenAsync(DbConnection connection)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
    }
}
