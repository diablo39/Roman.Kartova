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
}
