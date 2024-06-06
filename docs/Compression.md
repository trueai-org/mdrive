```csharp
// 使用 LZ4 共享实例完成压缩任务
// 创建一个新的流接收压缩 Compress 的数据
//using (FileStream fileStream = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read))
//using (MemoryStream compressedStream = new MemoryStream())
//{
//    blockStartIndex = packageStream.Length;

//    // 将文件流压缩到内存流

//    LZ4Compressor.Shared.Compress(fileStream, compressedStream);
//    compressedStream.Position = 0;
//    compressedStream.CopyTo(packageStream);

//    blockSize = compressedStream.Length;
//    blockEndIndex = packageStream.Length - 1;
//}

```

## 加密

- 使用 `sqlite-net-sqlcipher` 加密数据库后，非常慢，只有不加密的 1/10 速度

```

//// 设置数据库连接字符串
//var connectionString = $"Data Source={dbPath};password={password}";

//using (var connection = new SqliteConnection($"{connectionString}"))
//{
//    connection.Open();

//    using (var command = new SqliteCommand("PRAGMA key = '" + password + "';", connection))
//    {
//        command.ExecuteNonQuery();
//    }

//    using (var command = new SqliteCommand("CREATE TABLE IF NOT EXISTS MyTable (Id INTEGER PRIMARY KEY, Name TEXT)", connection))
//    {
//        command.ExecuteNonQuery();
//    }

//    //using (var command = connection.CreateCommand())
//    //{
//    //    command.CommandText = "PRAGMA key = @password;";
//    //    command.Parameters.AddWithValue("@password", password);
//    //    command.ExecuteNonQuery();
//    //}
//}

using SQLite;
using System.Linq.Expressions;

namespace MDriveSync.Security
{
    /// <summary>
    /// SQLite 数据库的泛型仓库类
    /// </summary>
    /// <typeparam name="T">数据实体类型。</typeparam>
    public class SqliteRepository<T> where T : class, IBaseId, new()
    {
        private static readonly object _lock = new();
        private readonly SQLiteConnection _db;

        /// <summary>
        /// 创建 sqlite
        /// </summary>
        /// <param name="dbName">数据库名称，例如：log.db</param>
        /// <param name="password">数据库密码</param>
        public SqliteRepository(string dbName, string password)
        {
            var dbPath = Path.Combine(Directory.GetCurrentDirectory(), dbName);
            lock (_lock)
            {
                if (!Directory.Exists(Path.GetDirectoryName(dbPath)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dbPath));
                }
            }

            var options = new SQLiteConnectionString(dbPath, true, key: password);
            _db = new SQLiteConnection(options);
            _db.CreateTable<T>();

            // 设置缓存大小
            _db.Execute("PRAGMA cache_size = 10000;");

            // 设置同步模式
            _db.Execute("PRAGMA synchronous = NORMAL;");
        }

        // 增加
        public void Add(T entity)
        {
            lock (_lock)
            {
                entity.Id = _db.Insert(entity);
            }
        }

        // 批量增加，优化处理超过 10000 条记录的情况
        public void AddRange(IEnumerable<T> entities)
        {
            const int batchSize = 10000;
            var entityList = entities.ToList();

            lock (_lock)
            {
                for (int i = 0; i < entityList.Count; i += batchSize)
                {
                    var batch = entityList.Skip(i).Take(batchSize);
                    _db.InsertAll(batch);
                }
            }
        }

        // 删除
        public void Delete(int id)
        {
            lock (_lock)
            {
                var entity = _db.Find<T>(id);
                if (entity != null)
                {
                    _db.Delete(entity);
                }
            }
        }

        public void Delete(T obj)
        {
            lock (_lock)
            {
                _db.Delete(obj);
            }
        }

        public int Delete(Expression<Func<T, bool>> predicate)
        {
            lock (_lock)
            {
                var items = _db.Table<T>().Where(predicate).ToList();
                var count = 0;
                foreach (var item in items)
                {
                    _db.Delete(item);
                    count++;
                }
                return count;
            }
        }

        // 修改
        public void Update(T entity)
        {
            lock (_lock)
            {
                _db.Update(entity);
            }
        }

        // 查询所有
        public List<T> GetAll()
        {
            lock (_lock)
            {
                return _db.Table<T>().ToList();
            }
        }

        // 根据ID查询
        public T Get(int id)
        {
            lock (_lock)
            {
                return _db.Find<T>(id);
            }
        }

        // 查询
        // 注意 lambda 表达式不支持部分运算，包括 + ...
        public List<T> Where(Expression<Func<T, bool>> predicate)
        {
            lock (_lock)
            {
                return _db.Table<T>().Where(predicate).ToList();
            }
        }

        public TableQuery<T> Query(Expression<Func<T, bool>> predicate)
        {
            return _db.Table<T>().Where(predicate);
        }

        // 注意 lambda 表达式不支持部分运算
        public T Single(Expression<Func<T, bool>> predicate)
        {
            lock (_lock)
            {
                return _db.Table<T>().Where(predicate).FirstOrDefault();
            }
        }

        public bool Any(Expression<Func<T, bool>> predicate)
        {
            lock (_lock)
            {
                return _db.Table<T>().Count(predicate) > 0;
            }
        }

        public long Count(Expression<Func<T, bool>> predicate)
        {
            lock (_lock)
            {
                return _db.Table<T>().Count(predicate);
            }
        }

        public void Compact()
        {
            lock (_lock)
            {
                _db.Execute("VACUUM");
            }
        }
    }
}
```