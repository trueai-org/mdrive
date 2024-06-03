using MDriveSync.Security.Models;
using ServiceStack.Text;
using System.Security.Cryptography;

namespace MDriveSync.Security
{
    public class Testfile
    {
        /// <summary>
        /// 默认包大小（用于首次打包计算）
        /// </summary>
        private const long PACKAGE_SIZE = 16 * 1024 * 1024;

        /// <summary>
        /// 最大包大小（当包中的文件增长超出时，将增长的文件加入新的包，当前包进行收缩，保证当前包的最大大小）
        /// </summary>
        private const long MAX_PACKAGE_SIZE = 32 * 1024 * 1024;

        /// <summary>
        /// 根目录包索引
        /// </summary>
        private static SqliteRepository<RootPackage> _rootPackageDb;

        /// <summary>
        /// 根目录文件索引
        /// </summary>
        private static SqliteRepository<RootFileset> _rootFilesetDb;

        /// <summary>
        /// 根目录
        /// </summary>
        private const string _baseDir = "E:\\_test";

        public static void Run()
        {
            _rootPackageDb = new(Path.Combine(_baseDir, "root.d"));
            _rootFilesetDb = new(Path.Combine(_baseDir, "root.d"));

            var files = Directory.GetFiles("E:\\_test\\imgs", "*.*", SearchOption.TopDirectoryOnly);
            foreach (var item in files)
            {
                ProcessFile(item);
            }
        }

        public static void ProcessFile(string filePath)
        {
            // 获取文件大小，计算根分类
            var fileInfo = new FileInfo(filePath);
            var fileSize = fileInfo.Length;
            var category = GetRootCategory(fileSize);

            // 计算文件 SHA256
            var sha256Hash = ComputeSha256(filePath);

            RootPackage last = null;

            var rootFile = _rootFilesetDb.Single(c => c.FilesetSourceKey == fileInfo.FullName);
            if (rootFile != null)
            {
                if (rootFile.FilesetHash == sha256Hash)
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
                        if (package.Multifile)
                        {
                            // 多文件包
                            var dbPath = Path.Combine(_baseDir, package.Key, "0.d");
                            if (File.Exists(dbPath))
                            {
                                var blocksetDb = new SqliteRepository<Blockset>(dbPath);
                                var fileDb = new SqliteRepository<Fileset>(dbPath);

                                var fileset = fileDb.Single(c => c.SourceKey == fileInfo.FullName);
                                if (fileset != null)
                                {
                                    // 判断文件是增长还是收缩
                                    // 如果文件被缩小，或者包大小 + 文件大小 < MAX_PACKAGE_SIZE，则直接还是更新次包
                                    if (fileSize <= fileset.Size || (package.Size - fileset.Size + fileSize) <= MAX_PACKAGE_SIZE)
                                    {
                                        // 在原本的包基础上进行处理
                                        PackageDeleteFile(package, fileInfo.FullName);
                                        PackageAddFile(package, fileInfo, sha256Hash);

                                        // 处理完成
                                        return;
                                    }
                                }

                                // 删除并收缩包，然后在新的包中处理
                                PackageDeleteFile(package, fileInfo.FullName);
                            }
                        }
                        else
                        {
                            // 单文件包，以滚动的方式验证哪个块需要增长或收缩
                            PackageShrinkFileBySingle(package, fileInfo.FullName, sha256Hash, rootFile);
                            return;
                        }
                    }
                }
            }

            // 获取最后一个包
            if (last == null)
            {
                // 计算包 index
                // 读取 sqlite 数据库，获取最新包的索引，如果没有则为 0，如果 (包size + 文件size) < PACKAGE_SIZE，则为当前包，否则为新包（index+1）
                last = _rootPackageDb.Where(c => c.Category == category).OrderByDescending(c => c.Index).FirstOrDefault();
            }

