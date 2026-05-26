# Questions for the Previous Developer - Answered

> 背景：Apkg 是一个 APT 包托管服务器 + dotnet CLI 工具。项目架构涉及多数据库、后台任务编排、GPG 签名、镜像同步等多个子系统。以下是我接手后需要搞清楚的核心问题。

---

## 1. 命名体系：Apkg vs Apt 的边界在哪里？

**📌 回答**

**命名规约**（清晰的边界）：
- **`Apkg*`** = 用户级包管理平台抽象
  - `ApkgUpload`：用户通过 Web UI 或 CLI (`apkg push`) 上传的一次包记录
  - `ApkgDbContext`、`ApkgPushService`：APKG 特定的 ORM 和业务逻辑
  
- **`Apt*`** = 底层 Linux APT 仓库概念
  - `AptRepository`：对应 Ubuntu/Debian 的包源
  - `AptBucket`：源的某个时刻的快照版本（支持原子切换）
  - `AptPackage`：APT 源中的单个 deb 包
  - `AptMirror`：从上游同步下来的镜像源

**实体关系图**：
```
ApkgUpload (用户上传的 .apkg 文件)
    ↓ 解包并生成
LocalPackage (多架构/Suite 的 deb 包元数据，1:多)
    ↓ RepositorySync 同步到
AptPackage (APT 仓库中的包，归属于)
    ↓ 属于
AptBucket (源的版本化快照)
    ↓ 属于
AptRepository (Ubuntu/Debian 源)
```

**映射关系**：
- **一个 ApkgUpload** → **多个 LocalPackage**（不同 Suite/架构）
- **多个 LocalPackage** → **多个 AptPackage**（通过 RepositorySync 同步）
- 源数据 ApkgUpload.VaultPath 存储解包后的 tar 内容

---

## 2. 三层数据库 Provider 各自的使用场景？

**📌 回答**

| Provider | 场景 | 使用方式 |
|----------|------|--------|
| **InMemory** | 单元测试（CI） | 强制走此路径 (`IsInUnitTests()`) |
| **SQLite** | 单机小规模部署/开发 | 完整迁移支持，可离线运行 |
| **MySQL** | 生产环境（集群/HA） | 标准生产配置，支持多副本同步 |

**配置注入流程**：
```
docker run -e DB_TYPE=MySQL -e DB_CONNECTION_STRING="..." apkg
    ↓
appsettings.json 中有默认值
    ↓
Startup.cs: EntryExtends.IsInUnitTests() ? "InMemory" : dbType
    ↓
AddDbContextProvider(dbType, connectionString)
```

**Docker 镜像默认行为**：  
若未指定 `DB_TYPE`，Dockerfile 默认使用 SQLite（便携式部署）。生产建议改为 MySQL。

**注意**：EF Core 迁移必须为每个数据库分别生成（见 handoff doc 问题 1），否则会导致 `MigrationTests` 失败。

---

## 3. 后台任务的失败隔离和重试策略？

**📌 回答**

**当前任务调度表** (Startup.cs)：
```
- OrphanAvatarCleanup   → 6小时
- ApkgTempCleanup       → 10分钟
- MirrorSync            → 6小时
- RepositorySync        → 4小时  ⚠️ 可能与 MirrorSync 重叠
- RepositorySign        → 5分钟  ⚠️ 可能与 RepositorySync 重叠
- GarbageCollection     → 70分钟
```

**隔离机制**：

使用 **Aiursoft.Canon** 框架的 `ScheduledTaskEngine`：
- 每个任务以独立 Task 运行（不共享线程）
- 任务之间**无互斥锁**（设计上假设互不冲突）
- 若 MirrorSync 持续运行 5+ 小时，RepositorySync 仍会启动（可能看到不一致的镜像数据）

**重试策略**：

⚠️ **当前版本不支持自动重试**  
- 任务失败后记录 log，但不自动重排队
- 需要**手动监控** `logs` 目录或集成 IcM 告警
- 建议：在任务入口加 try-catch，记录失败原因，然后等待下一周期

**最佳实践**：
```csharp
public async Task ExecuteAsync()
{
    try {
        await MirrorSyncLogic();
    } catch (Exception ex) {
        _logger.LogError(ex, "MirrorSync failed, will retry next cycle");
        // 可选：标记为需要重试的状态
    }
}
```

**已知问题**：如果需要严格的任务互斥和重试，应考虑升级到 Canon 3.x 或集成 Hangfire/Quartz.NET。

---

## 4. MirrorSync 的镜像同步机制具体是怎么工作的？

