# UDataset 查询扩展API

## 概述

UDataset查询扩展API通过`QueryExpression`类提供强大的复杂查询功能，支持Join关联、聚合函数、分组查询、排序和分页等高级查询场景，实现跨数据库的统一查询抽象。

## 核心类

### QueryExpression类

QueryExpression 类是通用的查询参数描述器，使用场景：构建复杂查询以检索数据。

#### 构造函数

- **`QueryExpression()`**: 初始化 `QueryExpression` 类的新实例。
- **`QueryExpression(string collection, string? alias = null)`**: 使用指定的主实体名称和可选别名初始化 `QueryExpression` 类的新实例。

#### 属性

- **`Collection` (string?)**: 查询的主实体名称。
- **`Alias` (string?)**: 主实体的别名。
- **`Select` (List<SelectItem>)**: **主表要选择的字段列表**。
- **`Join` (List<JoinClause>?)**: 联接子句列表。
- **`Filter` (string?)**: WHERE 子句的条件表达式。
- **`Parameters` (Dictionary<string, object>?)**: 查询参数。
- **`OrderBy` (List<OrderByItem>)**: 排序字段列表。
- **`GroupBy` (List<string>?)**: 分组字段列表。
- **`Having` (string?)**: HAVING 子句的条件表达式（用于聚合查询）。
- **`Top` (int?)**: 限制返回结果的数量。
- **`Skip` (int?)**: 跳过指定数量的记录。

#### 方法

- **`AddJoin(string targetEntityName, string? targetAlias = null, string? condition = null)`**: 添加一个连接子句。
- **`AddJoin(JoinType joinType, string targetEntityName, string? targetAlias = null, string? condition = null)`**: 添加指定类型的连接子句。
- **`AddSelect(string columnExpression, string? alias = null)`**: 添加一个选择字段。
- **`AddSelect(string expression)`**: 从字符串表达式添加选择字段。
- **`AddOrderBy(string fieldExpression, SortDirection direction = SortDirection.Ascending)`**: 添加一个排序字段。
- **`AddOrderBy(string expression)`**: 从字符串表达式添加排序字段。

### JoinClause类

JoinClause 类用于表示查询中的一个JOIN操作，并**支持指定该关联表需要查询的字段**。

#### 构造函数

- **`JoinClause()`**: 初始化 `JoinClause` 类的新实例，默认连接类型为 `Inner`。
- **`JoinClause(string targetEntityName, string? targetAlias, string? condition)`**: 使用目标实体名称、别名和条件初始化 `JoinClause` 类的新实例，默认连接类型为 `Inner`。
- **`JoinClause(JoinType joinType, string targetEntityName, string? targetAlias = null, string? condition = null)`**: 使用指定的连接类型、目标实体名称、别名和条件初始化 `JoinClause` 类的新实例。

#### 属性

- **`JoinType` (JoinType)**: JOIN类型（Inner、Left、Right、Full、Cross）。
- **`Collection` (string)**: 目标实体名称。
- **`Alias` (string?)**: 目标别名。
- **`Condition` (string?)**: 连接条件。
- **`Select` (List<SelectItem>)**: **该关联表需要查询的字段列表**。

#### 方法

- **`AddSelect(string columnExpression, string? alias = null)`**: 添加一个选择字段。
- **`AddSelect(string expression)`**: 从字符串表达式添加选择字段。

### SelectItem类

表示选择字段项。

#### 构造函数

- **`SelectItem(string columnExpression, string? alias = null)`**: 使用列表达式和可选别名初始化 `SelectItem` 类的新实例。

#### 属性

- **`ColumnExpression` (string)**: 列表达式。
- **`Alias` (string?)**: 字段别名。

#### 方法

- **`FromString(string expression)`**: 从字符串解析选择字段。

### OrderByItem类

表示排序字段项。

#### 构造函数

- **`OrderByItem(string fieldExpression, SortDirection direction = SortDirection.Ascending)`**: 使用字段表达式和可选排序方向初始化 `OrderByItem` 类的新实例。

#### 属性

- **`FieldExpression` (string)**: 字段表达式。
- **`Direction` (SortDirection)**: 排序方向。

## 枚举类型

### JoinType枚举

表示JOIN类型，使用场景：定义查询中的连接类型。

