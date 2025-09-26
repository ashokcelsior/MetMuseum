using Polly;
using Polly.Retry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace MetMuseum.Utilities
{
    public static class RetryHelper
    {
        public static AsyncRetryPolicy<HttpResponseMessage> CreateRetryPolicy(int maxRetries = 3)
        {
            return Policy<HttpResponseMessage>
                .Handle<HttpRequestException>()
                 .OrResult(r => r.StatusCode == (HttpStatusCode)429)
                .WaitAndRetryAsync(
                    maxRetries,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    (outcome, timespan, retryCount, context) =>
                    {
                        string reason = outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString() ?? "Unknown";
                        Console.WriteLine($"Retry {retryCount} after {timespan.TotalSeconds}s due to {reason}");
                    });
        }
    }
}
