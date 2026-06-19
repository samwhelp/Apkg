using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Aiursoft.Apkg.WebTests;

/// <summary>
/// Guards against String.Format-style placeholders like {arch} or {name} appearing
/// in Localizer keys or resx values without being proper numeric indices ({0}, {1}, …).
/// LocalizedHtmlString.WriteTo calls String.Format on the translated value, so any
/// {letter} placeholder causes a FormatException at runtime.
/// </summary>
[TestClass]
public class LocalizationFormatTests
{
    // Matches {word} where the first char after { is a letter/underscore.
    // The negative lookbehind (?<!{) excludes already-escaped {{ sequences.
    private static readonly Regex InvalidFormatSpec = new(
        @"(?<!\{)\{[A-Za-z_][^}]*\}",
        RegexOptions.Compiled);

    private static string GetSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.GetFiles("*.sln").Any())
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Solution root not found.");
    }

    /// <summary>
    /// Removes C# string literal escape sequences (like \" or \\) from text
    /// read out of a .cshtml file so that the key matches the corresponding
    /// resx entry (where the value is the actual, unescaped string).
    /// </summary>
    private static string UnescapeCSharpString(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                sb.Append(s[i + 1]);
                i++;
            }
            else
            {
                sb.Append(s[i]);
            }
        }
        return sb.ToString();
    }

    [TestMethod]
    public void LocalizerKeysInViews_ShouldNotContainNonIntegerFormatSpecifiers()
    {
        var root = GetSolutionRoot();
        var srcRoot = Path.Combine(root, "src");
        var violations = new List<string>();

        foreach (var file in Directory.GetFiles(srcRoot, "*.cshtml", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(file);
            // Extract the first argument (key) of every @Localizer["…"] call.
            // The pattern handles C#-style escaped quotes (\" inside the string).
            var matches = Regex.Matches(content, @"Localizer\[""((?:[^""\\]|\\.)*)""");
            foreach (Match m in matches)
            {
                var key = UnescapeCSharpString(m.Groups[1].Value);
                if (InvalidFormatSpec.IsMatch(key))
                    violations.Add($"{Path.GetRelativePath(root, file)}: key \"{key}\"");
            }
        }

        Assert.AreEqual(0, violations.Count,
            "Localizer keys with non-integer format specifiers (would cause FormatException at render time):\n"
            + string.Join("\n", violations));
    }

    [TestMethod]
    public void AllLocalizerKeysInIndexView_ShouldHaveCorrespondingResxEntry()
    {
        var root = GetSolutionRoot();
        var srcRoot = Path.Combine(root, "src");
        var indexView = Path.Combine(srcRoot, "Aiursoft.Apkg", "Views", "Home", "Index.cshtml");

        Assert.IsTrue(File.Exists(indexView),
            $"Index.cshtml not found at expected path: {indexView}");

        var content = File.ReadAllText(indexView);

        // Extract all @Localizer["…"] keys, unescaping C# string literal syntax
        // so keys match their resx counterparts (which store the actual string value).
        var localizerKeys = Regex.Matches(content, @"Localizer\[""((?:[^""\\]|\\.)*)""")
            .Select(m => UnescapeCSharpString(m.Groups[1].Value))
            .Distinct()
            .ToList();

        Assert.IsTrue(localizerKeys.Count > 0,
            "Index.cshtml should contain at least one @Localizer call.");

        // Load the en-GB resx file
        var enGbResx = Path.Combine(srcRoot, "Aiursoft.Apkg", "Resources", "Views", "Home", "Index.en-GB.resx");
        Assert.IsTrue(File.Exists(enGbResx),
            $"Index.en-GB.resx not found at expected path: {enGbResx}");

        var doc = XDocument.Load(enGbResx);
        var resxKeys = doc.Descendants("data")
            .Select(d => d.Attribute("name")?.Value)
            .Where(n => n != null)
            .ToHashSet();

        // Report any Localizer key missing from the resx
        var missing = localizerKeys.Where(k => !resxKeys.Contains(k)).ToList();

        Assert.AreEqual(0, missing.Count,
            "Localizer keys in Index.cshtml that have no corresponding resx entry in Index.en-GB.resx:\n"
            + string.Join("\n", missing));
    }

    [TestMethod]
    public void ResxValues_ShouldNotContainNonIntegerFormatSpecifiers()
    {
        var root = GetSolutionRoot();
        var srcRoot = Path.Combine(root, "src");
        var violations = new List<string>();

        foreach (var file in Directory.GetFiles(srcRoot, "*.resx", SearchOption.AllDirectories))
        {
            var doc = XDocument.Load(file);
            foreach (var data in doc.Descendants("data"))
            {
                var value = data.Element("value")?.Value ?? string.Empty;
                if (InvalidFormatSpec.IsMatch(value))
                {
                    var name = data.Attribute("name")?.Value ?? "(unknown)";
                    violations.Add($"{Path.GetRelativePath(root, file)}: key \"{name}\"");
                }
            }
        }

        Assert.AreEqual(0, violations.Count,
            "Resx values with non-integer format specifiers (would cause FormatException at render time):\n"
            + string.Join("\n", violations));
    }
}
