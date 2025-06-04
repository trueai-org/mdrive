using MDriveSync.Core.DB;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Xunit.Abstractions;

namespace MDriveSync.Test
{
    /// <summary>
    /// SqliteRepository 性能测试类
    /// </summary>
    public class SqliteRepositoryPerformanceTests : BaseTests
    {
        private readonly ITestOutputHelper _output;
        private readonly string _testDbPath;

        public SqliteRepositoryPerformanceTests(ITestOutputHelper output)
        {
            _output = output;
            _testDbPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(_testDbPath);
        }

        [Fact]
        public void TestEncryptedVsUnencryptedPerformance()
        {
            // 配置测试参数
            int[] recordCounts = { 10000, 50000 };
            int iterations = 3; // 每个测试重复执行次数
            string password = "test-encryption-password-123"; // 加密密码

            var results = new Dictionary<string, Dictionary<int, List<TestResult>>>();
            results["加密"] = new Dictionary<int, List<TestResult>>();
            results["未加密"] = new Dictionary<int, List<TestResult>>();

            foreach (int count in recordCounts)
            {
                results["加密"][count] = new List<TestResult>();
                results["未加密"][count] = new List<TestResult>();
            }

            // 执行测试
            foreach (int count in recordCounts)
            {
                _output.WriteLine($"测试记录数: {count}");

                // 生成测试数据
                var testEntities = GenerateTestEntities(count);

                // 测试未加密数据库
                _output.WriteLine("测试未加密数据库...");
                for (int i = 0; i < iterations; i++)
                {
                    string dbName = $"unencrypted_{count}_{i}.db";
                    var result = RunPerformanceTest(dbName, null, testEntities, count);
                    results["未加密"][count].Add(result);
                    _output.WriteLine($"  迭代 {i + 1}: 添加: {result.AddTime:F2}ms, 查询: {result.QueryTime:F2}ms, 更新: {result.UpdateTime:F2}ms, 删除: {result.DeleteTime:F2}ms");
                }

                //// 测试加密数据库
                //_output.WriteLine("测试加密数据库...");
                //for (int i = 0; i < iterations; i++)
                //{
                //    string dbName = $"encrypted_{count}_{i}.db";
                //    var result = RunPerformanceEncryptTest(dbName, password, testEntities, count);
                //    results["加密"][count].Add(result);
                //    _output.WriteLine($"  迭代 {i + 1}: 添加: {result.AddTime:F2}ms, 查询: {result.QueryTime:F2}ms, 更新: {result.UpdateTime:F2}ms, 删除: {result.DeleteTime:F2}ms");
                //}

                // 测试 litedb
                //RunPerformanceLiteDbTest
                _output.WriteLine("测试 LiteDB 数据库...");
                for (int i = 0; i < iterations; i++)
                {
                    string dbName = $"litedb_{count}_{i}.db";
                    var result = RunPerformanceLiteDbTest(dbName, password, testEntities, count);
                    results["加密"][count].Add(result);
                    _output.WriteLine($"  迭代 {i + 1}: 添加: {result.AddTime:F2}ms, 查询: {result.QueryTime:F2}ms, 更新: {result.UpdateTime:F2}ms, 删除: {result.DeleteTime:F2}ms");
                }

                _output.WriteLine("");
            }

            // 生成报告
            string report = GenerateReport(results, recordCounts, iterations);
            _output.WriteLine(report);

            // 保存报告
            string reportPath = Path.Combine(Directory.GetCurrentDirectory(), "SqliteRepository性能测试报告.md");
            File.WriteAllText(reportPath, report, Encoding.UTF8);
            _output.WriteLine($"报告已保存至: {reportPath}");
        }

