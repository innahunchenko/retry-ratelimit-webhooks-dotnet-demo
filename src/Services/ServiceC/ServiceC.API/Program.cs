var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient("notifyA", client =>
{
    client.BaseAddress = new Uri("http://service-a");
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

app.MapPost("/do-work", async (HttpContext ctx, IHttpClientFactory factory) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var payload = await reader.ReadToEndAsync();
    Console.WriteLine($"[C] Received work: {payload}");
    await Task.Delay(3000); // long-running work
    var client = factory.CreateClient("notifyA"); // after work done, send webhook to Service A
    var response = await client.PostAsync("/webhook", new StringContent($"done:{payload}"));
    Console.WriteLine($"[C] Sent webhook to A: {(int)response.StatusCode}");
    return Results.Ok(new { done = true });
});

app.Run();