using System.CommandLine;
using Aiursoft.Apkg.Sdk.Services;
using Aiursoft.CommandFramework.Framework;
using Aiursoft.CommandFramework.Models;
using Aiursoft.CommandFramework.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aiursoft.Apkg.Client.Handlers;

public class LintHandler : ExecutableCommandHandlerBuilder
{
    protected override string Name => "lint";
    protected override string Description => "Validate an .aosproj project file for correctness.";

    private static readonly Option<string> PathOption =
        new(name: "--path", aliases: ["-p"])
        {
            Description = "Directory containing the .aosproj file (defaults to current directory).",
            DefaultValueFactory = _ => "."
        };

    protected override IEnumerable<Option> GetCommandOptions() => [PathOption];

    protected override async Task Execute(ParseResult context)
    {
        var verbose = context.GetValue(CommonOptionsProvider.VerboseOption);
        var pathArg = context.GetValue(PathOption)!;

        var services = ServiceBuilder
            .CreateCommandHostBuilder<Startup>(verbose)
            .Build()
            .Services;

        var aosprojSerializer = services.GetRequiredService<AosprojSerializer>();
        var linter = services.GetRequiredService<AosprojLinter>();
        var logger = services.GetRequiredService<ILogger<LintHandler>>();

        var projectDir = Path.GetFullPath(pathArg);
        var projectFile = AosprojSerializer.FindProjectFile(projectDir);
        logger.LogInformation("Linting {File}", projectFile);

        var project = await aosprojSerializer.DeserializeFromFileAsync(projectFile);
        var issues = linter.Lint(project, projectDir);

        if (issues.Count == 0)
        {
            logger.LogInformation("✓ No issues found.");
            return;
        }

        var hasErrors = false;
        foreach (var issue in issues)
        {
            if (issue.Level == AosprojLinter.Severity.Error)
            {
                logger.LogError("  ✗ {Message}", issue.Message);
                hasErrors = true;
            }
            else
            {
                logger.LogWarning("  ⚠ {Message}", issue.Message);
            }
        }

        if (hasErrors)
            throw new InvalidOperationException($"Linting failed with {issues.Count(i => i.Level == AosprojLinter.Severity.Error)} error(s).");

        logger.LogWarning("Lint complete with {Count} warning(s).", issues.Count);
    }
}