| 成员 | 说明 |
|------|------|
| `Inner` | 内连接，仅返回匹配的记录。 |
| `Left` | 左外连接，返回所有左表记录和匹配的右表记录。 |
| `Right` | 右外连接，返回所有右表记录和匹配的左表记录。 |
| `Full` | 全外连接，返回所有两表记录。 |
| `Cross` | 交叉连接，返回笛卡尔积。 |

### SortDirection枚举

表示排序方向。

| 成员 | 说明 |
|------|------|
| `Ascending` | 升序排列。 |
| `Descending` | 降序排列。 |

## 使用示例

### 基础查询

#### 简单字段选择

```csharp
// 查询指定字段
var query = new QueryExpression("Users", "u");
query.AddSelect("u.Name");
query.AddSelect("u.Age");
query.AddSelect("u.IsActive");

var results = await connection.Query(query);
```

#### 字段别名

```csharp
// 使用字段别名
var query = new QueryExpression("Users", "u");
query.AddSelect("u.Name", "UserName");
query.AddSelect("u.CreatedAt", "RegistrationDate");

var results = await connection.Query(query);
foreach (var row in results)
{
    Console.WriteLine($"用户: {row["UserName"]}, 注册时间: {row["RegistrationDate"]}");
}
```

#### 条件查询

```csharp
// 带参数的条件查询
var query = new QueryExpression("Users", "u");
query.AddSelect("u.Name");
query.AddSelect("u.Age");
query.Filter = "u.Age > @minAge AND u.IsActive = @isActive";
query.Parameters = new Dictionary<string, object>
{
    ["minAge"] = 25,
    ["isActive"] = true
};

var results = await connection.Query(query);
```

### JOIN查询

#### 内连接 (Inner Join)

```csharp
// 用户和订单的内连接
var query = new QueryExpression("Users", "u");
query.AddSelect("u.Name", "UserName");
query.AddSelect("o.TotalAmount", "OrderAmount");
query.AddSelect("o.OrderDate");

// 添加内连接，并选择关联表的字段
var orderJoin = new JoinClause(JoinType.Inner, "Orders", "o", "u.Id = o.UserId");
orderJoin.AddSelect("o.OrderNumber", "OrderNo"); // 选择关联表的字段
orderJoin.AddSelect("o.Status"); // 选择关联表的字段
query.Join.Add(orderJoin);

var results = await connection.Query(query);
foreach (var row in results)
{
    Console.WriteLine($"用户: {row["UserName"]}, 订单号: {row["OrderNo"]}, 订单金额: {row["OrderAmount"]}");
}
```

#### 左外连接 (Left Join)

```csharp
// 查询所有用户及其订单（包括没有订单的用户）
var query = new QueryExpression("Users", "u");
query.AddSelect("u.Name", "UserName");
query.AddSelect("o.TotalAmount", "OrderAmount");
query.AddSelect("o.OrderDate");

// 左外连接：显示所有用户，包括没有订单的用户，并选择关联表的字段
var orderJoin = new JoinClause(JoinType.Left, "Orders", "o", "u.Id = o.UserId");
orderJoin.AddSelect("o.OrderNumber", "OrderNo"); // 选择关联表的字段
orderJoin.AddSelect("o.Status"); // 选择关联表的字段
query.Join.Add(orderJoin);

var results = await connection.Query(query);
foreach (var row in results)
{
    Console.WriteLine($"用户: {row["UserName"]}, 订单号: {row.ContainsKey("OrderNo") ? row["OrderNo"] : "无"}, 订单金额: {row.ContainsKey("OrderAmount") ? row["OrderAmount"] : "0"}");
}
```

#### 右外连接 (Right Join)

```csharp
// 查询所有订单及其对应的用户（包括没有用户的孤立订单）
var query = new QueryExpression("Orders", "o");
query.AddSelect("o.OrderNumber", "OrderNo");
query.AddSelect("o.TotalAmount", "OrderAmount");
query.AddSelect("u.Name", "UserName");

// 右外连接：显示所有订单，包括没有对应用户的订单
var userJoin = new JoinClause(JoinType.Right, "Users", "u", "o.UserId = u.Id");
userJoin.AddSelect("u.Email"); // 选择关联表的字段
query.Join.Add(userJoin);

var results = await connection.Query(query);
foreach (var row in results)
{
    Console.WriteLine($"订单号: {row["OrderNo"]}, 订单金额: {row["OrderAmount"]}, 用户: {row.ContainsKey("UserName") ? row["UserName"] : "无"}");
}
```