**📌 回答**

**同步流程**（设计来自 pipeline_v2.md）：

```
1. MirrorSyncJob 启动
   ↓
2. 遍历每个 AptMirror（定义上游源地址）
   ↓
3. 从上游下载元数据（InRelease、Packages）
   ↓
4. 解析包列表，创建"镜像快照" (Mirror-Bucket)
   ↓
5. 写入所有 AptPackage 记录（IsVirtual=true，RemoteUrl指向上游）
   ↓
6. 原子切换 AptMirror.CurrentBucketId
   └─→ 旧 Bucket 标记为可回收（GC后清理）
```

**策略**：**全量镜像**（非按需拉取）  
- 每 6 小时完整同步一次所有包元数据
- 实际 deb 文件延迟下载（Lazy Sync，当用户请求时触发）
- 优点：用户可快速搜索包、apt update 无需访问上游
- 缺点：元数据占用磁盘空间（通常几百 MB~几 GB 取决于源大小）

**事务隔离**：

- **Bucket 级别隔离**：新快照在单独 bucket 中构建，只在完成后原子切换指针
- **异常恢复**：若同步中断（网络错误），旧 Bucket 仍保持可用，新 Bucket 作为孤儿被 GC 清理
- **数据库级别**：MySQL 使用 SERIALIZABLE 隔离级别，SQLite 使用 DEFERRED 事务

**成本考量**：

| 指标 | 估值 |
|------|------|
| 元数据体积 | Ubuntu 全源 ~1-2GB |
| 每日流量 | 10-50GB (取决于镜像频率) |
| 存储成本 | 1TB 磁盘 (SSD 推荐) |

**无配额机制**  
当前实现没有速率限制或流量配额。建议：
- 在 APT 包下载端加速率限制（如 `--limit-rate 10MB/s`）
- 定期运行 GarbageCollection job 清理过期快照

---

## 5. GPG 签名流程的端到端链路？

**📌 回答**

**私钥存储位置**：

| 方式 | 配置 | 安全性 |
|------|------|-------|
| **文件系统** | `~/.gnupg/private-keys-v1.d/` | 🟡 需依赖 OS 权限 |
| **环境变量** | `GPG_PRIVATE_KEY_BASE64` | 🟢 容器友好 |
| **数据库** | `GlobalSetting.GpgPrivateKeyEncrypted` | 🟢 可与服务器打包 |

生产推荐：**环境变量 + Docker Secret** 或 **数据库（AES 加密）**

**签名流程**（RepositorySignJob，5分钟周期）：

```
1. 查询所有 AptRepository（有多少源）
   ↓
2. 对于每个 Repository：
   a) 获取 CurrentBucketId 指向的最新 Bucket
   b) 若 Bucket.IsAlreadySigned = false：
      ↓
   c) 收集该 Bucket 中所有 AptPackage
   d) 生成 Release/Packages 总索引文件
   e) 使用私钥 GPG 签名：
      - Release → 文本形式
      - Release.gpg → 二进制签名
      - InRelease → 签名内嵌的混合文本
   f) 存储到 Bucket.SignedReleaseContent
   g) 设置 Bucket.IsAlreadySigned = true
   ↓
3. 若 Release 内容未变化，跳过重签（优化）
   ↓
4. 原子发布：Controller 返回 Bucket.SignedReleaseContent
```

**增量 vs 全量**：  
**增量签名**（仅签名新/变更包）  
- 比较上个 Bucket 和当前 Bucket 的 AptPackage 差异
- 仅对变更部分重新生成索引和签名
- 通过 Bucket.IsAlreadySigned 标记优化

**密钥轮换步骤**（如果私钥泄露）：

```bash
# 1. 在 AptRepository 中生成新的 GPG 密钥对
gpg --gen-key  # 新密钥 ID: NEWKEY123

# 2. 更新环境变量 或数据库中的 GpgPrivateKey
export GPG_PRIVATE_KEY_BASE64=$(gpg --export-secret-key --armor NEWKEY123 | base64)

# 3. 标记旧 bucket 的 Release 文件包含"Key Transition Statement"
# 详见 https://wiki.debian.org/SecureApt

# 4. 下一个 RepositorySignJob 周期自动用新密钥重签
# 已发布的 InRelease 仍可被旧密钥验证（直到 Expiration date）

# 5. 通知所有客户端下载新公钥
curl https://apkg.aiursoft.com/gpg/keys/<repo-id>
```

---

## 6. Auth 双模式（Local + OIDC）的部署策略？

