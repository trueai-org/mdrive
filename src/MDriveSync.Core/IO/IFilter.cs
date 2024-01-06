namespace MDriveSync.Core.IO
{
    /// <summary>
    /// 表示一个单独的过滤器。
    /// </summary>
    public interface IFilter
    {
        /// <summary>
        /// 排序顺序。
        /// </summary>
        long Order { get; set; }

        /// <summary>
        /// 如果为true，则过滤器包含条目；如果为false，则排除。
        /// </summary>
        bool Include { get; set; }

        /// <summary>
        /// 过滤表达式。
        /// 如果过滤器是一个正则表达式，它以方括号 [ ] 开始和结束。
        /// </summary>
        string Expression { get; set; }
    }
}