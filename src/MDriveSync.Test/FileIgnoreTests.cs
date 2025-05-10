using MDriveSync.Core.Services;

namespace MDriveSync.Test
{
    /// <summary>
    /// 文件忽略规则测试类
    /// </summary>
    public class FileIgnoreTests : BaseTests
    {
        private readonly string _testRootPath;

        public FileIgnoreTests()
        {
            // 创建测试根目录
            _testRootPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(_testRootPath);
        }

        [Fact]
        public void TestBasicWildcards()
        {
            // 测试基本通配符
            var ignorePatterns = FileIgnoreHelper.BuildIgnorePatterns(
                "*.txt",       // 匹配所有txt文件
                "file?.log",   // 匹配单个字符
                "**/logs"      // 匹配任意多层路径
            );

            var ignoreRules = new FileIgnoreRuleSet(_testRootPath, ignorePatterns);

            // 测试 *.txt
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "file.txt")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "another.txt")));
            Assert.False(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "file.log")));
            Assert.False(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "subfolder", "file.log")));

            // 测试 file?.log
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "file1.log")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "fileA.log")));
            Assert.False(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "file.log")));
            Assert.False(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "file12.log")));

            // 测试 **/logs
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "logs")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "app", "logs")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "app", "sub", "logs")));
            Assert.False(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "logs.txt")));
        }

        [Fact]
        public void TestPathRules()
        {
            // 测试路径规则
            var ignorePatterns = FileIgnoreHelper.BuildIgnorePatterns(
                "/bin",     // 只匹配根目录下的bin
                "temp/",    // 匹配目录
                "lib"       // 匹配任何位置的lib
            );

            var ignoreRules = new FileIgnoreRuleSet(_testRootPath, ignorePatterns);

            // 测试 /bin
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "bin")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "bin", "file.exe")));
            Assert.False(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "app", "bin")));
            Assert.False(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "app", "bin", "file.exe")));

            // 测试 temp/
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "temp")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "temp", "file.txt")));
            Assert.False(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "temp.txt")));
            Assert.False(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "temporary")));

            // 测试 lib
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "lib")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "app", "lib")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "lib", "file.dll")));
        }

        [Fact]
        public void TestSpecialMatching()
        {
            // 测试特殊匹配规则
            var ignorePatterns = FileIgnoreHelper.BuildIgnorePatterns(
                "*.txt",                    // 忽略所有txt文件
                "!important.txt",           // 但不忽略important.txt
                "file[123].log",           // 忽略file1.log, file2.log, file3.log
                "report[a-c].pdf"          // 忽略reporta.pdf到reportc.pdf
            );

            var ignoreRules = new FileIgnoreRuleSet(_testRootPath, ignorePatterns);

            // 测试否定规则
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "regular.txt")));
            Assert.False(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "important.txt")));

            // 测试字符集合
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "file1.log")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "file2.log")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "file3.log")));
            Assert.False(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "file4.log")));
            Assert.False(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "file.log")));

            // 测试字符范围
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "reporta.pdf")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "reportb.pdf")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "reportc.pdf")));
            Assert.False(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "reportd.pdf")));
            Assert.False(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "report.pdf")));
        }

        [Fact]
        public void TestRulePriority()
        {
            // 测试规则优先级
            var ignorePatterns = FileIgnoreHelper.BuildIgnorePatterns(
                "*.log",           // 忽略所有日志文件
                "!error.log",      // 但不忽略错误日志
                "debug/*.log",     // 忽略debug目录下的所有日志
                "!debug/critical.log" // 但不忽略debug目录下的critical.log
            );

            var ignoreRules = new FileIgnoreRuleSet(_testRootPath, ignorePatterns);

            // 测试基本规则和否定规则的顺序
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "info.log")));
            Assert.False(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "error.log")));

            // 测试更具体的目录规则和最终的否定规则
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "debug", "info.log")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "debug", "warning.log")));
            Assert.False(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "debug", "critical.log")));
        }

        [Fact]
        public void TestCompoundRules()
        {
            // 测试复合规则
            var ignorePatterns = FileIgnoreHelper.BuildIgnorePatterns(
                "**/node_modules/**",   // 忽略所有node_modules及其内容
                "**/.git/**",           // 忽略所有.git目录及其内容
                "**/bin/**/Debug/**",   // 忽略所有bin目录下的Debug子目录
                "!**/bin/**/Debug/important.config"  // 但不忽略特定配置文件
            );

            var ignoreRules = new FileIgnoreRuleSet(_testRootPath, ignorePatterns);

            // 测试node_modules忽略
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "node_modules")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "node_modules", "package.json")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "project", "node_modules", "lib", "index.js")));

            // 测试.git忽略
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, ".git")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, ".git", "index")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "project", ".git", "objects")));

            // 测试Debug目录忽略
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "bin", "Debug")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "bin", "Debug", "app.exe")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "project", "bin", "x64", "Debug", "lib.dll")));

            // 测试重要文件不忽略
            Assert.False(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "bin", "Debug", "important.config")));
            Assert.False(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "project", "bin", "x64", "Debug", "important.config")));
        }

        [Fact]
        public void TestDefaultIgnorePatterns()
        {
            // 测试默认忽略模式
            var ignorePatterns = FileIgnoreHelper.BuildIgnorePatterns();
            var ignoreRules = new FileIgnoreRuleSet(_testRootPath, ignorePatterns);

            // 测试一些常见的应该被忽略的文件和目录
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, ".git")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "node_modules")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "bin", "Debug")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "temp")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "file.tmp")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "file.bak")));
        }

        [Fact]
        public void TestCustomIgnorePatternsForLargeSystem()
        {
            // 测试为大型文件系统优化的忽略规则
            var ignorePatterns = FileIgnoreHelper.BuildIgnorePatterns(
                "*.bak",
                "*.tmp",
                "**/temp/**",
                "**/cache/**"
            );

            var ignoreRules = new FileIgnoreRuleSet(_testRootPath, ignorePatterns);

            // 测试应该被忽略的文件和目录
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "data.bak")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "config.tmp")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "temp")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "temp", "file.txt")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "project", "temp", "data.bin")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "cache")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "cache", "data.cache")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "project", "cache", "user.cache")));

            // 测试不应被忽略的文件和目录
            Assert.False(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "data.txt")));
            Assert.False(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "temporary.txt"))); // 不匹配temp目录模式
            Assert.False(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "project", "data", "important.dat")));
        }

        [Fact]
        public void TestCaseInsensitiveMatching()
        {
            // 测试大小写不敏感匹配
            var ignorePatterns = FileIgnoreHelper.BuildIgnorePatterns(
                "*.TXT",
                "Temp/",
                "**/LOG/**"
            );

            var ignoreRules = new FileIgnoreRuleSet(_testRootPath, ignorePatterns);

            // 测试不同大小写的文件和目录
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "file.txt")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "FILE.TXT")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "File.Txt")));

            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "temp")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "TEMP")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "Temp")));

            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "log")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "LOG")));
            Assert.True(ignoreRules.ShouldIgnore(Path.Combine(_testRootPath, "project", "Log", "system.log")));
        }

        public override void Dispose()
        {
            // 清理临时测试目录
            if (Directory.Exists(_testRootPath))
            {
                try
                {
                    Directory.Delete(_testRootPath, true);
                }
                catch
                {
                    // 忽略清理错误
                }
            }

            base.Dispose();
        }
    }
}