# Apkg

[![MIT licensed](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/aiursoftweb/apkg/blob/master/LICENSE)
[![Pipeline stat](https://gitlab.aiursoft.com/aiursoft/apkg/badges/master/pipeline.svg)](https://gitlab.aiursoft.com/aiursoft/apkg/-/pipelines)
[![Test Coverage](https://gitlab.aiursoft.com/aiursoft/apkg/badges/master/coverage.svg)](https://gitlab.aiursoft.com/aiursoft/apkg/-/pipelines)
[![Website](https://img.shields.io/website?url=https%3A%2F%2Fapkg.aiursoft.com)](https://apkg.aiursoft.com)
[![Docker](https://img.shields.io/docker/pulls/aiursoft/apkg.svg)](https://hub.docker.com/r/aiursoft/apkg)

Apkg is an APT repository platform and packaging toolchain for Debian-compatible distributions. It hosts signed APT repositories, mirrors upstream package indexes, accepts developer-published `.apkg` bundles, and exposes packages through ordinary `apt update` and `apt install` workflows.

It is designed for meta-distribution work such as AnduinOS: keep Ubuntu as an upstream source, publish distribution-specific package overrides, sign the final repository snapshot, and serve a stable APT endpoint to machines and image builders.

![Apkg dashboard](./screenshot.png)

## What Apkg Provides

- **APT repository hosting**: serve `InRelease`, `Release`, `Packages.gz`, `Contents`, GPG keys, and `.deb` pool files from standard Debian/Ubuntu-style URLs.
- **Upstream mirror ingestion**: synchronize package metadata from upstream APT mirrors such as Ubuntu archives.
- **Repository snapshots**: build immutable buckets and atomically promote only signed snapshots, so APT clients never see half-built metadata.
- **Package override pipeline**: upload local `.deb` packages and let them replace upstream packages in the final repository by package name and architecture.
- **`.aosproj` build format**: describe Debian package contents, maintainer scripts, systemd units, dependencies, suites, and architectures in a project file.
- **`.apkg` bundle publishing**: pack one or more `.deb` files plus `manifest.xml` into a portable archive that can be pushed to any Apkg server.
- **CLI tooling**: create, lint, build, publish, push, unpack, install, and add APT sources from a `dotnet` global tool.
- **Web administration**: manage users, roles, permissions, repositories, upstream mirrors, signing certificates, package uploads, API keys, jobs, and global settings.
- **Background operations**: scheduled mirror sync, repository sync, signing, static export, garbage collection, and temporary file cleanup.
- **Authentication options**: local accounts by default, with optional OpenID Connect integration and API key authentication for CI/CD.

## Core Concepts

| Concept | Purpose |
|---------|---------|
| `AptMirror` | Upstream repository definition, such as Ubuntu `questing` from `archive.ubuntu.com`. |
| `AptRepository` | Repository served to clients. It has its own distro, suite, components, architecture list, and signing certificate. |
| `AptBucket` | Immutable snapshot of repository metadata. Primary buckets are visible to APT clients; secondary buckets are build/sign staging areas. |
| `AptPackage` | One package entry inside a bucket, including Debian control metadata, checksums, filename, and lazy-download state. |
| `ApkgPackage` | A logical package family owned by a user, identified by name, distro, and component. |
| `ApkgRevision` | One upload event created by `apkg push` or the web upload flow. |
| `ApkgDebPackage` | User-uploaded `.deb` package that can override upstream packages during repository sync. |
| `AptCertificate` | GPG key pair used to sign repository metadata. |

The central pipeline is:

```text
Upstream APT mirror
  -> MirrorSyncJob
  -> AptMirror bucket
  -> RepositorySyncJob
  -> AptRepository secondary bucket
  -> RepositorySignJob
  -> AptRepository primary bucket
  -> APT clients
```

Only the repository primary bucket is exposed through APT artifact endpoints. This keeps clients on a consistent signed snapshot even while sync, signing, and garbage collection jobs are running.

## Default Startup Behavior

When the web application starts, it:

1. Builds the ASP.NET Core application.
2. Initializes ClickHouse logging infrastructure if enabled.
3. Applies database migrations.
4. Seeds global settings.
5. Creates the default administrator if the database has no users or roles.
6. Creates an `Administrators` role and grants all application permissions.
7. Generates a default GPG signing certificate if none exists.
8. Seeds default Ubuntu mirrors and AnduinOS repositories for `questing`, `questing-updates`, `questing-backports`, and `questing-security`.
9. Copies the default avatar into the configured storage folder.
10. Starts the web server and scheduled background jobs.

Default local credentials:

| Field | Value |
|-------|-------|
| User name | `admin` |
| Email | `admin@default.com` |
| Password | `Admin@123456!` |

Change the default password immediately after first login on any real deployment.

## Storage and Database

Default development configuration:

| Setting | Default |
|---------|---------|
| Database provider | SQLite |
| Database file | `app.db` |
| Storage path | `/tmp/data` |
| Static export path | `/tmp/export` |
| Authentication provider | Local |
| ClickHouse logging | Disabled |

Docker rewrites the app configuration so persistent data lives under `/data`.

Apkg stores package binaries in content-addressed storage where appropriate and keeps repository metadata in bucket folders. Garbage collection removes bucket and object files that are no longer referenced by active mirrors, repositories, or uploaded packages.

## Install the CLI

Prerequisite: [.NET 10 SDK](https://dotnet.microsoft.com/download)

```bash
dotnet tool install --global Aiursoft.Apkg.Client --add-source https://nuget.aiursoft.com/v3/index.json
```

Verify the tool:

```bash
apkg --help
```

The CLI commands are:

| Command | Purpose |
|---------|---------|
| `apkg new` | Create a new `.aosproj` package project. |
| `apkg add` | Add files, folders, scripts, conffiles, or other project entries. |
| `apkg lint` | Validate an `.aosproj` project. |
| `apkg build` | Build `.deb` packages from an `.aosproj`. |
| `apkg publish` | Build and pack generated `.deb` files into a `.apkg` archive. |
| `apkg push` | Upload a `.apkg` archive to an Apkg server. |
| `apkg unpack` | Extract a `.apkg` archive for inspection. |
| `apkg install` | Install a package archive locally with `dpkg`. |
| `apkg add-source` | Add an Apkg repository to the local APT source list. |

## Package Workflow

```bash
# 1. Create a package project
apkg new --name my-package

# 2. Add package content
apkg add --path ./my-package --file ./bin/my-tool --target /usr/bin/my-tool

# 3. Validate the project
apkg lint --path ./my-package

# 4. Build .deb files and publish a .apkg bundle
apkg publish --path ./my-package

# 5. Push to a server
apkg push ./my-package/bin/my-package.apkg --source https://apkg.example.com --api-key <your-api-key>
```

After repository sync and signing jobs run, clients install the package through normal APT commands.

## Add an APT Source

Each repository exposes a machine-readable source configuration:

```bash
sudo apkg add-source https://apkg.example.com/api/sources/1
sudo apt update
sudo apt install my-package
```

The `add-source` command downloads repository metadata, configures the `.sources` file, and installs the GPG key when signing is enabled.

## Run Manually

Requirements:

1. Install [.NET 10 SDK](https://dotnet.microsoft.com/download).
2. Install [Node.js](https://nodejs.org/).
3. Restore frontend packages:

```bash
npm install --prefix src/Aiursoft.Apkg/wwwroot
```

Run the web application:

```bash
dotnet run --project src/Aiursoft.Apkg/Aiursoft.Apkg.csproj
```

Then open `http://localhost:5000`.

## Run in Docker

```bash
image=aiursoft/apkg
appName=apkg
sudo docker pull $image
sudo docker run -d \
  --name $appName \
  --restart unless-stopped \
  -p 5000:5000 \
  -v /var/www/$appName:/data \
  $image
```

Docker runtime layout:

| Item | Path |
|------|------|
| Application | `/app` |
| Persistent data | `/data` |
| Configuration | `/data/appsettings.json` |
| HTTP port | `5000` |

## Run on Ubuntu with the Install Script

The install script builds and registers Apkg as a systemd service:

```bash
curl -sL https://github.com/aiursoftweb/apkg/raw/master/install.sh | sudo bash
```

Optionally specify a port:

```bash
curl -sL https://github.com/aiursoftweb/apkg/raw/master/install.sh | sudo bash -s 8080
```

The script installs prerequisites, clones the repository, publishes the web app, registers a systemd service, and starts it.

## Documentation

- [Design document](docs/design.md)
- [`.aosproj` format](docs/aosproj.md)
- [Development notes](docs/development.md)
- [Operations guide](docs/operations.md)

## Contributing

Issues, pull requests, and design discussions are welcome. Keep changes focused, include tests for behavioral changes, and avoid bundling unrelated formatting or refactors with feature work.

## License

Apkg is licensed under the [MIT License](LICENSE).
