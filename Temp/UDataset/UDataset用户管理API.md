# UDataset 用户管理API

## 概述

UDataset用户管理API提供了统一的用户和权限管理接口，通过`IUserManager`接口实现跨数据库的用户账户管理和权限控制功能。

## 核心接口

### IUserManager接口

IUserManager 接口定义了用户和权限管理的核心操作，使用场景：在应用程序中管理数据库用户及其权限。

#### 属性

- **`ConnectionString` (string)**: 获取或设置数据库连接字符串，使用场景：配置连接。

#### 方法

- **`Create(string username, string password)`**: 创建一个新用户，使用场景：注册新用户。
- **`Delete(string username)`**: 删除指定用户，使用场景：移除用户账户。
- **`Disable(string username)`**: 禁用指定用户，使用场景：暂时禁止用户登录。
- **`Enable(string username)`**: 启用指定用户，使用场景：恢复用户账户的活跃状态。
- **`Exists(string username)`**: 判断用户是否存在，使用场景：验证用户注册或登录。
- **`GrantPermissions(string username, DatabasePermission[] permissions)`**: 授予用户特定权限，使用场景：分配用户对数据库操作的权限。
- **`RevokePermissions(string username, DatabasePermission[] permissions)`**: 撤销用户特定权限，使用场景：回收用户权限。
- **`GetPermissions(string username)`**: 获取用户的当前权限列表，使用场景：检查用户权限。

## 权限管理

### DatabasePermission枚举

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

### 权限组合使用

DatabasePermission枚举支持位标志操作，可以组合多个权限：

```csharp
// 组合多个权限
var permissions = DatabasePermission.Select | DatabasePermission.Insert | DatabasePermission.Update;

// 检查是否具有特定权限
bool hasSelectPermission = permissions.HasFlag(DatabasePermission.Select);

// 基本CRUD权限组合
var crudPermissions = new[] 
{
    DatabasePermission.Select,
    DatabasePermission.Insert,
    DatabasePermission.Update,
    DatabasePermission.Delete
};
```

## 使用示例

### 基本用户管理

```csharp
// 注册提供者并创建用户管理器
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

### 完整用户生命周期管理

```csharp
public async Task ManageUserLifecycle(string username, string password)
{
    var userManager = ProviderFactory.CreateUserManager("PostgreSQL", connectionString);
    
    try
    {
        // 1. 创建用户
        var created = userManager.Create(username, password);
        if (!created)
        {
            Console.WriteLine($"用户 {username} 已存在");
            return;
        }
        
        // 2. 验证用户存在
        Assert.IsTrue(userManager.Exists(username), "用户应该存在");
        
        // 3. 授予基本权限
        var basicPermissions = new[] 
        {
            DatabasePermission.Select,
            DatabasePermission.Insert,
            DatabasePermission.Update
        };
        userManager.GrantPermissions(username, basicPermissions);
        
        // 4. 验证权限
        var userPermissions = userManager.GetPermissions(username);
        foreach (var permission in basicPermissions)
        {
            Assert.IsTrue(
                userPermissions.Any(p => p.HasFlag(permission)), 
                $"用户应该具有 {permission} 权限"
            );
        }
        
        // 5. 禁用用户（模拟暂停账户）
        userManager.Disable(username);
        Assert.IsTrue(userManager.Exists(username), "禁用后用户应该仍然存在");
        
        // 6. 重新启用用户
        userManager.Enable(username);
        
        // 7. 撤销部分权限
        userManager.RevokePermissions(username, new[] { DatabasePermission.Update });
        
        var remainingPermissions = userManager.GetPermissions(username);
        Assert.IsFalse(
            remainingPermissions.Any(p => p.HasFlag(DatabasePermission.Update)),
            "Update权限应该被撤销"
        );
        
    }
    finally
    {
        // 8. 清理：删除用户
        if (userManager.Exists(username))
        {
            userManager.Delete(username);
        }
    }
}
```

### 角色权限管理

```csharp
public class UserRoleManager
{
    private readonly IUserManager _userManager;
    
    public UserRoleManager(IUserManager userManager)
    {
        _userManager = userManager;
    }
    
    /// <summary>
    /// 为用户分配只读角色
    /// </summary>
    public void AssignReadOnlyRole(string username)
    {
        var readOnlyPermissions = new[] { DatabasePermission.Select };
        _userManager.GrantPermissions(username, readOnlyPermissions);
    }
    
