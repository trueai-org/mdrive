using MDriveSync.Core.Services;
using MDriveSync.Security.Models;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Xunit.Abstractions;

namespace MDriveSync.Test
{
    /// <summary>
    /// FileSyncHelper ��Ԫ����
    /// </summary>
    public class FileSyncHelperTests : BaseTests
    {
        private readonly ITestOutputHelper _output;
        private readonly string _testSourceDir;
        private readonly string _testTargetDir;

        public FileSyncHelperTests(ITestOutputHelper output)
        {
            _output = output;

            // ��������Ŀ¼
            _testSourceDir = Path.Combine(Path.GetTempPath(), $"MDriveSync_Test_Source_{Guid.NewGuid()}");
            _testTargetDir = Path.Combine(Path.GetTempPath(), $"MDriveSync_Test_Target_{Guid.NewGuid()}");

            Directory.CreateDirectory(_testSourceDir);
            Directory.CreateDirectory(_testTargetDir);
        }

        public override void Dispose()
        {
            // �������Ŀ¼
            try
            {
                if (Directory.Exists(_testSourceDir))
                    Directory.Delete(_testSourceDir, true);

                if (Directory.Exists(_testTargetDir))
                    Directory.Delete(_testTargetDir, true);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"�������Ŀ¼ʱ����: {ex.Message}");
            }

            base.Dispose();
        }

        #region ��������

