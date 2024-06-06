using FastMember;
using ServiceStack.OrmLite;
using System.Linq.Expressions;

namespace MDriveSync.Security
{
    /// <summary>
    /// SQLite 数据库的泛型仓库类。
    /// </summary>
    /// <typeparam name="T">数据实体类型。</typeparam>
    public class OrmLiteRepository<T> : IRepository<T> where T : IBaseId, new()
    {
        private static readonly object _lock = new();
        private readonly OrmLiteConnectionFactory _dbFactory;
        private readonly TypeAccessor _accessor = TypeAccessor.Create(typeof(T));
        private readonly MemberSet _members;

        /// <summary>
        /// 创建 SQLite 数据库的实例。
        /// SQLite 模式默认不支持加密文件数据，使用加密太慢了。
        /// </summary>
        /// <param name="dbName">数据库名称，例如：log.db</param>
        public OrmLiteRepository(string dbName)
        {
            _members = _accessor.GetMembers();

            var dbPath = Path.Combine(Directory.GetCurrentDirectory(), dbName);
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

        /// <summary>
        /// 初始化仓库，创建必要的索引。
        /// </summary>
        public void Init()
        {
            // 初始化索引等
        }

        /// <summary>
        /// 添加一个实体到仓库。
        /// </summary>
        /// <param name="entity">要添加的实体。</param>
        public void Add(T entity)
        {
            using var db = _dbFactory.Open();
            var obj = db.Insert(entity, selectIdentity: true);
            entity.Id = (int)obj;
        }

        /// <summary>
        /// 批量添加实体到仓库，优化处理超过 10000 条记录的情况。
        /// </summary>
        /// <param name="entities">要添加的实体集合。</param>
        public void AddRange(IEnumerable<T> entities)
        {
            const int batchSize = 10000;
            using var db = _dbFactory.Open();

            var entityList = entities.ToList();
            for (int i = 0; i < entityList.Count; i += batchSize)
            {
                var batch = entityList.Skip(i).Take(batchSize);
                db.InsertAll(batch);
            }
        }

        /// <summary>
        /// 更新指定的实体。
        /// </summary>
        /// <param name="entity">要更新的实体对象。</param>
        public void Update(T entity)
        {
            using var db = _dbFactory.Open();
            db.Update(entity);
        }

        /// <summary>
        /// 根据条件查询实体。
        /// </summary>
        /// <param name="predicate">查询条件表达式。</param>
        /// <returns>满足条件的实体列表。</returns>
        public List<T> Where(Expression<Func<T, bool>> predicate)
        {
            using var db = _dbFactory.Open();
            return db.Select(predicate);
        }

        /// <summary>
        /// 根据条件查询实体，并进行排序。
        /// </summary>
        /// <param name="filter">查询条件表达式。</param>
        /// <param name="orderBy">排序字段表达式。</param>
        /// <param name="orderByAsc">是否升序排序。</param>
        /// <returns>满足条件的实体列表。</returns>
        public List<T> Where(Expression<Func<T, bool>> filter = null, Expression<Func<T, object>> orderBy = null, bool orderByAsc = true)
        {
            using var db = _dbFactory.Open();
            var query = db.From<T>();
            if (filter != null)
            {
                query = query.Where(filter);
            }

            if (orderBy != null)
            {
                query = orderByAsc ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
            }

            return db.Select(query).ToList();
        }

        /// <summary>
        /// 获取单个满足条件的实体。
        /// </summary>
        /// <param name="predicate">查询条件表达式。</param>
        /// <returns>满足条件的单个实体。</returns>
        public T Single(Expression<Func<T, bool>> predicate)
        {
            using var db = _dbFactory.Open();
            return db.Single(predicate);
        }

        /// <summary>
        /// 获取单个满足条件的实体，并进行排序。
        /// </summary>
        /// <param name="filter">查询条件表达式。</param>
        /// <param name="orderBy">排序字段表达式。</param>
        /// <param name="orderByAsc">是否升序排序。</param>
        /// <returns>满足条件的单个实体。</returns>
        public T Single(Expression<Func<T, bool>> filter = null, Expression<Func<T, object>> orderBy = null, bool orderByAsc = true)
        {
            using var db = _dbFactory.Open();
            var query = db.From<T>();
            if (filter != null)
            {
                query = query.Where(filter);
            }

            if (orderBy != null)
            {
                query = orderByAsc ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
            }

            return db.Single(query);
        }

        /// <summary>
        /// 判断是否存在满足条件的实体。
        /// </summary>
        /// <param name="predicate">查询条件表达式。</param>
        /// <returns>是否存在满足条件的实体。</returns>
        public bool Any(Expression<Func<T, bool>> predicate)
        {
            using var db = _dbFactory.Open();
            return db.Count(predicate) > 0;
        }

        /// <summary>
        /// 获取满足条件的实体数量。
        /// </summary>
        /// <param name="predicate">查询条件表达式。</param>
        /// <returns>满足条件的实体数量。</returns>
        public long Count(Expression<Func<T, bool>> predicate)
        {
            using var db = _dbFactory.Open();
            return db.Count(predicate);
        }

        /// <summary>
        /// 删除指定的实体。
        /// </summary>
        /// <param name="model">要删除的实体对象。</param>
        public void Delete(T model)
        {
            using var db = _dbFactory.Open();
            db.Delete(model);
        }

        /// <summary>
        /// 根据条件删除实体。
        /// </summary>
        /// <param name="predicate">删除条件表达式。</param>
        /// <returns>删除的实体数量。</returns>
        public int Delete(Expression<Func<T, bool>> predicate)
        {
            using var db = _dbFactory.Open();
            return db.Delete(predicate);
        }

        /// <summary>
        /// 根据实体ID删除一个实体。
        /// </summary>
        /// <param name="id">实体的ID。</param>
        public void Delete(int id)
        {
            using var db = _dbFactory.Open();
            db.DeleteById<T>(id);
        }

        /// <summary>
        /// 获取所有实体。
        /// </summary>
        /// <returns>实体列表。</returns>
        public List<T> GetAll()
        {
            using var db = _dbFactory.Open();
            return db.Select<T>().ToList();
        }

        /// <summary>
        /// 根据实体ID获取实体。
        /// </summary>
        /// <param name="id">实体的ID。</param>
        /// <returns>对应的实体对象。</returns>
        public T Get(int id)
        {
            using var db = _dbFactory.Open();
            return db.SingleById<T>(id);
        }

        /// <summary>
        /// 批量更新实体，每批次最多更新 1000 条记录。
        /// </summary>
        /// <param name="entities">要更新的实体集合。</param>
        public void UpdateRange(IEnumerable<T> entities)
        {
            const int batchSize = 1000;
            using var db = _dbFactory.Open();

            var entityList = entities.ToList();
            for (int i = 0; i < entityList.Count; i += batchSize)
            {
                var batch = entityList.Skip(i).Take(batchSize).ToList();
                db.UpdateAll(batch);
            }
        }

        /// <summary>
        /// 反射比较两个对象的所有属性是否相等。性能略低，每秒 1200万+。
        /// </summary>
        /// <param name="obj1">第一个对象。</param>
        /// <param name="obj2">第二个对象。</param>
        /// <returns>如果所有属性相等，则为 true；否则为 false。</returns>
        public bool AreObjectsEqual(T obj1, T obj2)
        {
            if (ReferenceEquals(obj1, obj2))
                return true;

            if (obj1 == null || obj2 == null)
                return false;

            var type = typeof(T);

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
        /// 快速比较两个对象。比反射更快，每秒 3600万+。
        /// </summary>
        /// <param name="obj1">第一个对象。</param>
        /// <param name="obj2">第二个对象。</param>
        /// <returns>如果所有属性相等，则为 true；否则为 false。</returns>
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
        /// 在事务中执行操作。
        /// </summary>
        /// <param name="action">要执行的操作。</param>
        public void Transaction(Action action)
        {
            using var db = _dbFactory.Open();
            using var transaction = db.OpenTransaction();
            try
            {
                action();
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        /// <summary>
        /// 对数据库进行压缩。
        /// </summary>
        public void Compact()
        {
            using var db = _dbFactory.Open();
            db.ExecuteSql("VACUUM");
        }
    }
}