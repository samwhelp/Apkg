using System.CommandLine;
using Aiursoft.CommandFramework.Framework;
using Aiursoft.CommandFramework.Models;
using Aiursoft.CommandFramework.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Aiursoft.Apkg.Client.Handlers;

public class PackHandler : ExecutableCommandHandlerBuilder
{
    protected override string Name => "pack";
    protected override string Description => "Pack a package from a source directory into an .apkg file.";

    private static readonly Option<string> PathOption =
        new(name: "--path", aliases: ["-p"])
        {
            Description = "The path to the package source directory.",
            Required = true
        };

    private static readonly Option<string> OutputOption =
        new(name: "--output", aliases: ["-o"])
        {
            Description = "The output directory for the packed .apkg file.",
            DefaultValueFactory = _ => "."
        };

    protected override IEnumerable<Option> GetCommandOptions() =>
    [
        PathOption,
        OutputOption,
    ];

    protected override Task Execute(ParseResult context)
    {
        var verbose = context.GetValue(CommonOptionsProvider.VerboseOption);
        var path = context.GetValue(PathOption)!;
        var output = context.GetValue(OutputOption)!;

        var services = ServiceBuilder
            .CreateCommandHostBuilder<Startup>(verbose)
            .Build()
            .Services;

        // TODO: Implement package packing logic.
        throw new NotImplementedException();
    }
}
