namespace MDriveSync.Infrastructure
{
    public class ConsulOptions
    {
        /// <summary>
        /// 是否启用 Consul 服务注册
        /// </summary>
        public bool Enable { get; set; } = false;

        public string ConsulUrl { get; set; } = "http://localhost:8500";

        public string ConsulToken { get; set; } = "";

        public string ServiceName { get; set; } = "docker-proxy";

        public int ServicePort { get; set; } = 8080;

        /// <summary>
        /// 是否有效
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public bool IsValid => Enable && !string.IsNullOrEmpty(ConsulUrl) && !string.IsNullOrEmpty(ServiceName);

        public string HealthCheckUrl { get; set; } = "/health";

        public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(10);

        public TimeSpan HealthCheckTimeout { get; set; } = TimeSpan.FromSeconds(5);

        public TimeSpan DeregisterCriticalServiceAfter { get; set; } = TimeSpan.FromMinutes(1);
    }
}
