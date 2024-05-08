using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;
using ThrottlingTroll;
using ThrottlingTroll.CounterStores.Redis;
using StackExchange.Redis;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication((builderContext, workerAppBuilder) => {

        workerAppBuilder.UseThrottlingTroll((functionContext, options) =>
        {
            string redisConnString = Environment.GetEnvironmentVariable("RedisConnString")!;
            if (!string.IsNullOrEmpty(redisConnString))
            {
                options.CounterStore = new RedisCounterStore(ConnectionMultiplexer.Connect(redisConnString));
            }

            options.GetConfigFunc = async () =>
            {
                using var conn = new SqlConnection(Environment.GetEnvironmentVariable("SqlConnString"));
                conn.Open();

                using var cmd = new SqlCommand("SELECT * FROM ThrottlingTroll", conn);
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
                    UniqueName = "CalleeFunc"
                };
            };

            options.IntervalToReloadConfigInSeconds = 5;
        });
    })
    .ConfigureServices(services => {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
    })
    .Build();

host.Run();
