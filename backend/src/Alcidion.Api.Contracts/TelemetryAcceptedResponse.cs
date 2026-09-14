namespace Alcidion.Api.Contracts;

/// <summary>The number of accepted frontend events and the request's correlation id.</summary>
public sealed record TelemetryAcceptedResponse(int Received, string CorrelationId);
