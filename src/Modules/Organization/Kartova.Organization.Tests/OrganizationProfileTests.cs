using Kartova.Organization.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.Organization.Tests;

[TestClass]
public sealed class OrganizationProfileTests
{
    private static Domain.Organization Make() =>
        Domain.Organization.Create("Org A", TimeProvider.System);

    [TestMethod]
    public void UpdateProfile_sets_all_fields()
    {
        var org = Make();
        org.UpdateProfile("Org A New", "A description.", "Europe/Warsaw");
        Assert.AreEqual("Org A New", org.DisplayName);
        Assert.AreEqual("A description.", org.Description);
        Assert.AreEqual("Europe/Warsaw", org.DefaultTimeZone);
    }

    [TestMethod]
    public void UpdateProfile_rejects_empty_display_name()
    {
        var org = Make();
        var ex = Assert.ThrowsExactly<ArgumentException>(() => org.UpdateProfile("", null, "UTC"));
        Assert.AreEqual("displayName", ex.ParamName);
    }

    [TestMethod]
    public void UpdateProfile_rejects_overlong_description()
    {
        var org = Make();
        var tooLong = new string('x', 1025);
        var ex = Assert.ThrowsExactly<ArgumentException>(() => org.UpdateProfile("Org A", tooLong, "UTC"));
        Assert.AreEqual("description", ex.ParamName);
    }

    [TestMethod]
    public void UpdateProfile_rejects_unknown_timezone()
    {
        var org = Make();
        var ex = Assert.ThrowsExactly<ArgumentException>(() => org.UpdateProfile("Org A", null, "Mars/Olympus"));
        Assert.AreEqual("tz", ex.ParamName);
    }

    [TestMethod]
    public void UpdateProfile_accepts_description_at_max_length()
    {
        var org = Make();
        var maxDescription = new string('x', 1024);
        org.UpdateProfile("Org A", maxDescription, "UTC");
        Assert.AreEqual(maxDescription, org.Description);
    }

    [TestMethod]
    public void SetLogo_assigns_logo()
    {
        var org = Make();
        var logo = OrgLogo.Create(new byte[16], "image/png");
        org.SetLogo(logo);
        Assert.AreSame(logo, org.Logo);
    }

    [TestMethod]
    public void ClearLogo_removes_logo()
    {
        var org = Make();
        org.SetLogo(OrgLogo.Create(new byte[16], "image/png"));
        org.ClearLogo();
        Assert.IsNull(org.Logo);
    }
}
