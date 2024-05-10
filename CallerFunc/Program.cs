using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;
using ThrottlingTroll;
using ThrottlingTroll.CounterStores.Redis;
using StackExchange.Redis;
using Microsoft.ApplicationInsights;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services => {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        services.AddHttpClient("my-throttled-httpclient").AddThrottlingTrollMessageHandler((serviceProvider, options) =>
        {
            string redisConnString = Environment.GetEnvironmentVariable("RedisConnString")!;
            if (!string.IsNullOrEmpty(redisConnString))
            {
                options.CounterStore = new RedisCounterStore(ConnectionMultiplexer.Connect(redisConnString));
            }

            var getShouldRetryUntil = async () =>
            {
                using var conn = new SqlConnection(Environment.GetEnvironmentVariable("SqlConnString"));
                conn.Open();

                using var cmd = new SqlCommand("SELECT MAX(ShouldRetryUntil) FROM ThrottlingTrollEgress", conn);
                var result = await cmd.ExecuteScalarAsync();
                return (int)result!;
            };
            Task<int> shouldRetryUntilTask = getShouldRetryUntil();

            var telemetryClient = new TelemetryClient();

            options.GetConfigFunc = async () =>
            {
                // Just refreshing this task
                shouldRetryUntilTask = getShouldRetryUntil();

                using var conn = new SqlConnection(Environment.GetEnvironmentVariable("SqlConnString"));
                conn.Open();

                using var cmd = new SqlCommand("SELECT * FROM ThrottlingTrollEgress", conn);
                using var reader = await cmd.ExecuteReaderAsync();

                var rules = new List<ThrottlingTrollRule>();

                while (reader.Read())
                {
                    var rule = new ThrottlingTrollRule
                    {
                        UriPattern = reader["UriPattern"].ToString(),

                        LimitMethod = new FixedWindowRateLimitMethod
                        {
                            PermitLimit = (int)reader["PermitLimit"],
                            IntervalInSeconds = (int)reader["IntervalInSeconds"],
                        }
                    };

                    rules.Add(rule);
                }

                return new ThrottlingTrollConfig
                {
                    Rules = rules,
                    UniqueName = "CallerFunc"
                };
            };

            options.IntervalToReloadConfigInSeconds = 10;

            options.ResponseFabric = async (checkResults, requestProxy, responseProxy, cancelToken) =>
            {
                int shouldRetryUntil = await shouldRetryUntilTask;
                
                var egressResponse = (IEgressHttpResponseProxy)responseProxy;
                egressResponse.ShouldRetry = egressResponse.RetryCount < shouldRetryUntil;

                if (egressResponse.ShouldRetry)
                {
                    telemetryClient.TrackMetric("CallerFuncHttpCallRetry", 1);
                }
            };
        });


        // Starting the infinite loop on this particular instance by calling HttpStart endpoint numerous times. Seems to be the only reliable way.

        string hostName = Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME")!;

        Task.Delay(3000).ContinueWith(t => { 

            var client = new HttpClient();
            // Need to make multiple calls, just to make sure all instances are touched
            for (int i = 0; i < 32; i++)
            {
                Thread.Sleep(300);

                client.GetAsync($"{(hostName.StartsWith("localhost") ? "http" : "https")}://{hostName}/api/{nameof(CallerFunc.HttpStart)}");
            }
        });

    })
    .Build();

host.Run();
