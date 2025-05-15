# MDriveSync CLI

## 概述

MDriveSync CLI 是一个功能强大的命令行工具，用于执行多平台文件同步操作。该工具提供了灵活的配置选项，支持多种同步模式、文件比较方法和哈希算法，适用于各种文件同步和备份场景。

## 安装


### 使用 .NET Global Tool（推荐）

```bash
# 安装
dotnet tool install -g mdrive

# 升级
dotnet tool update -g mdrive

# 同步
mdrive sync -s /a -t /b
```

### 发布

发布到 newget.org

发布后，将 Release 目录下的 newgut.1.1.0.nupkg 上传到 newget.org

```bash
dotnet pack -c Release
dotnet nuget push ./bin/Release/mdrive.1.1.0.nupkg -k xxx -s https://api.nuget.org/v3/index.json

dotnet pack --configuration Release
dotnet nuget push bin/Release/mdrive.1.1.0.nupkg --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json
```

### 直接下载二进制文件

从 [GitHub Releases](https://github.com/trueai-org/mdrive/releases) 下载最新版本的二进制文件。

```shell
# 下载最新版本
# 解压后使用命令行运行
dotnet MDriveSync.Cli.dll [命令] [选项]
```

## 基本命令

MDriveSync CLI 提供了以下主要命令：


```
多平台文件同步工具

用法:
  mdrive [命令] [选项]

命令:
  sync     执行文件同步操作
  config   管理同步配置文件
  version  显示程序版本信息
  exit     退出程序 (仅在开发模式下有效)
  quit     退出程序 (仅在开发模式下有效)

选项:
  --help, -h     显示帮助信息
  --dev          开发模式，交互式运行程序
```

## 同步命令 (sync)

同步命令用于在两个目录之间执行文件同步操作。


```
sync - 执行文件同步操作

用法:
  mdrive sync [选项]

选项:
  --source, -s                源目录路径 (必需)
  --target, -t                目标目录路径 (必需)
  --mode, -m                  同步模式: OneWay(单向), Mirror(镜像), TwoWay(双向) (默认: OneWay)
  --compare, -c               比较方法: Size(大小), DateTime(修改时间), DateTimeAndSize(时间和大小), Content(内容), Hash(哈希) (默认: DateTimeAndSize)
  --hash                      哈希算法: MD5, SHA1, SHA256(默认), SHA3, SHA384, SHA512, BLAKE3, XXH3, XXH128
  --sampling-rate             哈希抽样率 (0.0-1.0之间的小数，默认: 0.1)
  --sampling-min-size         参与抽样的最小文件大小 (字节，默认: 1MB)
  --date-threshold            修改时间比较阈值 (秒，默认: 0)
  --parallel                  是否启用并行文件操作 (默认: true)
  --config, -f                配置文件路径, 示例: -f sync.json
  --exclude, -e               排除的文件或目录模式 (支持通配符，可多次指定)
  --preview, -p               预览模式，不实际执行操作 (默认: false)
  --verbose, -v               显示详细日志信息 (默认: false)
  --threads, -j               并行操作的最大线程数 (默认: CPU核心数)
  --recycle-bin, -r           使用回收站代替直接删除文件 (默认: true)
  --preserve-time             保留原始文件时间 (默认: true)
  --continue-on-error         发生错误时是否继续执行 (默认: true)
  --retry                     操作失败时的最大重试次数 (默认: 3)
  --conflict                  冲突解决策略: SourceWins, TargetWins, KeepBoth, Skip, Newer(默认), Older, Larger
  --follow-symlinks           是否跟踪符号链接 (默认: false)
  --interval, -i              同步间隔, 单位秒
  --cron                      Cron表达式，设置后将优先使用Cron表达式进行调度
  --execute-immediately, -ei  配置定时执行时，是否立即执行一次同步 (默认: true)
  --chunk-size, --chunk       文件同步分块大小（MB），大于0启用分块传输
  --sync-last-modified-time   同步完成后是否同步文件的最后修改时间 (默认: true)
  --temp-file-suffix          临时文件后缀 (默认: .mdrivetmp)
  --verify-after-copy         文件传输完成后验证文件完整性 (默认: true)
  --help                      显示帮助信息

```

### 示例


```shell
# 基本单向同步
mdrive sync --source "D:\Documents" --target "E:\Backup\Documents"

# 使用镜像模式和SHA1哈希算法
mdrive sync --source "D:\Projects" --target "E:\Backup\Projects" --mode Mirror --hash SHA1

# 排除特定文件和目录
mdrive sync --source "D:\Photos" --target "E:\Backup\Photos" --exclude "*.tmp" --exclude "**/Thumbs.db"

# 使用预览模式
mdrive sync --source "D:\Music" --target "E:\Backup\Music" --preview

# 从配置文件加载同步选项
mdrive sync --source "D:\Videos" --target "E:\Backup\Videos" --config "sync.config.json"

# 使用高级选项
mdrive sync --source "D:\Work" --target "E:\Backup\Work" --mode TwoWay --compare Hash --threads 4 --verbose

```

## 配置命令 (config)

配置命令用于管理同步配置文件。


```
config - 管理同步配置文件

用法:
  mdrive config [子命令] [选项]

子命令:
  create    创建新的配置文件
  view      查看现有配置文件内容

选项:
  --help    显示帮助信息

```

### 创建配置文件


```
create - 创建新的配置文件

用法:
  mdrive config create [选项]

选项:
  -o, --output <OUTPUT>    输出文件路径 (必需)
  -s, --source <SOURCE>    源目录路径 (必需)
  -t, --target <TARGET>    目标目录路径 (必需)
  -m, --mode <MODE>        同步模式：OneWay(单向)、Mirror(镜像)、TwoWay(双向) [默认: OneWay]
  --help                   显示帮助信息

```

### 查看配置文件


```
view - 查看现有配置文件内容

用法:
  mdrive config view [选项]

选项:
  -f, --file <FILE>    配置文件路径 (必需)
  --help               显示帮助信息

```

### 示例


```shell
# 创建新的配置文件
mdrive config create --output "sync.config.json" --source "D:\Documents" --target "E:\Backup\Documents" --mode Mirror

# 查看配置文件内容
mdrive config view --file "sync.config.json"
```

## 版本命令 (version)

显示程序版本信息。


```shell
mdrive version

```

## 同步模式说明

MDriveSync 支持以下同步模式：

- **OneWay (单向)**: 从源目录到目标目录的单向同步，仅更新目标目录中的文件。
- **Mirror (镜像)**: 使目标目录成为源目录的精确副本，包括删除目标目录中多余的文件。
- **TwoWay (双向)**: 在源目录和目标目录之间进行双向同步，保持两个目录的内容一致。

## 文件比较方法

可用的文件比较方法包括：

- **Size**: 仅比较文件大小
- **DateTime**: 仅比较修改时间
- **DateTimeAndSize**: 比较修改时间和文件大小（默认）
- **Content**: 比较文件内容（读取文件）
- **Hash**: 使用哈希算法比较

## 哈希算法

支持的哈希算法包括：

- **MD5**
- **SHA1**
- **SHA256**
- **SHA384**

## 配置文件格式

配置文件使用 JSON 格式，包含以下主要选项：


```json
{
  "SourcePath": "D:\\Source",
  "TargetPath": "E:\\Target",
  "SyncMode": "OneWay",
  "CompareMethod": "DateTimeAndSize",
  "HashAlgorithm": "SHA256",
  "MaxParallelOperations": 8,
  "PreviewOnly": false,
  "UseRecycleBin": true,
  "PreserveFileTime": true,
  "IgnorePatterns": [
    "**/System Volume Information/**",
    "**/$RECYCLE.BIN/**",
    "**/Thumbs.db",
    "**/*.tmp",
    "**/*.temp",
    "**/*.bak"
  ]
}

```

## 排除模式

您可以使用通配符来指定要排除的文件或目录：

- `*` 匹配任意数量的字符（不包括目录分隔符）
- `**` 匹配任意数量的字符（包括目录分隔符）
- `?` 匹配单个字符

示例：
- `*.tmp` - 排除所有 .tmp 文件
- `**/node_modules/**` - 排除所有 node_modules 目录及其内容
- `backup/*` - 排除 backup 目录下的所有文件

## 日志

MDriveSync CLI 会输出日志到控制台，并存储在 `logs/mdrivesync-cli.log` 文件中。使用 `--verbose` 选项可以获取更详细的日志信息。

## 许可证

MDriveSync CLI 是一款开源工具，遵循 [MIT 许可证](https://opensource.org/licenses/MIT)。