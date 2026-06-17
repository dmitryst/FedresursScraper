using Lots.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.EntityFrameworkCore.Diagnostics;
using FedresursScraper.Extensions;
using FedresursScraper.UserAds.Hubs;
using FedresursScraper.Services;
using Lots.Application.Extensions;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();

var connectionString = configuration.GetConnectionString("Postgres");

builder.Services.AddLotsDbContext(connectionString!);

// Configure shared services using the extension method from Lots.Data
builder.Services.AddFileStorageServices(configuration);

// Регистрация Application и Infrastructure сервисов, необходимых для WebApi
builder.Services.AddScoped<ILotCopyService, LotCopyService>();
builder.Services.AddScoped<ILotEvaluationService, LotEvaluationService>();
builder.Services.AddVehicleFilterOptions(configuration);
builder.Services.AddVehicleNormalization(configuration, registerBackfillWorker: true);
builder.Services.AddVehicleAttributesAdmin();
builder.Services.AddSingleton<IBiddingDataCache, InMemoryBiddingDataCache>();
builder.Services.AddScoped<TradeResultsImportService>();
builder.Services.AddHttpClient<IIndexNowService, IndexNowService>();

builder.Services.AddScoped<Lots.Application.Services.ContractGenerationService>();

builder.Services.AddTransient<IRosreestrService, RosreestrService>();
builder.Services.AddSingleton<IRosreestrQueue, RosreestrQueue>();
builder.Services.AddSingleton<IClassificationQueue, ClassificationQueue>();

var rosreestrServiceUrl = Environment.GetEnvironmentVariable("ROSREESTR_SERVICE_URL") ?? "http://localhost:8000";
builder.Services.AddHttpClient<IRosreestrServiceClient, RosreestrServiceClient>(client =>
{
    client.BaseAddress = new Uri(rosreestrServiceUrl);
});

// Configure Auth
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = configuration["Jwt:Audience"],
            ValidateLifetime = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"] ?? "default_secret_key_1234567890123456")),
            ValidateIssuerSigningKey = true,
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                context.Token = context.Request.Cookies["access_token"];
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();

// CORS
var myAllowSpecificOrigins = "_myAllowSpecificOrigins";
builder.Services.AddCors(options =>
{
    options.AddPolicy(name: myAllowSpecificOrigins,
        policy =>
        {
            if (builder.Environment.IsDevelopment())
            {
                policy.SetIsOriginAllowed(origin => true)
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials();
            }
            else
            {
                var allowedOrigins = Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS");
                if (!string.IsNullOrEmpty(allowedOrigins))
                {
                    var origins = allowedOrigins.Split(',').Select(o => o.Trim()).ToArray();
                    policy.WithOrigins(origins)
                          .AllowAnyMethod()
                          .AllowAnyHeader()
                          .AllowCredentials();
                }
            }
        });
});

var app = builder.Build();

//app.UseSwagger();
//app.UseSwaggerUI();

app.UseCors(myAllowSpecificOrigins);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/chathub");

// Применение миграций при старте
await ApplyMigrations(app);

await app.RunAsync();

// Вспомогательный метод для применения миграций
static async Task ApplyMigrations(WebApplication app)
{
    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILogger<Program>>();
        try
        {
            var context = services.GetRequiredService<LotsDbContext>();
            await context.Database.MigrateAsync();
            logger.LogInformation("Миграции успешно применены.");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Произошла критическая ошибка при применении миграций.");
            Environment.Exit(1);
        }
    }
}