            var lastPackageIndex = 0;
            if (last == null)
            {
                last = new RootPackage()
                {
                    Category = category,
                    Index = lastPackageIndex,
                    Key = category + $"{lastPackageIndex % 256:x2}/{lastPackageIndex}",
                    Size = 0,
                    Multifile = fileSize < PACKAGE_SIZE
                };
                _rootPackageDb.Add(last);
            }
            else if (fileSize < PACKAGE_SIZE && last.Multifile && (last.Size + fileSize <= PACKAGE_SIZE))
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
                    Multifile = fileSize < PACKAGE_SIZE
                };
                _rootPackageDb.Add(last);
            }

            // 根据包索引获取包的存储路径
            // 包根路径
            var packageRootPath = GetPackagePathByIndex(category, lastPackageIndex);

            // 包完整路径
            var packageFullPath = Path.Combine(packageRootPath, $"{lastPackageIndex}");
            Directory.CreateDirectory(packageFullPath);

            // 包加入
            PackageAddFile(last, fileInfo, sha256Hash);

            // TODO
            // 单文件包和多文件包

            //if (fileSize > PACKAGE_SIZE)
            //{
            //    // 拆包
            //}
            //else
            //{
            //}
            //if (fileSize <= PACKAGE_SIZE)
            //{
            //    // 文件大小小于包大小，直接加入包
            //    CombineIntoPackage(filePath, packageFilePath);
            //}
            //else
            //{
            //    // 文件大小大于包大小，需要分包
            //    // 1. 读取包文件，计算包大小
            //    var packageSize = new FileInfo(packageFilePath).Length;

            //    // 2. 判断是否需要新建包
            //    if (packageSize + fileSize > MAX_PACKAGE_SIZE)
            //    {
            //        // 3. 新建包
            //        index++;
            //        last = new RootPackage()
            //        {
            //            Category = category,
            //            Index = index,
            //            Key = category + $"{index % 256:x2}",
            //            Size = 0
            //        };
            //        _rootIndexDb.Add(last);

            //        // 4. 更新包路径
            //        packageFullPath = Path.Combine(packageRootPath, $"{index}");
            //        Directory.CreateDirectory(packageFullPath);

            //        // 5. 更新包数据库文件路径
            //        packageDbPath = Path.Combine(packageFullPath, $"{index}.d");

            //        // 6. 更新包文件路径
            //        packageFilePath = Path.Combine(packageFullPath, $"{index}.f");
            //    }

            //    // 7. 加入包
            //    CombineIntoPackage(filePath, packageFilePath);
            //}

            //var cellDb = new SqliteRepository<Cell, string>(packageDbPath);

            // 更新索引和校验
            //UpdateIndexAndVerify(packagePath, category);
        }

        /// <summary>
        /// 删除文件并收缩包
        /// </summary>
        /// <param name="package"></param>
        /// <param name="fileFullName"></param>
        public static void PackageDeleteFile(RootPackage package, string fileFullName)
        {
            // 加锁执行
            LocalResourceLock.Lock(package.Key, () =>
            {
                var packageDbPath = Path.Combine(_baseDir, package.Key, "0.d");

                if (File.Exists(fileFullName) && File.Exists(packageDbPath))
                {
                    var blocksetDb = new SqliteRepository<Blockset>(packageDbPath);
                    var filesetDb = new SqliteRepository<Fileset>(packageDbPath);

                    // 含有多个文件的单个包
                    if (package.Multifile)
                    {
                        var packageFilePath = Path.Combine(_baseDir, package.Key, "0.f");

                        var fileset = filesetDb.Single(c => c.SourceKey == fileFullName);
                        if (fileset != null)
                        {
                            var blocksets = blocksetDb.Where(c => c.FilesetId == fileset.Id);

                            blocksetDb.Delete(c => c.FilesetId == fileset.Id);
                            filesetDb.Delete(fileset);

                            package.Size -= fileset.Size;
                            _rootPackageDb.Update(package);
                            _rootFilesetDb.Delete(c => c.FilesetSourceKey == fileFullName);

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
                    }
                    else
                    {
                        // 只有一个文件的包，包含 1 个或多个块
                        var filesets = filesetDb.Where(c => c.SourceKey == fileFullName);
                        var filesetIds = filesets.Select(f => f.Id).ToList();
                        var blocksets = blocksetDb.Where(c => filesetIds.Contains(c.FilesetId));

                        if (filesets.Count > 0)
                        {
                            foreach (var fileset in filesets)
                            {
                                blocksetDb.Delete(c => c.FilesetId == fileset.Id);
                                filesetDb.Delete(fileset);
                            }

                            package.Size -= filesets.Sum(c => c.Size);
                            _rootPackageDb.Update(package);
                            _rootFilesetDb.Delete(c => c.FilesetSourceKey == fileFullName);

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
                    }
                }
            });
        }

        /// <summary>
        /// 单文件包更新，以滚动的方式验证哪个块需要增长或收缩
        /// </summary>
        /// <param name="package"></param>
        /// <param name="fileFullName"></param>
        public static void PackageShrinkFileBySingle(RootPackage package, string fileFullName, string sha256Hash, RootFileset rootFileset)
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
                    var blocksetDb = new SqliteRepository<Blockset>(packageDbPath);
                    var filesetDb = new SqliteRepository<Fileset>(packageDbPath);

                    var filesets = filesetDb.Where(c => c.SourceKey == fileFullName).ToList();
                    var filesetIds = filesets.Select(f => f.Id).ToList();
                    var blocksets = blocksetDb.Where(c => filesetIds.Contains(c.FilesetId)).OrderBy(c => c.Index).ToList();

                    if (filesets.Count > 0)
                    {
                        // 减去历史块文件大小
                        package.Size -= filesets.Sum(c => c.Size);

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

                                string blockHash;
                                using (SHA256 sha256 = SHA256.Create())
                                {
                                    blockHash = BitConverter.ToString(sha256.ComputeHash(buffer)).Replace("-", "").ToLowerInvariant();
                                }

                                if (block.Hash != blockHash)
                                {
                                    blockMismatch = true;

                                    // 删除所有后续块
                                    var remainingBlocks = blocksets.Skip(block.Index).ToList();
                                    foreach (var remainingBlock in remainingBlocks)
                                    {
                                        // 删除块信息
                                        blocksetDb.Delete(remainingBlock);

                                        // 删除分块文件信息
                                        var fds = filesets.Where(c => c.Id == remainingBlock.FilesetId).ToList();
                                        foreach (var fd in fds)
                                        {
                                            filesetDb.Delete(fd);
                                            filesets.Remove(fd);
                                        }

                                        // 删除分块文件
                                        var deleteFile = Path.Combine(_baseDir, package.Key, $"{remainingBlock.Index}.f");
                                        if (File.Exists(deleteFile))
                                        {
                                            File.Delete(deleteFile);
                                        }
                                    }

                                    // 从当前位置开始重新写入包文件
                                    fs.Position = blockStartIndex;

                                    var packageFilePath = Path.Combine(_baseDir, package.Key, $"{block.Index}.f");
                                    using (FileStream packageStream = new FileStream(packageFilePath, FileMode.OpenOrCreate, FileAccess.Write))
                                    {
                                        packageStream.Position = blockStartIndex;
                                        packageStream.Write(buffer, 0, buffer.Length);
                                    }

                                    // 更新当前块信息
                                    block.Hash = blockHash;
                                    block.Size = blockLength;
                                    block.EndIndex = blockEndIndex;
                                    blocksetDb.Update(block);

                                    blockIndex = block.Index + 1;

                                    // 继续处理剩余的块
                                    break;
                                }
                                else
                                {
                                    // 块一致，不需要处理，继续验证下一个块
                                    blockIndex++;
                                }
                            }

                            if (!blockMismatch)
                            {
                                // 如果没有块不一致，文件的其余部分处理
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

                                    string blockHash;
                                    using (SHA256 sha256 = SHA256.Create())
                                    {
                                        blockHash = BitConverter.ToString(sha256.ComputeHash(buffer)).Replace("-", "").ToLowerInvariant();
                                    }

                                    var packageFilePath = Path.Combine(_baseDir, package.Key, $"{blockIndex}.f");
                                    using (FileStream packageStream = new FileStream(packageFilePath, FileMode.Append))
                                    {
                                        packageStream.Write(buffer, 0, (int)blockLength);
                                    }

                                    // 创建块文件信息
                                    var file = new Fileset()
                                    {
                                        SourceKey = fileInfo.FullName,
                                        Created = fileInfo.CreationTime.ToUnixTime(),
                                        Updated = fileInfo.LastWriteTime.ToUnixTime(),
                                        Hash = sha256Hash,
                                        Size = fileInfo.Length,
                                        Key = $"{package.Key}/{blockIndex}.f"
                                    };
                                    filesetDb.Add(file);

                                    // 创建新的块记录
                                    var newBlock = new Blockset()
                                    {
                                        FilesetId = file.Id,
                                        Hash = blockHash,
                                        Size = blockLength,
                                        Index = blockIndex,
                                        StartIndex = blockStartIndex,
                                        EndIndex = blockEndIndex
                                    };
                                    blocksetDb.Add(newBlock);
                                    blockIndex++;
                                }
                            }
                        }

                        // 重新计算当前文件大小
                        package.Size += filesets.Sum(c => c.Size);
                        _rootPackageDb.Update(package);

                        // 更新根文件集信息
                        rootFileset.FilesetHash = sha256Hash;
                        _rootFilesetDb.Update(rootFileset);
                    }
                }
            });
        }


        // TODO 注意更新 root package 和 rootFileset 需要单独加锁更新，因为这两个表是全局的，需要保证数据的一致性

        /// <summary>
        /// 在指定的包中添加文件
        /// </summary>
        /// <param name="package"></param>
        /// <param name="fileFullName"></param>
        public static void PackageAddFile(RootPackage package, FileInfo fileInfo, string sha256Hash)
        {
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
                var filesetDb = new SqliteRepository<Fileset>(packageDbPath);
                var blocksetDb = new SqliteRepository<Blockset>(packageDbPath);

                var oldFile = filesetDb.Single(c => c.SourceKey == fileInfo.FullName);
                if (oldFile != null)
                {
                    if (oldFile.Hash == sha256Hash)
                    {
                        // 处理过且 hash 一致，直接返回
                        return;
                    }
                    else
                    {
                        // 不一致，删除包重新处理
                        PackageDeleteFile(package, fileInfo.FullName);
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

                    using (FileStream packageStream = new FileStream(packageFilePath, FileMode.Append))
                    {
                        using (FileStream fileStream = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.ReadWrite))
                        {
                            blockStartIndex = packageStream.Length;
                            fileStream.CopyTo(packageStream);
                            blockEndIndex = packageStream.Length - 1;
                        }
                    }

                    var file = new Fileset()
                    {
                        SourceKey = fileInfo.FullName,
                        Created = fileInfo.CreationTime.ToUnixTime(),
                        Updated = fileInfo.LastWriteTime.ToUnixTime(),
                        Hash = sha256Hash,
                        Size = fileSize,
                        Key = $"{package.Key}/0.f"
                    };

                    filesetDb.Add(file);

                    // 创建块
                    var bs = new Blockset()
                    {
                        FilesetId = file.Id,
                        Hash = sha256Hash,
                        Size = fileSize,
                        Index = blockIndex,
                        StartIndex = blockStartIndex,
                        EndIndex = blockEndIndex
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

                            string blockHash;
                            using (SHA256 sha256 = SHA256.Create())
                            {
                                blockHash = BitConverter.ToString(sha256.ComputeHash(buffer)).Replace("-", "").ToLowerInvariant();
                            }

                            // 写入包文件
                            var packageFilePath = Path.Combine(_baseDir, package.Key, $"{blockIndex}.f");
                            using (FileStream packageStream = new FileStream(packageFilePath, FileMode.Append))
                            {
                                packageStream.Write(buffer, 0, (int)blockLength);
                            }

                            var file = new Fileset()
                            {
                                SourceKey = fileInfo.FullName,
                                Created = fileInfo.CreationTime.ToUnixTime(),
                                Updated = fileInfo.LastWriteTime.ToUnixTime(),
                                Hash = sha256Hash,
                                Size = fileSize,
                                Key = $"{package.Key}/{blockIndex}.f"
                            };
                            filesetDb.Add(file);

                            // 创建块
                            var bs = new Blockset()
                            {
                                FilesetId = file.Id,
                                Hash = blockHash,
                                Size = blockLength,
                                Index = blockIndex,
                                StartIndex = blockStartIndex,
                                EndIndex = blockEndIndex
                            };
                            blocksetDb.Add(bs);

                            blockIndex++;
                        }

                        fs.Close();
                    }
                }


                // 更新包大小
                package.Size += fileSize;

                _rootFilesetDb.Add(new RootFileset()
                {
                    RootPackageId = package.Id,
                    FilesetSourceKey = fileInfo.FullName,
                    FilesetHash = sha256Hash
                });
                _rootPackageDb.Update(package);
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
            if (fileSize <= 1024) return "a";
            else if (fileSize <= 10 * 1024) return "b";
            else if (fileSize <= 100 * 1024) return "c";
            else if (fileSize <= 1024 * 1024) return "d";
            else if (fileSize <= 10 * 1024 * 1024) return "e";
            else if (fileSize <= 100 * 1024 * 1024) return "f";
            else if (fileSize <= 1024 * 1024 * 1024) return "g";
            else if (fileSize <= 10 * 1024 * 1024 * 1024L) return "h";
            else if (fileSize <= 100 * 1024 * 1024 * 1024L) return "i";
            else if (fileSize <= 1024 * 1024 * 1024 * 1024L) return "j";
            else throw new NotSupportedException("不支持大于 1TB 的文件");
        }

        /// <summary>
        /// 计算文件的 SHA256 哈希值
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public static string ComputeSha256(string filePath)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                using (FileStream fileStream = File.OpenRead(filePath))
                {
                    byte[] hashBytes = sha256.ComputeHash(fileStream);
                    return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                }
            }
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

        public static void UpdateIndexAndVerify(string packagePath, string category)
        {
            string indexFile = Path.Combine(_baseDir, $"{category}.f");

            // 更新 index 文件
            File.AppendAllText(indexFile, packagePath + Environment.NewLine);

            // 验证 index 文件
            VerifyIndexFile(indexFile);
        }

        public static void VerifyIndexFile(string indexFile)
        {
            // 简单校验逻辑示例，可以根据实际需求进行更复杂的校验
            if (!File.Exists(indexFile))
            {
                throw new FileNotFoundException("Index file not found");
            }
            else
            {
                Console.WriteLine("Index file verification passed");
            }
        }
    }
}