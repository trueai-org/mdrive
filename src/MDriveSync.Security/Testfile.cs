using MDriveSync.Security.Models;
using ServiceStack;
using ServiceStack.OrmLite;
using ServiceStack.Text;

namespace MDriveSync.Security
{
    public class Testfile
    {
        /// <summary>
        /// 默认包大小（用于首次打包计算，必须 > 10MB 且 < 100MB）
        /// </summary>
        private const long PACKAGE_SIZE = 16 * 1024 * 1024;

        /// <summary>
        /// 最大包大小（当包中的文件增长超出时，将增长的文件加入新的包，当前包进行收缩，保证当前包的最大大小）
        /// </summary>
        private const long MAX_PACKAGE_SIZE = 32 * 1024 * 1024;

        /// <summary>
        /// 根目录包索引
        /// </summary>
        private static IRepository<RootPackage> _rootPackageDb;

        /// <summary>
        /// 根目录文件索引
        /// </summary>
        private static IRepository<RootFileset> _rootFilesetDb;

        /// <summary>
        /// 根目录
        /// </summary>
        private static string _baseDir = "E:\\_test";

        /// <summary>
        /// 开始备份
        /// </summary>
        public static void RunBackup()
        {
            var pwd = "";
            _rootPackageDb = GetRepository<RootPackage>(Path.Combine(_baseDir, "root.d"), pwd);
            _rootFilesetDb = GetRepository<RootFileset>(Path.Combine(_baseDir, "root.d"), pwd);

            _rootFilesetDb.Init();

            var files = Directory.GetFiles("E:\\_test\\imgs", "*.*", SearchOption.AllDirectories);
            var count = files.Length;
            var i = 0;

            foreach (var item in files)
            {
                // 压缩测试
                //ProcessFile(item, "SHA256", "LZ4");

                // 加密测试
                ProcessFile(item, "BLAKE3", "Zstd", "ChaCha20-Poly1305", pwd);

                i++;
                Console.WriteLine($"{i}/{count}, {item}");
            }

            // 执行数据库压缩
            _rootPackageDb.Compact();
        }

        /// <summary>
        /// 开始还原
        /// </summary>
        public static void RunRestore()
        {
            var pwd = "123";

            _rootPackageDb = GetRepository<RootPackage>(Path.Combine(_baseDir, "root.d"), pwd);
            _rootFilesetDb = GetRepository<RootFileset>(Path.Combine(_baseDir, "root.d"), pwd);

            // 还原目录
            var restoreRootDir = "E:\\_test\\imgs_restore";
            Directory.CreateDirectory(restoreRootDir); // 确保还原目录存在

            // 获取所有的包和文件
            var pkgs = _rootPackageDb.GetAll();

            var count = pkgs.Count;
            var i = 0;

            foreach (var pkg in pkgs)
            {
                // 包还原到指定目录
                // 获取包路径
                var packagePath = Path.Combine(_baseDir, pkg.Key);
                var packageDbPath = Path.Combine(packagePath, "0.d");

                var restoreDir = Path.Combine(restoreRootDir, pkg.Key);
                Directory.CreateDirectory(restoreDir);

                RestoreByPackage(packageDbPath, restoreDir, true, false, false, pwd);

                i++;

                Console.WriteLine($"{i}/{count}, {pkg.Key}");
            }
        }

