using System.Reflection;
using Alcidion.Api.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit.Abstractions;

namespace Alcidion.Api.Tests;

/// <summary>
/// ADR 0001 makes JSON Schema the source of truth for request bodies. This is the test that keeps
/// that true as the API grows from five endpoints to thirty: a body that is not generated from a
/// schema is a 400 whose rules are not in the published document, and the frontend goes back to
/// re-implementing them by hand.
/// </summary>
public class RequestBodyContractTests(ITestOutputHelper output)
{
    /// <summary>
    /// Bodies still bound C#-first, from before the ADR. Each name here is an endpoint whose
    /// constraints the published contract does not carry; delete a line when its schema lands.
    /// A new endpoint may not be added to this list - that is the point of it being a list.
    /// </summary>
    private static readonly string[] OutstandingMigrations = ["AdmitPatientCommand", "LoginRequest"];

    [Fact]
    public void Every_request_body_is_generated_from_a_schema()
    {
        var unmigrated = Bodies()
            .Where(b => !JsonSchemaInputFormatter.IsSchemaGenerated(b.Type))
            .ToList();

        output.WriteLine($"Outstanding migrations: {string.Join(", ", unmigrated.Select(b => b.Describe()))}");

        var unexpected = unmigrated.Where(b => !OutstandingMigrations.Contains(b.Type.Name)).ToList();
        Assert.True(unexpected.Count == 0,
            "These bodies are not generated from a JSON Schema, so their rules are not in the published " +
            "contract. See docs/adr/0001-schema-first-request-validation.md." + Environment.NewLine +
            string.Join(Environment.NewLine, unexpected.Select(b => "  " + b.Describe())));
    }

    [Fact]
    public void The_outstanding_migration_list_names_only_bodies_that_still_exist()
    {
        // Otherwise the list outlives the work and stops meaning anything.
        var bodies = Bodies().Select(b => b.Type.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(OutstandingMigrations.Where(name => !bodies.Contains(name)));
    }

    private sealed record Body(MethodInfo Action, Type Type)
    {
        public string Describe() => $"{Action.DeclaringType!.Name}.{Action.Name} takes {Type.Name}";
    }

    /// <summary>Every request body the API can be sent, whether or not it says <c>[FromBody]</c>:
    /// <c>[ApiController]</c> infers the body source for a complex parameter, so a missing
    /// attribute must not be a way past this test.</summary>
    private static IEnumerable<Body> Bodies() =>
        from type in typeof(Program).Assembly.GetTypes()
        where typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract
        from action in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        where AcceptsABody(action)
        from parameter in action.GetParameters()
        where IsBody(parameter)
        select new Body(action, parameter.ParameterType);

    /// <summary>True for an action routed on a verb that carries a body. A GET with one would be
    /// the bug, not the case to check.</summary>
    private static bool AcceptsABody(MethodInfo action) =>
        action.GetCustomAttributes<HttpMethodAttribute>()
            .SelectMany(a => a.HttpMethods)
            .Any(verb => verb is not ("GET" or "HEAD" or "DELETE" or "OPTIONS"));

    private static bool IsBody(ParameterInfo parameter)
    {
        if (parameter.GetCustomAttribute<FromBodyAttribute>() is not null) return true;

        // Anything with a different source named, anything the framework supplies, and anything
        // simple enough to arrive in the route or query string, is not a body.
        if (parameter.GetCustomAttributes().Any(a =>
                a is FromQueryAttribute or FromRouteAttribute or FromHeaderAttribute or FromFormAttribute or FromServicesAttribute))
        {
            return false;
        }

        return !IsSimple(parameter.ParameterType) && parameter.ParameterType != typeof(CancellationToken);
    }

    private static bool IsSimple(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying.IsPrimitive
            || underlying.IsEnum
            || underlying == typeof(string)
            || underlying == typeof(decimal)
            || underlying == typeof(Guid)
            || underlying == typeof(DateTime)
            || underlying == typeof(DateTimeOffset)
            || underlying == typeof(DateOnly)
            || underlying == typeof(TimeOnly)
            || underlying == typeof(TimeSpan)
            || underlying == typeof(Uri);
    }
}
