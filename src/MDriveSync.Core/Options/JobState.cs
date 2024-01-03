using System.ComponentModel;

namespace MDriveSync.Core
{
    /// <summary>
    /// 代表作业的不同状态。
    /// </summary>
    public enum JobState
    {
        /// <summary>
        /// 默认状态/表示已创建作业 -> 初始化 -> 启动 -> 空闲 -> 检查 -> 执行相关业务 -> ...
        /// </summary>
        [Description("默认")]
        None = 0,

        /// <summary>
        /// 初始化中。
        /// 表示作业正在进行初始化操作。
        /// </summary>
        [Description("初始化中")]
        Initializing = 1,

        /// <summary>
        /// 空闲状态。
        /// 表示作业当前没有执行任何操作。
        /// </summary>
        [Description("空闲")]
        Idle = 5,

        /// <summary>
        /// 启动中。
        /// 表示作业正在启动。
        /// </summary>
        [Description("启动")]
        Starting = 6,

        /// <summary>
        /// 扫描中。
        /// 表示作业正在进行扫描操作。
        /// </summary>
        [Description("扫描")]
        Scanning = 7,

        /// <summary>
        /// 备份中/同步中。
        /// 表示作业正在进行数据备份操作。
        /// </summary>
        [Description("同步")]
        BackingUp = 8,

        /// <summary>
        /// 还原中。
        /// 表示作业正在执行数据还原操作。
        /// </summary>
        [Description("还原")]
        Restoring = 9,

        /// <summary>
        /// 校验中。
        /// 表示作业正在进行数据校验操作。
        /// </summary>
        [Description("校验")]
        Verifying = 10,

        /// <summary>
        /// 队列中。
        /// 表示作业正在等待队列中。
        /// 表示上个作业还在执行中，正在队列中排队。
        /// </summary>
        [Description("队列")]
        Queued = 11,

        /// <summary>
        /// 完成。
        /// 表示作业已完成。
        /// 表示：执行的一次性作业已经完成，后续不再继续执行。
        /// </summary>
        [Description("完成")]
        Completed = 15,

        /// <summary>
        /// 暂停。
        /// 表示作业已被暂停。
        /// </summary>
        [Description("暂停")]
        Paused = 16,

        /// <summary>
        /// 错误。
        /// 表示作业遇到错误。
        /// </summary>
        [Description("错误")]
        Error = 17,

        /// <summary>
        /// 取消中。
        /// 表示作业正在取消操作。
        /// </summary>
        [Description("取消中")]
        Cancelling = 18,

        /// <summary>
        /// 已取消。
        /// 表示作业已经取消。
        /// 未完成或作业中取消。
        /// </summary>
        [Description("取消")]
        Cancelled = 19,

        /// <summary>
        /// 禁用。
        /// 表示作业已被禁用。
        /// </summary>
        [Description("禁用")]
        Disabled = 100,

        /// <summary>
        /// 删除。
        /// 表示作业已被删除。
        /// </summary>
        [Description("删除")]
        Deleted = 101,
    }
}