# UBot 重命名与新仓库迁移方案

## 0. 背景与目标
- **现状**：代码库命名为 *NanoBot.Net*，与原 nanobot 项目逐步分化，产品能力已超出移植范围。
- **目标**：将整体项目品牌、命名空间、发布名统一更名为 **UBot**，并创建一个全新的 Git 仓库承载未来演进，停止使用旧仓库。
- **约束**：
  1. 基于 Microsoft.Agents.AI 的技术栈保持不变，禁止重复造轮子。
  2. 新仓库初始化时保留当前代码基线，但不携带旧仓库 Git 历史（明确“放弃原有的库”）。
  3. 迁移期间需保证构建、测试、CLI、打包脚本可用，避免业务中断。

---

## 1. 范围与非目标
### 范围
1. 命名体系重构：仓库名、解决方案、项目目录、命名空间、NuGet 包名、Docker 映像名、脚本等。
2. 文档、配置与资源中的品牌替换（README、doc.ai、install 脚本、示例配置、NLog、Dockerfile 等）。
3. CI/CD、发布流水线、版本号标签、包发布通道重建并指向新仓库。
4. 依赖注入、配置文件、反射/资源加载等代码逻辑中涉及 `NanoBot` 的硬编码清理。
5. 对外兼容策略：向用户/内部团队提供升级指引或 breaking change 通告。

### 非目标
- 不在此轮引入新功能或重构业务逻辑（除为支持重命名必须的调整）。
- 不迁移旧仓库 issue / PR 历史；如需保留，另外导出。
- 不改变 Microsoft.Agents.AI、依赖注入和配置框架选型。

---

## 2. 命名策略与映射
| 原名称 | 新名称 | 备注 |
| --- | --- | --- |
| 仓库 `NanoBot.Net` | `UBot` | Git 根目录与远程仓库同名 |
| 解决方案 `NanoBot.Net.sln` | `UBot.sln` | 解决方案内部引用更新 |
| 项目 `NanoBot.***` | `UBot.***` | 示例：`NanoBot.Agent`→`UBot.Agent` |
| 命名空间 `NanoBot.*` | `UBot.*` | 全量替换，保持模块层级一致 |
| NuGet 包名 `NanoBot.*` | `UBot.*` | 更新 `PackageId`、`AssemblyName`、`RootNamespace` |
| Docker 映像 `nanobot-net` | `ubot` | 更新 Dockerfile/compose/发布脚本 |
| CLI/可执行名称 `nanobot` | `ubot` | 调整 System.CommandLine root command、安装脚本 |
| 文档品牌 `NanoBot.Net` | `UBot` | README、doc.ai、workspace 模板等 |

---

## 3. 新 Git 仓库建设
1. **准备阶段**
   - 在当前工作副本冻结代码（确保 CI 通过）。
   - 记录老仓库最终 commit hash 以备追溯。
2. **创建新仓库**
   - 在目标托管平台（GitHub/GitLab）创建空仓库 `UBot`，初始化默认分支 `main`。
   - 在本地复制当前工作树到全新目录 `/Users/.../UBot`，删除 `.git`，重新执行 `git init`。
   - 配置远程 `origin` 指向新仓库，首个 commit 即迁移后的 UBot 代码。
3. **访问控制**
   - 复制/调整旧仓库的团队权限、branch protection、Secrets/Actions 权限。
   - 在旧仓库 README 置顶写明存档状态并指向新仓库。

---

## 4. 迁移实施步骤
### 阶段 0：基线确认
- 运行 `dotnet test`、CLI 自测，生成基线报告。
- 整理所有引用 `NanoBot` 的外部资源（NuGet 包名、Docker Hub 名、内部脚本、文档链接）。

### 阶段 1：代码与结构重命名
1. **目录与文件**
   - 重命名根目录与解决方案文件；更新 `Directory.Build.props` 中 `<RootNamespace>`、`<Company>` 等 metadata。
   - 各项目文件夹 `src/NanoBot.*` / `tests/NanoBot.*` → `src/UBot.*` / `tests/UBot.*`。
2. **项目文件**
   - 在所有 `.csproj` 中更新 `AssemblyName`、`RootNamespace`、`PackageId`、`InternalsVisibleTo`。
   - 更新 `ProjectReference` 路径与 `<IncludeAssets>` 等包含的目标路径。