        /// <summary>
        /// 根据单个包信息还原文件（不依赖根文件 root.d ）
        /// </summary>
        /// <param name="packageDbPath">cell 包信息</param>
        /// <param name="restorFileDir">还原文件目录位置，如果目录为空则为原目录</param>
        /// <param name="sameOverwrite">同名覆盖</param>
        /// <param name="sameRename">同名重命名</param>
        /// <param name="sameJump">同名跳过</param>
        public static void RestoreByPackage(string packageDbPath,
            string restorFileDir,
            bool sameOverwrite = false,
            bool sameRename = false,
            bool sameJump = false,
            string encryptionKey = null)
        {
            // 加锁执行
            var lockKey = packageDbPath;
            LocalResourceLock.Lock(lockKey, () =>
            {
                if (File.Exists(packageDbPath))
                {
                    var packagePath = Path.GetDirectoryName(packageDbPath);

                    var blocksetDb = GetRepository<Blockset>(packageDbPath, encryptionKey);
                    var filesetDb = GetRepository<Fileset>(packageDbPath, encryptionKey);

                    var files = filesetDb.GetAll();
                    var blocksets = blocksetDb.GetAll().OrderBy(b => b.Index).ToList();

                    // 循环所有块，并还原到指定目录
                    foreach (var block in blocksets)
                    {
                        var hashAlgorithm = block.HashAlgorithm;
                        var internalCompression = block.InternalCompression;
                        var encryptionType = block.EncryptionAlgorithm;

                        var blockFilePath = Path.Combine(packagePath, $"{block.Index}.f");
                        if (File.Exists(blockFilePath))
                        {
                            var file = files.Single(f => f.Id == block.FilesetId);
                            var sourceKey = file.SourceKey;

                            // 默认还原到原目录
                            var filePartPath = sourceKey + ".part";

                            // 如果指定了还原目录，则还原到指定目录
                            if (!string.IsNullOrWhiteSpace(restorFileDir))
                            {
                                filePartPath = Path.Combine(restorFileDir, Path.GetFileName(sourceKey) + ".part");
                            }

                            var fileDir = Path.GetDirectoryName(filePartPath);
                            if (fileDir != null)
                            {
                                Directory.CreateDirectory(fileDir);
                            }

                            // 这里需要块的文件流位置，然后写入到文件
                            if (block.Index == 0 && block.StartIndex == 0 && block.EndIndex == 0)
                            {
                                // 0 字节文件
                                File.Create(filePartPath).Close();
                            }
                            else
                            {
                                // 当处理第一个块时，直接写入文件
                                if (block.Index == 0)
                                {
                                    using (var fs = new FileStream(filePartPath, FileMode.Create, FileAccess.Write))
                                    {
                                        using (var blockStream = new FileStream(blockFilePath, FileMode.Open, FileAccess.Read))
                                        {
                                            blockStream.Seek(block.StartIndex, SeekOrigin.Begin);
                                            byte[] buffer = new byte[block.Size];
                                            blockStream.Read(buffer, 0, buffer.Length);

                                            // 解压解密 buffer
                                            buffer = CompressionHelper.Decompress(buffer, internalCompression, encryptionType, encryptionKey);

                                            fs.Write(buffer, 0, buffer.Length);
                                        }
                                    }
                                }
                                else
                                {
                                    // 说明文件有多个快，需要追加写入
                                    using (var fs = new FileStream(filePartPath, FileMode.Append, FileAccess.Write))
                                    {
                                        using (var blockStream = new FileStream(blockFilePath, FileMode.Open, FileAccess.Read))
                                        {
                                            blockStream.Seek(0, SeekOrigin.Begin);
                                            byte[] buffer = new byte[block.Size];
                                            blockStream.Read(buffer, 0, buffer.Length);

                                            // 解压解密 buffer
                                            buffer = CompressionHelper.Decompress(buffer, internalCompression, encryptionType, encryptionKey);

                                            fs.Write(buffer, 0, buffer.Length);
                                        }
                                    }

                                    //// 说明文件有多个块，需要覆盖写入，而不是追加写入
                                    //using (var fs = new FileStream(filePartPath, FileMode.Open, FileAccess.Write))
                                    //{
                                    //    using (var blockStream = new FileStream(blockFilePath, FileMode.Open, FileAccess.Read))
                                    //    {
                                    //        blockStream.Seek(0, SeekOrigin.Begin);
                                    //        byte[] buffer = new byte[block.Size];
                                    //        blockStream.Read(buffer, 0, buffer.Length);

                                    //        // 解压解密 buffer
                                    //        buffer = CompressionHelper.Decompress(buffer, internalCompression, encryptionType, encryptionKey);

                                    //        // 确保文件指针移动到要覆盖的位置
                                    //        fs.Seek(0, SeekOrigin.Begin);
                                    //        fs.Write(buffer, 0, buffer.Length);
                                    //    }
                                    //}
                                }
                            }
                        }
                    }

                    // 校验所有 .part 文件的 sha
                    // 如果校验通过，则重命名为原文件名
                    // 如果校验不通过，则删除临时 part 文件
                    foreach (var file in files)
                    {
                        var block = blocksets.FirstOrDefault(c => c.FilesetId == file.Id);
                        if (block == null && file.Size == 0)
                        {
                            var sourceKey = file.SourceKey;

                            // 默认还原到原目录
                            var filePartPath = sourceKey + ".part";

                            // 如果指定了还原目录，则还原到指定目录
                            if (!string.IsNullOrWhiteSpace(restorFileDir))
                            {
                                filePartPath = Path.Combine(restorFileDir, Path.GetFileName(sourceKey) + ".part");
                            }

                            Directory.CreateDirectory(Path.GetDirectoryName(filePartPath));

                            // 如果是 0 字节文件，直接创建文件
                            File.Create(filePartPath).Close();

                            // 校验通过，重命名文件
                            var fileNewPath = filePartPath.Substring(0, filePartPath.Length - 5);

                            // 如果文件已经存在，根据选项处理
                            if (File.Exists(fileNewPath))
                            {
                                // 同名跳过
                                if (sameJump)
                                {
                                    continue;
                                }

                                if (sameOverwrite)
                                {
                                    File.Delete(fileNewPath);
                                }
                                else if (sameRename)
                                {
                                    var fileDir = Path.GetDirectoryName(fileNewPath);
                                    var fileName = Path.GetFileNameWithoutExtension(fileNewPath);
                                    var fileExt = Path.GetExtension(fileNewPath);

                                    for (int i = 1; i < int.MaxValue; i++)
                                    {
                                        fileNewPath = Path.Combine(fileDir, $"{fileName} ({i}){fileExt}");
                                        if (!File.Exists(fileNewPath))
                                        {
                                            break;
                                        }
                                    }
                                }
                            }

                            File.Move(filePartPath, fileNewPath, sameOverwrite);

                            // 恢复文件时间
                            var fileInfo = new FileInfo(fileNewPath);
                            fileInfo.CreationTime = DateTimeOffset.FromUnixTimeMilliseconds(file.Created).LocalDateTime;
                            fileInfo.LastWriteTime = DateTimeOffset.FromUnixTimeMilliseconds(file.Updated).LocalDateTime;
                        }
                        else
                        {
                            var hashAlgorithm = block.HashAlgorithm;

                            var sourceKey = file.SourceKey;

                            // 默认还原到原目录
                            var filePartPath = sourceKey + ".part";

                            // 如果指定了还原目录，则还原到指定目录
                            if (!string.IsNullOrWhiteSpace(restorFileDir))
                            {
                                filePartPath = Path.Combine(restorFileDir, Path.GetFileName(sourceKey) + ".part");
                            }

                            if (File.Exists(filePartPath))
                            {
                                var hash = HashHelper.ComputeHashHex(filePartPath, hashAlgorithm);
                                if (hash == file.Hash)
                                {
                                    // 校验通过，重命名文件
                                    var fileNewPath = filePartPath.Substring(0, filePartPath.Length - 5);

                                    // 如果文件已经存在，根据选项处理
                                    if (File.Exists(fileNewPath))
                                    {
                                        // 同名跳过
                                        if (sameJump)
                                        {
                                            continue;
                                        }

                                        if (sameOverwrite)
                                        {
                                            File.Delete(fileNewPath);
                                        }
                                        else if (sameRename)
                                        {
                                            var fileDir = Path.GetDirectoryName(fileNewPath);
                                            var fileName = Path.GetFileNameWithoutExtension(fileNewPath);
                                            var fileExt = Path.GetExtension(fileNewPath);

                                            for (int i = 1; i < int.MaxValue; i++)
                                            {
                                                fileNewPath = Path.Combine(fileDir, $"{fileName} ({i}){fileExt}");
                                                if (!File.Exists(fileNewPath))
                                                {
                                                    break;
                                                }
                                            }
                                        }
                                    }

                                    File.Move(filePartPath, fileNewPath, sameOverwrite);

                                    // 恢复文件时间
                                    var fileInfo = new FileInfo(fileNewPath);
                                    fileInfo.CreationTime = DateTimeOffset.FromUnixTimeMilliseconds(file.Created).LocalDateTime;
                                    fileInfo.LastWriteTime = DateTimeOffset.FromUnixTimeMilliseconds(file.Updated).LocalDateTime;
                                }
                                else
                                {
                                    // 校验不通过，删除临时部分文件
                                    File.Delete(filePartPath);
                                }
                            }
                        }
                    }
                }
            });
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="internalCompression">在将数据存储到仓库之前，数据是否进行压缩，Zstd、LZ4、Snappy</param>
        public static void ProcessFile(string filePath,
            string hashAlgorithm = "SHA256",
            string internalCompression = null,
            string encryptionType = null,
            string encryptionKey = null)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    // 文件被删除了
                    var upath = filePath.ToUrlPath();
                    var rf = _rootFilesetDb.Single(c => c.FilesetSourceKey == upath);
                    if (rf != null)
                    {
                        // 文件 hash 不一致，重新处理
                        // 计算在原本块中增长或收缩文件
                        var package = _rootPackageDb.Get(rf.RootPackageId);
                        if (package != null)
                        {
                            // 多文件包
                            var dbPath = Path.Combine(_baseDir, package.Key, "0.d");
                            if (File.Exists(dbPath))
                            {
                                PackageDeleteFile(package, filePath, encryptionKey);
                            }
                        }
                    }

                    return;
                }

