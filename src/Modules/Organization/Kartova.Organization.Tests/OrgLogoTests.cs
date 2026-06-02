using Kartova.Organization.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.Organization.Tests;

[TestClass]
public sealed class OrgLogoTests
{
    [TestMethod]
    public void Create_with_valid_png_returns_logo_with_hash()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xDE, 0xAD };
        var logo = OrgLogo.Create(bytes, "image/png");
        Assert.AreEqual("image/png", logo.MimeType);
        Assert.AreEqual(64, logo.ContentHash.Length);  // SHA-256 hex
        CollectionAssert.AreEqual(bytes, logo.Bytes);
    }

    [TestMethod]
    public void Create_rejects_empty_bytes()
    {
        Assert.ThrowsExactly<ArgumentException>(() => OrgLogo.Create([], "image/png"));
    }

    [TestMethod]
    public void Create_rejects_oversize_bytes()
    {
        Assert.ThrowsExactly<ArgumentException>(() => OrgLogo.Create(new byte[256 * 1024 + 1], "image/png"));
    }

    [TestMethod]
    [DataRow("image/gif")]
    [DataRow("application/octet-stream")]
    [DataRow("")]
    public void Create_rejects_unsupported_mime(string mime)
    {
        Assert.ThrowsExactly<ArgumentException>(() => OrgLogo.Create(new byte[16], mime));
    }

    [TestMethod]
    public void Create_accepts_max_size_bytes()
    {
        var bytes = new byte[256 * 1024];
        var logo = OrgLogo.Create(bytes, "image/png");
        Assert.AreEqual(256 * 1024, logo.Bytes.Length);
        Assert.AreEqual(64, logo.ContentHash.Length);
    }

    [TestMethod]
    public void Create_makes_defensive_copy_so_caller_mutations_dont_leak()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xDE, 0xAD };
        var logo = OrgLogo.Create(bytes, "image/png");
        var originalFirst = logo.Bytes[0];
        bytes[0] = 0xFF;   // mutate caller's array
        Assert.AreEqual(originalFirst, logo.Bytes[0]);
    }

    [TestMethod]
    public void Bytes_returned_array_is_a_defensive_clone()
    {
        // S4 (slice-9 carry-forward): the public Bytes getter MUST return a
        // fresh clone on every read. Without this, callers could mutate the
        // returned array, invalidating ContentHash (which is computed once
        // in Create and never recomputed). Pinning the clone behaviour with
        // three independent assertions keeps the invariant under mutation
        // testing — any mutant that drops the .Clone() call would flip
        // AreNotSame, and any mutant that aliases the field would let the
        // post-mutation ContentHash check fail.
        var input = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var logo = OrgLogo.Create(input, "image/png");
        var firstRead = logo.Bytes;
        var secondRead = logo.Bytes;

        // Different array instance per read (clone), but same content.
        Assert.AreNotSame(firstRead, secondRead);
        CollectionAssert.AreEqual(firstRead, secondRead);

        // Mutating the returned array does NOT affect the stored ContentHash.
        var originalHash = logo.ContentHash;
        firstRead[0] = 0xFF;
        Assert.AreEqual(originalHash, logo.ContentHash);

        // And the next read returns a fresh clone with the ORIGINAL bytes.
        var thirdRead = logo.Bytes;
        CollectionAssert.AreEqual(input, thirdRead);
    }
}
