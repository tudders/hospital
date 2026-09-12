# API

dotnet

skills https://github.com/dotnet/skills

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddOpenApi(); // Built-in in modern .NET, or use AddSwaggerGen()

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
app.MapOpenApi(); // Generates /openapi/v1.json
}

tdd
dep injections
SOLID
Go4 patterns - always reference
observability, link front and back

demonstrate attributes and auth

mock microservice with 2 domains? and events between them?

# Frontend

skills: vercel react https://agenticskills.io/skills/react-best-practices
performance
bundle size
observability - event and session recording, link to backend w/ correlation.
quick and dirty desgin system.
Events?

# database

Not sure if needed at this point

# pocock skills

Talk about a life cycle.

Hooks

https://github.com/mattpocock/skills

monorepo

additions 1.

redis cloud.
Cron to trigger - simulation to redis.
authenticated subscriber to redis.
3 mock channels - personnal, hospital, ward.
Example - show notifications.

What is a nice graphic of
room, ward, floor ...
When an event comes, it updates the graphic.

Add validation to API, example fluent validation.
