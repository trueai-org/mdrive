using System.Runtime.CompilerServices;

namespace MDriveSync.Core.IO
{
    /// <summary>
    /// 包含内部路径帮助器，这些帮助器在多个项目之间共享。
    /// 关于路径处理的实用方法，例如检查驱动器字符的有效性、判断路径是否部分合格（即相对于当前驱动器或工作目录），以及判断一个字符是否是目录分隔符
    /// </summary>
    internal static class PathInternalWindows
    {
        // 所有在Win32中的路径最终都会变成对Windows对象管理器中File对象的路径。传入的路径通过对象树中的DosDevice符号链接映射到\Devices下的实际File对象。
        // 举个例子，这是一个典型的路径“Foo”作为任何Win32 API的文件名所发生的事情：
        // 1. “Foo”被识别为相对路径，并附加到当前目录（例如，我们的例子中的"C:\"）
        // 2. “C:\Foo”前面加上了DosDevice命名空间“\??\”
        // 3. CreateFile尝试创建请求文件“\??\C:\Foo”的对象句柄
        // 4. 对象管理器识别DosDevices前缀，然后查找
        //    a. 首先在当前会话的DosDevices中查找（例如，映射的网络驱动器会在这里）
        //    b. 如果在会话中找不到，它会在全局DosDevices中查找（“\GLOBAL??\”）
        // 5. 在DosDevices中找到了“C:”（在我们的案例中是“\GLOBAL??\C:”，它是指向“\Device\HarddiskVolume6”的符号链接）
        // 6. 完整路径现在是“\Device\HarddiskVolume6\Foo”，“\Device\HarddiskVolume6”是一个文件对象，解析工作转交给文件的注册解析方法
        // 7. 调用文件对象的注册打开方法来创建文件句柄，然后返回该句柄。
        //
        // 有多种方式可以直接指定DosDevices路径。最终格式“\??\”是其中一种方式。也可以指定为“\\.\”（最常见的文档方式）和“\\?\”。如果使用问号语法，则路径将跳过规范化（基本上是GetFullPathName()）和路径长度检查。

        // Windows Kernel-Mode Object Manager链接
        // https://msdn.microsoft.com/en-us/library/windows/hardware/ff565763.aspx
        // https://channel9.msdn.com/Shows/Going+Deep/Windows-NT-Object-Manager
        //
        // MS-DOS设备名介绍链接
        // https://msdn.microsoft.com/en-us/library/windows/hardware/ff548088.aspx
        //
        // 本地和全局MS-DOS设备名链接
        // https://msdn.microsoft.com/en-us/library/windows/hardware/ff554302.aspx

        // 扩展设备路径前缀
        internal const string ExtendedDevicePathPrefix = @"\\?\";
        // UNC路径前缀
        internal const string UncPathPrefix = @"\\";
        // 插入的UNC设备前缀
        internal const string UncDevicePrefixToInsert = @"?\UNC\";
        // 扩展的UNC路径前缀
        internal const string UncExtendedPathPrefix = @"\\?\UNC\";
        // 设备路径前缀
        internal const string DevicePathPrefix = @"\\.\";

        // 最大短路径长度
        internal const int MaxShortPath = 260;

        // \\?\, \\.\, \??\ 前缀长度
        internal const int DevicePrefixLength = 4;

        /// <summary>
        /// 如果给定字符是有效的驱动器字母，则返回true。
        /// </summary>
        internal static bool IsValidDriveChar(char value)
        {
            return ((value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z'));
        }

        /// <summary>
        /// 如果指定的路径相对于当前驱动器或工作目录，则返回true。
        /// 如果路径固定到特定的驱动器或UNC路径，则返回false。此方法不对路径进行验证（因此，URI会被认为是相对的）。
        /// </summary>
        /// <remarks>
        /// 处理使用备用目录分隔符的路径。常见的错误是假设根路径（Path.IsPathRooted）不是相对的。但这不是情况。
        /// "C:a" 是相对于C:的当前目录的驱动器相对路径（根，但相对）。
        /// "C:\a" 是根的，不是相对的（当前目录不会用来修改路径）。
        /// </remarks>
        internal static bool IsPartiallyQualified(string path)
        {
            if (path.Length < 2)
            {
                // 它不是固定的，它必须是相对的。用一个字符（或更少）无法指定固定路径。
                return true;
            }

            if (IsDirectorySeparator(path[0]))
            {
                // 无效方式指定相对路径的两个初始斜杠或 \?，因为 ? 对驱动器相对路径无效，而 \??\ 等同于 \\?\
                return !(path[1] == '?' || IsDirectorySeparator(path[1]));
            }

            // 唯一指定不以两个斜杠开始的固定路径的方式是驱动器、冒号、斜杠格式，即 C:\
            return !((path.Length >= 3)
                && (path[1] == Path.VolumeSeparatorChar)
                && IsDirectorySeparator(path[2])
                && IsValidDriveChar(path[0])); // 为匹配旧行为，我们也会检查驱动器字符的有效性，因为如果没有有效的驱动器，路径技术上不合格。"=:\\" 是 "=" 文件的默认数据流。
        }

        /// <summary>
        /// 如果给定字符是目录分隔符，则返回true。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsDirectorySeparator(char c)
        {
            return c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
        }
    }
}
