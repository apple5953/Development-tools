using System;

namespace BEAI.QueryHost.Identity
{
    public interface IIdentityContext
    {
        string UserId { get; }
        string TenantId { get; }
        string DeviceId { get; }
        string SessionId { get; }
        bool IsAuthenticated { get; }
        string Mode { get; }
    }

    public class DevelopmentIdentityContext : IIdentityContext
    {
        private static readonly Lazy<DevelopmentIdentityContext> _lazy = new Lazy<DevelopmentIdentityContext>(delegate { return new DevelopmentIdentityContext(); });
        public static DevelopmentIdentityContext Instance { get { return _lazy.Value; } }

        public DevelopmentIdentityContext()
        {
            UserId = "local-developer";
            TenantId = "development";
            DeviceId = DeviceIdentityManager.Instance.DeviceId;
            SessionId = Guid.NewGuid().ToString("N");
            IsAuthenticated = true;
            Mode = "Development";
        }

        public string UserId { get; private set; }
        public string TenantId { get; private set; }
        public string DeviceId { get; private set; }
        public string SessionId { get; private set; }
        public bool IsAuthenticated { get; private set; }
        public string Mode { get; private set; }
    }
}
