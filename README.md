# MDrive

任何第三方、服务商都无法查阅备份数据，保护您的数据安全和隐私。

多平台、模块化、可挂载、安全、加密的云盘自动同步、备份工具，支持本地存储、阿里云盘等，支持镜像、备份等同步模式，完全免费开源。

支持数据同步、文件加密、文件名加密、差异备份、增量备份、定时备份、版本历史、在线下载、文件管理等功能，支持多账号、多作业计划、多线程上传、下载、同步等功能，支持秒传、加密、压缩等功能。

提供 Docker、Windows、Linux、macOS、Web、Cli 等多平台版本。

- 加密算法：**AES256-GCM（Default）、ChaCha20-Poly1305。**
- 压缩算法：**Zstd（Default）、LZ4、Snappy。**
- 哈希算法：**SHA256（Default）、SHA1、SHA3、SHA384、SHA512、MD5、XXH3、XXH128、BLAKE3。**
- 分块算法：**Buzhash+（Default）、FastCDC+、FastCDC。**
- 分块策略：**Balanced（Default）、Dynamic、Fixed。**

## 预览

> 账号：admin，密码：123456

<http://g.googlec.cc:18080/>

## 同步助手

MDriveSync CLI 是一个功能强大的命令行工具，用于执行多平台文件同步操作。该工具提供了灵活的配置选项，支持多种同步模式、文件比较方法和哈希算法，适用于各种文件同步和备份场景。

更多详情参考 [README.md](src/MDriveSync.Cli/README.md)

### 使用 .NET Global Tool（推荐）

```bash
# 安装
dotnet tool install -g mdrive

# 升级
dotnet tool update -g mdrive

# 同步
mdrive sync -s /a -t /b

# 帮助
mdirve -h
```

```bash
mdrive - 多平台文件同步工具 v1.2.0

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

使用 'mdrive [命令] --help' 查看特定命令的帮助信息

执行文件同步操作

用法:
  mdrive sync [选项]

选项:
  --source, -s                源目录路径 (必需)
  --target, -t                目标目录路径 (必需)
  --mode, -m                  同步模式: OneWay(单向), Mirror(镜像), TwoWay(双向) (默认: OneWay)
  --compare, -c               比较方法: Size(大小), DateTime(修改时间), DateTimeAndSize(时间和大小), Content(内容), Hash(哈希) (默认: DateTimeAndSize)
  --hash, -h                  哈希算法: MD5, SHA1, SHA256(默认), SHA3, SHA384, SHA512, BLAKE3, XXH3, XXH128
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


管理同步配置文件

用法:
  mdrive config [子命令] [选项]

子命令:
  create    创建新的配置文件
  view      查看现有配置文件内容

使用 'mdrive config [子命令] --help' 查看特定子命令的帮助信息

示例:
mdrive sync --source C:\Source --target D:\Targetd
```

## 截图

![作业](/docs/screenshots/job.gif)
![挂载](/docs/screenshots/mount.png)
![macOS](/docs/screenshots/macOS.png)

## 安装与使用

### 快速启动

> Docker 版本

```bash
# 阿里云镜像（推荐国内使用）
docker pull registry.cn-guangzhou.aliyuncs.com/trueai-org/mdrive
docker run --name mdrive -d --restart=always \
 -e BASIC_AUTH_USER=admin -e BASIC_AUTH_PASSWORD=123456 \
 -p 8080:8080 --user root \
 -e TZ=Asia/Shanghai \
 -v /etc/localtime:/etc/localtime:ro \
 -v /etc/timezone:/etc/timezone:ro \
 registry.cn-guangzhou.aliyuncs.com/trueai-org/mdrive

# Docker Hub 镜像
docker pull trueaiorg/mdrive
docker run --name mdrive -d --restart=always \
 -e BASIC_AUTH_USER=admin -e BASIC_AUTH_PASSWORD=123456 \
 -p 8080:8080 --user root \
 -e TZ=Asia/Shanghai \
 -v /etc/localtime:/etc/localtime:ro \
 -v /etc/timezone:/etc/timezone:ro \
 trueaiorg/mdrive

# GitHub 镜像
docker pull ghcr.io/trueai-org/mdrive
docker run --name mdrive -d --restart=always \
 -e BASIC_AUTH_USER=admin -e BASIC_AUTH_PASSWORD=123456 \
 -p 8080:8080 --user root \
 -e TZ=Asia/Shanghai \
 -v /etc/localtime:/etc/localtime:ro \
 -v /etc/timezone:/etc/timezone:ro \
 ghcr.io/trueai-org/mdrive

# 演示站点启动配置
docker pull registry.cn-guangzhou.aliyuncs.com/trueai-org/mdrive
docker run --name mdrive -d --restart=always \
 -e BASIC_AUTH_USER=admin -e BASIC_AUTH_PASSWORD=123456 \
 -e DEMO=true \
 -p 8080:8080 --user root \
 -v /root/mdrive/data:/app/data:rw \
 -e TZ=Asia/Shanghai \
 -v /etc/localtime:/etc/localtime:ro \
 -v /etc/timezone:/etc/timezone:ro \
 registry.cn-guangzhou.aliyuncs.com/trueai-org/mdrive
```

