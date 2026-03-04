# UDataset 数据操作API

## 概述

UDataset数据操作API通过`IConnection`接口提供统一的CRUD（创建、读取、更新、删除）操作和事务管理功能，支持跨数据库的数据访问和操作。

> **🎉 InsertOptions 重构说明**
> 
> UDataset 已完成 `InsertOptions` 类的重构，采用全新的流畅接口设计：
> - **统一枚举**：使用单一的 `OnConflictBehavior` 枚举，消除了原有的 `InsertMode` 和 `ConflictResolution` 重叠
> - **流畅接口**：支持链式调用，如 `InsertOptions.Upsert().OnFields("A", "B").ExcludeFields("CreatedAt")`
> - **语义化命名**：使用 `OnFields`、`ExcludeFields`、`WhenNotExists` 等语义化方法名
> - **向后兼容**：保持了所有原有功能，同时提供更简洁优雅的API
> - **复杂场景支持**：完美支持基于任意字段组合的冲突检测和复杂策略组合

## 核心接口

### IConnection接口

IConnection 接口定义了数据提供者的核心 CRUD 操作和事务管理，使用场景：执行数据库交互。

#### 属性

- **`SchemaManager` (ISchemaManager)**: 获取模式管理器实例，使用场景：管理表结构。
- **`SqlTransformer` (ISqlTransformer)**: 获取 SQL 转换器实例，使用场景：生成数据库特定SQL。

#### CRUD操作方法

##### 创建操作 (Create)

- **`Task<IRow> Create(IRow dataRow, InsertOptions? options = null)`**: 创建单条数据记录，使用场景：新增数据。支持可选的插入选项。
- **`Task<IEnumerable<IRow>> Create(IEnumerable<IRow> dataRows, InsertOptions? options = null)`**: 批量创建数据记录，使用场景：高效批量插入。支持可选的插入选项。

##### 读取操作 (Retrieve)

- **`Task<IRow> Retrieve(string entityName, object Id)`**: 通过 ID 获取单条数据记录，使用场景：查询单个记录。
- **`Task<IEnumerable<IRow>> Retrieve(string entityName, IEnumerable<object> ids)`**: 通过多个 ID 获取多条数据记录，使用场景：批量查询。

##### 更新操作 (Update)

- **`Task<IRow> Update(IRow dataRow)`**: 更新单条数据记录，使用场景：修改数据。
- **`Task<IEnumerable<IRow>> Update(IEnumerable<IRow> dataRows)`**: 批量更新数据记录，使用场景：批量修改。

##### 删除操作 (Delete)

- **`Task<bool> Delete(IRow dataRow)`**: 删除单条数据记录，使用场景：移除数据。
- **`Task<int> Delete(IEnumerable<IRow> dataRows)`**: 批量删除数据记录，使用场景：批量移除。
- **`Task<bool> Delete(string entityName, object id)`**: 通过 ID 删除单条数据记录，使用场景：基于主键删除。
- **`Task<int> Delete(string entityName, IEnumerable<object> ids)`**: 通过多个 ID 批量删除数据记录，使用场景：批量删除。

##### 查询操作 (Query)

- **`Task<IEnumerable<IRow>> Query(QueryExpression queryExpression)`**: 执行查询并返回多条记录，使用场景：复杂查询。
- **`Task<IRow?> QuerySingle(QueryExpression queryExpression)`**: 执行查询并返回单条记录，使用场景：获取单一结果。

#### 事务管理方法

- **`void BeginTransaction()`**: 开始一个数据库事务，使用场景：确保操作原子性。
- **`void CommitTransaction()`**: 提交当前事务，使用场景：确认操作。
- **`void RollbackTransaction()`**: 回滚当前事务，使用场景：撤销操作。

## 核心数据模型

### Row类

Row 类用于保存单条数据记录，支持动态访问数据。它在场景中用于快速处理单条记录，例如在 Web API 中返回用户数据。

#### 属性

- **`Schema` (string)**: 获取此记录所属的实体（表）名称，使用场景：标识记录来源。
- **`Id` (object?)**: 获取或设置记录的唯一标识符。在更新和删除操作中，此字段为必填，使用场景：作为主键在 CRUD 操作中。
- **`Version` (string?)**: 获取或设置行版本号，用于乐观并发控制，使用场景：防止数据冲突，如在多用户编辑系统中。

#### 构造函数

- **`Row(string schema)`**: 使用指定的实体（表）名称初始化 `Row` 类的新实例。
- **`Row(string schema, object id)`**: 使用指定的实体（表）名称和记录ID初始化 `Row` 类的新实例。此构造函数在更新或删除已知ID的记录时非常有用。

#### 方法

- **`this[string key]` (object?)**: 索引器，用于通过字段名获取或设置字段值，使用场景：动态访问记录字段。
- **`Add(string key, object? value)`**: 向记录中添加一个字段及其值，使用场景：构建新记录时动态添加属性。
- **`Remove(string key)`**: 从记录中删除指定字段，使用场景：清理记录时移除不必要字段。
- **`TryGetValue(string key, out object? value)`**: 尝试获取指定字段的值，使用场景：安全检查字段是否存在。
- **`ContainsKey(string key)`**: 检查记录是否包含指定字段，使用场景：验证数据完整性。
- **`GetEnumerator()`**: 获取用于遍历记录中所有字段的枚举器，使用场景：迭代记录字段。
- **`CreateUpdateRow()`**: 创建一个新的Row对象用于更新，自动携带Schema、Id和Version信息，使用场景：局部更新记录。

### QueryExpression类

`QueryExpression` 类用于构建复杂的查询条件，以灵活地从数据库中检索数据。它封装了SELECT、JOIN、WHERE、GROUP BY、ORDER BY、LIMIT/OFFSET等SQL查询的各个方面。

