using LiteDB;
using MDriveSync.Security.Models;
using System.Linq.Expressions;

namespace MDriveSync.Security
{
    /// <summary>
    /// LiteDB 数据库的泛型仓库类。
    /// </summary>
    /// <typeparam name="T">数据实体类型。</typeparam>
    /// <typeparam name="TId">实体的标识类型。</typeparam>
    public class LiteRepository<T> : IRepository<T> where T : IBaseId, new()
    {
        private static readonly object _lock = new();
        private readonly LiteDatabase _db;

        /// <summary>
        /// 创建 LiteDB 数据库的实例。
        /// </summary>
        /// <param name="dbName">数据库名称</param>
        public LiteRepository(string dbName, string password)
        {
            var dbPath = Path.Combine(Directory.GetCurrentDirectory(), dbName);

            lock (_lock)
            {
                if (!Directory.Exists(Path.GetDirectoryName(dbPath)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dbPath));
                }
            }

            var connectionString = $"Filename={dbPath};Connection=shared;";
            if (!string.IsNullOrEmpty(password))
            {
                connectionString += $"Password={password};";
            }

            _db = new LiteDatabase(connectionString);

            // 会自动创建索引，不需要手动创建
            //_db.GetCollection<T>().EnsureIndex(x => x.Id);
        }

        // 初始化索引等
        public void Init()
        {
            _db.GetCollection<RootFileset>().EnsureIndex(x => x.RootPackageId);
            _db.GetCollection<RootFileset>().EnsureIndex(x => x.FilesetSourceKey);

            _db.GetCollection<RootPackage>().EnsureIndex(x => x.Key);
        }

        // 增加
        public void Add(T entity)
        {
            var col = _db.GetCollection<T>();
            entity.Id = col.Insert(entity);
        }

        // 批量增加，优化处理超过 10000 条记录的情况
        public void AddRange(IEnumerable<T> entities)
        {
            const int batchSize = 10000;
            var col = _db.GetCollection<T>();

            // 分批处理实体
            var entityList = entities.ToList();
            for (int i = 0; i < entityList.Count; i += batchSize)
            {
                var batch = entityList.Skip(i).Take(batchSize);
                col.InsertBulk(batch);
            }
        }

        // 删除
        public void Delete(int id)
        {
            var col = _db.GetCollection<T>();
            col.Delete(new BsonValue(id));
        }

        public void Delete(T obj)
        {
            var col = _db.GetCollection<T>();
            col.Delete(new BsonValue(obj.Id));
        }

        public int Delete(Expression<Func<T, bool>> predicate)
        {
            return _db.GetCollection<T>().DeleteMany(predicate);
        }

        // 修改
        public void Update(T entity)
        {
            var col = _db.GetCollection<T>();
            col.Update(entity);
        }

        // 查询所有
        public List<T> GetAll()
        {
            return _db.GetCollection<T>().FindAll().ToList();
        }

        // 根据ID查询
        public T Get(int id)
        {
            return _db.GetCollection<T>().FindById(new BsonValue(id));
        }

        // 查询
        public List<T> Where(Expression<Func<T, bool>> predicate)
        {
            return _db.GetCollection<T>().Find(predicate).ToList();
        }

        public List<T> Where(Expression<Func<T, bool>> filter = null, Expression<Func<T, object>> orderBy = null, bool orderByAsc = true)
        {
            var query = _db.GetCollection<T>().Query();
            if (filter != null)
            {
                query = query.Where(filter);
            }

            if (orderBy != null)
            {
                var orderByField = orderBy;
                query = orderByAsc ? query.OrderBy(orderByField) : query.OrderByDescending(orderByField);
            }

            return query.ToList();
        }

        public T Single(Expression<Func<T, bool>> predicate)
        {
            return _db.GetCollection<T>().FindOne(predicate);
        }

        public T Single(Expression<Func<T, bool>> filter = null, Expression<Func<T, object>> orderBy = null, bool orderByAsc = true)
        {
            var query = _db.GetCollection<T>().Query();
            if (filter != null)
            {
                query = query.Where(filter);
            }

            if (orderBy != null)
            {
                query = orderByAsc ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
            }

            return query.FirstOrDefault();
        }


        public bool Any(Expression<Func<T, bool>> predicate)
        {
            return _db.GetCollection<T>().Exists(predicate);
        }

        public long Count(Expression<Func<T, bool>> predicate)
        {
            return _db.GetCollection<T>().Count(predicate);
        }

        public void Compact()
        {
            _db.Rebuild();
        }
    }
}