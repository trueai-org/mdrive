using ServiceStack.OrmLite;
using System.Runtime.Caching;

namespace MDriveSync.Core.DB
{
    public interface IBaseKey<T>
    {
        T Key { get; set; }
    }

    public class SqliteRepository<T> : SqliteRepository<T, int> where T : IBaseKey<int>, new()
    {
        /// <inheritdoc />
        public SqliteRepository(string dbName, bool? noCache) : base(dbName, noCache)
        {
        }
    }

    /// <summary>
    /// SQLite 数据库的泛型仓库类。
    /// 提供对指定类型的基本 CRUD 操作，并支持缓存功能。
    /// </summary>
    /// <typeparam name="T">数据实体类型。</typeparam>
    /// <typeparam name="TId">实体的标识类型。</typeparam>
    public class SqliteRepository<T, TId> where T : IBaseKey<TId>, new()
    {
        private static readonly object _lock = new object();

        private readonly OrmLiteConnectionFactory _dbFactory;
        private readonly MemoryCache _cache = new MemoryCache(typeof(T).Name);
        private readonly string _cacheKey = typeof(T).Name;
        private readonly bool? _useCache;

        /// <summary>
        /// 创建 sqlite
        /// </summary>
        /// <param name="dbName">数据库名称，例如：log.db</param>
        /// <param name="noCache">是否使用缓存</param>
        public SqliteRepository(string dbName, bool? noCache = null)
        {
            _useCache = noCache;

            var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "db", dbName);
            lock (_lock)
            {
                if (!Directory.Exists(Path.GetDirectoryName(dbPath)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dbPath));
                }
            }

            _dbFactory = new OrmLiteConnectionFactory(dbPath, SqliteDialect.Provider);
            using var db = _dbFactory.Open();
            db.CreateTableIfNotExists<T>();
        }

        // 增加
        public void Add(T entity)
        {
            using var db = _dbFactory.Open();
            db.Insert(entity);
            AddToCache(entity);
        }

        //// 批量增加
        //public void AddRange(IEnumerable<T> entities)
        //{
        //    using var db = _dbFactory.Open();
        //    db.InsertAll(entities);

        //    var items = GetCachedDataOrNull();
        //    if (items != null)
        //    {
        //        items.AddRange(entities);
        //        UpdateCache(items);
        //    }
        //}

        // 批量增加，优化处理超过 10000 条记录的情况
        public void AddRange(IEnumerable<T> entities)
        {
            const int batchSize = 10000;
            using var db = _dbFactory.Open();

            // 分批处理实体
            var entityList = entities.ToList();
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

        // 删除
        public void Delete(TId id)
        {
            using var db = _dbFactory.Open();
            db.DeleteById<T>(id);
            RemoveFromCache(id);
        }

        // 修改
        public void Update(T entity)
        {
            using var db = _dbFactory.Open();
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
                using var db = _dbFactory.Open();
                return db.Select<T>();
            }
        }

        // 查询
        public T Get(TId id)
        {
            using var db = _dbFactory.Open();
            return db.SingleById<T>(id);
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
        /// </summary>
        /// <param name="obj1"></param>
        /// <param name="obj2"></param>
        /// <returns></returns>
        public bool AreObjectsEqual(T obj1, T obj2)
        {
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
    }
}