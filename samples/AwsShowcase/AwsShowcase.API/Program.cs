using AwsShowcase.Core;
using AwsShowcase.Integration;
using AwsShowcase.Integration.Persistence;
using Persistence.Common.AWS.DependencyInjection;
using Persistence.Common.AWS.Repositories;

var builder = WebApplication.CreateBuilder(args);

// When pointing at LocalStack (AWS:ServiceURL set), supply dummy credentials so the
// AWS SDK's credential chain is satisfied - LocalStack accepts any values. Real AWS
// runs use a real credential source (profile, role, or environment) instead.
if (!string.IsNullOrEmpty(builder.Configuration["AWS:ServiceURL"]))
{
    Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", "test");
    Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", "test");
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o => o.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
{
    Title = "AWS Common Libraries - Showcase",
    Version = "v1",
    Description = "Clean-architecture sample (API / Core / Entity / Integration) exercising every library service. " +
                  "Runs against LocalStack by default: docker run -p 4566:4566 localstack/localstack"
}));

builder.Services
    .AddShowcaseCore()                                                   // MediatR + logging/resilience/caching pipeline
    .AddShowcaseIntegration(builder.Configuration, builder.Environment); // AWS wiring behind abstractions

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

// X-Ray distributed tracing middleware from the library (off by default locally -
// requires the X-Ray daemon/sidecar to receive segments).
if (app.Configuration.GetValue<bool>("Showcase:EnableXRay"))
{
    app.UseMiddleware<Infrastructure.Common.AWS.XRay.XRayMiddleware>();
}

app.MapControllers();

// Create the DynamoDB tables (incl. the CustomerEmail GSI) on startup. Skipped
// gracefully when neither LocalStack nor AWS is reachable so Swagger still opens.
try
{
    await app.UsePersistenceDynamoAsync<ShowcaseContext>(new DynamoDbRepositoryOptions());
    app.Logger.LogInformation("DynamoDB tables verified/created.");
}
catch (Exception ex)
{
    app.Logger.LogWarning("Could not reach DynamoDB ({Message}). Start LocalStack and restart to use the data endpoints.", ex.Message);
}

await app.RunAsync();
