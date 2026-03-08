using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RpgGame.Api.Hubs;
using RpgGame.Api.Services;
using RpgGame.Api.Swagger;
using RpgGame.Infrastructure.Data;
using RpgGame.Infrastructure.Identity;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// PostgreSQL + Identity
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(config["ConnectionStrings:Default"]));

builder.Services.AddIdentity<AppUser, IdentityRole>(o =>
    {
        o.Password.RequireDigit = false;
        o.Password.RequireLowercase = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequiredLength = 6;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// JWT
builder.Services.AddAuthentication(o =>
    {
        o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = config["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = config["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(config["Jwt:Secret"]!)),
            ValidateLifetime = true,
        };

        // Allow SignalR to receive token from query string (WebSocket handshake)
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                var path = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    ctx.Token = accessToken;
                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();

// Redis connection multiplexer (for live state keys)
// abortConnect=false lets the swagger CLI start the app without a running Redis instance
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var connStr = config["Redis:ConnectionString"]!;
    var opts = ConfigurationOptions.Parse(connStr);
    opts.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(opts);
});

// SignalR with Redis backplane
builder.Services.AddSignalR()
    .AddStackExchangeRedis(config["Redis:ConnectionString"]!);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserAccessor, HttpCurrentUserAccessor>();

builder.Services.AddControllers();

builder.Services.AddSwaggerGen(c => c.OperationFilter<ResponseTypeOperationFilter>());

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:4200")
     .AllowAnyHeader()
     .AllowAnyMethod()
     .AllowCredentials()));

var app = builder.Build();

// Auto-migrate on startup (skipped silently if DB is unreachable, e.g. during swagger spec generation)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        await db.Database.MigrateAsync();
    }
    catch (Exception ex) when (!app.Environment.IsProduction())
    {
        var logger = scope.ServiceProvider.GetService<ILogger<Program>>();
        logger?.LogWarning("DB migration skipped (DB unavailable): {Message}", ex.Message);
    }
}

app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<GameHub>("/hubs/game");

app.Run();
