using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Smolcoder.Web.Services;

namespace Smolcoder.Web.Hosting;

/// <summary>
/// The hub listens on 127.0.0.1 only, and every request must carry the random key:
/// as "?k=" in the printed URL once, then as a cookie. Requests from other origins are refused.
/// </summary>
public sealed class TokenAuthMiddleware
{
    public const string CookieName = "smol_k";

    private readonly RequestDelegate next;
    private readonly HubOptions options;

    public TokenAuthMiddleware(RequestDelegate next, HubOptions options)
    {
        this.next = next;
        this.options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Cache-Control"] = "no-store";

        if (!OriginAllowed(context))
        {
            await Forbid(context);
            return;
        }

        string? queryKey = context.Request.Query["k"];
        if (queryKey != null && Matches(queryKey))
        {
            CookieOptions cookie = new CookieOptions();
            cookie.HttpOnly = true;
            cookie.SameSite = SameSiteMode.Strict;
            cookie.Path = "/";
            cookie.IsEssential = true;
            context.Response.Cookies.Append(CookieName, options.Token, cookie);
            if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path == "/")
            {
                // Drop the key from the address bar. The cookie carries it from now on.
                context.Response.Redirect("/");
                return;
            }
            await next(context);
            return;
        }

        string? cookieKey = context.Request.Cookies[CookieName];
        if (cookieKey != null && Matches(cookieKey))
        {
            await next(context);
            return;
        }
        await Forbid(context);
    }

    private bool Matches(string candidate)
    {
        byte[] expected = Encoding.UTF8.GetBytes(options.Token);
        byte[] given = Encoding.UTF8.GetBytes(candidate);
        return CryptographicOperations.FixedTimeEquals(expected, given);
    }

    private bool OriginAllowed(HttpContext context)
    {
        string? origin = context.Request.Headers["Origin"];
        if (origin == null || origin.Length == 0)
        {
            return true;
        }
        return origin == "http://127.0.0.1:" + options.Port || origin == "http://localhost:" + options.Port;
    }

    private static async Task Forbid(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync("forbidden — open smolcoder's printed URL (it includes the session key)");
    }
}