#### 全外连接 (Full Join)

```csharp
// 查询所有用户和所有订单（包括没有订单的用户和没有用户的订单）
var query = new QueryExpression("Users", "u");
query.AddSelect("u.Name", "UserName");
query.AddSelect("o.OrderNumber", "OrderNo");
query.AddSelect("o.TotalAmount", "OrderAmount");

// 全外连接：返回所有用户和所有订单
var orderJoin = new JoinClause(JoinType.Full, "Orders", "o", "u.Id = o.UserId");
query.Join.Add(orderJoin);

var results = await connection.Query(query);
foreach (var row in results)
{
    Console.WriteLine($"用户: {row.ContainsKey("UserName") ? row["UserName"] : "无"}, 订单号: {row.ContainsKey("OrderNo") ? row["OrderNo"] : "无"}, 订单金额: {row.ContainsKey("OrderAmount") ? row["OrderAmount"] : "0"}");
}
```

#### 交叉连接 (Cross Join)

```csharp
// 查询用户和订单的笛卡尔积（不常用）
var query = new QueryExpression("Users", "u");
query.AddSelect("u.Name", "UserName");
query.AddSelect("o.OrderNumber", "OrderNo");

// 交叉连接：返回笛卡尔积
query.AddJoin(JoinType.Cross, "Orders", "o"); // 交叉连接不需要条件
// Note: Cross Join might not be directly supported by some underlying databases or require specific syntax.
// UDataset will attempt to translate this, but it's often more efficient to achieve this in application logic if possible.

var results = await connection.Query(query);
foreach (var row in results)
{
    Console.WriteLine($"用户: {row["UserName"]}, 订单号: {row["OrderNo"]}");
}
```

#### 多表连接

```csharp
// 三表连接：用户 -> 订单 -> 订单详情，并选择关联表的字段
var query = new QueryExpression("Users", "u");
query.AddSelect("u.Name", "UserName");

// 用户到订单
var orderJoin = new JoinClause(JoinType.Inner, "Orders", "o", "u.Id = o.UserId");
orderJoin.AddSelect("o.OrderDate");
query.Join.Add(orderJoin);

// 订单到订单详情
var orderDetailJoin = new JoinClause(JoinType.Inner, "OrderDetails", "od", "o.Id = od.OrderId");
orderDetailJoin.AddSelect("od.ProductName");
orderDetailJoin.AddSelect("od.Quantity");
query.Join.Add(orderDetailJoin);

var results = await connection.Query(query);
```

### 聚合查询

#### 基础聚合函数

```csharp
// 统计信息查询
var query = new QueryExpression("Users");
query.AddSelect("COUNT(*)", "TotalUsers");
query.AddSelect("AVG(Age)", "AverageAge");
query.AddSelect("MIN(Age)", "MinAge");
query.AddSelect("MAX(Age)", "MaxAge");
query.AddSelect("SUM(Age)", "TotalAge");

var results = await connection.Query(query);
var stats = results.First();

Console.WriteLine($"总用户数: {stats["TotalUsers"]}");
Console.WriteLine($"平均年龄: {stats["AverageAge"]:F1}");
Console.WriteLine($"最小年龄: {stats["MinAge"]}");
Console.WriteLine($"最大年龄: {stats["MaxAge"]}");
```

#### 带条件的聚合

```csharp
// 查询活跃用户的统计信息
var query = new QueryExpression("Users");
query.AddSelect("COUNT(*)", "ActiveUserCount");
query.AddSelect("AVG(Age)", "AvgAge");
query.Filter = "IsActive = @isActive";
query.Parameters = new Dictionary<string, object>
{
    ["isActive"] = true
};

var results = await connection.Query(query);
```

### 分组查询 (GROUP BY)

#### 单字段分组

