using System.Text;

namespace MDriveSync.Test
{
    /// <summary>
    /// 测试基类
    /// </summary>
    public class BaseTests
    {
        public BaseTests()
        {
            // 避免中文输出乱码问题
            Console.OutputEncoding = Encoding.UTF8;
        }

        /// <summary>
        /// 避免中文输出乱码问题 - 用于方法
        /// </summary>
        public virtual void SetOutputUTF8()
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
    }
}
