using System.CommandLine;
using System.Formats.Tar;
using System.IO.Compression;
using System.Xml.Serialization;
using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Sdk.Services;
using Aiursoft.CommandFramework.Framework;
using Aiursoft.CommandFramework.Models;
using Aiursoft.CommandFramework.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aiursoft.Apkg.Client.Handlers;

/// <summary>
/// <c>apkg publish [--path .]</c>
/// Packages all built .deb files from bin/ into a single .apkg archive ready for upload.
/// </summary>
public class PublishHandler : ExecutableCommandHandlerBuilder
{
    protected override string Name => "publish";
    protected override string Description => "Pack built .deb files into a .apkg archive for distribution.";

    private static readonly Option<string> PathOption =
        new(name: "--path", aliases: ["-p"])
        {
            Description = "Directory containing the .aosproj file (defaults to current directory).",
            DefaultValueFactory = _ => "."
        };

    private static readonly Option<string> OutputOption =
        new(name: "--output", aliases: ["-o"])
        {
            Description = "Output directory for the .apkg file (defaults to bin/ inside the project directory).",
            DefaultValueFactory = _ => string.Empty
        };

    private static readonly Option<string> BinDirOption =
        new(name: "--bin-dir")
        {
            Description = "Directory containing the built .deb files (defaults to bin/ inside the project directory).",
            DefaultValueFactory = _ => string.Empty
        };

    protected override IEnumerable<Option> GetCommandOptions() =>
    [
        PathOption,
        OutputOption,
        BinDirOption,
    ];

    protected override async Task Execute(ParseResult context)
    {
        var verbose = context.GetValue(CommonOptionsProvider.VerboseOption);
        var pathArg = context.GetValue(PathOption)!;
        var outputArg = context.GetValue(OutputOption)!;
        var binDirArg = context.GetValue(BinDirOption)!;

        var services = ServiceBuilder
            .CreateCommandHostBuilder<Startup>(verbose)
            .Build()
            .Services;

        var aosprojSerializer = services.GetRequiredService<AosprojSerializer>();
        var logger = services.GetRequiredService<ILogger<PublishHandler>>();

        var projectDir = Path.GetFullPath(pathArg);
        var projectFile = AosprojSerializer.FindProjectFile(projectDir);
        var project = await aosprojSerializer.DeserializeFromFileAsync(projectFile);

        var binDir = string.IsNullOrWhiteSpace(binDirArg)
            ? Path.Combine(projectDir, "bin")
            : Path.GetFullPath(binDirArg);

        var outputDir = string.IsNullOrWhiteSpace(outputArg)
            ? Path.Combine(projectDir, "bin")
            : Path.GetFullPath(outputArg);

        // Discover all .deb files in bin/ that match "pkgname_version_<suite>_<arch>.deb"
        if (!Directory.Exists(binDir))
            throw new DirectoryNotFoundException(
                $"bin/ directory not found at {binDir}. Run 'apkg build' first.");

        var debFiles = Directory.GetFiles(binDir, $"{project.PackageName}_*.deb");
        if (debFiles.Length == 0)
            throw new FileNotFoundException(
                $"No .deb files found in {binDir} matching '{project.PackageName}_*.deb'. Run 'apkg build' first.");

        logger.LogInformation("Found {Count} .deb file(s) to pack:", debFiles.Length);

        var entries = new List<ApkgPackageEntry>();
        foreach (var debPath in debFiles)
        {
            var debFileName = Path.GetFileName(debPath);
            var (suite, arch) = ParseDebFileName(debFileName, project.PackageName, project.PackageVersion);
            var distro = project.TargetDistro;

            entries.Add(new ApkgPackageEntry
            {
                DebFile = debFileName,
                Distro = distro,
                Suite = suite,
                Component = project.Component
            });

            logger.LogInformation("  + {File} → {Distro}/{Suite}", debFileName, distro, suite);
        }

        // Build manifest
        var manifest = new ApkgPackageManifest
        {
            Name = project.PackageName,
            Version = project.PackageVersion,
            Maintainer = string.IsNullOrWhiteSpace(project.Maintainer) ? project.PackageAuthors : project.Maintainer,
            Description = project.PackageDescription,
            Homepage = project.PackageHomepage,
            License = project.LicenseType,
            Entries = entries
        };

        // Serialize manifest to XML string
        var manifestXml = SerializeManifest(manifest);

        // Write .apkg (tar.gz)
        Directory.CreateDirectory(outputDir);
        var apkgFileName = $"{project.PackageName}.{project.PackageVersion}.apkg";
        var apkgPath = Path.Combine(outputDir, apkgFileName);

        logger.LogInformation("Writing {File}...", apkgFileName);

        await using (var fileStream = new FileStream(apkgPath, FileMode.Create, FileAccess.Write))
        await using (var gzip = new GZipStream(fileStream, CompressionLevel.Optimal))
        await using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: false))
        {
            // Write manifest.xml
            var manifestBytes = System.Text.Encoding.UTF8.GetBytes(manifestXml);
            var manifestEntry = new PaxTarEntry(TarEntryType.RegularFile, "manifest.xml")
            {
                DataStream = new MemoryStream(manifestBytes)
            };
            await tar.WriteEntryAsync(manifestEntry);
            logger.LogDebug("  + manifest.xml");

            // Write deb files
            foreach (var debPath in debFiles)
            {
                var debFileName = Path.GetFileName(debPath);
                await tar.WriteEntryAsync(debPath, debFileName);
                logger.LogDebug("  + {File}", debFileName);
            }
        }

        logger.LogInformation("Done! Created {ApkgPath}", apkgPath);
        logger.LogInformation("To upload: apkg push --file {File} --source <URL> --api-key <KEY>", apkgPath);
    }

    private static (string suite, string arch) ParseDebFileName(string fileName, string packageName, string version)
    {
        // Expected: pkgname_version_suite_arch.deb
        var withoutExt = Path.GetFileNameWithoutExtension(fileName);
        var prefix = $"{packageName}_{version}_";
        if (!withoutExt.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Cannot parse suite/arch from deb filename '{fileName}'. Expected format: {packageName}_{version}_<suite>_<arch>.deb");

        var rest = withoutExt[prefix.Length..];
        var lastUnderscore = rest.LastIndexOf('_');
        if (lastUnderscore < 0)
            throw new InvalidOperationException(
                $"Cannot parse suite/arch from deb filename '{fileName}'. Expected format: {packageName}_{version}_<suite>_<arch>.deb");

        return (suite: rest[..lastUnderscore], arch: rest[(lastUnderscore + 1)..]);
    }

    private static string SerializeManifest(ApkgPackageManifest manifest)
    {
        var serializer = new XmlSerializer(typeof(ApkgPackageManifest));
        using var sw = new System.IO.StringWriter();
        serializer.Serialize(sw, manifest);
        return sw.ToString();
    }
}