        private TestResult RunPerformanceTest(string dbName, string password, List<TestEntity> entities, int count)
        {
            // 创建测试数据库路径
            string dbFilePath = Path.Combine(_testDbPath, dbName);
            if (File.Exists(dbFilePath))
            {
                File.Delete(dbFilePath);
            }

            // 初始化仓库
            var repo = new SqliteRepository<TestEntity>(dbName, _testDbPath, false);

            var result = new TestResult();
            var stopwatch = new Stopwatch();

            try
            {
                // 测试添加性能
                stopwatch.Restart();
                repo.AddRange(entities);
                stopwatch.Stop();
                result.AddTime = stopwatch.Elapsed.TotalMilliseconds;

                // 测试查询性能
                stopwatch.Restart();
                var allItems = repo.GetAll();
                stopwatch.Stop();
                result.QueryTime = stopwatch.Elapsed.TotalMilliseconds;

                // 验证记录数
                if (allItems.Count != count)
                {
                    throw new Exception($"查询返回的记录数 {allItems.Count} 与预期的 {count} 不匹配");
                }

                // 测试更新性能
                stopwatch.Restart();
                foreach (var item in entities.Take(count / 10)) // 更新10%的记录
                {
                    item.Name = $"Updated-{item.Name}";
                    repo.Update(item);
                }
                stopwatch.Stop();
                result.UpdateTime = stopwatch.Elapsed.TotalMilliseconds;

                // 测试删除性能
                stopwatch.Restart();
                foreach (var item in entities.Take(count / 10)) // 删除10%的记录
                {
                    repo.Delete(item.Key);
                }
                stopwatch.Stop();
                result.DeleteTime = stopwatch.Elapsed.TotalMilliseconds;

                return result;
            }
            finally
            {
                // 清理测试文件
                try
                {
                    if (File.Exists(dbFilePath))
                    {
                        File.Delete(dbFilePath);
                    }
                }
                catch { /* 忽略清理错误 */ }
            }
        }

        private TestResult RunPerformanceEncryptTest(string dbName, string password, List<TestEntity> entities, int count)
        {
            // 创建测试数据库路径
            string dbFilePath = Path.Combine(_testDbPath, dbName);
            if (File.Exists(dbFilePath))
            {
                File.Delete(dbFilePath);
            }

            // 初始化仓库
            var repo = new EncryptSqliteRepository<TestEntity>(dbName, password, _testDbPath, false);

            var result = new TestResult();
            var stopwatch = new Stopwatch();

            try
            {
                // 测试添加性能
                stopwatch.Restart();
                repo.AddRange(entities);
                stopwatch.Stop();
                result.AddTime = stopwatch.Elapsed.TotalMilliseconds;

                // 测试查询性能
                stopwatch.Restart();
                var allItems = repo.GetAll();
                stopwatch.Stop();
                result.QueryTime = stopwatch.Elapsed.TotalMilliseconds;

                // 验证记录数
                if (allItems.Count != count)
                {
                    throw new Exception($"查询返回的记录数 {allItems.Count} 与预期的 {count} 不匹配");
                }

                // 测试更新性能
                stopwatch.Restart();
                foreach (var item in entities.Take(count / 10)) // 更新10%的记录
                {
                    item.Name = $"Updated-{item.Name}";
                    repo.Update(item);
                }
                stopwatch.Stop();
                result.UpdateTime = stopwatch.Elapsed.TotalMilliseconds;

                // 测试删除性能
                stopwatch.Restart();
                foreach (var item in entities.Take(count / 10)) // 删除10%的记录
                {
                    repo.Delete(item.Key);
                }
                stopwatch.Stop();
                result.DeleteTime = stopwatch.Elapsed.TotalMilliseconds;

                return result;
            }
            catch (Exception ex)
            {
                throw;
            }
            finally
            {
                // 清理测试文件
                try
                {
                    if (File.Exists(dbFilePath))
                    {
                        File.Delete(dbFilePath);
                    }
                }
                catch { /* 忽略清理错误 */ }
            }
        }

