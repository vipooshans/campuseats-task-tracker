// ============================================================================
//  SE3090 - Software Engineering Frameworks
//  Lecture 04 teaching demo
//  Database Design, Authentication, Authorization and Integration
//
//  This file is the COMPOSITION ROOT. Read it top to bottom and you have read
//  the architecture of the whole application:
//
//      Section A   configuration                    (SLIDE 23)
//      Section B   the database connection          (SLIDE 23, 26)
//      Section C   the DI container                 (SLIDE 24)
//      Section D   authentication - who are you?    (SLIDE 30, 31)
//      Section E   authorization - may you?         (SLIDE 36, 38)
//      Section F   the middleware pipeline          (SLIDE 39) - ORDER MATTERS
//
//  Lecture 03 ended with two commented-out lines in this file:
//        // app.UseAuthentication();
//        // app.UseAuthorization();
//  Today we fill them in, and Sections D and E are what makes them work.
//
//  Run:      dotnet run
//  Swagger:  http://localhost:5090/swagger
// ============================================================================

using System.Text;
using LankaMart.Api.Common;
using LankaMart.Api.Data;
using LankaMart.Api.Middleware;
using LankaMart.Api.Repositories;
using LankaMart.Api.Security;
using LankaMart.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
//  SECTION A - CONFIGURATION                                     (SLIDE 23)
// ---------------------------------------------------------------------------
//  appsettings.json is committed to Git, so it holds NO secrets. The
//  connection string and the JWT signing key arrive from outside the repository:
//
//      dev        : dotnet user-secrets, or a .env / environment variable
//      production : the platform injects them (Azure App Settings,
//                   AWS Secrets Manager, container env vars)
//
//  Same binary, different environment - the twelve-factor rule.
// ---------------------------------------------------------------------------

builder.Services
    .AddOptions<LankaMartOptions>()
    .Bind(builder.Configuration.GetSection(LankaMartOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// The JWT settings are needed HERE, before the DI container is built, because
// the authentication middleware is configured with them below.
var jwtOptions = builder.Configuration
    .GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

// ---- FAIL FAST ON A BAD KEY --------------------------------------------
//  SLIDE 33, "Weak / leaked secret". An application that starts happily with a
//  six-character signing key is an application whose Admin tokens can be forged
//  over lunch. Better to refuse to start, loudly, with instructions.
const string DevelopmentKeyMarker = "DEVELOPMENT-ONLY";

if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey) || jwtOptions.SigningKey.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:SigningKey is missing or shorter than 32 characters. HMAC-SHA256 needs " +
        "at least 256 bits of key material.\n" +
        "  Development : it is already set in appsettings.Development.json.\n" +
        "  Anywhere else: supply your own, e.g.\n" +
        "      export Jwt__SigningKey=\"$(openssl rand -base64 48)\"");
}

if (!builder.Environment.IsDevelopment() && jwtOptions.SigningKey.Contains(DevelopmentKeyMarker))
{
    throw new InvalidOperationException(
        "Refusing to start outside Development with the shared development signing key. " +
        "That key is published in this repository; anyone could mint an Admin token. " +
        "Set Jwt__SigningKey from the environment.");
}

// ---------------------------------------------------------------------------
//  SECTION B - THE DATABASE CONNECTION                       (SLIDE 23, 26)
// ---------------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("LankaMartDb");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "No connection string found for 'LankaMartDb'.\n" +
        "  dotnet user-secrets set \"ConnectionStrings:LankaMartDb\" " +
        "\"Host=localhost;Port=5432;Database=lankamart;Username=lankamart_app;Password=...\"\n" +
        "  ...or set the environment variable ConnectionStrings__LankaMartDb\n" +
        "See the README, step 4.");
}

builder.Services.AddDbContext<AppDbContext>(o =>
{
    // UseNpgsql wires EF Core to the PostgreSQL provider (SLIDE 25).
    o.UseNpgsql(connectionString, npgsql =>
    {
        // SLIDE 26 - "retry only transient faults". This retries connection
        // drops and timeouts, and leaves genuine constraint violations alone.
        npgsql.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(2),
            errorCodesToAdd: null);
    });

    if (builder.Environment.IsDevelopment())
    {
        // In the lecture we WANT the SQL on screen. Never in production: the
        // parameter values printed here can include personal data.
        o.EnableDetailedErrors();
        o.EnableSensitiveDataLogging();
    }
});

// NOTE ON POOLING (SLIDE 26): there is no pooling code here because there is
// nothing to write. Npgsql pools connections automatically, and AddDbContext
// registers the context as SCOPED - one per HTTP request, disposed by the
// framework when the request ends, which returns its connection to the pool.
// The classic leak is creating a DbContext with 'new' and never disposing it;
// after a hundred of those you meet "sorry, too many clients already" (53300).