#### 属性

- **`Collection` (string)**: 要查询的主实体（表）的名称。
- **`Alias` (string?)**: 主实体的别名，用于在复杂查询中区分表或提高可读性。
- **`Select` (List<SelectItem>)**: 定义要从查询结果中选择的列或表达式。`SelectItem` 可以表示一个普通列或一个聚合函数（如COUNT(*), SUM(Age)）。
- **`Join` (List<JoinClause>)**: 定义与主实体关联的JOIN子句列表。每个 `JoinClause` 描述一个连接的表、其别名和连接条件。
- **`Filter` (string?)**: 查询的WHERE条件表达式，可以使用参数占位符（例如 "Age > @minAge"）。
- **`Parameters` (Dictionary<string, object>?)**: `Filter` 表达式中使用的参数的字典。
- **`OrderBy` (List<OrderByItem>)**: 定义查询结果的排序规则列表。
- **`GroupBy` (List<string>?)**: 定义用于聚合查询的分组列列表。
- **`Top` (int?)**: 要返回的最大记录数，用于限制结果集大小。对应SQL的LIMIT或TOP。
- **`Skip` (int?)**: 要跳过的记录数，用于分页查询。对应SQL的OFFSET。

#### 方法

- **`AddSelect(string columnExpression, string? alias = null)`**: 向SELECT列表中添加一个列或表达式。`columnExpression` 可以是列名，也可以是聚合函数表达式（例如 "COUNT(*)"）。
- **`AddOrderBy(string fieldExpression, SortDirection direction = SortDirection.Ascending)`**: 向ORDER BY列表中添加一个排序字段及其方向。
- **`AddJoin(string targetCollection, string alias, string joinCondition, JoinType joinType = JoinType.Inner)`**: 向查询中添加一个JOIN子句。

### JoinClause类

`JoinClause` 类用于定义SQL查询中的JOIN操作，指定要连接的表、其别名以及连接条件。

#### 属性

- **`JoinType` (JoinType)**: 连接类型，例如 `Inner` (内连接), `Left` (左外连接)。
- **`Collection` (string)**: 要连接的实体（表）的名称。
- **`Alias` (string)`**: 连接的表的别名。
- **`Condition` (string)`**: 连接条件，例如 "主表别名.列名 = 连接表别名.列名"。
- **`Select` (List<SelectItem>)**: 定义从这个连接表中选择的列或表达式。

#### 方法

- **`AddSelect(string columnExpression, string? alias = null)`**: 向此JOIN子句的SELECT列表中添加一个列或表达式。

### SelectItem类

`SelectItem` 表示查询结果中选择的单个项，可以是列或表达式。

#### 属性

- **`ColumnExpression` (string)`**: 列的名称或SQL表达式（例如 "Name", "COUNT(*)"）。
- **`Alias` (string?)`**: 结果集中此项的别名。

### OrderByItem类

`OrderByItem` 定义查询结果的排序规则。

#### 属性

- **`FieldExpression` (string)`**: 用于排序的字段或表达式。
- **`Direction` (SortDirection)`**: 排序方向，枚举值包括 `Ascending` (升序) 和 `Descending` (降序)。

### SortDirection枚举

表示排序的方向。

| 成员 | 值 | 说明 |
|------|----|------|
| `Ascending` | `0` | 升序排列。 |
| `Descending` | `1` | 降序排列。 |

### DataOperation枚举

表示操作类型，使用场景：定义 CRUD 操作。

| 成员 | 值 | 说明 |
|------|----|------|
| `Create` | `1` | 表示创建操作。 |
| `Update` | `2` | 表示更新操作。 |
| `Delete` | `4` | 表示删除操作。 |
| `Query` | `8` | 表示查询操作。 |

### InsertOptions类

`InsertOptions` 类提供了灵活的插入选项，采用流畅接口设计，支持多种冲突处理策略和复杂业务场景。

#### 核心属性

- **`OnConflict` (OnConflictBehavior)**: 冲突处理行为，默认为 `Throw`
- **`ConflictFields` (List<string>?)**: 用于检测冲突的字段列表，如果为空则使用主键进行冲突检测
- **`IfNotExists` (QueryExpression?)**: 条件查询表达式，用于条件插入 - 只有满足此条件时才执行插入操作
- **`ExcludeFromUpdate` (List<string>?)**: 在Update模式下，指定更新时要排除的字段（这些字段只在插入时设置，更新时不会被修改）
- **`IncludeInUpdate` (List<string>?)**: 在Update模式下，指定更新时要包含的字段（如果指定了此项，则只有这些字段会在更新时被修改）
- **`ReturnRecords` (bool)**: 是否返回受影响的记录，默认为true（如果设置为false则可能提高性能但不返回完整记录信息）

#### 静态工厂方法

- **`InsertOptions.Default()`**: 创建默认插入选项（冲突时抛出异常）
- **`InsertOptions.IgnoreConflicts()`**: 创建忽略冲突的插入选项
- **`InsertOptions.Upsert()`**: 创建Upsert插入选项（冲突时更新）
- **`InsertOptions.ReturnIfExists()`**: 创建返回已存在记录的插入选项

#### 流畅接口方法

##### 冲突字段配置
- **`OnFields(params string[] fields)`**: 指定用于检测冲突的字段，支持链式调用
- **`OnFields(List<string> fields)`**: 指定用于检测冲突的字段列表，支持链式调用
  
**冲突字段语义说明：**
- **复合字段使用 AND 语义**：当指定多个字段（如 `.OnFields("OrgId", "Code")`）时，只有当数据库中存在记录满足 *所有字段都相等* 才视为冲突（复合唯一键语义）。
- **字段名大小写不敏感**：`Row` 的字段 Key（如 `"name"` / `"Name"`）与 `.OnFields("Name")` 的大小写不必一致，UDataset 会按表定义规范化后执行冲突检测。
- **值使用参数化绑定**：冲突检测的值通过参数绑定传入，支持 `$sab`、空格、中文等特殊字符/国际化文本，不需要手动转义。

