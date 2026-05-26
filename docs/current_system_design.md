# APKG 系统当前设计与实现状态（2025-05-26）

> 本文档归纳了截至 2025-05-26 的 APKG 项目的完整系统设计、已实现模块、以及当前的架构决策。这是对 `design.md`（战略愿景）的**补充与同步**，确保文档与代码实现保持一致。

---

## 一、项目概述

**APKG** 是一个 APT 包管理系统，由以下三大部分组成：

1. **Web 服务器** (`Aiursoft.Apkg`）  
   - ASP.NET Core 8.0 Web 应用  
   - NuGet 式的包托管与管理平台  
   - 提供 Web UI 和 REST API  

2. **CLI 客户端** (`Aiursoft.Apkg.Client`)  
   - 全局工具（dotnet tool install）  
   - 支持 `apkg new/pack/push/unpack/add-source` 等命令  
   - 基于 CommandFramework + AiurProtocol 架构  

3. **SDK 库** (`Aiursoft.Apkg.Sdk`)  
   - 为 CLI 提供核心功能（打包、推送、解包等）  
   - 与服务器通信的客户端库  

---

## 二、APKG 包格式 & APT 兼容性策略

### 2.1 .apkg 包格式

**定义**：一个 TAR 文件，包含多个 `.deb` 文件 + 元数据 manifest

**内部结构**：
```
my-package.1.0.0.apkg (tar 格式)
├── manifest.xml              # 元数据文件
├── debs/
│   ├── main/
│   │   ├── ubuntu-24.04-amd64.deb
│   │   ├── ubuntu-24.04-i386.deb
│   │   ├── ubuntu-25.04-amd64.deb
│   │   └── ...
│   └── restricted/
│       ├── ubuntu-24.04-amd64.deb
│       └── ...
└── [可选] README、LICENSE 等
```

**manifest.xml 职责**：
- 声明支持的操作系统（Ubuntu、Debian）  
- 声明支持的 suites（questing、questing-updates、questing-security 等）  
- 声明支持的 architectures（amd64、i386、arm64）  
- 声明所属 component（main、restricted、universe、multiverse）  
- 映射 suite/arch 到具体 deb 文件

**设计理念**  
- 一个 .apkg 包 = 一个逻辑产品单元（如 "统计学" 软件）  
- 该产品可能需要为不同 Ubuntu 版本和硬件架构编译不同的 deb  
- manifest 负责描述这些变体之间的关系  
- 服务器在解包后，根据 manifest 指导：为每个 suite/arch 配置一个 APT 仓库条目  

### 2.2 APT 兼容性

**核心策略**：APKG 不自己安装包，而是与 APT/dpkg 深度融合。

**用户工作流**：
```bash
# 1. 从 APKG 服务器添加源（一次性）
sudo apkg add-source https://apkg.example.com/api/sources/1

# 2. 之后用标准 apt 命令安装
sudo apt update
sudo apt install my-package

# 3. 如果需要本地打包和发布
apkg new --name my-package
apkg pack --path ./my-package
apkg push --file my-package.1.0.0.apkg --source https://apkg.example.com --api-key xxx

# 4. 如果从已有 .apkg 文件快速获取内容（不安装）
apkg unpack --file my-package.1.0.0.apkg --output ./unpacked
```

**关键设计点**  
- `apkg install` 不存在（改名为 `apkg add-source` 一键配置）  
- `apkg unpack` 用于解压、检查、覆盖架构  
- 真正的安装由 `apt install` 或 `dpkg` 处理  
- 卸载由 `apt remove` 处理  
- APKG = 打包 + 源配置，APT/dpkg = 安装卸载  

### 2.3 Repository（APT 仓库）与 Suite 概念

**Repository 对标 Ubuntu PPA**  
- 每个 Repository 有一个唯一的 ID（服务器分配）  
- Repository 下可以有多个 Suite（如 questing、questing-updates 等）  
- 每个 Suite 包含多个 Component（main, restricted, universe, multiverse）  
- 每个 Component 支持多个 Architecture（amd64, i386, arm64...）  

**APT 源配置文件示例** (`/etc/apt/sources.list.d/apkg.sources`，DEB822 格式）：
```
Types: deb
URIs: https://apkg.aiursoft.com/repo/1/questing
Suites: questing
Components: main restricted universe multiverse
Signed-By: /usr/share/keyrings/apkg-keyring.gpg

Types: deb
URIs: https://apkg.aiursoft.com/repo/1/questing-updates
Suites: questing-updates
Components: main restricted universe multiverse
Signed-By: /usr/share/keyrings/apkg-keyring.gpg
```

`apkg add-source <repo-url>` 的职责：
- 从服务器获取该 Repository 的元数据（包含 GPG 公钥 URL、支持的 suites 等）  
- 自动生成上述 sources.list.d 条目  
- 下载并安装 GPG 公钥  
- 运行 `apt update`  

---

## 三、Web 服务器架构

### 3.1 核心模块

#### A. Controllers（控制器）

| Controller | 职责 | 路由 | 认证 |
|----------|------|-----|------|
| `ApkgUploadsController` | 上传管理 UI（Index、Upload 表单、Details、Unlist、Delete 等） | `/ApkgUploads/*` | `[Authorize]` |
| `ApiPackagesController` | 包管理 API（查询、上传） | `/api/packages/*` | 部分需要 API Key |
| `ApiSourcesController` | 源配置 API（返回 GPG key、suite 信息） | `/api/sources/{id}` | 可选 |
| `LocalPackagesController` | 本地包展示（自托管内容） | `/LocalPackages/*` | `[Authorize]` |

**新增（2025-05-26）**：
- `ApkgUploadsController` → 核心的 Web 上传流程  
- `ApiPackagesController:post /api/packages/apkg-upload` → 接收 CLI `apkg push` 请求  
- `ApiSourcesController:get /api/sources/{id}` → 返回源配置（GPG 公钥、suite 等）  

#### B. Models & Entities

**关键实体**：
```csharp
public class ApkgUpload          // 一次上传记录
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public int VersionNumber { get; set; }
    public string OwnerId { get; set; }
    public AiurApplicationUser Owner { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public bool IsPublic { get; set; }
    public IEnumerable<ApkgPackage> Packages { get; set; }
}

public class ApkgPackage         // 解包后的单个包
{
    public int Id { get; set; }
    public int ApkgUploadId { get; set; }
    public string PackageName { get; set; }
    public string Architecture { get; set; }
    public string Suite { get; set; }
    public string Component { get; set; }
    public string DebFileName { get; set; }
    public long DebFileSize { get; set; }
    public byte[] DebFileSha256 { get; set; }
}
```

#### C. 存储层

**数据库支持**：
- **MySQL** (生产，使用 EF Core）  
- **SQLite** (开发/测试，使用 EF Core）  
- **InMemory** (单元测试快速运行）  

**文件存储**：
- 使用 `IFileStorageService` 抽象（实现在 `LocalPackages` 模块）  
- 实际存储路径：`wwwroot/LocalPackages/{uploadId}/{debFileName}`  

#### D. Pipeline & Job Ballet

参考 `docs/pipeline_v2.md`，关键概念：

| 组件 | 职责 |
|------|------|
| **AptMirror** | 从上游（如 Ubuntu）抓取包元数据，生成 Mirror-Bucket |
| **AptRepository** | 从 Mirror-Bucket 复制数据，GPG 重签名，生成 Repository-Bucket |
| **AptBucket** | 版本化快照容器（原子切换 CurrentBucketId） |
| **AptPackage** | 单个包实体，支持虚转实（Lazy Sync） |

**当前实现状态**：Pipeline V2 架构已设计，Job Ballet 后续补齐。

---

### 3.2 关键 API 端点

#### POST /api/packages/apkg-upload

接收 CLI 的 `apkg push` 请求。

**请求格式**：
```
POST /api/packages/apkg-upload HTTP/1.1
Authorization: Bearer <api-key>
Content-Type: application/octet-stream

[binary tar.gz data]
```

**响应**：
```json
{
  "code": 0,
  "message": "Upload successful",
  "data": {
    "uploadId": 42,
    "packageCount": 3
  }
}
```

**验证**：
- 认证 (401 if missing/invalid API key)  
- Content-Type 校验 (400 if not tar.gz)  
- 文件大小限制  
- TAR 格式校验  
- manifest.xml 解析  

#### GET /api/sources/{id}

返回源配置信息，供 `apkg add-source` 使用。

**请求**：
```
GET /api/sources/1 HTTP/1.1
```

**响应**：
```json
{
  "code": 0,
  "message": "Success",
  "data": {
    "sourceId": 1,
    "friendlyName": "AnduinOS Official",
    "gpgPublicKeyUrl": "https://apkg.aiursoft.com/gpg/1/public.asc",
    "supportedSuites": ["questing", "questing-updates", "questing-security"],
    "supportedComponents": ["main", "restricted", "universe"],
    "supportedArchitectures": ["amd64", "i386", "arm64"]
  }
}
```

---

## 四、CLI 客户端架构

### 4.1 命令结构（CommandFramework 模式）

```
apkg
├── new              # 创建新包项目
├── pack             # 打包成 .apkg
├── push             # 上传到服务器
├── unpack           # 解包 .apkg
└── add-source       # 配置 APT 源
```

**使用 NestedCommandApp 模式**（见 CommandFramework）：
```csharp
public class ApkgApp : AppBase
{
    public override string AppName => "apkg";
    public override string AppDescription => "APKG package manager CLI";
    
    // 子命令在 Services 中注册
    protected override void RegisterCommands(IServiceCollection services)
    {
        services.AddCommand<NewCommand>();
        services.AddCommand<PackCommand>();
        services.AddCommand<PushCommand>();
        services.AddCommand<UnpackCommand>();
        services.AddCommand<AddSourceCommand>();
    }
}
```

### 4.2 核心命令职责

| 命令 | 输入 | 输出 | SDK 依赖 |
|------|------|------|---------|
| `new` | `--name my-package` | 文件夹结构 | `ApkgProjectGenerator` |
| `pack` | `--path ./my-package` | `./my-package.1.0.0.apkg` | `ApkgPacker` |
| `push` | `--file *.apkg --source <url> --api-key xxx` | 上传状态 | `ApkgPushService` |
| `unpack` | `--file *.apkg --output ./unpacked` | 解包文件 | `ApkgExtractor` |
| `add-source` | `<source-url>` | APT 源配置 | `ApkgSourceConfigurator` |

### 4.3 SDK 核心类

**Aiursoft.Apkg.Sdk** 提供：

```csharp
public class ApkgPacker
{
    // 将 ./apkg 目录打成 tar.gz
    public async Task PackAsync(string projectPath, string outputPath);
}

public class ApkgPushService
{
    // 将 .apkg 推送到服务器
    public async Task PushAsync(string filePath, string source, string apiKey);
}

public class ApkgExtractor
{
    // 解包 .apkg，支持 --override-architecture
    public async Task ExtractAsync(string filePath, string outputPath, string? overrideArch);
}

public class ApkgSourceConfigurator
{
    // 调用服务器 /api/sources/{id} 获取配置，写入 /etc/apt/sources.list.d/
    public async Task ConfigureSourceAsync(string sourceUrl);
}
```

---

## 五、测试策略（2025-05-26 更新）

### 5.1 单元测试框架

使用 **MSTest.TestAdapter 4.2.3**，覆盖三层：

| 层 | 测试文件 | 用例数 | 覆盖对象 |
|----|---------|-------|---------|
| SDK（客户端库） | `Aiursoft.Apkg.Client.Tests` | 26 | Packer、Extractor 等 |
| APT 工具集成 | `Aiursoft.AptClient.Tests` | 11 | APT 源管理、GPG 验证 |
| Web 服务集成 | `Aiursoft.Apkg.WebTests` | 363 | Controllers、Permission、Redirect |

**新增测试（2025-05-26）**：
- `ApkgUploadsControllerTests`：16 个 UI 流程测试  
- `ApiPackagesApkgUploadTests`：4 个 API 上传验证测试  
- `ApiSourcesControllerTests`：5 个源配置 API 测试  

**新增特性**：
- SQLite 迁移测试通过  
- 权限模型（[Authorize]）完全覆盖  
- Redirect 路由验证  
- Tar.gz 格式验证  

### 5.2 Lint & Code Quality

**工具**：JetBrains ReSharper (jb CLI)

**覆盖的检查**：
- 未使用的变量/赋值  
- 冗余类型转换  
- 对象初始化与 using 配对规则  
- Razor section 解析  

**状态**（2025-05-26）：✅ 0 警告

---

## 六、部署 & 环境配置

### 6.1 本地开发

**前提条件**：
- .NET 10 SDK  
- Node.js（用于 `npm install wwwroot/`）  
- MySQL 或 SQLite（EF 自动迁移）  

**启动**：
```bash
npm install # 在 wwwroot 目录
dotnet run
# 访问 http://localhost:5000
```

**默认用户**：
- Email: `admin@default.com`  
- Password: `Admin@123456!`  

### 6.2 Ubuntu 生产部署

**一键脚本**：
```bash
curl -sL https://github.com/aiursoftweb/apkg/raw/master/install.sh | sudo bash -s 8080
```

**安装内容**：
- systemd 服务 (`/etc/systemd/system/apkg.service`)  
- 应用二进制 (`/opt/apps/apkg/`)  
- 自动启动  

### 6.3 Docker 部署

```bash
image=aiursoft/apkg
sudo docker pull $image
sudo docker run -d \
  --name apkg \
  --restart unless-stopped \
  -p 5000:5000 \
  -v /var/www/apkg:/data \
  $image
```

---

## 七、关键设计决策与权衡

### 决策 1：为何不用 `apkg install`？

**问题**：`apkg install <package-name>` 与 APT 会产生语义混淆。

**决策**：
- ❌ `apkg install` 保留给本地 .apkg 文件的快速安装（但这与 dpkg/apt 重复）  
- ✅ `apkg add-source` 用于配置 APT 源（一次性）  
- ✅ 之后用 `apt install` 安装包（标准工作流）  

**好处**：
- 用户清晰地知道 APKG = 打包 + 源配置，APT = 安装卸载  
- 减少用户误操作（如尝试 `apkg uninstall` 而实际应用卸载需要 `apt remove`）  
- 与 apt 工具链的深度融合  

### 决策 2：为何支持多 Suite？

**场景**：用户的同一个产品可能需要在 Ubuntu 24.04 和 25.04 的不同版本上部署。

**解决方案**（manifest.xml）：
```xml
<packages>
  <package suite="questing" arch="amd64" deb="debs/main/ubuntu-25.04-amd64.deb" />
  <package suite="jammy" arch="amd64" deb="debs/main/ubuntu-24.04-amd64.deb" />
</packages>
```

**好处**：
- 一个 .apkg 包可以适配多个 Ubuntu 版本  
- 无需为每个版本单独打包和发布  
- 用户 `apkg add-source` 后，apt 会根据本地 `/etc/lsb-release` 自动选择合适的版本  

### 决策 3：为何有 ApkgPackage 表？

**场景**：上传 .apkg 文件后需要快速查询有哪些 deb 包、大小、校验和。

**表设计**（ApkgPackage）：
- 存储解包后的元数据（PackageName、Architecture、Suite、Component）  
- 便于 Web UI 预览、权限检查、完整性验证  
- 支持后续的 Lazy Sync 机制（虚转实）  

---

## 八、已知限制与未来工作

### 8.1 当前限制

| 限制 | 影响 | 优先级 |
|------|------|-------|
| Job Ballet 未实现 | APT Mirror/Repository 同步尚无后台任务调度 | P1 |
| 无 GPG 签名验证 | 上传时未检查 GPG 签名有效期 | P2 |
| 无速率限制 | API 可被滥用 | P2 |
| 无包依赖解析 | .apkg 中的 deb 包依赖关系未记录 | P3 |

### 8.2 下一步工作

| 工作 | 预期产出 | 难度 |
|------|--------|------|
| 真实打包演练 | 端到端工作流验证 | 低 |
| Job Ballet 实现 | Mirror/Repo 同步自动化 | 中 |
| 前端优化 | Upload UI 改善、包详情展示 | 低 |
| 文档完善 | 用户指南、API 文档 | 低 |

---

## 九、文档导航

| 文档 | 内容 | 更新周期 |
|------|------|---------|
| `design.md` | 战略愿景、为何构建 APKG | 半年 |
| `current_system_design.md` | **本文档**，当前实现状态 | 每月 |
| `handoff_2025_05_26.md` | 交接指南、开发注意事项 | 交接时 |
| `pipeline_v2.md` | APT Pipeline 架构（Mirror/Repo/Bucket） | 半年 |
| `job_ballet.md` | Job 调度框架、后台任务 | 半年 |
| `local_package.md` | 本地包存储 (LocalPackages 模块) | 季度 |

---

**最后更新**：2025-05-26  
**维护者**：GitHub Copilot CLI  
**状态**：✅ 已审核、与代码实现同步