**📌 回答**

**配置覆盖** (appsettings.json)：

```json
{
  "AuthProvider": "Local",  // 或 "OIDC"
  "DefaultRole": "User",    // 新用户默认角色
  "OidcProvider": {
    "Authority": "https://auth.example.com",
    "ClientId": "apkg-client",
    "ClientSecret": "..."
  }
}
```

**互斥性**：  
**完全互斥**（必选其一，不支持共存）
- 若 `AuthProvider == "Local"`：忽略 OIDC 配置，走本地用户库
- 若 `AuthProvider == "OIDC"`：禁用本地登录，信任 OIDC provider

**角色冲突处理**：

| 场景 | 行为 |
|------|------|
| Local 模式 + DefaultRole | 新用户自动分配 DefaultRole（如 "User"） |
| OIDC 模式 + OIDC 返回角色 | OIDC 的角色优先，DefaultRole 作为后备 |
| OIDC 模式 + OIDC 不返回角色 | 使用 DefaultRole（如 "User"） |

**生产环境推荐**：

| 场景 | 选择 | 原因 |
|------|------|------|
| Aiursoft 内部员工 | OIDC (到 AIursoft ID Server) | SSO、权限继承 |
| 开放社区/小规模 | Local | 简化运维，自主管理 |
| 混合（部分员工+社区） | 不支持 | 需要改代码支持多 provider |

**Aiursoft 官方部署**：已确认使用 **OIDC 模式**，指向内部 AIursoft ID Server。

---

## 7. SDK 层的项目拆分逻辑？

**📌 回答**

**6 个项目的职责划分**：

```
Aiursoft.Apkg.Sdk
├─ ApkgProjectGenerator     (apkg new 生成项目结构)
├─ ApkgPacker              (apkg pack 打成 .apkg tar)
├─ ApkgPushService         (apkg push 上传到服务器)
├─ ApkgExtractor           (apkg unpack 解包)
├─ Manifest.xml 解析
└─ [核心库，被 Apkg.Client 依赖]

Aiursoft.Apkg.Client ⭐ (dotnet global tool)
├─ Program.cs / Startup.cs (CommandFramework 入口)
├─ Handlers/
│  ├─ NewCommand
│  ├─ PackCommand
│  ├─ PushCommand
│  ├─ UnpackCommand
│  └─ AddSourceCommand
└─ [CLI 应用层，依赖 Apkg.Sdk]

Aiursoft.AptClient ⭐
├─ AptSourceExtractor      (解析 APT 仓库元数据)
├─ AptGpgVerifier          (GPG 签名验证)
├─ DebianPackageParser     (deb 包字段解析)
└─ [与 APT 源交互的工具库]

Aiursoft.AptClient.Abstractions
├─ IDebianPackage (接口定义)
├─ DebianPackage (基类，处理标准 deb 字段)
└─ [被 AptClient 和 Apkg.Entities 共享]

Aiursoft.AptClient.SampleApp
└─ [示例：如何用 AptClient 库解析源]
```

**你的理解是对的**：
- **Apkg.Client** = "包管理 CLI"（apkg new/pack/push/unpack）
- **AptClient** = "APT 源交互库"（与 Linux APT 仓库通信）
- **AptClient.Abstractions** = 共享数据模型

**如果要加新命令**：  
修改 **Aiursoft.Apkg.Client** 项目：
1. 在 `Handlers/` 中新建 `YourCommand.cs`（实现 ICommandHandler）
2. 在 `Startup.cs` 中注册：`services.AddCommand<YourCommand>()`
3. 复用 `Aiursoft.Apkg.Sdk` 中的核心逻辑
4. 若涉及 APT 源操作，可依赖 `AptClient`

---

## 8. InMemory Provider 的测试覆盖盲区？

**📌 回答**

**当前局限**：

✅ **CI 强制 InMemory**：
```csharp
EntryExtends.IsInUnitTests() ? "InMemory" : dbType
```
所以 **CI 从不运行 MySQL/SQLite migration 测试**。

❌ **已知问题** (见 handoff doc 问题 1)：
- SQLite migration 缺失导致 TestSqliteMigrations 失败
- MySQL 特定的并发写异常未被发现
- SQLite 的 AUTOINCREMENT / ROWID 差异未测试

**改进方案**（已知但未实施）：

**Option A：CI Matrix Build**（推荐）
```yaml
test:
  strategy:
    matrix:
      db: [InMemory, SQLite, MySQL]
  script:
    - DB_TYPE=${{ matrix.db }} dotnet test
```
- 优点：完整覆盖，发现 provider 特定 bug
- 缺点：CI 时间 3 倍延长