        private TestResult RunPerformanceLiteDbTest(string dbName, string password, List<TestEntity> entities, int count)
        {
            // 创建测试数据库路径
            string dbFilePath = Path.Combine(_testDbPath, dbName);
            if (File.Exists(dbFilePath))
            {
                File.Delete(dbFilePath);
            }

            // 初始化仓库
            var repo = new LiteRepository<TestEntity, int>(dbName, password, _testDbPath);

            var result = new TestResult();
            var stopwatch = new Stopwatch();

            try
            {
                // 测试添加性能
                stopwatch.Restart();
                repo.AddRange(entities);
                stopwatch.Stop();
                result.AddTime = stopwatch.Elapsed.TotalMilliseconds;

                // 测试查询性能
                stopwatch.Restart();
                var allItems = repo.GetAll();
                stopwatch.Stop();
                result.QueryTime = stopwatch.Elapsed.TotalMilliseconds;

                // 验证记录数
                if (allItems.Count != count)
                {
                    throw new Exception($"查询返回的记录数 {allItems.Count} 与预期的 {count} 不匹配");
                }

                // 测试更新性能
                stopwatch.Restart();
                foreach (var item in entities.Take(count / 10)) // 更新10%的记录
                {
                    item.Name = $"Updated-{item.Name}";
                    repo.Update(item);
                }
                stopwatch.Stop();
                result.UpdateTime = stopwatch.Elapsed.TotalMilliseconds;

                // 测试删除性能
                stopwatch.Restart();
                foreach (var item in entities.Take(count / 10)) // 删除10%的记录
                {
                    repo.Delete(item.Key);
                }
                stopwatch.Stop();
                result.DeleteTime = stopwatch.Elapsed.TotalMilliseconds;

                return result;
            }
            catch (Exception ex)
            {
                throw;
            }
            finally
            {
                // 清理测试文件
                try
                {
                    if (File.Exists(dbFilePath))
                    {
                        File.Delete(dbFilePath);
                    }
                }
                catch { /* 忽略清理错误 */ }
            }
        }

        private List<TestEntity> GenerateTestEntities(int count)
        {
            var entities = new List<TestEntity>();
            for (int i = 0; i < count; i++)
            {
                entities.Add(new TestEntity
                {
                    Key = i + 1,
                    Name = $"Test-{i}",
                    Description = $"This is a test entity with index {i} and some additional text for data size.",
                    CreatedAt = DateTime.Now.AddMinutes(-i),
                    IsActive = i % 3 == 0
                });
            }
            return entities;
        }

        private string GenerateReport(Dictionary<string, Dictionary<int, List<TestResult>>> results, int[] recordCounts, int iterations)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# SQLite 仓库性能测试报告");
            sb.AppendLine();
            sb.AppendLine("## 测试环境");
            sb.AppendLine($"- 操作系统: {RuntimeInformation.OSDescription}");
            sb.AppendLine($"- 处理器: {Environment.ProcessorCount} 核心");
            sb.AppendLine($"- .NET 版本: {Environment.Version}");
            sb.AppendLine($"- 测试时间: {DateTime.Now}");
            sb.AppendLine();

            sb.AppendLine("## 测试结果");
            sb.AppendLine();

