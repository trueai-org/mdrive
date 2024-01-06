namespace MDriveSync.Core.IO
{
    /// <summary>
    /// A single setting
    /// </summary>
    public interface ISetting
    {
        /// <summary>
        /// The filter expression
        /// </summary>
        string Filter { get; }

        /// <summary>
        /// The setting option
        /// </summary>
        string Name { get; set; }

        /// <summary>
        /// The setting value
        /// </summary>
        string Value { get; set; }
    }
}