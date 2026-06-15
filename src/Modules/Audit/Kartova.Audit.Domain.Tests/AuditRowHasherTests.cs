using System.Collections.Generic;
using Kartova.Audit.Domain;

namespace Kartova.Audit.Domain.Tests;

[TestClass]
public class AuditRowHasherTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Actor = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset When = new(2026, 6, 12, 9, 30, 0, TimeSpan.Zero);

    private static byte[] Hash(IReadOnlyDictionary<string, string?>? data, byte[] prev) =>
        AuditRowHasher.ComputeRowHash(
            Tenant, seq: 1, When, AuditActorType.User, Actor,
            action: "member.role_changed", targetType: "User", targetId: Actor.ToString(),
            data, prev);

    [TestMethod]
    public void GenesisHash_is_32_zero_bytes()
    {
        Assert.AreEqual(32, AuditRowHasher.GenesisHash.Length);
        CollectionAssert.AreEqual(new byte[32], AuditRowHasher.GenesisHash);
    }

    [TestMethod]
    public void ComputeRowHash_is_deterministic()
    {
        var data = new Dictionary<string, string?> { ["old_role"] = "Member", ["new_role"] = "OrgAdmin" };
        var a = Hash(data, AuditRowHasher.GenesisHash);
        var b = Hash(data, AuditRowHasher.GenesisHash);
        CollectionAssert.AreEqual(a, b);
        Assert.AreEqual(32, a.Length);
    }

    [TestMethod]
    public void ComputeRowHash_is_independent_of_data_key_insertion_order()
    {
        var ordered = new Dictionary<string, string?> { ["new_role"] = "OrgAdmin", ["old_role"] = "Member" };
        var reversed = new Dictionary<string, string?> { ["old_role"] = "Member", ["new_role"] = "OrgAdmin" };
        CollectionAssert.AreEqual(Hash(ordered, AuditRowHasher.GenesisHash), Hash(reversed, AuditRowHasher.GenesisHash));
    }

    [TestMethod]
    public void ComputeRowHash_changes_when_prev_hash_changes()
    {
        var data = new Dictionary<string, string?> { ["k"] = "v" };
        var genesis = Hash(data, AuditRowHasher.GenesisHash);
        var chained = Hash(data, genesis);
        CollectionAssert.AreNotEqual(genesis, chained);
    }

    [TestMethod]
    public void ComputeRowHash_changes_when_payload_changes()
    {
        var d1 = new Dictionary<string, string?> { ["new_role"] = "OrgAdmin" };
        var d2 = new Dictionary<string, string?> { ["new_role"] = "Viewer" };
        CollectionAssert.AreNotEqual(Hash(d1, AuditRowHasher.GenesisHash), Hash(d2, AuditRowHasher.GenesisHash));
    }

    [TestMethod]
    public void ComputeRowHash_handles_null_data()
    {
        var h = Hash(null, AuditRowHasher.GenesisHash);
        Assert.AreEqual(32, h.Length);
    }

    // Golden value pins the canonical encoding so a silent format change (field order, timestamp
    // format, key sort, prev_hash encoding) is caught — such a change would orphan already-stored
    // rows. Expected value computed once from the real serializer and frozen.
    [TestMethod]
    public void ComputeRowHash_matches_pinned_golden_value()
    {
        var data = new Dictionary<string, string?> { ["old_role"] = "Member", ["new_role"] = "OrgAdmin" };
        var actual = AuditRowHasher.ComputeRowHash(
            Tenant, seq: 1, When, AuditActorType.User, Actor,
            action: "member.role_changed", targetType: "User", targetId: Actor.ToString(),
            data, AuditRowHasher.GenesisHash);

        const string KnownHashHex = "A52E421EF6EA5484B75C66538EAF0A1E77FA0EE04FEC80210B22B453AA6E051C";
        Assert.AreEqual(KnownHashHex, Convert.ToHexString(actual));
    }

    // Survivor 1: AuditCanonicalSerializer.cs:43 — arithmetic mutation `Ticks - (Ticks % 10)` → `+`
    // A µs-aligned base time (Ticks % 10 == 0) makes + and − identical; use a sub-µs offset to
    // distinguish floor (correct) from ceil (mutant).
    [TestMethod]
    public void ComputeRowHash_sub_microsecond_timestamp_is_floored_to_microsecond_boundary()
    {
        // Base time whose Ticks are exactly µs-aligned (% 10 == 0).
        var baseTime = new DateTimeOffset(2026, 6, 12, 9, 30, 0, TimeSpan.Zero);
        Assert.AreEqual(0, baseTime.Ticks % 10, "precondition: base time is µs-aligned");

        var subMicros = baseTime + TimeSpan.FromTicks(3); // Ticks % 10 == 3  → floor == base, ceil == base+10
        var floor     = baseTime;                          // Ticks − 3 → same as base
        var ceil      = baseTime + TimeSpan.FromTicks(10); // Ticks + 3 → base+10 (what the + mutant produces)

        var actualHash = AuditRowHasher.ComputeRowHash(
            Tenant, seq: 1, subMicros, AuditActorType.User, Actor,
            action: "member.role_changed", targetType: "User", targetId: Actor.ToString(),
            data: null, AuditRowHasher.GenesisHash);

        var floorHash = AuditRowHasher.ComputeRowHash(
            Tenant, seq: 1, floor, AuditActorType.User, Actor,
            action: "member.role_changed", targetType: "User", targetId: Actor.ToString(),
            data: null, AuditRowHasher.GenesisHash);

        var ceilHash = AuditRowHasher.ComputeRowHash(
            Tenant, seq: 1, ceil, AuditActorType.User, Actor,
            action: "member.role_changed", targetType: "User", targetId: Actor.ToString(),
            data: null, AuditRowHasher.GenesisHash);

        // Correct code floors → actualHash == floorHash
        CollectionAssert.AreEqual(floorHash, actualHash);
        // The + mutant would produce ceilHash instead; verify floor and ceil differ
        CollectionAssert.AreNotEqual(floorHash, ceilHash);
    }

    // Survivor 2: AuditCanonicalSerializer.cs:47 — NoCoverage: `else w.WriteNull("actor_id")`
    // No test previously passed a null actorId through the hasher.
    [TestMethod]
    public void ComputeRowHash_null_actor_id_produces_32_byte_hash_distinct_from_non_null()
    {
        var nullActorHash = AuditRowHasher.ComputeRowHash(
            Tenant, seq: 1, When, AuditActorType.System, actorId: null,
            action: "svc.started", targetType: "Service", targetId: "svc-1",
            data: null, AuditRowHasher.GenesisHash);

        var nonNullActorHash = AuditRowHasher.ComputeRowHash(
            Tenant, seq: 1, When, AuditActorType.System, actorId: Actor,
            action: "svc.started", targetType: "Service", targetId: "svc-1",
            data: null, AuditRowHasher.GenesisHash);

        Assert.AreEqual(32, nullActorHash.Length);
        CollectionAssert.AreNotEqual(nullActorHash, nonNullActorHash);
    }

    [TestMethod]
    public void ComputeRowHash_null_actor_id_matches_pinned_golden_value()
    {
        var data = new Dictionary<string, string?> { ["k"] = "v" };
        var actual = AuditRowHasher.ComputeRowHash(
            Tenant, seq: 1, When, AuditActorType.System, actorId: null,
            action: "system.purge", targetType: "Tenant", targetId: Tenant.ToString(),
            data, AuditRowHasher.GenesisHash);

        const string KnownHashHex = "39B1C3079F142478D07D78B049F71F176C0988D565F715804A65751C6A33E9C3";
        Assert.AreEqual(KnownHashHex, Convert.ToHexString(actual));
    }

    // Survivor 3: AuditCanonicalSerializer.cs:63 — NoCoverage: `w.WriteNull(key)` for null dict value
    // No test had a data dictionary with a null value.
    [TestMethod]
    public void ComputeRowHash_null_dict_value_is_distinct_from_empty_string_and_absent_key()
    {
        var nullValueHash = AuditRowHasher.ComputeRowHash(
            Tenant, seq: 1, When, AuditActorType.User, Actor,
            action: "a", targetType: "T", targetId: "x",
            data: new Dictionary<string, string?> { ["k"] = null },
            AuditRowHasher.GenesisHash);

        var emptyStringHash = AuditRowHasher.ComputeRowHash(
            Tenant, seq: 1, When, AuditActorType.User, Actor,
            action: "a", targetType: "T", targetId: "x",
            data: new Dictionary<string, string?> { ["k"] = "" },
            AuditRowHasher.GenesisHash);

        var absentKeyHash = AuditRowHasher.ComputeRowHash(
            Tenant, seq: 1, When, AuditActorType.User, Actor,
            action: "a", targetType: "T", targetId: "x",
            data: new Dictionary<string, string?>(),
            AuditRowHasher.GenesisHash);

        Assert.AreEqual(32, nullValueHash.Length);
        CollectionAssert.AreNotEqual(nullValueHash, emptyStringHash);
        CollectionAssert.AreNotEqual(nullValueHash, absentKeyHash);
    }
}
