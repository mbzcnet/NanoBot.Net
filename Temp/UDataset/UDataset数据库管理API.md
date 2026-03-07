# UDataset 数据库管理API

## 概述

UDataset数据库管理API提供了数据库级别的操作接口，通过`IDatabaseManager`接口实现跨数据库的统一数据库管理功能。

## 核心接口

### IDatabaseManager接口

IDatabaseManager 接口负责数据库级别的操作，使用场景：管理数据库实例。

#### 属性

- **`ConnectionString` (string)**: 获取或设置数据库连接字符串，使用场景：配置连接。

#### 方法

- **`CreateConnection()`**: 创建一个连接实例，使用场景：获取 IConnection。
- **`Exists(string databaseName)`**: 判断数据库是否存在，使用场景：检查数据库。
- **`Create(string databaseName, DatabaseOptions? options)`**: 创建数据库，使用场景：新建数据库。
- **`Drop(string databaseName)`**: 删除数据库，使用场景：移除数据库。
- **`Rename(string oldDatabaseName, string newDatabaseName)`**: 重命名数据库，使用场景：重构环境。
- **`Use(string databaseName)`**: 切换到指定的数据库上下文，使用场景：改变操作数据库。
- **`Alter(string databaseName, DatabaseOptions options)`**: 修改数据库选项，使用场景：调整配置。

## 数据库选项

### DatabaseOptions类

用于配置数据库创建和修改时的选项。

#### 属性

- **`MaxSizeMB` (int?)**: 数据库最大大小（MB）
- **`InitialSizeMB` (int?)**: 数据库初始大小（MB）
- **`MaxConnections` (int?)**: 最大连接数
- **`FileGrowthMB` (int?)**: 文件增长大小（MB）
- **`Collation` (Collation)**: 排序规则

#### 枚举

##### Charset枚举
表示数据库的字符集，使用场景：处理国际化数据。
| 成员 | 说明 |
|------|------|
| `Default` | 默认字符集。 |
| `UTF8` | UTF-8 编码。 |
| `GBK` | GBK 编码。 |
| `EnglishUS` | 美式英语字符集。 |

##### Collation枚举
表示数据库的排序规则，使用场景：定义数据排序。
| 成员 | 说明 |
|------|------|
| `Default` | 默认排序规则。 |
| `EnglishUS` | 美式英语排序规则。 |
| `ChineseSimplified_Pinyin` | 简体中文按拼音排序。 |

## 使用示例

### 基本使用方法

```csharp
// 注册提供者并创建数据库管理器
MySQLBootstrapper.Register();
var databaseManager = ProviderFactory.CreateDatabaseManager("MySQL", connectionString);

// 1. 创建数据库
string databaseName = "TestDatabase";
var options = new DatabaseOptions
{
    MaxSizeMB = 1024,
    InitialSizeMB = 100,
    MaxConnections = 200,
    Collation = Collation.EnglishUS
};
databaseManager.Create(databaseName, options);
Console.WriteLine($"已创建数据库: {databaseName}");

// 2. 验证数据库是否存在
bool dbExists = databaseManager.Exists(databaseName);
Console.WriteLine($"数据库 '{databaseName}' 是否存在: {dbExists}");

// 3. 修改数据库选项
var newOptions = new DatabaseOptions
{
    MaxConnections = 150,
    FileGrowthMB = 50 // 仅适用于 SQL Server 等支持文件增长的数据库
};
databaseManager.Alter(databaseName, newOptions);
Console.WriteLine($"已修改数据库 '{databaseName}' 的选项。");

// 4. 重命名数据库
string oldDatabaseName = "TestDatabase";
string newDatabaseName = "NewTestDatabase";
databaseManager.Rename(oldDatabaseName, newDatabaseName);
Console.WriteLine($"已将数据库 '{oldDatabaseName}' 重命名为 '{newDatabaseName}'。");

// 5. 切换数据库上下文
databaseManager.Use(newDatabaseName);
Console.WriteLine($"已切换到数据库: {newDatabaseName}");

// 6. 删除数据库
databaseManager.Drop(newDatabaseName);
Console.WriteLine($"已删除数据库: {newDatabaseName}");
```

### 创建数据库连接

```csharp
// 通过数据库管理器创建连接实例
var connection = databaseManager.CreateConnection();

// 现在可以使用connection进行数据操作
var user = new Row("users") 
{
    ["Name"] = "John",
    ["Email"] = "john@example.com"
};
await connection.Create(user);
```

## 跨数据库兼容性

### 数据库特定功能支持

不同数据库产品在数据库管理功能上可能存在差异：

- **SQL Server**
  - 支持完整的数据库创建、删除、重命名操作
  - 支持文件增长配置（FileGrowthMB）
  - 支持最大大小和初始大小配置

- **PostgreSQL**
  - 支持数据库创建、删除、重命名操作
  - 支持字符集和排序规则配置
  - 数据库大小限制通过系统配置管理

