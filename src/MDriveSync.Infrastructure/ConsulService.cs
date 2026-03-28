using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Consul;
using Microsoft.Extensions.Options;
using Serilog;

namespace MDriveSync.Infrastructure
{
    public class ConsulService : IDisposable
    {
        private readonly ConsulClient _consulClient;
        private readonly ConsulOptions _consulOptions;
        private readonly Serilog.ILogger _logger;
        private readonly string _uniqueInstanceId;

        private string _serviceId;

        public ConsulService(IOptionsMonitor<ConsulOptions> options)
        {
            _logger = Log.Logger;
            _consulOptions = options.CurrentValue;

            if (_consulOptions?.Enable == true)
            {
                var consulClientConfiguration = new ConsulClientConfiguration
                {
                    Address = new Uri(_consulOptions.ConsulUrl)
                };
                _consulClient = new ConsulClient(consulClientConfiguration);
            }

            // 生成唯一实例ID（基于机器名+进程ID+时间戳）
            _uniqueInstanceId = $"{Environment.MachineName}-{Environment.ProcessId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        }

        public async Task RegisterServiceAsync()
        {
            try
            {
                var localIp = await PrivateNetworkHelper.GetAliyunPrivateIpAsync();
                if (string.IsNullOrWhiteSpace(localIp))
                {
                    localIp = PrivateNetworkHelper.GetPrimaryPrivateIP();
                }

                _logger.Information($"尝试注册服务到 Consul: {_consulOptions.ServiceName} at {localIp}:{_consulOptions.ServicePort}");

                // 先清理可能存在的同IP同端口的旧实例
                await CleanupOldInstancesAsync(localIp, _consulOptions.ServicePort);

                // 生成新的服务ID
                _serviceId = $"{_consulOptions.ServiceName}-{localIp.Replace(".", "-")}-{_consulOptions.ServicePort}-{_uniqueInstanceId}";

                var tags = new List<string>() {
                    // 实例元数据
                    $"instance.id={_serviceId}",
                    $"instance.unique={_uniqueInstanceId}",
                    $"instance.started={DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}",
                    "version=1.0.0",
                    "api",
                    "docker-proxy",
                    "v1"
                };
                //tags.AddRange(_consulOptions.ServiceTags ?? Array.Empty<string>());

                var registration = new AgentServiceRegistration
                {
                    ID = _serviceId,
                    Name = _consulOptions.ServiceName, // 服务名保持一致用于负载均衡
                    Address = localIp,
                    Port = _consulOptions.ServicePort,
                    Tags = tags.Distinct().ToArray(),
                    Check = new AgentServiceCheck
                    {
                        HTTP = $"http://{localIp}:{_consulOptions.ServicePort}{_consulOptions.HealthCheckUrl}",
                        Interval = _consulOptions.HealthCheckInterval,
                        Timeout = _consulOptions.HealthCheckTimeout,
                        DeregisterCriticalServiceAfter = _consulOptions.DeregisterCriticalServiceAfter
                    }
                };

                await _consulClient.Agent.ServiceRegister(registration);
                _logger.Information($"服务已注册到 Consul: {_serviceId} at {localIp}:{_consulOptions.ServicePort}");
                _logger.Information($"实例唯一标识: {_uniqueInstanceId}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "注册服务到 Consul 失败");
            }
        }

        private async Task CleanupOldInstancesAsync(string currentIp, int currentPort)
        {
            try
            {
                _logger.Information("检查并清理旧的服务实例...");

                // 获取当前机器上同端口的所有实例
                var services = await _consulClient.Agent.Services();
                var oldInstances = services.Response.Values
                    .Where(s => s.Service == _consulOptions.ServiceName &&
                               s.Address == currentIp &&
                               s.Port == currentPort)
                    .ToList();

                foreach (var oldInstance in oldInstances)
                {
                    try
                    {
                        // 检查进程是否还存在
                        if (IsProcessStillRunning(oldInstance.ID))
                        {
                            _logger.Information($"实例 {oldInstance.ID} 对应的进程仍在运行，跳过清理");
                            continue;
                        }

                        _logger.Information($"清理旧实例: {oldInstance.ID}");
                        await _consulClient.Agent.ServiceDeregister(oldInstance.ID);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, $"清理旧实例失败: {oldInstance.ID}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "清理旧实例时发生错误");
            }
        }

        private bool IsProcessStillRunning(string serviceId)
        {
            try
            {
                // 从服务ID中提取进程ID
                var parts = serviceId.Split('-');
                if (parts.Length >= 2 && int.TryParse(parts[^1], out int processId))
                {
                    var process = System.Diagnostics.Process.GetProcessById(processId);
                    return process != null && !process.HasExited;
                }
            }
            catch
            {
                // 进程不存在或访问被拒绝
            }
            return false;
        }

        public async Task DeregisterServiceAsync()
        {
            try
            {
                if (!string.IsNullOrEmpty(_serviceId))
                {
                    await _consulClient.Agent.ServiceDeregister(_serviceId);
                    _logger.Information($"服务已从 Consul 注销: {_serviceId}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "从 Consul 注销服务失败");
            }
        }

        /// <summary>
        /// 获取所有已注册健康的服务实例
        /// </summary>
        /// <returns></returns>
        public async Task<List<string>> GetAllRegisteredServicesAsync()
        {
            try
            {
                if (_consulOptions == null || !_consulOptions.Enable)
                {
                    return [];
                }

                // 从 Consul 获取服务信息
                var servicesResult = await _consulClient.Agent.Services();

                // 获取健康检查状态
                var healthChecks = await _consulClient.Health.State(HealthStatus.Any);
                var checksDict = healthChecks.Response.ToDictionary(c => c.ServiceID);

                // 过滤出目标服务，并且必须是健康的服务
                var midjourneyServices = servicesResult.Response.Values
                    .Where(s => s.Service == _consulOptions.ServiceName)
                    .Where(s => !checksDict.TryGetValue(s.ID, out var check) || check.Status == HealthStatus.Passing)
                    .ToList();

                return midjourneyServices.Select(c => c.Address).Distinct().ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "获取已注册服务列表失败");
            }
            return [];
        }

        private string GetLocalIPAddress()
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
                socket.Connect("223.5.5.5", 65530);
                var endPoint = socket.LocalEndPoint as IPEndPoint;
                var primaryIP = endPoint?.Address.ToString();
                if (!string.IsNullOrEmpty(primaryIP) && primaryIP != "127.0.0.1")
                {
                    return primaryIP;
                }
            }
            catch { }

            try
            {
                // 方法2：获取第一个非回环的网络接口 IP
                var host = Dns.GetHostEntry(Dns.GetHostName());
                var ip = host.AddressList
                    .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork &&
                                       !IPAddress.IsLoopback(ip));

                if (ip != null)
                {
                    return ip.ToString();
                }
            }
            catch { }

            // 方法3：通过网络接口获取
            try
            {
                var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var networkInterface in networkInterfaces)
                {
                    if (networkInterface.OperationalStatus == OperationalStatus.Up &&
                        networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        var props = networkInterface.GetIPProperties();
                        var ip = props.UnicastAddresses
                            .FirstOrDefault(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                                                  !IPAddress.IsLoopback(addr.Address))
                            ?.Address.ToString();

                        if (!string.IsNullOrEmpty(ip))
                        {
                            return ip;
                        }
                    }
                }
            }
            catch { }

            return "127.0.0.1";
        }

        public void Dispose()
        {
            _consulClient?.Dispose();
        }
    }
}