// ---------------------------------------------------------------------------
//  SECTION C - THE DI CONTAINER                                  (SLIDE 24)
// ---------------------------------------------------------------------------
//  Register the CONTRACT -> the IMPLEMENTATION once. Every constructor that
//  asks for the interface is handed an instance. No 'new' in the application.
//
//  Compare with Lecture 03: the repositories were SINGLETON there, because the
//  ConcurrentDictionary inside them WAS the datastore and had to outlive a
//  request. Now the datastore is PostgreSQL, so they go back to SCOPED - they
//  share the request's DbContext, and therefore its change tracker and
//  transaction. Registering them as Singleton now would be a genuine bug: a
//  singleton holding a scoped DbContext is the most common DI error in .NET.
// ---------------------------------------------------------------------------

builder.Services.AddScoped<IUnitOfWork,             EfUnitOfWork>();
builder.Services.AddScoped<IUserRepository,         EfUserRepository>();
builder.Services.AddScoped<IRoleRepository,         EfRoleRepository>();
builder.Services.AddScoped<ICategoryRepository,     EfCategoryRepository>();
builder.Services.AddScoped<IProductRepository,      EfProductRepository>();
builder.Services.AddScoped<IOrderRepository,        EfOrderRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, EfRefreshTokenRepository>();

builder.Services.AddScoped<IAuthService,      AuthService>();
builder.Services.AddScoped<IProductService,   ProductService>();
builder.Services.AddScoped<IOrderService,     OrderService>();
builder.Services.AddScoped<IUserAdminService, UserAdminService>();

// Stateless helpers - one instance for the whole application is enough.
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddSingleton<ITokenService, JwtTokenService>();

builder.Services.AddSingleton(new JwtOptionsSnapshot
{
    RefreshTokenDays = jwtOptions.RefreshTokenDays
});

// One place for every error (SLIDE 41).
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddControllers();
builder.Services.AddRouting(options => options.LowercaseUrls = true);

// CORS allow-list, never "*" (SLIDE 39).
//  Say this out loud in class: CORS is enforced by BROWSERS. It protects your
//  users from a malicious page calling this API with their cookies; it does
//  nothing about curl or Postman. CORS is not authorization.
builder.Services.AddCors(options =>
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins(builder.Configuration
            .GetSection($"{LankaMartOptions.SectionName}:AllowedOrigins")
            .Get<string[]>() ?? Array.Empty<string>())
        .AllowAnyHeader()
        .AllowAnyMethod()));

// ---------------------------------------------------------------------------
//  SECTION D - AUTHENTICATION: "WHO ARE YOU?"               (SLIDE 30, 31)
// ---------------------------------------------------------------------------
//  This is step 6 of the flow on slide 31. The middleware registered here runs
//  BEFORE any controller: it finds the Authorization: Bearer header, checks the
//  signature and the expiry, and attaches the resulting identity to the request.
//  If any check fails, your code never runs.
//
//  Note there is NO database lookup in that description. That is the whole
//  point of a self-contained signed token - and the reason a role revoked one
//  minute ago still appears in a token issued two minutes ago.
// ---------------------------------------------------------------------------

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep the short claim names from slide 30 ("sub", "role", "email")
        // instead of letting them be rewritten to long WS-Federation URIs.
        // With this false, User.FindFirst("sub") works and a token pasted into
        // jwt.io matches the slide exactly.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            // --- The four checks that matter -------------------------------
            ValidateIssuer   = true,
            ValidIssuer      = jwtOptions.Issuer,

            ValidateAudience = true,
            ValidAudience    = jwtOptions.Audience,

            // SLIDE 33 - "No expiry validation -> stolen tokens work forever."
            ValidateLifetime = true,

            // SLIDE 33 - "alg: none / weak alg". The SERVER decides which
            // algorithms are acceptable; a token does not get to nominate its
            // own. This is the historic JWT library flaw, closed by default here
            // and pinned explicitly so the intent is visible in review.
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidAlgorithms  = new[] { SecurityAlgorithms.HmacSha256 },

            // Default is FIVE MINUTES of grace, which quietly makes a
            // "15 minute" token last 20 and confuses every expiry demo.
            ClockSkew = TimeSpan.FromSeconds(30),

            // Which claim carries the role, now that mapping is off. Without
            // this line [Authorize(Roles = "Admin")] silently matches nobody -
            // a genuinely nasty bug, because the code looks correct.
            RoleClaimType = ClaimsPrincipalExtensions.RoleClaim,
            NameClaimType = "name"
        };

        // Teaching aid: log WHY a token was rejected. Students otherwise see a
        // bare 401 and guess. Development only - the header itself already
        // tells a caller the reason, but the log tells us the token id too.
        options.IncludeErrorDetails = builder.Environment.IsDevelopment();

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                var log = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();

                log.LogWarning("JWT rejected: {Reason}", context.Exception.Message);
                return Task.CompletedTask;
            }
        };
    });

