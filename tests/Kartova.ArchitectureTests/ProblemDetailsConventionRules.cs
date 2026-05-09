using System.Text.RegularExpressions;

namespace Kartova.ArchitectureTests;

/// <summary>
/// ADR-0091: every HTTP error response uses RFC 7807 <c>application/problem+json</c>
/// via <c>Results.Problem(...)</c>. The ADR's Implementation Notes section calls for
/// an architecture test that prevents ad-hoc <c>{ error = ... }</c> body shapes from
/// landing.
///
/// Implementation: source-text scan of <c>src/</c>. Regex-based, so it catches the
/// common shapes (<c>Results.Json(new { error ...</c>, <c>Results.Ok(new { error ...</c>,
/// <c>Results.BadRequest(new { error ...</c>) but is bypassable by reformatting or
/// extracting to a variable. That is a deliberate trade-off — a Roslyn analyzer would
/// catch every shape but adds a heavy compile-time dependency. The text scan covers
/// the realistic accident path; reviewers handle the rest.
/// </summary>
[TestClass]
public class ProblemDetailsConventionRules
{
    [TestMethod]
    public void Source_does_not_emit_ad_hoc_error_shapes_per_ADR_0091()
    {
        var srcDir = LocateSrcDir();
        Assert.IsTrue(
            Directory.Exists(srcDir),
            $"expected to locate the project's src directory at {srcDir}");

        var offenders = new List<string>();
        foreach (var file in EnumerateProductionSources(srcDir))
        {
            var content = File.ReadAllText(file);
            foreach (var (description, pattern) in ForbiddenPatterns())
            {
                if (pattern.IsMatch(content))
                {
                    offenders.Add($"{ToRelativePath(srcDir, file)}: {description}");
                }
            }
        }

        Assert.AreEqual(
            0,
            offenders.Count,
            "ADR-0091 requires error responses to use Results.Problem(...) with a type from " +
            "ProblemTypes — not ad-hoc anonymous-error shapes. Offending source: " +
            string.Join("; ", offenders));
    }

    /// <summary>
    /// Self-test: verifies each forbidden-shape regex actually matches a known-bad
    /// string. Without this, a regex that accidentally matches nothing would let the
    /// production scan pass while silently providing no protection.
    /// </summary>
    [TestMethod]
    [DataRow("Results.Json(new { error = \"x\" })")]
    [DataRow("Results . Json ( new { error = \"x\" } )")]
    [DataRow("Results.Json(new {\n            error = \"x\"\n        })")]
    [DataRow("Results.Ok(new { error = \"x\" })")]
    [DataRow("Results.BadRequest(new { error = \"x\" })")]
    [DataRow("Results.Conflict(new { error = \"x\" })")]
    [DataRow("Results.UnprocessableEntity(new { error = \"x\" })")]
    public void Forbidden_patterns_match_known_bad_shapes(string sample)
    {
        var anyMatched = ForbiddenPatterns().Any(p => p.Pattern.IsMatch(sample));
        Assert.IsTrue(
            anyMatched,
            $"the production scan must catch this shape: '{sample}'");
    }

    /// <summary>
    /// Counter-test: legitimate <c>Results.Problem(...)</c> and <c>Results.Json(dto, statusCode: 201)</c>
    /// shapes must not trigger the forbidden-shape rule.
    /// </summary>
    [TestMethod]
    [DataRow("Results.Problem(type: ProblemTypes.ValidationFailed, statusCode: 400)")]
    [DataRow("Results.Json(org, statusCode: StatusCodes.Status201Created)")]
    [DataRow("Results.Ok(dto)")]
    [DataRow("Results.Created(\"/api/v1/organizations/abc\", org)")]
    public void Forbidden_patterns_do_not_match_legitimate_shapes(string sample)
    {
        var anyMatched = ForbiddenPatterns().Any(p => p.Pattern.IsMatch(sample));
        Assert.IsFalse(
            anyMatched,
            $"this shape is ADR-0091-compliant and must not flag: '{sample}'");
    }

    private static (string Description, Regex Pattern)[] ForbiddenPatterns() => new (string, Regex)[]
    {
        ("Results.Json(new { error ... })",
            new Regex(@"Results\s*\.\s*Json\s*\(\s*new\s*\{\s*error\b", RegexOptions.Compiled | RegexOptions.Singleline)),
        ("Results.Ok(new { error ... })",
            new Regex(@"Results\s*\.\s*Ok\s*\(\s*new\s*\{\s*error\b", RegexOptions.Compiled | RegexOptions.Singleline)),
        ("Results.BadRequest(new { error ... })",
            new Regex(@"Results\s*\.\s*BadRequest\s*\(\s*new\s*\{\s*error\b", RegexOptions.Compiled | RegexOptions.Singleline)),
        ("Results.Conflict(new { error ... })",
            new Regex(@"Results\s*\.\s*Conflict\s*\(\s*new\s*\{\s*error\b", RegexOptions.Compiled | RegexOptions.Singleline)),
        ("Results.UnprocessableEntity(new { error ... })",
            new Regex(@"Results\s*\.\s*UnprocessableEntity\s*\(\s*new\s*\{\s*error\b", RegexOptions.Compiled | RegexOptions.Singleline)),
    };

    private static IEnumerable<string> EnumerateProductionSources(string srcDir) =>
        Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"));

    private static string LocateSrcDir()
    {
        // Test bin path: tests/Kartova.ArchitectureTests/bin/Debug/net10.0/
        // Five levels up reaches the repo root.
        var testAssemblyLocation = Path.GetDirectoryName(typeof(ProblemDetailsConventionRules).Assembly.Location)!;
        var repoRoot = Path.GetFullPath(
            Path.Combine(testAssemblyLocation, "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "src");
    }

    private static string ToRelativePath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');
}