3. **命名空间与类型**
   - 批量替换 `namespace NanoBot...` 与 `using NanoBot...` 为 `UBot...`。
   - 检查 `InternalsVisibleTo("NanoBot.*")`、`FriendAssemblies`，同步更新。
4. **资源与嵌入文件**
   - `workspace/`、`skills/`、`doc.ai` 等仍保留目录名，但引用文案替换品牌。

### 阶段 2：应用层与 CLI 调整
- System.CommandLine root command 名称改为 `ubot`；更新 CLI help/banner。
- 配置样例 `config.example.json` 字段中描述 `NanoBot`→`UBot`。
- `nlog.config`、`appsettings`、`AGENTS.md`、`SOUL.md` 等内容中的品牌替换。
- 验证 CLI 输出、日志、异常信息不再出现旧名称（除兼容提示）。

### 阶段 3：脚本、容器与发布渠道
- `install/install.sh|ps1`, `publish.sh`, `docker-compose.yml`, `Dockerfile` 重命名构建结果（镜像、容器、生成路径）。
- 若通过 Homebrew、自建 feed 分发，更新 formula 名称与下载 URL。
- GitHub Actions / release workflow：
  - 更新工作流文件名（如 `release.yml`）中的 `NanoBot.Net` 字符串、badge、artifact 命名。
  - 调整 `DOTNET_CLI_TELEMETRY_OPTOUT` 等环境变量保持不变。
- NuGet / container registry：创建新的组织/namespace，旧包留在原名不再更新。

### 阶段 4：文档与知识库
- README、LICENSE、CLA、doc.ai（plans/solutions/reports）全文检索替换品牌。
- `workspace` 模板中的对外提示（如 `USER.md`）同步更新。
- 在 doc.ai/report 编写迁移通告，列出 breaking changes 与升级步骤。

### 阶段 5：验证与发布
1. 在新仓库中执行：
   - `dotnet build`, `dotnet test`（含 CI）。
   - CLI 集成测试：初始化 workspace，运行 heartbeat/memory/session 关键路径。
   - Docker build & run，验证 CLI/agent 可执行。
2. 完成 QA checklist 后打首个 `vNext` tag（例如 `v0.9.0-ubot`）。
3. 公布迁移完成信息，关闭旧仓库写权限，仅保留只读。

---

## 5. 风险与缓解
| 风险 | 影响 | 缓解措施 |
| --- | --- | --- |
| 命名替换遗漏导致运行期反射/DI 失败 | 应用无法启动 | 使用 `dotnet format` + Roslyn 分析查找 `NanoBot` 文本；添加编译期 Analyzer 检查 |
| 新仓库 CI/CD 配置缺失 | 无法发布 | 迁移前导出现有 workflow 清单，迁移后逐项验证 badge、Secrets、环境变量 |
| 外部依赖仍拉取旧 NuGet 包 | 用户升级失败 | 在 README、release note、NuGet description 中声明弃用，并在旧包 README 中提示迁移 |
| 文档/脚本路径硬编码 | 用户脚本执行失败 | 全局搜索 `NanoBot`/`nanobot`，结合脚本测试；必要时提供兼容 symlink 或 wrapper |
| 旧仓库仍被自动化引用 | 潜在自动化报错 | 在旧仓库设置 archive + README 顶部警告；保留最后 release 包下载链接 |

---

## 6. 验收清单
- [ ] 新仓库 `UBot` 可独立 clone/build/test 通过。
- [ ] 解决方案与命名空间无 `NanoBot` 残留（除历史文档/兼容提示）。
- [ ] CLI、日志、安装脚本输出均为 `UBot`。
- [ ] GitHub Actions / Release / Docker 发布在新仓库运行成功。
- [ ] README 与 doc.ai 文档更新完成，并提供迁移指南链接。
- [ ] 旧仓库标记为 Archived，README 指向 UBot。

---

## 7. 后续动作
1. 编写迁移公告（doc.ai/reports + README Migration Guide），告知 breaking changes。
2. 规划后续版本路线图（UBot 专属特性），同步更新 doc.ai/plans。
3. 评估是否需要自动化脚本帮助现有用户从 NanoBot workspace 迁移到 UBot（配置/内存/会话保留）。
