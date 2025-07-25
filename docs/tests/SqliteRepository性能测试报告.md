﻿# SQLite 仓库性能测试报告

## 测试环境
- 操作系统: Microsoft Windows 10.0.22621
- 处理器: 24 核心
- .NET 版本: 8.0.15
- 测试时间: 2025/6/4 15:48:00

## 测试结果

### 记录数: 10,000

| 数据库类型 | 添加 (ms) | 查询 (ms) | 更新 (ms) | 删除 (ms) | 总时间 (ms) |
|------------|-----------|-----------|-----------|-----------|-------------|
| 未加密 | 16.06 | 23.98 | 2357.00 | 2447.49 | 4844.53 |
| 加密 | 67.69 | 41.50 | 5124.12 | 5174.75 | 10408.06 |

加密数据库相比未加密数据库:
- 添加操作: 321.38% 更慢
- 查询操作: 73.10% 更慢
- 总体性能: 114.84% 更慢


### 记录数: 50,000

| 数据库类型 | 添加 (ms) | 查询 (ms) | 更新 (ms) | 删除 (ms) | 总时间 (ms) |
|------------|-----------|-----------|-----------|-----------|-------------|
| 未加密 | 83.61 | 53.38 | 13192.96 | 12857.03 | 26186.99 |
| 加密 | 252.37 | 188.97 | 27840.04 | 27016.59 | 55297.96 |

加密数据库相比未加密数据库:
- 添加操作: 201.82% 更慢
- 查询操作: 253.97% 更慢
- 总体性能: 111.17% 更慢


## 结论

- 加密数据库比未加密数据库在所有操作上通常会有一定的性能开销。
- 随着记录数量的增加，加密和非加密数据库之间的性能差异变得更加明显。
- 查询操作在加密数据库中受到的影响最大，因为需要解密整个数据块。
- 在决定是否使用数据库加密时，应权衡安全需求和性能要求。
