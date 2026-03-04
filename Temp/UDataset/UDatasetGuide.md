# UDataset库使用说明

## 1. 引言

### 1.1 设计理念
UDataset库旨在提供**一站式**的数据服务，支持通用的数据操作，简化数据管理。我们的目标是让你**写代码像写自然语言一样流畅**，让数据操作变得**简单、直观、高效**。

### 1.2 架构概述
UDataset采用模块化架构设计，将核心抽象与具体数据库实现完全分离：

- **UDataset.Core**: 核心抽象层，定义统一的接口和数据模型
- **UDataset.SQLServer**: SQL Server数据库实现
- **UDataset.PostgreSQL**: PostgreSQL数据库实现
- **UDataset.MySQL**: MySQL数据库实现
- **UDataset.DuckDB**: DuckDB数据库实现
- **UDataset.Oracle**: Oracle数据库实现

## 2. 核心概念与类型

### 2.1 核心类详细说明

#### Row类
Row 类用于保存单条数据记录，支持动态访问数据。它在场景中用于快速处理单条记录，例如在 Web API 中返回用户数据。

- **属性：**
  - `Schema` (string): 获取此记录所属的实体（表）名称，使用场景：标识记录来源。
  - `Id` (object?): 获取或设置记录的唯一标识符。在更新和删除操作中，此字段为必填，使用场景：作为主键在 CRUD 操作中。
  - `Version` (string?): 获取或设置行版本号，用于乐观并发控制，使用场景：防止数据冲突，如在多用户编辑系统中。

- **方法：**
  - `this[string key]` (object?): 索引器，用于通过字段名获取或设置字段值，使用场景：动态访问记录字段。
  - `Add(string key, object? value)`: 向记录中添加一个字段及其值，使用场景：构建新记录时动态添加属性。
  - `Remove(string key)`: 从记录中删除指定字段，使用场景：清理记录时移除不必要字段。
  - `TryGetValue(string key, out object? value)`: 尝试获取指定字段的值，使用场景：安全检查字段是否存在。
  - `ContainsKey(string key)`: 检查记录是否包含指定字段，使用场景：验证数据完整性。
  - `GetEnumerator()`: 获取用于遍历记录中所有字段的枚举器，使用场景：迭代记录字段。
  - `CreateUpdateRow()`: 创建一个新的Row对象用于更新，自动携带Schema、Id和Version信息，使用场景：局部更新记录。

#### Table类
Table 类用于定义实体（数据库表）的数据结构和元数据信息，使用场景：设计数据库模式时定义表结构。

- **属性：**
  - `Name` (string): 集合（表）的名称，使用场景：指定表标识。
  - `PrimaryKey` (string): 主键字段的名称，默认为 `_id`，使用场景：确保数据唯一性。
  - `VersionAttribute` (string?): 行版本字段的名称，用于乐观锁控制，使用场景：管理并发更新。
  - `Attributes` (List<Column>): 包含此表所有字段定义的列表，使用场景：定义表列。
  - `Indexes` (List<TableIndex>): 包含此表所有索引定义的列表，使用场景：优化查询性能。

- **方法：**
  - `this[string name]` (Column): 索引器，通过字段名称获取字段定义，使用场景：快速访问表字段。
  - `AddAttribute(Column attribute)`: 向表中添加一个字段定义，使用场景：扩展表结构。
  - `AddIndex(TableIndex index)`: 向表中添加一个索引定义，使用场景：创建索引以提升查询效率。
  - `InferKeyFields()`: 从 `Attributes` 中推断主键字段和版本字段，使用场景：自动生成表元数据。
  - `GetPrimaryKeyColumn()`: 获取主键字段的定义，使用场景：验证主键。
  - `GetVersionColumn()`: 获取版本字段的定义，使用场景：处理并发控制。
  - `GetColumn(string columnName)`: 根据字段名获取定义，使用场景：检索特定字段信息。

#### Column类
Column 类用于定义实体字段的详细元数据信息，使用场景：构建表结构时指定字段属性。

- **属性：**
  - `Name` (string): 属性（字段）的名称，使用场景：标识字段。
  - `DataType` (DataType): 字段的数据类型，使用场景：确保数据类型一致性。
  - `IsRequired` (bool): 指示字段是否必填，使用场景：数据验证。
  - `DefaultValue` (object?): 字段的默认值，使用场景：初始化字段。
  - `Properties` (Dictionary<ColumnProperty, object>): 存储字段的特定属性，使用场景：自定义字段约束。
  - `Collection` (string?): 所属集合的名称，使用场景：关联字段与表。
  - `IsPrimaryKey` (bool): 指示此字段是否为主键，使用场景：定义唯一标识。

- **方法：**
  - `GetLength()`: 获取字符串或二进制字段的长度，使用场景：检查字段大小限制。
  - `GetPrecision()`: 获取数值字段的总位数精度，使用场景：处理精确计算。
  - `GetScale()`: 获取数值字段的小数位数，使用场景：管理小数点精度。
  - `GetMinValue()`: 获取数值字段的最小值约束，使用场景：数据边界验证。
  - `GetMaxValue()`: 获取数值字段的最大值约束，使用场景：数据边界验证。
  - `IsUnique()`: 检查字段是否具有唯一性约束，使用场景：确保数据唯一。
  - `IsIndexed()`: 检查字段是否已创建索引，使用场景：查询优化检查。
  - `GetForeignKeyTable()`: 获取外键字段引用的目标表名称，使用场景：处理关系型数据。
  - `GetForeignKeyColumn()`: 获取外键字段引用的目标列名称，使用场景：定义外键关系。
  - `GetDescription()`: 获取字段的描述信息，使用场景：文档化字段。
  - `GetCheckConstraints()`: 获取字段的检查约束表达式，使用场景：添加数据验证规则。
  - `GetOptionSetValues()`: 获取选项集类型字段的所有可选值列表，使用场景：处理枚举型数据。
  - `IsForeignKey()`: 检查当前字段是否为外键字段，使用场景：验证关系。
  - `ToTypeExpression()`: 将当前列定义转换为字符串类型的表达式，使用场景：生成SQL语句。
  - `ToString()`: 返回列定义的字符串表示，使用场景：序列化字段信息。

##### 列类型表达式 (Column Type Expression)
UDataset支持使用字符串表达式来定义列的数据类型和各种属性，这提供了一种简洁且灵活的方式来声明列的元数据。这些表达式在`Column`类的构造函数中被解析，并转化为内部的`DataType`和`Properties`字典。

**表达式格式：**
`DataTypeName(parameter1, parameter2, ..., key1=value1, key2=value2, ...)`

