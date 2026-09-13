using System.Globalization;
using Corvus.Text.Json;

namespace Alcidion.Api.Contracts;

/// <summary>
/// One recorded frontend event, as plain values.
/// </summary>
/// <param name="Seq">Monotonic per session: orders the session regardless of the order batches
/// arrive in.</param>
/// <param name="T">Milliseconds from session start, so a session can be replayed at the pace the
/// user experienced.</param>
/// <param name="Props">The event payload, as the JSON the caller sent. It is open by contract, so
/// there is nothing to deserialise it into - it is carried through to the log line verbatim.</param>
public sealed record ClientEvent(string Name, string SessionId, DateTimeOffset At, long Seq, long T, string? Props);

/// <summary>
/// Body of <c>POST /api/telemetry/events</c>, generated from <c>Schemas/client-event-batch.json</c>.
/// </summary>
/// <remarks>
/// The first array-rooted body in the codebase: a hole in the batch ("[null]") is a schema failure
/// at <c>[0]</c> rather than something the action has to guard against by hand.
/// </remarks>
[JsonSchemaTypeGenerator("Schemas/client-event-batch.json")]
public readonly partial struct ClientEventBatch : IEnumerable<ClientEventBatch.ItemsEntity>
{
    /// <summary>
    /// Declared so that System.Text.Json classifies the type as a collection.
    /// </summary>
    /// <remarks>
    /// Nothing in this codebase enumerates the batch through the interface - <see cref="ToEvents"/>
    /// uses the generated enumerator. It is here because ASP.NET's OpenAPI generator walks into
    /// whatever a schema transformer leaves behind and resolves each child against the reflected
    /// type: the published <c>items</c> sends it looking for this type's element type, and without
    /// this it finds none and the document endpoint throws. An array contract whose C# type does
    /// not read as a sequence is the actual defect; this fixes that rather than the symptom.
    /// </remarks>
    IEnumerator<ItemsEntity> IEnumerable<ItemsEntity>.GetEnumerator()
    {
        for (var i = 0; i < GetArrayLength(); i++) yield return this[i];
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        ((IEnumerable<ItemsEntity>)this).GetEnumerator();

    /// <summary>
    /// Copies the batch out as plain records. Every conversion is safe because the schema has
    /// already been evaluated - the formatter fails the request before the action runs.
    /// </summary>
    /// <remarks>
    /// Copying rather than enumerating in place is the point: these values reach
    /// <c>ILogger</c>, whose providers may hold the state object past the end of the request, and
    /// the batch is only a view over a pooled document disposed with the response.
    /// </remarks>
    /// <para>
    /// Both timeline fields are optional and default to 0. An absent property reads back as
    /// <c>Undefined</c>, which no numeric conversion accepts, so each is checked before it is read.
    /// </para>
    public IReadOnlyList<ClientEvent> ToEvents()
    {
        var events = new ClientEvent[GetArrayLength()];
        var i = 0;
        foreach (var e in EnumerateArray())
        {
            events[i++] = new ClientEvent(
                (string)e.Name,
                (string)e.SessionId,
                DateTimeOffset.Parse((string)e.At, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                e.Seq.ValueKind is JsonValueKind.Number ? (long)e.Seq : 0,
                e.T.ValueKind is JsonValueKind.Number ? (long)e.T : 0,
                e.Props.ValueKind is JsonValueKind.Object ? e.Props.ToString() : null);
        }

        return events;
    }
}
