using Aiursoft.Apkg.Client.Handlers;
using Aiursoft.Apkg.Sdk.Models;

namespace Aiursoft.Apkg.Client.Tests;

[TestClass]
public class PublishHandlerTests
{
    // ── ResolveBuildTargets ───────────────────────────────────────────────────

    [TestMethod]
    public void ResolveBuildTargets_All_CartesianProduct()
    {
        var project = new AosprojProject
        {
            TargetDistro = "ubuntu",
            TargetSuites = "jammy noble",
            TargetArchitectures = "amd64 arm64"
        };

        var result = PublishHandler.ResolveBuildTargets(project, buildAll: true, "", "", "");

        Assert.AreEqual(4, result.Count);
        Assert.IsTrue(result.Contains(("ubuntu", "jammy", "amd64")));
        Assert.IsTrue(result.Contains(("ubuntu", "jammy", "arm64")));
        Assert.IsTrue(result.Contains(("ubuntu", "noble", "amd64")));
        Assert.IsTrue(result.Contains(("ubuntu", "noble", "arm64")));
    }

    [TestMethod]
    public void ResolveBuildTargets_NoSuiteArchArgs_FallsBackToAll()
    {
        var project = new AosprojProject
        {
            TargetDistro = "debian",
            TargetSuites = "bookworm",
            TargetArchitectures = "amd64"
        };

        var result = PublishHandler.ResolveBuildTargets(project, buildAll: false, "", "", "");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(("debian", "bookworm", "amd64"), result[0]);
    }

    [TestMethod]
    public void ResolveBuildTargets_All_MissingTargetDistro_Throws()
    {
        var project = new AosprojProject
        {
            TargetDistro = "",
            TargetSuites = "jammy",
            TargetArchitectures = "amd64"
        };

        try
        {
            PublishHandler.ResolveBuildTargets(project, buildAll: true, "", "", "");
            Assert.Fail("Expected InvalidOperationException was not thrown.");
        }
        catch (InvalidOperationException ex)
        {
            Assert.IsTrue(ex.Message.Contains("TargetDistro"));
        }
    }

    [TestMethod]
    public void ResolveBuildTargets_All_MissingTargetSuites_Throws()
    {
        var project = new AosprojProject
        {
            TargetDistro = "ubuntu",
            TargetSuites = "",
            TargetArchitectures = "amd64"
        };

        try
        {
            PublishHandler.ResolveBuildTargets(project, buildAll: true, "", "", "");
            Assert.Fail("Expected InvalidOperationException was not thrown.");
        }
        catch (InvalidOperationException ex)
        {
            Assert.IsTrue(ex.Message.Contains("TargetSuites"));
        }
    }

    [TestMethod]
    public void ResolveBuildTargets_All_MissingTargetArchitectures_Throws()
    {
        var project = new AosprojProject
        {
            TargetDistro = "ubuntu",
            TargetSuites = "jammy",
            TargetArchitectures = ""
        };

        try
        {
            PublishHandler.ResolveBuildTargets(project, buildAll: true, "", "", "");
            Assert.Fail("Expected InvalidOperationException was not thrown.");
        }
        catch (InvalidOperationException ex)
        {
            Assert.IsTrue(ex.Message.Contains("TargetArchitectures"));
        }
    }

    [TestMethod]
    public void ResolveBuildTargets_ExplicitSuiteAndArch_SingleTarget()
    {
        var project = new AosprojProject
        {
            TargetDistro = "ubuntu",
            TargetSuites = "jammy",
            TargetArchitectures = "amd64"
        };

        var result = PublishHandler.ResolveBuildTargets(
            project, buildAll: false, distroArg: "", suiteArg: "noble", archArg: "arm64");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(("ubuntu", "noble", "arm64"), result[0]);
    }

    [TestMethod]
    public void ResolveBuildTargets_ExplicitSuite_MissingArch_Throws()
    {
        var project = new AosprojProject
        {
            TargetDistro = "ubuntu",
            TargetSuites = "jammy",
            TargetArchitectures = "amd64"
        };

        try
        {
            PublishHandler.ResolveBuildTargets(
                project, buildAll: false, distroArg: "", suiteArg: "jammy", archArg: "");
            Assert.Fail("Expected InvalidOperationException was not thrown.");
        }
        catch (InvalidOperationException ex)
        {
            Assert.IsTrue(ex.Message.Contains("--arch"));
        }
    }

    [TestMethod]
    public void ResolveBuildTargets_ExplicitArch_MissingSuite_Throws()
    {
        var project = new AosprojProject
        {
            TargetDistro = "ubuntu",
            TargetSuites = "jammy",
            TargetArchitectures = "amd64"
        };

        try
        {
            PublishHandler.ResolveBuildTargets(
                project, buildAll: false, distroArg: "", suiteArg: "", archArg: "amd64");
            Assert.Fail("Expected InvalidOperationException was not thrown.");
        }
        catch (InvalidOperationException ex)
        {
            Assert.IsTrue(ex.Message.Contains("--suite"));
        }
    }

