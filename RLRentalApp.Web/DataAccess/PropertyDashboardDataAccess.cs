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
        await using var connection = _authDbContext.Database.GetDbConnection();
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
        await using var connection = _authDbContext.Database.GetDbConnection();
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
        await using var connection = _authDbContext.Database.GetDbConnection();
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
        await using var connection = _authDbContext.Database.GetDbConnection();
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

    public async Task<decimal?> LoadLatestRentAsync(int leaseId)
    {
        await using var connection = _authDbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT amount
            FROM rent_rate
            WHERE lease_id = @leaseId
            ORDER BY effective_from DESC
            LIMIT 1;";

        var parameter = cmd.CreateParameter();
        parameter.ParameterName = "@leaseId";
        parameter.Value = leaseId;
        cmd.Parameters.Add(parameter);

        var value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? null : Convert.ToDecimal(value);
    }

    public async Task<decimal> LoadCurrentMonthServiceTotalAsync(int leaseId)
    {
        await using var connection = _authDbContext.Database.GetDbConnection();
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
        await using var connection = _authDbContext.Database.GetDbConnection();
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

    public async Task<List<StatementEntryDataModel>> LoadCurrentMonthEntriesAsync(int leaseId)
    {
        await using var connection = _authDbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT billing_period, 'Service' AS entry_type, 'Service charge' AS description, amount
            FROM service_charge
            WHERE lease_id = @leaseId
              AND date_trunc('month', billing_period) = date_trunc('month', CURRENT_DATE)

            UNION ALL

            SELECT paid_on, 'Payment' AS entry_type, COALESCE(reference, 'Payment received') AS description, -amount
            FROM payment
            WHERE lease_id = @leaseId
              AND date_trunc('month', paid_on) = date_trunc('month', CURRENT_DATE)

            ORDER BY 1, 2;";

        var parameter = cmd.CreateParameter();
        parameter.ParameterName = "@leaseId";
        parameter.Value = leaseId;
        cmd.Parameters.Add(parameter);

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

    private static async Task EnsureConnectionOpenAsync(DbConnection connection)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
    }
}
