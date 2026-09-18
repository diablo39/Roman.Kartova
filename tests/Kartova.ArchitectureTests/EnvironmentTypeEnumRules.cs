using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;

namespace Kartova.ArchitectureTests;

/// <summary>
/// Pins <see cref="EnvironmentType"/> persisted-value stability. Numeric values
/// (0=Development, 1=Staging, 2=Production) are stored in the <c>type smallint</c>
/// column on <c>catalog_environments</c>; reordering/renumbering would corrupt
/// rows already on disk. Append new members at the end only. Mirrors
/// <see cref="LifecycleEnumRules"/>.
/// </summary>
[ExcludeFromCodeCoverage]
[TestClass]
public class EnvironmentTypeEnumRules
{
#pragma warning disable MSTEST0032
	[TestMethod]
	public void EnvironmentType_has_exactly_three_members_with_explicit_values()
	{
		Assert.AreEqual(3, Enum.GetValues<EnvironmentType>().Length);
		Assert.AreEqual(0, (int)EnvironmentType.Development);
		Assert.AreEqual(1, (int)EnvironmentType.Staging);
		Assert.AreEqual(2, (int)EnvironmentType.Production);
	}
#pragma warning restore MSTEST0032
}
