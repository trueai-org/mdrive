using MDriveSync.Core.Services;
using MDriveSync.Security.Models;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Xunit.Abstractions;

namespace MDriveSync.Test
{
    /// <summary>
    /// FileSyncHelper 单元测试
    /// </summary>
    public class FileSyncHelperTests : BaseTests
    {
        private readonly ITestOutputHelper _output;
        private readonly string _testSourceDir;
        private readonly string _testTargetDir;

        public FileSyncHelperTests(ITestOutputHelper output)
        {
            _output = output;

            // 创建测试目录
            _testSourceDir = Path.Combine(Path.GetTempPath(), $"MDriveSync_Test_Source_{Guid.NewGuid()}");
            _testTargetDir = Path.Combine(Path.GetTempPath(), $"MDriveSync_Test_Target_{Guid.NewGuid()}");

            Directory.CreateDirectory(_testSourceDir);
            Directory.CreateDirectory(_testTargetDir);
        }

        public override void Dispose()
        {
            // 清理测试目录
            try
            {
                if (Directory.Exists(_testSourceDir))
                    Directory.Delete(_testSourceDir, true);

                if (Directory.Exists(_testTargetDir))
                    Directory.Delete(_testTargetDir, true);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"清理测试目录时出错: {ex.Message}");
            }

            base.Dispose();
        }

        #region 辅助方法

        /// <summary>
        /// 创建测试文件结构
        /// </summary>
        private void CreateTestFileStructure(string rootDir, Dictionary<string, byte[]> files, string[] directories)
        {
            // 创建目录
            foreach (var dir in directories)
            {
                string fullPath = Path.Combine(rootDir, dir);
                Directory.CreateDirectory(fullPath);
            }

            // 创建文件
            foreach (var file in files)
            {
                string fullPath = Path.Combine(rootDir, file.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                File.WriteAllBytes(fullPath, file.Value);
            }
        }

        /// <summary>
        /// 生成随机数据
        /// </summary>
        private byte[] GenerateRandomData(int size, int seed = 0)
        {
            var random = seed == 0 ? new Random() : new Random(seed);
            var data = new byte[size];
            random.NextBytes(data);
            return data;
        }

        /// <summary>
        /// 创建文件修改时间差异
        /// </summary>
        private void SetFileLastWriteTime(string path, DateTime dateTime)
        {
            File.SetLastWriteTime(path, dateTime);
        }

        /// <summary>
        /// 验证目录结构匹配
        /// </summary>
        private void VerifyDirectoryMatch(string sourceDir, string targetDir, bool expectMatch = true)
        {
            var sourceFiles = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories)
                .Select(f => f.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar))
                .OrderBy(f => f)
                .ToList();

