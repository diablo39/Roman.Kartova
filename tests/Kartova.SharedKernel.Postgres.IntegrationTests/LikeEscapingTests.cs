using Kartova.SharedKernel.Postgres.Pagination;

namespace Kartova.SharedKernel.Postgres.IntegrationTests;

[TestClass]
public sealed class LikeEscapingTests
{
    [TestMethod]
    public void Plain_text_is_unchanged()
        => Assert.AreEqual("payments", LikeEscaping.EscapeLike("payments"));

    [TestMethod]
    public void Percent_underscore_and_backslash_are_escaped()
    {
        // Backslash MUST be escaped first, or the escapes added for % and _ get re-escaped.
        Assert.AreEqual(@"50\% off", LikeEscaping.EscapeLike("50% off"));
        Assert.AreEqual(@"a\_b", LikeEscaping.EscapeLike("a_b"));
        Assert.AreEqual(@"c\\d", LikeEscaping.EscapeLike(@"c\d"));
    }

    [TestMethod]
    public void Combined_metacharacters_escape_in_backslash_first_order()
        => Assert.AreEqual(@"\\\%\_", LikeEscaping.EscapeLike(@"\%_"));

    [TestMethod]
    public void Null_input_throws_ArgumentNullException()
        => Assert.ThrowsExactly<ArgumentNullException>(() => LikeEscaping.EscapeLike(null!));

    [TestMethod]
    public void Empty_string_returns_empty_string()
        => Assert.AreEqual("", LikeEscaping.EscapeLike(""));
}