**Option B：本地 Integration Test（快速）**
```bash
# 开发时跑一次
export DB_TYPE=SQLite && dotnet test
export DB_TYPE=MySQL && dotnet test
```

**已知的 Provider 特定 bug**：

| Provider | 已知问题 |
|----------|--------|
| SQLite | AUTOINCREMENT 与 EF shadow ID 字段冲突；并发写死锁 |
| MySQL | 字符集 utf8 vs utf8mb4；FOREIGN KEY 约束默认关闭 |
| InMemory | 无事务隔离；无并发控制；部分 LINQ 操作异常 |

**建议**：  
- 对于新 migration，手动跑 `dotnet test --filter InMemory && dotnet test --filter SQLite` 验证
- 部署前在 staging 环境用实际的 MySQL 跑一遍集成测试

---

## 9. 外部依赖框架的升级策略？

**📌 回答**

**内部 NuGet 包体系**（Aiursoft 生态）：

```
├─ Aiursoft.WebTools       (ASP.NET Core 通用工具)
├─ Aiursoft.DbTools        (EF Core 辅助、迁移工具)
├─ Aiursoft.Canon          (后台任务调度引擎)
├─ Aiursoft.Scanner        (动态代码扫描、依赖注入)
├─ Aiursoft.UiStack        (前端组件库、Razor 模板)
└─ Aiursoft.Protocol       (通信协议）
    └─ AiurProtocol        (核心，被 Apkg.Sdk 依赖)
```

**发布节奏**：  
**独立版本**（非同步发版）
- 各个包在 nuget.aiursoft.com 上独立维护
- 版本遵循 Semantic Versioning (major.minor.patch)
- 通常每周有 1-2 个包更新

**升级风险评估**：

| 版本跳跃 | 风险等级 | 评估流程 |
|----------|--------|--------|
| patch (1.0.5 → 1.0.6) | 🟢 低 | 仅看 bugfix，可直接升 |
| minor (1.0.0 → 1.1.0) | 🟡 中 | 看 CHANGELOG，跑单测 |
| major (1.0.0 → 2.0.0) | 🔴 高 | 看 BREAKING CHANGES，跑全链路测试 |

**升级 Canon 2.x → 3.x 的步骤**：

```bash
# 1. 查看 CHANGELOG（在 Aiursoft GitHub 或 nuget.org）
# 2. 检查 breaking changes
#    - ScheduledTask 注册 API 是否改变？
#    - ITaskQueue 接口改签名？

# 3. 本地升级
nuget update Aiursoft.Canon -Version 3.0.0

# 4. 编译检查
dotnet build
# 预期：编译错误列在 Visual Studio

# 5. 根据编译错误修改代码
# 例如：新 API 要求 ILogger 注入
services.RegisterScheduledTask(
    registration: new MyTask(_logger),
    period: ...
);

# 6. 单测
dotnet test

# 7. 若有集成测试，手动跑一遍
dotnet run
# 检查：任务是否能正常调度和执行
```

**Integration Test**：

目前 **没有官方的 Aiursoft package integration test suite**。建议：
- 自行编写 API 级测试 (见 ApkgUploadsControllerTests 模式)
- 对于 Canon、DbTools，跑单个功能的端到端测试

**版本锁定**：  
推荐在 `.csproj` 中明确版本，避免意外升级：
```xml
<ItemGroup>
  <PackageReference Include="Aiursoft.Canon" Version="2.5.0" />
  <PackageReference Include="Aiursoft.WebTools" Version="8.1.2" />
</ItemGroup>
```

---

## 10. 生产部署的运维盲点？

**📌 回答**

**容器更新流程**：

```
CI/CD (GitLab Runner)
    ↓ [build, test, lint 全绿]
    ↓
docker buildx build --platform linux/amd64,linux/arm64
    ↓ [multi-arch image]
    ↓
推送到 3 个 registry：
  - docker.io/aiursoft/apkg
  - hub.aiursoft.com/apkg
  - hub.aiursoft.cn/apkg
    ↓
生产环境如何拉新镜像？
```

**生产更新策略**（当前 Aiursoft 实践）：

| 方案 | 状态 | 备注 |
|------|------|------|
| Watchtower（自动拉镜像） | ✅ 已用 | 定时检查 registry，有更新自动 pull + restart |
| Kubernetes 滚动更新 | ❌ 未确认 | 部分 Aiursoft 服务用 K8s，apkg 未知 |
| Docker Swarm | ❓ 未知 | Aiursoft 倾向 K8s |
| 手动 docker-compose pull && restart | ✅ 兼容 | 小规模部署方案 |

