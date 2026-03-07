using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RLRentalApp.Models;
using RLRentalApp.Web.Data;
using System.Data;
using System.Data.Common;

namespace RLRentalApp.Controllers;

[Authorize]
public class AdminController : Controller
{
    private readonly AuthDbContext _dbContext;

    public AdminController(AuthDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var vm = new AdminVm
        {
            Properties = await LoadPropertiesAsync(),
            Message = TempData["AdminMessage"]?.ToString(),
            ErrorMessage = TempData["AdminError"]?.ToString()
        };

        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> CreateProperty([FromForm] CreatePropertyRequestVm request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.AddressLine1))
        {
            TempData["AdminError"] = "Property name and address are required.";
            return RedirectToAction(nameof(Index));
        }

        var connection = _dbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO property (name, address_line1, address_line2, notes, is_active)
            VALUES (@name, @address1, @address2, @notes, @isActive);";

        AddParameter(cmd, "@name", request.Name.Trim());
        AddParameter(cmd, "@address1", request.AddressLine1.Trim());
        AddParameter(cmd, "@address2", request.AddressLine2.Trim());
        AddParameter(cmd, "@notes", request.Notes.Trim());
        AddParameter(cmd, "@isActive", request.IsActive);

        var inserted = await cmd.ExecuteNonQueryAsync();
        TempData["AdminMessage"] = inserted > 0 ? "Property created." : "No property created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> OnboardExistingProperty([FromForm] OnboardExistingPropertyRequestVm request)
    {
        if (string.IsNullOrWhiteSpace(request.PropertyName) || string.IsNullOrWhiteSpace(request.PropertyAddressLine1) || string.IsNullOrWhiteSpace(request.TenantFullName))
        {
            TempData["AdminError"] = "Property and tenant details are required.";
            return RedirectToAction(nameof(Index));
        }

        if (request.InitialRent <= 0)
        {
            TempData["AdminError"] = "Initial rent must be greater than zero.";
            return RedirectToAction(nameof(Index));
        }

        var connection = _dbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);
        await using var tx = await connection.BeginTransactionAsync();

