using Aiursoft.Apkg.Sdk.Models;

namespace Aiursoft.Apkg.Sdk.Tests;

[TestClass]
public class AosprojProjectTests
{
    [TestMethod]
    public void Defaults_PackageVersionIsSet()
    {
        var project = new AosprojProject();
        Assert.AreEqual("1.0.0", project.PackageVersion);
    }

    [TestMethod]
    public void Defaults_ComponentIsSet()
    {
        var project = new AosprojProject();
        Assert.AreEqual("main", project.Component);
    }

    [TestMethod]
    public void Defaults_LicenseTypeIsSet()
    {
        var project = new AosprojProject();
        Assert.AreEqual("MIT", project.LicenseType);
    }

    [TestMethod]
    public void Defaults_TargetDistroIsSet()
    {
        var project = new AosprojProject();
        Assert.AreEqual("ubuntu", project.TargetDistro);
    }

    [TestMethod]
    public void Defaults_TargetArchitecturesIsSet()
    {
        var project = new AosprojProject();
        Assert.AreEqual("amd64", project.TargetArchitectures);
    }

    [TestMethod]
    public void Defaults_AllCollectionsAreInitialized()
    {
        var project = new AosprojProject();
        Assert.IsNotNull(project.Dependencies);
        Assert.IsNotNull(project.PrebuildCommands);
        Assert.IsNotNull(project.IncludeFiles);
        Assert.IsNotNull(project.IncludeFolders);
        Assert.IsNotNull(project.IncludeScripts);
        Assert.IsNotNull(project.ConfFiles);
        Assert.IsNotNull(project.PostInstallScripts);
        Assert.IsNotNull(project.PreRemoveScripts);
        Assert.IsNotNull(project.SystemdUnits);
    }

    [TestMethod]
    public void SuiteList_SplitsBySpace()
    {
        var project = new AosprojProject { TargetSuites = "jammy noble plucky" };
        var suites = project.SuiteList;
        Assert.AreEqual(3, suites.Length);
        Assert.AreEqual("jammy", suites[0]);
        Assert.AreEqual("noble", suites[1]);
        Assert.AreEqual("plucky", suites[2]);
    }

    [TestMethod]
    public void SuiteList_SplitsByComma()
    {
        var project = new AosprojProject { TargetSuites = "jammy,noble,plucky" };
        var suites = project.SuiteList;
        Assert.AreEqual(3, suites.Length);
    }

    [TestMethod]
    public void SuiteList_HandlesMixedSeparators()
    {
        var project = new AosprojProject { TargetSuites = "jammy noble,plucky" };
        var suites = project.SuiteList;
        Assert.AreEqual(3, suites.Length);
    }

    [TestMethod]
    public void SuiteList_TrimsEntries()
    {
        var project = new AosprojProject { TargetSuites = "  jammy  ,  noble  " };
        var suites = project.SuiteList;
        Assert.AreEqual("jammy", suites[0]);
        Assert.AreEqual("noble", suites[1]);
    }

    [TestMethod]
    public void SuiteList_EmptyString_ReturnsEmptyArray()
    {
        var project = new AosprojProject { TargetSuites = "" };
        Assert.AreEqual(0, project.SuiteList.Length);
    }

    [TestMethod]
    public void ArchList_SplitsBySpace()
    {
        var project = new AosprojProject { TargetArchitectures = "amd64 arm64 riscv64" };
        var archs = project.ArchList;
        Assert.AreEqual(3, archs.Length);
    }

    [TestMethod]
    public void ArchList_DefaultValue()
    {
        var project = new AosprojProject();
        var archs = project.ArchList;
        Assert.AreEqual(1, archs.Length);
        Assert.AreEqual("amd64", archs[0]);
    }

    [TestMethod]
    public void TargetDistro_IsSingular()
    {
        var project = new AosprojProject { TargetDistro = "anduinos" };
        Assert.AreEqual("anduinos", project.TargetDistro);
    }

    [TestMethod]
    public void Dependencies_CanAddConditionalValues()
    {
        var project = new AosprojProject();
        project.Dependencies.Add(new ConditionalValue
        {
            Value = "libc6",
            Condition = "'$(Suite)' == 'jammy'"
        });
        project.Dependencies.Add(new ConditionalValue
        {
            Value = "libssl3",
            Condition = null
        });

        Assert.AreEqual(2, project.Dependencies.Count);
        Assert.AreEqual("libc6", project.Dependencies[0].Value);
        Assert.AreEqual("'$(Suite)' == 'jammy'", project.Dependencies[0].Condition);
        Assert.IsNull(project.Dependencies[1].Condition);
    }

    [TestMethod]
    public void SystemdUnit_DefaultsToAutoEnable()
    {
        var unit = new SystemdUnitItem { Source = "myapp.service" };
        Assert.IsTrue(unit.AutoEnable);
    }

    [TestMethod]
    public void IncludeScriptItem_InheritsBaseItem()
    {
        var item = new IncludeScriptItem
        {
            Source = "scripts/deploy.sh",
            Target = "/usr/bin/deploy",
            Condition = "'$(Arch)' == 'amd64'"
        };
        Assert.AreEqual("scripts/deploy.sh", item.Source);
        Assert.AreEqual("/usr/bin/deploy", item.Target);
        Assert.AreEqual("'$(Arch)' == 'amd64'", item.Condition);
    }

    // ── UpstreamSource ─────────────────────────────────────────────────────────

    [TestMethod]
    public void HasUpstreamSource_WhenUpstreamPackageIsSet_ReturnsTrue()
    {
        var project = new AosprojProject { UpstreamPackage = "base-files" };
        Assert.IsTrue(project.HasUpstreamSource);
    }

    [TestMethod]
    public void HasUpstreamSource_WhenUpstreamPackageIsEmpty_ReturnsFalse()
    {
        var project = new AosprojProject();
        Assert.IsFalse(project.HasUpstreamSource);
    }

    [TestMethod]
    public void HasUpstreamSource_WhenUpstreamPackageIsWhitespace_ReturnsFalse()
    {
        var project = new AosprojProject { UpstreamPackage = "  " };
        Assert.IsFalse(project.HasUpstreamSource);
    }

    [TestMethod]
    public void Defaults_UpstreamComponentIsMain()
    {
        var project = new AosprojProject();
        Assert.AreEqual("main", project.UpstreamComponent);
    }

    [TestMethod]
    public void Defaults_UpstreamArchIsAll()
    {
        var project = new AosprojProject();
        Assert.AreEqual("all", project.UpstreamArch);
    }

    [TestMethod]
    public void Defaults_UpstreamFieldsAreEmpty()
    {
        var project = new AosprojProject();
        Assert.AreEqual("", project.UpstreamUrl);
        Assert.AreEqual("", project.UpstreamDistro);
        Assert.AreEqual("", project.UpstreamPackage);
        Assert.AreEqual("", project.UpstreamSuite);
    }
}
