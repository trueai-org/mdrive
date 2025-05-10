using System.Text.RegularExpressions;
using static MDriveSync.Core.Services.FileIgnoreHelper;

namespace MDriveSync.Core.Services
{
    /// <summary>
    /// 文件忽略助手（兼容 kopia 忽略规则）
    /// </summary>
    public class FileIgnoreHelper
    {
        /// <summary>
        /// 忽略规则类型
        /// </summary>
        public enum IgnoreRuleType
        {
            Include,    // 包含规则（以!开头的规则）
            Exclude     // 排除规则（标准规则）
        }

        /// <summary>
        /// 忽略模式列表
        /// </summary>
        public static List<string> BuildIgnorePatterns(params string[] patterns)
        {
            var result = new List<string>
            {
                // 添加常见的默认忽略
                "*.tmp",
                "*.temp",
                "*~",
                ".DS_Store",
                "Thumbs.db",
                "$RECYCLE.BIN",
                "System Volume Information"
            };

            // 添加用户自定义规则
            if (patterns != null && patterns.Length > 0)
            {
                result.AddRange(patterns.Where(p => !string.IsNullOrWhiteSpace(p)));
            }

            return result;
        }
    }

    /// <summary>
    /// 忽略规则
    /// </summary>
    public class FileIgnoreRule
    {
        public string Pattern { get; }          // 原始模式
        public IgnoreRuleType Type { get; }     // 规则类型
        public Regex CompiledPattern { get; }   // 编译后的正则表达式
        public bool IsRootOnly { get; }         // 是否只适用于根目录

        public FileIgnoreRule(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                throw new ArgumentException("忽略规则不能为空", nameof(pattern));
            }

            // 检查是否为包含规则（以!开头）
            if (pattern.StartsWith("!"))
            {
                Type = IgnoreRuleType.Include;
                pattern = pattern.Substring(1);
            }
            else
            {
                Type = IgnoreRuleType.Exclude;
            }

            // 检查是否为根目录专用规则
            if (pattern.StartsWith("/"))
            {
                IsRootOnly = true;
                pattern = pattern.Substring(1);
            }
            else
            {
                IsRootOnly = false;
            }

            Pattern = pattern;

            // 将通配符模式转换为正则表达式
            string regexPattern = WildcardToRegex(pattern);
            CompiledPattern = new Regex(regexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// 将通配符转换为正则表达式 - 修复版
        /// </summary>
        private string WildcardToRegex(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return "^$";

            // 逐字符处理通配符模式，避免复杂的转义问题
            string result = ""; // 移除 ^

            for (int i = 0; i < pattern.Length; i++)
            {
                char c = pattern[i];

                if (c == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
                {
                    // 处理 ** 模式（匹配任意路径）
                    result += ".*?";
                    i++; // 跳过下一个 *
                }
                else if (c == '*')
                {
                    // 处理 * 模式（匹配单层中的任意字符）
                    result += "[^/\\\\]*";
                }
                else if (c == '?')
                {
                    // 处理 ? 模式（匹配单个字符）
                    result += "[^/\\\\]";
                }
                else if (c == '[' || c == ']')
                {
                    // 方括号原样保留
                    result += c;
                }
                else
                {
                    // 其他字符需要转义
                    result += Regex.Escape(c.ToString());
                }
            }

            return result + "$";
        }

        /// <summary>
        /// 检查路径是否匹配当前规则
        /// </summary>
        public bool IsMatch(string path, string rootPath)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            // 标准化路径分隔符
            path = path.Replace('\\', '/');

            // 如果是根目录规则，确保路径相对于根目录
            if (IsRootOnly && !string.IsNullOrEmpty(rootPath))
            {
                rootPath = rootPath.Replace('\\', '/').TrimEnd('/') + "/";

                // 获取相对于根目录的路径
                if (path.StartsWith(rootPath))
                {
                    path = path.Substring(rootPath.Length).TrimStart('/');
                }
                else
                {
                    return false; // 不在根目录下，不适用此规则
                }
            }

            // 执行正则表达式匹配
            return CompiledPattern.IsMatch(path);
        }
    }

    /// <summary>
    /// 忽略规则集合
    /// </summary>
    public class FileIgnoreRuleSet
    {
        private readonly List<FileIgnoreRule> _rules = new List<FileIgnoreRule>();
        private readonly string _rootPath;

        public FileIgnoreRuleSet(string rootPath, IEnumerable<string> patterns)
        {
            if (string.IsNullOrEmpty(rootPath))
                throw new ArgumentException("根路径不能为空", nameof(rootPath));

            _rootPath = rootPath.Replace('\\', '/').TrimEnd('/') + "/";

            patterns ??= BuildIgnorePatterns();

            if (patterns != null)
            {
                foreach (var pattern in patterns.Where(p => !string.IsNullOrWhiteSpace(p)))
                {
                    AddRule(pattern);
                }
            }
        }

        public void AddRule(string pattern)
        {
            if (!string.IsNullOrWhiteSpace(pattern))
            {
                _rules.Add(new FileIgnoreRule(pattern));
            }
        }

        /// <summary>
        /// 检查路径是否应被忽略
        /// </summary>
        public bool ShouldIgnore(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            // 默认不忽略
            bool shouldExclude = false;

            // 规则按顺序执行，后面的规则可以覆盖前面的规则
            foreach (var rule in _rules)
            {
                if (rule.IsMatch(path, _rootPath))
                {
                    shouldExclude = (rule.Type == IgnoreRuleType.Exclude);
                }
            }

            return shouldExclude;
        }
    }
}