            var targetFiles = Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories)
                .Select(f => f.Substring(targetDir.Length).TrimStart(Path.DirectorySeparatorChar))
                .OrderBy(f => f)
                .ToList();

            if (expectMatch)
            {
                Assert.Equal(sourceFiles.Count, targetFiles.Count);

                for (int i = 0; i < sourceFiles.Count; i++)
                {
                    Assert.Equal(sourceFiles[i], targetFiles[i]);

                    // 比较文件内容
                    var sourceContent = File.ReadAllBytes(Path.Combine(sourceDir, sourceFiles[i]));
                    var targetContent = File.ReadAllBytes(Path.Combine(targetDir, targetFiles[i]));
                    Assert.Equal(sourceContent, targetContent);
                }
            }
            else if (sourceFiles.Count == targetFiles.Count)
            {
                // 应当有不同，检查文件内容是否至少有一个不同
                bool hasDifference = false;

                for (int i = 0; i < sourceFiles.Count; i++)
                {
                    if (sourceFiles[i] != targetFiles[i])
                    {
                        hasDifference = true;
                        break;
                    }

                    var sourceContent = File.ReadAllBytes(Path.Combine(sourceDir, sourceFiles[i]));
                    var targetContent = File.ReadAllBytes(Path.Combine(targetDir, targetFiles[i]));

                    if (!sourceContent.SequenceEqual(targetContent))
                    {
                        hasDifference = true;
                        break;
                    }
                }

                Assert.True(hasDifference, "期望源目录和目标目录有差异，但实际上它们完全匹配");
            }
        }

        /// <summary>
        /// 创建进度报告器
        /// </summary>
        private IProgress<SyncProgress> CreateProgressReporter()
        {
            return new Progress<SyncProgress>(progress =>
            {
                _output.WriteLine($"进度: {progress.ProgressPercentage}%, 消息: {progress.Message}");
                _output.WriteLine($"处理: {progress.ProcessedItems}/{progress.TotalItems}, 速度: {progress.FormattedSpeed}");

                if (progress.Statistics != null)
                {
                    _output.WriteLine($"复制: {progress.Statistics.FilesCopied}/{progress.Statistics.FilesToCopy}, " +
                                     $"更新: {progress.Statistics.FilesUpdated}/{progress.Statistics.FilesToUpdate}, " +
                                     $"删除: {progress.Statistics.FilesDeleted}/{progress.Statistics.FilesToDelete}");
                }
            });
        }

        #endregion

        #region 单元测试

        [Fact]
        public async Task OneWaySync_EmptyTarget_ShouldCopyAllFiles()
        {
            // 准备测试数据
            var sourceFiles = new Dictionary<string, byte[]>
            {
                ["file1.txt"] = Encoding.UTF8.GetBytes("This is file 1"),
                ["file2.txt"] = Encoding.UTF8.GetBytes("This is file 2"),
                ["subdir/file3.txt"] = Encoding.UTF8.GetBytes("This is file 3"),
            };

            var sourceDirs = new[] { "subdir", "emptydir" };

            CreateTestFileStructure(_testSourceDir, sourceFiles, sourceDirs);

            // 配置同步选项
            var options = new SyncOptions
            {
                SourcePath = _testSourceDir,
                TargetPath = _testTargetDir,
                SyncMode = SyncMode.OneWay,
                CompareMethod = CompareMethod.Content,
                MaxParallelOperations = 2
            };

            // 执行同步
            var syncHelper = new FileSyncHelper(options, CreateProgressReporter());
            var result = await syncHelper.SyncAsync();

            // 验证结果
            Assert.Equal(SyncStatus.Completed, result.Status);
            Assert.True(result.IsSuccessful);
            Assert.Equal(3, result.Statistics.FilesCopied); // 应该复制了3个文件
            Assert.True(result.Statistics.DirectoriesCreated >= 2); // 应该创建了至少2个目录

            // 验证目录结构匹配
            VerifyDirectoryMatch(_testSourceDir, _testTargetDir);
        }

        [Fact]
        public async Task OneWaySync_ExistingTarget_ShouldUpdateChangedFiles()
        {
            // 准备源目录测试数据
            var sourceFiles = new Dictionary<string, byte[]>
            {
                ["file1.txt"] = Encoding.UTF8.GetBytes("This is file 1 - updated"),
                ["file2.txt"] = Encoding.UTF8.GetBytes("This is file 2"),
                ["subdir/file3.txt"] = Encoding.UTF8.GetBytes("This is file 3"),
                ["newfile.txt"] = Encoding.UTF8.GetBytes("This is a new file"),
            };

            var sourceDirs = new[] { "subdir", "newdir" };

            // 准备目标目录测试数据（部分内容不同）
            var targetFiles = new Dictionary<string, byte[]>
            {
                ["file1.txt"] = Encoding.UTF8.GetBytes("This is file 1"),
                ["file2.txt"] = Encoding.UTF8.GetBytes("This is file 2"),
                ["subdir/file3.txt"] = Encoding.UTF8.GetBytes("This is file 3"),
                ["oldfile.txt"] = Encoding.UTF8.GetBytes("This is an old file"),
            };

            var targetDirs = new[] { "subdir", "olddir" };

            CreateTestFileStructure(_testSourceDir, sourceFiles, sourceDirs);
            CreateTestFileStructure(_testTargetDir, targetFiles, targetDirs);

            // 配置同步选项
            var options = new SyncOptions
            {
                SourcePath = _testSourceDir,
                TargetPath = _testTargetDir,
                SyncMode = SyncMode.OneWay,
                CompareMethod = CompareMethod.Content,
                MaxParallelOperations = 2
            };

            // 执行同步
            var syncHelper = new FileSyncHelper(options, CreateProgressReporter());
            var result = await syncHelper.SyncAsync();

            // 验证结果
            Assert.Equal(SyncStatus.Completed, result.Status);
            Assert.True(result.IsSuccessful);
            Assert.Equal(1, result.Statistics.FilesUpdated); // file1.txt 应该被更新
            Assert.Equal(1, result.Statistics.FilesCopied);  // newfile.txt 应该被复制
            Assert.True(result.Statistics.DirectoriesCreated >= 1); // newdir 应该被创建

            // 验证 oldfile.txt 和 olddir 仍然存在（单向同步不删除目标中存在但源中不存在的文件）
            Assert.True(File.Exists(Path.Combine(_testTargetDir, "oldfile.txt")));
            Assert.True(Directory.Exists(Path.Combine(_testTargetDir, "olddir")));

            // 验证 file1.txt 已更新
            var file1Content = File.ReadAllText(Path.Combine(_testTargetDir, "file1.txt"));
            Assert.Equal("This is file 1 - updated", file1Content);

            // 验证 newfile.txt 已复制
            var newfileContent = File.ReadAllText(Path.Combine(_testTargetDir, "newfile.txt"));
            Assert.Equal("This is a new file", newfileContent);
        }

        [Fact]
        public async Task MirrorSync_ShouldDeleteFilesNotInSource()
        {
            // 准备源目录测试数据
            var sourceFiles = new Dictionary<string, byte[]>
            {
                ["file1.txt"] = Encoding.UTF8.GetBytes("This is file 1"),
                ["file2.txt"] = Encoding.UTF8.GetBytes("This is file 2"),
                ["subdir/file3.txt"] = Encoding.UTF8.GetBytes("This is file 3"),
            };

            var sourceDirs = new[] { "subdir" };

            // 准备目标目录测试数据（包含额外文件）
            var targetFiles = new Dictionary<string, byte[]>
            {
                ["file1.txt"] = Encoding.UTF8.GetBytes("This is file 1"),
                ["file2.txt"] = Encoding.UTF8.GetBytes("This is file 2"),
                ["subdir/file3.txt"] = Encoding.UTF8.GetBytes("This is file 3"),
                ["oldfile.txt"] = Encoding.UTF8.GetBytes("This is an old file"),
                ["olddir/oldsubfile.txt"] = Encoding.UTF8.GetBytes("This is an old sub file"),
            };

            var targetDirs = new[] { "subdir", "olddir" };

            CreateTestFileStructure(_testSourceDir, sourceFiles, sourceDirs);
            CreateTestFileStructure(_testTargetDir, targetFiles, targetDirs);

            // 配置同步选项
            var options = new SyncOptions
            {
                SourcePath = _testSourceDir,
                TargetPath = _testTargetDir,
                SyncMode = SyncMode.Mirror,
                CompareMethod = CompareMethod.Content,
                MaxParallelOperations = 2,
                // 使用普通删除，不使用回收站（方便测试）
                UseRecycleBin = false
            };

            // 执行同步
            var syncHelper = new FileSyncHelper(options, CreateProgressReporter());
            var result = await syncHelper.SyncAsync();

            // 验证结果
            Assert.Equal(SyncStatus.Completed, result.Status);
            Assert.True(result.IsSuccessful);
            Assert.Equal(0, result.Statistics.FilesUpdated); // 没有文件需要更新
            Assert.Equal(0, result.Statistics.FilesCopied);  // 没有文件需要复制
            Assert.Equal(2, result.Statistics.FilesDeleted); // 2个文件应该被删除
            Assert.Equal(1, result.Statistics.DirectoriesDeleted); // 1个目录应该被删除

            // 验证 oldfile.txt 和 olddir 不再存在
            Assert.False(File.Exists(Path.Combine(_testTargetDir, "oldfile.txt")));
            Assert.False(Directory.Exists(Path.Combine(_testTargetDir, "olddir")));

            // 验证目录结构匹配
            VerifyDirectoryMatch(_testSourceDir, _testTargetDir);
        }

        [Fact]
        public async Task TwoWaySync_WithChangesOnBothSides_ShouldResolveConflicts()
        {
            // 准备共同的初始数据
            var commonFiles = new Dictionary<string, byte[]>
            {
                ["common.txt"] = Encoding.UTF8.GetBytes("This is a common file"),
                ["unchanged.txt"] = Encoding.UTF8.GetBytes("This file will not change"),
                ["subdir/file.txt"] = Encoding.UTF8.GetBytes("This is a subdirectory file"),
            };

            var commonDirs = new[] { "subdir" };

            // 创建初始的相同结构
            CreateTestFileStructure(_testSourceDir, commonFiles, commonDirs);
            CreateTestFileStructure(_testTargetDir, commonFiles, commonDirs);

            // 在源上做一些更改
            File.WriteAllText(Path.Combine(_testSourceDir, "common.txt"), "This is updated in source");
            File.WriteAllText(Path.Combine(_testSourceDir, "source_only.txt"), "This file only exists in source");
            Directory.CreateDirectory(Path.Combine(_testSourceDir, "source_dir"));

            // 在目标上做一些更改
            File.WriteAllText(Path.Combine(_testTargetDir, "common.txt"), "This is updated in target");
            File.WriteAllText(Path.Combine(_testTargetDir, "target_only.txt"), "This file only exists in target");
            Directory.CreateDirectory(Path.Combine(_testTargetDir, "target_dir"));

            // 设置时间戳，使源文件比目标文件更新
            var sourceTime = DateTime.Now;
            var targetTime = sourceTime.AddMinutes(-30);

            SetFileLastWriteTime(Path.Combine(_testSourceDir, "common.txt"), sourceTime);
            SetFileLastWriteTime(Path.Combine(_testTargetDir, "common.txt"), targetTime);

            // 配置同步选项，使用"较新"策略解决冲突
            var options = new SyncOptions
            {
                SourcePath = _testSourceDir,
                TargetPath = _testTargetDir,
                SyncMode = SyncMode.TwoWay,
                CompareMethod = CompareMethod.DateTimeAndSize,
                ConflictResolution = ConflictResolution.Newer,
                MaxParallelOperations = 2
            };

            // 执行同步
            var syncHelper = new FileSyncHelper(options, CreateProgressReporter());
            var result = await syncHelper.SyncAsync();

            // 验证结果
            Assert.Equal(SyncStatus.Completed, result.Status);
            Assert.True(result.IsSuccessful);

            // 验证冲突解决：common.txt 应该使用源版本（较新）
            var commonContent = File.ReadAllText(Path.Combine(_testTargetDir, "common.txt"));
            Assert.Equal("This is updated in source", commonContent);

            // 验证单向文件已复制：source_only.txt 应该在目标中存在
            Assert.True(File.Exists(Path.Combine(_testTargetDir, "source_only.txt")));

            // 验证另一方向的文件也已复制：target_only.txt 应该在源中存在
            Assert.True(File.Exists(Path.Combine(_testSourceDir, "target_only.txt")));

            // 验证目录也已同步
            Assert.True(Directory.Exists(Path.Combine(_testTargetDir, "source_dir")));
            Assert.True(Directory.Exists(Path.Combine(_testSourceDir, "target_dir")));
        }

        [Fact]
        public async Task ConflictResolution_KeepBoth_ShouldRenameTarget()
        {
            // 准备共同的初始数据
            var commonFiles = new Dictionary<string, byte[]>
            {
                ["conflict.txt"] = Encoding.UTF8.GetBytes("Original content"),
            };

            // 创建初始的相同结构
            CreateTestFileStructure(_testSourceDir, commonFiles, new string[0]);
            CreateTestFileStructure(_testTargetDir, commonFiles, new string[0]);

            // 在源和目标上修改同一个文件
            File.WriteAllText(Path.Combine(_testSourceDir, "conflict.txt"), "Source modified content");
            File.WriteAllText(Path.Combine(_testTargetDir, "conflict.txt"), "Target modified content");

            // 配置同步选项，使用"保留两者"策略解决冲突
            var options = new SyncOptions
            {
                SourcePath = _testSourceDir,
                TargetPath = _testTargetDir,
                SyncMode = SyncMode.TwoWay,
                CompareMethod = CompareMethod.Content,
                ConflictResolution = ConflictResolution.KeepBoth,
                MaxParallelOperations = 1
            };

            // 执行同步
            var syncHelper = new FileSyncHelper(options, CreateProgressReporter());
            var result = await syncHelper.SyncAsync();

            // 验证结果
            Assert.Equal(SyncStatus.Completed, result.Status);
            Assert.True(result.IsSuccessful);

            // 验证保留了两个版本
            var targetContent = File.ReadAllText(Path.Combine(_testTargetDir, "conflict.txt"));
            Assert.Equal("Source modified content", targetContent); // 目标文件应该是源文件的内容

            // 目标中应该有一个带时间戳的重命名文件
            var targetFiles = Directory.GetFiles(_testTargetDir);
            Assert.Equal(2, targetFiles.Length);

            var renamedFile = targetFiles.First(f => !f.EndsWith("conflict.txt"));
            Assert.Contains("conflict", Path.GetFileName(renamedFile)); // 重命名文件应包含原名

            var renamedContent = File.ReadAllText(renamedFile);
            Assert.Equal("Target modified content", renamedContent); // 重命名文件应包含目标原始内容
        }

        [Fact]
        public async Task HashComparison_ShouldDetectContentChanges()
        {
            // 准备源目录测试数据
            var sourceFiles = new Dictionary<string, byte[]>
            {
                ["smallfile.bin"] = GenerateRandomData(1024, 1),
                ["largefile.bin"] = GenerateRandomData(1024 * 1024, 2), // 1MB
            };

            // 准备目标目录测试数据（文件大小相同但内容略有不同）
            var targetFiles = new Dictionary<string, byte[]>
            {
                ["smallfile.bin"] = GenerateRandomData(1024, 1), // 完全相同
                ["largefile.bin"] = GenerateRandomData(1024 * 1024, 3), // 不同种子，内容不同
            };

            CreateTestFileStructure(_testSourceDir, sourceFiles, new string[0]);
            CreateTestFileStructure(_testTargetDir, targetFiles, new string[0]);

            // 确保修改时间相同，这样只会通过哈希检测变化
            var sameTime = DateTime.Now;
            SetFileLastWriteTime(Path.Combine(_testSourceDir, "smallfile.bin"), sameTime);
            SetFileLastWriteTime(Path.Combine(_testTargetDir, "smallfile.bin"), sameTime);
            SetFileLastWriteTime(Path.Combine(_testSourceDir, "largefile.bin"), sameTime);
            SetFileLastWriteTime(Path.Combine(_testTargetDir, "largefile.bin"), sameTime);

            // 配置同步选项，使用哈希比较
            var options = new SyncOptions
            {
                SourcePath = _testSourceDir,
                TargetPath = _testTargetDir,
                SyncMode = SyncMode.OneWay,
                CompareMethod = CompareMethod.Hash,
                HashAlgorithm = EHashType.SHA256,
                SamplingRate = 0.1, // 10% 抽样率
                MaxParallelOperations = 1
            };

            // 执行同步
            var syncHelper = new FileSyncHelper(options, CreateProgressReporter());
            var result = await syncHelper.SyncAsync();

            // 验证结果
            Assert.Equal(SyncStatus.Completed, result.Status);
            Assert.True(result.IsSuccessful);
            Assert.Equal(1, result.Statistics.FilesUpdated); // 只有 largefile.bin 应该被更新
            Assert.Equal(0, result.Statistics.FilesCopied);

            // 验证文件内容已更新
            using (var md5 = MD5.Create())
            {
                var sourceHash = md5.ComputeHash(File.ReadAllBytes(Path.Combine(_testSourceDir, "largefile.bin")));
                var targetHash = md5.ComputeHash(File.ReadAllBytes(Path.Combine(_testTargetDir, "largefile.bin")));
                Assert.Equal(sourceHash, targetHash); // 哈希应该相同
            }
        }

        [Fact]
        public async Task PreviewMode_ShouldNotMakeChanges()
        {
            // 准备源目录测试数据
            var sourceFiles = new Dictionary<string, byte[]>
            {
                ["file1.txt"] = Encoding.UTF8.GetBytes("Source file 1"),
                ["file2.txt"] = Encoding.UTF8.GetBytes("Source file 2"),
            };

            // 准备目标目录测试数据（不同内容）
            var targetFiles = new Dictionary<string, byte[]>
            {
                ["file1.txt"] = Encoding.UTF8.GetBytes("Target file 1"),
                // file2.txt 不存在于目标
            };

            CreateTestFileStructure(_testSourceDir, sourceFiles, new string[0]);
            CreateTestFileStructure(_testTargetDir, targetFiles, new string[0]);

            // 记录目标目录的原始状态
            var originalTargetContent = File.ReadAllText(Path.Combine(_testTargetDir, "file1.txt"));
            var originalTargetFileCount = Directory.GetFiles(_testTargetDir).Length;

            // 配置同步选项，启用预览模式
            var options = new SyncOptions
            {
                SourcePath = _testSourceDir,
                TargetPath = _testTargetDir,
                SyncMode = SyncMode.OneWay,
                CompareMethod = CompareMethod.Content,
                PreviewOnly = true,
                MaxParallelOperations = 1
            };

            // 执行同步
            var syncHelper = new FileSyncHelper(options, CreateProgressReporter());
            var result = await syncHelper.SyncAsync();

            // 验证结果
            Assert.Equal(SyncStatus.Completed, result.Status);
            Assert.True(result.IsSuccessful);

            // 验证操作计划
            Assert.Equal(1, result.Actions.Count(a => a.ActionType == SyncActionType.UpdateFile)); // file1.txt 应该被更新
            Assert.Equal(1, result.Actions.Count(a => a.ActionType == SyncActionType.CopyFile));  // file2.txt 应该被复制

            // 但由于是预览模式，实际文件应该没有变化
            var currentTargetContent = File.ReadAllText(Path.Combine(_testTargetDir, "file1.txt"));
            var currentTargetFileCount = Directory.GetFiles(_testTargetDir).Length;

            Assert.Equal(originalTargetContent, currentTargetContent); // 内容应该没有变化
            Assert.Equal(originalTargetFileCount, currentTargetFileCount); // 文件数量应该没有变化
            Assert.False(File.Exists(Path.Combine(_testTargetDir, "file2.txt"))); // file2.txt 不应该被创建
        }

        [Fact]
        public async Task IgnorePatterns_ShouldSkipMatchingFiles()
        {
            // 准备源目录测试数据
            var sourceFiles = new Dictionary<string, byte[]>
            {
                ["file1.txt"] = Encoding.UTF8.GetBytes("Regular file"),
                ["temp.txt"] = Encoding.UTF8.GetBytes("Temporary file"),
                ["file.tmp"] = Encoding.UTF8.GetBytes("Another temporary file"),
                ["logs/log.txt"] = Encoding.UTF8.GetBytes("Log file"),
                ["backup.bak"] = Encoding.UTF8.GetBytes("Backup file"),
            };

            var sourceDirs = new[] { "logs", "cache" };

            CreateTestFileStructure(_testSourceDir, sourceFiles, sourceDirs);

            // 配置同步选项，包含忽略模式
            var options = new SyncOptions
            {
                SourcePath = _testSourceDir,
                TargetPath = _testTargetDir,
                SyncMode = SyncMode.OneWay,
                CompareMethod = CompareMethod.Content,
                IgnorePatterns = new List<string>
                {
                    "*.tmp",
                    "*.bak",
                    "**/logs/**"
                },
                MaxParallelOperations = 1
            };

            // 执行同步
            var syncHelper = new FileSyncHelper(options, CreateProgressReporter());
            var result = await syncHelper.SyncAsync();

            // 验证结果
            Assert.Equal(SyncStatus.Completed, result.Status);
            Assert.True(result.IsSuccessful);

            // 验证非忽略文件已复制
            Assert.True(File.Exists(Path.Combine(_testTargetDir, "file1.txt")));
            Assert.True(File.Exists(Path.Combine(_testTargetDir, "temp.txt")));

            // 验证忽略文件未复制
            Assert.False(File.Exists(Path.Combine(_testTargetDir, "file.tmp")));
            Assert.False(File.Exists(Path.Combine(_testTargetDir, "backup.bak")));
            Assert.False(File.Exists(Path.Combine(_testTargetDir, "logs", "log.txt")));
            Assert.False(Directory.Exists(Path.Combine(_testTargetDir, "logs")));
        }

        [Fact]
        public async Task ConfigFileLoad_ShouldApplyCorrectSettings()
        {
            // 创建配置文件
            string configPath = Path.Combine(_testSourceDir, "sync.config");
            var configOptions = new SyncOptions
            {
                SourcePath = _testSourceDir,
                TargetPath = _testTargetDir,
                SyncMode = SyncMode.OneWay,
                CompareMethod = CompareMethod.Hash,
                HashAlgorithm = EHashType.SHA256,
                SamplingRate = 0.2,
                MaxParallelOperations = 2,
                IgnorePatterns = new List<string> { "*.tmp", "*.bak" }
            };

            FileSyncHelper.SaveToJsonFile(configOptions, configPath);

            // 准备测试文件
            var sourceFiles = new Dictionary<string, byte[]>
            {
                ["file1.txt"] = Encoding.UTF8.GetBytes("Test file"),
                ["ignore.tmp"] = Encoding.UTF8.GetBytes("Should be ignored"),
            };

            CreateTestFileStructure(_testSourceDir, sourceFiles, new string[0]);

            // 从配置文件加载选项
            var loadedOptions = FileSyncHelper.LoadFromJsonFile(configPath);

            // 执行同步
            var syncHelper = new FileSyncHelper(loadedOptions, CreateProgressReporter());
            var result = await syncHelper.SyncAsync();

            // 验证结果
            Assert.Equal(SyncStatus.Completed, result.Status);
            Assert.True(result.IsSuccessful);

            // 验证是否正确应用了忽略规则
            Assert.True(File.Exists(Path.Combine(_testTargetDir, "file1.txt")));
            Assert.False(File.Exists(Path.Combine(_testTargetDir, "ignore.tmp")));
        }

        [Fact]
        public async Task ErrorHandling_ShouldContinueOnError()
        {
            // 准备源目录测试数据
            var sourceFiles = new Dictionary<string, byte[]>
            {
                ["file1.txt"] = Encoding.UTF8.GetBytes("File 1"),
                ["file2.txt"] = Encoding.UTF8.GetBytes("File 2"),
                ["file3.txt"] = Encoding.UTF8.GetBytes("File 3"),
            };

            CreateTestFileStructure(_testSourceDir, sourceFiles, new string[0]);

            // 创建部分目标结构
            Directory.CreateDirectory(_testTargetDir);

            // 制造错误条件：创建目标中的只读文件
            var readOnlyFile = Path.Combine(_testTargetDir, "file2.txt");
            File.WriteAllText(readOnlyFile, "Read-only content");
            File.SetAttributes(readOnlyFile, FileAttributes.ReadOnly);

            // 配置同步选项，启用错误继续
            var options = new SyncOptions
            {
                SourcePath = _testSourceDir,
                TargetPath = _testTargetDir,
                SyncMode = SyncMode.OneWay,
                CompareMethod = CompareMethod.Content,
                ContinueOnError = true,
                MaxParallelOperations = 1
            };

            // 执行同步
            var syncHelper = new FileSyncHelper(options, CreateProgressReporter());
            var result = await syncHelper.SyncAsync();

            // 清理只读属性，便于后续删除
            File.SetAttributes(readOnlyFile, FileAttributes.Normal);

            // 验证结果
            Assert.Equal(SyncStatus.Completed, result.Status); // 应该完成，尽管有错误
            Assert.True(result.IsSuccessful);
            Assert.True(result.Statistics.Errors > 0); // 应该有错误记录

            // 验证其他文件是否被正确同步
            Assert.True(File.Exists(Path.Combine(_testTargetDir, "file1.txt")));
            Assert.True(File.Exists(Path.Combine(_testTargetDir, "file3.txt")));

            // file2.txt 应该仍是原来的内容
            Assert.Equal("Read-only content", File.ReadAllText(Path.Combine(_testTargetDir, "file2.txt")));
        }

        [Fact]
        public async Task Sync_LargeFiles_ShouldHandleThemCorrectly()
        {
            // 创建一个较大的源文件（约2MB）
            var largeFileData = GenerateRandomData(2 * 1024 * 1024, 42);
            var sourceFiles = new Dictionary<string, byte[]>
            {
                ["largefile.dat"] = largeFileData,
            };

            CreateTestFileStructure(_testSourceDir, sourceFiles, new string[0]);

            // 配置同步选项，启用内容比较
            var options = new SyncOptions
            {
                SourcePath = _testSourceDir,
                TargetPath = _testTargetDir,
                SyncMode = SyncMode.OneWay,
                CompareMethod = CompareMethod.Content,
                MaxParallelOperations = 1
            };

            // 测量同步时间
            var stopwatch = Stopwatch.StartNew();
            var syncHelper = new FileSyncHelper(options, CreateProgressReporter());
            var result = await syncHelper.SyncAsync();
            stopwatch.Stop();

            // 输出性能信息
            _output.WriteLine($"同步2MB文件的耗时: {stopwatch.ElapsedMilliseconds}ms");
            _output.WriteLine($"同步速度: {result.BytesPerSecond / (1024 * 1024):F2} MB/s");

            // 验证结果
            Assert.Equal(SyncStatus.Completed, result.Status);
            Assert.True(result.IsSuccessful);
            Assert.Equal(1, result.Statistics.FilesCopied);

            // 验证文件内容
            var targetFileData = File.ReadAllBytes(Path.Combine(_testTargetDir, "largefile.dat"));
            Assert.Equal(largeFileData, targetFileData);
        }

        [Fact]
        public async Task FileSyncReport_ShouldContainDetailedInformation()
        {
            // 准备测试数据
            var sourceFiles = new Dictionary<string, byte[]>
            {
                ["file1.txt"] = Encoding.UTF8.GetBytes("File 1"),
                ["file2.txt"] = Encoding.UTF8.GetBytes("File 2"),
                ["subdir/file3.txt"] = Encoding.UTF8.GetBytes("File 3"),
            };

            var sourceDirs = new[] { "subdir" };

            CreateTestFileStructure(_testSourceDir, sourceFiles, sourceDirs);

            // 配置同步选项
            var options = new SyncOptions
            {
                SourcePath = _testSourceDir,
                TargetPath = _testTargetDir,
                SyncMode = SyncMode.OneWay,
                CompareMethod = CompareMethod.Content,
                MaxParallelOperations = 1
            };

            // 执行同步
            var syncHelper = new FileSyncHelper(options, CreateProgressReporter());
            var result = await syncHelper.SyncAsync();

            // 获取报告
            string summary = result.GetSummary();
            string report = result.GenerateReport();

            // 验证报告内容
            Assert.Contains("同步模式: 单向同步", summary);
            Assert.Contains($"源路径: {_testSourceDir}", summary);
            Assert.Contains($"目标路径: {_testTargetDir}", summary);
            Assert.Contains("文件统计:", summary);
            Assert.Contains("复制: 3 个文件", summary);

            Assert.Contains("同步操作报告", report);
            Assert.Contains("详细操作记录", report);
            Assert.Contains("【复制文件】操作记录", report);

            // 输出报告以供检查
            _output.WriteLine("=== 同步摘要 ===");
            _output.WriteLine(summary);
            _output.WriteLine("=== 详细报告 ===");
            _output.WriteLine(report);
        }

        #endregion
    }
}
