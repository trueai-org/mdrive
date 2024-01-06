namespace MDriveSync.Core.IO
{
    /// <summary>
    /// Represents a single filter
    /// </summary>
    public interface IFilter
    {
        /// <summary>
        /// The sort order
        /// </summary>
        long Order { get; set; }

        /// <summary>
        /// True if the filter includes the items, false if it excludes
        /// </summary>
        bool Include { get; set; }

        /// <summary>
        /// The filter expression.
        /// If the filter is a regular expression, it starts and ends with hard brackets [ ]
        /// </summary>
        string Expression { get; set; }
    }
}