- `DataTypeName`: 核心数据类型名称（如 `string`, `int`, `decimal`, `lookup`, `optionset` 等）。
- `parameters`: 位置参数，通常用于指定类型的主要约束，例如字符串的长度或数值的精度和小数位。
- `key=value`: 命名参数，用于指定更具体的列属性，如 `unique`, `indexed`, `description`, `min`, `max` 等。参数之间使用逗号 `,` 分隔。

**示例：**

1.  **基本数据类型及长度/范围约束：**
    -   `string(200)`: 定义一个最大长度为200的字符串字段。
    -   `nvarchar(100)`: SQL Server特有的NVARCHAR类型，映射为UDataset的`String`类型，长度为100。
    -   `text`: 大文本字段，映射为UDataset的`Text`类型。
    -   `int(min=0,max=200)`: 定义一个整数，最小值为0，最大值为200。
    -   `int(0,200)`: 整数范围的简化形式，等同于`min=0,max=200`。
    -   `decimal(10,2)`: 定义一个十进制数，总共10位，其中2位是小数。
    -   `decimal(precision=10,scale=2)`: 十进制数的命名参数形式，等同于`decimal(10,2)`。
    -   `money`: SQL Server特有的货币类型，映射为UDataset的`Decimal`类型。
    -   `bigint`: 64位整数。
    -   `double`: 双精度浮点数。
    -   `date`, `datetime`, `time`, `timespan`, `boolean`, `guid`, `json`, `xml`: 其他直接映射的类型。
    -   `uuid`: PostgreSQL特有的UUID类型，映射为UDataset的`Guid`类型。
    -   `bytea`, `blob`, `binary(1000)`, `varbinary`: 二进制数据类型，映射为UDataset的`ByteArray`类型，`binary(1000)`可指定长度。

2.  **引用类型：**
    -   `lookup(user)`: 定义一个外键字段，引用名为`user`的表。
    -   `lookup(department,column=id)` 或 `lookup(department,id)`: 定义一个外键字段，引用`department`表的`id`列。

3.  **选项集类型：**
    -   `optionset(active,inactive,deleted)`: 定义一个选项集字段，其可选值为`active`, `inactive`, `deleted`。

4.  **其他属性约束：**
    -   `string(100,unique=true)`: 字符串字段，最大长度100，并具有唯一性约束。
    -   `string(50,indexed=true,indexOrder=1)`: 字符串字段，最大长度50，创建索引，索引顺序为1。
    -   `string(20,check=status IN ('active','inactive','pending'))`: 字符串字段，最大长度20，并带有SQL检查约束表达式。
    -   `string(20,unique=true,indexed=true,indexOrder=2,description='产品唯一编码')`: 组合多种属性约束，包括唯一性、索引、索引顺序和描述。
    

这些表达式提供了一种声明式的、数据库无关的方式来定义数据模型。

#### JoinClause类
JoinClause 类用于表示查询中的一个JOIN操作，并**支持指定该关联表需要查询的字段**。

- **属性：**
  - `Collection` (string): 目标实体名称。
  - `Alias` (string?): 目标别名。
  - `Condition` (string?): 连接条件。
  - `Select` (List<SelectItem>): **该关联表需要查询的字段列表**。

- **方法：**
  - `AddSelect(string columnExpression, string? alias = null)`: 添加一个选择字段。
  - `AddSelect(string expression)`: 从字符串表达式添加选择字段。

#### QueryExpression类
QueryExpression 类是通用的查询参数描述器，使用场景：构建复杂查询以检索数据。

- **属性：**
  - `Collection` (string?): 查询的主实体名称。
  - `Alias` (string?): 主实体的别名。
  - `Select` (List<SelectItem>): **主表要选择的字段列表**。
  - `Join` (List<JoinClause>?): 联接子句列表。
  - `Filter` (string?): WHERE 子句的条件表达式。
  - `Parameters` (Dictionary<string, object>?): 查询参数。
  - `OrderBy` (List<OrderByItem>): 排序字段列表。
  - `GroupBy` (List<string>?): 分组字段列表。
  - `Top` (int?): 限制返回结果的数量。
  - `Skip` (int?): 跳过指定数量的记录。

- **方法：**
  - `AddJoin(string targetEntityName, string? targetAlias = null, string? condition = null)`: 添加一个连接子句。
  - `AddSelect(string columnExpression, string? alias = null)`: 添加一个选择字段。
  - `AddSelect(string expression)`: 从字符串表达式添加选择字段。
  - `AddOrderBy(string fieldExpression, SortDirection direction = SortDirection.Ascending)`: 添加一个排序字段。
  - `AddOrderBy(string expression)`: 从字符串表达式添加排序字段。

#### IConnection接口
IConnection 接口定义了数据提供者的核心 CRUD 操作和事务管理，使用场景：执行数据库交互。

- **属性：**
  - `SchemaManager` (ISchemaManager): 获取模式管理器实例，使用场景：管理表结构。
  - `SqlTransformer` (ISqlTransformer): 获取 SQL 转换器实例，使用场景：生成数据库特定SQL。

- **方法：**
  - `Create(IRow dataRow)`: 创建单条数据记录，使用场景：新增数据。
  - `Create(IEnumerable<IRow> dataRows)`: 批量创建数据记录，使用场景：高效批量插入。
  - `Update(IRow dataRow)`: 更新单条数据记录，使用场景：修改数据。
  - `Update(IEnumerable<IRow> dataRows)`: 批量更新数据记录，使用场景：批量修改。
  - `Delete(IRow dataRow)`: 删除单条数据记录，使用场景：移除数据。
  - `Delete(IEnumerable<IRow> dataRows)`: 批量删除数据记录，使用场景：批量移除。
  - `Delete(string entityName, object id)`: 通过 ID 删除单条数据记录，使用场景：基于主键删除。
  - `Delete(string entityName, IEnumerable<object> ids)`: 通过多个 ID 批量删除数据记录，使用场景：批量删除。
  - `Retrieve(string entityName, object Id)`: 通过 ID 获取单条数据记录，使用场景：查询单个记录。
  - `Retrieve(string entityName, IEnumerable<object> ids)`: 通过多个 ID 获取多条数据记录，使用场景：批量查询。
  - `Query(QueryExpression queryExpression)`: 执行查询并返回多条记录，使用场景：复杂查询。
  - `QuerySingle(QueryExpression queryExpression)`: 执行查询并返回单条记录，使用场景：获取单一结果。
  - `BeginTransaction()`: 开始一个数据库事务，使用场景：确保操作原子性。
  - `CommitTransaction()`: 提交当前事务，使用场景：确认操作。
  - `RollbackTransaction()`: 回滚当前事务，使用场景：撤销操作。