        try
        {
            var propertyId = await InsertPropertyAsync(connection, tx, request.PropertyName, request.PropertyAddressLine1, request.PropertyAddressLine2, request.PropertyNotes, true);
            var tenantId = await InsertTenantAsync(connection, tx, request.TenantFullName, request.TenantEmail, request.TenantPhone, request.OpeningOutstanding, 0m, "Onboarded as existing property takeover");
            var leaseId = await InsertLeaseAsync(connection, tx, propertyId, tenantId, request.LeaseStartDate, "Onboarded from admin");
            var rentRateId = await InsertRentRateAsync(connection, tx, leaseId, request.LeaseStartDate, request.InitialRent, "Initial rent on takeover");
            await UpsertStatementSdtAsync(connection, tx, leaseId, request.LeaseStartDate, "Rent", "Rent for statement month", request.InitialRent, "rent_rate", rentRateId);
            if (request.DepositRequired > 0)
            {
                await UpsertStatementSdtAsync(connection, tx, leaseId, request.LeaseStartDate, "Deposit", "Deposit required", request.DepositRequired, "tenant_deposit", tenantId);
            }

            await tx.CommitAsync();
            TempData["AdminMessage"] = "Existing property takeover created with tenant, lease, opening saldo, deposit required and rent.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            TempData["AdminError"] = $"Could not onboard existing property: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> AddTenantToProperty([FromForm] AddTenantToPropertyRequestVm request)
    {
        if (request.PropertyId <= 0 || string.IsNullOrWhiteSpace(request.TenantFullName))
        {
            TempData["AdminError"] = "Please select a property and capture tenant name.";
            return RedirectToAction(nameof(Index));
        }

        if (request.InitialRent <= 0)
        {
            TempData["AdminError"] = "Initial rent must be greater than zero.";
            return RedirectToAction(nameof(Index));
        }

        var connection = _dbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);
        await using var tx = await connection.BeginTransactionAsync();

        try
        {
            await CloseActiveLeaseAsync(connection, tx, request.PropertyId, request.LeaseStartDate);
            var tenantId = await InsertTenantAsync(connection, tx, request.TenantFullName, request.TenantEmail, request.TenantPhone, request.OpeningOutstanding, 0m, "Added as new tenant to existing property");
            var leaseId = await InsertLeaseAsync(connection, tx, request.PropertyId, tenantId, request.LeaseStartDate, "Tenant change from admin");
            var rentRateId = await InsertRentRateAsync(connection, tx, leaseId, request.LeaseStartDate, request.InitialRent, "Initial rent for new tenant");
            await UpsertStatementSdtAsync(connection, tx, leaseId, request.LeaseStartDate, "Rent", "Rent for statement month", request.InitialRent, "rent_rate", rentRateId);
            if (request.DepositRequired > 0)
            {
                await UpsertStatementSdtAsync(connection, tx, leaseId, request.LeaseStartDate, "Deposit", "Deposit required", request.DepositRequired, "tenant_deposit", tenantId);
            }

            await tx.CommitAsync();
            TempData["AdminMessage"] = "New tenant added to property with opening saldo, deposit required, and rent captured.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            TempData["AdminError"] = $"Could not add tenant to property: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<List<PropertyOptionVm>> LoadPropertiesAsync()
    {
        var connection = _dbContext.Database.GetDbConnection();
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

    private static async Task<int> InsertPropertyAsync(DbConnection connection, DbTransaction tx, string name, string address1, string address2, string notes, bool isActive)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO property (name, address_line1, address_line2, notes, is_active)
            VALUES (@name, @address1, @address2, @notes, @isActive)
            RETURNING id;";

        AddParameter(cmd, "@name", name.Trim());
        AddParameter(cmd, "@address1", address1.Trim());
        AddParameter(cmd, "@address2", address2.Trim());
        AddParameter(cmd, "@notes", notes.Trim());
        AddParameter(cmd, "@isActive", isActive);

        var id = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(id);
    }

    private static async Task<int> InsertTenantAsync(DbConnection connection, DbTransaction tx, string fullName, string email, string phone, decimal openingOutstanding, decimal depositHeld, string notes)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO tenant (full_name, email, phone, notes, is_active, current_amount_outstanding, deposit_held)
            VALUES (@fullName, @email, @phone, @notes, TRUE, @openingOutstanding, @depositHeld)
            RETURNING id;";

        AddParameter(cmd, "@fullName", fullName.Trim());
        AddParameter(cmd, "@email", email.Trim());
        AddParameter(cmd, "@phone", phone.Trim());
        AddParameter(cmd, "@notes", notes);
        AddParameter(cmd, "@openingOutstanding", openingOutstanding);
        AddParameter(cmd, "@depositHeld", depositHeld);

        var id = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(id);
    }

    private static async Task<int> InsertLeaseAsync(DbConnection connection, DbTransaction tx, int propertyId, int tenantId, DateTime leaseStartDate, string notes)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO lease (property_id, tenant_id, start_date, end_date, notes)
            VALUES (@propertyId, @tenantId, @startDate, NULL, @notes)
            RETURNING id;";

        AddParameter(cmd, "@propertyId", propertyId);
        AddParameter(cmd, "@tenantId", tenantId);
        AddParameter(cmd, "@startDate", leaseStartDate.Date);
        AddParameter(cmd, "@notes", notes);

        var id = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(id);
    }

    private static async Task<int> InsertRentRateAsync(DbConnection connection, DbTransaction tx, int leaseId, DateTime effectiveFrom, decimal amount, string notes)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO rent_rate (lease_id, effective_from, amount, notes)
            VALUES (@leaseId, @effectiveFrom, @amount, @notes)
            RETURNING id;";

        AddParameter(cmd, "@leaseId", leaseId);
        AddParameter(cmd, "@effectiveFrom", new DateTime(effectiveFrom.Year, effectiveFrom.Month, 1));
        AddParameter(cmd, "@amount", amount);
        AddParameter(cmd, "@notes", notes);

        var id = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(id);
    }

    private static async Task CloseActiveLeaseAsync(DbConnection connection, DbTransaction tx, int propertyId, DateTime newLeaseStartDate)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            UPDATE lease
            SET end_date = @endDate
            WHERE property_id = @propertyId
              AND end_date IS NULL;";

        AddParameter(cmd, "@propertyId", propertyId);
        AddParameter(cmd, "@endDate", newLeaseStartDate.Date.AddDays(-1));

        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task UpsertStatementSdtAsync(DbConnection connection, DbTransaction tx, int leaseId, DateTime entryDate, string entryType, string description, decimal amount, string sourceTable, long sourceId)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
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
