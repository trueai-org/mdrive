namespace MDriveSync.Core.IO
{
    /// <summary>
    /// 实现一个类似于 ExtJS treenode 的类，以便于 JSON 导出
    /// </summary>
    public class TreeNode
    {
        /// <summary>
        /// 节点显示的文本
        /// </summary>
        public string text { get; set; }

        /// <summary>
        /// 节点的 ID
        /// </summary>
        public string id { get; set; }

        /// <summary>
        /// 应用于节点的类
        /// </summary>
        public string cls { get; set; }

        /// <summary>
        /// 应用于图标的类
        /// </summary>
        public string iconCls { get; set; }

        /// <summary>
        /// 如果元素应该被选中则为 true
        /// </summary>
        public bool check { get; set; }

        /// <summary>
        /// 如果元素是叶节点则为 true
        /// </summary>
        public bool leaf { get; set; }

        /// <summary>
        /// 获取或设置当前路径，如果该项是一个符号路径
        /// </summary>
        public string resolvedpath { get; set; }

        /// <summary>
        /// 如果元素被隐藏则为 true
        /// </summary>
        public bool hidden { get; set; }

        /// <summary>
        /// 如果元素是一个符号链接则为 true
        /// </summary>
        public bool symlink { get; set; }

        /// <summary>
        /// 构造一个新的 TreeNode
        /// </summary>
        public TreeNode()
        {
            this.cls = "folder";
            this.iconCls = "x-tree-icon-parent";
            this.check = false;
        }
    }
}