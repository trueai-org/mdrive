using System.Runtime.InteropServices;

namespace MDriveSync.Core.Services
{
    /// <summary>
    /// Windows 文件操作 API 包装类
    /// 主要用于实现文件回收站支持等功能
    /// </summary>
    public static class FileOperationAPIWrapper
    {
        // Shell32.dll 中的 SHFileOperation 函数定义
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

        // IFileOperation 接口（Windows Vista+）
        [ComImport]
        [Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOperation
        {
            uint Advise(IntPtr pfops, out uint pdwCookie);

            void Unadvise(uint dwCookie);

            void SetOperationFlags(uint dwOperationFlags);

            void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);

            void SetProgressDialog([MarshalAs(UnmanagedType.Interface)] object popd);

            void SetProperties([MarshalAs(UnmanagedType.Interface)] object pproparray);

            void SetOwnerWindow(uint hwndParent);

            void ApplyPropertiesToItem([MarshalAs(UnmanagedType.Interface)] object psi);

            void ApplyPropertiesToItems([MarshalAs(UnmanagedType.Interface)] object punkItems);

            void RenameItem([MarshalAs(UnmanagedType.Interface)] object psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IntPtr pfopsItem);

            void RenameItems([MarshalAs(UnmanagedType.Interface)] object pUnkItems, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);

            void MoveItem([MarshalAs(UnmanagedType.Interface)] object psiItem, [MarshalAs(UnmanagedType.Interface)] object psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IntPtr pfopsItem);

            void MoveItems([MarshalAs(UnmanagedType.Interface)] object punkItems, [MarshalAs(UnmanagedType.Interface)] object psiDestinationFolder);

            void CopyItem([MarshalAs(UnmanagedType.Interface)] object psiItem, [MarshalAs(UnmanagedType.Interface)] object psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IntPtr pfopsItem);

            void CopyItems([MarshalAs(UnmanagedType.Interface)] object punkItems, [MarshalAs(UnmanagedType.Interface)] object psiDestinationFolder);

            void DeleteItem([MarshalAs(UnmanagedType.Interface)] object psiItem, IntPtr pfopsItem);

            void DeleteItems([MarshalAs(UnmanagedType.Interface)] object punkItems);

            void NewItem([MarshalAs(UnmanagedType.Interface)] object psiDestinationFolder, uint dwFileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string pszName, [MarshalAs(UnmanagedType.LPWStr)] string pszTemplateName, IntPtr pfopsItem);

            void PerformOperations();

            void GetAnyOperationsAborted(out bool pfAnyOperationsAborted);
        }

        [ComImport]
        [Guid("3ad05575-8857-4850-9277-11b85bdb8e09")]
        [ClassInterface(ClassInterfaceType.None)]
        private class FileOperation
        {
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;

            [MarshalAs(UnmanagedType.U4)]
            public int wFunc;

            public string pFrom;
            public string pTo;
            public short fFlags;

            [MarshalAs(UnmanagedType.Bool)]
            public bool fAnyOperationsAborted;

            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        // 常量定义
        private const int FO_DELETE = 0x0003;

        private const int FOF_ALLOWUNDO = 0x0040;          // 允许撤销（使用回收站）
        private const int FOF_NOCONFIRMATION = 0x0010;     // 不显示确认对话框
        private const int FOF_NOERRORUI = 0x0400;          // 不显示错误用户界面
        private const int FOF_SILENT = 0x0004;             // 静默操作，不显示进度对话框

        /// <summary>
        /// 将文件或目录移动到回收站
        /// </summary>
        /// <param name="path">文件或目录路径</param>
        /// <returns>操作是否成功</returns>
        public static bool MoveToRecycleBin(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));

            if (!File.Exists(path) && !Directory.Exists(path))
                return false;

            try
            {
                // 使用 SHFileOperation 实现移动到回收站
                SHFILEOPSTRUCT fileOp = new SHFILEOPSTRUCT
                {
                    wFunc = FO_DELETE,
                    pFrom = path + '\0' + '\0', // 需要双终止符
                    fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT
                };

                int result = SHFileOperation(ref fileOp);
                return result == 0;
            }
            catch (Exception ex)
            {
                // 出现异常时记录日志并返回失败
                System.Diagnostics.Debug.WriteLine($"移动到回收站失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 将多个文件或目录移动到回收站
        /// </summary>
        /// <param name="paths">文件或目录路径数组</param>
        /// <returns>操作是否成功</returns>
        public static bool MoveToRecycleBin(string[] paths)
        {
            if (paths == null || paths.Length == 0)
                throw new ArgumentNullException(nameof(paths));

            try
            {
                // 构建结束符字符串
                string fromStr = string.Join("\0", paths) + "\0\0";

                // 使用 SHFileOperation 实现移动到回收站
                SHFILEOPSTRUCT fileOp = new SHFILEOPSTRUCT
                {
                    wFunc = FO_DELETE,
                    pFrom = fromStr,
                    fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT
                };

                int result = SHFileOperation(ref fileOp);
                return result == 0;
            }
            catch (Exception ex)
            {
                // 出现异常时记录日志并返回失败
                System.Diagnostics.Debug.WriteLine($"批量移动到回收站失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 检查当前系统是否支持回收站操作
        /// </summary>
        /// <returns>是否支持回收站</returns>
        public static bool IsRecycleBinSupported()
        {
            // 目前仅Windows平台支持回收站功能
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        }

        /// <summary>
        /// 安全删除文件（尝试使用回收站，如不支持则直接删除）
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="useRecycleBin">是否使用回收站</param>
        /// <returns>操作是否成功</returns>
        public static bool SafeDeleteFile(string filePath, bool useRecycleBin = true)
        {
            if (!File.Exists(filePath))
                return true; // 文件不存在视为成功

            try
            {
                if (useRecycleBin && IsRecycleBinSupported())
                {
                    return MoveToRecycleBin(filePath);
                }
                else
                {
                    File.Delete(filePath);
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"删除文件失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 安全删除目录（尝试使用回收站，如不支持则递归删除）
        /// </summary>
        /// <param name="directoryPath">目录路径</param>
        /// <param name="useRecycleBin">是否使用回收站</param>
        /// <returns>操作是否成功</returns>
        public static bool SafeDeleteDirectory(string directoryPath, bool useRecycleBin = true)
        {
            if (!Directory.Exists(directoryPath))
                return true; // 目录不存在视为成功

            try
            {
                if (useRecycleBin && IsRecycleBinSupported())
                {
                    return MoveToRecycleBin(directoryPath);
                }
                else
                {
                    Directory.Delete(directoryPath, true);
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"删除目录失败: {ex.Message}");
                return false;
            }
        }
    }
}