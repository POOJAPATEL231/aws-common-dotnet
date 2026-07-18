using CloudIntegrator.Core.Interfaces;
using CloudIntegrator.Core.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register services
builder.Services.AddScoped<IDataTransformService, DataTransformService>();
builder.Services.AddScoped<IIntegrationService, IntegrationService>();

// Register cloud services (you'll need to configure connection strings)
builder.Services.AddScoped<ICloudService>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<AzureBlobService>>();
    var connectionString = builder.Configuration.GetConnectionString("AzureStorage") ?? "";
    return new AzureBlobService(connectionString, logger);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