```csharp
// 按活跃状态分组统计
var query = new QueryExpression("Users");
query.AddSelect("IsActive");
query.AddSelect("COUNT(*)", "UserCount");
query.AddSelect("AVG(Age)", "AvgAge");
query.GroupBy = new List<string> { "IsActive" };

var results = await connection.Query(query);
foreach (var group in results)
{
    Console.WriteLine($"活跃状态: {group["IsActive"]}, 用户数: {group["UserCount"]}, 平均年龄: {group["AvgAge"]:F1}");
}
```

#### 多字段分组

```csharp
// 按部门和状态分组
var query = new QueryExpression("Users", "u");
query.AddSelect("u.Department");
query.AddSelect("u.IsActive");
query.AddSelect("COUNT(*)", "Count");
query.AddSelect("AVG(u.Age)", "AverageAge");
query.GroupBy = new List<string> { "u.Department", "u.IsActive" };
query.AddOrderBy("u.Department");
query.AddOrderBy("u.IsActive");

var results = await connection.Query(query);
```

#### HAVING子句过滤

```csharp
// 使用HAVING子句过滤分组结果
var query = new QueryExpression("Users");
query.AddSelect("IsActive", "ActiveStatus");
query.AddSelect("COUNT(*)", "UserCount");
query.AddSelect("AVG(Age)", "AvgAge");
query.GroupBy = new List<string> { "IsActive" };
query.Having = "COUNT(*) >= @minCount AND AVG(Age) > @minAvgAge";
query.Parameters["minCount"] = 2;
query.Parameters["minAvgAge"] = 30;

var results = await connection.Query(query);
foreach (var group in results)
{
    Console.WriteLine($"活跃状态: {group["ActiveStatus"]}, 用户数: {group["UserCount"]}, 平均年龄: {group["AvgAge"]:F1}");
}
```

#### JOIN与分组结合

```csharp
// 按用户统计订单信息
var query = new QueryExpression("Orders", "o");
query.AddSelect("u.Name", "UserName");
query.AddSelect("COUNT(*)", "OrderCount");
query.AddSelect("SUM(o.TotalAmount)", "TotalSales");
query.AddSelect("AVG(o.TotalAmount)", "AvgOrderAmount");

query.AddJoin(JoinType.Inner, "Users", "u", "o.UserId = u.Id");
query.GroupBy = new List<string> { "o.UserId", "u.Name" };
query.AddOrderBy("TotalSales", SortDirection.Descending);

var results = await connection.Query(query);
```

### 排序查询 (ORDER BY)

#### 单字段排序

```csharp
// 按年龄升序排列
var query = new QueryExpression("Users");
query.AddSelect("Name");
query.AddSelect("Age");
query.AddOrderBy("Age");

var results = await connection.Query(query);
```

#### 多字段排序

```csharp
// 多级排序：先按部门，再按年龄降序
var query = new QueryExpression("Users");
query.AddSelect("Name");
query.AddSelect("Department");
query.AddSelect("Age");
query.AddOrderBy("Department", SortDirection.Ascending);
query.AddOrderBy("Age", SortDirection.Descending);

var results = await connection.Query(query);
```

#### 字符串表达式排序

```csharp
// 使用字符串表达式指定排序
var query = new QueryExpression("Users");
query.AddSelect("Name");
query.AddSelect("Age");
query.AddOrderBy("Name ASC");
query.AddOrderBy("Age DESC");

var results = await connection.Query(query);
```

### 分页查询

#### 基础分页

```csharp
// 分页查询：跳过前20条，取10条
var query = new QueryExpression("Users");
query.AddSelect("Name");
query.AddSelect("Age");
query.AddOrderBy("Name"); // 分页需要指定排序
query.Skip = 20;
query.Top = 10;

var results = await connection.Query(query);
```

#### 带条件的分页

```csharp
// 查询活跃用户的第3页数据
int pageSize = 10;
int pageNumber = 3;

var query = new QueryExpression("Users");
query.AddSelect("Name");
query.AddSelect("Age");
query.AddSelect("CreatedAt");
query.Filter = "IsActive = @isActive";
query.Parameters = new Dictionary<string, object> { ["isActive"] = true };
query.AddOrderBy("CreatedAt", SortDirection.Descending);
query.Skip = (pageNumber - 1) * pageSize;
query.Top = pageSize;

var results = await connection.Query(query);
```

### 复杂查询示例

