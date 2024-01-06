namespace MDriveSync.Core.IO
{
    /// <summary>
    /// All settings for a single backup
    /// </summary>
    public interface IBackup
    {
        /// <summary>
        /// The backup ID
        /// </summary>
        string ID { get; set; }

        /// <summary>
        /// The backup name
        /// </summary>
        string Name { get; set; }

        /// <summary>
        /// The backup description
        /// </summary>
        string Description { get; set; }

        /// <summary>
        /// The backup tags
        /// </summary>
        string[] Tags { get; set; }

        /// <summary>
        /// The backup target url
        /// </summary>
        string TargetURL { get; set; }

        /// <summary>
        /// The path to the local database
        /// </summary>
        string DBPath { get; }

        /// <summary>
        /// The backup source folders and files
        /// </summary>
        string[] Sources { get; set; }

        /// <summary>
        /// The backup settings
        /// </summary>
        ISetting[] Settings { get; set; }

        /// <summary>
        /// The filters applied to the source files
        /// </summary>
        IFilter[] Filters { get; set; }

        /// <summary>
        /// The backup metadata
        /// </summary>
        IDictionary<string, string> Metadata { get; set; }

        /// <summary>
        /// Gets a value indicating if this instance is not persisted to the database
        /// </summary>
        bool IsTemporary { get; }

        void SanitizeTargetUrl();

        void SanitizeSettings();
    }
}