#### ISchemaManager接口
ISchemaManager 接口负责数据库表结构的创建、修改、删除，使用场景：动态管理数据库模式。

- **属性：**
  - `ConnectionString` (string): 获取或设置数据库连接字符串，使用场景：配置连接。
  - `this[string name]` (Table): 索引器，通过集合名称获取定义，使用场景：访问表信息。

- **方法：**
  - `Create(Table collection)`: 创建集合，使用场景：新建表。
  - `Create(string collectionName, Column[] attributes)`: 向指定集合中添加属性，使用场景：扩展表字段。
  - `Create(TableIndex collectionIndex)`: 创建索引，使用场景：优化查询。
  - `Drop(Table collection)`: 删除集合，使用场景：移除表。
  - `Drop(string collectionName, Column[] attributes)`: 从指定集合中删除属性，使用场景：删除字段。
  - `Drop(TableIndex collectionIndex)`: 删除索引，使用场景：移除索引。
  - `GetTable(string name)`: 获取单个集合定义，使用场景：查询表结构。
  - `GetTables(string[] tableNames)`: 获取多个集合定义，使用场景：批量查询表。
  - `GetAllTables()`: 获取所有集合定义，使用场景：全面检查模式。
  - `ExistsTable(string name)`: 判断集合是否存在，使用场景：验证表存在性。
  - `ExistsColumn(string collectionName, string attributeName)`: 判断属性是否存在，使用场景：字段检查。
  - `ExistsIndex(string name)`: 判断索引是否存在，使用场景：索引检查。
  - `Alter(string oldName, Table newCollection)`: 修改表的定义，使用场景：更新表结构。
  - `RenameTable(string oldName, string newName)`: 重命名表，使用场景：重构数据库。
  - `Alter(string collectionName, Column newAttribute)`: 修改属性的定义，使用场景：更新字段。
  - `RenameColumn(string collectionName, string oldAttributeName, string newAttributeName)`: 重命名属性，使用场景：字段重命名。
  - `Alter(string oldIndexName, TableIndex newIndex)`: 修改索引的定义，使用场景：优化索引。

#### IDatabaseManager接口
IDatabaseManager 接口负责数据库级别的操作，使用场景：管理数据库实例。

- **属性：**
  - `ConnectionString` (string): 获取或设置数据库连接字符串，使用场景：配置连接。

- **方法：**
  - `CreateConnection()`: 创建一个连接实例，使用场景：获取 IConnection。
  - `Exists(string databaseName)`: 判断数据库是否存在，使用场景：检查数据库。
  - `Create(string databaseName, DatabaseOptions? options)`: 创建数据库，使用场景：新建数据库。
  - `Drop(string databaseName)`: 删除数据库，使用场景：移除数据库。
  - `Rename(string oldDatabaseName, string newDatabaseName)`: 重命名数据库，使用场景：重构环境。
  - `Use(string databaseName)`: 切换到指定的数据库上下文，使用场景：改变操作数据库。
  - `Alter(string databaseName, DatabaseOptions options)`: 修改数据库选项，使用场景：调整配置。

#### ISqlTransformer接口
ISqlTransformer 负责将通用的查询对象转换为特定数据库的 SQL 语句，使用场景：桥接抽象层和数据库。

- **方法：**
  - `Build(QueryExpression queryExpression)`: 将查询表达式转换为 SQL，使用场景：生成执行语句。

#### IUserManager接口
IUserManager 接口定义了用户和权限管理的核心操作，使用场景：在应用程序中管理数据库用户及其权限。

- **属性：**
  - `ConnectionString` (string): 获取或设置数据库连接字符串，使用场景：配置连接。

- **方法：**
  - `Create(string username, string password)`: 创建一个新用户，使用场景：注册新用户。
  - `Delete(string username)`: 删除指定用户，使用场景：移除用户账户。
  - `Disable(string username)`: 禁用指定用户，使用场景：暂时禁止用户登录。
  - `Enable(string username)`: 启用指定用户，使用场景：恢复用户账户的活跃状态。
  - `Exists(string username)`: 判断用户是否存在，使用场景：验证用户注册或登录。
  - `GrantPermissions(string username, DatabasePermission[] permissions)`: 授予用户特定权限，使用场景：分配用户对数据库操作的权限。
  - `RevokePermissions(string username, DatabasePermission[] permissions)`: 撤销用户特定权限，使用场景：回收用户权限。
  - `GetPermissions(string username)`: 获取用户的当前权限列表，使用场景：检查用户权限。

### 2.2 枚举详细说明

#### DataOperation枚举
表示操作类型，使用场景：定义 CRUD 操作。
| 成员 | 值 | 说明 |
|------|----|------|
| `Create` | `1` | 表示创建操作。 |
| `Update` | `2` | 表示更新操作。 |
| `Delete` | `4` | 表示删除操作。 |
| `Query` | `8` | 表示查询操作。 |


#### DataType枚举
表示数据类型，使用场景：确保跨数据库一致性。
| 成员 | 说明 |
|------|------|
| `String` | 可变长度字符串。 |
| `Int` | 32位整数。 |
| `BigInt` | 64位整数。 |
| `Float` | 单精度浮点数。 |
| `Double` | 双精度浮点数。 |
| `Decimal` | 精确数值。 |
| `Boolean` | 布尔值。 |
| `Date` | 仅日期。 |
| `DateTime` | 不带时区的日期时间。 |
| `DateTimeOffset` | 带时区的日期时间。 |
| `Text` | 大文本。 |
| `ByteArray` | 二进制数据。 |
| `Guid` | 全局唯一标识符。 |
| `Json` | JSON数据。 |
| `Xml` | XML数据。 |
| `Lookup` | 外键引用。 |
| `OptionSet` | 选项集。 |

| `RowVersion` | 行版本号，用于并发控制。 |

#### IndexType枚举
表示索引类型，使用场景：定义索引策略。
| 成员 | 说明 |
|------|------|
| `PRIMARYKEY` | 主键索引，确保唯一性。 |
| `UNIQUEINDEX` | 唯一索引，确保值唯一。 |
| `INDEX` | 普通索引，提高性能。 |

#### DatabasePermission枚举
表示数据库权限，使用场景：控制访问权限。
| 成员 | 值 | 说明 |
|------|----|------|
| `None` | `0` | 无权限。 |
| `Select` | `1 << 0` | 查询权限。 |
| `Insert` | `1 << 1` | 插入权限。 |
| `Update` | `1 << 2` | 更新权限。 |
| `Delete` | `1 << 3` | 删除权限。 |
| `Create` | `1 << 4` | 创建权限。 |
| `Drop` | `1 << 5` | 删除对象权限。 |
| `Alter` | `1 << 6` | 修改结构权限。 |
| `Owner` | `0` | 对象所有者权限，拥有全部控制权（所有权限的组合）。 |

