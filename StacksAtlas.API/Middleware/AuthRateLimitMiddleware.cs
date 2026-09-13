using System.Collections.Concurrent;

namespace StacksAtlas.API.Middleware;

/// <summary>
/// In-memory sliding-window rate limit for unauthenticated auth endpoints (LAN brute-force mitigation).
/// </summary>
public class AuthRateLimitMiddleware(RequestDelegate next, ILogger<AuthRateLimitMiddleware> logger)
{
    private const int MaxAttempts = 20;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private static readonly ConcurrentDictionary<string, AttemptWindow> Attempts = new();

    private static readonly HashSet<string> LimitedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/login",
        "/api/auth/setup",
        "/api/auth/portable-session"
    };

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method)
            || !LimitedPaths.Contains(context.Request.Path.Value ?? string.Empty))
        {
            await next(context);
            return;
        }

        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var window = Attempts.GetOrAdd(key, _ => new AttemptWindow());

        if (!window.TryRecord(MaxAttempts, Window))
        {
            logger.LogWarning("Auth rate limit exceeded for {RemoteIp} on {Path}", key, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsJsonAsync(new { message = "Too many authentication attempts. Try again later." });
            return;
        }

        await next(context);
    }

    private sealed class AttemptWindow
    {
        private readonly object _lock = new();
        private int _count;
        private DateTime _windowStartUtc = DateTime.UtcNow;

        public bool TryRecord(int maxAttempts, TimeSpan window)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                if (now - _windowStartUtc > window)
                {
                    _count = 0;
                    _windowStartUtc = now;
                }

                _count++;
                return _count <= maxAttempts;
            }
        }
    }
}
