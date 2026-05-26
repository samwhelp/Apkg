using Aiursoft.Apkg.Client.Handlers;
using Aiursoft.CommandFramework;
using Aiursoft.CommandFramework.Models;

namespace Aiursoft.Apkg.Client.Tests;

[TestClass]
public class IntegrationTests
{
    private NestedCommandApp Program => new NestedCommandApp()
        .WithGlobalOptions(CommonOptionsProvider.VerboseOption)
        .WithFeature(new NewHandler())
        .WithFeature(new PackHandler());

    [TestMethod]
    public async Task InvokeHelp()
    {
        var result = await Program.TestRunAsync(["--help"]);

        Assert.AreEqual(0, result.ProgramReturn);
        Assert.IsTrue(string.IsNullOrWhiteSpace(result.StdErr));
    }

    [TestMethod]
    public async Task InvokeVersion()
    {
        var result = await Program.TestRunAsync(["--version"]);

        Assert.AreEqual(0, result.ProgramReturn);
    }

    [TestMethod]
    public async Task InvokeUnknown()
    {
        var result = await Program.TestRunAsync(["--wtf"]);

        Assert.AreEqual(1, result.ProgramReturn);
    }

    [TestMethod]
    public async Task InvokeWithoutArg()
    {
        var result = await Program.TestRunAsync([]);

        Assert.AreEqual(1, result.ProgramReturn);
    }

    [TestMethod]
    public async Task InvokeNewHelp()
    {
        var result = await Program.TestRunAsync(["new", "--help"]);

        Assert.AreEqual(0, result.ProgramReturn);
        Assert.IsTrue(result.StdOut.Contains("--name"));
        Assert.IsTrue(string.IsNullOrWhiteSpace(result.StdErr));
    }

    [TestMethod]
    public async Task InvokePackHelp()
    {
        var result = await Program.TestRunAsync(["pack", "--help"]);

        Assert.AreEqual(0, result.ProgramReturn);
        Assert.IsTrue(result.StdOut.Contains("--path"));
        Assert.IsTrue(string.IsNullOrWhiteSpace(result.StdErr));
    }
}