#### ColumnProperty枚举
表示列的特定属性，使用场景：自定义字段行为。
| 成员 | 说明 |
|------|------|
| `Length` | 字符串长度。 |
| `Precision` | 数值精度。 |
| `Scale` | 数值标度。 |
| `MinValue` | 最小值约束。 |
| `MaxValue` | 最大值约束。 |
| `IsUnique` | 唯一性约束。 |
| `IsIndexed` | 是否索引。 |
| `IndexOrder` | 复合索引顺序。 |
| `ForeignKeyTable` | 外键目标表。 |
| `ForeignKeyColumn` | 外键目标列。 |
| `Description` | 字段描述。 |
| `CheckConstraints` | 检查约束。 |

#### Charset枚举
表示数据库的字符集，使用场景：处理国际化数据。
| 成员 | 说明 |
|------|------|
| `Default` | 默认字符集。 |
| `UTF8` | UTF-8 编码。 |
| `GBK` | GBK 编码。 |
| `EnglishUS` | 美式英语字符集。 |

#### Collation枚举
表示数据库的排序规则，使用场景：定义数据排序。
| 成员 | 说明 |
|------|------|
| `Default` | 默认排序规则。 |
| `EnglishUS` | 美式英语排序规则。 |
| `ChineseSimplified_Pinyin` | 简体中文按拼音排序。 |

#### JoinType枚举
表示JOIN类型，使用场景：定义查询中的连接类型。
| 成员 | 说明 |
|------|------|
| Inner | 内连接，仅返回匹配的记录。 |
| Left | 左外连接，返回所有左表记录和匹配的右表记录。 |
| Right | 右外连接，返回所有右表记录和匹配的左表记录。 |
| Full | 全外连接，返回所有两表记录。 |
| Cross | 交叉连接，返回笛卡尔积。 |

## 3. 快速开始

### 3.1 安装包引用
在您的项目中，通过 NuGet 安装 UDataset 相关的包：

```xml
<!-- 核心包（必需） -->
<PackageReference Include="UDataset.Core" Version="{版本号}" />

<!-- 根据需要选择数据库实现包（例如，同时使用SQL Server和PostgreSQL） -->
<PackageReference Include="UDataset.SQLServer" Version="{版本号}" />
<PackageReference Include="UDataset.PostgreSQL" Version="{版本号}" />
<PackageReference Include="UDataset.MySQL" Version="{版本号}" />
<PackageReference Include="UDataset.DuckDB" Version="{版本号}" />
<PackageReference Include="UDataset.Oracle" Version="{版本号}" />
```

### 3.2 注册数据库提供者
在应用程序启动时，需要注册您计划使用的数据库提供者。这是 UDataset 能够识别和使用特定数据库的关键步骤。

```csharp
using UDataset.Core;
using UDataset.SQLServer;
using UDataset.PostgreSQL;
using UDataset.MySQL;
using UDataset.DuckDB;
using UDataset.Oracle;

// 注册SQL Server提供者
SqlServerBootstrapper.Register();

// 注册PostgreSQL提供者
PostgreSQLBootstrapper.Register();

// 注册MySQL提供者
MySQLBootstrapper.Register();

// 注册DuckDB提供者
DuckDbBootstrapper.Register();

// 注册Oracle提供者
OracleBootstrapper.Register();

// 提示：建议在应用程序启动时（如ASP.NET Core的Startup.ConfigureServices方法中）一次性注册所有需要的提供者。
```

### 3.3 创建数据库连接
使用已注册的提供者名称字符串和数据库连接字符串来创建 `IConnection` 实例。`IConnection` 是执行数据操作的主要接口。

```csharp
using UDataset.Core;

// 假设 connString 已经定义
string sqlServerConnString = "Data Source=.;Initial Catalog=MyDb;Integrated Security=True;";
string postgresConnString = "Host=localhost;Port=5432;Database=MyDb;Username=user;Password=password;";
string mysqlConnString = "Server=localhost;Port=3306;Database=MyDb;Uid=user;Pwd=password;";
string duckdbConnString = "Data Source=MyDb.duckdb";
string oracleConnString = "Data Source=localhost:1521/XEPDB1;User Id=user;Password=password;";

// 创建SQL Server连接
IConnection sqlServerDb = ProviderFactory.CreateConnection("SqlServer", sqlServerConnString);

// 创建PostgreSQL连接
IConnection postgresDb = ProviderFactory.CreateConnection("PostgreSQL", postgresConnString);

// 创建MySQL连接
IConnection mysqlDb = ProviderFactory.CreateConnection("MySQL", mysqlConnString);

// 创建DuckDB连接
IConnection duckdbDb = ProviderFactory.CreateConnection("DuckDB", duckdbConnString);

// 创建Oracle连接
IConnection oracleDb = ProviderFactory.CreateConnection("Oracle", oracleConnString);
```

### 3.4 DuckDB 特别说明
DuckDB 是“嵌入式 / 文件型数据库”，与 SQL Server / PostgreSQL / MySQL 等服务器型数据库的使用方式有明显差异。

- **[数据库即文件]**
  - DuckDB 的“数据库”通常是一个本地文件（常见扩展名：`.duckdb`、`.db`）。
  - UDataset 当前 `DuckDB` 实现中，`IDatabaseManager.Create(databaseName)` 会创建对应数据库文件；当 `databaseName` 不是绝对路径时，会落到**当前工作目录**，并自动追加 `.duckdb` 后缀。

- **[内存数据库 :memory:]**
  - DuckDB 支持内存数据库模式，进程结束后数据不落盘。
  - 连接字符串示例：`Data Source=:memory:`

- **[并发模型与多进程写入限制]**
  - DuckDB 官方文档的推荐模型是：**单进程**内可读写并支持多线程并发（写入冲突时会报冲突错误）。
  - **多进程**同时写入同一数据库文件不是默认支持的模式；如果确实需要多进程写入，通常需要在应用层实现跨进程互斥/重试等策略，或改用服务器型数据库存储。

- **[ATTACH 多数据库文件]**
  - DuckDB 支持 `ATTACH` 将多个数据库文件挂载到同一连接中。
  - 但在事务语义上，单个事务只允许写入一个已挂载的数据库文件（写多个会报错）。

## 4. 数据操作 (CRUD)

UDataset 提供了一套通用的 CRUD (创建、读取、更新、删除) 操作接口，通过 `IConnection` 实例进行。

### 4.1 创建记录 (Create)
使用 `Create` 方法向集合（表）中插入单条或多条记录。

