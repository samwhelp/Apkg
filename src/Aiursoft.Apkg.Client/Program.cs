using Aiursoft.Apkg.Client.Handlers;
using Aiursoft.CommandFramework;
using Aiursoft.CommandFramework.Models;

return await new NestedCommandApp()
    .WithGlobalOptions(CommonOptionsProvider.VerboseOption)
    .WithFeature(new NewHandler())
    .WithFeature(new PackHandler())
    .RunAsync(args);