#### 多表连接
```csharp
// 三表连接：用户 -> 订单 -> 订单详情
var query = new QueryExpression("Users", "u");
query.AddSelect("u.Name", "UserName");
query.AddSelect("o.OrderDate");
query.AddSelect("od.ProductName");
query.AddSelect("od.Quantity");

// 用户到订单
query.AddJoin(JoinType.Inner, "Orders", "o", "u.Id = o.UserId");
// 订单到订单详情
query.AddJoin(JoinType.Inner, "OrderDetails", "od", "o.Id = od.OrderId");

var results = await connection.Query(query);
```

#### 聚合查询与分页结合
```csharp
// 按用户统计订单信息，并分页
var query = new QueryExpression("Orders", "o");
query.AddSelect("u.Name", "UserName");
query.AddSelect("COUNT(*)", "OrderCount");
query.AddSelect("SUM(o.TotalAmount)", "TotalSales");
query.AddSelect("AVG(o.TotalAmount)", "AvgOrderAmount");

query.AddJoin(JoinType.Inner, "Users", "u", "o.UserId = u.Id");
query.GroupBy = new List<string> { "o.UserId", "u.Name" };
query.AddOrderBy("TotalSales", SortDirection.Descending);
query.Top = 10;
query.Skip = 20;

var results = await connection.Query(query);
```

#### 关键字自动转义

UDataset 能够自动识别并转义 SQL 关键字作为表名或列名的情况，确保在不同数据库中生成的 SQL 语法正确，避免因关键字冲突导致的查询失败。

```csharp
// 示例：使用SQL关键字"order"作为表名，"group"作为字段名
var query = new QueryExpression("order"); // 表名是关键字
query.AddSelect("Id");
query.AddSelect("TotalAmount");
query.AddSelect("group", "OrderGroup"); // 字段名是关键字，并设置别名
query.Filter = "TotalAmount > @minAmount";
query.Parameters["minAmount"] = 100;
query.Top = 10;

// UDataset 会根据数据库类型自动转义这些关键字
// 例如：
// SQL Server: SELECT TOP 10 [Id], [TotalAmount], [group] AS [OrderGroup] FROM [order] WHERE [TotalAmount] > @minAmount
// PostgreSQL: SELECT "Id", "TotalAmount", "group" AS "OrderGroup" FROM "order" WHERE "TotalAmount" > @minAmount LIMIT 10
// MySQL: SELECT `Id`, `TotalAmount`, `group` AS `OrderGroup` FROM `order` WHERE `TotalAmount` > @minAmount LIMIT 10

var results = await connection.Query(query);
```

#### 聚合查询与JOIN结合（自动GROUP BY）

当聚合查询包含JOIN操作且SELECT列表中包含非聚合列时，UDataset能够智能地将这些非聚合列自动添加到GROUP BY子句中，以满足SQL规范，避免常见的`不在聚合函数或GROUP BY子句中`的错误。这简化了复杂聚合查询的构建。

```csharp
// 查询每个客户的总销售额、平均订单金额、订单数量以及客户信息
var query = new QueryExpression("Orders", "o");
query.AddSelect("SUM(o.TotalAmount)", "totalSales");
query.AddSelect("AVG(o.TotalAmount)", "avgOrderAmount");
query.AddSelect("COUNT(o.Id)", "orderCount");
query.AddSelect("o.CustomerId"); // Orders表中的非聚合列

// 连接Customers表，并选择客户名称和城市
var customerJoin = new JoinClause(JoinType.Inner, "Customers", "c", "o.CustomerId = c.Id");
customerJoin.AddSelect("c.Name", "CustomerName"); // Customers表中的非聚合列
customerJoin.AddSelect("c.City", "CustomerCity"); // Customers表中的非聚合列
query.Join.Add(customerJoin);

query.Filter = "o.Status IN (@status1, @status2)";
query.Parameters = new Dictionary<string, object>
{
    ["status1"] = 2,
    ["status2"] = 3
};

// 开发者无需手动添加所有非聚合列到GroupBy，UDataset会自动处理
// query.GroupBy = new List<string> { "o.CustomerId", "c.Name", "c.City" }; // 这一行现在不是必需的，但手动指定也无妨

var results = await connection.Query(query);
foreach (var row in results)
{
    Console.WriteLine($"客户ID: {row["CustomerId"]}, 客户名称: {row["CustomerName"]}, 城市: {row["CustomerCity"]}, 总销售额: {row["totalSales"]:F2}, 订单数: {row["orderCount"]}");
}
```

