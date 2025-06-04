namespace MDriveSync.Core.DB
{
    public interface IBaseId<T>
    {
        T Id { get; set; }
    }
}