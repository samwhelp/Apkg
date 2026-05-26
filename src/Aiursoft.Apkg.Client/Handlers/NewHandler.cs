using System.CommandLine;
using Aiursoft.CommandFramework.Framework;
using Aiursoft.CommandFramework.Models;
using Aiursoft.CommandFramework.Services;

namespace Aiursoft.Apkg.Client.Handlers;

public class NewHandler : ExecutableCommandHandlerBuilder
{
    protected override string Name => "new";
    protected override string Description => "Create a new Apkg package manifest in the current directory.";

    private static readonly Option<string> NameOption =
        new(name: "--name", aliases: ["-n"])
        {
            Description = "The name of the new package.",
            Required = true
        };

    private static readonly Option<string> OutputOption =
        new(name: "--output", aliases: ["-o"])
        {
            Description = "The output directory where the package manifest will be created.",
            DefaultValueFactory = _ => "."
        };

    protected override IEnumerable<Option> GetCommandOptions() =>
    [
        NameOption,
        OutputOption,
    ];

    protected override Task Execute(ParseResult context)
    {
        var verbose = context.GetValue(CommonOptionsProvider.VerboseOption);

        ServiceBuilder
            .CreateCommandHostBuilder<Startup>(verbose)
            .Build();

        // TODO: Implement package manifest creation logic.
        throw new NotImplementedException();
    }
}
