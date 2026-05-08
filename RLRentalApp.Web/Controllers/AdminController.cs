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
    private const string PropertyTab = "property";
    private const string RenterTab = "renter";
    private const string RentTab = "rent";
    private const string LeavingTab = "leaving";
    private const string StatementTab = "statement";
    private const string ManualRowSourceTable = "manual_statement_entry";

    private readonly AuthDbContext _dbContext;

    public AdminController(AuthDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] AdminStatementFilterVm statementFilter, [FromQuery] string? tab = null)
    {
        var normalizedTab = NormalizeAdminTab(tab);
        var vm = new AdminVm
        {
            Properties = await LoadPropertiesAsync(),
            Leases = await LoadLeaseOptionsAsync(),
            Tenants = await LoadTenantOptionsAsync(),
            ActiveTab = normalizedTab,
            StatementFilter = statementFilter,
            Message = TempData["AdminMessage"]?.ToString(),
            ErrorMessage = TempData["AdminError"]?.ToString()
        };

        if (statementFilter.PropertyId is > 0 || statementFilter.TenantId is > 0)
        {
            vm.ActiveTab = StatementTab;
            vm.Statement = await LoadAdminStatementAsync(statementFilter);
        }

        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> CreateProperty([FromForm] CreatePropertyRequestVm request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.AddressLine1))
        {
            TempData["AdminError"] = "Property name and address are required.";
            return RedirectToAdminTab(PropertyTab);
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
        TempData["AdminMessage"] = inserted > 0 ? "Property created. You can now add a renter to it." : "No property created.";
        return RedirectToAdminTab(PropertyTab);
    }

    [HttpPost]
    public async Task<IActionResult> OnboardExistingProperty([FromForm] OnboardExistingPropertyRequestVm request)
    {
        if (string.IsNullOrWhiteSpace(request.PropertyName) || string.IsNullOrWhiteSpace(request.PropertyAddressLine1) || string.IsNullOrWhiteSpace(request.TenantFullName))
        {
            TempData["AdminError"] = "Property and tenant details are required.";
            return RedirectToAdminTab(RenterTab);
        }

        if (request.LeaseStartDate == default || request.InitialRent <= 0)
        {
            TempData["AdminError"] = "Lease start date and initial rent greater than zero are required.";
            return RedirectToAdminTab(RenterTab);
        }

        var connection = _dbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);
        await using var tx = await connection.BeginTransactionAsync();

        try
        {
            var propertyId = await InsertPropertyAsync(connection, tx, request.PropertyName, request.PropertyAddressLine1, request.PropertyAddressLine2, request.PropertyNotes, true);
            var tenantId = await InsertTenantAsync(connection, tx, request.TenantFullName, request.TenantEmail, request.TenantPhone, request.PaymentReference, request.OpeningOutstanding, 0m, "Onboarded as existing property takeover");
            var leaseId = await InsertLeaseAsync(connection, tx, propertyId, tenantId, request.LeaseStartDate, "Onboarded from admin");
            var rentRateId = await InsertRentRateAsync(connection, tx, leaseId, request.LeaseStartDate, request.InitialRent, "Initial rent on takeover");
            await UpsertStatementSdtAsync(connection, tx, leaseId, request.LeaseStartDate, "Rent", "Rent for statement month", request.InitialRent, "rent_rate", rentRateId);
            if (request.DepositRequired > 0)
            {
                await UpsertStatementSdtAsync(connection, tx, leaseId, request.LeaseStartDate, "Deposit", "Deposit required", request.DepositRequired, "tenant_deposit", tenantId);
            }

            await tx.CommitAsync();
            TempData["AdminMessage"] = "Existing property takeover created with property, renter, lease, opening saldo, deposit required, and rent.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            TempData["AdminError"] = $"Could not onboard existing property: {ex.Message}";
        }

        return RedirectToAdminTab(RenterTab);
    }

    [HttpPost]
    public async Task<IActionResult> AddTenantToProperty([FromForm] AddTenantToPropertyRequestVm request)
    {
        if (request.PropertyId <= 0 || string.IsNullOrWhiteSpace(request.TenantFullName))
        {
            TempData["AdminError"] = "Please select a property and capture tenant name.";
            return RedirectToAdminTab(RenterTab);
        }

        if (request.LeaseStartDate == default || request.InitialRent <= 0)
        {
            TempData["AdminError"] = "Lease start date and initial rent greater than zero are required.";
            return RedirectToAdminTab(RenterTab);
        }

        var connection = _dbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);
        await using var tx = await connection.BeginTransactionAsync();

        try
        {
            await CloseActiveLeaseForPropertyAsync(connection, tx, request.PropertyId, request.LeaseStartDate, "Closed when a new renter was added from admin");
            var tenantId = await InsertTenantAsync(connection, tx, request.TenantFullName, request.TenantEmail, request.TenantPhone, request.PaymentReference, request.OpeningOutstanding, 0m, "Added as new tenant to existing property");
            var leaseId = await InsertLeaseAsync(connection, tx, request.PropertyId, tenantId, request.LeaseStartDate, "Tenant change from admin");
            var rentRateId = await InsertRentRateAsync(connection, tx, leaseId, request.LeaseStartDate, request.InitialRent, "Initial rent for new tenant");
            await UpsertStatementSdtAsync(connection, tx, leaseId, request.LeaseStartDate, "Rent", "Rent for statement month", request.InitialRent, "rent_rate", rentRateId);
            if (request.DepositRequired > 0)
            {
                await UpsertStatementSdtAsync(connection, tx, leaseId, request.LeaseStartDate, "Deposit", "Deposit required", request.DepositRequired, "tenant_deposit", tenantId);
            }

            await tx.CommitAsync();
            TempData["AdminMessage"] = "New renter added to property with opening saldo, deposit required, and rent captured.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            TempData["AdminError"] = $"Could not add renter to property: {ex.Message}";
        }

        return RedirectToAdminTab(RenterTab);
    }

    [HttpPost]
    public async Task<IActionResult> UpdateRent([FromForm] UpdateRentAdminRequestVm request)
    {
        if (request.LeaseId <= 0 || request.EffectiveFrom == default || request.Amount <= 0)
        {
            TempData["AdminError"] = "Select a current renter, rent effective month, and rent amount greater than zero.";
            return RedirectToAdminTab(RentTab);
        }

        var connection = _dbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);
        await using var tx = await connection.BeginTransactionAsync();

        try
        {
            if (!await IsActiveLeaseAsync(connection, tx, request.LeaseId))
            {
                TempData["AdminError"] = "Rent can only be updated for a current renter/active lease.";
                await tx.RollbackAsync();
                return RedirectToAdminTab(RentTab);
            }

            var rentRateId = await InsertRentRateAsync(connection, tx, request.LeaseId, request.EffectiveFrom, request.Amount, string.IsNullOrWhiteSpace(request.Notes) ? "Rent updated from admin" : request.Notes.Trim());
            await UpsertStatementSdtAsync(connection, tx, request.LeaseId, request.EffectiveFrom, "Rent", "Rent for statement month", request.Amount, "rent_rate", rentRateId);
            await tx.CommitAsync();
            TempData["AdminMessage"] = "Rent updated and added to the statement ledger.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            TempData["AdminError"] = $"Could not update rent: {ex.Message}";
        }

        return RedirectToAdminTab(RentTab);
    }

    [HttpPost]
    public async Task<IActionResult> EndLease([FromForm] EndLeaseAdminRequestVm request)
    {
        if (request.LeaseId <= 0 || request.EndDate == default)
        {
            TempData["AdminError"] = "Select the current renter and the date they are leaving.";
            return RedirectToAdminTab(LeavingTab);
        }

        var connection = _dbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);
        await using var tx = await connection.BeginTransactionAsync();

        try
        {
            await CloseLeaseAsync(connection, tx, request.LeaseId, request.EndDate, request.Notes);
            await tx.CommitAsync();
            TempData["AdminMessage"] = "Renter marked as leaving and the active lease was closed.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            TempData["AdminError"] = $"Could not mark renter as leaving: {ex.Message}";
        }

        return RedirectToAdminTab(LeavingTab);
    }

    [HttpPost]
    public async Task<IActionResult> AddManualStatementEntry([FromForm] AddManualStatementEntryRequestVm request)
    {
        if (request.LeaseId <= 0 || request.EntryDate == default || string.IsNullOrWhiteSpace(request.Description))
        {
            TempData["AdminError"] = "Select a current renter, date, and description for the statement row.";
            return RedirectToAdminTab(StatementTab);
        }

        var connection = _dbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);
        await using var tx = await connection.BeginTransactionAsync();

        try
        {
            if (!await IsActiveLeaseAsync(connection, tx, request.LeaseId))
            {
                TempData["AdminError"] = "Manual rows can only be added to the current tenant for a property.";
                await tx.RollbackAsync();
                return RedirectToAdminTab(StatementTab);
            }

            var manualEntryId = await InsertManualStatementEntryAsync(connection, tx, request);
            await UpsertStatementSdtAsync(connection, tx, request.LeaseId, request.EntryDate, request.EntryType, request.Description, request.Amount, ManualRowSourceTable, manualEntryId);
            await tx.CommitAsync();
            TempData["AdminMessage"] = "Manual statement row added.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            TempData["AdminError"] = $"Could not add manual row: {ex.Message}";
        }

        return RedirectToAdminTab(StatementTab);
    }

    [HttpPost]
    public async Task<IActionResult> UpdateStatementEntry([FromForm] UpdateAdminStatementEntryRequestVm request)
    {
        if (request.StatementEntryId <= 0 || request.EntryDate == default || string.IsNullOrWhiteSpace(request.Description))
        {
            TempData["AdminError"] = "Statement row date and description are required.";
            return RedirectToAdminTab(StatementTab);
        }

        var connection = _dbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);
        await using var tx = await connection.BeginTransactionAsync();

        try
        {
            var source = await LoadStatementSourceAsync(connection, tx, request.StatementEntryId);
            if (source is null)
            {
                TempData["AdminError"] = "Statement row was not found.";
                await tx.RollbackAsync();
                return RedirectToAdminTab(StatementTab);
            }

            await UpdateSourceRowAsync(connection, tx, source.Value, request);
            await UpdateStatementLedgerRowAsync(connection, tx, source.Value.LeaseId, request);
            await tx.CommitAsync();
            TempData["AdminMessage"] = "Statement row updated.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            TempData["AdminError"] = $"Could not update statement row: {ex.Message}";
        }

        return RedirectToAdminTab(StatementTab);
    }

    private RedirectToActionResult RedirectToAdminTab(string tab)
    {
        return RedirectToAction(nameof(Index), new { tab });
    }

    private static string NormalizeAdminTab(string? tab)
    {
        return tab switch
        {
            RenterTab => RenterTab,
            RentTab => RentTab,
            LeavingTab => LeavingTab,
            StatementTab => StatementTab,
            _ => PropertyTab
        };
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

    private async Task<List<TenantOptionVm>> LoadTenantOptionsAsync()
    {
        var connection = _dbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, full_name
            FROM tenant
            ORDER BY full_name;";

        var result = new List<TenantOptionVm>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new TenantOptionVm { TenantId = reader.GetInt32(0), TenantName = reader.GetString(1) });
        }

        return result;
    }

    private async Task<List<LeaseOptionVm>> LoadLeaseOptionsAsync()
    {
        var connection = _dbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT l.id, p.id, p.name, t.id, t.full_name, l.start_date, l.end_date
            FROM lease l
            INNER JOIN property p ON p.id = l.property_id
            INNER JOIN tenant t ON t.id = l.tenant_id
            ORDER BY p.name, l.end_date IS NOT NULL, l.start_date DESC, t.full_name;";

        var result = new List<LeaseOptionVm>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new LeaseOptionVm
            {
                LeaseId = reader.GetInt32(0),
                PropertyId = reader.GetInt32(1),
                PropertyName = reader.GetString(2),
                TenantId = reader.GetInt32(3),
                TenantName = reader.GetString(4),
                StartDate = reader.GetDateTime(5),
                EndDate = reader.IsDBNull(6) ? null : reader.GetDateTime(6)
            });
        }

        return result;
    }

    private async Task<AdminStatementVm> LoadAdminStatementAsync(AdminStatementFilterVm filter)
    {
        var connection = _dbContext.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection);

        var where = new List<string>();
        if (filter.PropertyId is > 0) where.Add("s.property_id = @propertyId");
        if (filter.TenantId is > 0) where.Add("s.tenant_id = @tenantId");
        var scopeWhere = where.Count == 0 ? "TRUE" : string.Join(" AND ", where);

        decimal openingBalance = 0m;
        if (filter.FromDate.HasValue)
        {
            await using var openingCmd = connection.CreateCommand();
            openingCmd.CommandText = $@"
                SELECT COALESCE(SUM(s.amount), 0)
                FROM statement_sdt s
                WHERE {scopeWhere}
                  AND s.entry_date < @fromDate;";
            AddStatementFilterParameters(openingCmd, filter);
            AddParameter(openingCmd, "@fromDate", filter.FromDate.Value.Date);
            openingBalance = Convert.ToDecimal(await openingCmd.ExecuteScalarAsync());
        }

        var dateWhere = new List<string>(where);
        if (filter.FromDate.HasValue) dateWhere.Add("s.entry_date >= @fromDate");
        if (filter.ToDate.HasValue) dateWhere.Add("s.entry_date <= @toDate");
        var finalWhere = dateWhere.Count == 0 ? "TRUE" : string.Join(" AND ", dateWhere);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $@"
            SELECT s.id, s.lease_id, p.name, t.full_name, s.entry_date, s.entry_type, s.description, s.amount, l.end_date IS NULL
            FROM statement_sdt s
            INNER JOIN property p ON p.id = s.property_id
            INNER JOIN tenant t ON t.id = s.tenant_id
            INNER JOIN lease l ON l.id = s.lease_id
            WHERE {finalWhere}
            ORDER BY s.entry_date, s.id;";
        AddStatementFilterParameters(cmd, filter);
        if (filter.FromDate.HasValue) AddParameter(cmd, "@fromDate", filter.FromDate.Value.Date);
        if (filter.ToDate.HasValue) AddParameter(cmd, "@toDate", filter.ToDate.Value.Date);

        var entries = new List<AdminStatementEntryVm>();
        var runningBalance = openingBalance;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var amount = reader.GetDecimal(7);
            runningBalance += amount;
            var isCurrentTenant = reader.GetBoolean(8);
            entries.Add(new AdminStatementEntryVm
            {
                StatementEntryId = reader.GetInt64(0),
                LeaseId = reader.GetInt32(1),
                PropertyName = reader.GetString(2),
                TenantName = reader.GetString(3),
                EntryDate = reader.GetDateTime(4),
                EntryType = reader.GetString(5),
                Description = reader.GetString(6),
                Amount = amount,
                RunningBalance = runningBalance,
                IsCurrentTenant = isCurrentTenant,
                CanEdit = isCurrentTenant
            });
        }

        return new AdminStatementVm
        {
            ScopeTitle = await BuildStatementScopeTitleAsync(connection, filter),
            OpeningBalance = openingBalance,
            ClosingBalance = runningBalance,
            Entries = entries
        };
    }

    private static void AddStatementFilterParameters(DbCommand cmd, AdminStatementFilterVm filter)
    {
        if (filter.PropertyId is > 0) AddParameter(cmd, "@propertyId", filter.PropertyId.Value);
        if (filter.TenantId is > 0) AddParameter(cmd, "@tenantId", filter.TenantId.Value);
    }

    private static async Task<string> BuildStatementScopeTitleAsync(DbConnection connection, AdminStatementFilterVm filter)
    {
        if (filter.PropertyId is not > 0 && filter.TenantId is not > 0)
        {
            return "All rental statement rows";
        }

        await using var cmd = connection.CreateCommand();
        if (filter.PropertyId is > 0 && filter.TenantId is > 0)
        {
            cmd.CommandText = @"
                SELECT CONCAT(p.name, ' / ', t.full_name)
                FROM property p
                CROSS JOIN tenant t
                WHERE p.id = @propertyId AND t.id = @tenantId;";
            AddParameter(cmd, "@propertyId", filter.PropertyId.Value);
            AddParameter(cmd, "@tenantId", filter.TenantId.Value);
        }
        else if (filter.PropertyId is > 0)
        {
            cmd.CommandText = "SELECT name FROM property WHERE id = @propertyId;";
            AddParameter(cmd, "@propertyId", filter.PropertyId.Value);
        }
        else
        {
            cmd.CommandText = "SELECT full_name FROM tenant WHERE id = @tenantId;";
            AddParameter(cmd, "@tenantId", filter.TenantId!.Value);
        }

        return Convert.ToString(await cmd.ExecuteScalarAsync()) ?? "Statement";
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

    private static async Task<int> InsertTenantAsync(DbConnection connection, DbTransaction tx, string fullName, string email, string phone, string paymentReference, decimal openingOutstanding, decimal depositHeld, string notes)
    {
        var hasPaymentReferenceColumn = await HasTenantPaymentReferenceColumnAsync(connection, tx);
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = hasPaymentReferenceColumn
            ? @"
                INSERT INTO tenant (full_name, email, phone, payment_reference, notes, is_active, current_amount_outstanding, deposit_held)
                VALUES (@fullName, @email, @phone, @paymentReference, @notes, TRUE, @openingOutstanding, @depositHeld)
                RETURNING id;"
            : @"
                INSERT INTO tenant (full_name, email, phone, notes, is_active, current_amount_outstanding, deposit_held)
                VALUES (@fullName, @email, @phone, @notes, TRUE, @openingOutstanding, @depositHeld)
                RETURNING id;";

        AddParameter(cmd, "@fullName", fullName.Trim());
        AddParameter(cmd, "@email", email.Trim());
        AddParameter(cmd, "@phone", phone.Trim());
        if (hasPaymentReferenceColumn)
        {
            AddParameter(cmd, "@paymentReference", paymentReference.Trim());
        }
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

    private static async Task<long> InsertManualStatementEntryAsync(DbConnection connection, DbTransaction tx, AddManualStatementEntryRequestVm request)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO manual_statement_entry (lease_id, entry_date, entry_type, description, amount, notes)
            VALUES (@leaseId, @entryDate, @entryType, @description, @amount, @notes)
            RETURNING id;";
        AddParameter(cmd, "@leaseId", request.LeaseId);
        AddParameter(cmd, "@entryDate", request.EntryDate.Date);
        AddParameter(cmd, "@entryType", string.IsNullOrWhiteSpace(request.EntryType) ? "Manual" : request.EntryType.Trim());
        AddParameter(cmd, "@description", request.Description.Trim());
        AddParameter(cmd, "@amount", request.Amount);
        AddParameter(cmd, "@notes", request.Notes.Trim());
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task<bool> IsActiveLeaseAsync(DbConnection connection, DbTransaction tx, int leaseId)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM lease WHERE id = @leaseId AND end_date IS NULL);";
        AddParameter(cmd, "@leaseId", leaseId);
        return await cmd.ExecuteScalarAsync() is bool exists && exists;
    }

    private static async Task CloseActiveLeaseForPropertyAsync(DbConnection connection, DbTransaction tx, int propertyId, DateTime newLeaseStartDate, string notes)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            UPDATE lease
            SET end_date = @endDate,
                notes = CONCAT(COALESCE(notes, ''), CASE WHEN COALESCE(notes, '') = '' THEN '' ELSE E'\n' END, @notes)
            WHERE property_id = @propertyId
              AND end_date IS NULL;";

        AddParameter(cmd, "@propertyId", propertyId);
        AddParameter(cmd, "@endDate", newLeaseStartDate.Date.AddDays(-1));
        AddParameter(cmd, "@notes", notes);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task CloseLeaseAsync(DbConnection connection, DbTransaction tx, int leaseId, DateTime endDate, string notes)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            WITH closed AS (
                UPDATE lease
                SET end_date = @endDate,
                    notes = CONCAT(COALESCE(notes, ''), CASE WHEN COALESCE(notes, '') = '' THEN '' ELSE E'\n' END, @notes)
                WHERE id = @leaseId
                  AND end_date IS NULL
                RETURNING tenant_id
            )
            UPDATE tenant t
            SET is_active = FALSE
            WHERE t.id IN (SELECT tenant_id FROM closed)
              AND NOT EXISTS (
                  SELECT 1 FROM lease l
                  WHERE l.tenant_id = t.id
                    AND l.end_date IS NULL
              );";
        AddParameter(cmd, "@leaseId", leaseId);
        AddParameter(cmd, "@endDate", endDate.Date);
        AddParameter(cmd, "@notes", string.IsNullOrWhiteSpace(notes) ? "Renter marked as leaving from admin" : notes.Trim());
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
        AddParameter(cmd, "@entryType", string.IsNullOrWhiteSpace(entryType) ? "Manual" : entryType.Trim());
        AddParameter(cmd, "@description", description.Trim());
        AddParameter(cmd, "@amount", amount);
        AddParameter(cmd, "@sourceTable", sourceTable);
        AddParameter(cmd, "@sourceId", sourceId);

        await cmd.ExecuteNonQueryAsync();
    }

    private readonly record struct StatementSource(int LeaseId, string SourceTable, long SourceId);

    private static async Task<StatementSource?> LoadStatementSourceAsync(DbConnection connection, DbTransaction tx, long statementEntryId)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            SELECT lease_id, source_table, source_id
            FROM statement_sdt
            WHERE id = @statementEntryId;";
        AddParameter(cmd, "@statementEntryId", statementEntryId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new StatementSource(reader.GetInt32(0), reader.GetString(1), reader.GetInt64(2));
    }

    private static async Task UpdateSourceRowAsync(DbConnection connection, DbTransaction tx, StatementSource source, UpdateAdminStatementEntryRequestVm request)
    {
        if (string.Equals(source.SourceTable, ManualRowSourceTable, StringComparison.OrdinalIgnoreCase))
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                UPDATE manual_statement_entry
                SET entry_date = @entryDate,
                    entry_type = @entryType,
                    description = @description,
                    amount = @amount,
                    updated_at = NOW()
                WHERE id = @sourceId
                  AND lease_id = @leaseId;";
            AddCommonStatementUpdateParameters(cmd, source, request, request.Amount);
            await cmd.ExecuteNonQueryAsync();
        }
        else if (string.Equals(source.SourceTable, "rent_rate", StringComparison.OrdinalIgnoreCase))
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                UPDATE rent_rate
                SET effective_from = @entryMonth,
                    amount = @amount,
                    notes = @description
                WHERE id = @sourceId
                  AND lease_id = @leaseId;";
            AddParameter(cmd, "@entryMonth", new DateTime(request.EntryDate.Year, request.EntryDate.Month, 1));
            AddCommonStatementUpdateParameters(cmd, source, request, Math.Abs(request.Amount));
            await cmd.ExecuteNonQueryAsync();
        }
        else if (string.Equals(source.SourceTable, "service_charge", StringComparison.OrdinalIgnoreCase))
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                UPDATE service_charge
                SET billing_period = @entryMonth,
                    amount = @amount,
                    notes = @description
                WHERE id = @sourceId
                  AND lease_id = @leaseId;";
            AddParameter(cmd, "@entryMonth", new DateTime(request.EntryDate.Year, request.EntryDate.Month, 1));
            AddCommonStatementUpdateParameters(cmd, source, request, Math.Abs(request.Amount));
            await cmd.ExecuteNonQueryAsync();
        }
        else if (string.Equals(source.SourceTable, "payment", StringComparison.OrdinalIgnoreCase))
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                UPDATE payment
                SET paid_on = @entryDate,
                    amount = @amount,
                    reference = @description
                WHERE id = @sourceId
                  AND lease_id = @leaseId;";
            AddCommonStatementUpdateParameters(cmd, source, request, Math.Abs(request.Amount));
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static void AddCommonStatementUpdateParameters(DbCommand cmd, StatementSource source, UpdateAdminStatementEntryRequestVm request, decimal sourceAmount)
    {
        AddParameter(cmd, "@entryDate", request.EntryDate.Date);
        AddParameter(cmd, "@entryType", string.IsNullOrWhiteSpace(request.EntryType) ? "Manual" : request.EntryType.Trim());
        AddParameter(cmd, "@description", request.Description.Trim());
        AddParameter(cmd, "@amount", sourceAmount);
        AddParameter(cmd, "@sourceId", source.SourceId);
        AddParameter(cmd, "@leaseId", source.LeaseId);
    }

    private static async Task UpdateStatementLedgerRowAsync(DbConnection connection, DbTransaction tx, int leaseId, UpdateAdminStatementEntryRequestVm request)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            UPDATE statement_sdt
            SET entry_date = @entryDate,
                entry_type = @entryType,
                description = @description,
                amount = @amount,
                updated_at = NOW()
            WHERE id = @statementEntryId
              AND lease_id = @leaseId;";
        AddParameter(cmd, "@statementEntryId", request.StatementEntryId);
        AddParameter(cmd, "@leaseId", leaseId);
        AddParameter(cmd, "@entryDate", request.EntryDate.Date);
        AddParameter(cmd, "@entryType", string.IsNullOrWhiteSpace(request.EntryType) ? "Manual" : request.EntryType.Trim());
        AddParameter(cmd, "@description", request.Description.Trim());
        AddParameter(cmd, "@amount", request.Amount);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<bool> HasTenantPaymentReferenceColumnAsync(DbConnection connection, DbTransaction tx)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = current_schema()
                  AND table_name = 'tenant'
                  AND column_name = 'payment_reference'
            );";

        var value = await cmd.ExecuteScalarAsync();
        return value is bool exists && exists;
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
