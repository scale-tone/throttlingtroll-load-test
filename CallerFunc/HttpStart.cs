using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace CallerFunc
{
    public class HttpStart
    {
        private static Task? InfiniteTask;
        private readonly IHttpClientFactory _httpClientFactory;

        private readonly ILogger<HttpStart> _logger;
        private readonly TelemetryClient _telemetryClient = new TelemetryClient();

        public HttpStart(IHttpClientFactory httpClientFactory, ILogger<HttpStart> logger)
        {
            this._httpClientFactory = httpClientFactory;
            this._logger = logger;
        }

        [Function(nameof(HttpStart))]
        public IActionResult Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
        {
            // Starting the task just once. This approach with timer seems to be the only reliable way
            if (InfiniteTask == null)
            {
                InfiniteTask = this.InfiniteMethod();
            }

            return new OkResult();
        }

        private async Task InfiniteMethod()
        {
            while (true)
            {
                using var client = this._httpClientFactory.CreateClient("my-throttled-httpclient");

                var tasks = new List<Task>();
                string endpointUrl = Environment.GetEnvironmentVariable("EndpointUrl")!;
                int numOfParallelRequests = int.Parse(Environment.GetEnvironmentVariable("NumOfParallelRequests")!);

                for (int i = 0; i < numOfParallelRequests; i++)
                {
                    tasks.Add(client.GetAsync(endpointUrl));
                }

                this._telemetryClient.TrackMetric("CallerFuncHttpCall", numOfParallelRequests);

                try
                {
                    await Task.WhenAll(tasks);
                }
                catch
                {
                    // Doing nothing                
                }
            }
        }
    }
}