// ---------------------------------------------------------------------------
//  SECTION E - AUTHORIZATION: "MAY YOU?"                    (SLIDE 36, 38)
// ---------------------------------------------------------------------------
builder.Services.AddAuthorization(options =>
{
    // DENY BY DEFAULT. Every endpoint requires an authenticated user unless it
    // says [AllowAnonymous]. The alternative - remembering [Authorize] on each
    // new controller - fails the first time somebody is in a hurry, and the
    // failure is silent.
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization
        .AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    // SLIDE 38, bottom note - POLICIES rather than raw role lists. The endpoint
    // declares the CAPABILITY it needs; which roles hold that capability is a
    // data question, answered by the role_permissions table.
    options.AddPolicy(Policies.CanRefundOrders, p =>
        p.RequireClaim(ClaimsPrincipalExtensions.PermissionClaim, Permissions.OrdersRefund));

    options.AddPolicy(Policies.CanViewAllOrders, p =>
        p.RequireClaim(ClaimsPrincipalExtensions.PermissionClaim, Permissions.OrdersViewAll));

    options.AddPolicy(Policies.CanManageUsers, p =>
        p.RequireClaim(ClaimsPrincipalExtensions.PermissionClaim, Permissions.UsersManage));
});

// --- Swagger, with a place to paste the token (SLIDE 45, Lecture 03) -------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "LankaMart API - Lecture 04",
        Version     = "v1",
        Description = "SE3090 Lecture 04 teaching demo. PostgreSQL + EF Core, "
                    + "JWT authentication, role- and permission-based authorization."
    });

    // Adds the "Authorize" button. Log in via /auth/login, copy accessToken,
    // click Authorize, paste the token ALONE - Swagger adds the word "Bearer".
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name         = "Authorization",
        Type         = SecuritySchemeType.Http,
        Scheme       = "bearer",
        BearerFormat = "JWT",
        In           = ParameterLocation.Header,
        Description  = "Paste ONLY the access token. Swagger sends the "
                     + "Authorization: Bearer <token> header for you."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id   = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });

    var xml = Path.Combine(AppContext.BaseDirectory,
        $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml)) options.IncludeXmlComments(xml);
});

var app = builder.Build();

// ---------------------------------------------------------------------------
//  SECTION F - THE MIDDLEWARE PIPELINE                           (SLIDE 39)
// ---------------------------------------------------------------------------
//  A request walks past each checkpoint in the order written below. Any
//  checkpoint may inspect it, change it, or turn it away; only survivors reach
//  your controller. The response walks back out through the same corridor.
//
//  ORDER IS NOT COSMETIC. Three real student-lab bugs:
//
//    * UseAuthorization() before UseAuthentication()
//        -> no identity has been established yet, so every request is anonymous
//           and EVERYTHING returns 401. The code looks right.
//    * UseCors() after MapControllers()
//        -> CORS headers are never added and the React app is mysteriously
//           blocked, while Postman works fine.
//    * UseExceptionHandler() late
//        -> exceptions thrown by earlier middleware escape it entirely.
// ---------------------------------------------------------------------------

// FIRST, so it wraps everything thrown later anywhere in the chain.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/swagger/v1/swagger.json", "LankaMart API v1");
        o.DocumentTitle = "LankaMart API - SE3090 Lecture 04";
    });
}

app.UseHttpsRedirection();

app.UseCors("Frontend");

// THE TWO LINES LECTURE 03 LEFT COMMENTED OUT.
app.UseAuthentication();     // who are you?  - validates the JWT   (SLIDE 31)
app.UseAuthorization();      // may you?      - roles and policies  (SLIDE 38)

app.MapControllers();

// AllowAnonymous is not optional here: the fallback policy above requires an
// authenticated user for every endpoint that does not say otherwise, and that
// includes this one. Without it, opening the site root returns 401.
app.MapGet("/", () => Results.Redirect("/swagger"))
   .AllowAnonymous()
   .ExcludeFromDescription();

// ---------------------------------------------------------------------------
//  STARTUP - check the database, apply migrations, seed, then report.
// ---------------------------------------------------------------------------
await DatabaseStartup.PrepareAsync(app);

var log = app.Services.GetRequiredService<ILogger<Program>>();

log.LogInformation("LankaMart API starting in {Environment}", app.Environment.EnvironmentName);
log.LogInformation("Access token lifetime: {Minutes} minute(s); refresh token: {Days} day(s)",
    jwtOptions.AccessTokenMinutes, jwtOptions.RefreshTokenDays);

if (app.Environment.IsDevelopment())
    log.LogWarning("Using the DEVELOPMENT JWT signing key from appsettings.Development.json. " +
                   "Never deploy with it - it is committed to this repository.");

app.Run();