                // 获取文件大小，计算根分类
                var fileInfo = new FileInfo(filePath);
                var fileSize = fileInfo.Length;
                var category = GetRootCategory(fileSize);

                // 0字节文件处理
                if (category == "0" && fileSize == 0)
                {
                    // 如果历史文件不为 0 字节，则删除
                    var upath = fileInfo.FullName.ToUrlPath();
                    var rf = _rootFilesetDb.Single(c => c.FilesetSourceKey == upath);
                    if (rf != null)
                    {
                        if (!string.IsNullOrWhiteSpace(rf.FilesetHash))
                        {
                            // 如果原文件存在 hash，则删除
                            var package = _rootPackageDb.Get(rf.RootPackageId);
                            PackageDeleteFile(package, fileInfo.FullName, encryptionKey);
                        }
                        else
                        {
                            // 0 字节文件没有 hash
                            return;
                        }
                    }

                    // 0 字节文件
                    var pkg = _rootPackageDb.Single(c => c.Category == category);
                    if (pkg == null)
                    {
                        pkg = new RootPackage()
                        {
                            Category = category,
                            Index = 0,
                            Key = category + $"{0 % 256:x2}/{0}",
                            Size = 0,
                            Multifile = true
                        };
                        _rootPackageDb.Add(pkg);
                    }

                    // 加锁执行
                    LocalResourceLock.Lock(pkg.Key, () =>
                    {
                        var pkgPath = Path.Combine(GetPackagePathByIndex(category, 0), $"{0}");
                        Directory.CreateDirectory(pkgPath);

                        var fileSize = fileInfo.Length;

                        var packageDbPath = Path.Combine(_baseDir, pkg.Key, "0.d");

                        // 判断是文件是否处理过
                        var filesetDb = GetRepository<Fileset>(packageDbPath, encryptionKey);

                        var upath = fileInfo.FullName.ToUrlPath();
                        var oldFile = filesetDb.Single(c => c.SourceKey == upath);
                        if (oldFile != null)
                        {
                            if (string.IsNullOrWhiteSpace(oldFile.Hash))
                            {
                                // 处理过返回
                                return;
                            }
                        }

                        var file = new Fileset()
                        {
                            SourceKey = fileInfo.FullName.ToUrlPath(),
                            Created = fileInfo.CreationTime.ToUnixTimeMs(),
                            Updated = fileInfo.LastWriteTime.ToUnixTimeMs(),
                            Size = fileSize,
                            Key = $"{pkg.Key}/0.f"
                        };
                        filesetDb.Add(file);

                        var rfModel = new RootFileset()
                        {
                            RootPackageId = pkg.Id,
                            FilesetSourceKey = fileInfo.FullName.ToUrlPath(),
                            FilesetCreated = fileInfo.CreationTime.ToUnixTimeMs(),
                            FilesetUpdated = fileInfo.LastWriteTime.ToUnixTimeMs(),
                        };
                        _rootFilesetDb.Add(rfModel);
                    });

                    // 处理完毕
                    return;
                }

                // 计算文件 hash
                var hash = HashHelper.ComputeHashHex(filePath, hashAlgorithm);

