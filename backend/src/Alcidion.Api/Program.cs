using Alcidion.Admissions;
using Alcidion.Api.Auth;
using Alcidion.Api.Configuration;
using Alcidion.Api.Contracts;
using Alcidion.Api.Observability;
using Alcidion.Hospital;
using Alcidion.Patients;
using Alcidion.Shared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// --- Domains (each bounded context registers its own services) ---
// One resolved connection string decides the store for every context: with it they read and write
// the hospital database through EF Core, without it the writing domains run on their in-memory
// repositories and the occupancy view reports itself unavailable. Either way the API starts, which
// is what keeps the integration tests hermetic with no database attached.
var hospitalConnection = HospitalConnection.Resolve(builder.Configuration, builder.Environment);
builder.Services
    .AddSharedKernel()
    .AddPatientsDomain(hospitalConnection)
    .AddAdmissionsDomain(hospitalConnection)
    .AddHospitalReadModel(hospitalConnection);

// --- Web ---
// Schema-generated request types bind and validate through their own formatter; everything else
// keeps falling through to System.Text.Json. See docs/adr/0001-schema-first-request-validation.md.
builder.Services.AddControllers(o => o.InputFormatters.Insert(0, new JsonSchemaInputFormatter()));
builder.Services.AddOpenApi(o => o.AddSchemaTransformer<JsonSchemaOpenApiTransformer>());
builder.Services.AddProblemDetails();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders(CorrelationIdMiddleware.HeaderName)));

// --- Auth: JWT bearer with a symmetric dev key; swap for an IdP by changing config ---
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? throw new InvalidOperationException("Jwt config missing.");
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DevTokenIssuer>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = jwt.SigningKey,
        ClockSkew = TimeSpan.FromSeconds(30),
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Clinician, p => p.RequireRole(Roles.Clinician, Roles.Admin))
    .AddPolicy(Policies.Admin, p => p.RequireRole(Roles.Admin));

// --- Observability: traces + metrics via OpenTelemetry, correlation id on every log line ---
// Without IncludeScopes the correlation-id scope is carried but never printed, so ordinary log
// lines come out unjoinable to the request that produced them.
builder.Logging.AddSimpleConsole(o =>
{
    o.IncludeScopes = true;
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});
builder.Logging.Configure(o => o.ActivityTrackingOptions =
    Microsoft.Extensions.Logging.ActivityTrackingOptions.TraceId | Microsoft.Extensions.Logging.ActivityTrackingOptions.SpanId);
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("alcidion-api", serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString()))
    .WithTracing(t =>
    {
        t.AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"));
        t.AddSource(Telemetry.ActivitySourceName);
        if (builder.Configuration["Otlp:Endpoint"] is { Length: > 0 } otlp) t.AddOtlpExporter(o => o.Endpoint = new Uri(otlp));
        else if (builder.Environment.IsDevelopment()) t.AddConsoleExporter();
    })
    .WithMetrics(m =>
    {
        m.AddAspNetCoreInstrumentation();
        m.AddMeter(Telemetry.MeterName);
        if (builder.Configuration["Otlp:Endpoint"] is { Length: > 0 } otlp) m.AddOtlpExporter(o => o.Endpoint = new Uri(otlp));
    });

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<CorrelationIdMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.Run();

/// <summary>Exposed so integration tests can use WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program;
