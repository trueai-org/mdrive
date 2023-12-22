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
        None = 0,

        /// <summary>
        /// 初始化中。
        /// 表示作业正在进行初始化操作。
        /// </summary>
        Initializing = 1,

        /// <summary>
        /// 空闲状态。
        /// 表示作业当前没有执行任何操作。
        /// </summary>
        Idle = 5,

        /// <summary>
        /// 启动中。
        /// 表示作业正在启动。
        /// </summary>
        Starting = 6,

        /// <summary>
        /// 扫描中。
        /// 表示作业正在进行扫描操作。
        /// </summary>
        Scanning = 7,

        /// <summary>
        /// 备份中。
        /// 表示作业正在进行数据备份操作。
        /// </summary>
        BackingUp = 8,

        /// <summary>
        /// 还原中。
        /// 表示作业正在执行数据还原操作。
        /// </summary>
        Restoring = 9,

        /// <summary>
        /// 校验中。
        /// 表示作业正在进行数据校验操作。
        /// </summary>
        Verifying = 10,

        /// <summary>
        /// 队列中。
        /// 表示作业正在等待队列中。
        /// </summary>
        Queued = 11,

        /// <summary>
        /// 完成。
        /// 表示作业已完成。
        /// </summary>
        Completed = 15,

        /// <summary>
        /// 暂停。
        /// 表示作业已被暂停。
        /// </summary>
        Paused = 16,

        /// <summary>
        /// 错误。
        /// 表示作业遇到错误。
        /// </summary>
        Error = 17,

        /// <summary>
        /// 取消中。
        /// 表示作业正在取消操作。
        /// </summary>
        Cancelling = 18,

        /// <summary>
        /// 已取消。
        /// 表示作业已经取消。
        /// </summary>
        Cancelled = 19,

        /// <summary>
        /// 禁用。
        /// 表示作业已被禁用。
        /// </summary>
        Disabled = 100,
    }
}