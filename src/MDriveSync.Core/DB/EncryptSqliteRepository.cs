using FastMember;
using SQLite;
using System.Linq.Expressions;
using System.Runtime.Caching;

namespace MDriveSync.Core.DB
{
    public class EncryptSqliteRepository<T> : EncryptSqliteRepository<T, int> where T : IBaseKey<int>, new()
    {
        /// <inheritdoc />
        public EncryptSqliteRepository(string dbName, bool? noCache) : base(dbName, null, noCache)
        {
        }

        /// <inheritdoc />
        public EncryptSqliteRepository(string dbName, string subPath = null, bool? noCache = null)
            : base(dbName, subPath, noCache)
        {
        }

        public EncryptSqliteRepository(string dbName, string password = null, string subPath = null, bool? noCache = null)
            : base(dbName, password, subPath, noCache)
        {
        }
    }

    /// <summary>
    /// SQLite 数据库的泛型仓库类。
    /// 提供对指定类型的基本 CRUD 操作，并支持缓存功能。
    /// </summary>
    /// <typeparam name="T">数据实体类型。</typeparam>
    /// <typeparam name="TId">实体的标识类型。</typeparam>
    public class EncryptSqliteRepository<T, TId> where T : IBaseKey<TId>, new()
    {
        private static readonly object _lock = new object();

        private readonly string _dbPath;
        private readonly string _password;
        private readonly MemoryCache _cache = new(typeof(T).Name);
        private readonly string _cacheKey = typeof(T).Name;
        private readonly bool? _useCache;

        private readonly TypeAccessor _accessor = TypeAccessor.Create(typeof(T));
        private readonly MemberSet _members;

        /// <summary>
        /// 创建 sqlite
        /// </summary>
        /// <param name="dbName">数据库名称，例如：log.db</param>
        /// <param name="subPath">子路径</param>
        /// <param name="noCache">是否使用缓存</param>
        public EncryptSqliteRepository(string dbName, string subPath = null, bool? noCache = null)
            : this(dbName, null, subPath, noCache)
        {
        }

