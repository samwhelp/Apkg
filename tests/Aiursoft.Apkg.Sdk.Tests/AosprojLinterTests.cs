using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Sdk.Services;

namespace Aiursoft.Apkg.Sdk.Tests;

[TestClass]
public class AosprojLinterTests
{
    private readonly AosprojLinter _linter;

    public AosprojLinterTests()
    {
        _linter = new AosprojLinter(new ConditionEvaluator());
    }

    [TestMethod]
    public void Lint_ValidProject_NoIssues()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "app"), "binary");

            var project = new AosprojProject
            {
                PackageName = "my-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "A test package",
                TargetSuites = "jammy",
                Maintainer = "Test <test@example.com>",
                IncludeFiles =
                {
                    new IncludeFileItem { Source = "app", Target = "/usr/bin/app" }
                }
            };

            var issues = _linter.Lint(project, dir);
            Assert.AreEqual(0, issues.Count, $"Expected 0 issues but got: {string.Join("; ", issues.Select(i => i.Message))}");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Lint_MissingPackageName_Error()
    {
        var dir = CreateTempDir();
        try
        {
            var project = new AosprojProject
            {
                PackageName = "",
                PackageVersion = "1.0.0",
                PackageDescription = "desc",
                TargetSuites = "jammy"
            };

            var issues = _linter.Lint(project, dir);
            Assert.IsTrue(issues.Any(i => i.Message.Contains("PackageName")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Lint_MissingPackageVersion_Error()
    {
        var dir = CreateTempDir();
        try
        {
            var project = new AosprojProject
            {
                PackageName = "my-pkg",
                PackageVersion = "",
                PackageDescription = "desc",
                TargetSuites = "jammy"
            };

            var issues = _linter.Lint(project, dir);
            Assert.IsTrue(issues.Any(i => i.Message.Contains("PackageVersion")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Lint_MissingPackageDescription_Error()
    {
        var dir = CreateTempDir();
        try
        {
            var project = new AosprojProject
            {
                PackageName = "my-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "",
                TargetSuites = "jammy"
            };

            var issues = _linter.Lint(project, dir);
            Assert.IsTrue(issues.Any(i => i.Message.Contains("PackageDescription")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Lint_MissingTargetSuites_Error()
    {
        var dir = CreateTempDir();
        try
        {
            var project = new AosprojProject
            {
                PackageName = "my-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "desc",
                TargetSuites = ""
            };

            var issues = _linter.Lint(project, dir);
            Assert.IsTrue(issues.Any(i => i.Message.Contains("TargetSuites")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Lint_NoMaintainerOrAuthors_Warning()
    {
        var dir = CreateTempDir();
        try
        {
            var project = new AosprojProject
            {
                PackageName = "my-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "desc",
                TargetSuites = "jammy",
                Maintainer = "",
                PackageAuthors = ""
            };

            var issues = _linter.Lint(project, dir);
            Assert.IsTrue(issues.Any(i => i.Message.Contains("Maintainer") && i.Message.Contains("PackageAuthors")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Lint_HasMaintainer_NoAuthorsWarning()
    {
        var dir = CreateTempDir();
        try
        {
            var project = new AosprojProject
            {
                PackageName = "my-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "desc",
                TargetSuites = "jammy",
                Maintainer = "Somebody <somebody@example.com>",
                PackageAuthors = ""
            };

            var issues = _linter.Lint(project, dir);
            Assert.IsFalse(issues.Any(i => i.Message.Contains("Maintainer") && i.Message.Contains("PackageAuthors")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Lint_HasAuthors_NoMaintainerWarning()
    {
        var dir = CreateTempDir();
        try
        {
            var project = new AosprojProject
            {
                PackageName = "my-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "desc",
                TargetSuites = "jammy",
                PackageAuthors = "Alice <alice@example.com>",
                Maintainer = ""
            };

            var issues = _linter.Lint(project, dir);
            Assert.IsFalse(issues.Any(i => i.Message.Contains("Maintainer") && i.Message.Contains("PackageAuthors")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Lint_ValidPackageName_Accepted()
    {
        var dir = CreateTempDir();
        try
        {
            var project = new AosprojProject
            {
                PackageName = "my-pkg99",
                PackageVersion = "1.0.0",
                PackageDescription = "desc",
                TargetSuites = "jammy",
                Maintainer = "Test <test@example.com>"
            };

            var issues = _linter.Lint(project, dir);
            Assert.IsFalse(issues.Any(i => i.Message.Contains("not a valid Debian package name")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Lint_InvalidPackageNameUppercase_Error()
    {
        var dir = CreateTempDir();
        try
        {
            var project = new AosprojProject
            {
                PackageName = "My-Pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "desc",
                TargetSuites = "jammy",
                Maintainer = "Test <test@example.com>"
            };

            var issues = _linter.Lint(project, dir);
            Assert.IsTrue(issues.Any(i => i.Message.Contains("not a valid Debian package name")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Lint_InvalidPackageNameUnderscore_Error()
    {
        var dir = CreateTempDir();
        try
        {
            var project = new AosprojProject
            {
                PackageName = "my_pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "desc",
                TargetSuites = "jammy",
                Maintainer = "Test <test@example.com>"
            };

            var issues = _linter.Lint(project, dir);
            Assert.IsTrue(issues.Any(i => i.Message.Contains("not a valid Debian package name")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Lint_InvalidPackageNameLeadingHyphen_Error()
    {
        var dir = CreateTempDir();
        try
        {
            var project = new AosprojProject
            {
                PackageName = "-mypkg",
                PackageVersion = "1.0.0",
                PackageDescription = "desc",
                TargetSuites = "jammy",
                Maintainer = "Test <test@example.com>"
            };

            var issues = _linter.Lint(project, dir);
            Assert.IsTrue(issues.Any(i => i.Message.Contains("not a valid Debian package name")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Lint_PackageNameWithPlus_Accepted()
    {
        var dir = CreateTempDir();
        try
        {
            var project = new AosprojProject
            {
                PackageName = "libc++abi1",
                PackageVersion = "1.0.0",
                PackageDescription = "desc",
                TargetSuites = "jammy",
                Maintainer = "Test <test@example.com>"
            };

            var issues = _linter.Lint(project, dir);
            Assert.IsFalse(issues.Any(i => i.Message.Contains("not a valid Debian package name")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Lint_MissingSource_Warning()
    {
        var dir = CreateTempDir();
        try
        {
            var project = new AosprojProject
            {
                PackageName = "my-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "desc",
                TargetSuites = "jammy",
                Maintainer = "Test <test@example.com>",
                IncludeFiles =
                {
                    new IncludeFileItem { Source = "nonexistent-file.txt", Target = "/opt/file.txt" }
                }
            };

            var issues = _linter.Lint(project, dir);
            Assert.IsTrue(issues.Any(i => i.Message.Contains("nonexistent-file.txt") && i.Message.Contains("not found")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Lint_MissingFolderSource_Warning()
    {
        var dir = CreateTempDir();
        try
        {
            var project = new AosprojProject
            {
                PackageName = "my-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "desc",
                TargetSuites = "jammy",
                Maintainer = "Test <test@example.com>",
                IncludeFolders =
                {
                    new IncludeFolderItem { Source = "nonexistent-dir", Target = "/opt/data" }
                }
            };

            var issues = _linter.Lint(project, dir);
            Assert.IsTrue(issues.Any(i => i.Message.Contains("nonexistent-dir") && i.Message.Contains("not found")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Lint_ExistingSourceFile_NoWarning()
    {
        var dir = CreateTempDir();
        try
        {
            var srcFile = Path.Combine(dir, "real-file.txt");
            File.WriteAllText(srcFile, "content");

            var project = new AosprojProject
            {
                PackageName = "my-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "desc",
                TargetSuites = "jammy",
                Maintainer = "Test <test@example.com>",
                IncludeFiles =
                {
                    new IncludeFileItem { Source = "real-file.txt", Target = "/opt/file.txt" }
                }
            };

            var issues = _linter.Lint(project, dir);
            Assert.IsFalse(issues.Any(i => i.Message.Contains("not found")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Lint_ExistingFolderSource_NoWarning()
    {
        var dir = CreateTempDir();
        try
        {
            var srcDir = Path.Combine(dir, "real-dir");
            Directory.CreateDirectory(srcDir);

            var project = new AosprojProject
            {
                PackageName = "my-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "desc",
                TargetSuites = "jammy",
                Maintainer = "Test <test@example.com>",
                IncludeFolders =
                {
                    new IncludeFolderItem { Source = "real-dir", Target = "/opt/data" }
                }
            };

            var issues = _linter.Lint(project, dir);
            Assert.IsFalse(issues.Any(i => i.Message.Contains("not found")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Lint_EmptySourceAttribute_Error()
    {
        var dir = CreateTempDir();
        try
        {
            var project = new AosprojProject
            {
                PackageName = "my-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "desc",
                TargetSuites = "jammy",
                Maintainer = "Test <test@example.com>",
                IncludeFiles =
                {
                    new IncludeFileItem { Source = "", Target = "/opt/file.txt" }
                }
            };

            var issues = _linter.Lint(project, dir);
            Assert.IsTrue(issues.Any(i => i.Message.Contains("empty Source")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Lint_ValidCondition_NoError()
    {
        var dir = CreateTempDir();
        try
        {
            var project = new AosprojProject
            {
                PackageName = "my-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "desc",
                TargetSuites = "jammy",
                Maintainer = "Test <test@example.com>",
                Dependencies =
                {
                    new ConditionalValue { Value = "libc6", Condition = "'$(Suite)' == 'jammy'" }
                }
            };

            var issues = _linter.Lint(project, dir);
            Assert.IsFalse(issues.Any(i => i.Message.Contains("Invalid condition")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Lint_InvalidCondition_Error()
    {
        var dir = CreateTempDir();
        try
        {
            var project = new AosprojProject
            {
                PackageName = "my-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "desc",
                TargetSuites = "jammy",
                Maintainer = "Test <test@example.com>",
                Dependencies =
                {
                    new ConditionalValue { Value = "libc6", Condition = "$(Suite) junk $(Distro)" }
                }
            };

            var issues = _linter.Lint(project, dir);
            Assert.IsTrue(issues.Any(i => i.Message.Contains("Invalid condition")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Lint_NoFiles_Warning()
    {
        var dir = CreateTempDir();
        try
        {
            var project = new AosprojProject
            {
                PackageName = "my-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "desc",
                TargetSuites = "jammy",
                Maintainer = "Test <test@example.com>"
            };

            var issues = _linter.Lint(project, dir);
            Assert.IsTrue(issues.Any(i => i.Message.Contains("No files declared")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Lint_HasFiles_NoEmptyWarning()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "file.txt"), "content");

            var project = new AosprojProject
            {
                PackageName = "my-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "desc",
                TargetSuites = "jammy",
                Maintainer = "Test <test@example.com>",
                IncludeFiles =
                {
                    new IncludeFileItem { Source = "file.txt", Target = "/opt/file.txt" }
                }
            };

            var issues = _linter.Lint(project, dir);
            Assert.IsFalse(issues.Any(i => i.Message.Contains("No files declared")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Lint_MissingTargetOnIncludeFile_Error()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "file.txt"), "content");

            var project = new AosprojProject
            {
                PackageName = "my-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "desc",
                TargetSuites = "jammy",
                Maintainer = "Test <test@example.com>",
                IncludeFiles =
                {
                    new IncludeFileItem { Source = "file.txt", Target = "" }
                }
            };

            var issues = _linter.Lint(project, dir);
            Assert.IsTrue(issues.Any(i => i.Level == AosprojLinter.Severity.Error && i.Message.Contains("Target")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Lint_ChecksAllItemTypesForSource()
    {
        var dir = CreateTempDir();
        try
        {
            var project = new AosprojProject
            {
                PackageName = "my-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "desc",
                TargetSuites = "jammy",
                Maintainer = "Test <test@example.com>",
                IncludeScripts =
                {
                    new IncludeScriptItem { Source = "missing.sh", Target = "/usr/bin/script" }
                },
                ConfFiles =
                {
                    new ConfFileItem { Source = "missing.conf", Target = "/etc/app.conf" }
                },
                PostInstallScripts =
                {
                    new PostInstallScriptItem { Source = "missing-postinst.sh" }
                },
                PreRemoveScripts =
                {
                    new PreRemoveScriptItem { Source = "missing-prerm.sh" }
                },
                SystemdUnits =
                {
                    new SystemdUnitItem { Source = "missing.service" }
                }
            };

            var issues = _linter.Lint(project, dir);
            var sourceWarnings = issues.Where(i => i.Message.Contains("not found")).ToList();
            Assert.AreEqual(5, sourceWarnings.Count);
            Assert.IsTrue(sourceWarnings.Any(i => i.Message.Contains("IncludeScript")));
            Assert.IsTrue(sourceWarnings.Any(i => i.Message.Contains("ConfFile")));
            Assert.IsTrue(sourceWarnings.Any(i => i.Message.Contains("PostInstallScript")));
            Assert.IsTrue(sourceWarnings.Any(i => i.Message.Contains("PreRemoveScript")));
            Assert.IsTrue(sourceWarnings.Any(i => i.Message.Contains("SystemdUnit")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "lint-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
