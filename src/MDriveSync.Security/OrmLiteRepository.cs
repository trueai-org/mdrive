using FastMember;
using ServiceStack;
using ServiceStack.OrmLite;
using ServiceStack.OrmLite.Legacy;
using System.Data.SQLite;
using System.Linq.Expressions;

namespace MDriveSync.Security
{
    public interface IBaseId
    {
        int Id { get; set; }
    }

    /// <summary>
    /// SQLite 数据库的泛型仓库类。
    /// </summary>
    /// <typeparam name="T">数据实体类型。</typeparam>
    public class OrmLiteRepository<T> : IRepository<T> where T : IBaseId, new()
    {
        private static readonly object _lock = new object();

        private readonly OrmLiteConnectionFactory _dbFactory;

        private readonly TypeAccessor _accessor = TypeAccessor.Create(typeof(T));
        private readonly MemberSet _members;

        /// <summary>
        /// 创建 sqlite
        /// sqlite 模式默认不支持加密文件数据，使用加密太慢了
        /// </summary>
        /// <param name="dbName">数据库名称，例如：log.db</param>
        public OrmLiteRepository(string dbName)
        {
            // 设置日志回调函数为 null，禁用日志
            SQLiteLog.Enabled = false;

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


        // 初始化索引等
        public void Init()
        {

        }

        // 增加
        public void Add(T entity)
        {
            using var db = _dbFactory.Open();
            var obj = db.Insert(entity, selectIdentity: true);
            entity.Id = (int)obj;
        }

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
            }
        }

        // 批量更新，每批次最多更新1000条记录
        public void UpdateRange(IEnumerable<T> entities)
        {
            const int batchSize = 1000;
            using var db = _dbFactory.Open();

            // 分批处理实体
            var entityList = entities.ToList();
            for (int i = 0; i < entityList.Count; i += batchSize)
            {
                var batch = entityList.Skip(i).Take(batchSize).ToList();
                db.UpdateAll(batch);
            }
        }

        // 修改
        public void Update(T entity)
        {
            using var db = _dbFactory.Open();
            db.Update(entity);
        }

        // 查询所有
        public List<T> GetAll(bool? useCache = null)
        {
            using var db = _dbFactory.Open();
            return db.Select<T>();
        }

        // 查询
        public List<T> Where(Expression<Func<T, bool>> predicate)
        {
            using var db = _dbFactory.Open();
            return db.Select<T>(predicate);
        }

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


        public T Single(Expression<Func<T, bool>> predicate)
        {
            using var db = _dbFactory.Open();
            return db.Single<T>(predicate);
        }

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


        public bool Any(Expression<Func<T, bool>> predicate)
        {
            using var db = _dbFactory.Open();
            return db.Count(predicate) > 0;
        }

        public long Count(Expression<Func<T, bool>> predicate)
        {
            using var db = _dbFactory.Open();
            return db.Count(predicate);
        }

        public void Delete(T model)
        {
            using var db = _dbFactory.Open();
            db.Delete(model);
        }

        public int Delete(Expression<Func<T, bool>> predicate)
        {
            using var db = _dbFactory.Open();
            return db.Delete(predicate);
        }

        // 查询
        public T Get(object id)
        {
            using var db = _dbFactory.Open();
            return db.SingleById<T>(id);
        }

        // 收据库收缩/压缩
        public void Compact()
        {
            using var db = _dbFactory.Open();
            db.ExecuteSql("VACUUM");
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
        /// 在事务中执行操作
        /// </summary>
        /// <param name="action">要执行的操作</param>
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

        public void Delete(int id)
        {
            using var db = _dbFactory.Open();
            db.Delete(id);
        }

        public List<T> GetAll()
        {
            using var db = _dbFactory.Open();
            return db.Select<T>().ToList();
        }

        public T Get(int id)
        {
            using var db = _dbFactory.Open();
            return db.SingleById<T>(id);
        }
    }
}