##### 条件插入配置
- **`WhenNotExists(QueryExpression condition)`**: 设置条件插入 - 只有当不存在满足条件的记录时才插入，支持链式调用
- **`WhenNotExists(string tableName, string filter, Dictionary<string, object>? parameters = null)`**: 设置条件插入的简化版本，支持链式调用

##### 更新字段控制
- **`ExcludeFields(params string[] fields)`**: 排除指定字段不参与更新（仅在Upsert模式下有效），支持链式调用
- **`OnlyUpdateFields(params string[] fields)`**: 只更新指定字段（仅在Upsert模式下有效），支持链式调用

##### 返回设置
- **`WithReturnRecords(bool returnRecords = true)`**: 设置是否返回记录，支持链式调用

### OnConflictBehavior枚举

冲突处理行为枚举，统一的冲突处理策略。

| 成员 | 值 | 说明 |
|------|----|------|
| `Throw` | `0` | 抛出异常（默认行为） |
| `Ignore` | `1` | 忽略冲突，不插入新记录 |
| `Update` | `2` | 更新现有记录 |
| `ReturnExisting` | `3` | 返回现有记录（不插入也不更新） |

## 使用示例

### 连接创建

```csharp
// 注册数据库提供者
SqlServerBootstrapper.Register();
PostgreSQLBootstrapper.Register();
MySQLBootstrapper.Register();

// 创建连接
string sqlServerConnString = "Data Source=.;Initial Catalog=MyDb;Integrated Security=True;";
string postgresConnString = "Host=localhost;Port=5432;Database=MyDb;Username=user;Password=password;";
string mysqlConnString = "Server=localhost;Port=3306;Database=MyDb;Uid=user;Pwd=password;";

IConnection sqlServerDb = ProviderFactory.CreateConnection("SqlServer", sqlServerConnString);
IConnection postgresDb = ProviderFactory.CreateConnection("PostgreSQL", postgresConnString);
IConnection mysqlDb = ProviderFactory.CreateConnection("MySQL", mysqlConnString);
```

### 创建记录 (Create)

#### 单条记录创建

```csharp
// 创建单条记录
var dataRow = new Row("Users") // "Users" 是集合名称
{
    ["Name"] = "Test User",
    ["Age"] = 25,
    ["IsActive"] = true,
    ["CreatedAt"] = DateTime.UtcNow
};

var createdResult = await connection.Create(dataRow);
Console.WriteLine($"新记录ID: {createdResult.Id}, 版本: {createdResult.Version}");
```

#### 批量记录创建

```csharp
// 创建多条记录 (批量插入)
var dataRows = new List<Row>
{
    new Row("Users") { ["Name"] = "User 1", ["Age"] = 20, ["IsActive"] = true },
    new Row("Users") { ["Name"] = "User 2", ["Age"] = 30, ["IsActive"] = false },
    new Row("Users") { ["Name"] = "User 3", ["Age"] = 25, ["IsActive"] = true }
};

var insertedRows = await connection.Create(dataRows);
foreach (var row in insertedRows)
{
    Console.WriteLine($"批量插入记录 ID: {row.Id}, 版本: {row.Version}");
}
```

#### 使用插入选项 (InsertOptions) - 流畅接口设计

##### 默认插入

```csharp
// 默认插入 - 如果主键冲突则失败
var dataRow = new Row("Users")
{
    ["Name"] = "Default Insert Test",
    ["Age"] = 25,
    ["IsActive"] = true
};

var options = InsertOptions.Default();
var result = await connection.Create(dataRow, options);
Console.WriteLine($"默认插入成功，ID: {result.Id}");
```

##### 忽略冲突插入

```csharp
// 忽略冲突的插入 - 如果记录已存在则忽略，不抛出异常
var user = new Row("Users")
{
    ["Email"] = "existing@example.com",
    ["Name"] = "John Doe",
    ["Age"] = 25
};

// 第一次插入，记录会被创建
var firstResult = await connection.Create(user);
Console.WriteLine($"第一次插入成功，ID: {firstResult.Id}");

// 尝试插入相同邮箱的记录，使用流畅接口配置忽略冲突
var duplicateUser = new Row("Users")
{
    ["Email"] = "existing@example.com", 
    ["Name"] = "Should Not Be Saved",
    ["Age"] = 30
};

var ignoreOptions = InsertOptions.IgnoreConflicts()
    .OnFields("Email");  // 基于Email字段检测冲突

var ignoreResult = await connection.Create(duplicateUser, ignoreOptions);
Console.WriteLine($"第二次插入（忽略冲突）完成。");

// 批量忽略冲突插入
var batchRows = new List<IRow>
{
    new Row("Users") { ["Email"] = "batch_existing@example.com", ["Name"] = "Should Be Ignored", ["Age"] = 30 },
    new Row("Users") { ["Email"] = "new_user@example.com", ["Name"] = "New Record", ["Age"] = 35 }
};

var batchIgnoreOptions = InsertOptions.IgnoreConflicts()
    .OnFields("Email")
    .WithReturnRecords(true);

var batchResults = await connection.Create(batchRows, batchIgnoreOptions);
Console.WriteLine($"批量忽略冲突插入完成，插入了 {batchResults.Count()} 条新记录。");
```

##### Upsert操作（更新或插入）

