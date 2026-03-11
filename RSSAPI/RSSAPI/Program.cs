using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using RSSAPI.Data;
using RSSAPI.Services;
using System.Text;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Http;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

string? jwtSecretKey = builder.Configuration["Jwt:Key"];
string? jwtIssuer = builder.Configuration["Jwt:Issuer"];
string? jwtAudience = builder.Configuration["Jwt:Audience"];
string[]? allowedFrontendOrigins = builder.Configuration.GetSection("FrontendUrls").Get<string[]>();

if (string.IsNullOrEmpty(jwtSecretKey) || string.IsNullOrEmpty(jwtIssuer) || string.IsNullOrEmpty(jwtAudience))
{
    throw new InvalidOperationException("JWT configuration is missing.");
}

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("RequireAdminRole", policy =>
        policy.RequireAssertion(context =>
        {
            ClaimsPrincipal userPrincipal = context.User;

            if (userPrincipal?.Identity == null || !userPrincipal.Identity.IsAuthenticated)
            {
                return false;
            }

            bool isAdmin = userPrincipal.IsInRole("Admin") ||
                          userPrincipal.HasClaim(claim => claim.Type == "role" && claim.Value == "Admin") ||
                          userPrincipal.HasClaim(claim => claim.Type == ClaimTypes.Role && claim.Value == "Admin");

            return isAdmin;
        }));

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.LoginPath = "/api/auth/login";
    options.LogoutPath = "/api/auth/logout";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = 403;
        return Task.CompletedTask;
    };
})
.AddJwtBearer(options =>
{
    byte[] jwtSigningKey = Encoding.UTF8.GetBytes(jwtSecretKey);

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(jwtSigningKey),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            string? extractedToken = context.Request.Headers.Authorization.FirstOrDefault()?.Split(" ").Last();

            if (string.IsNullOrEmpty(extractedToken))
            {
                extractedToken = context.Request.Cookies["AuthToken"];

                if (!string.IsNullOrEmpty(extractedToken))
                {
                    context.Request.Headers.Authorization = $"Bearer {extractedToken}";
                }
            }

            context.Token = extractedToken;
            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            Console.WriteLine($"Authentication failed: {context.Exception.Message}");
            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            Console.WriteLine("Token validated successfully");
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddCors(options =>
{
    if (allowedFrontendOrigins == null || allowedFrontendOrigins.Length == 0)
    {
        throw new InvalidOperationException("FrontendUrls configuration is missing or empty.");
    }

    options.AddPolicy("AllowFrontend",
        policy =>
        {
            policy.WithOrigins(allowedFrontendOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        });
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

builder.Services.AddAuthorization();
builder.Services.AddDbContext<ScreenDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("ScreenDB")));
builder.Services.AddHostedService<ScreenStatusMonitorService>();
builder.Services.AddScoped<UserService>();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "RSS API",
        Version = "v1",
        Description = "API for RSS Management System"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token in the text input below. Example: 'Bearer 12345abcdef'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                },
                Scheme = "oauth2",
                Name = "Bearer",
                In = ParameterLocation.Header
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddEndpointsApiExplorer();

WebApplication app = builder.Build();

app.UseStaticFiles();
app.UseRouting();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/swagger") &&
        !context.Request.Path.Value!.Contains(".json") &&
        !context.Request.Path.StartsWithSegments("/swagger-ui/login") &&
        !context.Request.Path.Value!.EndsWith(".js") &&
        !context.Request.Path.Value!.EndsWith(".css") &&
        !context.Request.Path.Value!.EndsWith(".png"))
    {
        bool hasAuthHeader = !string.IsNullOrEmpty(
            context.Request.Headers.Authorization.FirstOrDefault());
        bool hasAuthCookie = context.Request.Cookies.ContainsKey("AuthToken");

        Console.WriteLine($"Auth Header: {hasAuthHeader}, Auth Cookie: {hasAuthCookie}, Path: {context.Request.Path}");

        if (!hasAuthHeader && !hasAuthCookie)
        {
            context.Response.Redirect("/swagger-ui/login.html");
            return;
        }
    }

    await next();
});

app.UseAuthentication();
app.UseAuthorization();
app.UseCors("AllowFrontend");

app.MapControllers();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "RSS API v1");
    options.RoutePrefix = "swagger";

    options.InjectJavascript("/swagger-ui/swagger-auth.js");

    options.DocumentTitle = "RSS API Documentation";
});

app.MapGet("/swagger-ui/login.html", async context =>
{
    await context.Response.SendFileAsync(Path.Combine(
        app.Environment.WebRootPath, "swagger-ui", "login.html"));
});

app.MapGet("/swagger-ui/swagger-auth.js", async context =>
{
    context.Response.ContentType = "application/javascript";
    await context.Response.SendFileAsync(Path.Combine(
        app.Environment.WebRootPath, "swagger-ui", "swagger-auth.js"));
});

app.MapGet("/swagger-ui/login-styles.css", async context =>
{
    context.Response.ContentType = "text/css";
    await context.Response.SendFileAsync(Path.Combine(
        app.Environment.WebRootPath, "swagger-ui", "login-styles.css"));
});

app.Run();