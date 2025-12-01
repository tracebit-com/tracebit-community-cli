namespace Tracebit.Cli.API;

public class QuotaExceededException() : Exception($"You've reached your maximum number of canary credentials. You can unlock higher limits by referring friends and giving feedback on your experience with Tracebit.\nFind out more at {Constants.TracebitUrl}");

public class TooManyEmailCanariesDeployed(string msg) : Exception(msg);
