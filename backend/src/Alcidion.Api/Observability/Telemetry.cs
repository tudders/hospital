using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Alcidion.Api.Observability;

/// <summary>Single place for custom spans and metrics so names stay consistent.</summary>
public static class Telemetry
{
    public const string ActivitySourceName = "Alcidion.Api";
    public const string MeterName = "Alcidion.Api";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> PatientsRegistered = Meter.CreateCounter<long>("alcidion.patients.registered");
    public static readonly Counter<long> PatientsAdmitted = Meter.CreateCounter<long>("alcidion.patients.admitted");
    public static readonly Counter<long> ClientEvents = Meter.CreateCounter<long>("alcidion.client.events");
}
