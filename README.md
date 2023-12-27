# MDriveSync

多平台、模块化、安全的云盘同步工具备份，支持百度网盘、阿里云盘等，集成 Duplicati、Kopia 等多种模块，支持加密还原等，支持单向、镜像、双向等同步备份，完全免费开源。

提供 Docker 版、Duplicati 版、Kopia 版、Windows 服务版、Windows 版、手机版、网页版、Linux版、Mac 版等多平台版本。

支持多种算法同步与备份，保证数据的安全性，任何第三方、任何云盘服务商都无法查看或分析你的数据，只有通过你本人设置的安全密钥才能解密数据，保证您的数据安全和隐私。

> 更多版本，敬请期待~~


A multi-platform, modular, secure cloud drive synchronization and backup tool that supports Baidu Cloud Disk, Alibaba Cloud Disk, and others. Integrates various modules such as Duplicati and Kopia, with features like encryption and restoration. Offers different types of synchronization and backup, including unidirectional, mirror, and bidirectional. The tool is completely free and open source.

Available in multiple platform versions including Docker, Duplicati, Kopia, Windows Service, Windows, Mobile, Web, Linux, and Mac.

Supports a variety of algorithms for synchronization and backup.

> More versions, stay tuned!

## 安装与使用

### Docker 版

https://hub.docker.com/r/trueaiorg/m-drive-sync-client

```
# 确保目录存在
# 确保映射/挂载了备份目录
# 创建云盘存储目录
mkdir /home/mdrive
cd /home/mdirve

# 创建配置文件
vi appsettings.Client.json

# 输入授权令牌，修改备份目录、作业计划时间、目标位置等
{
  "Client": {
    "AliyunDrives": [
      {
        "Id": "1",
        "Name": "云盘",
        "RefreshToken": "这里输入授权令牌",
        "Jobs": [
          {
            "Id": "1",
            "Name": "test",
            "State": 0,
            "Schedules": [
              "0 * * * * ?"
            ],
            "Sources": [
               "/data"
            ],
            "Target": "backups/test",
            "RapidUpload": true,
            "FileWatcher": true,
            "IsTemporary": true
          }
        ]
      }
    ]
  }
}

# 确保配置具有可写配置权限 appsettings.Client.json
chmod 666 appsettings.Client.json

# 拉取镜像
docker pull trueaiorg/m-drive-sync-client

# 快速启动示例，并挂载 /data 目录到容器 /data 只读模式，并映射端口 8080
docker run --name mdrive -d --restart=always \
 -v /home/mdrive/appsettings.Client.json:/app/appsettings.Client.json \
 -v /data:/data:ro \
 -p 8080:8080 trueaiorg/m-drive-sync-client

# 调试日志
docker logs mdrive

# 进入容器
docker exec -it mdrive /bin/bash

# 访问端口
http://{ip}:8080

# 跟多示例
# 配置日志、映射云盘配置、映射程序配置、挂载 /data
mkdir /home/mdrive/logs
docker run --name mdrive -d --restart=always \
 -v /home/mdrive/appsettings.json:/app/appsettings.json:rw \
 -v /home/mdrive/appsettings.Client.json:/app/appsettings.Client.json \
 -v /home/mdrive/logs:/app/logs \
 -v /data:/data:ro \
 -p 8080:8080 trueaiorg/m-drive-sync-client
```

### Windows 服务版

下载 `MDrive` 并解压，修改授权、密钥等配置，运行 `.exe` 程序即可。

- 作为服务后台执行：
  - 可使用系统自带的 `任务计划程序`，创建基本任务，选择 `.exe` 程序即可，请选择`请勿启动多个实例`，保证只有一个任务执行即可。
  - 可使用其他服务集成，例如：nssm、winsw等。

## 友情链接

