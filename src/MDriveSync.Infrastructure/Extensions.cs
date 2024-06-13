﻿using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace MDriveSync.Infrastructure
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
        /// 移除指路径的后缀
        /// </summary>
        /// <param name="path"></param>
        /// <param name="removeSuffix"></param>
        /// <returns></returns>
        public static string TrimSuffix(this string path, string removeSuffix = "")
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            if (!string.IsNullOrWhiteSpace(removeSuffix))
            {
                if (path.EndsWith(removeSuffix))
                {
                    path = path.Substring(0, path.Length - removeSuffix.Length);
                }
            }

            return path;
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

        /// <summary>
        /// 计算数据流的哈希值并返回十六进制字符串
        /// </summary>
        /// <param name="hash"></param>
        /// <returns></returns>
        public static string ToHex(this byte[] hash)
        {
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }

        /// <summary>
        /// 格式化文件大小
        /// </summary>
        /// <param name="size"></param>
        /// <returns></returns>
        public static string ToFileSizeString(this double size)
        {
            //size switch
            //{
            //    var s when s >= 1024 * 1024 * 1024 => $"{s / 1024 / 1024 / 1024:F2} GB/s",
            //    var s when s >= 1024 * 1024 => $"{s / 1024 / 1024:F2} MB/s",
            //    var s when s >= 1024 => $"{s / 1024:F2} KB/s",
            //    var s => $"{s:F2} B/s"
            //};

            if (size >= 1024 * 1024 * 1024)
            {
                return $"{size / 1024 / 1024 / 1024:F2} GB";
            }
            else if (size >= 1024 * 1024)
            {
                return $"{size / 1024 / 1024:F2} MB";
            }
            else if (size >= 1024)
            {
                return $"{size / 1024:F2} KB";
            }
            else
            {
                return $"{size:F2} B";
            }
        }
    }
}