```csharp
// Upsert - 如果记录存在则更新，否则插入
var user = new Row("Users")
{
    ["Email"] = "user@example.com",
    ["Name"] = "Jane Smith",
    ["Age"] = 28,
    ["CreatedAt"] = DateTime.UtcNow
};

// 第一次插入，使用流畅接口
var firstUpsertOptions = InsertOptions.Upsert()
    .OnFields("Email");  // 基于Email检测冲突

var firstUpsertResult = await connection.Create(user, firstUpsertOptions);
Console.WriteLine($"第一次Upsert插入完成: {firstUpsertResult.Id}");

// 使用相同Email更新记录，展示流畅接口的强大功能
var updateUser = new Row("Users")
{
    ["Email"] = "user@example.com",
    ["Name"] = "Jane Doe", // 更新名字
    ["Age"] = 29,
    ["CreatedAt"] = DateTime.UtcNow // 这个字段会被排除，保持原值
};

var upsertOptions = InsertOptions.Upsert()
    .OnFields("Email")                    // 基于Email检测冲突
    .ExcludeFields("CreatedAt")          // 更新时不修改创建时间
    .WithReturnRecords(true);            // 返回完整记录

var result = await connection.Create(updateUser, upsertOptions);
Console.WriteLine($"Upsert 更新操作完成: {result.Id}");

// 高级用法：只更新指定字段
var partialUpdateUser = new Row("Users")
{
    ["Email"] = "user@example.com",
    ["Name"] = "Only Name Updated",
    ["Age"] = 999,        // 这个值会被忽略
    ["IsActive"] = false  // 这个值也会被忽略
};

var partialUpdateOptions = InsertOptions.Upsert()
    .OnFields("Email")
    .OnlyUpdateFields("Name");  // 只更新Name字段

await connection.Create(partialUpdateUser, partialUpdateOptions);
Console.WriteLine("Upsert 部分更新操作完成。");
```

##### 返回已存在记录

```csharp
// 如果记录已存在则返回现有记录，否则创建新记录
var user = new Row("Users")
{
    ["Email"] = "return_existing@example.com",
    ["Name"] = "Bob Wilson",
    ["Age"] = 30
};

// 第一次插入，记录会被创建
var firstReturnResult = await connection.Create(user);
Console.WriteLine($"第一次插入（ReturnExisting）成功，ID: {firstReturnResult.Id}");

// 尝试插入相同邮箱的记录，使用流畅接口配置返回已存在记录
var duplicateUserForReturn = new Row("Users")
{
    ["Email"] = "return_existing@example.com",
    ["Name"] = "New Bob", // 这个名字不会被保存
    ["Age"] = 35 // 这个年龄不会被保存
};

var returnExistingOptions = InsertOptions.ReturnIfExists()
    .OnFields("Email")              // 基于Email检测冲突
    .WithReturnRecords(true);       // 确保返回完整记录

var result = await connection.Create(duplicateUserForReturn, returnExistingOptions);
// 验证返回的是原记录
Console.WriteLine($"ReturnIfExists 操作完成，返回的记录 Name: {result["Name"]}, Age: {result["Age"]}");
// 名字和年龄应该是原始值：Bob Wilson, 30
```
  
**行为说明：**
- **不更新现有记录**：`ReturnExisting` 只会“返回已存在记录”，不会把输入行中的其它字段写回数据库（与 `Upsert` 不同）。
- **批量返回与输入对应**：当使用 `Create(IEnumerable<IRow>, options)` 批量调用时，返回结果与输入行一一对应（便于上层按输入顺序做映射）。

##### 条件插入

```csharp
// 条件插入 - 只有满足特定条件时才插入
// 场景：只有当年龄大于25的用户少于一定数量时才插入
var newRow = new Row("Users")
{
    ["Name"] = "Conditional User",
    ["Age"] = 35,
    ["IsActive"] = true
};

// 使用流畅接口创建条件插入：检查年龄>25的用户数量
var conditionalQuery = new QueryExpression("Users");
conditionalQuery.AddSelect("COUNT(*)", "count");
conditionalQuery.Filter = "Age > @minAge";
conditionalQuery.Parameters = new Dictionary<string, object> { ["minAge"] = 25 };

var conditionalOptions = InsertOptions.Default()
    .WhenNotExists(conditionalQuery)    // 设置条件：当满足查询条件时才插入
    .WithReturnRecords(true);

var conditionalResult = await connection.Create(newRow, conditionalOptions);

// 如果条件满足，conditionalResult 将包含新插入的记录信息
if (conditionalResult != null)
{
    Console.WriteLine($"条件插入成功，ID: {conditionalResult.Id}");
}
else
{
    Console.WriteLine("条件不满足，记录未插入。");
}

// 使用简化版本的条件插入
var simpleConditionalUser = new Row("Users")
{
    ["Name"] = "Simple Conditional User",
    ["Age"] = 28,
    ["IsActive"] = true
};

var simpleConditionalOptions = InsertOptions.Default()
    .WhenNotExists("Users", "Age > @minAge AND IsActive = @isActive", 
                   new Dictionary<string, object> 
                   { 
                       ["minAge"] = 30, 
                       ["isActive"] = true 
                   });

var simpleResult = await connection.Create(simpleConditionalUser, simpleConditionalOptions);
Console.WriteLine(simpleResult != null ? "简化条件插入成功" : "简化条件不满足");

// 批量条件插入示例
var batchConditionalUsers = new List<IRow>
{
    new Row("Users") { ["Name"] = "BatchConditional1", ["Age"] = 30, ["IsActive"] = true },
    new Row("Users") { ["Name"] = "BatchConditional2", ["Age"] = 35, ["IsActive"] = true }
};

var batchConditionQuery = new QueryExpression("Users");
batchConditionQuery.AddSelect("COUNT(*)", "count");
batchConditionQuery.Filter = "Name IS NOT NULL";

var batchConditionalOptions = InsertOptions.Default()
    .WhenNotExists(batchConditionQuery)
    .WithReturnRecords(true);

var batchResults = await connection.Create(batchConditionalUsers, batchConditionalOptions);
Console.WriteLine($"批量条件插入成功，共插入 {batchResults.Count()} 条记录。");

// 复杂策略组合示例：展示流畅接口的强大能力
var complexUser = new Row("Users")
{
    ["Email"] = "complex@example.com",
    ["Name"] = "Complex Strategy User",
    ["Age"] = 32,
    ["Department"] = "Engineering",
    ["CreatedAt"] = DateTime.UtcNow
};

// 创建复杂的插入策略：
// 1. 基于Email和Department的Upsert
// 2. 更新时排除CreatedAt字段
// 3. 只有当部门人数少于10时才允许插入
var departmentQuery = new QueryExpression("Users");
departmentQuery.AddSelect("COUNT(*)", "count");
departmentQuery.Filter = "Department = @dept";
departmentQuery.Parameters = new Dictionary<string, object> { ["dept"] = "Engineering" };

var complexOptions = InsertOptions.Upsert()
    .OnFields("Email", "Department")     // 基于Email和Department的组合检测冲突
    .ExcludeFields("CreatedAt")          // 更新时保持原创建时间
    .WhenNotExists(departmentQuery)      // 条件：部门人数限制
    .WithReturnRecords(true);            // 返回完整记录

var complexResult = await connection.Create(complexUser, complexOptions);
Console.WriteLine(complexResult != null 
    ? $"复杂策略执行成功，ID: {complexResult.Id}" 
    : "复杂策略条件不满足");
```