```csharp
// 注册提供者并创建连接 (示例使用PostgreSQL)
PostgreSQLBootstrapper.Register();
var connection = ProviderFactory.CreateConnection("PostgreSQL", connectionString);

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

// 创建多条记录 (批量插入)
var dataRows = new List<Row>
{
    new Row("Users") { ["Name"] = "User 1", ["Age"] = 20 },
    new Row("Users") { ["Name"] = "User 2", ["Age"] = 30 }
};

var insertedRows = await connection.Create(dataRows);
foreach (var row in insertedRows)
{
    Console.WriteLine($"批量插入记录 ID: {row.Id}, 版本: {row.Version}");
}
```

### 4.2 读取记录 (Retrieve)
使用 `Retrieve` 方法根据主键读取单条或多条记录。

```csharp
// 假设 createdResult 是之前创建的单条记录
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

// 假设 insertedRows 是之前批量插入的记录列表
var recordIds = insertedRows.Select(r => r.Id).ToList();
var retrievedResults = await connection.Retrieve("Users", recordIds);

Console.WriteLine("批量检索到的记录:");
foreach (var row in retrievedResults)
{
    Console.WriteLine($"- Name: {row["Name"]}, Age: {row["Age"]}");
}
```

### 4.3 更新记录 (Update)
使用 `Update` 方法更新单条记录。通常需要提供记录的 ID 和当前版本号以支持乐观锁。

> **⚠️ 重要警告：避免全量更新**
> 
> 请**不要**直接使用 `Retrieve` 或 `Query` 查询到的完整数据对象进行更新。这样做会导致提交所有查询到的字段值，可能覆盖其他并发修改，或导致不必要的性能开销。
> 
> **错误做法：**
> ```csharp
> var targetRow = await db.Retrieve("table", id);  
> targetRow["name"] = "Tom";
> await db.Update(targetRow); // 错误！这将提交所有字段
> ```
> 
> **正确做法：**
> 应创建一个**新的 Row 对象**，仅包含 `Id`（必需）、`Version`（可选，用于乐观锁）以及**真正需要修改的字段**。建议使用 `CreateUpdateRow()` 方法简化操作。

```csharp
// 1. 准备要更新的数据 (仅包含ID和需要修改的字段)
// 使用 CreateUpdateRow() 方法从现有记录创建更新对象，它会自动复制 Schema, Id 和 Version
var updateTarget = createdResult.CreateUpdateRow();

// 或者手动创建：
// var updateTarget = new Row("Users", createdResult.Id); 
// updateTarget.Version = createdResult.Version; 

// 2. 设置需要修改的字段
updateTarget["Name"] = "Updated Name"; 
updateTarget["Age"] = 26;

// 3. 执行更新
// UDataset 只会生成包含 Name 和 Age 的 UPDATE 语句
var updatedResult = await connection.Update(updateTarget);
Console.WriteLine($"记录 {updatedResult.Id} 已更新，新版本: {updatedResult.Version}");
```

### 4.4 删除记录 (Delete)
使用 `Delete` 方法删除单条或多条记录。

```csharp
// 删除单条记录
bool deleteSuccess = await connection.Delete(createdResult); // 可以直接传递 Row 对象
Console.WriteLine($"ID 为 {createdResult.Id} 的记录删除成功: {deleteSuccess}");

// 或者根据 ID 删除
// bool deleteSuccessById = await connection.Delete("Users", someOtherRecordId);

// 删除多条记录
var recordsToDelete = insertedRows; // 假设 insertedRows 是之前查询到的记录列表
int bulkDeleteCount = await connection.Delete(recordsToDelete);
Console.WriteLine($"批量删除记录数量: {bulkDeleteCount}");
```

### 4.5 用户管理 (User Management)
UDataset 提供统一的用户和权限管理接口。

```csharp
// 注册提供者并创建用户管理器 (示例使用MySQL)
MySQLBootstrapper.Register();
var userManager = ProviderFactory.CreateUserManager("MySQL", connectionString);

// 创建用户
var username = "test_user";
var password = "test_password";
var created = userManager.Create(username, password);
Console.WriteLine($"用户 '{username}' 创建成功: {created}");

// 检查用户是否存在
var exists = userManager.Exists(username);
Console.WriteLine($"用户 '{username}' 是否存在: {exists}");

// 授予权限
userManager.GrantPermissions(username, new[] { DatabasePermission.Select, DatabasePermission.Insert });
Console.WriteLine($"已授予用户 '{username}' SELECT, INSERT 权限");

// 查询用户权限
var permissions = userManager.GetPermissions(username);
Console.WriteLine($"用户 '{username}' 的权限: {string.Join(", ", permissions)}");

// 禁用用户
userManager.Disable(username);
Console.WriteLine($"用户 '{username}' 已禁用");

// 启用用户
userManager.Enable(username);
Console.WriteLine($"用户 '{username}' 已启用");

// 撤销权限
userManager.RevokePermissions(username, new[] { DatabasePermission.Insert });
Console.WriteLine($"已撤销用户 '{username}' INSERT 权限");

// 删除用户
userManager.Delete(username);
Console.WriteLine($"用户 '{username}' 已删除");
```

## 5. 高级特性与用法

### 5.1 事务处理
UDataset 支持数据库事务，确保一系列操作的原子性，即所有操作要么全部成功，要么全部失败。

```csharp
connection.BeginTransaction(); // 开始事务
try
{
    // 在事务中执行多个操作，例如创建、更新、删除等
    var dataRow1 = new Row("Users") { ["Name"] = "User A" };
    await connection.Create(dataRow1);

    var dataRow2 = new Row("Users") { ["Name"] = "User B" };
    await connection.Create(dataRow2);

    // 假设这里发生了一个错误，将导致事务回滚
    // throw new Exception("模拟事务失败");

    // 如果所有操作都成功，则提交事务
    connection.CommitTransaction();
    Console.WriteLine("事务已成功提交。");
}
catch (Exception ex)
{
    // 如果任何操作失败，则回滚事务
    connection.RollbackTransaction();

    Console.WriteLine($"事务失败，已回滚: {ex.Message}");
    throw; // 可选：重新抛出异常以便上层处理
}
```

### 5.2 数据类型边界值和NULL值处理
在实际测试中，UDataset 确保了数据类型边界值和 NULL 值在跨数据库之间的一致性处理。

