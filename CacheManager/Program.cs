using Microsoft.AspNetCore.Server.Kestrel.Core;
using StackExchange.Redis;
using CacheManager.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddGrpc();

// Configure Redis connection
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var configuration = ConfigurationOptions.Parse("127.0.0.1:6379", true);
    // If Redis requires a password, include it here
    // configuration.Password = "your_redis_password";
    return ConnectionMultiplexer.Connect(configuration);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapGrpcService<CacheManagerService>();
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client.");
app.MapGet("/test-redis", async (IConnectionMultiplexer redis) =>
{
    var db = redis.GetDatabase();
    bool pong = await db.PingAsync() != TimeSpan.Zero;
    return pong ? Results.Ok("Redis is connected.") : Results.StatusCode(500);
});

app.Run();
