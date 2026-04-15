namespace AsbtCore.Broker.API.Services
{

    //app.UseMiddleware<RateLimitingMiddleware>();

    public class RateLimitingMiddleware
    {
        private readonly RequestDelegate _next;
        private static Dictionary<string, DateTime> _requestTracker;

        public RateLimitingMiddleware(RequestDelegate next)
        {
            _next = next;
            _requestTracker = new Dictionary<string, DateTime>();
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var clientKey = context.Request.HttpContext
                .Connection.RemoteIpAddress.ToString();

            if (_requestTracker.ContainsKey(clientKey) &&
                DateTime.Now.Subtract(_requestTracker[clientKey]).TotalSeconds < 60)
            {
                context.Response.StatusCode = 429; // Too Many Requests

                await context.Response.WriteAsync("Rate limit exceeded. Try again later.");

                return;
            }

            _requestTracker[clientKey] = DateTime.Now;

            await _next(context);
        }
    }
}
