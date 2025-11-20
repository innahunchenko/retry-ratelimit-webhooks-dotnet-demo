using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRateLimiter(options =>
{
    options.AddTokenBucketLimiter("token", config =>
    {
        config.TokenLimit = 5; // max tokens in bucket
        config.TokensPerPeriod = 1; // replenish 1 token per period
        config.ReplenishmentPeriod = TimeSpan.FromSeconds(2);
        config.AutoReplenishment = true;
        config.QueueLimit = 0; // don't queue inbound requests
    });
});

builder.Services.AddHttpClient("serviceC", client =>
{
    client.BaseAddress = new Uri("http://service-c");
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseRateLimiter();

// simulates work and sometimes returns 500 or 429
app.MapPost("/api/process", async (HttpContext ctx, IHttpClientFactory factory) =>
{
    // Rate limited by token bucket
    using var reader = new StreamReader(ctx.Request.Body);
    var payload = await reader.ReadToEndAsync();

    // Randomly fail to simulate transient errors (500)
    var rnd = new Random();
    var r = rnd.NextDouble();
    if (r < 0.2)
    {
        Console.WriteLine("[B] Simulating 500");
        return Results.StatusCode(500);
    }

    Console.WriteLine($"[B] Accepted payload: {payload}");

    // forward to service C for long-running work
    // that may hit token bucket -> if token not available - returns 429
    var client = factory.CreateClient("serviceC");
    var resp = await client.PostAsync("/do-work", new StringContent(payload));
    return Results.Ok(new { forwarded = resp.StatusCode.ToString() });
})
    .RequireRateLimiting("token");

app.Run();