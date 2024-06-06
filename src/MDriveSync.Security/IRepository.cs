using System.Linq.Expressions;

namespace MDriveSync.Security
{
    /// <summary>
    /// 泛型仓库接口，定义常用的数据操作方法。
    /// </summary>
    /// <typeparam name="T">数据实体类型。</typeparam>
    public interface IRepository<T> where T : IBaseId, new()
    {
        /// <summary>
        /// 初始化仓库（索引等）。
        /// </summary>
        void Init();

        /// <summary>
        /// 添加一个实体到仓库。
        /// </summary>
        /// <param name="entity">要添加的实体。</param>
        void Add(T entity);

        /// <summary>
        /// 批量添加实体到仓库。
        /// </summary>
        /// <param name="entities">要添加的实体集合。</param>
        void AddRange(IEnumerable<T> entities);

        /// <summary>
        /// 根据实体ID删除一个实体。
        /// </summary>
        /// <param name="id">实体的ID。</param>
        void Delete(int id);

        /// <summary>
        /// 删除指定的实体。
        /// </summary>
        /// <param name="obj">要删除的实体对象。</param>
        void Delete(T obj);

        /// <summary>
        /// 根据条件删除实体。
        /// </summary>
        /// <param name="predicate">删除条件表达式。</param>
        /// <returns>删除的实体数量。</returns>
        int Delete(Expression<Func<T, bool>> predicate);

        /// <summary>
        /// 更新指定的实体。
        /// </summary>
        /// <param name="entity">要更新的实体对象。</param>
        void Update(T entity);

        /// <summary>
        /// 获取所有实体。
        /// </summary>
        /// <returns>实体列表。</returns>
        List<T> GetAll();

        /// <summary>
        /// 根据实体ID获取实体。
        /// </summary>
        /// <param name="id">实体的ID。</param>
        /// <returns>对应的实体对象。</returns>
        T Get(int id);

        /// <summary>
        /// 根据条件查询实体。
        /// </summary>
        /// <param name="predicate">查询条件表达式。</param>
        /// <returns>满足条件的实体列表。</returns>
        List<T> Where(Expression<Func<T, bool>> predicate);

        /// <summary>
        /// 根据条件查询实体，并进行排序。
        /// </summary>
        /// <param name="filter">查询条件表达式。</param>
        /// <param name="orderBy">排序字段表达式。</param>
        /// <param name="orderByAsc">是否升序排序。</param>
        /// <returns>满足条件的实体列表。</returns>
        List<T> Where(Expression<Func<T, bool>> filter = null, Expression<Func<T, object>> orderBy = null, bool orderByAsc = true);

        /// <summary>
        /// 获取单个满足条件的实体。
        /// </summary>
        /// <param name="predicate">查询条件表达式。</param>
        /// <returns>满足条件的单个实体。</returns>
        T Single(Expression<Func<T, bool>> predicate);

        /// <summary>
        /// 获取单个满足条件的实体，并进行排序。
        /// </summary>
        /// <param name="filter">查询条件表达式。</param>
        /// <param name="orderBy">排序字段表达式。</param>
        /// <param name="orderByAsc">是否升序排序。</param>
        /// <returns>满足条件的单个实体。</returns>
        T Single(Expression<Func<T, bool>> filter = null, Expression<Func<T, object>> orderBy = null, bool orderByAsc = true);

        /// <summary>
        /// 判断是否存在满足条件的实体。
        /// </summary>
        /// <param name="predicate">查询条件表达式。</param>
        /// <returns>是否存在满足条件的实体。</returns>
        bool Any(Expression<Func<T, bool>> predicate);

        /// <summary>
        /// 获取满足条件的实体数量。
        /// </summary>
        /// <param name="predicate">查询条件表达式。</param>
        /// <returns>满足条件的实体数量。</returns>
        long Count(Expression<Func<T, bool>> predicate);

        /// <summary>
        /// 对数据库进行压缩。
        /// </summary>
        void Compact();
    }
}