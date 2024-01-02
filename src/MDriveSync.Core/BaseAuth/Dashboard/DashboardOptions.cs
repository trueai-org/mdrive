using MDriveSync.Core.BaseAuth;

namespace MDriveSync.Core.Dashboard
{
    public class DashboardOptions
    {
        public DashboardOptions()
        {
            Authorization = new[] { new LocalRequestsOnlyAuthorizationFilter() };
        }

        public IEnumerable<IDashboardAuthorizationFilter> Authorization { get; set; }

        public bool IgnoreAntiforgeryToken { get; set; }
    }
}