```csharp
var row = new Row("Users")
{
    ["StringField"] = string.Empty,  // 边界值测试：空字符串
    ["IntField"] = int.MinValue,     // 边界值测试：整数最小值
    ["BooleanField"] = null,         // NULL值测试
    ["DecimalField"] = 123456789.012345m, // 大精度Decimal
    ["GuidField"] = Guid.NewGuid(),  // GUID类型
    ["DateField"] = DateTime.UtcNow.Date // 仅日期
};
await connection.Create(row);
Console.WriteLine("已创建包含边界值和NULL值的记录。");
```

### 5.3 批量操作性能建议
对于大批量数据操作，UDataset 提供了优化方案，建议使用批量方法而非循环单条操作，并注意事务和分批处理。

```csharp
// 注册提供者并创建连接 (示例使用SqlServer)
SqlServerBootstrapper.Register();
var bulkConnection = ProviderFactory.CreateConnection("SqlServer", connectionString);

// 准备大批量数据
var dataRowsForBulk = new List<Row>();
for (int i = 0; i < 1000; i++)
{
    dataRowsForBulk.Add(new Row("user")
    {
        ["Name"] = $"BulkUser_{i}",
        ["Age"] = i % 100,
        ["IsActive"] = i % 2 == 0
    });
}

// 使用批量插入进行性能测试
var stopwatch = Stopwatch.StartNew();
var insertedRowsBulk = await bulkConnection.Create(dataRowsForBulk);
stopwatch.Stop();

Console.WriteLine($"插入1000条记录耗时: {stopwatch.ElapsedMilliseconds}ms");
Console.WriteLine($"吞吐量: {(double)1000 / stopwatch.ElapsedMilliseconds * 1000:F2} 记录/秒");

// 性能建议：
// 1. 批量操作建议在事务中执行，以确保数据一致性并提高性能。
// 2. 单次批量操作建议控制在5000条以内，避免单次操作过大导致内存或网络压力。
// 3. 对于超大批量操作（例如百万条级别），考虑分批次执行，每次处理少量数据，并通过多线程或异步方式优化。
```

### 5.4 自定义主键和行版本控制
UDataset 支持自定义主键字段和行版本字段，这对于高级审计和乐观锁控制非常有用。

```csharp
// 创建架构管理器 (示例使用PostgreSQL)
PostgreSQLBootstrapper.Register();
var schemaManager = ProviderFactory.CreateSchemaManager("PostgreSQL", connectionString);

// 创建表时指定自定义主键和行版本字段
var customTable = new Table("CustomUsers", "MyCustomId", "RowVersionColumn");

var customIdAttr = new Column("MyCustomId", DataType.Int, true) { IsPrimaryKey = true };
customIdAttr.Properties[ColumnProperty.IsUnique] = true; // 明确标记为主键
customTable.Attributes.Add(customIdAttr);

var nameAttr = new Column("UserName", DataType.String, true, "DefaultName");
customTable.Attributes.Add(nameAttr);

var versionAttr = new Column("RowVersionColumn", DataType.RowVersion); // 指定行版本类型
customTable.Attributes.Add(versionAttr);

schemaManager.Create(customTable);
Console.WriteLine($"已创建自定义主键 '{customTable.PrimaryKey}' 和行版本 '{customTable.VersionAttribute}' 的表 '{customTable.Name}'。");

// 验证创建结果
var retrievedTable = schemaManager.GetTable("CustomUsers");
Console.WriteLine($"检索到的表的主键: {retrievedTable.PrimaryKey}");
Console.WriteLine($"检索到的表的版本字段: {retrievedTable.VersionAttribute}");
```

### 5.5 查询表达式 (QueryExpression) 深入
`QueryExpression` 是定义数据查询的核心类，它提供了灵活的方式来构建各种复杂的查询，包括选择字段、过滤条件、联接、分组、排序和分页。

```csharp
// 创建 QueryExpression 实例
// 第一个参数是集合名称，第二个参数是可选的别名
var query = new QueryExpression("Users", "u");

// 1. 选择字段 (Select)
// 主表字段：通过 QueryExpression 实例添加
query.AddSelect("u.Name"); // 选择主表 Users 的 Name 字段
query.AddSelect("u.Age", "userAge"); // 选择主表 Users 的 Age 字段并指定别名为 userAge
query.AddSelect("COUNT(*)", "userCount"); // 选择聚合函数并指定别名
query.AddSelect("AVG(u.Age)", "averageAge"); // 选择带字段的聚合函数并指定别名

// 4. 连接 (Join) - 关联表字段通过 JoinClause 实例添加
query.Join = new List<JoinClause>
{
    new JoinClause("Groups", "g", "u.GroupId = g.Id") // 连接 Groups 表，别名为 g，连接条件为 u.GroupId = g.Id
    {
        Select = 
        {
            new SelectItem("g.Name", "GroupName"), // 选择关联表 Groups 的 Name 字段并指定别名
            new SelectItem("g.Id", "GroupId"),     // 选择关联表 Groups 的 Id 字段并指定别名
            new SelectItem("g.Type")               // 选择关联表 Groups 的 Type 字段
        }
    },

    // 可以添加多个 JoinClause 实现多表联接
    new JoinClause("Orders", "o", "u.Id = o.UserId")
    {
        Select = 
        {
            new SelectItem("o.Amount", "OrderAmount"), // 选择关联表 Orders 的 Amount 字段并指定别名
            new SelectItem("o.OrderDate")               // 选择关联表 Orders 的 OrderDate 字段
        }
    }
};

// 5. 分组 (GroupBy)
query.GroupBy = new List<string> { "u.IsActive" }; // 按 IsActive 字段分组
query.GroupBy = new List<string> { "u.Age", "u.IsActive" }; // 多个分组字段

// 6. 排序 (OrderBy)
query.AddOrderBy("u.Name", SortDirection.Ascending); // 按 Name 字段升序排序 (ASC)
query.AddOrderBy("u.Age", SortDirection.Descending); // 按 Age 字段降序排序 (DESC)
query.AddOrderBy("u.CreatedAt DESC"); // 从字符串添加排序表达式

// 2. 过滤条件 (Filter)
query.Filter = "u.Age > @minAge AND u.IsActive = @isActive"; // 简单过滤条件
query.Filter = "(u.Age > @minAge AND u.IsActive = @isActive) OR u.Name LIKE @namePattern"; // 复杂过滤条件
query.Filter = "u.Age BETWEEN @minAge AND @maxAge"; // 使用 BETWEEN 运算符

// 3. 查询参数 (Parameters)
query.Parameters = new Dictionary<string, object>
{
    ["minAge"] = 18,
    ["isActive"] = true,
    ["namePattern"] = "%Test%",
    ["maxAge"] = 30
};

// 7. 排序 (OrderBy)
query.AddOrderBy("u.Name", SortDirection.Ascending); // 按 Name 字段升序排序 (ASC)
query.AddOrderBy("u.Age", SortDirection.Descending); // 按 Age 字段降序排序 (DESC)

// 8. 分页 (Top 和 Skip)
query.Top = 10; // 限制返回的记录数量为 10
query.Skip = 20; // 跳过前 20 条记录
// 这将实现 LIMIT 10 OFFSET 20 类似的分页效果

// 9. 执行查询
// 假设 connection 已创建
IEnumerable<IRow> results = await connection.Query(query);
Console.WriteLine($"查询到 {results.Count()} 条记录。");

// 执行查询并返回单条记录 (如果结果多于一条则可能抛出异常，没有结果则返回 null)
IRow? singleResult = await connection.QuerySingle(query);
if (singleResult != null)
{
    Console.WriteLine($"查询到单条记录: {singleResult["Name"]}");
}
```