#### CAST类型转换

UDataset支持在查询中使用CAST函数进行类型转换，确保在不同数据库中的类型转换语法正确。

```csharp
// 使用CAST进行类型转换
var query = new QueryExpression("TestTable");
query.AddSelect("CAST(Age AS VARCHAR) AS AgeString");
query.AddSelect("CAST(Score AS VARCHAR) AS ScoreString");
query.AddOrderBy("Name");

// 不同数据库会生成相应的CAST语法：
// SQL Server: CAST([Age] AS VARCHAR) AS [AgeString]
// PostgreSQL: CAST("Age" AS TEXT) AS "AgeString"  
// MySQL: CAST(`Age` AS CHAR) AS `AgeString`

var results = await connection.Query(query);
```

#### 布尔值处理

UDataset能够正确处理布尔值，在不同数据库中生成相应的布尔语法。

```csharp
// 布尔值查询
var query = new QueryExpression("Users");
query.AddSelect("Name");
query.AddSelect("IsActive");
query.Filter = "IsActive = @isActive";
query.Parameters["isActive"] = true;

// 不同数据库会生成相应的布尔语法：
// SQL Server: [IsActive] = @isActive
// PostgreSQL: "IsActive" = @isActive
// MySQL: `IsActive` = @isActive

var results = await connection.Query(query);
```

## 最佳实践

### 1. 查询构建模式

```csharp
// 使用Builder模式构建复杂查询
public class UserQueryBuilder
{
    private readonly QueryExpression _query;
    
    public UserQueryBuilder()
    {
        _query = new QueryExpression("Users", "u");
    }
    
    public UserQueryBuilder SelectBasicInfo()
    {
        _query.AddSelect("u.Id");
        _query.AddSelect("u.Name");
        _query.AddSelect("u.Email");
        return this;
    }
    
    public UserQueryBuilder WithDepartment()
    {
        _query.AddSelect("u.Department");
        return this;
    }
    
    public UserQueryBuilder WithOrderStats()
    {
        _query.AddSelect("COUNT(o.Id)", "OrderCount");
        _query.AddSelect("SUM(o.TotalAmount)", "TotalSpent");
        _query.AddJoin(JoinType.Left, "Orders", "o", "u.Id = o.UserId");
        _query.GroupBy = new List<string> { "u.Id", "u.Name", "u.Email" };
        return this;
    }
    
    public UserQueryBuilder FilterByActive(bool isActive = true)
    {
        _query.Filter = "u.IsActive = @isActive";
        _query.Parameters["isActive"] = isActive;
        return this;
    }
    
    public UserQueryBuilder OrderByName()
    {
        _query.AddOrderBy("u.Name");
        return this;
    }
    
    public UserQueryBuilder Paginate(int page, int size)
    {
        _query.Skip = (page - 1) * size;
        _query.Top = size;
        return this;
    }
    
    public QueryExpression Build() => _query;
}

// 使用示例
var query = new UserQueryBuilder()
    .SelectBasicInfo()
    .WithOrderStats()
    .FilterByActive()
    .OrderByName()
    .Paginate(1, 20)
    .Build();

var results = await connection.Query(query);
```

### 2. 性能优化建议

```csharp
// 确保查询中使用的字段有适当的索引
// 特别是WHERE、JOIN、ORDER BY子句中的字段
var query = new QueryExpression("Users");
query.Filter = "Department = @dept AND IsActive = @active"; // 需要(Department, IsActive)索引
query.AddOrderBy("CreatedAt", SortDirection.Descending);    // 需要CreatedAt索引
```

### 3. 跨数据库兼容性

```csharp
// 相同的查询代码在不同数据库中生成不同的SQL
var query = new QueryExpression("Users");
query.AddSelect("Name");
query.AddOrderBy("Name");
query.Top = 10;

// SQL Server: SELECT TOP 10 [Name] FROM [Users] ORDER BY [Name]
// PostgreSQL: SELECT "Name" FROM "Users" ORDER BY "Name" LIMIT 10
// MySQL: SELECT `Name` FROM `Users` ORDER BY `Name` LIMIT 10
``` 