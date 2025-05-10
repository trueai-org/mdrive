# 文件系统忽略规则详细文档


兼容 Kopia 备份软件的忽略规则语法。它支持复杂的通配符模式、目录特定匹配、否定规则等。

## 忽略规则语法

### 基本通配符

| 通配符 | 描述 | 示例 | 匹配 | 不匹配 |
|--------|------|------|------|--------|
| `*` | 匹配单层中的任意字符 | `*.txt` | `file.txt`, `name.txt` | `file.log`, `dir/file.txt` |
| `**` | 匹配任意多层路径 | `**/logs` | `logs`, `app/logs`, `app/sub/logs` | - |
| `?` | 匹配单个字符 | `file?.log` | `file1.log`, `fileA.log` | `file.log`, `file12.log` |

### 路径规则

| 语法 | 描述 | 示例 | 匹配 | 不匹配 |
|------|------|------|------|--------|
| `/pattern` | 只匹配根目录下的内容 | `/bin` | `/bin`, `/bin/file.exe` | `app/bin`, `lib/bin` |
| `pattern` | 匹配任何位置的内容 | `bin` | `bin`, `app/bin`, `bin/file.exe` | - |
| `dir/` | 匹配目录（末尾有斜杠） | `temp/` | `temp/`, `temp/file.txt` | `temp.txt` |

### 特殊匹配

| 语法 | 描述 | 示例 | 匹配 | 不匹配 |
|------|------|------|------|--------|
| `!pattern` | 否定规则（不排除匹配的内容） | `!*.pdf` | `report.pdf` | - |
| `[abc]` | 字符集合（匹配集合中任一字符） | `file[123].txt` | `file1.txt`, `file2.txt`, `file3.txt` | `file4.txt` |
| `[a-z]` | 字符范围（匹配范围内任一字符） | `file[a-c].txt` | `filea.txt`, `fileb.txt`, `filec.txt` | `filed.txt` |

## 规则类型和优先级

### 规则类型

1. **排除规则**（标准规则）：默认规则类型，匹配的文件或目录将被排除。
2. **包含规则**（以 `!` 开头）：否定规则，匹配的文件或目录将被包含，即使它们匹配了排除规则。

### 规则优先级

1. 规则按照它们在列表中的顺序依次评估。
2. 后面的规则可以覆盖前面规则的效果。
3. 最后一个匹配的规则决定文件或目录是否被忽略。
4. 如果没有规则匹配，则默认包含该文件或目录。

### 处理逻辑

下面是文件/目录是否被忽略的判断流程：

```
初始状态：文件应被包含（不忽略）

对于每个规则:
    如果文件匹配规则:
        如果是排除规则:
            文件应被忽略
        如果是包含规则:
            文件应被包含

最终状态决定文件是被忽略还是包含
```

## 使用示例

### 基本使用

```csharp
// 创建默认的忽略规则
var ignorePatterns = FileIgnoreHelper.BuildIgnorePatterns();
```

### 自定义忽略规则

```csharp
// 创建自定义忽略规则
var ignorePatterns = FileIgnoreHelper.BuildIgnorePatterns(
    "*.obj",                // 忽略所有.obj文件
    "*.tmp",                // 忽略所有.tmp文件
    "/bin",                 // 忽略根目录下的bin文件夹
    "node_modules",         // 忽略所有node_modules目录
    "**/debug/**",          // 忽略所有debug目录及其内容
    "**/.git/**",           // 忽略所有.git目录及其内容
    "!*.pdf",               // 不忽略PDF文件
    "!important/**"         // 不忽略important目录下的任何内容
);
```

### 大型文件系统扫描

```csharp
// 为大型文件系统优化的使用方式
var ignorePatterns = FileIgnoreHelper.BuildIgnorePatterns(
    "*.bak",
    "*.tmp",
    "**/temp/**",
    "**/cache/**"
);
```
