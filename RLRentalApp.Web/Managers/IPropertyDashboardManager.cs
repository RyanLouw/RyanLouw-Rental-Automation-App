using RLRentalApp.Models;

namespace RLRentalApp.Web.Managers;

public interface IPropertyDashboardManager
{
    Task<HomeIndexVm> GetDashboardAsync();
    Task<PropertyStatusVm?> GetPropertyStatusAsync(int propertyId);
    Task<PropertyStatementVm?> GetPropertyStatementAsync(int propertyId);
}