> Windows 版本

```bash
a. 通过 https://github.com/trueai-org/mdrive/releases 下载 windows 最新免安装版，例如：MDrvie-win-x64.zip
b. 解压并执行 MDriveSync.Client.API.exe
c. 打开网站 http://localhost:8080/
d. 安装为系统服务（可选），右键文件以管理员身份运行 `一键安装或卸载*.bat`，选择安装或卸载服务。
e. 部署到 IIS（可选），在 IIS 添加网站，将文件夹部署到 IIS，配置应用程序池为`无托管代码`，启动网站。
f. 使用系统自带的 `任务计划程序`（可选），创建基本任务，选择 `.exe` 程序即可，请选择`请勿启动多个实例`，保证只有一个任务执行即可。
g. 磁盘挂载支持（可选），下载并安装驱动（http://localhost:8080/driver/Dokan_x64.msi），将云盘挂载到本地，像管理本地文件一样管理远程文件。
```

> Linux 版本

```bash
a. 通过 https://github.com/trueai-org/mdrive/releases 下载 linux 最新免安装版，例如：MDrive-linux-x64.zip
b. 解压到当前目录: tar -xzf MDrive-linux-x64-<VERSION>.tar.gz
c. 执行: run_app.sh
c. 启动方式1: sh run_app.sh
d. 启动方式2: chmod +x run_app.sh && ./run_app.sh
```
> macOS 版本

```bash
a. 通过 https://github.com/trueai-org/mdrive/releases 下载 macOS 最新免安装版，例如：MDrive-osx-x64.zip
b. 解压到当前目录: tar -xzf MDrive-osx-x64-<VERSION>.tar.gz
c. 执行: run_app_osx.sh
c. 启动方式1: sh run_app_osx.sh
d. 启动方式2: chmod +x run_app_osx.sh && ./run_app_osx.sh
```

### 压缩与加密

