using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.ApplicationInsights;

namespace CalleeFunc
{
    public class HttpTest1
    {
        private readonly ILogger<HttpTest1> _logger;
        private readonly TelemetryClient _telemetryClient = new TelemetryClient();

        public HttpTest1(ILogger<HttpTest1> logger)
        {
            _logger = logger;
        }

        [Function("HttpTest1")]
        public IActionResult Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
        {
            this._telemetryClient.TrackMetric("CalleeFuncHttpTest1Success", 1);

            return new OkObjectResult("OK");
        }
    }
}
