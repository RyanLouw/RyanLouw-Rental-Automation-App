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

    public async Task<decimal> LoadCurrentMonthServiceTotalAsync(int leaseId)
    {
        var connection = _authDbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT COALESCE(SUM(amount), 0)
            FROM service_charge
            WHERE lease_id = @leaseId
              AND date_trunc('month', billing_period) = date_trunc('month', CURRENT_DATE);";

        var parameter = cmd.CreateParameter();
        parameter.ParameterName = "@leaseId";
        parameter.Value = leaseId;
        cmd.Parameters.Add(parameter);

        var value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? 0m : Convert.ToDecimal(value);
    }

    public async Task<decimal> LoadCurrentMonthPaymentTotalAsync(int leaseId)
    {
        var connection = _authDbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT COALESCE(SUM(amount), 0)
            FROM payment
            WHERE lease_id = @leaseId
              AND date_trunc('month', paid_on) = date_trunc('month', CURRENT_DATE);";

        var parameter = cmd.CreateParameter();
        parameter.ParameterName = "@leaseId";
        parameter.Value = leaseId;
        cmd.Parameters.Add(parameter);

        var value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? 0m : Convert.ToDecimal(value);
    }

    public async Task<List<StatementEntryDataModel>> LoadMonthEntriesAsync(int leaseId, DateTime monthStart)
    {
        var connection = _authDbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT billing_period, 'Service' AS entry_type, 'Service charge' AS description, amount
            FROM service_charge
            WHERE lease_id = @leaseId
              AND date_trunc('month', billing_period) = date_trunc('month', @monthStart)

            UNION ALL

            SELECT paid_on, 'Payment' AS entry_type, COALESCE(reference, 'Payment received') AS description, -amount
            FROM payment
            WHERE lease_id = @leaseId
              AND date_trunc('month', paid_on) = date_trunc('month', @monthStart)

            ORDER BY 1, 2;";

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
                EntryDate = reader.GetDateTime(0),
                EntryType = reader.GetString(1),
                Description = reader.GetString(2),
                Amount = reader.GetDecimal(3)
            });
        }

        return entries;
    }




    public async Task<int> UpsertRentRateAsync(int leaseId, DateTime effectiveFrom, decimal amount, string notes)
    {
        var connection = _authDbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);

        await using var updateCmd = connection.CreateCommand();
        updateCmd.CommandText = @"
            UPDATE rent_rate
            SET amount = @amount,
                notes = @notes
            WHERE lease_id = @leaseId
              AND effective_from = @effectiveFrom;";

        AddParameter(updateCmd, "@leaseId", leaseId);
        AddParameter(updateCmd, "@effectiveFrom", effectiveFrom.Date);
        AddParameter(updateCmd, "@amount", amount);
        AddParameter(updateCmd, "@notes", notes);

        var updated = await updateCmd.ExecuteNonQueryAsync();
        if (updated > 0)
        {
            return updated;
        }

        await using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = @"
            INSERT INTO rent_rate (lease_id, effective_from, amount, notes)
            VALUES (@leaseId, @effectiveFrom, @amount, @notes);";

        AddParameter(insertCmd, "@leaseId", leaseId);
        AddParameter(insertCmd, "@effectiveFrom", effectiveFrom.Date);
        AddParameter(insertCmd, "@amount", amount);
        AddParameter(insertCmd, "@notes", notes);

        return await insertCmd.ExecuteNonQueryAsync();
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
                LIMIT 1;";

            AddParameter(cmd, "@leaseId", leaseId);
            AddParameter(cmd, "@billingPeriod", charge.BillingPeriod.Date);
            AddParameter(cmd, "@amount", charge.Amount);
            AddParameter(cmd, "@notes", charge.Notes);
            AddParameter(cmd, "@serviceTypeName", charge.ServiceTypeName);

            inserted += await cmd.ExecuteNonQueryAsync();
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
                VALUES (@leaseId, @paidOn, @amount, @reference, @notes);";

            AddParameter(cmd, "@leaseId", leaseId);
            AddParameter(cmd, "@paidOn", payment.PaidOn.Date);
            AddParameter(cmd, "@amount", payment.Amount);
            AddParameter(cmd, "@reference", payment.Reference);
            AddParameter(cmd, "@notes", payment.Notes);

            inserted += await cmd.ExecuteNonQueryAsync();
        }

        return inserted;
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