    [TestMethod]
    public void ResolveBuildTargets_DistroArgOverridesProject()
    {
        var project = new AosprojProject
        {
            TargetDistro = "ubuntu",
            TargetSuites = "jammy",
            TargetArchitectures = "amd64"
        };

        var result = PublishHandler.ResolveBuildTargets(
            project, buildAll: false, distroArg: "anduinos", suiteArg: "jammy", archArg: "amd64");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(("anduinos", "jammy", "amd64"), result[0]);
    }

    [TestMethod]
    public void ResolveBuildTargets_DistroArgEmpty_ProjectDistroEmpty_FallsBackToUbuntu()
    {
        var project = new AosprojProject
        {
            TargetDistro = "",
            TargetSuites = "jammy",
            TargetArchitectures = "amd64"
        };

        var result = PublishHandler.ResolveBuildTargets(
            project, buildAll: false, distroArg: "", suiteArg: "jammy", archArg: "amd64");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(("ubuntu", "jammy", "amd64"), result[0]);
    }

    // ── DeriveVersionFromDeb ──────────────────────────────────────────────────

    [TestMethod]
    public void DeriveVersionFromDeb_SimpleVersion()
    {
        var result = PublishHandler.DeriveVersionFromDeb(
            "/some/path/my-pkg_2.0.0_jammy_amd64.deb", "my-pkg");
        Assert.AreEqual("2.0.0", result);
    }

    [TestMethod]
    public void DeriveVersionFromDeb_UpstreamVersion()
    {
        var result = PublishHandler.DeriveVersionFromDeb(
            "base-files_13ubuntu10_jammy_amd64.deb", "base-files");
        Assert.AreEqual("13ubuntu10", result);
    }

    [TestMethod]
    public void DeriveVersionFromDeb_PackageNameWithUnderscores()
    {
        // Package name contains underscores but the version is between
        // the prefix (packageName + _) and the remaining _suite_arch
        var result = PublishHandler.DeriveVersionFromDeb(
            "some_pkg_1.2.3_noble_arm64.deb", "some_pkg");
        Assert.AreEqual("1.2.3", result);
    }

    [TestMethod]
    public void DeriveVersionFromDeb_JustFileName_NoDirectory()
    {
        var result = PublishHandler.DeriveVersionFromDeb(
            "pkg_3.4.5_plucky_amd64.deb", "pkg");
        Assert.AreEqual("3.4.5", result);
    }

    // ── ParseDebFileName ──────────────────────────────────────────────────────

    [TestMethod]
    public void ParseDebFileName_Basic()
    {
        var (suite, arch) = PublishHandler.ParseDebFileName("pkg_1.0_noble_amd64.deb");
        Assert.AreEqual("noble", suite);
        Assert.AreEqual("amd64", arch);
    }

    [TestMethod]
    public void ParseDebFileName_WithPathPrefix()
    {
        var (suite, arch) = PublishHandler.ParseDebFileName("/tmp/bin/pkg_2.0_plucky_arm64.deb");
        Assert.AreEqual("plucky", suite);
        Assert.AreEqual("arm64", arch);
    }

    [TestMethod]
    public void ParseDebFileName_SuiteWithHyphen()
    {
        var (suite, arch) = PublishHandler.ParseDebFileName("pkg_1.0_jammy-updates_amd64.deb");
        Assert.AreEqual("jammy-updates", suite);
        Assert.AreEqual("amd64", arch);
    }

    [TestMethod]
    public void ParseDebFileName_NoUnderscore_Throws()
    {
        try
        {
            PublishHandler.ParseDebFileName("pkg-amd64.deb");
            Assert.Fail("Expected InvalidOperationException was not thrown.");
        }
        catch (InvalidOperationException ex)
        {
            Assert.IsTrue(ex.Message.Contains("Cannot parse"));
        }
    }

    [TestMethod]
    public void ParseDebFileName_OnlyOneUnderscore_Throws()
    {
        try
        {
            PublishHandler.ParseDebFileName("pkg_amd64.deb");
            Assert.Fail("Expected InvalidOperationException was not thrown.");
        }
        catch (InvalidOperationException ex)
        {
            Assert.IsTrue(ex.Message.Contains("Cannot parse"));
        }
    }

    [TestMethod]
    public void ParseDebFileName_NoExtension()
    {
        var (suite, arch) = PublishHandler.ParseDebFileName("mypkg_1.0_jammy_amd64.deb");
        Assert.AreEqual("jammy", suite);
        Assert.AreEqual("amd64", arch);
    }
}