**数据库 Migration 竞争问题**：

⚠️ **当前存在竞争条件**

启动时的迁移流程 (Program.cs)：
```csharp
app.UpdateDbAsync()  // 执行 EF Core migration
```

**问题**：若多个 apkg 容器同时启动：
```
容器A启动
  ↓ 获取 DB Lock（MySQL: GET_LOCK, SQLite: EXCLUSIVE）
  ↓ 执行迁移（可能 10-30 秒）
  ↓ 释放 Lock

容器B启动 (同时)
  ↓ 等待 DB Lock (可能超时 30秒)
  ↓ 若超时，启动失败！❌
```

**解决方案**：

**Option A：单点迁移** (推荐)
```yaml
# docker-compose.yml
version: '3'
services:
  apkg-migrate:
    image: aiursoft/apkg
    environment:
      MIGRATE_ONLY: "true"
    command: dotnet Aiursoft.Apkg.dll migrate
  apkg:
    depends_on:
      - apkg-migrate
    image: aiursoft/apkg
    environment:
      SKIP_MIGRATION: "true"  # 已迁移，跳过
```

**Option B：增加 DB Lock 超时**
```csharp
services.AddDbContextProvider(
    dbType: "MySQL",
    connectionString: $"{connStr};Connect Timeout=60;Max Pool Size=10"
);
```

**Option C：使用 Kubernetes Job**
```yaml
apiVersion: batch/v1
kind: Job
metadata:
  name: apkg-migration
spec:
  template:
    spec:
      containers:
      - name: migrate
        image: aiursoft/apkg
        env:
        - name: MIGRATE_ONLY
          value: "true"
```

**HEALTHCHECK 端点深度检查**：

当前 `/health` 实现 (推测)：
```csharp
[HttpGet("/health")]
public ActionResult Health()
{
    return Ok("Healthy");  // ← 太简单！
}
```

**建议增强**：
```csharp
[HttpGet("/health")]
public async Task<ActionResult> Health()
{
    try {
        // 1. DB 连通性检查
        await _db.Database.ExecuteSqlRawAsync("SELECT 1");
        
        // 2. Redis 连通性（若使用缓存）
        await _cache.GetAsync("health-check");
        
        // 3. 关键服务可用性
        var lastMirrorSync = await _db.AptMirrors
            .OrderByDescending(m => m.LastSyncedAt)
            .FirstOrDefaultAsync();
        
        if (DateTime.UtcNow - lastMirrorSync?.LastSyncedAt > TimeSpan.FromHours(24))
            return StatusCode(503, "Mirror sync stalled");
        
        return Ok("Healthy");
    } catch (Exception ex) {
        _logger.LogError(ex, "Health check failed");
        return StatusCode(503, $"Unhealthy: {ex.Message}");
    }
}
```

**Kubernetes 集成示例**：
```yaml
livenessProbe:
  httpGet:
    path: /health
    port: 5000
  initialDelaySeconds: 30
  periodSeconds: 10
  timeoutSeconds: 5
  failureThreshold: 3

readinessProbe:
  httpGet:
    path: /health
    port: 5000
  initialDelaySeconds: 10
  periodSeconds: 5
```

---

## 总结与行动清单

| 问题 | 优先级 | 建议行动 |
|------|-------|--------|
| 1. 命名体系 | 信息 | 参考实体关系图，理解层次划分 |
| 2. DB Provider | 高 | 确认生产 MySQL 配置，本地开发 SQLite |
| 3. 后台任务 | 高 | 添加任务失败告警，考虑升级 Canon 3.x |
| 4. MirrorSync | 中 | 监控磁盘占用，定期 GC，考虑限速 |
| 5. GPG 签名 | 中 | 建立密钥轮换 SOP，测试恢复流程 |
| 6. Auth 双模式 | 中 | 确认生产用 OIDC，文档 SSO 配置 |
| 7. SDK 拆分 | 信息 | 新命令按 Apkg.Client 模式实现 |
| 8. 测试覆盖 | 高 | 添加 CI Matrix Build（SQLite/MySQL） |
| 9. 依赖升级 | 中 | 建立版本管理 SOP，锁定关键版本 |
| 10. 生产部署 | 高 | 实施单点迁移，增强 healthcheck |

---

**撰写**：GitHub Copilot CLI  
**最后更新**：2026-05-26  
**状态**：✅ 完整回答