- **MySQL**
  - 支持数据库创建和删除操作
  - **注意**：MySQL 不支持数据库重命名操作
  - 支持字符集配置

- **DuckDB**
  - 作为文件数据库，数据库即文件（常见扩展名：`.duckdb`、`.db`）
  - 在 UDataset 当前实现中：
    - `Create(databaseName)`：创建数据库文件（当 `databaseName` 为相对路径时，将落在**当前工作目录**，并自动追加 `.duckdb` 后缀）
    - `Drop(databaseName)`：删除数据库文件
    - `Rename(oldName, newName)`：重命名数据库文件
    - `Exists(databaseName)`：判断对应文件是否存在
    - `Use(databaseName)`：切换连接字符串中的 `Data Source` 指向的数据库文件
    - `Alter(databaseName, options)`：DuckDB 为文件型数据库，UDataset 当前实现中该操作为“忽略/不生效”（选项通常通过连接字符串或 DuckDB 配置设置）
  - 内存数据库：DuckDB 支持 `:memory:` 作为内存数据库（进程结束后数据不持久化），典型连接字符串：`Data Source=:memory:`
  - 并发注意事项：DuckDB 官方推荐的写入模型为“单进程可读写，多进程同时写同一数据库文件不是默认支持模式”；如需多进程写入，通常需要在应用层实现跨进程互斥/重试，或改用服务器型数据库

- **Oracle**
  - 支持数据库管理接口的逻辑抽象
  - 在当前版本中主要用于模式（Schema）管理和数据操作适配
  - 数据库管理 API 在 Oracle 提供者中更多体现为逻辑上的支持，部分底层 DDL 操作可能受限

### 使用限制

1. **重命名操作**：MySQL不支持数据库重命名，调用`Rename`方法会抛出异常
2. **文件增长**：仅SQL Server支持文件增长配置
3. **连接数限制**：不同数据库的连接数配置方式不同

## 最佳实践

### 1. 错误处理

```csharp
try
{
    databaseManager.Create("MyDatabase", options);
}
catch (InvalidOperationException ex)
{
    // 处理数据库已存在等业务逻辑错误
    Console.WriteLine($"操作失败: {ex.Message}");
}
catch (NotSupportedException ex)
{
    // 处理数据库不支持的功能
    Console.WriteLine($"功能不支持: {ex.Message}");
}
catch (Exception ex)
{
    // 处理其他系统级错误
    Console.WriteLine($"系统错误: {ex.Message}");
}
```

### 2. 连接字符串管理

```csharp
// 建议使用配置文件管理连接字符串
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var connectionString = configuration.GetConnectionString("DefaultConnection");
var databaseManager = ProviderFactory.CreateDatabaseManager("SqlServer", connectionString);
```

### 3. 数据库选项配置

```csharp
// 根据环境配置不同的数据库选项
var options = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Production"
    ? new DatabaseOptions
    {
        MaxSizeMB = 10240,      // 生产环境使用更大的数据库
        MaxConnections = 1000,
        Collation = Collation.EnglishUS
    }
    : new DatabaseOptions
    {
        MaxSizeMB = 1024,       // 开发环境使用较小的数据库
        MaxConnections = 100,
        Collation = Collation.Default
    };
```

### 4. 资源释放

```csharp
// 数据库管理器实现了IDisposable，建议使用using语句
using var databaseManager = ProviderFactory.CreateDatabaseManager("PostgreSQL", connectionString);

// 执行数据库操作
databaseManager.Create("TestDB", options);

// using语句结束时自动释放资源
```

## 测试支持

### 集成测试示例

```csharp
[TestMethod]
public void DatabaseLifecycle_AllOperations_ShouldWork()
{
    var databaseManager = ProviderFactory.CreateDatabaseManager("SqlServer", connectionString);
    string testDbName = $"TestDB_{Guid.NewGuid():N}";
    
    try
    {
        // 创建
        Assert.IsFalse(databaseManager.Exists(testDbName));
        databaseManager.Create(testDbName, new DatabaseOptions { MaxSizeMB = 100 });
        Assert.IsTrue(databaseManager.Exists(testDbName));
        
        // 重命名
        string newDbName = $"NewTestDB_{Guid.NewGuid():N}";
        databaseManager.Rename(testDbName, newDbName);
        Assert.IsFalse(databaseManager.Exists(testDbName));
        Assert.IsTrue(databaseManager.Exists(newDbName));
        
        // 删除
        databaseManager.Drop(newDbName);
        Assert.IsFalse(databaseManager.Exists(newDbName));
    }
    finally
    {
        // 确保测试数据库被清理
        if (databaseManager.Exists(testDbName))
            databaseManager.Drop(testDbName);
    }
}
```

数据库管理API为UDataset提供了完整的数据库级别操作能力，支持跨数据库的统一管理接口，是构建多数据库应用的重要基础组件。 