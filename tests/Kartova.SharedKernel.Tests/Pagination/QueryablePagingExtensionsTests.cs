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
        public string? NullableName { get; set; }
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

    // TD-001: nullable key sort, opts into the null-safe keyset path (NULLS LAST asc / FIRST desc).
    private static readonly SortSpec<TestRow> ByNullableName =
        new("nullableName", x => x.NullableName!) { IsNullable = true };

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

    private static readonly string OriginIso =
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).UtcDateTime
            .ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private static readonly Guid Row1Id = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static IReadOnlyDictionary<string, string> Filters(params (string K, string V)[] pairs)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in pairs) d[k] = v;
        return d;
    }

    [TestMethod]
    public async Task FilterMismatch_on_changed_value_reports_filter_name_expected_and_actual()
    {
        await SeedAsync(3);
        var cursor = CursorCodec.Encode(
            OriginIso, Row1Id, SortOrder.Asc,
            Filters(("includeDecommissioned", "true")));

        var ex = await Assert.ThrowsExactlyAsync<CursorFilterMismatchException>(async () =>
            await _db.Rows.ToCursorPagedAsync(
                ByCreatedAt, SortOrder.Asc, cursor, limit: 10, x => x.Id,
                x => x.Id, CancellationToken.None,
                expectedFilters: Filters(("includeDecommissioned", "false"))));

        Assert.AreEqual("includeDecommissioned", ex.FilterName);
        Assert.AreEqual("true", ex.ExpectedValue);
        Assert.AreEqual("false", ex.ActualValue);
    }

    [TestMethod]
    public async Task FilterMismatch_reports_ownerUserId_key_when_owner_changes()
    {
        await SeedAsync(3);
        var cursor = CursorCodec.Encode(
            OriginIso, Row1Id, SortOrder.Asc,
            Filters(("includeDecommissioned", "false"), ("ownerUserId", "aaaaaaaa-0000-0000-0000-000000000001")));

        var ex = await Assert.ThrowsExactlyAsync<CursorFilterMismatchException>(async () =>
            await _db.Rows.ToCursorPagedAsync(
                ByCreatedAt, SortOrder.Asc, cursor, limit: 10, x => x.Id,
                x => x.Id, CancellationToken.None,
                expectedFilters: Filters(("includeDecommissioned", "false"), ("ownerUserId", "aaaaaaaa-0000-0000-0000-000000000002"))));

        Assert.AreEqual("ownerUserId", ex.FilterName);
        Assert.AreEqual("aaaaaaaa-0000-0000-0000-000000000001", ex.ExpectedValue);
        Assert.AreEqual("aaaaaaaa-0000-0000-0000-000000000002", ex.ActualValue);
    }

    [TestMethod]
    public async Task Cursor_carrying_filters_replayed_against_no_filter_request_throws_mismatch()
    {
        await SeedAsync(5);
        var cursor = CursorCodec.Encode(
            OriginIso, Row1Id, SortOrder.Asc,
            Filters(("includeDecommissioned", "true")));

        var ex = await Assert.ThrowsExactlyAsync<CursorFilterMismatchException>(async () =>
            await _db.Rows.ToCursorPagedAsync(
                ByCreatedAt, SortOrder.Asc, cursor, limit: 10, x => x.Id,
                x => x.Id, CancellationToken.None,
                expectedFilters: null));

        Assert.AreEqual("includeDecommissioned", ex.FilterName);
        Assert.AreEqual("true", ex.ExpectedValue);
        Assert.AreEqual("(none)", ex.ActualValue);
    }

    [TestMethod]
    public async Task MatchingFilters_do_not_throw_and_return_rows()
    {
        await SeedAsync(5);
        var cursor = CursorCodec.Encode(
            OriginIso, Row1Id, SortOrder.Asc,
            Filters(("includeDecommissioned", "false")));

        var page = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Asc, cursor, limit: 10, x => x.Id,
            x => x.Id, CancellationToken.None,
            expectedFilters: Filters(("includeDecommissioned", "false")));

        // Matching filters must NOT interfere with the keyset: the cursor boundary
        // (origin / Row1Id) excludes only row-0 (CreatedAt == origin, id 000 < 001),
        // so rows 1-4 come back. Stronger than Any(): proves paging still advances
        // past the boundary rather than returning the whole table.
        Assert.AreEqual(4, page.Items.Count());
        Assert.IsFalse(page.Items.Any(r => r.Id == Guid.Empty),
            "Row-0 (id 000) is the keyset boundary and must be excluded.");
    }

    [TestMethod]
    public async Task NextCursor_round_trips_the_expectedFilters()
    {
        await SeedAsync(6);
        var expected = Filters(
            ("includeDecommissioned", "true"),
            ("ownerUserId", "aaaaaaaa-0000-0000-0000-000000000009"));

        var page = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Asc, cursor: null, limit: 5, x => x.Id,
            x => x.Id, CancellationToken.None,
            expectedFilters: expected);

        Assert.IsNotNull(page.NextCursor);
        var decoded = CursorCodec.Decode(page.NextCursor!);
        Assert.AreEqual("true", decoded.Filters["includeDecommissioned"]);
        Assert.AreEqual("aaaaaaaa-0000-0000-0000-000000000009", decoded.Filters["ownerUserId"]);
    }

    [TestMethod]
    public async Task No_expectedFilters_encodes_next_cursor_without_filter_state()
    {
        await SeedAsync(6);

        var page = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Asc, cursor: null, limit: 5, x => x.Id, CancellationToken.None);

        Assert.IsNotNull(page.NextCursor);
        var decoded = CursorCodec.Decode(page.NextCursor!);
        Assert.AreEqual(0, decoded.Filters.Count);
    }

    // ---- TD-004: cursor binds sortBy; a mid-pagination sort-field switch is a 400 --------

    [TestMethod]
    public async Task SortFieldMismatch_when_cursor_replayed_under_a_different_sortBy_throws()
    {
        await SeedAsync(3);
        // Cursor issued under sortBy=name (same sortOrder=asc) …
        var cursor = CursorCodec.Encode(
            "row-001", Row1Id, SortOrder.Asc, filters: null, sortField: "name");

        // … replayed under sortBy=createdAt → the new field's keyset predicate would run
        // against the old field's boundary value. Guard fires before that: 400.
        var ex = await Assert.ThrowsExactlyAsync<CursorSortFieldMismatchException>(async () =>
            await _db.Rows.ToCursorPagedAsync(
                ByCreatedAt, SortOrder.Asc, cursor, limit: 10, x => x.Id,
                x => x.Id, CancellationToken.None));

        Assert.AreEqual("name", ex.ExpectedField);
        Assert.AreEqual("createdAt", ex.ActualField);
    }

    [TestMethod]
    public async Task MatchingSortField_does_not_throw_and_returns_rows()
    {
        await SeedAsync(5);
        var cursor = CursorCodec.Encode(
            OriginIso, Row1Id, SortOrder.Asc, filters: null, sortField: "createdAt");

        var page = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Asc, cursor, limit: 10, x => x.Id,
            x => x.Id, CancellationToken.None);

        // Boundary (origin / Row1Id) excludes only row-0, so rows 1-4 come back — proves
        // the matching-field check does not interfere with the keyset.
        Assert.AreEqual(4, page.Items.Count());
    }

    [TestMethod]
    public async Task OldCursor_without_sortField_is_accepted_under_any_sortBy_backward_compat()
    {
        await SeedAsync(5);
        // Pre-TD-004 cursor: no `sf` discriminator.
        var cursor = CursorCodec.Encode(OriginIso, Row1Id, SortOrder.Asc);

        // Absent `sf` decodes as "no field recorded" → no check, no throw.
        var page = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Asc, cursor, limit: 10, x => x.Id,
            x => x.Id, CancellationToken.None);

        Assert.AreEqual(4, page.Items.Count());
    }

    [TestMethod]
    public async Task NextCursor_round_trips_the_sortField()
    {
        await SeedAsync(6);

        var page = await _db.Rows.ToCursorPagedAsync(
            ByName, SortOrder.Asc, cursor: null, limit: 5, x => x.Id,
            x => x.Id, CancellationToken.None);

        Assert.IsNotNull(page.NextCursor);
        var decoded = CursorCodec.Decode(page.NextCursor!);
        Assert.AreEqual("name", decoded.SortField);
    }

    // ---- TD-001: null-safe keyset pagination for IsNullable sort keys --------------------

    /// <summary>
    /// Seeds rows whose nullable sort key is a mix of present and NULL. The three named rows sort
    /// before/after the three NULL rows depending on direction; the NULL block is large enough
    /// (3 rows) that a small page boundary necessarily lands inside it — the exact condition that
    /// silently truncated pagination before TD-001.
    /// </summary>
    private async Task SeedWithNullsAsync()
    {
        string?[] names = ["alpha", null, "bravo", null, "charlie", null];
        for (var i = 0; i < names.Length; i++)
        {
            _db.Rows.Add(new TestRow
            {
                Id = Guid.Parse($"00000000-0000-0000-0000-{i:D12}"),
                Name = $"row-{i:D3}",
                CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(i),
                UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i),
                OwnerId = Guid.NewGuid(),
                Sequence = i,
                NullableName = names[i],
            });
        }
        await _db.SaveChangesAsync();
    }

    private async Task<List<TestRow>> PaginateAllAsync(SortSpec<TestRow> sort, SortOrder order, int limit)
    {
        var all = new List<TestRow>();
        string? cursor = null;
        // Bounded to avoid an infinite loop if paging ever fails to advance.
        for (var guard = 0; guard < 100; guard++)
        {
            var page = await _db.Rows.ToCursorPagedAsync(
                sort, order, cursor, limit, x => x.Id, CancellationToken.None);
            all.AddRange(page.Items);
            if (page.NextCursor is null) return all;
            cursor = page.NextCursor;
        }
        Assert.Fail("Pagination did not terminate within the guard limit.");
        return all;
    }

    [TestMethod]
    public async Task NullableSort_asc_pages_through_null_boundary_without_dropping_rows()
    {
        await SeedWithNullsAsync();

        // limit 2 over 6 rows → a page boundary falls inside the trailing 3-row NULL block.
        var all = await PaginateAllAsync(ByNullableName, SortOrder.Asc, limit: 2);

        Assert.AreEqual(6, all.Count, "every row must be returned exactly once across all pages");
        CollectionAssert.AreEquivalent(
            Enumerable.Range(0, 6).Select(i => Guid.Parse($"00000000-0000-0000-0000-{i:D12}")).ToList(),
            all.Select(r => r.Id).ToList());
        // NULLS LAST: the three named rows come first (ascending), then the three NULLs.
        CollectionAssert.AreEqual(
            new[] { "alpha", "bravo", "charlie" },
            all.Where(r => r.NullableName is not null).Select(r => r.NullableName).ToList());
        Assert.IsTrue(all.Skip(3).All(r => r.NullableName is null), "NULLs must sort last (asc)");
    }

    [TestMethod]
    public async Task NullableSort_desc_pages_through_null_boundary_without_dropping_rows()
    {
        await SeedWithNullsAsync();

        var all = await PaginateAllAsync(ByNullableName, SortOrder.Desc, limit: 2);

        Assert.AreEqual(6, all.Count);
        CollectionAssert.AreEquivalent(
            Enumerable.Range(0, 6).Select(i => Guid.Parse($"00000000-0000-0000-0000-{i:D12}")).ToList(),
            all.Select(r => r.Id).ToList());
        // NULLS FIRST: the three NULLs lead, then the named rows descending.
        Assert.IsTrue(all.Take(3).All(r => r.NullableName is null), "NULLs must sort first (desc)");
        CollectionAssert.AreEqual(
            new[] { "charlie", "bravo", "alpha" },
            all.Where(r => r.NullableName is not null).Select(r => r.NullableName).ToList());
    }

    [TestMethod]
    public async Task NullableSort_single_page_returns_all_rows_when_limit_exceeds_count()
    {
        await SeedWithNullsAsync();

        var page = await _db.Rows.ToCursorPagedAsync(
            ByNullableName, SortOrder.Asc, cursor: null, limit: 50, x => x.Id, CancellationToken.None);

        Assert.AreEqual(6, page.Items.Count());
        Assert.IsNull(page.NextCursor);
    }

    [TestMethod]
    public async Task NullableSort_boundary_cursor_inside_null_block_encodes_null_key()
    {
        await SeedWithNullsAsync();

        // asc, limit 4 → page 1 = [alpha, bravo, charlie, <first null>]; the boundary row's key is NULL.
        var page = await _db.Rows.ToCursorPagedAsync(
            ByNullableName, SortOrder.Asc, cursor: null, limit: 4, x => x.Id, CancellationToken.None);

        Assert.IsNotNull(page.NextCursor);
        var decoded = CursorCodec.Decode(page.NextCursor!);
        Assert.IsInstanceOfType<CursorNullSortValue>(decoded.SortValue);

        // Resuming from that null-key cursor returns exactly the remaining two NULL rows.
        var page2 = await _db.Rows.ToCursorPagedAsync(
            ByNullableName, SortOrder.Asc, page.NextCursor, limit: 4, x => x.Id, CancellationToken.None);
        Assert.AreEqual(2, page2.Items.Count());
        Assert.IsTrue(page2.Items.All(r => r.NullableName is null));
        Assert.IsNull(page2.NextCursor);
    }
}
