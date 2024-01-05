namespace MDriveSync.Core.Dashboard
{
    public interface IDashboardAuthorizationFilter
    {
        bool Authorize(DashboardContext context);
    }
}