namespace MDriveSync.Core.IO
{
    /// <summary>
    /// 代表一个单一的设置项。
    /// </summary>
    public interface ISetting
    {
        /// <summary>
        /// 过滤表达式。
        /// </summary>
        string Filter { get; }

        /// <summary>
        /// 设置选项的名称。
        /// </summary>
        string Name { get; set; }

        /// <summary>
        /// 设置的值。
        /// </summary>
        string Value { get; set; }
    }
}
