using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace RimWorldOtelExporter.Transport
{
    /// <summary>
    /// Sends serialized OTLP Protobuf payloads over HTTP.
    /// All calls are synchronous — call only from the background export thread.
    /// </summary>
    public sealed class OtlpHttpSender : IDisposable
    {
        private readonly HttpClient _client;

        public OtlpHttpSender()
        {
            _client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        }

        public void Configure(string? authHeader, string? orgId)
        {
            _client.DefaultRequestHeaders.Clear();

            if (!string.IsNullOrEmpty(authHeader))
                _client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authHeader);

            if (!string.IsNullOrEmpty(orgId))
                _client.DefaultRequestHeaders.TryAddWithoutValidation("X-Scope-OrgID", orgId);
        }

        /// <summary>POST a raw Protobuf payload to the given OTLP endpoint path.</summary>
        /// <param name="endpoint">Full URL, e.g. http://localhost:4318/v1/metrics</param>
        /// <param name="data">Serialized Protobuf bytes</param>
        /// <exception cref="HttpRequestException">Thrown on non-success HTTP status.</exception>
        public void Send(string endpoint, byte[] data)
        {
            var content = new ByteArrayContent(data);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");

            var response = _client.PostAsync(endpoint, content).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
        }

        public void Dispose() => _client.Dispose();
    }
}
