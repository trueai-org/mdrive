```csharp
// 使用 LZ4 共享实例完成压缩任务
// 创建一个新的流接收压缩 Compress 的数据
//using (FileStream fileStream = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read))
//using (MemoryStream compressedStream = new MemoryStream())
//{
//    blockStartIndex = packageStream.Length;

//    // 将文件流压缩到内存流

//    LZ4Compressor.Shared.Compress(fileStream, compressedStream);
//    compressedStream.Position = 0;
//    compressedStream.CopyTo(packageStream);

//    blockSize = compressedStream.Length;
//    blockEndIndex = packageStream.Length - 1;
//}

```