    /// <summary>
    /// 为用户分配数据操作员角色
    /// </summary>
    public void AssignDataOperatorRole(string username)
    {
        var operatorPermissions = new[] 
        {
            DatabasePermission.Select,
            DatabasePermission.Insert,
            DatabasePermission.Update,
            DatabasePermission.Delete
        };
        _userManager.GrantPermissions(username, operatorPermissions);
    }
    
    /// <summary>
    /// 为用户分配管理员角色
    /// </summary>
    public void AssignAdminRole(string username)
    {
        var adminPermissions = new[] 
        {
            DatabasePermission.Select,
            DatabasePermission.Insert,
            DatabasePermission.Update,
            DatabasePermission.Delete,
            DatabasePermission.Create,
            DatabasePermission.Drop,
            DatabasePermission.Alter
        };
        _userManager.GrantPermissions(username, adminPermissions);
    }
    
    /// <summary>
    /// 检查用户是否具有特定角色权限
    /// </summary>
    public bool HasRole(string username, string role)
    {
        var userPermissions = _userManager.GetPermissions(username);
        var combinedPermissions = userPermissions.Aggregate(
            DatabasePermission.None, 
            (current, permission) => current | permission
        );
        
        return role.ToLower() switch
        {
            "readonly" => combinedPermissions.HasFlag(DatabasePermission.Select),
            "operator" => combinedPermissions.HasFlag(DatabasePermission.Select | 
                                                     DatabasePermission.Insert | 
                                                     DatabasePermission.Update | 
                                                     DatabasePermission.Delete),
            "admin" => combinedPermissions.HasFlag(DatabasePermission.Owner),
            _ => false
        };
    }
}
```

## 跨数据库兼容性

### 数据库特定实现

不同数据库系统的用户管理实现存在差异：

#### SQL Server
- 支持登录（Login）和用户（User）的分离管理
- 使用数据库角色（如 db_owner）管理权限
- 支持 ENABLE/DISABLE 登录状态控制

```csharp
// SQL Server 特定功能示例
var sqlServerUserManager = ProviderFactory.CreateUserManager("SqlServer", sqlServerConnectionString);

// 创建用户会同时创建登录和数据库用户
sqlServerUserManager.Create("testuser", "password123");

// 授予Owner权限会将用户加入db_owner角色
sqlServerUserManager.GrantPermissions("testuser", new[] { DatabasePermission.Owner });
```

#### PostgreSQL
- 使用角色（Role）统一管理用户和权限
- 支持 LOGIN/NOLOGIN 属性控制登录能力
- 权限授予到模式（Schema）级别

```csharp
// PostgreSQL 特定功能示例
var postgresUserManager = ProviderFactory.CreateUserManager("PostgreSQL", postgresConnectionString);

// 创建用户会创建具有LOGIN属性的角色
postgresUserManager.Create("testuser", "password123");

// 禁用用户会设置NOLOGIN属性
postgresUserManager.Disable("testuser");
```

#### MySQL
- 使用用户@主机的格式管理用户
- 支持 ACCOUNT LOCK/UNLOCK 控制账户状态
- 权限授予到数据库级别

```csharp
// MySQL 特定功能示例
var mysqlUserManager = ProviderFactory.CreateUserManager("MySQL", mysqlConnectionString);

// 创建的用户格式为 'username'@'%'
mysqlUserManager.Create("testuser", "password123");

// 禁用用户会锁定账户
mysqlUserManager.Disable("testuser");
```

## 安全最佳实践

### 1. 密码策略

```csharp
public class PasswordPolicy
{
    public static bool ValidatePassword(string password)
    {
        // 最低8位，包含大小写字母、数字和特殊字符
        if (password.Length < 8)
            return false;
            
        bool hasUpper = password.Any(char.IsUpper);
        bool hasLower = password.Any(char.IsLower);
        bool hasDigit = password.Any(char.IsDigit);
        bool hasSpecial = password.Any(c => !char.IsLetterOrDigit(c));
        
        return hasUpper && hasLower && hasDigit && hasSpecial;
    }
    