### 5.6 元数据操作示例 (ISchemaManager)
以下示例演示如何使用 `ISchemaManager` 进行集合（表）、字段、索引的标准化元数据操作。

```csharp
// 注册提供者并创建架构管理器 (示例使用PostgreSQL)
PostgreSQLBootstrapper.Register();
var schemaManagerForMetadata = ProviderFactory.CreateSchemaManager("PostgreSQL", connectionString);

// 1. 创建集合（表）
var userTableDef = new Table("UsersForMetadata");
userTableDef.Attributes.Add(new Column("Id", DataType.Int, true) { IsPrimaryKey = true });
userTableDef.Attributes.Add(new Column("Name", "string(100)", true));
userTableDef.Attributes.Add(new Column("Email", "string(255,unique=true)"));
userTableDef.Attributes.Add(new Column("CreatedAt", DataType.DateTime));
schemaManagerForMetadata.Create(userTableDef);
Console.WriteLine($"已创建表: {userTableDef.Name}");

// 2. 判断集合、字段、索引是否存在
bool existsTable = schemaManagerForMetadata.ExistsTable("UsersForMetadata");
Console.WriteLine($"表 'UsersForMetadata' 是否存在: {existsTable}");

bool existsColumn = schemaManagerForMetadata.ExistsColumn("UsersForMetadata", "Email");
Console.WriteLine($"'UsersForMetadata' 表中的 'Email' 字段是否存在: {existsColumn}");

// 创建一个索引以便检查
var nameIndex = new TableIndex("IX_UsersForMetadata_Name", IndexType.INDEX, "UsersForMetadata", new List<string> { "Name" });
schemaManagerForMetadata.Create(nameIndex);
bool existsIndex = schemaManagerForMetadata.ExistsIndex("IX_UsersForMetadata_Name");
Console.WriteLine($"索引 'IX_UsersForMetadata_Name' 是否存在: {existsIndex}");

// 3. 获取集合结构
Table retrievedUserTable = schemaManagerForMetadata.GetTable("UsersForMetadata");
Console.WriteLine($"获取到表 '{retrievedUserTable.Name}'，包含 {retrievedUserTable.Attributes.Count} 个字段。");

// 4. 添加字段
var newAttr = new Column("Description", "string(500)");
schemaManagerForMetadata.Create("UsersForMetadata", new[] { newAttr });
Console.WriteLine($"已向 'UsersForMetadata' 表添加 'Description' 字段。");

// 5. 删除字段
var attrToRemove = new Column("Description", DataType.String); // 仅需名称和类型
schemaManagerForMetadata.Drop("UsersForMetadata", new[] { attrToRemove });
Console.WriteLine($"已从 'UsersForMetadata' 表删除 'Description' 字段。");

// 6. 修改字段
var modifiedAttr = new Column("UsersForMetadata", "Name", "string(200,description='用户全名')");
schemaManagerForMetadata.Alter("UsersForMetadata", modifiedAttr);
Console.WriteLine($"已修改 'UsersForMetadata' 表中 'Name' 字段的长度和描述。");

// 7. 字段重命名
string oldColumnName = "Email";
string newColumnName = "UserEmail";
schemaManagerForMetadata.RenameColumn("UsersForMetadata", oldColumnName, newColumnName);
Console.WriteLine($"已将 'UsersForMetadata' 表中的字段 '{oldColumnName}' 重命名为 '{newColumnName}'。");

// 8. 创建索引 (已在前面示例中创建)
// var anotherIndex = new TableIndex("IX_UsersForMetadata_Email", IndexType.UNIQUEINDEX, "UsersForMetadata", new List<string> { "UserEmail" });
// schemaManagerForMetadata.Create(anotherIndex);

// 9. 删除索引
var indexToDelete = new TableIndex("IX_UsersForMetadata_Name", IndexType.INDEX, "UsersForMetadata", new List<string> { "Name" });
schemaManagerForMetadata.Drop(indexToDelete);
Console.WriteLine($"已删除索引 '{indexToDelete.Name}'。");

// 10. 表（集合）重命名
string oldTableName = "UsersForMetadata";
string newTableName = "ApplicationUsers";
schemaManagerForMetadata.RenameTable(oldTableName, newTableName);
Console.WriteLine($"已将表 '{oldTableName}' 重命名为 '{newTableName}'。");

// 11. 删除集合
var tableToDelete = new Table("ApplicationUsers");
schemaManagerForMetadata.Drop(tableToDelete);
Console.WriteLine($"已删除表 '{tableToDelete.Name}'。");
```

### 5.7 数据库管理操作示例 (IDatabaseManager)
以下示例演示如何使用 `IDatabaseManager` 进行数据库级别的操作。

```csharp
// 注册提供者并创建数据库管理器 (示例使用MySQL)
MySQLBootstrapper.Register();
var databaseManager = ProviderFactory.CreateDatabaseManager("MySQL", connectionString);

// 1. 创建数据库
string databaseName = "TestDatabase";
var options = new DatabaseOptions
{
    MaxSizeMB = 1024,
    InitialSizeMB = 100,
    MaxConnections = 200,
    Collation = Collation.EnglishUS // 假设 Collation 枚举已定义
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

// 5. 删除数据库
databaseManager.Drop(newDatabaseName);
Console.WriteLine($"已删除数据库: {newDatabaseName}");
```

### 5.8 用户管理操作示例 (IUserManager)
以下示例演示如何使用 `IUserManager` 进行用户创建、权限授予、禁用/启用以及删除等操作。