            // 计算平均值并跳过第一次迭代（预热）
            foreach (int count in recordCounts)
            {
                sb.AppendLine($"### 记录数: {count:N0}");
                sb.AppendLine();
                sb.AppendLine("| 数据库类型 | 添加 (ms) | 查询 (ms) | 更新 (ms) | 删除 (ms) | 总时间 (ms) |");
                sb.AppendLine("|------------|-----------|-----------|-----------|-----------|-------------|");

                foreach (var type in new[] { "未加密", "加密" })
                {
                    if (iterations > 1)
                    {
                        // 跳过第一次迭代（预热）
                        var validResults = results[type][count].Skip(1).ToList();

                        if (validResults.Count > 0)
                        {
                            double avgAddTime = validResults.Average(r => r.AddTime);
                            double avgQueryTime = validResults.Average(r => r.QueryTime);
                            double avgUpdateTime = validResults.Average(r => r.UpdateTime);
                            double avgDeleteTime = validResults.Average(r => r.DeleteTime);
                            double totalTime = avgAddTime + avgQueryTime + avgUpdateTime + avgDeleteTime;

                            sb.AppendLine($"| {type} | {avgAddTime:F2} | {avgQueryTime:F2} | {avgUpdateTime:F2} | {avgDeleteTime:F2} | {totalTime:F2} |");
                        }
                    }
                    else
                    {
                        // 只有一次迭代，直接使用结果
                        var result = results[type][count][0];
                        double totalTime = result.AddTime + result.QueryTime + result.UpdateTime + result.DeleteTime;

                        sb.AppendLine($"| {type} | {result.AddTime:F2} | {result.QueryTime:F2} | {result.UpdateTime:F2} | {result.DeleteTime:F2} | {totalTime:F2} |");
                    }
                }

                // 计算性能差异百分比
                if (iterations > 1)
                {
                    var unencryptedResults = results["未加密"][count].Skip(1).ToList();
                    var encryptedResults = results["加密"][count].Skip(1).ToList();

                    if (unencryptedResults.Count > 0 && encryptedResults.Count > 0)
                    {
                        double avgUnencryptedAddTime = unencryptedResults.Average(r => r.AddTime);
                        double avgEncryptedAddTime = encryptedResults.Average(r => r.AddTime);
                        double addTimeDiff = (avgEncryptedAddTime / avgUnencryptedAddTime - 1) * 100;

                        double avgUnencryptedQueryTime = unencryptedResults.Average(r => r.QueryTime);
                        double avgEncryptedQueryTime = encryptedResults.Average(r => r.QueryTime);
                        double queryTimeDiff = (avgEncryptedQueryTime / avgUnencryptedQueryTime - 1) * 100;

                        double avgUnencryptedTotalTime = unencryptedResults.Average(r => r.AddTime + r.QueryTime + r.UpdateTime + r.DeleteTime);
                        double avgEncryptedTotalTime = encryptedResults.Average(r => r.AddTime + r.QueryTime + r.UpdateTime + r.DeleteTime);
                        double totalTimeDiff = (avgEncryptedTotalTime / avgUnencryptedTotalTime - 1) * 100;

                        sb.AppendLine();
                        sb.AppendLine($"加密数据库相比未加密数据库:");
                        sb.AppendLine($"- 添加操作: {addTimeDiff:F2}% {(addTimeDiff > 0 ? "更慢" : "更快")}");
                        sb.AppendLine($"- 查询操作: {queryTimeDiff:F2}% {(queryTimeDiff > 0 ? "更慢" : "更快")}");
                        sb.AppendLine($"- 总体性能: {totalTimeDiff:F2}% {(totalTimeDiff > 0 ? "更慢" : "更快")}");
                    }
                }

                sb.AppendLine();
                sb.AppendLine();
            }

            // 添加结论
            sb.AppendLine("## 结论");
            sb.AppendLine();
            sb.AppendLine("- 加密数据库比未加密数据库在所有操作上通常会有一定的性能开销。");
            sb.AppendLine("- 随着记录数量的增加，加密和非加密数据库之间的性能差异变得更加明显。");
            sb.AppendLine("- 查询操作在加密数据库中受到的影响最大，因为需要解密整个数据块。");
            sb.AppendLine("- 在决定是否使用数据库加密时，应权衡安全需求和性能要求。");

            return sb.ToString();
        }

        public override void Dispose()
        {
            // 清理测试目录
            if (Directory.Exists(_testDbPath))
            {
                try
                {
                    Directory.Delete(_testDbPath, true);
                }
                catch
                {
                    // 忽略清理错误
                }
            }

            base.Dispose();
        }

        // 测试实体类
        public class TestEntity : IBaseKey<int>, IBaseId<int>
        {
            [SQLite.PrimaryKey]
            [ServiceStack.DataAnnotations.PrimaryKey]
            public int Key { get; set; }

            public string Name { get; set; }
            public string Description { get; set; }
            public DateTime CreatedAt { get; set; }
            public bool IsActive { get; set; }

            public int Id { get; set; }
        }

        // 测试结果结构
        private class TestResult
        {
            public double AddTime { get; set; }
            public double QueryTime { get; set; }
            public double UpdateTime { get; set; }
            public double DeleteTime { get; set; }
        }
    }
}