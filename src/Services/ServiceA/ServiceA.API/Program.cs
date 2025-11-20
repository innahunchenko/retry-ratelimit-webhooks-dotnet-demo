using Microsoft.AspNetCore.RateLimiting;
using Polly;
using Polly.Extensions.Http;
using System.Threading.RateLimiting;
using System.Threading.Channels;

var builder = WebApplication.CreateBuilder(args);

// RateLimiter (concurrency) for webhooks
builder.Services.AddRateLimiter(options =>
{
    options.AddConcurrencyLimiter("webhook", config =>
    {
        config.PermitLimit = 5; // concurrent webhook handlers
        config.QueueLimit = 10; // extra queued webhooks
        config.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

builder.Services.AddHttpClient("serviceB", client =>
{
    client.BaseAddress = new Uri("http://service-b");
    //client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
})
    .AddPolicyHandler(GetRetryPolicy());

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.UseRateLimiter();

var channel = Channel.CreateBounded<string>(5);

app.MapPost("/webhook", async (HttpContext ctx) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var payload = await reader.ReadToEndAsync();

    if (!await channel.Writer.WaitToWriteAsync())
        return Results.StatusCode(429);

    await channel.Writer.WriteAsync(payload);
    return Results.Ok(new { received = true });
})
    .RequireRateLimiting("webhook"); // protected by concurrency limiter

// begin the flow: A -> B -> C -> webhook(A)
app.MapPost("/start", async (IHttpClientFactory httpFactory) =>
{
    var id = Guid.NewGuid().ToString();
    var payload = $"task:{id}";
    await channel.Writer.WriteAsync(payload); // push a payload to start process
    return Results.Ok(new { started = id });
});

// Background processor
app.Lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(async () =>
    {
        var factory = app.Services.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient("serviceB");

        await foreach (var msg in channel.Reader.ReadAllAsync())
        {
            Console.WriteLine($"[A] Processing {msg}");

            // call service B, it may return 429/500
            try
            {
                var resp = await client.PostAsync("/api/process", new StringContent(msg));
                var body = await resp.Content.ReadAsStringAsync();
                Console.WriteLine($"[A] ServiceB responded: ${(int)resp.StatusCode} - {body}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[A] Exception calling B: {ex.Message}");
            }
        }
    });
});

app.Run();

static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        .WaitAndRetryAsync(
            retryCount: 5,
            sleepDurationProvider: (attempt, response, context) =>
            {
                // if 429 - delay from the Retry-After header
                if (response?.Result?.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                    && response.Result.Headers.TryGetValues("Retry-After", out var values))
                {
                    if (int.TryParse(values.FirstOrDefault(), out var secs))
                        return TimeSpan.FromSeconds(secs); 
                }

                // if 500 - exponential backoff
                return TimeSpan.FromSeconds(Math.Pow(2, attempt)); 
            },

        onRetryAsync: async (outcome, timespan, retryNumber, ctx) =>
        {
            Console.WriteLine($"[A] Retry #{retryNumber} waiting {timespan}");
            await Task.CompletedTask;
        });
}