```csharp
// 注册提供者并创建用户管理器 (示例使用MySQL)
MySQLBootstrapper.Register();
var userManager = ProviderFactory.CreateUserManager("MySQL", connectionString);

// 1. 创建用户
string testUsername = "new_test_user";
string testPassword = "SecurePassword123!";
bool userCreated = userManager.Create(testUsername, testPassword);
Console.WriteLine($"用户 '{testUsername}' 创建成功: {userCreated}");

// 2. 检查用户是否存在
bool userExists = userManager.Exists(testUsername);
Console.WriteLine($"用户 '{testUsername}' 是否存在: {userExists}");

// 3. 授予权限
var permissionsToGrant = new[] { DatabasePermission.Select, DatabasePermission.Insert, DatabasePermission.Update };
userManager.GrantPermissions(testUsername, permissionsToGrant);
Console.WriteLine($"已授予用户 '{testUsername}' SELECT, INSERT, UPDATE 权限。");

// 4. 获取用户权限
var userPermissions = userManager.GetPermissions(testUsername);
Console.WriteLine($"用户 '{testUsername}' 的权限: {string.Join(", ", userPermissions)}");

// 5. 禁用用户
userManager.Disable(testUsername);
Console.WriteLine($"用户 '{testUsername}' 已禁用。");

// 6. 启用用户
userManager.Enable(testUsername);
Console.WriteLine($"用户 '{testUsername}' 已启用。");

// 7. 撤销权限
var permissionsToRevoke = new[] { DatabasePermission.Insert };
userManager.RevokePermissions(testUsername, permissionsToRevoke);
Console.WriteLine($"已撤销用户 '{testUsername}' INSERT 权限。");

// 8. 删除用户
userManager.Delete(testUsername);
Console.WriteLine($"已删除用户: {testUsername}");
```

## 6. 跨数据库兼容性

UDataset 旨在提供跨数据库的统一抽象层，同时在可能的情况下利用特定数据库的高级功能。

### 6.1 数据库特定功能支持
不同数据库产品在功能和性能上可能存在差异，UDataset 会在内部进行适配和优化。

- **SQL Server**
  - 支持 `SqlBulkCopy` 进行高性能批量插入。
  - 支持 `ROWVERSION` 数据类型用于乐观锁。
  - 支持完整的数据库重命名操作。

- **PostgreSQL**
  - 支持 `COPY` 命令进行高性能批量插入。
  - 支持数据库重命名操作。
  - 支持丰富的数据类型和扩展，如 JSONB。

- **MySQL**
  - 支持批量插入优化。
  - 支持 `TIMESTAMP` 用于行版本控制。
  - **注意**：MySQL 不支持数据库重命名操作。

### 6.2 性能对比
根据内部测试结果，不同数据库在批量操作上的性能表现可能有所不同：

| 数据库       | 操作   | 5000条记录耗时 (ms) | 性能损失 (%) | 吞吐量 (TPS/QPS) |
|--------------|--------|---------------------|--------------|------------------|
| SQL Server   | Create | 23681               | -2.40        | 211.14           |
| SQL Server   | Read   | 7477                | 0.31         | 668.72           |
| SQL Server   | Update | 24344               | 13.98        | 205.39           |
| PostgreSQL   | Create | 9863                | 11.95        | 506.95           |
| PostgreSQL   | Read   | 3582                | 3.35         | 1395.87          |
| PostgreSQL   | Update | 9918                | 8.39         | 504.13           |
| MySQL        | Create | 32606               | 47.15        | 153.35           |
| MySQL        | Read   | 4572                | 4.38         | 1093.61          |
| MySQL        | Update | 27534               | 36.02        | 181.59           |

注意：以上测试在开发者的笔记本电脑及Docker容器中运行，QPS、TPS仅作为参考。

## 7. 最佳实践

### 7.1 提供者注册
建议在应用程序启动时一次性注册所有需要的提供者，以确保 `ProviderFactory` 能够正确地创建数据库组件。

```csharp
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // 注册数据库提供者
        SqlServerBootstrapper.Register();
        PostgreSQLBootstrapper.Register();
        MySQLBootstrapper.Register();
        DuckDbBootstrapper.Register();
        OracleBootstrapper.Register();
 
        // 其他服务注册...
    }
}
```

### 7.2 连接管理
建议使用依赖注入 (Dependency Injection) 来管理数据库连接，确保连接的生命周期被正确管理，避免资源泄露。

```csharp
services.AddScoped<IConnection>(provider =>
{
    var configuration = provider.GetService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("DefaultConnection");
    var providerName = configuration["DatabaseProvider"]; // 从配置中获取提供者名称

    return ProviderFactory.CreateConnection(providerName, connectionString);
});
```

### 7.3 错误处理
实现统一的错误处理机制，捕获 UDataset 定义的特定异常，以便进行日志记录、用户友好提示和恢复操作。

```csharp
try
{
    await connection.Create(dataRow);
}
catch (ConcurrencyException ex)
{
    // 处理乐观锁冲突：例如，提示用户数据已被其他操作修改，建议刷新或重试。
    logger.LogWarning("并发冲突: {Message}", ex.Message);
    // throw new UserFriendlyException("数据已被修改，请刷新后重试。");
}
catch (DataValidationException ex) // 假设有DataValidationException
{
    // 处理数据验证错误：例如，提示用户输入的数据不符合业务规则。
    logger.LogError(ex, "数据验证失败: {Message}", ex.Message);
    // throw new UserFriendlyException($"数据输入不合法: {ex.Message}");
}
catch (DatasetException ex)
{
    // 处理UDataset特定异常：捕获所有Dataset相关的内部错误。
    logger.LogError(ex, "数据操作失败: {Message}", ex.Message);
    throw;
}
catch (Exception ex)
{
    // 处理其他未预料的系统级异常。
    logger.LogCritical(ex, "发生未处理的系统错误。");
    throw;
}
```

### 7.4 性能优化
- 对于大批量操作，始终优先使用 UDataset 提供的批量方法（如 `Create(IEnumerable<IRow> dataRows)`），而不是循环单条操作。
- 合理使用事务：将一组相关的数据库操作包裹在事务中，以确保原子性和数据一致性。避免长时间锁定数据库资源。
- 定期检查和优化索引：根据查询模式分析并创建合适的索引，提升查询性能。

### 7.5 测试建议
- 为每个支持的数据库编写集成测试：确保 UDataset 在不同数据库环境下的行为一致性和正确性。
- 使用独立的测试数据库：避免测试数据污染开发或生产环境。
- 测试并发场景和事务回滚：验证系统在高并发和异常情况下的鲁棒性。

### 7.6 测试最佳实践
基于测试代码，建议在集成测试中验证数据一致性，例如使用 `AssertDataConsistency` 方法来比较操作前后的数据状态。

```csharp
// 示例：验证数据一致性
// AssertDataConsistency(originalRow, queriedRow, "数据库类型");
// 此方法应在您的测试框架中实现，用于比较预期数据和实际从数据库中检索到的数据。
```
