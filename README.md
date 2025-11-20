# retry-ratelimit-webhooks-dotnet-demo
.NET project demonstrating how to implement retry policies (with exponential backoff), rate limit handling, request throttling, and webhook processing across three minimal API microservices.

**What is done here?**

Example with 3 minimal .NET 8 services (Minimal API) connected in Docker Compose:
1. **service-a** (http://localhost:5001) — main service:
    - endpoint POST /start — starts the flow A -> B -> C;
    - endpoint POST /webhook — receives webhook from C;
    - background queue (Channel) processes tasks and calls Service B with Polly retry;
    - rate limiter on /webhook (concurrency limiter)

2. **service-b** (http://localhost:5002) — external API with TokenBucket rate limiter:
    - endpoint POST /api/process — accepts task from A, sometimes returns 500/429 to simulate errors and rate limiting
    - on success, forwards to Service C /do-work

3. **service-c** (http://localhost:5003) — worker that simulates long work and sends webhook back to A:
    - endpoint POST /do-work — does work and POSTs http://service-a:5001/webhook when finished.


**To see the behavior of retries, backoff, rate-limiter, and webhook:**

1. From the root folder, run the following command:
    docker compose up --build
2. Execute POST /start on Service A (http://localhost:5001/start) — this will start the chain:
    A → B → C → webhook → A
3. Watch the container logs with:
    docker compose logs -f service-a
4. In the logs, you will see retries, 429 responses, webhook receipt, etc.
   