### 读取记录 (Retrieve)

#### 单条记录读取

```csharp
// 通过ID读取单条记录
object recordId = createdResult.Id;
var retrievedResult = await connection.Retrieve("Users", recordId);

if (retrievedResult != null)
{
    Console.WriteLine($"检索到记录 - Name: {retrievedResult["Name"]}, Age: {retrievedResult["Age"]}");
}
else
{
    Console.WriteLine($"未找到 ID 为 {recordId} 的记录。");
}
```

#### 批量记录读取

```csharp
// 通过多个ID批量读取记录
var recordIds = insertedRows.Select(r => r.Id).ToList();
var retrievedResults = await connection.Retrieve("Users", recordIds);

Console.WriteLine("批量检索到的记录:");
foreach (var row in retrievedResults)
{
    Console.WriteLine($"- ID: {row.Id}, Name: {row["Name"]}, Age: {row["Age"]}");
}
```

### 更新记录 (Update)

> **⚠️ 警告：请勿直接使用查询结果进行更新**
> 
> 更新数据时，**切勿**直接修改通过 `Retrieve` 或 `Query` 查询返回的完整 `Row` 对象并提交。
> *   **原因**：这样做会将所有查询到的字段（包括未修改的字段）都包含在 UPDATE 语句中，可能导致覆盖其他人的修改或不必要的性能开销。
> *   **正确做法**：创建一个**新的 `Row` 实例**，仅设置 `Id`、`Version`（如需乐观锁）以及**真正需要变更的字段**。可以使用 `CreateUpdateRow()` 方法简化此过程。

#### 单条记录更新

```csharp
// 正确做法：仅更新特定字段
// 1. 使用 CreateUpdateRow() 从现有记录创建更新对象
//    这将自动复制表名、ID 和版本号（用于乐观并发控制）
var updateTarget = createdResult.CreateUpdateRow();

// 或者手动创建：
// var updateTarget = new Row("Users", createdResult.Id);
// updateTarget.Version = createdResult.Version;

// 2. 仅设置需要修改的字段
updateTarget["Name"] = "Updated Name"; 
updateTarget["Age"] = 26;

// 3. 执行更新
// 生成的 SQL 仅会更新 Name 和 Age 字段
var updatedResult = await connection.Update(updateTarget);
Console.WriteLine($"记录 {updatedResult.Id} 已更新，新版本: {updatedResult.Version}");
```

#### 批量记录更新

```csharp
// 正确做法：批量仅更新特定字段
var updates = new List<IRow>();

// 假设 recordIds 是需要更新的一组ID
foreach (var id in recordIds)
{
    // 创建只包含ID和需要修改字段的新对象
    var row = new Row("Users", id);
    row["IsActive"] = false; // 仅更新 IsActive 字段
    updates.Add(row);
}

// 批量提交更新，每个记录只更新 IsActive 字段
var updatedResults = await connection.Update(updates);
Console.WriteLine($"批量更新了 {updatedResults.Count()} 条记录");
```

### 删除记录 (Delete)

#### 通过记录对象删除

```csharp
// 删除单条记录
bool deleteSuccess = await connection.Delete(createdResult);
Console.WriteLine($"ID 为 {createdResult.Id} 的记录删除成功: {deleteSuccess}");

// 批量删除记录
var recordsToDelete = insertedRows;
int bulkDeleteCount = await connection.Delete(recordsToDelete);
Console.WriteLine($"批量删除了 {bulkDeleteCount} 条记录");
```

#### 通过ID删除

```csharp
// 通过ID删除单条记录
bool deleteSuccessById = await connection.Delete("Users", someRecordId);
Console.WriteLine($"通过ID删除记录成功: {deleteSuccessById}");

// 通过多个ID批量删除
var idsToDelete = new List<object> { id1, id2, id3 };
int deletedCount = await connection.Delete("Users", idsToDelete);
Console.WriteLine($"通过ID批量删除了 {deletedCount} 条记录");
```

### 基础查询操作

#### 简单查询

```csharp
// 查询所有记录
var queryExpression = new QueryExpression("Users");
var allUsers = await connection.Query(queryExpression);

Console.WriteLine($"查询到 {allUsers.Count()} 条用户记录");
foreach (var user in allUsers)
{
    Console.WriteLine($"- {user["Name"]}, Age: {user["Age"]}");
}
```

