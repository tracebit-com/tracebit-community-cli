namespace Tracebit.Cli.API
{
    public static class Constants
    {
        private const string TracebitEnvironmentVariable = "TRACEBIT_ENVIRONMENT";
        private const string TracebitUrlVariable = "TRACEBIT_URL";
        private const string ProductionTracebitHost = "community.tracebit.com";
        private const string StagingTracebitHost = "community.stage.tracebit.com";
        public static Uri TracebitUrl
        {
            get
            {
                var url = Environment.GetEnvironmentVariable(TracebitUrlVariable);
                if (url is not null)
                    return RequireHttps(new Uri(url));
                return new UriBuilder { Scheme = "https", Host = TracebitHost }.Uri;
            }
        }
        public static Uri TracebitApiUrl { get; } = new Uri(TracebitUrl, "/api/");

        // Credentials and tokens travel over this connection, so a plaintext override is never acceptable
        internal static Uri RequireHttps(Uri url)
        {
            if (url.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException($"{TracebitUrlVariable} must use https, got '{url}'");
            return url;
        }

        public const string TracebitCliSource = "tracebit-cli";
        public const string SourceTypeDefault = "endpoint";

        public static string TracebitHost
        {
            get
            {
                var urlOverride = Environment.GetEnvironmentVariable(TracebitUrlVariable);
                if (urlOverride is not null)
                    return RequireHttps(new Uri(urlOverride)).Host;

                var environment = Environment.GetEnvironmentVariable(TracebitEnvironmentVariable);
                if (environment is null)
                    return ProductionTracebitHost;

                return environment.Equals("stage", StringComparison.OrdinalIgnoreCase) ? StagingTracebitHost : ProductionTracebitHost;
            }
        }

        public const string GitlabCookieInstanceId = "gitlab-cookie";
        public const string GitlabUsernamePasswordInstanceId = "gitlab-username-password";
        public const string EmailCanaryName = "Backup Codes Email";
    }
}
