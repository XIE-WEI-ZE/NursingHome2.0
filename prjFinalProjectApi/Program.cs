// Program.cs
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using prjFinalProjectApi.Helpers;
using prjFinalProjectApi.Hubs;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Services;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ===== CORS：統一 AllowWeb，從 appsettings:AllowedCorsOrigins 讀 =====
var allowed = builder.Configuration
    .GetSection("AllowedCorsOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(o => o.AddPolicy("AllowWeb", p =>
    p.WithOrigins(allowed)
     .AllowAnyHeader()
     .AllowAnyMethod()
     .AllowCredentials()
));

// ===== MVC / 服務 =====
builder.Services.AddControllers();
builder.Services.AddHttpClient();                 // 一般 HttpClient
builder.Services.AddHttpClient<LinePayService>(); // LinePay
builder.Services.AddScoped<EmailSender>();
builder.Services.AddSingleton<OneTimeTokenHelper>();
builder.Services.AddSignalR();

// ===== EF Core =====
var conn = builder.Configuration.GetConnectionString("NursingHomeConnection");
builder.Services.AddDbContext<DbNursingHomeContext>(opt => opt.UseSqlServer(conn));

// ===== JWT Options（強型別）+ 驗證 =====
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
          ?? throw new InvalidOperationException("Jwt 設定缺失");
if (string.IsNullOrWhiteSpace(jwt.Key) || string.IsNullOrWhiteSpace(jwt.Issuer) || string.IsNullOrWhiteSpace(jwt.Audience))
    throw new InvalidOperationException("Jwt:Key/Issuer/Audience 不可為空");

builder.Services.AddAuthentication(options =>
{
    // 預設仍用 JWT（前台）
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
        ClockSkew = TimeSpan.Zero,
        NameClaimType = ClaimTypes.Name
    };
})
// 後台員工 Cookie（跨站需 HTTPS + SameSite=None）
.AddCookie("EmployeeCookie", options =>
{
    options.LoginPath = "/api/EmployeeUserAccounts/login-cookie";
    options.AccessDeniedPath = "/api/forbidden";
    options.Cookie.Name = "erp.emp";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.None;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
});

// ===== Authorization：前台/後台分流 Policy =====
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("MemberOnly", p =>
        p.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
         .RequireAuthenticatedUser());

    options.AddPolicy("EmployeeCookieOnly", p =>
        p.AddAuthenticationSchemes("EmployeeCookie")
         .RequireAuthenticatedUser());
});

// LLM 服務（Ollama）
builder.Services.AddScoped<IAIService>(sp =>
    new OllamaService(sp.GetRequiredService<HttpClient>(), "http://192.168.61.90:11434/api/generate"));

// ===== Swagger（開發期）=====
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "NursingHome API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "輸入 JWT Token，格式: Bearer {your token}"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
    c.CustomSchemaIds(t => t.FullName?.Replace("+", "."));
    c.MapType<DateOnly>(() => new OpenApiSchema { Type = "string", Format = "date" });
    c.MapType<TimeOnly>(() => new OpenApiSchema { Type = "string", Format = "time" });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        const int days = 30;
        ctx.Context.Response.Headers["Cache-Control"] = $"public, max-age={days * 24 * 60 * 60}";
    }
});

app.UseRouting();
app.UseCors("AllowWeb");
app.UseAuthentication();
app.UseAuthorization();

app.MapHub<ChatHub>("/chathub");
app.MapControllers();


app.Run();