- [对称加密方法](https://soatok.blog/2020/07/12/comparison-of-symmetric-encryption-methods/) https://soatok.blog/2020/07/12/comparison-of-symmetric-encryption-methods/
- [AEAD 加密](https://libsodium.gitbook.io/doc/secret-key_cryptography/aead) https://libsodium.gitbook.io/doc/secret-key_cryptography/aead
- [压缩分析](https://kopia.io/docs/advanced/compression/) https://kopia.io/docs/advanced/compression/
- [压缩算法](https://tech.meituan.com/2021/01/07/pack-gzip-zstd-lz4.html) https://tech.meituan.com/2021/01/07/pack-gzip-zstd-lz4.html
- [LZ4 - lz4.org](https://github.com/lz4/lz4) https://github.com/lz4/lz4
- [Facebook zstd](https://github.com/facebook/zstd) https://github.com/facebook/zstd
- [Google snappy](https://github.com/google/snappy) https://github.com/google/snappy
 
![文件加密模式](/docs/screenshots/file-encrypt.png)

![文件打包加密模式](/docs/screenshots/package-encrypt.png)

### 哈希校验

> 测试环境

- 操作系统: Windows 10.0.22621.0
- 处理器: i7 8700K
- 测试数据大小: 100 MB
- 测试时间: 2025/5/8 18:19:50

| 算法名称 | 哈希宽度 (位) | 平均耗时 (ms) | 吞吐量 (GB/s) |
|----------|---------------|---------------|---------------|
| XXH3     | 64            | 6.80          | 15.66         |
| XXH128   | 128           | 6.30          | 15.50         |
| BLAKE3   | 256           | 19.90         | 4.91          |
| SHA-256  | 256           | 41.50         | 2.35          |
| SHA1     | 160           | 103.40        | 0.94          |
| SHA3     | 384           | 133.80        | 0.73          |

### Docker 部署

近期 Docker Hub 国内无法使用，可以使用 https://docker.m.daocloud.io 加速，或使用阿里云镜像。

https://hub.docker.com/r/trueaiorg/mdrive

**从阿里云 Registry 中拉取镜像**

> docker pull registry.cn-guangzhou.aliyuncs.com/trueai-org/mdrive:[镜像版本号]

**从 Docker Hub 中拉取镜像**

> docker pull trueaiorg/mdrive:[镜像版本号]

**从 GitHub Registry 中拉取镜像**

> docker pull ghcr.io/trueai-org/mdrive:[镜像版本号]


```
# 拉取镜像
docker pull trueaiorg/mdrive

# 快速启动并开启只读模式
docker run --name mdrive -d --restart=always \
 -e BASIC_AUTH_USER=admin -e BASIC_AUTH_PASSWORD=123456 \
 -e READ_ONLY=true \
 -p 8080:8080 trueaiorg/mdrive

# 快速启动完整示例2，持久化、映射配置、只读、账号密码
# 如果需要，更改目录权限
sudo chmod -R 777 /home/mdrive/db
docker run --name mdrive -d --restart=always \
 -v /home/mdrive/appsettings.json:/app/appsettings.json:rw \
 -v /home/mdrive/db:/app/db:rw \
 -e BASIC_AUTH_USER=admin -e BASIC_AUTH_PASSWORD=123456 \
 -e READ_ONLY=true \
 -p 18080:8080 --user root trueaiorg/mdrive

# 确保目录存在
# 确保映射/挂载了备份目录
# 创建云盘存储目录
mkdir /home/mdrive
cd /home/mdirve

# 创建配置文件（可选）
vi appsettings.json

# 确保配置具有可写配置权限 appsettings.json
chmod 666 appsettings.json

# 快速启动示例，并挂载 /data 目录到容器 /data 只读模式，并映射端口 8080
docker run --name mdrive -d --restart=always \
 -v /home/mdrive/appsettings.json:/app/appsettings.json:rw \
 -v /data:/data:ro \
 -e BASIC_AUTH_USER=admin -e BASIC_AUTH_PASSWORD=123456 \
 -p 8080:8080 --user root trueaiorg/mdrive

# 调试日志
docker logs mdrive

# 进入容器
docker exec -it mdrive /bin/bash

# 访问端口
http://{ip}:8080

# 更多示例
# 配置日志、映射云盘配置、映射程序配置、挂载 /data
mkdir /home/mdrive/logs
docker run --name mdrive -d --restart=always \
 -v /home/mdrive/appsettings.json:/app/appsettings.json:rw \
 -v /home/mdrive/logs:/app/logs \
 -v /home/mdrive/db:/app/db \
 -v /data:/data:ro \
 -e BASIC_AUTH_USER=admin -e BASIC_AUTH_PASSWORD=123456 \
 -p 8080:8080 --user root trueaiorg/mdrive

# 更多权限，容器内以 root 用户启动：--user root

```

### Windows 服务部署

下载 `MDrive` 并解压，运行 `.exe` 程序即可。

- 作为服务后台执行：
  - 可使用系统自带的 `任务计划程序`，创建基本任务，选择 `.exe` 程序即可，请选择`请勿启动多个实例`，保证只有一个任务执行即可。
  - 可使用其他服务后台启动，例如：nssm、sc、winsw、iis 等。

## 特性

- [√] 不限速，阿里云盘官方接口支持，上传下载均不限速。
- [√] 挂载云盘支持，支持将云盘挂载到本地，作为本地硬盘使用。
- [√] 多线程支持，多线程上传、下载、同步，充分利用带宽。
- [√] 定时作业，定时同步、还原、备份等。
- [√] 在线云盘文件管理。
- [√] 免费、开源
- [√] 跨平台，支持 Windows、Linux、Unix、Mac、Android、Docker 等平台。
- [√] 快速校验，多种算法支持 sha1、sha256、md5、blake3 等。
- [√] 安全，加密
- [√] 多账号支持，多阿里云盘账号支持
- [√] 多作业计划支持，可以配置多个同步时间点。
- [√] 多种同步方式：镜像、备份
- [√] 极速，采用安全极速的差异算法，极速实现同步。
- [√] 支持秒传
- [√] 支持过滤本地文件/文件夹，丰富的校验规则。
- [√] 高性能，采用 .NET8 最新技术，极致的性能体现，低内存、高性能、跨平台。
- [√] WebUI 可视化配置
- [√] WebUI 前后端分离，保证了后台服务的高可用。
- [√] 支持只读模式，只读模式启动服务，则不可以编辑配置。
- [√] 支持自定义端口。
- [√] 支持回收站功能，支持删除文件/夹到回收站。
- [√] WebUI 支持多主题，支持黑色模式。
- [√] 支持云盘管理和在线下载文件。
- [√] 支持作业暂停、恢复、禁用、取消、删除等。
- [√] 支持队列，保证作业的高可用，避免多任务卡顿以及抢占资源问题。
- [√] 支持登录验证，下载验证。
- [√] 支持从配置文件中启动作业。
- [√] 支持将云盘挂载到本地，像管理本地文件一样管理远程文件。
- [√] 支持将备份目录挂载到本地。
- [√] 支持快照，支持快照挂载。
- [√] 支持导入/导出配置。
- [√] 支持还原/下载云盘文件。

## 启动配置

- 只读模式：WebUI 下如果开启只读模式，则允许编辑和修改，只能查看，默认 `ReadOnly: false`。使用方式，可以通过修改 `appsettings.json` 或 docker 使用环境变量 `-e READ_ONLY=true`。
- 基础认证：WebUI 账号和密码，如果开启则打开网站管理后台时需要输入账号和密码，默认启用 `BasicAuth`。使用方式，可以通过修改 `appsettings.json` 或 docker 使用环境变量 ` -e BASIC_AUTH_USER=admin -e BASIC_AUTH_PASSWORD=123456`。
- 演示模式：网站配置为演示模式，只能查看和下载，功能受限，默认 `Demo: null`。使用方式，可以通过修改 `appsettings.json` 或 docker 使用环境变量 `-e DEMO=true`。
- 调试模式：非正式版（alpha、beta、rc）默认输出调试错误代码行号，正式版不包含 `.pdb` 文件。

## 系统配置

> 默认 `appsettings.json` 与 `appsettings.Example.json` 示例配置。

> 如果您通过 `Docker` 启动或简单使用则无需关心此配置。

```json
# appsettings.Example.json 示例配置
{
  // 网站配置为只读模式
  "ReadOnly": null,

  // 网站配置为演示模式
  "Demo": null,

  // 网站登录授权账号密码
  "BasicAuth": {
    "User": "",
    "Password": ""
  },
  "Client": {
    // 阿里云盘默认启动时加载的配置项（可选）
    "AliyunDrives": [
      {
        "Id": "1",
        "Name": "云盘1",
        "TokenType": "Bearer",
        "AccessToken": "",
        "RefreshToken": "【1】这里输入授权令牌",
        "ExpiresIn": 7200,
        "Jobs": [
          {
            "Id": "1",
            "Name": "test",
            "Description": "",
            "State": 0,
            "Schedules": [
              "【2】这里是备份计划时间，例如每分钟执行：0 * * * * ?"
            ],
            "Filters": [
              "/Recovery/",
              "/System Volume Information/",
              "/Boot/",
              "/$RECYCLE.BIN/",
              "/@Recycle/",
              "/@Recently-Snapshot/",
              "**/node_modules/**",
              "*.duplicatidownload"
            ],
            "Sources": [
              "【3】这里输入备份的文件夹，可填写多个路径，注意：windows 路径，例如：E:\\test"
            ],
            "Target": "【4】这里输入云盘备份目录，例如：backups/test",
            "Restore": "E:\\kopia_restore",
            "RapidUpload": true,
            "DefaultDrive": "backup",
            "CheckAlgorithm": "sha256",
            "CheckLevel": 1,
            "FileWatcher": true,
            "Order": 0,
            "IsTemporary": false,
            "IsRecycleBin": false,
            "UploadThread": 0,
            "DownloadThread": 0
          }
        ]
      }
    ]
  },
  // 日志输出
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Default": "Warning",
        "System": "Warning",
        "Microsoft": "Warning"
      }
    },
    "WriteTo": [
      // 是否将日志输出到文件
      {
        "Name": "File",
        "Args": {
          "path": "logs/log.txt",
          "rollingInterval": "Day",
          "fileSizeLimitBytes": null,
          "rollOnFileSizeLimit": false,
          "retainedFileCountLimit": 31
        }
      },
      // 是否将日志输出到控制台
      {
        "Name": "Console"
      }
    ]
  },
  // 系统日志配置
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "urls": "http://*:8080" // 默认程序启动的端口
}
```

> 注意系统日志路径配置，不同操作系统之间的差异。

```json
# window
"path": "logs\\log.txt"

# linux
"path": "logs/log.txt"
```

> 默认 `Client` 配置（可选，默认无需配置）

- `RefreshToken` 为必填项，其他不用填写。[点击获取授权](https://openapi.alipan.com/oauth/authorize?client_id=12561ebaf6504bea8a611932684c86f6&redirect_uri=https://api.duplicati.net/api/open/aliyundrive&scope=user:base,file:all:read,file:all:write&relogin=true)令牌，或登录官网获取授权令牌。
- `Jobs` 可以配置多个作业，计划中的作业时间可以可以配置多个时间点。
  - `Sources` 备份源目录列表，可以配置多个备份源，必填
  - `Target` 云盘存储目录，必填
  - `Schedules` 定时计划，可配置多个计划时间
  - `IsTemporary` 是否为临时任务或一次性的同步任务，也表示是否立即执行，如果为 `true`，则启动时立即执行作业
  - `State` 作业状态，例如：100 表示暂停，0 表示未开始
  - `Mode` 同步模式，0 镜像同步（以本地为主，远程为镜像，删除不一致冗余的远程文件），1 冗余同步（同步到远程，不删除远程文件）

```json
{
  "Client": { // 客户端备份/同步配置
    "AliyunDrives": [ // 阿里云盘配置
      {
        "Name": "云盘1", // 云盘名称
        "TokenType": "Bearer", // 令牌类型，这里是Bearer类型
        "AccessToken": "your_access_token", // 访问令牌，用于API访问
        "RefreshToken": "your_refresh_token", // 【必填】刷新令牌，用于获取新的访问令牌
        "ExpiresIn": 7200, // 令牌过期时间，单位为秒
        "Metadata": "", // 阿里云盘元信息，如用户信息、云盘信息、VIP信息等
        "Jobs": [ // 作业列表
          {
            "Id": "1", // 任务 ID
            "Name": "gpkopia", // 任务/作业名称
            "Description": "", // 作业描述
            "State": 0, // 作业状态，例如：100 表示暂停，0 表示未开始
            "Mode": 0, // 同步模式，0 镜像同步，1 冗余同步
            "Schedules": [ // 【必填】定时计划，使用cron表达式定义
              "0 0/10 * * * ?"
            ],
            "Filters": [ // 文件过滤列表
              "**/logs/*"
            ], 
            "Sources": [ // 【必填】源目录列表
              "E:\\kopia"
            ],
            "Target": "backups/gp", // 【必填】目标存储目录
            "Restore": "E:\\kopia_restore", // 还原目录
            "RapidUpload": true, // 是否启用秒传功能
            "DefaultDrive": "backup", // 默认备份的云盘类型，备份盘或资源盘
            "CheckAlgorithm": "sha256", // 文件对比检查算法
            "CheckLevel": 1, // 文件差异算法检查级别
            "FileWatcher": true, // 是否启用文件系统监听
            "Order": 0, // 任务显示顺序
            "IsTemporary": false, // 是否为临时任务，是否立即执行
            "IsRecycleBin": false, // 是否启用删除到回收站
            "UploadThread": 0, // 上传并行任务数
            "DownloadThread": 0, // 下载并行任务数
            "Metadata": "" // 作业元信息
          },
          // 更多作业配置...
        ]
      }
    ]
  }
}

```

```json
# 管理后台账号密码
# 可以通过配置，直接修改管理后台账号密码。在 docker 启动时，也可以通过环境变量设置。
{
  "BasicAuth": {
    "User": "admin",
    "Password": "123456"
  }
}
```

> `Schedules` 作业计划任务示例

```csharp
// cron 表达式 基于 Quartz 3.8.0
// https://www.bejson.com/othertools/cron/

// 每 5 秒
0/5 * * * * ?

// 每分钟
0 * * * * ?

// 每 5 分钟
0 0/5 * * * ?

// 每 10 分钟
0 0/10 * * * ?

// 每天 9 点
0 0 9 * * ?

// 每天 8 点 10 分
0 10 8 * * ?
```

> `Filters` 过滤文件/文件夹示例

```bash
# 忽略文件示例
# 忽略 `.exe` 结尾的所有文件
*.duplicatidownload
*.exe                       

# 忽略根目录示例，以 `/` 开头表示当前备份/同步的根目录
# 忽略当前备份/同步的根目录 `/Recovery/` 下的文件夹和文件
/Recovery/*                 
/Recovery/
/System Volume Information/
/System Volume Information/*
/Boot/
/Boot/*
/$RECYCLE.BIN/*
/$RECYCLE.BIN/
/bootmgr
/bootTel.dat
/@Recycle/*
/@Recently-Snapshot/*
/@Recycle/
/@Recently-Snapshot/

# 忽略根目录和子目录示例
# 忽略当前备份/同步的目录下 `/.next/` 目录以及子目录下的所有文件夹和文件
**/@Recycle/*
**/@Recently-Snapshot/*
**/.@__thumb/*
**/@Transcode/*
**/.obsidian/*
**/.git/*
**/.svn/*
**/node_modules/*
**/bin/Debug/*
**/bin/Release/*
**/logs/*
**/obj/*
**/packages/*
**/.next/*              
```


## 路线图

- [3.5] 支持 Linux 磁盘挂载。
- [3.x] 支持超时自动锁定管理后台。
- [3.x] 支持在线上传。
- [3.x] 多模块支持，支持 Duplicati、Kopia 模块，直接进行加密。
- [3.x] 多版本、快照模式
- [3.x] Kopia 模式、插件开发
- [3.x] 百度云盘集成、本地模式集成、阿里云 OSS、腾讯云 COS 等
- [3.x] 上传增加文件的本地时间
- [3.x] WebDAV 支持
- [3.x] 多版本、多备份、版本还原
- [3.x] 配置加密、作业加密
- [3.x] 支持自定义文件后缀。避免被任何文件分析。
- [3.x] 支持多个备份点，避免快照数据损坏（待定），支持多重备份点。
- [3.x] 还原时恢复原始权限和时间
- [3.x] Duplicati 版（自动加密）
- [3.x] Kopia 版（自动加密）
- [3.x] 提供 Mac 版、手机版（Andorid 版、IOS 版）
- ~~[验证] 空文件、空文件夹也同步~~
- ~~[验证] 快捷方式支持同步，注意可能存在兼容性问题~~
- [修复] 指向同一目标 bug -> https://github.com/trueai-org/MDriveSync/issues/3
- [优化] 优化挂载读写性能
- [优化] 优化磁盘挂载支持秒传
- [新增] 增加 linux/unix/mac 磁盘挂载支持
- [BUG] 多个源文件夹名称一致校验
- [优化] 添加自定义挂载磁盘名称支持 -> https://github.com/trueai-org/mdrive/issues/15

## GUI 路线图

- [3.x] WebUI 版本更新提示，支持热更新支持，使用子程序提供热更新服务。
- App 启动时，通过接口获取欢迎语，检查版本等
- App 增加下次作业时间
- App 增加公告
- App 增加上次执行结果
- Mac 版本、移动端打包、Windows 安装包、WPF 安装包
- 锁定模式，超时多少时间自动退出登录
- Windows 客户端 UI/WPF 版本

## 发布

通过 `Github Actions` 自动或生成到 `Docker Hub`、`ghci.io`、`Window` 安装包等，保证程序的安全。

## 性能

- 极致，待完善，仅供参考。

```txt
大小 29.7 GB, 1,467 个文件，3,640 个文件夹
首次同步：0.8秒 + 8秒（列表加载用时 9 秒）

[11:14:01 INF] Current: E:\***\publish
[11:14:01 INF] 作业初始化
[11:14:02 INF] 同步作业开始：12/27/2023 11:14:02
[11:14:02 INF] 云盘存储根目录初始化完成，用时：194ms
[11:14:02 INF] 开始加载云盘存储文件列表
[11:14:12 INF] 云盘文件加载完成，包含 1467 个文件，3643 个文件夹。
[11:14:12 INF] 加载云盘存储文件列表完成，用时：9835ms
[11:14:12 INF] 开始执行同步
[11:14:13 INF] 扫描本地文件，总文件数：1467, 扫描文件用时: 385.6735ms
[11:14:13 INF] 同步文件夹中 4/3641，用时：1.7352ms，kopia/p6c/f66
...
[11:14:13 INF] 同步文件夹中 3639/3641，用时：796.4739ms，kopia/p8a/032
[11:14:13 INF] 同步文件夹完成，总文件夹数：1467，用时：798.2616ms
[11:14:13 INF] 同步文件中 1/1467，用时：43.6954ms，kopia/p9e/80a/bd40cee2b0538167b1ac0cfe3f3-s9a17ce26b1838062122.f
...
[11:14:30 INF] 同步文件中 1467/1467，用时：17063.5177ms，kopia/p65/ef2/046536ebd68a2b0ec57a3f733ff-s9a17ce26b1838062122.f
[11:14:30 INF] 同步文件完成，总文件数：1467，用时：17063.8004ms
[11:14:30 INF] 同步作业完成，用时：18250ms
[11:14:30 INF] 同步作业结束：12/27/2023 11:14:30
[11:14:31 INF] 同步作业校验完成，用时：2ms

第二次同步：总用时 2 秒（列表加载用时 10 秒）

[11:19:01 INF] 开始例行检查
[11:20:00 INF] 同步作业开始：12/27/2023 11:20:00
[11:20:00 INF] 云盘存储根目录初始化完成，用时：367ms
...
[11:20:10 INF] 云盘文件加载完成，包含 1467 个文件，3643 个文件夹。
[11:20:10 INF] 加载云盘存储文件列表完成，用时：10047ms
[11:20:10 INF] 开始执行同步
[11:20:10 INF] 扫描本地文件，总文件数：1467, 扫描文件用时: 322.6471ms
[11:20:10 INF] 同步文件夹中 2/3641，用时：0.0552ms，kopia/p6c/f66
...
[11:20:12 INF] 同步文件夹完成，总文件夹数：1467，用时：1450.7582ms
...
[11:20:12 INF] 同步文件中 1467/1467，用时：519.6538ms，kopia/qd1/275/b7e487081d0ed47ea0638caa116-s3e0aeff9c8243770123.f
[11:20:12 INF] 同步文件完成，总文件数：1467，用时：520.4528ms
[11:20:12 INF] 同步作业完成，用时：2295ms
[11:20:12 INF] 同步作业结束：12/27/2023 11:20:12
[11:20:12 INF] 同步作业校验完成，用时：1ms
```

## 赞助

- 推广返现
- 购买阿里云盘会员返现
- 感谢所有的贡献者
- 如果您觉得本项目对您有帮助，欢迎收藏点赞，也可通过扫码购买会员，对作者提供支持！

> 限时推广返现，APP 扫码购买会员

[👍👍阿里云盘推广返现，8T云盘低至6元/月，点击购买会员支持作者，最高30%返现🌸🌸](https://www.alipan.com/cpx/member?userCode=MzAwMzE5)

![限时推广返现，最高30%返现](/docs/screenshots/aliyun.png)

> 微信返现客服

`tab-ai`

## 贡献者

感谢以下所有的贡献者！

<a href="https://github.com/616734202">
    <img src="https://github.com/616734202.png" width="68" height="68" />
</a>
<a href="https://github.com/tdsszz">
    <img src="https://github.com/tdsszz.png" width="68" height="68" />
</a>
<a href="https://github.com/Fingerbb">
    <img src="https://github.com/Fingerbb.png" width="68" height="68" />
</a>

<a href="https://github.com/trueai-org/MDriveSync/graphs/contributors"><img src="https://opencollective.com/MDriveSync/contributors.svg?width=890" /></a>

## 友情链接

- [阿里云盘小白羊网盘](https://github.com/gaozhangmin/aliyunpan) https://github.com/gaozhangmin/aliyunpan
- [阿里云盘命令行客户端](https://github.com/tickstep/aliyunpan) https://github.com/tickstep/aliyunpan
- [磁盘挂载备选方案](https://github.com/winfsp/winfsp) https://github.com/winfsp/winfsp

## 鸣谢

- 感谢阿里云盘共创团对 MDrive 的支持。
- 感谢 Duplicati 对本产品的支持。
- [WinSW](https://github.com/winsw/winsw) 系统服务，将本程序安装作为 Windows 服务运行。 