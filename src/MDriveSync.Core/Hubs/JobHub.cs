using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MDriveSync.Core.Hubs
{
    /// <summary>
    /// 作业状态实时推送 Hub
    /// 前端通过 SignalR 连接此 Hub，接收作业状态变化通知
    /// 替代前端每秒轮询 /api/drive/jobs 和 /api/local/jobs
    /// </summary>
    public class JobHub : Hub
    {
        // Hub 本身可以是空的
        // 推送由后端通过 IHubContext<JobHub> 主动发起
        // 如果需要，也可以定义客户端可调用的方法
    }
}