                var urlPath = fileInfo.FullName.ToUrlPath();
                var rootFile = _rootFilesetDb.Single(c => c.FilesetSourceKey == urlPath);
                if (rootFile != null)
                {
                    if (rootFile.FilesetHash == hash)
                    {
                        return;
                    }
                    else
                    {
                        // 文件 hash 不一致，重新处理
                        // 计算在原本块中增长或收缩文件
                        var package = _rootPackageDb.Get(rootFile.RootPackageId);
                        if (package != null)
                        {
                            if (rootFile.IsShadow)
                            {
                                // 如果是影子文件变更了，则将影子文件移除，作为新的文件处理
                                PackageDeleteFile(package, fileInfo.FullName, encryptionKey);
                            }
                            else
                            {
                                // 说明是0字节文件变更了
                                if (package.Key == "000/0")
                                {
                                    // 删除并收缩包，然后在新的包中处理
                                    // 删除重新处理
                                    PackageDeleteFile(package, fileInfo.FullName, encryptionKey);
                                }
                                else
                                {
                                    if (package.Multifile)
                                    {
                                        // 多文件包
                                        var dbPath = Path.Combine(_baseDir, package.Key, "0.d");
                                        if (File.Exists(dbPath))
                                        {
                                            var blocksetDb = GetRepository<Blockset>(dbPath, encryptionKey);
                                            var fileDb = GetRepository<Fileset>(dbPath, encryptionKey);

                                            var upath = fileInfo.FullName.ToUrlPath();
                                            var fileset = fileDb.Single(c => c.SourceKey == upath);
                                            if (fileset != null)
                                            {
                                                // 判断文件是增长还是收缩
                                                // 如果文件被缩小，或者包大小 + 文件大小 < MAX_PACKAGE_SIZE，则直接还是更新次包
                                                if (fileSize <= fileset.Size || (package.Size - fileset.Size + fileSize) <= MAX_PACKAGE_SIZE)
                                                {
                                                    // 在原本的包基础上进行处理
                                                    PackageDeleteFile(package, fileInfo.FullName, encryptionKey);
                                                    PackageAddFile(package, fileInfo, hash, hashAlgorithm, internalCompression, encryptionType, encryptionKey);

                                                    // 处理完成
                                                    return;
                                                }
                                            }

                                            // 删除并收缩包，然后在新的包中处理
                                            PackageDeleteFile(package, fileInfo.FullName, encryptionKey);
                                        }
                                    }
                                    else
                                    {
                                        // 单文件包，以滚动的方式验证哪个块需要增长或收缩
                                        PackageShrinkFileBySingle(package, fileInfo.FullName, hash, rootFile, hashAlgorithm, internalCompression, encryptionType, encryptionKey);
                                        return;
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    // 查找是否有相同的文件
                    // 如果有相同大小，且 hash 相同的文件，则创建影子文件
                    var upath = fileInfo.FullName.ToUrlPath();
                    var realFile = _rootFilesetDb.Single(c => c.FilesetSize == fileSize && c.FilesetHash == hash && c.IsShadow == false && c.FilesetSourceKey != upath);
                    if (realFile != null)
                    {
                        // 找到了相同的真实文件，说明新文件是影子文件
                        // 添加影子文件，影子文件不会被打包，只是一个指向原始数据的指针
                        // 影子文件和真实文件，保证在同一个包中
                        // 处理影子文件
                        var package = _rootPackageDb.Get(realFile.RootPackageId);
                        if (package != null)
                        {
                            // 加锁执行
                            var isShadowSave = false;
                            LocalResourceLock.Lock(package.Key, () =>
                            {
                                var dbPath = Path.Combine(_baseDir, package.Key, "0.d");
                                if (File.Exists(dbPath))
                                {
                                    var blocksetDb = GetRepository<Blockset>(dbPath, encryptionKey);
                                    var filesetDb = GetRepository<Fileset>(dbPath, encryptionKey);

                                    var filesets = filesetDb.Where(c => c.SourceKey == realFile.FilesetSourceKey);
                                    if (filesets.Count > 0)
                                    {
                                        var fids = filesets.Select(f => f.Id).ToList();
                                        var blocks = blocksetDb.Where(c => fids.Contains(c.FilesetId));
                                        if (blocks.Count > 0)
                                        {
                                            // 创建影子文件的 fileset， 并复制真实文件的 block
                                            foreach (var fsItem in filesets)
                                            {
                                                var shadowFile = new Fileset()
                                                {
                                                    SourceKey = fileInfo.FullName.ToUrlPath(),
                                                    Created = fileInfo.CreationTime.ToUnixTimeMs(),
                                                    Updated = fileInfo.LastWriteTime.ToUnixTimeMs(),
                                                    Size = fsItem.Size,
                                                    Key = fsItem.Key,
                                                    Hash = fsItem.Hash,
                                                    IsShadow = true
                                                };
                                                filesetDb.Add(shadowFile);

                                                // 复制 block
                                                foreach (var block in blocks.Where(c => c.FilesetId == fsItem.Id))
                                                {
                                                    var blockFilePath = Path.Combine(_baseDir, package.Key, $"{block.Index}.f");
                                                    if (File.Exists(blockFilePath))
                                                    {
                                                        var shadowBlock = new Blockset()
                                                        {
                                                            FilesetId = shadowFile.Id,
                                                            Index = block.Index,
                                                            Hash = block.Hash,
                                                            Size = block.Size,
                                                            StartIndex = block.StartIndex,
                                                            EndIndex = block.EndIndex,
                                                            EncryptionAlgorithm = block.EncryptionAlgorithm,
                                                            HashAlgorithm = block.HashAlgorithm,
                                                            InternalCompression = block.InternalCompression
                                                        };
                                                        blocksetDb.Add(shadowBlock);
                                                    }
                                                }
                                            }

                                            var rf = new RootFileset()
                                            {
                                                RootPackageId = package.Id,
                                                FilesetSourceKey = fileInfo.FullName.ToUrlPath(),
                                                FilesetCreated = fileInfo.CreationTime.ToUnixTimeMs(),
                                                FilesetUpdated = fileInfo.LastWriteTime.ToUnixTimeMs(),
                                                FilesetSize = fileSize,
                                                FilesetHash = hash,
                                                IsShadow = true
                                            };
                                            _rootFilesetDb.Add(rf);

                                            // 影子文件不更新包大小

                                            // 执行完毕
                                            isShadowSave = true;
                                        }
                                    }
                                }
                            });

                            if (isShadowSave)
                            {
                                return;
                            }
                        }
                    }
                }

                // 获取最后一个包
                // 计算包 index
                // 读取 sqlite 数据库，获取最新包的索引，如果没有则为 0，如果 (包size + 文件size) < PACKAGE_SIZE，则为当前包，否则为新包（index+1）

                RootPackage last = null;
                var lastPackageIndex = 0;

                // 一定是小于等于 PACKAGE_SIZE 的文件
                // 即相同类别 Category 的文件大小一定是相同 Multifile
                if (fileSize <= PACKAGE_SIZE)
                {
                    // 文件较小，打包处理
                    // 读取一个数据较少的包
                    // 由于数据库不支持 (c.Size + fileSize) < PACKAGE_SIZE
                    // 因此调整
                    var allowSize = PACKAGE_SIZE - fileSize;

                    last = _rootPackageDb.Single(c => c.Multifile == true && c.Category == category && c.Size <= allowSize, c => c.Size);

                    //last = _rootPackageDb.Where(c => c.Multifile == true && c.Category == category && (c.Size + fileSize) < PACKAGE_SIZE)
                    //   .OrderBy(c => c.Size)
                    //   .FirstOrDefault();

                    if (last == null)
                    {
                        // 最后一个包
                        last = _rootPackageDb.Single(c => c.Multifile == true && c.Category == category, c => c.Index, true);
                    }

                    if (last == null)
                    {
                        last = new RootPackage()
                        {
                            Category = category,
                            Index = lastPackageIndex,
                            Key = category + $"{lastPackageIndex % 256:x2}/{lastPackageIndex}",
                            Size = 0,
                            Multifile = true
                        };
                        _rootPackageDb.Add(last);
                    }
                    else if (last.Size + fileSize <= PACKAGE_SIZE)
                    {
                        // 如果是多文件包，且文件大小小于包大小，且包大小 + 文件大小小于包大小，则加入当前包
                        lastPackageIndex = last.Index;
                    }
                    else
                    {
                        lastPackageIndex = last.Index + 1;
                        last = new RootPackage()
                        {
                            Category = category,
                            Index = lastPackageIndex,
                            Key = category + $"{lastPackageIndex % 256:x2}/{lastPackageIndex}",
                            Size = 0,
                            Multifile = true
                        };
                        _rootPackageDb.Add(last);
                    }
                }
                else
                {
                    // 文件较大，分块处理
                    last = _rootPackageDb.Single(c => c.Multifile == false && c.Category == category, c => c.Index, false);

                    if (last == null)
                    {
                        last = new RootPackage()
                        {
                            Category = category,
                            Index = lastPackageIndex,
                            Key = category + $"{lastPackageIndex % 256:x2}/{lastPackageIndex}",
                            Size = 0,
                            Multifile = false
                        };
                        _rootPackageDb.Add(last);
                    }
                    else
                    {
                        lastPackageIndex = last.Index + 1;
                        last = new RootPackage()
                        {
                            Category = category,
                            Index = lastPackageIndex,
                            Key = category + $"{lastPackageIndex % 256:x2}/{lastPackageIndex}",
                            Size = 0,
                            Multifile = false
                        };
                        _rootPackageDb.Add(last);
                    }
                }

                // 根据包索引获取包的存储路径
                // 包根路径
                var packageRootPath = GetPackagePathByIndex(category, lastPackageIndex);

                // 包完整路径
                var packageFullPath = Path.Combine(packageRootPath, $"{lastPackageIndex}");
                Directory.CreateDirectory(packageFullPath);

                // 包加入
                PackageAddFile(last, fileInfo, hash, hashAlgorithm, internalCompression, encryptionType, encryptionKey);
            }
            finally
            {
                // TODO
                // 更新索引和校验
                //UpdateIndexAndVerify(packagePath, category);
            }
        }

        /// <summary>
        /// 删除文件并收缩包
        /// </summary>
        /// <param name="package"></param>
        /// <param name="fileFullName"></param>
        public static void PackageDeleteFile(RootPackage package, string fileFullName, string encryptionKey = null)
        {
            if (package == null || string.IsNullOrWhiteSpace(fileFullName))
            {
                return;
            }

            // 不判断文件是否存在，因为文件可能被删除了

            // 加锁执行
            LocalResourceLock.Lock(package.Key, () =>
            {
                var packageDbPath = Path.Combine(_baseDir, package.Key, "0.d");

                // 不判断源文件是否存在，因为文件可能被删除
                if (File.Exists(packageDbPath))
                {
                    var blocksetDb = GetRepository<Blockset>(packageDbPath, encryptionKey);
                    var filesetDb = GetRepository<Fileset>(packageDbPath, encryptionKey);

                    // 含有多个文件的单个包
                    if (package.Multifile)
                    {
                        var packageFilePath = Path.Combine(_baseDir, package.Key, "0.f");

                        var upath = fileFullName.ToUrlPath();
                        var fileset = filesetDb.Single(c => c.SourceKey == upath);
                        if (fileset != null)
                        {
                            var blocksets = blocksetDb.Where(c => c.FilesetId == fileset.Id);

                            blocksetDb.Delete(c => c.FilesetId == fileset.Id);
                            filesetDb.Delete(fileset);

                            _rootFilesetDb.Delete(c => c.FilesetSourceKey == upath);

                            // 是否允许收缩文件
                            var isAllowShrinkFile = false;
                            if (fileset.IsShadow)
                            {
                                // 如果是影子文件移除，则判断是否还有其他文件引用此文件，如果没有其他文件引用，则进行收缩处理
                                isAllowShrinkFile = !filesetDb.Any(c => c.Size == fileset.Size && c.Hash == fileset.Hash);
                            }
                            else
                            {
                                // 相反，如果是真实文件移除，则判断是否还有影子文件引用此文件，如果没有其他文件引用，则进行收缩处理
                                isAllowShrinkFile = !filesetDb.Any(c => c.Size == fileset.Size && c.Hash == fileset.Hash && c.IsShadow == true);
                            }

                            // 允许收缩
                            if (isAllowShrinkFile)
                            {
                                package.Size -= fileset.Size;
                                _rootPackageDb.Update(package);

                                // 收缩包
                                if (blocksets.Count > 0 && File.Exists(packageFilePath))
                                {
                                    using (FileStream fs = new FileStream(packageFilePath, FileMode.Open, FileAccess.ReadWrite))
                                    {
                                        foreach (var item in blocksets)
                                        {
                                            var start = item.StartIndex;
                                            var end = item.EndIndex;

                                            // 不允许操作
                                            if (start > end)
                                            {
                                                continue;
                                            }

                                            // 0 字节文件
                                            if (start == 0 && end == 0)
                                            {
                                                continue;
                                            }

                                            var length = end - start + 1;

                                            // 移动数据
                                            MoveData(fs, start, end);

                                            // 如果这个块不是从 0 开始的，说明后面的块都需要移动
                                            // 查找后面的那些块
                                            if (start > 0)
                                            {
                                                var nextBlocks = blocksetDb.Where(c => c.StartIndex > end);
                                                foreach (var nextBlock in nextBlocks)
                                                {
                                                    nextBlock.StartIndex -= length;
                                                    nextBlock.EndIndex -= length;
                                                    blocksetDb.Update(nextBlock);
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            // 处理完毕后，判断文件如果有且只有一个文件，且引用此文件的是影子文件，则将影子文件标记为真实文件
                            // 根据 hash 和大小判断
                            if (filesetDb.Count(c => c.Size == fileset.Size && c.Hash == fileset.Hash) == 1)
                            {
                                var shadowFiles = filesetDb.Where(c => c.Size == fileset.Size && c.Hash == fileset.Hash && c.IsShadow == true);
                                if (shadowFiles.Count() == 1)
                                {
                                    var shadowFile = shadowFiles.First();
                                    shadowFile.IsShadow = false;
                                    filesetDb.Update(shadowFile);

                                    // 标记 rootFileset isShadow
                                    var rootFileset = _rootFilesetDb.Single(c => c.FilesetSourceKey == shadowFile.SourceKey && c.IsShadow == true);
                                    if (rootFileset != null)
                                    {
                                        rootFileset.IsShadow = false;
                                        _rootFilesetDb.Update(rootFileset);
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // 只有一个文件的包，包含 1 个或多个块
                        var upath = fileFullName.ToUrlPath();
                        var filesets = filesetDb.Where(c => c.SourceKey == upath);
                        var filesetIds = filesets.Select(f => f.Id).ToList();
                        var blocksets = blocksetDb.Where(c => filesetIds.Contains(c.FilesetId));

                        if (filesets.Count > 0)
                        {
                            foreach (var fileset in filesets)
                            {
                                blocksetDb.Delete(c => c.FilesetId == fileset.Id);
                                filesetDb.Delete(fileset);
                            }

                            _rootFilesetDb.Delete(c => c.FilesetSourceKey == upath);

                            // 是否允许收缩文件
                            var isAllowShrinkFile = false;
                            if (filesets.Any(c => c.IsShadow))
                            {
                                // 如果是影子文件移除，则判断是否还有其他文件引用此文件，如果没有其他文件引用，则进行收缩处理
                                // 使用循环的方式验证
                                isAllowShrinkFile = true;
                                foreach (var item in filesets)
                                {
                                    isAllowShrinkFile = !filesetDb.Any(c => c.Size == item.Size && c.Hash == item.Hash);
                                    if (!isAllowShrinkFile)
                                    {
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                // 相反，如果是真实文件移除，则判断是否还有影子文件引用此文件，如果没有其他文件引用，则进行收缩处理
                                // 使用循环的方式验证
                                isAllowShrinkFile = true;
                                foreach (var item in filesets)
                                {
                                    isAllowShrinkFile = !filesetDb.Any(c => c.Size == item.Size && c.Hash == item.Hash && c.IsShadow == true);
                                    if (!isAllowShrinkFile)
                                    {
                                        break;
                                    }
                                }
                            }

                            // 允许收缩
                            if (isAllowShrinkFile)
                            {
                                package.Size -= filesets.Sum(c => c.Size);
                                _rootPackageDb.Update(package);

                                // 删除包
                                if (blocksets.Count > 0)
                                {
                                    foreach (var item in blocksets)
                                    {
                                        var packageFilePath = Path.Combine(_baseDir, package.Key, $"{item.Index}.f");
                                        if (File.Exists(packageFilePath))
                                        {
                                            File.Delete(packageFilePath);
                                        }
                                    }
                                }

                                // 收缩包，以滚动方式移动数据
                                if (blocksets.Count > 0)
                                {
                                    foreach (var item in blocksets)
                                    {
                                        var packageFilePath = Path.Combine(_baseDir, package.Key, $"{item.Index}.f");
                                        if (File.Exists(packageFilePath))
                                        {
                                            using (FileStream fs = new FileStream(packageFilePath, FileMode.Open, FileAccess.ReadWrite))
                                            {
                                                var start = item.StartIndex;
                                                var end = item.EndIndex;

                                                // 不允许操作
                                                if (start > end)
                                                {
                                                    continue;
                                                }

                                                // 0 字节文件
                                                if (start == 0 && end == 0)
                                                {
                                                    continue;
                                                }

                                                var length = end - start + 1;

                                                // 移动数据
                                                MoveData(fs, start, end);

                                                // 如果这个块不是从 0 开始的，说明后面的块都需要移动
                                                // 查找后面的那些块
                                                if (start > 0)
                                                {
                                                    var nextBlocks = blocksetDb.Where(c => c.StartIndex > end);
                                                    foreach (var nextBlock in nextBlocks)
                                                    {
                                                        nextBlock.StartIndex -= length;
                                                        nextBlock.EndIndex -= length;
                                                        blocksetDb.Update(nextBlock);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            // 处理完毕后，判断文件如果有且只有一个文件，且引用此文件的是影子文件，则将影子文件标记为真实文件
                            // 根据 hash 和大小判断
                            var fileHash = filesets.First().Hash;
                            var fss = filesetDb.Where(c => c.Hash == fileHash);
                            if (fss.GroupBy(c => c.SourceKey).Count() == 1 && fss.Any(x => x.IsShadow == true))
                            {
                                foreach (var item in fss)
                                {
                                    item.IsShadow = false;
                                    filesetDb.Update(item);
                                }

                                // 标记 rootFileset isShadow
                                var fssKey = fss.First().SourceKey;
                                var rootFileset = _rootFilesetDb.Single(c => c.FilesetSourceKey == fssKey && c.IsShadow == true);
                                if (rootFileset != null)
                                {
                                    rootFileset.IsShadow = false;
                                    _rootFilesetDb.Update(rootFileset);
                                }
                            }
                        }
                    }

                    filesetDb.Compact();
                }
            });
        }

        /// <summary>
        /// 单文件包更新，以滚动的方式验证哪个块需要增长或收缩
        /// </summary>
        /// <param name="package"></param>
        /// <param name="fileFullName"></param>
        public static void PackageShrinkFileBySingle(RootPackage package,
            string fileFullName,
            string sha256Hash,
            RootFileset rootFileset,
            string hashAlgorithm = "SHA256",
            string internalCompression = null,
            string encryptionType = null,
            string encryptionKey = null)
        {
            if (package.Multifile)
            {
                return;
            }

            var fileInfo = new FileInfo(fileFullName);
            if (!fileInfo.Exists)
            {
                return;
            }

            // 加锁执行

            LocalResourceLock.Lock(package.Key, () =>
            {
                var packageDbPath = Path.Combine(_baseDir, package.Key, "0.d");

                if (File.Exists(fileFullName) && File.Exists(packageDbPath))
                {
                    var blocksetDb = GetRepository<Blockset>(packageDbPath, encryptionKey);
                    var filesetDb = GetRepository<Fileset>(packageDbPath, encryptionKey);

                    var upath = fileFullName.ToUrlPath();
                    var filesets = filesetDb.Where(c => c.SourceKey == upath).ToList();
                    var filesetIds = filesets.Select(f => f.Id).ToList();
                    var blocksets = blocksetDb.Where(c => filesetIds.Contains(c.FilesetId), c => c.Index);

                    if (filesets.Count > 0)
                    {
                        // 在收缩或增长前需要判断是否是否被影子文件引用，如果没有被影子文件引用，则进行收缩或增长处理
                        // 如果被影子文件引用，则作为新文件处理
                        var isReferenced = false;
                        foreach (var item in filesets)
                        {
                            if (filesetDb.Any(c => c.Size == item.Size && c.Hash == item.Hash && c.IsShadow == true))
                            {
                                isReferenced = true;
                                break;
                            }
                        }

                        if (!isReferenced)
                        {
                            using (FileStream fs = new FileStream(fileFullName, FileMode.Open, FileAccess.Read))
                            {
                                var blockIndex = 0;

                                var blockStartIndex = 0L;
                                var blockEndIndex = 0L;
                                bool blockMismatch = false;

                                foreach (var block in blocksets)
                                {
                                    blockStartIndex = fs.Position;
                                    blockEndIndex = blockStartIndex + PACKAGE_SIZE - 1;
                                    if (blockEndIndex >= fs.Length)
                                    {
                                        blockEndIndex = fs.Length - 1;
                                    }

                                    var blockLength = blockEndIndex - blockStartIndex + 1;
                                    byte[] buffer = new byte[blockLength];
                                    fs.Read(buffer, 0, (int)blockLength);

                                    var blockHash = HashHelper.ComputeHashHex(buffer, hashAlgorithm);
                                    if (block.Hash != blockHash)
                                    {
                                        blockMismatch = true;

                                        // 删除所有后续块
                                        var remainingBlocks = blocksets.Skip(block.Index).ToList();
                                        var remainingBlockFids = remainingBlocks.Select(c => c.FilesetId).ToList();
                                        foreach (var remainingBlock in remainingBlocks)
                                        {
                                            // 删除分块信息
                                            blocksetDb.Delete(remainingBlock);

                                            // 删除分块文件
                                            var deleteFile = Path.Combine(_baseDir, package.Key, $"{remainingBlock.Index}.f");
                                            if (File.Exists(deleteFile))
                                            {
                                                File.Delete(deleteFile);
                                            }

                                            // 删除分块文件信息
                                            var fds = filesets.Where(c => c.Id == remainingBlock.FilesetId).ToList();
                                            foreach (var fd in fds)
                                            {
                                                filesetDb.Delete(fd);
                                            }
                                        }

                                        // 历史块信息删除
                                        blocksets.RemoveAll(b => b.Index >= block.Index);

                                        // 历史块文件信息删除
                                        filesets.RemoveAll(f => remainingBlockFids.Contains(f.Id));

                                        // 从当前位置开始重新写入包文件
                                        fs.Position = blockStartIndex;

                                        break;
                                    }
                                    else
                                    {
                                        // 块一致，不需要处理，继续验证下一个块
                                        blockIndex++;
                                    }
                                }

                                if (blockMismatch)
                                {
                                    // 处理文件的其余部分
                                    while (fs.Position < fs.Length)
                                    {
                                        blockStartIndex = fs.Position;
                                        blockEndIndex = blockStartIndex + PACKAGE_SIZE - 1;
                                        if (blockEndIndex >= fs.Length)
                                        {
                                            blockEndIndex = fs.Length - 1;
                                        }
                                        var blockLength = blockEndIndex - blockStartIndex + 1;

                                        byte[] buffer = new byte[blockLength];
                                        fs.Read(buffer, 0, (int)blockLength);
                                        var filesetSize = buffer.Length;

                                        var blockHash = HashHelper.ComputeHashHex(buffer, hashAlgorithm);

                                        // 读取文件流，根据压缩算法进行压缩、加密等处理
                                        var compressedBuffer = CompressionHelper.Compress(buffer, internalCompression, encryptionType, encryptionKey);
                                        blockLength = compressedBuffer.Length;

                                        // 创建块文件
                                        var packageFilePath = Path.Combine(_baseDir, package.Key, $"{blockIndex}.f");
                                        using (FileStream packageStream = new FileStream(packageFilePath, FileMode.Create, FileAccess.Write))
                                        {
                                            packageStream.Write(compressedBuffer, 0, compressedBuffer.Length);
                                        }

                                        // 创建块文件信息
                                        var file = new Fileset()
                                        {
                                            SourceKey = fileInfo.FullName.ToUrlPath(),
                                            Created = fileInfo.CreationTime.ToUnixTimeMs(),
                                            Updated = fileInfo.LastWriteTime.ToUnixTimeMs(),
                                            Hash = sha256Hash,
                                            Size = filesetSize,
                                            Key = $"{package.Key}/{blockIndex}.f"
                                        };
                                        filesetDb.Add(file);
                                        filesets.Add(file);

                                        // 创建新的块记录
                                        var newBlock = new Blockset()
                                        {
                                            FilesetId = file.Id,
                                            Hash = blockHash,
                                            Size = blockLength,
                                            Index = blockIndex,
                                            StartIndex = 0,
                                            EndIndex = blockLength - 1,
                                            EncryptionAlgorithm = encryptionType,
                                            HashAlgorithm = hashAlgorithm,
                                            InternalCompression = internalCompression
                                        };
                                        blocksetDb.Add(newBlock);
                                        blocksets.Add(newBlock);

                                        blockIndex++;
                                    }
                                }
                            }

                            // 了保证数据的一致性，需要更新文件信息
                            foreach (var item in filesets)
                            {
                                item.SourceKey = fileInfo.FullName.ToUrlPath();
                                item.Created = fileInfo.CreationTime.ToUnixTimeMs();
                                item.Updated = fileInfo.LastWriteTime.ToUnixTimeMs();
                                item.Hash = sha256Hash;
                                filesetDb.Update(item);
                            }

                            // 重新计算当前文件大小
                            package.Size = filesets.Sum(c => c.Size);
                            _rootPackageDb.Update(package);

                            // 更新根文件集信息
                            rootFileset.FilesetHash = sha256Hash;
                            rootFileset.FilesetSize = fileInfo.Length;
                            rootFileset.FilesetCreated = fileInfo.CreationTime.ToUnixTimeMs();
                            rootFileset.FilesetUpdated = fileInfo.LastWriteTime.ToUnixTimeMs();

                            _rootFilesetDb.Update(rootFileset);
                        }
                        else
                        {
                            // 删除并收缩包，然后在新的包中处理
                            PackageDeleteFile(package, fileInfo.FullName, encryptionKey);

                            // 重新处理
                            ProcessFile(fileFullName, hashAlgorithm, internalCompression, encryptionType, encryptionKey);
                        }
                    }

                    filesetDb.Compact();
                }
            });
        }

        /// <summary>
        /// 在指定的包中添加文件
        /// </summary>
        /// <param name="package"></param>
        /// <param name="fileFullName"></param>
        /// <param name="fileInfo"></param>
        /// <param name="internalCompression">Zstd、LZ4、Snappy</param>
        public static void PackageAddFile(RootPackage package, FileInfo fileInfo,
            string hash,
            string hashAlgorithm = "SHA256",
            string internalCompression = null,
            string encryptionType = null,
            string encryptionKey = null)
        {
            // 可以不用加锁，应为同一个包只有一个线程在处理
            // 注意更新 root package 和 rootFileset 需要单独加锁更新，因为这两个表是全局的，需要保证数据的一致性

            if (package == null || !fileInfo.Exists)
            {
                return;
            }

            // 加锁执行
            LocalResourceLock.Lock(package.Key, () =>
            {
                var fileSize = fileInfo.Length;

                var packageDbPath = Path.Combine(_baseDir, package.Key, "0.d");

                // 判断是文件是否处理过
                var filesetDb = GetRepository<Fileset>(packageDbPath, encryptionKey);
                var blocksetDb = GetRepository<Blockset>(packageDbPath, encryptionKey);

                var upath = fileInfo.FullName.ToUrlPath();
                var oldFile = filesetDb.Single(c => c.SourceKey == upath);
                if (oldFile != null)
                {
                    if (oldFile.Hash == hash)
                    {
                        // 处理过且 hash 一致，直接返回
                        return;
                    }
                    else
                    {
                        // 不一致，删除包重新处理
                        PackageDeleteFile(package, fileInfo.FullName, encryptionKey);
                    }
                }

                // 多文件包，不需要分块，在文件中直接追加
                if (package.Multifile)
                {
                    // 合并文件时，计算文件的块信息，返回块的起始和结束位置
                    var blockStartIndex = 0L;
                    var blockEndIndex = 0L;
                    var blockIndex = 0;

                    var packageFilePath = Path.Combine(_baseDir, package.Key, "0.f");

                    var blockSize = 0L;
                    using (FileStream packageStream = new FileStream(packageFilePath, FileMode.Append))
                    {
                        var buffer = File.ReadAllBytes(fileInfo.FullName);

                        // 读取文件流，根据压缩算法进行压缩、加密等处理
                        var compressedBuffer = CompressionHelper.Compress(buffer, internalCompression, encryptionType, encryptionKey);

                        blockStartIndex = packageStream.Length;
                        packageStream.Write(compressedBuffer, 0, compressedBuffer.Length);
                        blockEndIndex = packageStream.Length - 1;
                        blockSize = compressedBuffer.Length;
                    }

                    var file = new Fileset()
                    {
                        SourceKey = fileInfo.FullName.ToUrlPath(),
                        Created = fileInfo.CreationTime.ToUnixTimeMs(),
                        Updated = fileInfo.LastWriteTime.ToUnixTimeMs(),
                        Hash = hash,
                        Size = fileSize,
                        Key = $"{package.Key}/0.f"
                    };

                    filesetDb.Add(file);

                    // 创建块
                    var bs = new Blockset()
                    {
                        FilesetId = file.Id,
                        Hash = hash,
                        Index = blockIndex,
                        StartIndex = blockStartIndex,
                        EndIndex = blockEndIndex,
                        Size = blockSize,
                        InternalCompression = internalCompression,
                        HashAlgorithm = hashAlgorithm,
                        EncryptionAlgorithm = encryptionType,
                    };
                    blocksetDb.Add(bs);
                }
                else
                {
                    // 单文件包，需要分块，每个块大小为 PACKAGE_SIZE
                    using (FileStream fs = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read))
                    {
                        var blockIndex = 0;
                        var blockStartIndex = 0L;
                        var blockEndIndex = 0L;

                        while (fs.Position < fs.Length)
                        {
                            blockStartIndex = fs.Position;
                            blockEndIndex = blockStartIndex + PACKAGE_SIZE - 1;
                            if (blockEndIndex >= fs.Length)
                            {
                                blockEndIndex = fs.Length - 1;
                            }

                            var blockLength = blockEndIndex - blockStartIndex + 1;
                            byte[] buffer = new byte[blockLength];
                            fs.Read(buffer, 0, (int)blockLength);
                            var filesetSize = buffer.Length;

                            var blockHash = HashHelper.ComputeHashHex(buffer, hashAlgorithm);

                            // 读取文件流，根据压缩算法进行压缩、加密等处理
                            var compressedBuffer = CompressionHelper.Compress(buffer, internalCompression, encryptionType, encryptionKey);
                            var compressedBlockLength = compressedBuffer.Length;

                            // 写入包文件
                            var packageFilePath = Path.Combine(_baseDir, package.Key, $"{blockIndex}.f");
                            using (FileStream packageStream = new FileStream(packageFilePath, FileMode.Append))
                            {
                                packageStream.Write(compressedBuffer, 0, compressedBlockLength);
                            }

                            var file = new Fileset()
                            {
                                SourceKey = fileInfo.FullName.ToUrlPath(),
                                Created = fileInfo.CreationTime.ToUnixTimeMs(),
                                Updated = fileInfo.LastWriteTime.ToUnixTimeMs(),
                                Hash = hash,
                                Size = filesetSize,
                                Key = $"{package.Key}/{blockIndex}.f"
                            };
                            filesetDb.Add(file);

                            // 创建块
                            var bs = new Blockset()
                            {
                                FilesetId = file.Id,
                                Hash = blockHash,
                                Size = compressedBlockLength,
                                Index = blockIndex,
                                StartIndex = 0,
                                EndIndex = compressedBlockLength - 1,
                                InternalCompression = internalCompression,
                                EncryptionAlgorithm = encryptionType,
                                HashAlgorithm = hashAlgorithm,
                            };
                            blocksetDb.Add(bs);

                            blockIndex++;
                        }

                        fs.Close();
                    }
                }

                // 更新包大小
                package.Size += fileSize;

                var rf = new RootFileset()
                {
                    RootPackageId = package.Id,
                    FilesetSourceKey = fileInfo.FullName.ToUrlPath(),
                    FilesetHash = hash,
                    FilesetSize = fileSize,
                    FilesetCreated = fileInfo.CreationTime.ToUnixTimeMs(),
                    FilesetUpdated = fileInfo.LastWriteTime.ToUnixTimeMs(),
                };
                _rootFilesetDb.Add(rf);
                _rootPackageDb.Update(package);

                filesetDb.Compact();
            });
        }

        /// <summary>
        /// 移动数据
        /// </summary>
        /// <param name="fs"></param>
        /// <param name="start"></param>
        /// <param name="end"></param>
        private static void MoveData(FileStream fs, long start, long end)
        {
            long fileLength = fs.Length;
            long segmentLength = end - start + 1;
            long remainingLength = fileLength - end - 1;

            byte[] buffer = new byte[8192];
            long position = start;

            while (remainingLength > 0)
            {
                fs.Position = end + 1;
                int bytesRead = fs.Read(buffer, 0, (int)Math.Min(buffer.Length, remainingLength));

                fs.Position = position;
                fs.Write(buffer, 0, bytesRead);

                position += bytesRead;
                end += bytesRead;
                remainingLength -= bytesRead;
            }

            // 设置新文件长度
            fs.SetLength(fileLength - segmentLength);
        }

        /// <summary>
        /// 获取文件大小对应的根分类
        /// </summary>
        /// <param name="fileSize"></param>
        /// <returns></returns>
        /// <exception cref="NotSupportedException"></exception>
        public static string GetRootCategory(long fileSize)
        {
            if (fileSize <= 0) return "0"; // 0 字节文件
            else if (fileSize <= 1024) return "a";
            else if (fileSize <= 10 * 1024) return "b";
            else if (fileSize <= 100 * 1024) return "c";
            else if (fileSize <= 1024 * 1024) return "d";
            else if (fileSize <= 10 * 1024 * 1024) return "e";
            else if (fileSize <= PACKAGE_SIZE) return "f"; // <= 16MB 包大小为多个文件一个包
            else if (fileSize <= 100 * 1024 * 1024) return "g";
            else if (fileSize <= 1024 * 1024 * 1024) return "h";
            else if (fileSize <= 10 * 1024 * 1024 * 1024L) return "i";
            else if (fileSize <= 100 * 1024 * 1024 * 1024L) return "j";
            else if (fileSize <= 1024 * 1024 * 1024 * 1024L) return "k";
            else throw new NotSupportedException("不支持大于 1TB 的文件");
        }

        /// <summary>
        /// 根据包索引获取包的存储路径
        /// </summary>
        /// <param name="rootCategory"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        public static string GetPackagePathByIndex(string rootCategory, int index)
        {
            // 使用包 index 转为 16 进制字符串，计算包的存储路径
            var subDir = $"{index % 256:x2}";
            var path = Path.Combine(_baseDir, rootCategory + subDir);
            Directory.CreateDirectory(path);
            return path;
        }

        //public static void UpdateIndexAndVerify(string packagePath, string category)
        //{
        //    string indexFile = Path.Combine(_baseDir, $"{category}.f");

        //    // 更新 index 文件
        //    File.AppendAllText(indexFile, packagePath + Environment.NewLine);

        //    // 验证 index 文件
        //    VerifyIndexFile(indexFile);
        //}

        //public static void VerifyIndexFile(string indexFile)
        //{
        //    // 简单校验逻辑示例，可以根据实际需求进行更复杂的校验
        //    if (!File.Exists(indexFile))
        //    {
        //        throw new FileNotFoundException("Index file not found");
        //    }
        //    else
        //    {
        //        Console.WriteLine("Index file verification passed");
        //    }
        //}

        /// <summary>
        /// 获取仓储（没有密码使用 OrmLiteRepository，有密码使用 LiteRepository）
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dbPath"></param>
        /// <param name="password"></param>
        /// <returns></returns>
        public static IRepository<T> GetRepository<T>(string dbPath, string password = null) where T : IBaseId, new()
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                return new OrmLiteRepository<T>(dbPath);
            }
            else
            {
                return new LiteRepository<T>(dbPath, password);
            }
        }
    }
}