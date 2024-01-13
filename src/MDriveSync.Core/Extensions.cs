using MDriveSync.Core.Services;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace MDriveSync.Core
{
    public static partial class Extensions
    {
        private static readonly char[] PathSeparator = ['/'];

        /// <summary>
        /// 移除路径首尾 ' ', '/', '\'
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static string TrimPath(this string path)
        {
            return path?.Trim().Trim('/').Trim('\\').Trim('/').Trim();
        }

        /// <summary>
        /// 转为 url 路径
        /// 例如：由 E:\_backups\p00\3e4 -> _backups/p00/3e4
        /// </summary>
        /// <param name="path"></param>
        /// <param name="removePrefix">移除的前缀</param>
        /// <returns></returns>
        public static string ToUrlPath(this string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            // 替换所有的反斜杠为斜杠
            // 分割路径，移除空字符串，然后重新连接
            return string.Join("/", path.Replace("\\", "/").Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries)).TrimPath();
        }

        /// <summary>
        /// 将完整路径分解为子路径列表
        /// 例如：/a/b/c -> [a, b, c]
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static string[] ToSubPaths(this string path)
        {
            return path?.ToUrlPath().Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        }

        /// <summary>
        /// 转为 url 路径
        /// 例如：由 E:\_backups\p00\3e4 -> _backups/p00/3e4
        /// </summary>
        /// <param name="path"></param>
        /// <param name="removePrefix">移除的前缀</param>
        /// <returns></returns>
        public static string TrimPrefix(this string path, string removePrefix = "")
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            if (!string.IsNullOrWhiteSpace(removePrefix))
            {
                if (path.StartsWith(removePrefix))
                {
                    path = path.Substring(removePrefix.Length);
                }
            }

            // 替换所有的反斜杠为斜杠
            // 分割路径，移除空字符串，然后重新连接
            return string.Join("/", path.Replace("\\", "/").Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries)).TrimPath();
        }

        /// <summary>
        /// 获取枚举描述或名称
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static string GetDescription(this Enum value)
        {
            if (value == null)
            {
                return null;
            }
            var type = value.GetType();
            var displayName = Enum.GetName(type, value);
            var fieldInfo = type.GetField(displayName);
            var attributes = (DisplayAttribute[])fieldInfo?.GetCustomAttributes(typeof(DisplayAttribute), false);
            if (attributes?.Length > 0)
            {
                displayName = attributes[0].Description ?? attributes[0].Name;
            }
            else
            {
                var desAttributes = (DescriptionAttribute[])fieldInfo?.GetCustomAttributes(typeof(DescriptionAttribute), false);
                if (desAttributes?.Length > 0)
                    displayName = desAttributes[0].Description;
            }
            return displayName;
        }

        /// <returns>returns the new position of the file pointer</returns>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="Win32Exception"></exception>
        public static long SetFilePointer(this SafeFileHandle fileHandle, long offset, System.IO.SeekOrigin seekOrigin = System.IO.SeekOrigin.Begin)
        {
            if (fileHandle.IsInvalid) throw new InvalidOperationException("can't set pointer on an invalid handle");
            var wasSet = NativeMethods.SetFilePointerEx(fileHandle, offset, out var newposition, seekOrigin);
            if (!wasSet)
            {
                throw new Win32Exception();
            }
            return newposition;
        }

        public static void ReadFile(this SafeFileHandle fileHandle, IntPtr buffer, uint bytesToRead, out int bytesRead)
        {
            if (fileHandle.IsInvalid) throw new InvalidOperationException("can't set pointer on an invalid handle");
            if (!NativeMethods.ReadFile(fileHandle, buffer, bytesToRead, out bytesRead, IntPtr.Zero))
            {
                throw new Win32Exception();
            }
        }

        public static void WriteFile(this SafeFileHandle fileHandle, IntPtr buffer, uint bytesToWrite, out int bytesWritten)
        {
            if (fileHandle.IsInvalid) throw new InvalidOperationException("can't set pointer on an invalid handle");
            if (!NativeMethods.WriteFile(fileHandle, buffer, bytesToWrite, out bytesWritten, IntPtr.Zero))
            {
                throw new Win32Exception();
            }
        }
    }
}