using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BoreholeHub.Tests;

/// <summary>
/// Generic checks for the DE/EN text convention (docs/i18n.md), so that later pages are covered automatically:
/// every English resource key is used in the source, and every localized literal has an English entry.
/// </summary>
public class LocalizationCoverageTests
{
    /// <summary>Keys that are examples only and not used by any UI text (docs/i18n.md, section "Example keys").</summary>
    private static readonly HashSet<string> ExampleOnlyKeys = ["Beispieltext aus einer C#-Klasse"];

    private static readonly string WebRoot = FindWebRoot();
    private static readonly string ResourcesRoot = Path.Combine(WebRoot, "Resources");

    private static readonly Regex ViewKey = new("\\bL\\[\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);
    private static readonly Regex SharedKey = new("\\bT\\[\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);

    private static string FindWebRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "BoreholeHub.Web")))
        {
            dir = dir.Parent;
        }
        if (dir is null)
        {
            throw new DirectoryNotFoundException("src/BoreholeHub.Web was not found above the test output folder.");
        }
        return Path.Combine(dir.FullName, "src", "BoreholeHub.Web");
    }

    /// <summary>Source files of the web project, without build output (bin, obj).</summary>
    private static IEnumerable<string> SourceFiles(Func<string, bool> filter) =>
        Directory.EnumerateFiles(WebRoot, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(WebRoot, f).Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj"))
            .Where(filter);

    private static IEnumerable<string> ResourceFiles() =>
        Directory.EnumerateFiles(ResourcesRoot, "*.en.resx", SearchOption.AllDirectories);

    private static HashSet<string> KeysOf(string resxPath) =>
        XDocument.Load(resxPath).Descendants("data").Select(d => (string?)d.Attribute("name") ?? "").ToHashSet();

    /// <summary>"Pages/NotFound.en.resx" for a resource file under Resources/.</summary>
    private static string RelativeBase(string resxPath) =>
        Path.GetRelativePath(ResourcesRoot, resxPath).Replace(".en.resx", "").Replace('\\', '/');

    private static IEnumerable<string> LiteralsIn(Regex pattern, string text) =>
        pattern.Matches(text).Select(m => m.Groups[1].Value);

    [Fact]
    public void EveryEnglishKey_IsUsedAsLiteral_InItsSourceFiles()
    {
        var resx = ResourceFiles().ToList();
        Assert.NotEmpty(resx);
        var problems = new List<string>();

        foreach (var file in resx)
        {
            string baseName = RelativeBase(file);
            IEnumerable<string> sources;
            string scope;
            if (baseName == "SharedResource")
            {
                sources = SourceFiles(f => Path.GetExtension(f) != ".resx");
                scope = "any source file";
            }
            else
            {
                // A view and its page model share one resource file, so both kinds are searched.
                sources = SourceFiles(f => Path.GetExtension(f) is ".cshtml" or ".cs");
                scope = ".cshtml or .cs files";
            }
            var texts = sources.Select(File.ReadAllText).ToList();

            foreach (var key in KeysOf(file))
            {
                if (ExampleOnlyKeys.Contains(key))
                {
                    continue;
                }
                string literal = "\"" + key + "\"";
                if (!texts.Any(t => t.Contains(literal, StringComparison.Ordinal)))
                {
                    problems.Add($"{baseName}.en.resx: key '{key}' is not used as a literal in {scope}");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EveryViewLocalizerKey_HasEntryInMatchingResx()
    {
        var views = SourceFiles(f => Path.GetExtension(f) == ".cshtml")
            .Where(f => File.ReadAllText(f).Contains("IViewLocalizer", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(views);
        var problems = new List<string>();

        foreach (var view in views)
        {
            string relative = Path.GetRelativePath(WebRoot, view).Replace('\\', '/');
            // A view's resource is Resources/<same path as the view>.en.resx, e.g. Pages/NotFound.cshtml -> Resources/Pages/NotFound.en.resx.
            string resx = Path.Combine(ResourcesRoot, relative.Substring(0, relative.Length - ".cshtml".Length) + ".en.resx");
            if (!File.Exists(resx))
            {
                problems.Add($"{relative}: no matching resource file {Path.GetRelativePath(WebRoot, resx)}");
                continue;
            }
            var keys = KeysOf(resx);
            foreach (var key in LiteralsIn(ViewKey, File.ReadAllText(view)))
            {
                if (!keys.Contains(key))
                {
                    problems.Add($"{relative}: L[\"{key}\"] has no entry in {Path.GetRelativePath(WebRoot, resx)}");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EverySharedKey_HasEntryInSharedResource()
    {
        string sharedResx = Path.Combine(ResourcesRoot, "SharedResource.en.resx");
        Assert.True(File.Exists(sharedResx), "Resources/SharedResource.en.resx is missing.");
        var keys = KeysOf(sharedResx);
        var problems = new List<string>();

        var users = SourceFiles(f => Path.GetExtension(f) == ".cshtml")
            .Where(f => File.ReadAllText(f).Contains("SharedResource", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(users);

        foreach (var file in users)
        {
            foreach (var key in LiteralsIn(SharedKey, File.ReadAllText(file)))
            {
                if (!keys.Contains(key))
                {
                    problems.Add($"{Path.GetRelativePath(WebRoot, file)}: T[\"{key}\"] has no entry in SharedResource.en.resx");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
