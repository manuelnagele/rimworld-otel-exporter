using System;
using System.Net.Http;
using System.Net.Http.Headers;

namespace RimWorldOtelExporter.Transport
{
    public readonly struct SendResult
    {
        public readonly bool Ok;
        public readonly int StatusCode;
        public readonly string Detail;
        public SendResult(bool ok, int statusCode, string detail) { Ok = ok; StatusCode = statusCode; Detail = detail; }
    }

    /// <summary>
    /// Sends serialized OTLP Protobuf payloads over HTTP.
    /// Auth/tenant headers are applied per-request (not on DefaultRequestHeaders) so reconfiguring
    /// from the settings/UI thread never races an in-flight POST on the background export thread.
    /// </summary>
    public sealed class OtlpHttpSender : IDisposable
    {
        private static readonly string UserAgent = "RimWorldOtelExporter/" + ModInfo.Version;

        private readonly HttpClient _client;
        private volatile string? _authHeader;
        private volatile string? _orgId;

        public OtlpHttpSender()
        {
            _client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        }

        public void Configure(string? authHeader, string? orgId)
        {
            _authHeader = string.IsNullOrEmpty(authHeader) ? null : authHeader;
            _orgId = string.IsNullOrEmpty(orgId) ? null : orgId;
        }

        private HttpRequestMessage BuildRequest(string endpoint, byte[] data)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new ByteArrayContent(data),
            };
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
            req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

            var auth = _authHeader;
            if (!string.IsNullOrEmpty(auth))
                req.Headers.TryAddWithoutValidation("Authorization", auth);

            var org = _orgId;
            if (!string.IsNullOrEmpty(org))
                req.Headers.TryAddWithoutValidation("X-Scope-OrgID", org);

            return req;
        }

        /// <summary>POST a raw Protobuf payload to the given OTLP endpoint path.</summary>
        /// <exception cref="HttpRequestException">Thrown on non-success HTTP status.</exception>
        public void Send(string endpoint, byte[] data)
        {
            using var req = BuildRequest(endpoint, data);
            var response = _client.SendAsync(req).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                // Surface the response body — Grafana Cloud/Loki return the real reason (bad tenant,
                // schema, auth) in the body, which EnsureSuccessStatusCode would have discarded.
                string body = SafeReadBody(response);
                throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
            }
        }

        /// <summary>Non-throwing POST used by the in-game "Test connection" button.</summary>
        public SendResult SendWithResult(string endpoint, byte[] data)
        {
            try
            {
                using var req = BuildRequest(endpoint, data);
                var response = _client.SendAsync(req).GetAwaiter().GetResult();
                string body = SafeReadBody(response);
                return new SendResult(response.IsSuccessStatusCode, (int)response.StatusCode,
                    response.IsSuccessStatusCode ? "OK" : $"{response.ReasonPhrase}: {body}");
            }
            catch (Exception ex)
            {
                return new SendResult(false, 0, ex.InnerException?.Message ?? ex.Message);
            }
        }

        private static string SafeReadBody(HttpResponseMessage response)
        {
            try
            {
                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
                return body.Length > 300 ? body.Substring(0, 300) : body;
            }
            catch { return ""; }
        }

        public void Dispose() => _client.Dispose();
    }
}
