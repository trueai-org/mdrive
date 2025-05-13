namespace MDriveSync.Security.Models
{
    /// <summary>
    /// MD5、SHA1、SHA256、SHA3/SHA384、SHA512、BLAKE3、XXH3、XXH128
    /// </summary>
    public enum EHashType
    {
        /// <summary>
        /// MD5
        /// </summary>
        MD5,

        /// <summary>
        /// SHA1
        /// </summary>
        SHA1,

        /// <summary>
        /// SHA256
        /// </summary>
        SHA256,

        /// <summary>
        /// SHA512
        /// </summary>
        SHA512,

        /// <summary>
        /// SHA3/SHA384
        /// </summary>
        SHA3,

        /// <summary>
        /// SHA384
        /// </summary>
        SHA384,

        /// <summary>
        /// BLAKE3
        /// </summary>
        BLAKE3,

        /// <summary>
        /// XXH3
        /// </summary>
        XXH3,

        /// <summary>
        /// XXH128
        /// </summary>
        XXH128,
    }
}