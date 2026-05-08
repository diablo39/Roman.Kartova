using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.SharedKernel.Tests.Pagination;

[TestClass]
public sealed class QueryablePagingExtensionsTests
{
    private SqliteConnection _conn = null!;
    private TestDbContext _db = null!;

    public sealed class TestRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public Guid OwnerId { get; set; }
        public int Sequence { get; set; }
    }

    public sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> opts) : base(opts) { }
        public DbSet<TestRow> Rows => Set<TestRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // SQLite does not natively support DateTimeOffset ORDER BY.
            // Store as TEXT (ISO-8601) so comparisons and ordering work correctly.
            modelBuilder.Entity<TestRow>()
                .Property(r => r.CreatedAt)
                .HasConversion<string>();

            // Suppress EF Core's auto-generation for Guid PKs so Guid.Empty
            // (row-0) is stored as-is rather than replaced with a new Guid.
            modelBuilder.Entity<TestRow>()
                .Property(r => r.Id)
                .ValueGeneratedNever();

            // Same TEXT-storage trick as CreatedAt: SQLite cannot order DateTime
            // natively when round-tripped through EF Core type conversions.
            modelBuilder.Entity<TestRow>()
                .Property(r => r.UpdatedAt)
                .HasConversion<string>();
        }
    }

    private static readonly SortSpec<TestRow> ByCreatedAt = new("createdAt", x => x.CreatedAt);
    private static readonly SortSpec<TestRow> ByName = new("name", x => x.Name);
    private static readonly SortSpec<TestRow> ByUpdatedAt = new("updatedAt", x => x.UpdatedAt);
    private static readonly SortSpec<TestRow> ByOwnerId = new("ownerId", x => x.OwnerId);
    private static readonly SortSpec<TestRow> BySequence = new("sequence", x => x.Sequence);

    [TestInitialize]
    public async Task TestInit()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        await _conn.OpenAsync();
        var opts = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(_conn).Options;
        _db = new TestDbContext(opts);
        await _db.Database.EnsureCreatedAsync();
    }

    [TestCleanup]
    public async Task TestCleanup()
    {
        await _db.DisposeAsync();
        await _conn.DisposeAsync();
    }

    private async Task SeedAsync(int count)
    {
        var origin = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < count; i++)
        {
            _db.Rows.Add(new TestRow
            {
                Id = Guid.Parse($"00000000-0000-0000-0000-{i:D12}"),
                Name = $"row-{i:D3}",
                CreatedAt = origin.AddMinutes(i),
                UpdatedAt = origin.AddMinutes(i).UtcDateTime,
                OwnerId = Guid.Parse($"11111111-0000-0000-0000-{i:D12}"),
                Sequence = i,
            });
        }
        await _db.SaveChangesAsync();
    }

    private static void AssertAscending<T>(IEnumerable<T> sequence) where T : IComparable<T>
    {
        var list = sequence.ToList();
        for (var i = 1; i < list.Count; i++)
        {
            Assert.IsTrue(list[i - 1].CompareTo(list[i]) <= 0,
                $"Sequence is not ascending at index {i}: {list[i - 1]} > {list[i]}");
        }
    }

    private static void AssertDescending<T>(IEnumerable<T> sequence) where T : IComparable<T>
    {
        var list = sequence.ToList();
        for (var i = 1; i < list.Count; i++)
        {
            Assert.IsTrue(list[i - 1].CompareTo(list[i]) >= 0,
                $"Sequence is not descending at index {i}: {list[i - 1]} < {list[i]}");
        }
    }

    [TestMethod]
    public async Task EmptyTable_returns_empty_page_with_null_next()
    {
        var page = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Asc, cursor: null, limit: 10, x => x.Id, CancellationToken.None);

        Assert.AreEqual(0, page.Items.Count());
        Assert.IsNull(page.NextCursor);
        Assert.IsNull(page.PrevCursor);
    }

    [TestMethod]
    public async Task SinglePage_returns_all_rows_with_null_next()
    {
        await SeedAsync(5);

        var page = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Asc, cursor: null, limit: 10, x => x.Id, CancellationToken.None);

        Assert.AreEqual(5, page.Items.Count());
        Assert.IsNull(page.NextCursor);
    }

    [TestMethod]
    public async Task ExactLimit_does_not_emit_next_cursor()
    {
        await SeedAsync(5);

        var page = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Asc, cursor: null, limit: 5, x => x.Id, CancellationToken.None);

        Assert.AreEqual(5, page.Items.Count());
        Assert.IsNull(page.NextCursor);
    }

    [TestMethod]
    public async Task LimitPlusOne_emits_next_cursor_and_trims()
    {
        await SeedAsync(6);

        var page = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Asc, cursor: null, limit: 5, x => x.Id, CancellationToken.None);

        Assert.AreEqual(5, page.Items.Count());
        Assert.IsNotNull(page.NextCursor);
    }

    [TestMethod]
    public async Task PagingForward_yields_no_duplicates_no_skips()
    {
        await SeedAsync(20);

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = await _db.Rows.ToCursorPagedAsync(
                ByCreatedAt, SortOrder.Asc, cursor, limit: 7, x => x.Id, CancellationToken.None);
            seen.AddRange(page.Items.Select(r => r.Id));
            cursor = page.NextCursor;
        } while (cursor is not null);

        Assert.AreEqual(20, seen.Count);
        Assert.AreEqual(20, seen.Distinct().Count());
        AssertAscending(seen);
    }

    [TestMethod]
    public async Task DescendingOrder_returns_rows_in_reverse()
    {
        await SeedAsync(3);

        var page = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Desc, cursor: null, limit: 10, x => x.Id, CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "row-002", "row-001", "row-000" },
            page.Items.Select(r => r.Name).ToArray());
    }

    [TestMethod]
    public async Task TieOnSortValue_uses_id_as_stable_tiebreaker()
    {
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        _db.Rows.AddRange(
            new TestRow { Id = Guid.Parse("00000000-0000-0000-0000-000000000003"), Name = "c", CreatedAt = t },
            new TestRow { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), Name = "a", CreatedAt = t },
            new TestRow { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), Name = "b", CreatedAt = t });
        await _db.SaveChangesAsync();

        var first = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Asc, cursor: null, limit: 1, x => x.Id, CancellationToken.None);
        var second = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Asc, first.NextCursor, limit: 1, x => x.Id, CancellationToken.None);
        var third = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Asc, second.NextCursor, limit: 1, x => x.Id, CancellationToken.None);

        Assert.AreEqual("a", first.Items.Single().Name);
        Assert.AreEqual("b", second.Items.Single().Name);
        Assert.AreEqual("c", third.Items.Single().Name);
        Assert.IsNull(third.NextCursor);
    }

    [TestMethod]
    public async Task PagingForward_with_string_sort_key_yields_no_duplicates_no_skips()
    {
        await SeedAsync(15);

        var seen = new List<string>();
        string? cursor = null;
        do
        {
            var page = await _db.Rows.ToCursorPagedAsync(
                ByName, SortOrder.Asc, cursor, limit: 4, x => x.Id, CancellationToken.None);
            seen.AddRange(page.Items.Select(r => r.Name));
            cursor = page.NextCursor;
        } while (cursor is not null);

        Assert.AreEqual(15, seen.Count);
        Assert.AreEqual(15, seen.Distinct().Count());
        AssertAscending(seen);
    }

    [TestMethod]
    public async Task TieOnStringSortValue_uses_id_as_stable_tiebreaker()
    {
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        _db.Rows.AddRange(
            new TestRow { Id = Guid.Parse("00000000-0000-0000-0000-000000000003"), Name = "duplicate", CreatedAt = t.AddMinutes(2) },
            new TestRow { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), Name = "duplicate", CreatedAt = t.AddMinutes(0) },
            new TestRow { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), Name = "duplicate", CreatedAt = t.AddMinutes(1) });
        await _db.SaveChangesAsync();

        var first = await _db.Rows.ToCursorPagedAsync(
            ByName, SortOrder.Asc, cursor: null, limit: 1, x => x.Id, CancellationToken.None);
        var second = await _db.Rows.ToCursorPagedAsync(
            ByName, SortOrder.Asc, first.NextCursor, limit: 1, x => x.Id, CancellationToken.None);
        var third = await _db.Rows.ToCursorPagedAsync(
            ByName, SortOrder.Asc, second.NextCursor, limit: 1, x => x.Id, CancellationToken.None);

        // Three rows share the same Name; tiebreaker is Id ascending.
        Assert.AreEqual(Guid.Parse("00000000-0000-0000-0000-000000000001"), first.Items.Single().Id);
        Assert.AreEqual(Guid.Parse("00000000-0000-0000-0000-000000000002"), second.Items.Single().Id);
        Assert.AreEqual(Guid.Parse("00000000-0000-0000-0000-000000000003"), third.Items.Single().Id);
        Assert.IsNull(third.NextCursor);
    }

    [TestMethod]
    public async Task TieOnSortValue_with_desc_uses_id_as_descending_tiebreaker()
    {
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        _db.Rows.AddRange(
            new TestRow { Id = Guid.Parse("00000000-0000-0000-0000-000000000003"), Name = "c", CreatedAt = t },
            new TestRow { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), Name = "a", CreatedAt = t },
            new TestRow { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), Name = "b", CreatedAt = t });
        await _db.SaveChangesAsync();

        // Desc tiebreaker: id descending. Page through 1 row at a time.
        var first = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Desc, cursor: null, limit: 1, x => x.Id, CancellationToken.None);
        var second = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Desc, first.NextCursor, limit: 1, x => x.Id, CancellationToken.None);
        var third = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Desc, second.NextCursor, limit: 1, x => x.Id, CancellationToken.None);

        // Original code: desc tiebreaker is id < cursorId → expect 003, 002, 001.
        // Mutated code (always GreaterThan): id > cursorId → after first=003, second cursor would
        //   filter id > 003, returning nothing, so second.Items would be empty.
        Assert.AreEqual(Guid.Parse("00000000-0000-0000-0000-000000000003"), first.Items.Single().Id);
        Assert.AreEqual(Guid.Parse("00000000-0000-0000-0000-000000000002"), second.Items.Single().Id);
        Assert.AreEqual(Guid.Parse("00000000-0000-0000-0000-000000000001"), third.Items.Single().Id);
        Assert.IsNull(third.NextCursor);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(201)]
    public async Task LimitOutOfRange_throws_InvalidLimitException(int limit)
    {
        await Assert.ThrowsExactlyAsync<InvalidLimitException>(async () => await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Asc, cursor: null, limit, x => x.Id, CancellationToken.None));
    }

    [TestMethod]
    public async Task LimitAtMaxBoundary_does_not_throw()
    {
        // Kills mutant at line 50: `if (limit >= MaxLimit)` would reject limit=200 (200>=200=true→throws)
        // but original `if (limit < MinLimit || limit > MaxLimit)` accepts it (200>200=false→no throw).
        await SeedAsync(1);

        // Tightened from FA's NotThrowAsync<InvalidLimitException>: any thrown
        // exception now fails the test, which is the strictly-stronger invariant.
        await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Asc, cursor: null, limit: 200, x => x.Id, CancellationToken.None);
    }

    [TestMethod]
    public async Task PagingForward_with_desc_order_yields_no_duplicates_no_skips()
    {
        // Kills mutants at lines 133, 140, 145: mutated code uses GreaterThan for Desc (instead of LessThan),
        // so the keyset filter returns the wrong rows and the full set cannot be traversed without duplicates/skips.
        await SeedAsync(20);

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = await _db.Rows.ToCursorPagedAsync(
                ByCreatedAt, SortOrder.Desc, cursor, limit: 7, x => x.Id, CancellationToken.None);
            seen.AddRange(page.Items.Select(r => r.Id));
            cursor = page.NextCursor;
        } while (cursor is not null);

        Assert.AreEqual(20, seen.Count);
        Assert.AreEqual(20, seen.Distinct().Count());
    }

    [TestMethod]
    public async Task PagingForward_with_desc_order_string_sort_yields_correct_order()
    {
        // Kills mutant at line 133 (string-sort path): mutated LessThan→GreaterThan inverts keyset filter for Desc,
        // causing duplicates or skips when paginating by Name descending.
        await SeedAsync(15);

        var seen = new List<string>();
        string? cursor = null;
        do
        {
            var page = await _db.Rows.ToCursorPagedAsync(
                ByName, SortOrder.Desc, cursor, limit: 4, x => x.Id, CancellationToken.None);
            seen.AddRange(page.Items.Select(r => r.Name));
            cursor = page.NextCursor;
        } while (cursor is not null);

        Assert.AreEqual(15, seen.Count);
        Assert.AreEqual(15, seen.Distinct().Count());
        AssertDescending(seen);
    }

    [TestMethod]
    public async Task DirectionMismatch_between_cursor_and_request_throws()
    {
        await SeedAsync(5);
        var ascPage = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Asc, cursor: null, limit: 2, x => x.Id, CancellationToken.None);

        await Assert.ThrowsExactlyAsync<InvalidCursorException>(async () => await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Desc, ascPage.NextCursor, limit: 2, x => x.Id, CancellationToken.None));
    }

    [TestMethod]
    public async Task PagingForward_with_DateTime_sort_key_yields_no_duplicates_no_skips()
    {
        // Exercises ConvertCursorValue's DateTime branch (string → DateTime via Parse + ToUniversalTime).
        // DateTime is NOT IConvertible-friendly with string in invariant culture across all kinds,
        // so the explicit case in ConvertCursorValue is the only correct path.
        await SeedAsync(12);

        var seen = new List<DateTime>();
        string? cursor = null;
        do
        {
            var page = await _db.Rows.ToCursorPagedAsync(
                ByUpdatedAt, SortOrder.Asc, cursor, limit: 4, x => x.Id, CancellationToken.None);
            seen.AddRange(page.Items.Select(r => r.UpdatedAt));
            cursor = page.NextCursor;
        } while (cursor is not null);

        Assert.AreEqual(12, seen.Count);
        Assert.AreEqual(12, seen.Distinct().Count());
        AssertAscending(seen);
    }

    [TestMethod]
    public async Task PagingForward_with_Guid_sort_key_yields_no_duplicates_no_skips()
    {
        // Exercises ConvertCursorValue's Guid branch (string → Guid via Guid.Parse).
        // Guid does not implement IConvertible, so the explicit case is the only correct path —
        // without it, Convert.ChangeType throws InvalidCastException.
        await SeedAsync(10);

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = await _db.Rows.ToCursorPagedAsync(
                ByOwnerId, SortOrder.Asc, cursor, limit: 3, x => x.Id, CancellationToken.None);
            seen.AddRange(page.Items.Select(r => r.OwnerId));
            cursor = page.NextCursor;
        } while (cursor is not null);

        Assert.AreEqual(10, seen.Count);
        Assert.AreEqual(10, seen.Distinct().Count());
        AssertAscending(seen);
    }

    [TestMethod]
    public async Task PagingForward_with_int_sort_key_uses_Convert_ChangeType_fallback()
    {
        // Exercises ConvertCursorValue's `Convert.ChangeType` fallthrough — int is IConvertible
        // and round-trips through string via the invariant-culture overload. Cursor encoder writes
        // the boundary value as a long (JsonValueKind.Number → Int64), so the fallback receives
        // a boxed long that must be converted to int for the keyset comparison.
        await SeedAsync(10);

        var seen = new List<int>();
        string? cursor = null;
        do
        {
            var page = await _db.Rows.ToCursorPagedAsync(
                BySequence, SortOrder.Asc, cursor, limit: 3, x => x.Id, CancellationToken.None);
            seen.AddRange(page.Items.Select(r => r.Sequence));
            cursor = page.NextCursor;
        } while (cursor is not null);

        Assert.AreEqual(10, seen.Count);
        Assert.AreEqual(10, seen.Distinct().Count());
        AssertAscending(seen);
    }

    [TestMethod]
    public async Task ToCursorPagedAsync_throws_CursorFilterMismatchException_when_cursor_ic_does_not_match_expected()
    {
        // Arrange: seed rows so the keyset filter has real data available.
        await SeedAsync(3);
        // Encode a cursor that was issued with includeDecommissioned=true.
        // Use row-0's Id (non-empty Guid) as the boundary id so Decode accepts it.
        var origin = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var cursorWithIcTrue = CursorCodec.Encode(
            sortValue: origin.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            id: Guid.Parse("00000000-0000-0000-0000-000000000001"),
            direction: SortOrder.Asc,
            includeDecommissioned: true);

        // Act / Assert: the extension throws CursorFilterMismatchException with the right field values.
        var ex = await Assert.ThrowsExactlyAsync<CursorFilterMismatchException>(async () =>
            await _db.Rows.ToCursorPagedAsync(
                ByCreatedAt, SortOrder.Asc, cursorWithIcTrue, limit: 10, x => x.Id,
                x => x.Id, CancellationToken.None,
                expectedIncludeDecommissioned: false));
        Assert.AreEqual("includeDecommissioned", ex.FilterName);
        Assert.AreEqual("true", ex.ExpectedValue);   // cursor was issued with ic=true
        Assert.AreEqual("false", ex.ActualValue);    // request says ic=false
    }

    [TestMethod]
    public async Task ToCursorPagedAsync_with_null_expectedIncludeDecommissioned_skips_filter_check_even_when_cursor_has_ic_true()
    {
        // Arrange: seed several rows so the keyset filter returns real results.
        await SeedAsync(5);
        // Encode a cursor at row-0's boundary (ic=true) using a non-empty Guid so Decode accepts it.
        // The sortValue string must match what NormalizeForCursor produces for a DateTimeOffset:
        // origin.UtcDateTime formatted as "O" (round-trip). The keyset filter then returns rows
        // whose CreatedAt >= origin (row-0 was at origin, so rows 1-4 are strictly after it).
        var origin = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var cursorWithIcTrue = CursorCodec.Encode(
            sortValue: origin.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            id: Guid.Parse("00000000-0000-0000-0000-000000000001"),
            direction: SortOrder.Asc,
            includeDecommissioned: true);

        // Act: expectedIncludeDecommissioned omitted (null) → filter check must be skipped.
        // The keyset filter is applied with the cursor boundary, so rows after row-0 are returned.
        var page = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Asc, cursorWithIcTrue, limit: 10, x => x.Id,
            x => x.Id, CancellationToken.None,
            expectedIncludeDecommissioned: null);

        // Assert: no exception; pagination returns rows after the boundary
        // (rows 1-4 are after the row-0 boundary).
        Assert.IsTrue(page.Items.Any());
    }

    [TestMethod]
    public async Task ToCursorPagedAsync_with_null_expectedIncludeDecommissioned_encodes_next_cursor_with_ic_false()
    {
        // Kills mutation at QueryablePagingExtensions.cs:94: `expectedIncludeDecommissioned ?? false` → `?? true`.
        // With the mutation the next cursor would encode ic=true; this test decodes it and asserts ic=false.
        // Arrange: seed enough rows (> limit) so a NextCursor is produced.
        await SeedAsync(6);

        // Act: call with null expectedIncludeDecommissioned (default) and a small limit so NextCursor is set.
        var page = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Asc, cursor: null, limit: 5, x => x.Id, CancellationToken.None);

        // Assert: NextCursor must exist (6 rows > limit 5) and decode with IncludeDecommissioned == false.
        Assert.IsNotNull(page.NextCursor);
        var decoded = CursorCodec.Decode(page.NextCursor!);
        Assert.IsFalse(decoded.IncludeDecommissioned);
    }
}
