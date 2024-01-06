namespace MDriveSync.Core.IO
{
    /// <summary>
    /// 文件接口的主要实现类
    /// </summary>
    public class FileEntry : IFileEntry
    {
        private string m_name;
        private DateTime m_lastAccess;
        private DateTime m_lastModification;
        private long m_size;
        private bool m_isFolder;

        /// <summary>
        /// 获取或设置文件或文件夹的名称
        /// </summary>
        public string Name
        {
            get { return m_name; }
            set { m_name = value; }
        }

        /// <summary>
        /// 获取或设置文件或文件夹最后访问的时间
        /// </summary>
        public DateTime LastAccess
        {
            get { return m_lastAccess; }
            set { m_lastAccess = value; }
        }

        /// <summary>
        /// 获取或设置文件或文件夹最后修改的时间
        /// </summary>
        public DateTime LastModification
        {
            get { return m_lastModification; }
            set { m_lastModification = value; }
        }

        /// <summary>
        /// 获取或设置文件或文件夹的大小
        /// </summary>
        public long Size
        {
            get { return m_size; }
            set { m_size = value; }
        }

        /// <summary>
        /// 获取或设置一个值，该值指示条目是否为文件夹
        /// </summary>
        public bool IsFolder
        {
            get { return m_isFolder; }
            set { m_isFolder = value; }
        }

        /// <summary>
        /// 辅助函数，用于将实例初始化为默认值
        /// </summary>
        private FileEntry()
        {
            m_name = null;
            m_lastAccess = new DateTime();
            m_lastModification = new DateTime();
            m_size = -1;
            m_isFolder = false;
        }

        /// <summary>
        /// 仅使用名称构造条目。
        /// 默认假定该条目为文件。
        /// </summary>
        /// <param name="filename">文件的名称</param>
        public FileEntry(string filename)
            : this()
        {
            m_name = filename;
        }

        /// <summary>
        /// 使用名称和大小构造条目。
        /// 默认假定该条目为文件。
        /// </summary>
        /// <param name="filename">文件的名称</param>
        /// <param name="size">文件的大小</param>
        public FileEntry(string filename, long size)
            : this(filename)
        {
            m_size = size;
        }

        /// <summary>
        /// 提供所有信息构造条目
        /// </summary>
        /// <param name="filename">文件或文件夹的名称</param>
        /// <param name="size">文件或文件夹的大小</param>
        /// <param name="lastAccess">文件或文件夹最后访问的时间</param>
        /// <param name="lastModified">文件或文件夹最后修改的时间</param>
        public FileEntry(string filename, long size, DateTime lastAccess, DateTime lastModified)
            : this(filename, size)
        {
            m_lastModification = lastModified;
            m_lastAccess = lastAccess;
        }
    }
}