    public static string GenerateSecurePassword(int length = 12)
    {
        const string validChars = "ABCDEFGHJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*";
        var random = new Random();
        return new string(Enumerable.Repeat(validChars, length)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }
}

// 使用示例
string securePassword = PasswordPolicy.GenerateSecurePassword();
if (PasswordPolicy.ValidatePassword(securePassword))
{
    userManager.Create("newuser", securePassword);
}
```

### 2. 最小权限原则

```csharp
public void GrantMinimalPermissions(string username, string userRole)
{
    // 根据用户角色授予最小必要权限
    var permissions = userRole switch
    {
        "Reporter" => new[] { DatabasePermission.Select },
        "DataEntry" => new[] { DatabasePermission.Select, DatabasePermission.Insert },
        "DataAnalyst" => new[] { 
            DatabasePermission.Select, 
            DatabasePermission.Insert, 
            DatabasePermission.Update 
        },
        "Administrator" => new[] { 
            DatabasePermission.Select, 
            DatabasePermission.Insert, 
            DatabasePermission.Update, 
            DatabasePermission.Delete 
        },
        _ => new[] { DatabasePermission.None }
    };
    
    if (permissions.Any(p => p != DatabasePermission.None))
    {
        userManager.GrantPermissions(username, permissions);
    }
}
```

### 3. 定期权限审计

```csharp
public class PermissionAuditor
{
    private readonly IUserManager _userManager;
    
    public PermissionAuditor(IUserManager userManager)
    {
        _userManager = userManager;
    }
    
    public void AuditUserPermissions(string[] usernames)
    {
        var auditReport = new List<UserPermissionReport>();
        
        foreach (var username in usernames)
        {
            if (_userManager.Exists(username))
            {
                var permissions = _userManager.GetPermissions(username);
                auditReport.Add(new UserPermissionReport
                {
                    Username = username,
                    Permissions = permissions,
                    LastAuditDate = DateTime.UtcNow
                });
            }
        }
        
        // 生成审计报告
        GenerateAuditReport(auditReport);
    }
    
    private void GenerateAuditReport(List<UserPermissionReport> report)
    {
        Console.WriteLine("用户权限审计报告");
        Console.WriteLine("=================");
        
        foreach (var userReport in report)
        {
            Console.WriteLine($"用户: {userReport.Username}");
            Console.WriteLine($"权限: {string.Join(", ", userReport.Permissions)}");
            Console.WriteLine($"审计时间: {userReport.LastAuditDate}");
            Console.WriteLine();
        }
    }
}

public class UserPermissionReport
{
    public string Username { get; set; }
    public DatabasePermission[] Permissions { get; set; }
    public DateTime LastAuditDate { get; set; }
}
```

## 测试支持

### 单元测试示例

```csharp
[TestClass]
public class UserManagerTests
{
    private IUserManager _userManager;
    private const string TestUsername = "testuser";
    private const string TestPassword = "Test123!@#";
    
    [TestInitialize]
    public void Setup()
    {
        PostgreSQLBootstrapper.Register();
        _userManager = ProviderFactory.CreateUserManager("PostgreSQL", connectionString);
    }
    
    [TestCleanup]
    public void Cleanup()
    {
        if (_userManager.Exists(TestUsername))
        {
            _userManager.Delete(TestUsername);
        }
    }
    
    [TestMethod]
    public void Create_NewUser_ShouldSucceed()
    {
        // Act
        var result = _userManager.Create(TestUsername, TestPassword);
        
        // Assert
        Assert.IsTrue(result);
        Assert.IsTrue(_userManager.Exists(TestUsername));
    }
    
    [TestMethod]
    public void GrantPermissions_ValidUser_ShouldAddPermissions()
    {
        // Arrange
        _userManager.Create(TestUsername, TestPassword);
        var permissions = new[] { DatabasePermission.Select, DatabasePermission.Insert };
        
        // Act
        _userManager.GrantPermissions(TestUsername, permissions);
        
        // Assert
        var userPermissions = _userManager.GetPermissions(TestUsername);
        Assert.IsTrue(userPermissions.Any(p => p.HasFlag(DatabasePermission.Select)));
        Assert.IsTrue(userPermissions.Any(p => p.HasFlag(DatabasePermission.Insert)));
    }
    
    [TestMethod]
    public void DisableAndEnable_ExistingUser_ShouldWork()
    {
        // Arrange
        _userManager.Create(TestUsername, TestPassword);
        
        // Act & Assert
        _userManager.Disable(TestUsername);
        Assert.IsTrue(_userManager.Exists(TestUsername)); // 用户仍存在
        
        _userManager.Enable(TestUsername);
        Assert.IsTrue(_userManager.Exists(TestUsername)); // 用户仍存在
    }
}
```

用户管理API为UDataset提供了完整的用户账户和权限管理能力，支持跨数据库的统一用户管理接口，是构建安全数据库应用的重要组件。 