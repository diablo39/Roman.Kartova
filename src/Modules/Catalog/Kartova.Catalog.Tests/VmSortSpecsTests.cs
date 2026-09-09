using Kartova.Catalog.Contracts;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Catalog.Tests;

/// <summary>
/// Unit-tier tests for <see cref="VmSortSpecs"/>'s field-name mapping (ADR-0095 §5, ADR-0115
/// slice 2a). Pure enum-to-<c>SortSpec</c> resolution — no DB involved, so EF's translation of
/// the JSONB selectors to <c>jsonb_extract_path_text(...)</c> is NOT exercised here (that is
/// <c>VmSortSpecsSqlCaptureTests</c>' job, plus the real-Postgres EXPLAIN coverage in
/// <c>InfrastructureVmSortTests</c>).
/// <para>
/// Wire-name literals under test MUST stay exactly the ADR-0109 camelCase keys
/// (<c>powerState/os/vcpu/memoryGb/hostname/region</c>) plus <c>displayName/createdAt/provider</c>
/// since those are also the partial-index literal keys authored in the migration.
/// </para>
/// </summary>
[TestClass]
public class VmSortSpecsTests
{
    [TestMethod]
    [DataRow(VmSortField.DisplayName, "displayName")]
    [DataRow(VmSortField.CreatedAt, "createdAt")]
    [DataRow(VmSortField.Provider, "provider")]
    [DataRow(VmSortField.PowerState, "powerState")]
    [DataRow(VmSortField.Os, "os")]
    [DataRow(VmSortField.Vcpu, "vcpu")]
    [DataRow(VmSortField.MemoryGb, "memoryGb")]
    [DataRow(VmSortField.Hostname, "hostname")]
    [DataRow(VmSortField.Region, "region")]
    public void Resolve_MapsFieldName(VmSortField field, string wire) =>
        Assert.AreEqual(wire, VmSortSpecs.Resolve(field).FieldName);

    [TestMethod]
    public void AllowedFieldNames_contains_all_9_wire_names()
    {
        var expected = new[]
        {
            "displayName", "createdAt", "provider",
            "powerState", "os", "vcpu", "memoryGb", "hostname", "region",
        };
        CollectionAssert.AreEquivalent(expected, VmSortSpecs.AllowedFieldNames.ToList());
    }

    [TestMethod]
    public void AllowedFieldNames_has_no_duplicates() =>
        Assert.AreEqual(
            VmSortSpecs.AllowedFieldNames.Count,
            VmSortSpecs.AllowedFieldNames.Distinct(StringComparer.Ordinal).Count());

    [TestMethod]
    public void Resolve_throws_InvalidSortFieldException_for_undefined_enum_value() =>
        Assert.ThrowsExactly<InvalidSortFieldException>(
            () => VmSortSpecs.Resolve((VmSortField)999));
}