#### 条件查询

```csharp
// 带条件的查询
var query = new QueryExpression("Users");
query.Filter = "Age > @minAge AND IsActive = @isActive";
query.Parameters = new Dictionary<string, object>
{
    ["minAge"] = 25,
    ["isActive"] = true
};

var activeUsers = await connection.Query(query);
Console.WriteLine($"查询到 {activeUsers.Count()} 条活跃用户记录");
```

#### 单条记录查询

```csharp
// 查询单条记录
var singleQuery = new QueryExpression("Users");
singleQuery.Filter = "Name = @name";
singleQuery.Parameters = new Dictionary<string, object> { ["name"] = "Test User" };

var singleUser = await connection.QuerySingle(singleQuery);
if (singleUser != null)
{
    Console.WriteLine($"找到用户: {singleUser["Name"]}");
}
```

## 事务管理

### 基础事务操作

```csharp
// 开始事务
connection.BeginTransaction();
try
{
    // 在事务中执行多个操作
    var user1 = new Row("Users") 
    {
        ["Name"] = "Transaction User 1",
        ["Age"] = 25
    };
    await connection.Create(user1);

    var user2 = new Row("Users") 
    {
        ["Name"] = "Transaction User 2",
        ["Age"] = 30
    };
    await connection.Create(user2);

    // 提交事务
    connection.CommitTransaction();
    Console.WriteLine("事务提交成功");
}
catch (Exception ex)
{
    // 回滚事务
    connection.RollbackTransaction();
    Console.WriteLine($"事务回滚: {ex.Message}");
}
```

### 嵌套事务（Savepoint）

```csharp
// 嵌套事务示例
connection.BeginTransaction();
try
{
    var user1 = await connection.Create(new Row("Users") { ["Name"] = "User 1" });
    
    // 嵌套事务
    connection.BeginTransaction();
    try
    {
        var user2 = await connection.Create(new Row("Users") { ["Name"] = "User 2" });
        
        // 内部事务提交
        connection.CommitTransaction();
    }
    catch
    {
        // 内部事务回滚（回滚到Savepoint）
        connection.RollbackTransaction();
    }
    
    // 外部事务提交
    connection.CommitTransaction();
}
catch
{
    // 外部事务回滚
    connection.RollbackTransaction();
}
```

## 跨数据库兼容性

### 数据类型处理

UDataset自动处理不同数据库之间的数据类型差异：

```csharp
// 相同的代码可以在不同数据库中运行
var record = new Row("TestTable")
{
    ["StringField"] = "文本内容",           // 在SQL Server中映射为NVARCHAR，在PostgreSQL中映射为TEXT
    ["IntField"] = 42,                     // 在所有数据库中都映射为INTEGER
    ["DateField"] = DateTime.Now,          // 自动处理时区和精度差异
    ["BoolField"] = true,                  // SQL Server使用BIT，PostgreSQL使用BOOLEAN
    ["GuidField"] = Guid.NewGuid(),        // SQL Server使用UNIQUEIDENTIFIER，PostgreSQL使用UUID
    ["JsonField"] = "{\"key\": \"value\"}" // SQL Server 2016+支持JSON，PostgreSQL原生支持JSONB
};

// 在任何数据库中都能正常工作
var created = await connection.Create(record);
```

### 重要限制

#### MySQL GUID 主键批量插入限制

⚠️ **重要提示**: 在 MySQL 中，当使用 GUID 主键且设置了数据库默认值函数（如 `WithGuidPK()` 方法创建的主键）时，批量插入操作存在以下限制：

**问题描述**：
- MySQL 没有像 SQL Server 的 `OUTPUT` 子句或 PostgreSQL 的 `RETURNING` 子句
- 批量插入多条记录时，无法准确获取数据库生成的所有 GUID 值

**解决方案**：
为了保持批量插入的性能，UDataset 在检测到此场景时会自动切换到**客户端生成 GUID** 模式：

```csharp
// 对于使用 WithGuidPK() 创建的表，批量插入时会在客户端生成 GUID
var table = new Table("Users").WithGuidPK("Id");  // 设置数据库默认值函数
schemaManager.Create(table);

var users = new List<Row>();
for (int i = 0; i < 1000; i++)
{
    users.Add(new Row("Users") 
    {
        ["Name"] = $"User_{i}",
        ["Age"] = 20 + i
        // 不设置 Id 字段，让系统处理
    });
}

// MySQL: 客户端生成 GUID，跳过数据库的 UUID() 函数
// PostgreSQL/SQL Server: 正常使用数据库生成的 GUID
var createdUsers = await connection.Create(users);
```

**数据一致性影响**：
- ✅ **性能**: 保持了批量插入的高性能
- ⚠️ **一致性**: MySQL 生成的 GUID 来自客户端，而非数据库的 `UUID()` 函数
- ⚠️ **行为差异**: MySQL 与 PostgreSQL/SQL Server 在此场景下的行为不完全一致

**最佳实践建议**：
1. 如果需要完全一致的数据库行为，考虑使用自增整数主键
2. 如果必须使用 GUID 主键，可以接受客户端生成的 GUID
3. 对于单条记录插入，所有数据库都会正确使用各自的默认值函数

**UDataset对GUID格式的兼容性**：
UDataset在内部会自动将不同格式的GUID输入（如`System.Guid`对象、标准字符串、无连字符字符串、大写字符串）正确转换为数据库所需的GUID类型，确保数据插入和查询的兼容性。例如，在创建记录时，无论`Row`的`Id`属性或任何`Guid`类型的字段以何种形式赋值，UDataset都能妥善处理。

#### 批量操作与InsertOptions的兼容性

