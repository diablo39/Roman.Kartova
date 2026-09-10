using System.Linq.Expressions;
using System.Reflection;
using Kartova.SharedKernel.Pagination;
using NetArchTest.Rules;

namespace Kartova.ArchitectureTests;

/// <summary>
/// Enforces ADR-0095 §8: every <c>List*Handler</c> in any module's
/// <c>*.Infrastructure</c> assembly must return <c>Task&lt;CursorPage&lt;T&gt;&gt;</c>,
/// unless the handler class is decorated with <c>[BoundedListResult]</c>.
/// </summary>
[TestClass]
public sealed class PaginationConventionRules
{
    [TestMethod]
    public void List_handlers_in_infrastructure_assemblies_return_CursorPage_or_are_BoundedListResult()
    {
        var infraAssemblies = AssemblyRegistry.AllInfrastructureAssemblies();

        foreach (var asm in infraAssemblies)
        {
            var listHandlers = Types.InAssembly(asm)
                .That()
                .HaveNameMatching(@"^List.*Handler$")
                .And().AreClasses()
                .GetTypes()
                .ToList();

            foreach (var t in listHandlers)
            {
                var bounded = t.GetCustomAttributes(typeof(BoundedListResultAttribute), inherit: false)
                    .Cast<BoundedListResultAttribute>()
                    .FirstOrDefault();

                if (bounded is not null)
                {
                    Assert.IsFalse(
                        string.IsNullOrWhiteSpace(bounded.Reason),
                        $"{t.FullName} is [BoundedListResult] — reason must be set");
                    continue;
                }

                var handle = t.GetMethod("Handle")
                    ?? throw new InvalidOperationException($"{t.FullName} has no Handle method");
                var ret = handle.ReturnType;

                Assert.IsTrue(
                    ret.IsGenericType,
                    $"{t.FullName}.Handle must return Task<CursorPage<...>> per ADR-0095");
                Assert.AreEqual(
                    typeof(Task<>),
                    ret.GetGenericTypeDefinition(),
                    $"{t.FullName}.Handle must return Task<CursorPage<...>> per ADR-0095");
                var inner = ret.GetGenericArguments()[0];
                Assert.IsTrue(
                    inner.IsGenericType,
                    $"{t.FullName}.Handle must return Task<CursorPage<...>> per ADR-0095");
                Assert.AreEqual(
                    typeof(CursorPage<>),
                    inner.GetGenericTypeDefinition(),
                    $"{t.FullName}.Handle returns {ret} — must be Task<CursorPage<...>> per ADR-0095, " +
                    "or annotate the class with [BoundedListResult(reason: \"...\")]");
            }
        }
    }

    /// <summary>
    /// Enforces the TD-001 null-safety invariant: a <see cref="SortSpec{TEntity}"/> whose key
    /// selector reads a <b>nullable</b> CLR member (a nullable reference or <see cref="Nullable{T}"/>
    /// column) MUST set <c>IsNullable = true</c>. Otherwise the shared keyset predicate evaluates to
    /// SQL UNKNOWN at a NULL boundary and pagination silently truncates — a data-loss footgun the
    /// type system can't catch, since the boxing <c>Expression&lt;Func&lt;T,object&gt;&gt;</c> erases
    /// nullability. Selectors that aren't a plain member access (e.g. the VM JSONB
    /// <c>jsonb_extract_path_text</c> expression selectors) can't be resolved to a CLR property here
    /// and are skipped — that's the documented deferral (they rely on the write-path presence
    /// invariant; see ADR-0095 amendment + tech-debt TD-001).
    /// </summary>
    [TestMethod]
    public void SortSpecs_over_a_nullable_key_member_must_set_IsNullable()
    {
        var nullabilityContext = new NullabilityInfoContext();
        var offenders = new List<string>();
        var nullableSpecsInspected = 0;

        foreach (var asm in AssemblyRegistry.AllProduction())
        {
            foreach (var type in asm.GetTypes())
            {
                var specFields = type
                    .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .Where(f => f.FieldType.IsGenericType
                        && f.FieldType.GetGenericTypeDefinition() == typeof(SortSpec<>));

                foreach (var field in specFields)
                {
                    var spec = field.GetValue(null);
                    if (spec is null) continue;

                    var specType = field.FieldType;
                    var keySelector = (LambdaExpression)specType.GetProperty(nameof(SortSpec<object>.KeySelector))!.GetValue(spec)!;
                    var isNullable = (bool)specType.GetProperty(nameof(SortSpec<object>.IsNullable))!.GetValue(spec)!;
                    var fieldName = (string)specType.GetProperty(nameof(SortSpec<object>.FieldName))!.GetValue(spec)!;

                    var member = TryGetSelectedProperty(keySelector);
                    if (member is null) continue; // expression selector (e.g. JSONB) — documented exception.

                    if (!IsNullableMember(member, nullabilityContext)) continue;
                    nullableSpecsInspected++;
                    if (!isNullable)
                    {
                        offenders.Add(
                            $"{type.FullName}.{field.Name} (sortBy '{fieldName}') selects nullable member " +
                            $"'{member.DeclaringType!.Name}.{member.Name}' but has IsNullable=false");
                    }
                }
            }
        }

        Assert.AreEqual(
            0,
            offenders.Count,
            "A SortSpec over a nullable key must set IsNullable=true or keyset pagination silently " +
            "truncates at a NULL boundary (TD-001):\n" + string.Join("\n", offenders));

        // Guard against a vacuous pass: at least one nullable-member spec (e.g.
        // InfrastructureSortSpecs.Provider) must have been detected, proving the reflection +
        // nullability detection actually fired rather than silently matching nothing.
        Assert.IsTrue(
            nullableSpecsInspected > 0,
            "Expected to inspect at least one SortSpec over a nullable member (e.g. Provider); " +
            "found none — the reflection/nullability detection is not working.");
    }

    /// <summary>Resolves the CLR property a key selector reads, unwrapping the boxing
    /// <c>Convert</c> (and the erased <c>!</c> null-forgiving op). Returns <see langword="null"/>
    /// for any selector that is not a simple property access on the lambda parameter — method calls
    /// (<c>EF.Property</c>, JSONB helpers), constants, etc.</summary>
    private static PropertyInfo? TryGetSelectedProperty(LambdaExpression selector)
    {
        var body = selector.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } convert)
        {
            body = convert.Operand;
        }
        return body is MemberExpression { Member: PropertyInfo prop, Expression: ParameterExpression }
            ? prop
            : null;
    }

    private static bool IsNullableMember(PropertyInfo property, NullabilityInfoContext context)
    {
        if (Nullable.GetUnderlyingType(property.PropertyType) is not null) return true;
        if (property.PropertyType.IsValueType) return false;
        return context.Create(property).ReadState == NullabilityState.Nullable;
    }
}
