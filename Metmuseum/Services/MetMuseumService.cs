using MetMuseum.Utilities;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Polly.Retry;
using System.Net;
using System.Net.Http.Headers;

namespace Metmuseum.Services
{
    public class MetMuseumService: IMetMuseumService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<MetMuseumService> _logger;
        private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;

        public MetMuseumService(IHttpClientFactory httpClientFactory, ILogger<MetMuseumService> logger)
        {
            _httpClient = httpClientFactory.CreateClient("metMuseum");
            _logger = logger;
            _retryPolicy = RetryHelper.CreateRetryPolicy();
        }

        public async Task<IReadOnlyList<int>> GetAllIdsAsync(CancellationToken ct = default)
        {
            var url = "objects";

            HttpResponseMessage response = await _retryPolicy.ExecuteAsync(async () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MetMuseumClient/1.0)");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            });

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch all IDs: {status}", response.StatusCode);
                return Array.Empty<int>();
            }

            var ids = new List<int>();

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var streamReader = new StreamReader(stream);
            using var reader = new Newtonsoft.Json.JsonTextReader(streamReader);
            var serializer = new Newtonsoft.Json.JsonSerializer();

            await reader.ReadAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (reader.TokenType == Newtonsoft.Json.JsonToken.PropertyName &&
                    (string)reader.Value! == "objectIDs")
                {
                    await reader.ReadAsync(ct);
                    while (await reader.ReadAsync(ct) && reader.TokenType != Newtonsoft.Json.JsonToken.EndArray)
                    {
                        if (reader.Value != null)
                            ids.Add(Convert.ToInt32(reader.Value));
                    }
                    break;
                }
            }

            return ids;
        }

        public async Task<JObject?> GetObjectAsync(int id, CancellationToken ct = default)
        {
            var url = $"objects/{id}";

            HttpResponseMessage response = await _retryPolicy.ExecuteAsync(async () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MetMuseumClient/1.0)");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                return await _httpClient.SendAsync(request, ct);
            });

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch object {id}: {status}", id, response.StatusCode);
                return null;
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("Skipping object {id} due to 403 Forbidden", id);
                return null;
            }
            var json = await response.Content.ReadAsStringAsync(ct);
            return JObject.Parse(json);
        }
    }
}