- [阿里云盘小白羊网盘](https://github.com/gaozhangmin/aliyunpan) https://github.com/gaozhangmin/aliyunpan
- [阿里云盘小白羊版(暂停维护)](https://github.com/liupan1890/aliyunpan) https://github.com/liupan1890/aliyunpan
- [阿里云盘命令行客户端](https://github.com/tickstep/aliyunpan) https://github.com/tickstep/aliyunpan

## 高级配置

客户端高级配置 `appsettings.Client.json`

- `RefreshToken` 为必填项，其他不用填写。[点击获取授权](https://openapi.alipan.com/oauth/authorize?client_id=12561ebaf6504bea8a611932684c86f6&redirect_uri=https://api.duplicati.net/api/open/aliyundrive&scope=user:base,file:all:read,file:all:write&relogin=true)令牌，或登录官网获取授权令牌。
- `Jobs` 可以配置多个作业，计划中的作业时间可以可以配置多个时间点。
  - `Sources` 备份源目录列表，可以配置多个备份源，必填
  - `Target` 云盘存储目录，必填
  - `Schedules` 定时计划，可配置多个计划时间
  - `IsTemporary` 是否为临时任务或一次性的同步任务，也表示是否立即执行，如果为 `true`，则启动时立即执行作业
  - `State` 作业状态，例如：100 表示暂停，0 表示未开始
  - `Mode` 同步模式，0 镜像同步（以本地为主，远程为镜像，删除不一致冗余的远程文件），1 冗余同步（同步到远程，不删除远程文件），2 双向同步（远程与本地如果有冲突则进行重命名）

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
            "Mode": 0, // 同步模式，0 镜像同步，1 冗余同步，2 双向同步（如果有冲突则重命名）
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


## 注意

> 注意系统日志路径配置，不同操作系统之间的差异。

```json
# window
"path": "logs\\log.txt"

# linux
"path": "logs/log.txt"
```

## 路线图

- Windows 客户端 UI/WPF 版本
- WebUI 版本
- MacUI 版本
- 移动端（IOS、Andorid）版本
- Kopia 模式、插件开发
- 百度云盘集成
- 本地模式集成
- 阿里云 OSS、腾讯云 COS 等
- 加密、分块插件等
- 上传增加文件的本地时间
- WebDAV、磁盘挂载
- 多版本、多备份、版本还原
- 分块上传、分块下载、超大文件支持

## 发布

通过 `Github Actions` 自动或生成到 `Docker Hub`、`Window` 安装包等，保证程序的安全。

## 性能

- 极致，待完善，仅供参考。

```txt
大小 29.7 GB, 1,467 个文件，3,640 个文件夹
首次同步：0.8秒 + 8秒（列表加载用时 9 秒）

[11:14:01 INF] Current: E:\guanpeng\__my\MDriveSync\src\MDriveSync.Client.API\bin\Release\net8.0\publish
[11:14:01 INF] 开始例行检查
[11:14:01 INF] 作业初始化
[11:14:01 INF] Linux: False
[11:14:02 INF] 作业初始化完成，用时：1347ms
[11:14:02 INF] 作业启动中
[11:14:02 INF] 作业启动完成，用时：0ms
[11:14:02 INF] 例行检查完成
[11:14:02 INF] 同步作业开始：12/27/2023 11:14:02
[11:14:02 INF] 云盘存储根目录初始化完成，用时：194ms
[11:14:02 INF] 开始加载云盘存储文件列表
[11:14:12 INF] 云盘文件加载完成，包含 1467 个文件，3643 个文件夹。
[11:14:12 INF] 加载云盘存储文件列表完成，用时：9835ms
[11:14:12 INF] 开始执行同步
[11:14:13 INF] 扫描本地文件，总文件数：1467, 扫描文件用时: 385.6735ms
[11:14:13 INF] 同步文件夹中 4/3641，用时：1.7352ms，kopia/p6c/f66
[11:14:13 INF] 同步文件夹中 4/3641，用时：1.7352ms，kopia/xn5/_7b
[11:14:13 INF] 同步文件夹中 4/3641，用时：1.7354ms，kopia/p37/786
[11:14:13 INF] 同步文件夹中 1/3641，用时：1.7347ms，kopia/sc4/0e5
[11:14:13 INF] 同步文件夹中 5/3641，用时：1.9816ms，kopia/p8a/507
...
[11:14:13 INF] 同步文件夹中 3639/3641，用时：796.4739ms，kopia/p8a/032
[11:14:13 INF] 同步文件夹中 3640/3641，用时：796.6586ms，kopia/p03/79f
[11:14:13 INF] 同步文件夹中 3641/3641，用时：796.847ms，kopia/pd7/198
[11:14:13 INF] 同步文件夹完成，总文件夹数：1467，用时：798.2616ms
[11:14:13 INF] 同步文件中 1/1467，用时：43.6954ms，kopia/p9e/80a/bd40cee2b0538167b1ac0cfe3f3-s9a17ce26b1838062122.f
[11:14:13 INF] 同步文件中 2/1467，用时：46.7248ms，kopia/p32/927/8072e241b284b964dc4c243be92-s92bdc8b6bce76e0d123.f
[11:14:13 INF] 同步文件中 3/1467，用时：50.1298ms，kopia/p2b/e06/9d189daea0fd575d1d3af92f095-sddec03db03d4ace5122.f
...
[11:14:30 INF] 同步文件中 1466/1467，用时：17060.3177ms，kopia/pbf/0fb/8035432aee39f4ed142d17b7cca-s9a17ce26b1838062122.f
[11:14:30 INF] 同步文件中 1467/1467，用时：17063.5177ms，kopia/p65/ef2/046536ebd68a2b0ec57a3f733ff-s9a17ce26b1838062122.f
[11:14:30 INF] 同步文件完成，总文件数：1467，用时：17063.8004ms
[11:14:30 INF] 同步作业完成，用时：18250ms
[11:14:30 INF] 同步作业结束：12/27/2023 11:14:30
[11:14:31 INF] 同步作业校验完成，用时：2ms

第二次同步：总用时 2 秒（列表加载用时 10 秒）

[11:19:01 INF] 开始例行检查
[11:19:01 INF] 例行检查完成
[11:20:00 INF] 同步作业开始：12/27/2023 11:20:00
[11:20:00 INF] 云盘存储根目录初始化完成，用时：367ms
[11:20:00 INF] 开始加载云盘存储文件列表
[11:20:01 INF] 开始例行检查
[11:20:01 INF] 例行检查完成
[11:20:10 INF] 云盘文件加载完成，包含 1467 个文件，3643 个文件夹。
[11:20:10 INF] 加载云盘存储文件列表完成，用时：10047ms
[11:20:10 INF] 开始执行同步
[11:20:10 INF] 扫描本地文件，总文件数：1467, 扫描文件用时: 322.6471ms
[11:20:10 INF] 同步文件夹中 2/3641，用时：0.0552ms，kopia/p6c/f66
...
[11:20:12 INF] 同步文件夹完成，总文件夹数：1467，用时：1450.7582ms
...
[11:20:12 INF] 同步文件中 1466/1467，用时：519.4365ms，kopia/p65/ef2/046536ebd68a2b0ec57a3f733ff-s9a17ce26b1838062122.f
[11:20:12 INF] 同步文件中 1467/1467，用时：519.6538ms，kopia/qd1/275/b7e487081d0ed47ea0638caa116-s3e0aeff9c8243770123.f
[11:20:12 INF] 同步文件完成，总文件数：1467，用时：520.4528ms
[11:20:12 INF] 同步作业完成，用时：2295ms
[11:20:12 INF] 同步作业结束：12/27/2023 11:20:12
[11:20:12 INF] 同步作业校验完成，用时：1ms
```

## 鸣谢

- 阿里云盘

## 推广

- TODO