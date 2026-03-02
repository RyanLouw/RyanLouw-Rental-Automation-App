using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RLRentalApp.Models;
using RLRentalApp.Web.Data;
using System.Data.Common;
using System.Diagnostics;

namespace RLRentalApp.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly AuthDbContext _authDbContext;

    public HomeController(ILogger<HomeController> logger, AuthDbContext authDbContext)
    {
        _logger = logger;
        _authDbContext = authDbContext;
    }

    public async Task<IActionResult> Index()
    {
        var properties = await LoadPropertiesAsync();

        var vm = new HomeIndexVm
        {
            Properties = properties
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> PropertyStatus(int propertyId)
    {
        var status = await LoadPropertyStatusAsync(propertyId);

        if (status is null)
        {
            return NotFound();
        }

        return Json(status);
    }

    [HttpGet]
    public async Task<IActionResult> PropertyStatement(int propertyId)
    {
        var statement = await LoadPropertyStatementAsync(propertyId);

        if (statement is null)
        {
            return NotFound();
        }

        return Json(statement);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    private async Task<List<PropertyOptionVm>> LoadPropertiesAsync()
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

    private async Task<PropertyStatusVm?> LoadPropertyStatusAsync(int propertyId)
    {
        await using var connection = _authDbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);

        var property = await LoadPropertyAsync(connection, propertyId);
        if (property is null)
        {
            return null;
        }

        var activeLease = await LoadActiveLeaseAsync(connection, propertyId);

        if (activeLease is null)
        {
            return new PropertyStatusVm
            {
                PropertyId = property.Id,
                PropertyName = property.Name,
                PropertyAddress = property.AddressLine1,
                IsPropertyActive = property.IsActive,
                HasActiveLease = false
            };
        }

        var latestRent = await LoadLatestRentAsync(connection, activeLease.LeaseId);
        var serviceTotal = await LoadCurrentMonthServiceTotalAsync(connection, activeLease.LeaseId);
        var paymentTotal = await LoadCurrentMonthPaymentTotalAsync(connection, activeLease.LeaseId);
        var openingOutstanding = await LoadOpeningOutstandingAsync(connection, activeLease.TenantId);

        return new PropertyStatusVm
        {
            PropertyId = property.Id,
            PropertyName = property.Name,
            PropertyAddress = property.AddressLine1,
            IsPropertyActive = property.IsActive,
            HasActiveLease = true,
            LeaseId = activeLease.LeaseId,
            TenantId = activeLease.TenantId,
            TenantName = activeLease.TenantName,
            LeaseStartDate = activeLease.StartDate,
            LatestRent = latestRent,
            OpeningOutstanding = openingOutstanding,
            CurrentMonthServiceTotal = serviceTotal,
            CurrentMonthPaymentTotal = paymentTotal,
            CurrentBalance = openingOutstanding + (latestRent ?? 0m) + serviceTotal - paymentTotal
        };
    }

    private async Task<PropertyStatementVm?> LoadPropertyStatementAsync(int propertyId)
    {
        await using var connection = _authDbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);

        var property = await LoadPropertyAsync(connection, propertyId);
        var activeLease = await LoadActiveLeaseAsync(connection, propertyId);

        if (property is null || activeLease is null)
        {
            return null;
        }

        var openingOutstanding = await LoadOpeningOutstandingAsync(connection, activeLease.TenantId);
        var latestRent = await LoadLatestRentAsync(connection, activeLease.LeaseId);
        var statementEntries = await LoadCurrentMonthEntriesAsync(connection, activeLease.LeaseId, latestRent);

        var runningBalance = openingOutstanding;
        foreach (var entry in statementEntries)
        {
            runningBalance += entry.Amount;
            entry.RunningBalance = runningBalance;
        }

        return new PropertyStatementVm
        {
            PropertyId = property.Id,
            LeaseId = activeLease.LeaseId,
            TenantId = activeLease.TenantId,
            PropertyName = property.Name,
            TenantName = activeLease.TenantName,
            OpeningOutstanding = openingOutstanding,
            CurrentBalance = runningBalance,
            Entries = statementEntries
        };
    }

    private static async Task EnsureConnectionOpenAsync(DbConnection connection)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
    }

    private static async Task<PropertyOptionVm?> LoadPropertyAsync(DbConnection connection, int propertyId)
    {
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

    private static async Task<ActiveLeaseVm?> LoadActiveLeaseAsync(DbConnection connection, int propertyId)
    {
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

        return new ActiveLeaseVm
        {
            LeaseId = reader.GetInt32(0),
            TenantId = reader.GetInt32(1),
            TenantName = reader.GetString(2),
            StartDate = reader.GetDateTime(3)
        };
    }

    private static async Task<decimal> LoadOpeningOutstandingAsync(DbConnection connection, int tenantId)
    {
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

    private static async Task<decimal?> LoadLatestRentAsync(DbConnection connection, int leaseId)
    {
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

    private static async Task<decimal> LoadCurrentMonthServiceTotalAsync(DbConnection connection, int leaseId)
    {
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

    private static async Task<decimal> LoadCurrentMonthPaymentTotalAsync(DbConnection connection, int leaseId)
    {
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

    private static async Task<List<PropertyStatementEntryVm>> LoadCurrentMonthEntriesAsync(DbConnection connection, int leaseId, decimal? latestRent)
    {
        var entries = new List<PropertyStatementEntryVm>();

        if (latestRent.HasValue)
        {
            entries.Add(new PropertyStatementEntryVm
            {
                EntryDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1),
                EntryType = "Rent",
                Description = "Current month rent",
                Amount = latestRent.Value
            });
        }

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

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(new PropertyStatementEntryVm
            {
                EntryDate = reader.GetDateTime(0),
                EntryType = reader.GetString(1),
                Description = reader.GetString(2),
                Amount = reader.GetDecimal(3)
            });
        }

        return entries.OrderBy(x => x.EntryDate).ThenBy(x => x.EntryType).ToList();
    }

    private sealed class ActiveLeaseVm
    {
        public int LeaseId { get; set; }
        public int TenantId { get; set; }
        public string TenantName { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
    }
}
