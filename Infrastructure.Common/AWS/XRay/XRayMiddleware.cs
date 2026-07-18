using Amazon.XRay.Recorder.Core;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Infrastructure.Common.AWS.XRay
{
    public class XRayMiddleware
    {
        private readonly RequestDelegate _next;

        public XRayMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Extracting session ID from the request header
            if (context.Request.Headers.TryGetValue("Session-Id", out var sessionId))
            {
                // Adding session ID as an annotation
                AWSXRayRecorder.Instance.AddAnnotation("SessionId", sessionId.ToString());
            }

            await _next(context);
        }
    }
    public record Default
    {
        [JsonPropertyName("fixed_target")]
        public int FixedTarget { get; init; }

        [JsonPropertyName("rate")]
        public double Rate { get; init; }
    }
    public record Rule
    {
        [JsonPropertyName("description")]
        public string Description { get; init; } = string.Empty;

        [JsonPropertyName("service_name")]
        public string ServiceName { get; init; } = string.Empty;

        [JsonPropertyName("http_method")]
        public string HttpMethod { get; init; } = string.Empty;

        [JsonPropertyName("url_path")]
        public string UrlPath { get; init; } = string.Empty;

        [JsonPropertyName("fixed_target")]
        public int FixedTarget { get; init; }

        [JsonPropertyName("rate")]
        public double Rate { get; init; }
    }
    public record SamplingRuleManifest
    {
        [JsonPropertyName("version")]
        public int Version { get; init; }

        [JsonPropertyName("rules")]
        public List<Rule>? Rules { get; init; }

        [JsonPropertyName("default")]
        public Default? Default { get; init; }
    }
    }
