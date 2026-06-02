using Kartova.Organization.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.Organization.Tests;

[TestClass]
public sealed class InvitationTokenTests
{
    [TestMethod]
    public void Hash_is_deterministic_for_same_input()
    {
        Assert.AreEqual(InvitationToken.Hash("abc"), InvitationToken.Hash("abc"));
    }

    [TestMethod]
    public void Hash_differs_for_different_input()
    {
        Assert.AreNotEqual(InvitationToken.Hash("abc"), InvitationToken.Hash("abd"));
    }

    [TestMethod]
    public void Hash_is_base64url_of_sha256_43_chars()
    {
        // SHA-256 = 32 bytes -> base64url without padding = 43 chars.
        Assert.AreEqual(43, InvitationToken.Hash("anything").Length);
        Assert.IsFalse(InvitationToken.Hash("anything").Contains('+'));
        Assert.IsFalse(InvitationToken.Hash("anything").Contains('/'));
        Assert.IsFalse(InvitationToken.Hash("anything").Contains('='));
    }

    [TestMethod]
    public void Issue_returns_distinct_high_entropy_plaintext_and_matching_hash()
    {
        var (p1, h1) = InvitationToken.Issue();
        var (p2, _) = InvitationToken.Issue();
        Assert.AreNotEqual(p1, p2);
        Assert.AreEqual(43, p1.Length);
        Assert.AreEqual(InvitationToken.Hash(p1), h1);
    }
}