        /// <summary>
        /// 创建加密的 SQLite 数据库
        /// </summary>
        /// <param name="dbName">数据库名称</param>
        /// <param name="password">数据库密码</param>
        /// <param name="subPath">子路径</param>
        /// <param name="noCache">是否使用缓存</param>
        public EncryptSqliteRepository(string dbName, string password = null, string subPath = null, bool? noCache = null)
        {
            _useCache = noCache;
            _members = _accessor.GetMembers();
            _password = password;

            _dbPath = Path.Combine(Directory.GetCurrentDirectory(), "data", subPath ?? "", dbName);

            lock (_lock)
            {
                var directory = Path.GetDirectoryName(_dbPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }

            // 初始化数据库和表
            InitializeDatabase();
        }

        /// <summary>
        /// 初始化数据库连接和表结构
        /// </summary>
        private void InitializeDatabase()
        {
            try
            {
                using var db = GetConnection();
                db.CreateTable<T>();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"初始化数据库失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 获取数据库连接
        /// </summary>
        /// <returns></returns>
        private SQLiteConnection GetConnection()
        {
            //var options = new SQLiteConnectionString(_dbPath, true, key: _password);
            //var encryptedDb = new SQLiteAsyncConnection(options);
            //return encryptedDb.GetConnection();

            // 最简化的异步连接配置
            var options = new SQLiteConnectionString(_dbPath, true, key: _password);
            return new SQLiteConnection(options);



            //var options = SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache;

            //var connection = new SQLiteConnection(_dbPath, options, !string.IsNullOrEmpty(_password));

            //if (!string.IsNullOrEmpty(_password))
            //{
            //    // 设置加密密码
            //    connection.Execute("PRAGMA key = ?", _password);
            //}

            //return connection;
        }

        // 增加
        public void Add(T entity)
        {
            using var db = GetConnection();
            db.Insert(entity);
            AddToCache(entity);
        }

        // 批量增加，优化处理超过 10000 条记录的情况
        public void AddRange(IEnumerable<T> entities)
        {
            const int batchSize = 10000;
            var entityList = entities.ToList();

            using var db = GetConnection();

            // 分批处理实体
            for (int i = 0; i < entityList.Count; i += batchSize)
            {
                var batch = entityList.Skip(i).Take(batchSize);
                db.InsertAll(batch);

                // 更新缓存
                if (_useCache == true)
                {
                    var items = GetCachedDataOrNull();
                    if (items != null)
                    {
                        items.AddRange(batch);
                        UpdateCache(items);
                    }
                }
            }
        }

        // 批量更新，每批次最多更新1000条记录
        public void UpdateRange(IEnumerable<T> entities)
        {
            const int batchSize = 1000;
            var entityList = entities.ToList();

            using var db = GetConnection();

            // 分批处理实体
            for (int i = 0; i < entityList.Count; i += batchSize)
            {
                var batch = entityList.Skip(i).Take(batchSize).ToList();
                db.UpdateAll(batch);

                // 更新缓存
                if (_useCache == true)
                {
                    var items = GetCachedDataOrNull();
                    if (items != null)
                    {
                        foreach (var entity in batch)
                        {
                            var itemToUpdate = items.FirstOrDefault(item => item.Key.Equals(entity.Key));
                            if (itemToUpdate != null)
                            {
                                items.Remove(itemToUpdate);
                            }
                            items.Add(entity);
                        }
                        UpdateCache(items);
                    }
                }
            }
        }

        // 删除
        public void Delete(TId id)
        {
            using var db = GetConnection();
            db.Delete<T>(id);
            RemoveFromCache(id);
        }

        // 批量删除，每批次最多删除1000条记录
        public void DeleteRange(IEnumerable<TId> ids)
        {
            const int batchSize = 1000;
            var idList = ids.ToList();

            using var db = GetConnection();

            for (int i = 0; i < idList.Count; i += batchSize)
            {
                var batch = idList.Skip(i).Take(batchSize);
                foreach (var id in batch)
                {
                    db.Delete<T>(id);
                }
            }

            // 更新缓存
            if (_useCache == true)
            {
                var items = GetCachedDataOrNull();
                if (items != null)
                {
                    items.RemoveAll(item => idList.Contains(item.Key));
                    UpdateCache(items);
                }
            }
        }

        // 修改
        public void Update(T entity)
        {
            using var db = GetConnection();
            db.Update(entity);
            UpdateCache(entity);
        }

        // 查询所有
        public List<T> GetAll(bool? useCache = null)
        {
            if (useCache == null)
                useCache = _useCache;

            if (useCache == true)
            {
                return GetCachedData();
            }
            else
            {
                using var db = GetConnection();
                return db.Table<T>().ToList();
            }
        }

        // 查询
        public T Get(TId id)
        {
            using var db = GetConnection();
            return db.Get<T>(id);
        }

        /// <summary>
        /// 获取单个满足条件的实体。
        /// </summary>
        /// <param name="predicate">查询条件表达式。</param>
        /// <returns>满足条件的单个实体。</returns>
        public T Single(Expression<Func<T, bool>> predicate)
        {
            using var db = GetConnection();
            return db.Table<T>().Where(predicate).Single();
        }

        /// <summary>
        /// 获取第一个满足条件的实体，如果没有找到则返回 null。
        /// </summary>
        /// <param name="predicate">查询条件表达式。</param>
        /// <returns>满足条件的第一个实体或 null。</returns>
        public T FirstOrDefault(Expression<Func<T, bool>> predicate)
        {
            using var db = GetConnection();
            return db.Table<T>().Where(predicate).FirstOrDefault();
        }

        /// <summary>
        /// 获取满足条件的所有实体。
        /// </summary>
        /// <param name="predicate">查询条件表达式。</param>
        /// <returns>满足条件的实体列表。</returns>
        public List<T> Where(Expression<Func<T, bool>> predicate)
        {
            using var db = GetConnection();
            return db.Table<T>().Where(predicate).ToList();
        }

        /// <summary>
        /// 获取实体数量。
        /// </summary>
        /// <returns>实体总数。</returns>
        public int Count()
        {
            using var db = GetConnection();
            return db.Table<T>().Count();
        }

        /// <summary>
        /// 获取满足条件的实体数量。
        /// </summary>
        /// <param name="predicate">查询条件表达式。</param>
        /// <returns>满足条件的实体数量。</returns>
        public int Count(Expression<Func<T, bool>> predicate)
        {
            using var db = GetConnection();
            return db.Table<T>().Count(predicate);
        }

        private List<T> GetCachedDataOrNull()
        {
            if (_cache.Contains(_cacheKey))
            {
                return GetCachedData();
            }
            return null;
        }

        private List<T> GetCachedData()
        {
            if (!_cache.Contains(_cacheKey))
            {
                var items = GetAll(false);
                UpdateCache(items);
            }
            return (List<T>)_cache.Get(_cacheKey);
        }

        private void AddToCache(T entity)
        {
            var items = GetCachedDataOrNull();
            if (items != null)
            {
                items.Add(entity);
                UpdateCache(items);
            }
        }

        private void RemoveFromCache(TId id)
        {
            var items = GetCachedDataOrNull();
            if (items != null)
            {
                var itemToRemove = items.FirstOrDefault(item => item.Key.Equals(id));
                if (itemToRemove != null)
                {
                    items.Remove(itemToRemove);
                    UpdateCache(items);
                }
            }
        }

        private void UpdateCache(T entity)
        {
            var items = GetCachedDataOrNull();
            if (items != null)
            {
                var itemToUpdate = items.FirstOrDefault(item => item.Key.Equals(entity.Key));
                if (itemToUpdate != null)
                {
                    items.Remove(itemToUpdate);
                }
                items.Add(entity);
                UpdateCache(items);
            }
        }

        private void UpdateCache(List<T> items)
        {
            _cache.Set(_cacheKey, items, ObjectCache.InfiniteAbsoluteExpiration);
        }

        /// <summary>
        /// 反射比较两个对象的所有属性是否相等
        /// 性能略低，每秒 1200万+
        /// </summary>
        /// <param name="obj1"></param>
        /// <param name="obj2"></param>
        /// <returns></returns>
        public bool AreObjectsEqual(T obj1, T obj2)
        {
            if (ReferenceEquals(obj1, obj2))
                return true;

            if (obj1 == null || obj2 == null)
                return false;

            // 获取对象类型
            var type = typeof(T);

            // 比较每个属性的值
            foreach (var prop in type.GetProperties())
            {
                var val1 = prop.GetValue(obj1);
                var val2 = prop.GetValue(obj2);

                if (!Equals(val1, val2))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 快速比较 2 个对象
        /// 比反射更快，每秒 3600万+
        /// </summary>
        /// <param name="obj1"></param>
        /// <param name="obj2"></param>
        /// <returns></returns>
        public bool FastAreObjectsEqual(T obj1, T obj2)
        {
            if (ReferenceEquals(obj1, obj2))
                return true;

            if (obj1 == null || obj2 == null)
                return false;

            foreach (var member in _members)
            {
                if (!Equals(_accessor[obj1, member.Name], _accessor[obj2, member.Name]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _cache?.Dispose();
        }
    }
}