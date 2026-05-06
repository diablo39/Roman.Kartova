using FluentAssertions;
using Kartova.SharedKernel.Pagination;
using NetArchTest.Rules;

namespace Kartova.ArchitectureTests;

/// <summary>
/// Enforces ADR-0095 §8: every <c>List*Handler</c> in any module's
/// <c>*.Infrastructure</c> assembly must return <c>Task&lt;CursorPage&lt;T&gt;&gt;</c>,
/// unless the handler class is decorated with <c>[BoundedListResult]</c>.
/// </summary>
public sealed class PaginationConventionRules
{
    [Fact]
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
                    bounded.Reason.Should().NotBeNullOrWhiteSpace(
                        because: $"{t.FullName} is [BoundedListResult] — reason must be set");
                    continue;
                }

                var handle = t.GetMethod("Handle")
                    ?? throw new InvalidOperationException($"{t.FullName} has no Handle method");
                var ret = handle.ReturnType;

                ret.IsGenericType.Should().BeTrue(
                    because: $"{t.FullName}.Handle must return Task<CursorPage<...>> per ADR-0095");
                ret.GetGenericTypeDefinition().Should().Be(typeof(Task<>),
                    because: $"{t.FullName}.Handle must return Task<CursorPage<...>> per ADR-0095");
                var inner = ret.GetGenericArguments()[0];
                inner.IsGenericType.Should().BeTrue(
                    because: $"{t.FullName}.Handle must return Task<CursorPage<...>> per ADR-0095");
                inner.GetGenericTypeDefinition().Should().Be(typeof(CursorPage<>),
                    because: $"{t.FullName}.Handle returns {ret} — must be Task<CursorPage<...>> per ADR-0095, " +
                             "or annotate the class with [BoundedListResult(reason: \"...\")]");
            }
        }
    }
}
