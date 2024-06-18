using LiteDB;
using MDriveSync.Core.Models;
using ServiceStack;

namespace MDriveSync.Core.DB
{
    public interface IBaseId<T>
    {
        T Id { get; set; }
    }

    /// <summary>
    /// LiteDB 数据库的泛型仓库类。
    /// 使用 LiteDB 作为数据库存储，并利用线程安全的字典来管理缓存。
    /// </summary>
    /// <typeparam name="T">数据实体类型。</typeparam>
    /// <typeparam name="TId">实体的标识类型。</typeparam>
    public class LiteRepository<T, TId> where T : IBaseId<TId>, new()
    {
        private static readonly object _lock = new();
        private readonly LiteDatabase _db;

        /// <summary>
        /// 创建 LiteDB 数据库的实例。
        /// </summary>
        /// <param name="dbName">数据库名称，例如：log.db</param>
        public LiteRepository(string dbName)
        {
            var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "data", dbName);
            lock (_lock)
            {
                if (!Directory.Exists(Path.GetDirectoryName(dbPath)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dbPath));
                }
            }

            var connectionString = $"Filename={dbPath};Connection=shared;";

            _db = new LiteDatabase(connectionString);

            // 设置 litedb 别名
            BsonMapper.Global.ResolveCollectionName = type =>
            {
                if (type == typeof(AliyunStorageConfig))
                {
                    return "AliyunDriveConfig";
                }
                else if (type == typeof(LocalStorageConfig))
                {
                    return "LocalStorageConfig";
                }
                return type.Name;
            };
        }

        // 增加
        public void Add(T entity)
        {
            var col = _db.GetCollection<T>();
            col.Insert(entity);
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
        public void Delete(TId id)
        {
            var col = _db.GetCollection<T>();
            col.Delete(new BsonValue(id));
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
            var col = _db.GetCollection<T>();
            return col.FindAll().ToList();
        }

        // 根据ID查询
        public T Get(TId id)
        {
            var col = _db.GetCollection<T>();
            return col.FindById(new BsonValue(id));
        }
    }
}