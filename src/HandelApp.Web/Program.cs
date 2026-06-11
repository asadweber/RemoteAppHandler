using HandelApp.Web.Authorization;
using HandelApp.Web.Services;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

// ── Windows Authentication (Negotiate = NTLM / Kerberos) ───────────────────
builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
    .AddNegotiate();

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;

    var allowedGroups = builder.Configuration
        .GetSection("Authorization:AllowedGroups").Get<string[]>() ?? [];
    var readOnlyGroups = builder.Configuration
        .GetSection("Authorization:ReadOnlyGroups").Get<string[]>() ?? [];

    //options.AddPolicy("CanControlApp", policy =>
    //    policy.Requirements.Add(new AdGroupRequirement(allowedGroups)));

    //options.AddPolicy("CanViewStatus", policy =>
    //    policy.Requirements.Add(new AdGroupRequirement([.. allowedGroups, .. readOnlyGroups])));
});

builder.Services.AddSingleton<IAuthorizationHandler, AdGroupHandler>();

// ── Remote Agent ─────────────────────────────────────────────────────────────
builder.Services.Configure<RemoteAgentOptions>(
    builder.Configuration.GetSection("RemoteAgent"));
builder.Services.AddSingleton<RemoteAgentService>();
builder.Services.AddSingleton<IRemoteAgentService>(sp =>
    sp.GetRequiredService<RemoteAgentService>());
builder.Services.AddHostedService<AgentConnectionMonitor>();

// ── MVC + Swagger ─────────────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
    c.SwaggerDoc("v1", new() { Title = "HandelApp API", Version = "v1" }));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Apps}/{action=Index}/{id?}");

app.Run();
