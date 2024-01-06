namespace MDriveSync.Core.IO
{
    public static partial class Extensions
    {
        /// <summary>
        /// Extension method for ISystemIO which determines whether the given path is a symlink.
        /// </summary>
        /// <param name="systemIO">ISystemIO implementation</param>
        /// <param name="path">File or folder path</param>
        /// <returns>Whether the path is a symlink</returns>
        public static bool IsSymlink(this ISystemIO systemIO, string path)
        {
            return systemIO.IsSymlink(path, systemIO.GetFileAttributes(path));
        }

        /// <summary>
        /// Extension method for ISystemIO which determines whether the given path is a symlink.
        /// </summary>
        /// <param name="systemIO">ISystemIO implementation</param>
        /// <param name="path">File or folder path</param>
        /// <param name="attributes">File attributes</param>
        /// <returns>Whether the path is a symlink</returns>
        public static bool IsSymlink(this ISystemIO systemIO, string path, FileAttributes attributes)
        {
            // Not all reparse points are symlinks.
            // For example, on Windows 10 Fall Creator's Update, the OneDrive folder (and all subfolders)
            // are reparse points, which allows the folder to hook into the OneDrive service and download things on-demand.
            // If we can't find a symlink target for the current path, we won't treat it as a symlink.
            return (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint && !string.IsNullOrEmpty(systemIO.GetSymlinkTarget(path));
        }

        public static IEnumerable<TSource> Union<TSource>(this IEnumerable<TSource> first, IEnumerable<TSource> second)
        {
            return UnionIterator(first, second, null);
        }

        private static IEnumerable<TSource> UnionIterator<TSource>(IEnumerable<TSource> first, IEnumerable<TSource> second, IEqualityComparer<TSource> comparer)
        {
            var set = comparer != null ? new HashSet<TSource>(comparer) : new HashSet<TSource>();

            foreach (TSource item in first)
            {
                if (set.Add(item))
                {
                    yield return item;
                }
            }

            foreach (TSource item in second)
            {
                if (set.Add(item))
                {
                    yield return item;
                }
            }
        }
    }
}