        /// <summary>
        /// ���������ļ��ṹ
        /// </summary>
        private void CreateTestFileStructure(string rootDir, Dictionary<string, byte[]> files, string[] directories)
        {
            // ����Ŀ¼
            foreach (var dir in directories)
            {
                string fullPath = Path.Combine(rootDir, dir);
                Directory.CreateDirectory(fullPath);
            }

            // �����ļ�
            foreach (var file in files)
            {
                string fullPath = Path.Combine(rootDir, file.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                File.WriteAllBytes(fullPath, file.Value);
            }
        }

        /// <summary>
        /// �����������
        /// </summary>
        private byte[] GenerateRandomData(int size, int seed = 0)
        {
            var random = seed == 0 ? new Random() : new Random(seed);
            var data = new byte[size];
            random.NextBytes(data);
            return data;
        }

        /// <summary>
        /// �����ļ��޸�ʱ�����
        /// </summary>
        private void SetFileLastWriteTime(string path, DateTime dateTime)
        {
            File.SetLastWriteTime(path, dateTime);
        }

        /// <summary>
        /// ��֤Ŀ¼�ṹƥ��
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

                    // �Ƚ��ļ�����
                    var sourceContent = File.ReadAllBytes(Path.Combine(sourceDir, sourceFiles[i]));
                    var targetContent = File.ReadAllBytes(Path.Combine(targetDir, targetFiles[i]));
                    Assert.Equal(sourceContent, targetContent);
                }
            }
            else if (sourceFiles.Count == targetFiles.Count)
            {
                // Ӧ���в�ͬ������ļ������Ƿ�������һ����ͬ
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

                Assert.True(hasDifference, "����ԴĿ¼��Ŀ��Ŀ¼�в��죬��ʵ����������ȫƥ��");
            }
        }

        /// <summary>
        /// �������ȱ�����
        /// </summary>
        private IProgress<SyncProgress> CreateProgressReporter()
        {
            return new Progress<SyncProgress>(progress =>
            {
                _output.WriteLine($"����: {progress.ProgressPercentage}%, ��Ϣ: {progress.Message}");
                _output.WriteLine($"����: {progress.ProcessedItems}/{progress.TotalItems}, �ٶ�: {progress.FormattedSpeed}");

                if (progress.Statistics != null)
                {
                    _output.WriteLine($"����: {progress.Statistics.FilesCopied}/{progress.Statistics.FilesToCopy}, " +
                                     $"����: {progress.Statistics.FilesUpdated}/{progress.Statistics.FilesToUpdate}, " +
                                     $"ɾ��: {progress.Statistics.FilesDeleted}/{progress.Statistics.FilesToDelete}");
                }
            });
        }

        #endregion

        #region ��Ԫ����

        [Fact]
        public async Task OneWaySync_EmptyTarget_ShouldCopyAllFiles()
        {
            // ׼����������
            var sourceFiles = new Dictionary<string, byte[]>
            {
                ["file1.txt"] = Encoding.UTF8.GetBytes("This is file 1"),
                ["file2.txt"] = Encoding.UTF8.GetBytes("This is file 2"),
                ["subdir/file3.txt"] = Encoding.UTF8.GetBytes("This is file 3"),
            };

            var sourceDirs = new[] { "subdir", "emptydir" };

            CreateTestFileStructure(_testSourceDir, sourceFiles, sourceDirs);

            // ����ͬ��ѡ��
            var options = new SyncOptions
            {
                SourcePath = _testSourceDir,
                TargetPath = _testTargetDir,
                SyncMode = SyncMode.OneWay,
                CompareMethod = CompareMethod.Content,
                MaxParallelOperations = 2
            };

            // ִ��ͬ��
            var syncHelper = new FileSyncHelper(options, CreateProgressReporter());
            var result = await syncHelper.SyncAsync();

            // ��֤���
            Assert.Equal(SyncStatus.Completed, result.Status);
            Assert.True(result.IsSuccessful);
            Assert.Equal(3, result.Statistics.FilesCopied); // Ӧ�ø�����3���ļ�
            Assert.True(result.Statistics.DirectoriesCreated >= 2); // Ӧ�ô���������2��Ŀ¼

            // ��֤Ŀ¼�ṹƥ��
            VerifyDirectoryMatch(_testSourceDir, _testTargetDir);
        }

        [Fact]
        public async Task OneWaySync_ExistingTarget_ShouldUpdateChangedFiles()
        {
            // ׼��ԴĿ¼��������
            var sourceFiles = new Dictionary<string, byte[]>
            {
                ["file1.txt"] = Encoding.UTF8.GetBytes("This is file 1 - updated"),
                ["file2.txt"] = Encoding.UTF8.GetBytes("This is file 2"),
                ["subdir/file3.txt"] = Encoding.UTF8.GetBytes("This is file 3"),
                ["newfile.txt"] = Encoding.UTF8.GetBytes("This is a new file"),
            };

            var sourceDirs = new[] { "subdir", "newdir" };

            // ׼��Ŀ��Ŀ¼�������ݣ��������ݲ�ͬ��
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

            // ����ͬ��ѡ��
            var options = new SyncOptions
            {
                SourcePath = _testSourceDir,
                TargetPath = _testTargetDir,
                SyncMode = SyncMode.OneWay,
                CompareMethod = CompareMethod.Content,
                MaxParallelOperations = 2
            };

            // ִ��ͬ��
            var syncHelper = new FileSyncHelper(options, CreateProgressReporter());
            var result = await syncHelper.SyncAsync();

            // ��֤���
            Assert.Equal(SyncStatus.Completed, result.Status);
            Assert.True(result.IsSuccessful);
            Assert.Equal(1, result.Statistics.FilesUpdated); // file1.txt Ӧ�ñ�����
            Assert.Equal(1, result.Statistics.FilesCopied);  // newfile.txt Ӧ�ñ�����
            Assert.True(result.Statistics.DirectoriesCreated >= 1); // newdir Ӧ�ñ�����

            // ��֤ oldfile.txt �� olddir ��Ȼ���ڣ�����ͬ����ɾ��Ŀ���д��ڵ�Դ�в����ڵ��ļ���
            Assert.True(File.Exists(Path.Combine(_testTargetDir, "oldfile.txt")));
            Assert.True(Directory.Exists(Path.Combine(_testTargetDir, "olddir")));

            // ��֤ file1.txt �Ѹ���
            var file1Content = File.ReadAllText(Path.Combine(_testTargetDir, "file1.txt"));
            Assert.Equal("This is file 1 - updated", file1Content);

            // ��֤ newfile.txt �Ѹ���
            var newfileContent = File.ReadAllText(Path.Combine(_testTargetDir, "newfile.txt"));
            Assert.Equal("This is a new file", newfileContent);
        }

        [Fact]
        public async Task MirrorSync_ShouldDeleteFilesNotInSource()
        {
            // ׼��ԴĿ¼��������
            var sourceFiles = new Dictionary<string, byte[]>
            {
                ["file1.txt"] = Encoding.UTF8.GetBytes("This is file 1"),
                ["file2.txt"] = Encoding.UTF8.GetBytes("This is file 2"),
                ["subdir/file3.txt"] = Encoding.UTF8.GetBytes("This is file 3"),
            };

            var sourceDirs = new[] { "subdir" };

            // ׼��Ŀ��Ŀ¼�������ݣ����������ļ���
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

            // ����ͬ��ѡ��
            var options = new SyncOptions
            {
                SourcePath = _testSourceDir,
                TargetPath = _testTargetDir,
                SyncMode = SyncMode.Mirror,
                CompareMethod = CompareMethod.Content,
                MaxParallelOperations = 2,
                // ʹ����ͨɾ������ʹ�û���վ��������ԣ�
                UseRecycleBin = false
            };

            // ִ��ͬ��
            var syncHelper = new FileSyncHelper(options, CreateProgressReporter());
            var result = await syncHelper.SyncAsync();

            // ��֤���
            Assert.Equal(SyncStatus.Completed, result.Status);
            Assert.True(result.IsSuccessful);
            Assert.Equal(0, result.Statistics.FilesUpdated); // û���ļ���Ҫ����
            Assert.Equal(0, result.Statistics.FilesCopied);  // û���ļ���Ҫ����
            Assert.Equal(2, result.Statistics.FilesDeleted); // 2���ļ�Ӧ�ñ�ɾ��
            Assert.Equal(1, result.Statistics.DirectoriesDeleted); // 1��Ŀ¼Ӧ�ñ�ɾ��

            // ��֤ oldfile.txt �� olddir ���ٴ���
            Assert.False(File.Exists(Path.Combine(_testTargetDir, "oldfile.txt")));
            Assert.False(Directory.Exists(Path.Combine(_testTargetDir, "olddir")));

            // ��֤Ŀ¼�ṹƥ��
            VerifyDirectoryMatch(_testSourceDir, _testTargetDir);
        }

        [Fact]
        public async Task TwoWaySync_WithChangesOnBothSides_ShouldResolveConflicts()
        {
            // ׼����ͬ�ĳ�ʼ����
            var commonFiles = new Dictionary<string, byte[]>
            {
                ["common.txt"] = Encoding.UTF8.GetBytes("This is a common file"),
                ["unchanged.txt"] = Encoding.UTF8.GetBytes("This file will not change"),
                ["subdir/file.txt"] = Encoding.UTF8.GetBytes("This is a subdirectory file"),
            };

            var commonDirs = new[] { "subdir" };

            // ������ʼ����ͬ�ṹ
            CreateTestFileStructure(_testSourceDir, commonFiles, commonDirs);
            CreateTestFileStructure(_testTargetDir, commonFiles, commonDirs);

            // ��Դ����һЩ����
            File.WriteAllText(Path.Combine(_testSourceDir, "common.txt"), "This is updated in source");
            File.WriteAllText(Path.Combine(_testSourceDir, "source_only.txt"), "This file only exists in source");
            Directory.CreateDirectory(Path.Combine(_testSourceDir, "source_dir"));

            // ��Ŀ������һЩ����
            File.WriteAllText(Path.Combine(_testTargetDir, "common.txt"), "This is updated in target");
            File.WriteAllText(Path.Combine(_testTargetDir, "target_only.txt"), "This file only exists in target");
            Directory.CreateDirectory(Path.Combine(_testTargetDir, "target_dir"));

            // ����ʱ�����ʹԴ�ļ���Ŀ���ļ�����
            var sourceTime = DateTime.Now;
            var targetTime = sourceTime.AddMinutes(-30);

            SetFileLastWriteTime(Path.Combine(_testSourceDir, "common.txt"), sourceTime);
            SetFileLastWriteTime(Path.Combine(_testTargetDir, "common.txt"), targetTime);

            // ����ͬ��ѡ�ʹ��"����"���Խ����ͻ
            var options = new SyncOptions
            {
                SourcePath = _testSourceDir,
                TargetPath = _testTargetDir,
                SyncMode = SyncMode.TwoWay,
                CompareMethod = CompareMethod.DateTimeAndSize,
                ConflictResolution = ConflictResolution.Newer,
                MaxParallelOperations = 2
            };

            // ִ��ͬ��
            var syncHelper = new FileSyncHelper(options, CreateProgressReporter());
            var result = await syncHelper.SyncAsync();

            // ��֤���
            Assert.Equal(SyncStatus.Completed, result.Status);
            Assert.True(result.IsSuccessful);

            // ��֤��ͻ�����common.txt Ӧ��ʹ��Դ�汾�����£�
            var commonContent = File.ReadAllText(Path.Combine(_testTargetDir, "common.txt"));
            Assert.Equal("This is updated in source", commonContent);

            // ��֤�����ļ��Ѹ��ƣ�source_only.txt Ӧ����Ŀ���д���
            Assert.True(File.Exists(Path.Combine(_testTargetDir, "source_only.txt")));

            // ��֤��һ������ļ�Ҳ�Ѹ��ƣ�target_only.txt Ӧ����Դ�д���
            Assert.True(File.Exists(Path.Combine(_testSourceDir, "target_only.txt")));

            // ��֤Ŀ¼Ҳ��ͬ��
            Assert.True(Directory.Exists(Path.Combine(_testTargetDir, "source_dir")));
            Assert.True(Directory.Exists(Path.Combine(_testSourceDir, "target_dir")));
        }

        [Fact]
        public async Task ConflictResolution_KeepBoth_ShouldRenameTarget()
        {
            // ׼����ͬ�ĳ�ʼ����
            var commonFiles = new Dictionary<string, byte[]>
            {
                ["conflict.txt"] = Encoding.UTF8.GetBytes("Original content"),
            };

            // ������ʼ����ͬ�ṹ
            CreateTestFileStructure(_testSourceDir, commonFiles, new string[0]);
            CreateTestFileStructure(_testTargetDir, commonFiles, new string[0]);

            // ��Դ��Ŀ�����޸�ͬһ���ļ�
            File.WriteAllText(Path.Combine(_testSourceDir, "conflict.txt"), "Source modified content");
            File.WriteAllText(Path.Combine(_testTargetDir, "conflict.txt"), "Target modified content");

            // ����ͬ��ѡ�ʹ��"��������"���Խ����ͻ
            var options = new SyncOptions
            {
                SourcePath = _testSourceDir,
                TargetPath = _testTargetDir,
                SyncMode = SyncMode.TwoWay,
                CompareMethod = CompareMethod.Content,
                ConflictResolution = ConflictResolution.KeepBoth,
                MaxParallelOperations = 1
            };

            // ִ��ͬ��
            var syncHelper = new FileSyncHelper(options, CreateProgressReporter());
            var result = await syncHelper.SyncAsync();

            // ��֤���
            Assert.Equal(SyncStatus.Completed, result.Status);
            Assert.True(result.IsSuccessful);

            // ��֤�����������汾
            var targetContent = File.ReadAllText(Path.Combine(_testTargetDir, "conflict.txt"));
            Assert.Equal("Source modified content", targetContent); // Ŀ���ļ�Ӧ����Դ�ļ�������

            // Ŀ����Ӧ����һ����ʱ������������ļ�
            var targetFiles = Directory.GetFiles(_testTargetDir);
            Assert.Equal(2, targetFiles.Length);

            var renamedFile = targetFiles.First(f => !f.EndsWith("conflict.txt"));
            Assert.Contains("conflict", Path.GetFileName(renamedFile)); // �������ļ�Ӧ����ԭ��

            var renamedContent = File.ReadAllText(renamedFile);
            Assert.Equal("Target modified content", renamedContent); // �������ļ�Ӧ����Ŀ��ԭʼ����
        }

        [Fact]
        public async Task HashComparison_ShouldDetectContentChanges()
        {
            // ׼��ԴĿ¼��������
            var sourceFiles = new Dictionary<string, byte[]>
            {
                ["smallfile.bin"] = GenerateRandomData(1024, 1),
                ["largefile.bin"] = GenerateRandomData(1024 * 1024, 2), // 1MB
            };

            // ׼��Ŀ��Ŀ¼�������ݣ��ļ���С��ͬ���������в�ͬ��
            var targetFiles = new Dictionary<string, byte[]>
            {
                ["smallfile.bin"] = GenerateRandomData(1024, 1), // ��ȫ��ͬ
                ["largefile.bin"] = GenerateRandomData(1024 * 1024, 3), // ��ͬ���ӣ����ݲ�ͬ
            };

            CreateTestFileStructure(_testSourceDir, sourceFiles, new string[0]);
            CreateTestFileStructure(_testTargetDir, targetFiles, new string[0]);

            // ȷ���޸�ʱ����ͬ������ֻ��ͨ����ϣ���仯
            var sameTime = DateTime.Now;
            SetFileLastWriteTime(Path.Combine(_testSourceDir, "smallfile.bin"), sameTime);
            SetFileLastWriteTime(Path.Combine(_testTargetDir, "smallfile.bin"), sameTime);
            SetFileLastWriteTime(Path.Combine(_testSourceDir, "largefile.bin"), sameTime);
            SetFileLastWriteTime(Path.Combine(_testTargetDir, "largefile.bin"), sameTime);

            // ����ͬ��ѡ�ʹ�ù�ϣ�Ƚ�
            var options = new SyncOptions
            {
                SourcePath = _testSourceDir,
                TargetPath = _testTargetDir,
                SyncMode = SyncMode.OneWay,
                CompareMethod = CompareMethod.Hash,
                HashAlgorithm = EHashType.SHA256,
                SamplingRate = 0.1, // 10% ������
                MaxParallelOperations = 1
            };

            // ִ��ͬ��
            var syncHelper = new FileSyncHelper(options, CreateProgressReporter());
            var result = await syncHelper.SyncAsync();

            // ��֤���
            Assert.Equal(SyncStatus.Completed, result.Status);
            Assert.True(result.IsSuccessful);
            Assert.Equal(1, result.Statistics.FilesUpdated); // ֻ�� largefile.bin Ӧ�ñ�����
            Assert.Equal(0, result.Statistics.FilesCopied);

            // ��֤�ļ������Ѹ���
            using (var md5 = MD5.Create())
            {
                var sourceHash = md5.ComputeHash(File.ReadAllBytes(Path.Combine(_testSourceDir, "largefile.bin")));
                var targetHash = md5.ComputeHash(File.ReadAllBytes(Path.Combine(_testTargetDir, "largefile.bin")));
                Assert.Equal(sourceHash, targetHash); // ��ϣӦ����ͬ
            }
        }

        [Fact]
        public async Task PreviewMode_ShouldNotMakeChanges()
        {
            // ׼��ԴĿ¼��������
            var sourceFiles = new Dictionary<string, byte[]>
            {
                ["file1.txt"] = Encoding.UTF8.GetBytes("Source file 1"),
                ["file2.txt"] = Encoding.UTF8.GetBytes("Source file 2"),
            };

            // ׼��Ŀ��Ŀ¼�������ݣ���ͬ���ݣ�
            var targetFiles = new Dictionary<string, byte[]>
            {
                ["file1.txt"] = Encoding.UTF8.GetBytes("Target file 1"),
                // file2.txt ��������Ŀ��
            };

            CreateTestFileStructure(_testSourceDir, sourceFiles, new string[0]);
            CreateTestFileStructure(_testTargetDir, targetFiles, new string[0]);

            // ��¼Ŀ��Ŀ¼��ԭʼ״̬
            var originalTargetContent = File.ReadAllText(Path.Combine(_testTargetDir, "file1.txt"));
            var originalTargetFileCount = Directory.GetFiles(_testTargetDir).Length;

            // ����ͬ��ѡ�����Ԥ��ģʽ
            var options = new SyncOptions
            {
                SourcePath = _testSourceDir,
                TargetPath = _testTargetDir,
                SyncMode = SyncMode.OneWay,
                CompareMethod = CompareMethod.Content,
                PreviewOnly = true,
                MaxParallelOperations = 1
            };

            // ִ��ͬ��
            var syncHelper = new FileSyncHelper(options, CreateProgressReporter());
            var result = await syncHelper.SyncAsync();

            // ��֤���
            Assert.Equal(SyncStatus.Completed, result.Status);
            Assert.True(result.IsSuccessful);

            // ��֤�����ƻ�
            Assert.Equal(1, result.Actions.Count(a => a.ActionType == SyncActionType.UpdateFile)); // file1.txt Ӧ�ñ�����
            Assert.Equal(1, result.Actions.Count(a => a.ActionType == SyncActionType.CopyFile));  // file2.txt Ӧ�ñ�����

            // ��������Ԥ��ģʽ��ʵ���ļ�Ӧ��û�б仯
            var currentTargetContent = File.ReadAllText(Path.Combine(_testTargetDir, "file1.txt"));
            var currentTargetFileCount = Directory.GetFiles(_testTargetDir).Length;

            Assert.Equal(originalTargetContent, currentTargetContent); // ����Ӧ��û�б仯
            Assert.Equal(originalTargetFileCount, currentTargetFileCount); // �ļ�����Ӧ��û�б仯
            Assert.False(File.Exists(Path.Combine(_testTargetDir, "file2.txt"))); // file2.txt ��Ӧ�ñ�����
        }

        [Fact]
        public async Task IgnorePatterns_ShouldSkipMatchingFiles()
        {
            // ׼��ԴĿ¼��������
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

            // ����ͬ��ѡ���������ģʽ
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

            // ִ��ͬ��
            var syncHelper = new FileSyncHelper(options, CreateProgressReporter());
            var result = await syncHelper.SyncAsync();

            // ��֤���
            Assert.Equal(SyncStatus.Completed, result.Status);
            Assert.True(result.IsSuccessful);

            // ��֤�Ǻ����ļ��Ѹ���
            Assert.True(File.Exists(Path.Combine(_testTargetDir, "file1.txt")));
            Assert.True(File.Exists(Path.Combine(_testTargetDir, "temp.txt")));

            // ��֤�����ļ�δ����
            Assert.False(File.Exists(Path.Combine(_testTargetDir, "file.tmp")));
            Assert.False(File.Exists(Path.Combine(_testTargetDir, "backup.bak")));
            Assert.False(File.Exists(Path.Combine(_testTargetDir, "logs", "log.txt")));
            Assert.False(Directory.Exists(Path.Combine(_testTargetDir, "logs")));
        }

        [Fact]
        public async Task ConfigFileLoad_ShouldApplyCorrectSettings()
        {
            // ���������ļ�
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

            // ׼�������ļ�
            var sourceFiles = new Dictionary<string, byte[]>
            {
                ["file1.txt"] = Encoding.UTF8.GetBytes("Test file"),
                ["ignore.tmp"] = Encoding.UTF8.GetBytes("Should be ignored"),
            };

            CreateTestFileStructure(_testSourceDir, sourceFiles, new string[0]);

            // �������ļ�����ѡ��
            var loadedOptions = FileSyncHelper.LoadFromJsonFile(configPath);

            // ִ��ͬ��
            var syncHelper = new FileSyncHelper(loadedOptions, CreateProgressReporter());
            var result = await syncHelper.SyncAsync();

            // ��֤���
            Assert.Equal(SyncStatus.Completed, result.Status);
            Assert.True(result.IsSuccessful);

            // ��֤�Ƿ���ȷӦ���˺��Թ���
            Assert.True(File.Exists(Path.Combine(_testTargetDir, "file1.txt")));
            Assert.False(File.Exists(Path.Combine(_testTargetDir, "ignore.tmp")));
        }

        [Fact]
        public async Task ErrorHandling_ShouldContinueOnError()
        {
            // ׼��ԴĿ¼��������
            var sourceFiles = new Dictionary<string, byte[]>
            {
                ["file1.txt"] = Encoding.UTF8.GetBytes("File 1"),
                ["file2.txt"] = Encoding.UTF8.GetBytes("File 2"),
                ["file3.txt"] = Encoding.UTF8.GetBytes("File 3"),
            };

            CreateTestFileStructure(_testSourceDir, sourceFiles, new string[0]);

            // ��������Ŀ��ṹ
            Directory.CreateDirectory(_testTargetDir);

            // �����������������Ŀ���е�ֻ���ļ�
            var readOnlyFile = Path.Combine(_testTargetDir, "file2.txt");
            File.WriteAllText(readOnlyFile, "Read-only content");
            File.SetAttributes(readOnlyFile, FileAttributes.ReadOnly);

            // ����ͬ��ѡ����ô������
            var options = new SyncOptions
            {
                SourcePath = _testSourceDir,
                TargetPath = _testTargetDir,
                SyncMode = SyncMode.OneWay,
                CompareMethod = CompareMethod.Content,
                ContinueOnError = true,
                MaxParallelOperations = 1
            };

            // ִ��ͬ��
            var syncHelper = new FileSyncHelper(options, CreateProgressReporter());
            var result = await syncHelper.SyncAsync();

            // ����ֻ�����ԣ����ں���ɾ��
            File.SetAttributes(readOnlyFile, FileAttributes.Normal);

            // ��֤���
            Assert.Equal(SyncStatus.Completed, result.Status); // Ӧ����ɣ������д���
            Assert.True(result.IsSuccessful);
            Assert.True(result.Statistics.Errors > 0); // Ӧ���д����¼

            // ��֤�����ļ��Ƿ���ȷͬ��
            Assert.True(File.Exists(Path.Combine(_testTargetDir, "file1.txt")));
            Assert.True(File.Exists(Path.Combine(_testTargetDir, "file3.txt")));

            // file2.txt Ӧ������ԭ��������
            Assert.Equal("Read-only content", File.ReadAllText(Path.Combine(_testTargetDir, "file2.txt")));
        }

        [Fact]
        public async Task Sync_LargeFiles_ShouldHandleThemCorrectly()
        {
            // ����һ���ϴ��Դ�ļ���Լ2MB��
            var largeFileData = GenerateRandomData(2 * 1024 * 1024, 42);
            var sourceFiles = new Dictionary<string, byte[]>
            {
                ["largefile.dat"] = largeFileData,
            };

            CreateTestFileStructure(_testSourceDir, sourceFiles, new string[0]);

            // ����ͬ��ѡ��������ݱȽ�
            var options = new SyncOptions
            {
                SourcePath = _testSourceDir,
                TargetPath = _testTargetDir,
                SyncMode = SyncMode.OneWay,
                CompareMethod = CompareMethod.Content,
                MaxParallelOperations = 1
            };

            // ����ͬ��ʱ��
            var stopwatch = Stopwatch.StartNew();
            var syncHelper = new FileSyncHelper(options, CreateProgressReporter());
            var result = await syncHelper.SyncAsync();
            stopwatch.Stop();

            // ���������Ϣ
            _output.WriteLine($"ͬ��2MB�ļ��ĺ�ʱ: {stopwatch.ElapsedMilliseconds}ms");
            _output.WriteLine($"ͬ���ٶ�: {result.BytesPerSecond / (1024 * 1024):F2} MB/s");

            // ��֤���
            Assert.Equal(SyncStatus.Completed, result.Status);
            Assert.True(result.IsSuccessful);
            Assert.Equal(1, result.Statistics.FilesCopied);

            // ��֤�ļ�����
            var targetFileData = File.ReadAllBytes(Path.Combine(_testTargetDir, "largefile.dat"));
            Assert.Equal(largeFileData, targetFileData);
        }

        [Fact]
        public async Task FileSyncReport_ShouldContainDetailedInformation()
        {
            // ׼����������
            var sourceFiles = new Dictionary<string, byte[]>
            {
                ["file1.txt"] = Encoding.UTF8.GetBytes("File 1"),
                ["file2.txt"] = Encoding.UTF8.GetBytes("File 2"),
                ["subdir/file3.txt"] = Encoding.UTF8.GetBytes("File 3"),
            };

            var sourceDirs = new[] { "subdir" };

            CreateTestFileStructure(_testSourceDir, sourceFiles, sourceDirs);

            // ����ͬ��ѡ��
            var options = new SyncOptions
            {
                SourcePath = _testSourceDir,
                TargetPath = _testTargetDir,
                SyncMode = SyncMode.OneWay,
                CompareMethod = CompareMethod.Content,
                MaxParallelOperations = 1
            };

            // ִ��ͬ��
            var syncHelper = new FileSyncHelper(options, CreateProgressReporter());
            var result = await syncHelper.SyncAsync();

            // ��ȡ����
            string summary = result.GetSummary();
            string report = result.GenerateReport();

            // ��֤��������
            Assert.Contains("ͬ��ģʽ: ����ͬ��", summary);
            Assert.Contains($"Դ·��: {_testSourceDir}", summary);
            Assert.Contains($"Ŀ��·��: {_testTargetDir}", summary);
            Assert.Contains("�ļ�ͳ��:", summary);
            Assert.Contains("����: 3 ���ļ�", summary);

            Assert.Contains("ͬ����������", report);
            Assert.Contains("��ϸ������¼", report);
            Assert.Contains("�������ļ���������¼", report);

            // ��������Թ����
            _output.WriteLine("=== ͬ��ժҪ ===");
            _output.WriteLine(summary);
            _output.WriteLine("=== ��ϸ���� ===");
            _output.WriteLine(report);
        }

        #endregion
    }
}