⚠️ **重要说明：批量操作优化策略**

不同数据库在批量操作方面有不同的优化策略，UDataset 确保 `InsertOptions` 在所有场景下都能正常工作：

**SQL Server批量操作策略**：
- **无InsertOptions或默认Throw**：根据数据量选择INSERT或SqlBulkCopy
- **有InsertOptions**：始终使用INSERT + OUTPUT，确保所有选项功能可用
- **性能影响**：SqlBulkCopy性能最高，但不支持InsertOptions功能

```csharp
// SQL Server批量插入策略示例
var users = GenerateUsers(5000); // 生成5000条记录

// 场景1：无选项，大数据量 - 可能使用SqlBulkCopy（最高性能）
var result1 = await connection.Create(users);

// 场景2：有选项 - 始终使用INSERT，确保功能完整性
var result2 = await connection.Create(users, InsertOptions.IgnoreConflicts());

// 场景3：复杂选项 - 功能和性能平衡
var result3 = await connection.Create(users, 
    InsertOptions.Upsert()
        .OnFields("Email")
        .ExcludeFields("CreatedAt"));
```

**PostgreSQL和MySQL**：
- 使用原生的批量INSERT语法（VALUES + RETURNING/返回机制）
- 天然支持所有InsertOptions功能
- 性能和功能兼顾

**最佳实践建议**：
1. **性能优先场景**：大数据量且无特殊需求时，不使用InsertOptions
2. **功能优先场景**：需要冲突处理、条件插入等，使用相应的InsertOptions
3. **混合策略**：先筛选需要特殊处理的记录使用InsertOptions，简单记录使用默认插入

### 性能优化

#### 批量操作

```csharp
// 大批量插入优化
const int batchSize = 1000;
var allRecords = new List<Row>();

for (int i = 0; i < 10000; i++)
{
    allRecords.Add(new Row("Users") 
    {
        ["Name"] = $"User_{i}",
        ["Age"] = 20 + i % 50,
        ["IsActive"] = i % 2 == 0
    });
}

// 分批处理
for (int i = 0; i < allRecords.Count; i += batchSize)
{
    var batch = allRecords.Skip(i).Take(batchSize);
    await connection.Create(batch);
    Console.WriteLine($"已处理 {Math.Min(i + batchSize, allRecords.Count)} / {allRecords.Count} 条记录");
}
```

#### 连接管理

```csharp
// 使用using语句确保连接正确释放
using var connection = ProviderFactory.CreateConnection("PostgreSQL", connectionString);

// 执行操作
var results = await connection.Query(new QueryExpression("Users"));

// 连接会在using块结束时自动释放
```

## 最佳实践

### 1. 错误处理

```csharp
try
{
    var result = await connection.Create(dataRow);
}
catch (ArgumentNullException ex)
{
    // 处理参数为空的情况
    Console.WriteLine($"参数错误: {ex.Message}");
}
catch (InvalidOperationException ex)
{
    // 处理操作无效的情况（如在没有事务时提交）
    Console.WriteLine($"操作错误: {ex.Message}");
}
catch (Exception ex)
{
    // 处理其他数据库相关错误
    Console.WriteLine($"数据库错误: {ex.Message}");
}
```

### 2. 版本控制和并发

```csharp
// 乐观并发控制示例
var originalRecord = await connection.Retrieve("Users", userId);
var originalVersion = originalRecord.Version;

// 修改数据
originalRecord["Name"] = "New Name";

try
{
    var updated = await connection.Update(originalRecord);
    Console.WriteLine($"更新成功，新版本: {updated.Version}");
}
catch (Exception ex)
{
    // 版本冲突，需要重新获取最新数据
    Console.WriteLine($"并发冲突: {ex.Message}");
    var latestRecord = await connection.Retrieve("Users", userId);
    // 处理冲突...
}
```

### 3. 资源管理

```csharp
// 推荐的资源管理模式
public class UserService
{
    private readonly IConnection _connection;
    
    public UserService(string connectionString)
    {
        _connection = ProviderFactory.CreateConnection("SqlServer", connectionString);
    }
    
    public async Task<IRow> CreateUserAsync(string name, int age)
    {
        var user = new Row("Users")
        {
            ["Name"] = name,
            ["Age"] = age,
            ["CreatedAt"] = DateTime.UtcNow
        };
        
        return await _connection.Create(user);
    }
    
    public void Dispose()
    {
        _connection?.Dispose();
    }
}
```

### 4. 参数化查询

```csharp
// 始终使用参数化查询防止SQL注入
var query = new QueryExpression("Users");
query.Filter = "Name LIKE @namePattern AND Age >= @minAge";
query.Parameters = new Dictionary<string, object>
{
    ["namePattern"] = $"%{searchTerm}%",  // 安全的参数化模糊查询
    ["minAge"] = minAge
};

var results = await connection.Query(query);
```

### 5. 聚合查询与 `GROUP BY` 自动补全

**描述**：UDataset的SQL转换器在处理包含聚合函数和非聚合列的查询时，会自动将所有 `SELECT` 列表中非聚合的列添加到 `GROUP BY` 子句中。这旨在避免SQL Server和PostgreSQL等数据库在 `GROUP BY` 限制下的常见错误（即"SELECT列表中非聚合的列必须出现在GROUP BY子句中"）。

**示例**：
```csharp
// 查询每个部门的平均年龄和员工数量，并选择部门名称
var query = new QueryExpression("Employees", "e");
query.AddSelect("e.Department", "Department"); // 非聚合列
query.AddSelect("COUNT(*) ", "EmployeeCount"); // 聚合函数
query.AddSelect("AVG(e.Age)", "AverageAge");    // 聚合函数

// 用户只需指定需要分组的主键列，UDataset会自动补全GROUP BY子句
query.GroupBy = new List<string> { "e.Department" }; 

var results = await connection.Query(query);
foreach (var result in results)
{
    Console.WriteLine($"部门: {result["Department"]}, 员工数: {result["EmployeeCount"]}, 平均年龄: {result["AverageAge"]}");
}
```

