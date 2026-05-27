# Apkg 运维指南

## 快速开发启动

```bash
# 安装并注册 systemd 服务到指定端口
./install.sh 5000
```

克隆仓库 → 安装 .NET + Node 依赖 → 发布到 `/opt/apps/apkg` → 注册 systemd 服务。

## 默认管理员凭据

首次运行（空数据库）时，`Program.SeedAsync()` (ProgramExtends.cs:57) 自动创建：

- **用户名**: `admin`
- **密码**: `Admin@123456!`
- **角色**: Administrators（拥有所有权限）

**生产环境请立即修改。**

## 数据库配置

`appsettings.json` → `ConnectionStrings`:

```json
// SQLite（默认，本地开发）:
"DbType": "Sqlite",
"DefaultConnection": "DataSource=app.db;Cache=Shared"

// MySQL（生产）:
"DbType": "MySql",
"DefaultConnection": "Server=localhost;Database=apkg;Uid=apkg;Pwd=..."
```

使用 `Aiursoft.DbTools.Switchable` — `DbType` 决定启动时加载哪个 Provider。单元测试自动切换到 InMemory。

## 认证方式

```json
"AppSettings": {
  "AuthProvider": "Local",  // 或 "OIDC"
  "OIDC": { ... },
  "Local": {
    "AllowRegister": true,
    "AllowWeakPassword": true
  }
}
```

Local 和 OIDC 互斥。切换认证方式需清理现有用户会话。

## 存储路径

```json
"Storage": {
  "Path": "/tmp/data"  // 生产环境改为持久化路径
}
```

包含: LocalPackages、CAS objects（`Objects/{sha256[..2]}/{sha256}.deb`）、用户头像、GPG 密钥环。

## Docker

```bash
docker build -t apkg .
docker run -d -p 5000:5000 -v /srv/apkg:/data apkg
```

关键 Dockerfile 细节：
- **基础镜像**: `dotnetonlyruntime`（仅 ASP.NET 运行时）
- **运行时依赖**: `gnupg ubuntu-keyring`（GPG 签名 + 上游密钥验证）
- **Volume**: `/data` — 持久化数据库、包文件、GPG 密钥
- **配置 Symlink**: 首次运行时 `appsettings.json` 从 `/app` 复制到 `/data` 并建立软链接，容器重建后配置不丢失
- **HEALTHCHECK**: `wget --spider http://localhost:5000/health`，间隔 10s，3 次重试，180s 启动宽限期
- **端口**: 5000

## Lint

```bash
./lint.sh
```

运行 JetBrains ReSharper Global Tools (`jb inspectcode`)。需先安装 `dotnet tool install JetBrains.ReSharper.GlobalTools`。过滤已知误报：InconsistentNaming、AssignNullToNotNullAttribute、UnusedAutoPropertyAccessor、DuplicateResource、NotOverriddenInSpecificCulture。任何 `WARNING` 或 `ERROR` 级别的问题都会导致构建失败。

## 生产环境证书

每个 AptRepository 有独立 GPG 签名密钥。生产环境私钥可托管到外部服务（HashiCorp Vault、Azure Key Vault）— Apkg 通过网络调用签名 API，全程不接触私钥。

种子数据（`Program.SeedMirrorsAsync()`, ProgramExtends.cs:122）自动生成默认 GPG 证书（名称: "anduinos"，邮箱: support@aiursoft.com）。生产环境应替换为真实证书。
