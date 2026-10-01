using Chatbot.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<FrontendOptions>()
    .Bind(builder.Configuration.GetSection(FrontendOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddRazorPages();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Strict CSP: all scripts/styles are served from this origin; the only other origin allowed is the API.
var apiOrigin = new Uri(app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<FrontendOptions>>().Value.ApiBaseUrl)
    .GetLeftPart(UriPartial.Authority);
var wsOrigin = apiOrigin.Replace("https://", "wss://").Replace("http://", "ws://");
var csp = string.Join("; ",
    "default-src 'self'",
    "script-src 'self'",
    "style-src 'self'",
    "img-src 'self' data:",
    $"connect-src 'self' {apiOrigin} {wsOrigin}",
    "frame-ancestors 'none'",
    "base-uri 'self'",
    "form-action 'self'");

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.ContentSecurityPolicy = csp;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    await next();
});

app.UseRouting();
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

app.Run();