### 6. 列名歧义与表别名

**建议**：在进行涉及JOIN的查询时，特别是在 `SELECT` 和 `GROUP BY` 子句中引用列时，建议明确使用表别名来引用列（例如 `o.TotalAmount`, `c.Name`）。这有助于提高SQL的可读性，并避免在某些数据库中因列名歧义而导致的错误，即使UDataset在大多数情况下能智能处理。

**示例**：
```csharp
var query = new QueryExpression("Orders", "o");
query.AddSelect("o.TotalAmount");
query.AddSelect("c.Name", "CustomerName");
query.AddJoin("Customers", "c", "o.CustomerId = c.Id");

var results = await connection.Query(query);
```

### 7. 分页与排序的确定性

**建议**：当使用 `Top` (或SQL的 `LIMIT`) 和 `Skip` (或 `OFFSET`) 进行分页查询时，应**始终**伴随明确的 `ORDER BY` 子句。如果没有 `ORDER BY`，数据库无法保证结果的顺序，每次查询（即使 `Top` 和 `Skip` 相同）都可能返回不同的记录。

**示例**：
```csharp
var query = new QueryExpression("Products");
query.AddSelect("Name");
query.AddOrderBy("Price", SortDirection.Descending); // 必须指定排序
query.Skip = 10;
query.Top = 5;

var products = await connection.Query(query);
// 返回价格从高到低排序的第11到15个产品
```

### 8. 复杂查询的模拟（子查询与UNION）

**背景**：虽然UDataset致力于提供统一的查询接口，但对于某些极其复杂的嵌套子查询或 `UNION` 等高级SQL操作，其内部的SQL转换器可能无法直接生成最优或兼容所有数据库的SQL。在这种情况下，用户可以考虑在应用层进行分步查询和数据处理。

**子查询模拟示例**：

假设需要查询年龄大于所有活跃用户平均年龄的用户：

```csharp
// 第一步：计算活跃用户的平均年龄
var avgAgeQuery = new QueryExpression("Users");
avgAgeQuery.AddSelect("AVG(Age)", "AvgAge");
avgAgeQuery.Filter = "IsActive = @isActive";
avgAgeQuery.Parameters["isActive"] = true;

var avgResult = await connection.QuerySingle(avgAgeQuery);
var avgAge = Convert.ToDouble(avgResult["AvgAge"]);

// 第二步：查找年龄大于平均年龄的用户
var usersQuery = new QueryExpression("Users");
usersQuery.Filter = "Age > @avgAge";
usersQuery.Parameters["avgAge"] = avgAge;

var largeAgeUsers = await connection.Query(usersQuery);
foreach (var user in largeAgeUsers)
{
    Console.WriteLine($"姓名: {user["Name"]}, 年龄: {user["Age"]}");
}
```

**UNION操作模拟示例**：

假设需要合并来自不同条件下的用户列表：

```csharp
// 查询活跃用户
var activeUsersQuery = new QueryExpression("Users");
activeUsersQuery.AddSelect("Name");
activeUsersQuery.Filter = "IsActive = @isActive";
activeUsersQuery.Parameters["isActive"] = true;
var activeUsers = await connection.Query(activeUsersQuery);

// 查询年龄大于30的用户
var olderUsersQuery = new QueryExpression("Users");
olderUsersQuery.AddSelect("Name");
olderUsersQuery.Filter = "Age > @minAge";
olderUsersQuery.Parameters["minAge"] = 30;
var olderUsers = await connection.Query(olderUsersQuery);

// 在客户端合并并去重（模拟 UNION DISTINCT）
var combinedUserNames = new HashSet<string>();
foreach (var user in activeUsers)
{
    combinedUserNames.Add(user["Name"].ToString());
}
foreach (var user in olderUsers)
{
    combinedUserNames.Add(user["Name"].ToString());
}

Console.WriteLine("合并后的用户列表:");
foreach (var name in combinedUserNames)
{
    Console.WriteLine($"- {name}");
}
```

## 测试支持

### 单元测试示例

```csharp
[TestClass]
public class UserRepositoryTests
{
    private IConnection _connection;
    private ISchemaManager _schemaManager;
    
    [TestInitialize]
    public async Task Setup()
    {
        // 使用测试数据库
        PostgreSQLBootstrapper.Register();
        _connection = ProviderFactory.CreateConnection("PostgreSQL", testConnectionString);
        _schemaManager = _connection.SchemaManager;
        
        // 创建测试表
        var userTable = new Table("TestUsers");
        userTable.Attributes.Add(new Column("Id", DataType.Int, true) { IsPrimaryKey = true });
        userTable.Attributes.Add(new Column("Name", "string(100)", true));
        userTable.Attributes.Add(new Column("Age", DataType.Int));
        
        _schemaManager.Create(userTable);
    }
    
    [TestMethod]
    public async Task CreateUser_ValidData_ReturnsCreatedUser()
    {
        // Arrange
        var userData = new Row("TestUsers")
        {
            ["Name"] = "Test User",
            ["Age"] = 25
        };
        
        // Act
        var result = await _connection.Create(userData);
        
        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Id);
        Assert.AreEqual("Test User", result["Name"]);
        Assert.AreEqual(25, result["Age"]);
    }
    
    [TestCleanup]
    public async Task Cleanup()
    {
        // 清理测试数据
        if (_schemaManager.ExistsTable("TestUsers"))
        {
            _schemaManager.Drop(new Table("TestUsers"));
        }
        
        _connection?.Dispose();
    }
}
``` 
