// Program.cs
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using prjFinalProjectApi.Helpers;
using prjFinalProjectApi.Models;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ===== CORS（從 appsettings 的 AllowedCorsOrigins 讀；允許帶 Cookie）=====
var allowed = builder.Configuration
    .GetSection("AllowedCorsOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(o => o.AddPolicy("AllowWeb", p =>
    p.WithOrigins(allowed)
     .AllowAnyHeader()
     .AllowAnyMethod()
     .AllowCredentials()
));

// 必須註冊 MVC 控制器
builder.Services.AddControllers();

// 其他服務
builder.Services.AddScoped<EmailSender>();
builder.Services.AddSingleton<OneTimeTokenHelper>();

// ===== Swagger（前台用 Bearer 測試仍可用）=====
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
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });

    // 讓 DateOnly/TimeOnly 在 Swagger 正確呈現
    c.CustomSchemaIds(type => type.FullName?.Replace("+", "."));
    c.MapType<DateOnly>(() => new OpenApiSchema { Type = "string", Format = "date" });
    c.MapType<TimeOnly>(() => new OpenApiSchema { Type = "string", Format = "time" });
});

// ===== EF Core =====
var conn = builder.Configuration.GetConnectionString("NursingHomeConnection");
builder.Services.AddDbContext<DbNursingHomeContext>(opt => opt.UseSqlServer(conn));

// ===== 讀取前台 JWT 設定 =====
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"] ?? throw new InvalidOperationException("Jwt:Key 缺失");
var jwtIssuer = jwtSection["Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer 缺失");
var jwtAudience = jwtSection["Audience"] ?? throw new InvalidOperationException("Jwt:Audience 缺失");

// ===== Authentication：保留前台 JWT，新增後台 Cookie =====
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
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.Zero,
        NameClaimType = ClaimTypes.Name
    };
})
// 後台員工 Cookie（配合跨域）
.AddCookie("EmployeeCookie", options =>
{
    options.LoginPath = "/api/EmployeeUserAccounts/login-cookie";
    options.AccessDeniedPath = "/api/forbidden";
    options.Cookie.Name = "erp.emp";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // 需 https
    options.Cookie.SameSite = SameSiteMode.None;             // 跨站 XHR 必須 None
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
});

// ===== Authorization：員工 Cookie 專屬 Policy =====
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("EmployeeCookieOnly", p =>
        p.AddAuthenticationSchemes("EmployeeCookie")
         .RequireAuthenticatedUser());
});

var app = builder.Build();

// ===== Swagger（僅開發）=====
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ===== 強制 HTTPS / 靜態檔 =====
app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        const int days = 30;
        ctx.Context.Response.Headers.CacheControl = $"public,max-age={days * 24 * 60 * 60}";
    }
});

// ===== 路由 / CORS / Auth =====
app.UseRouting();
app.UseCors("AllowWeb");           // 一定要在 Auth 前
app.UseAuthentication();
app.UseAuthorization();

// ===== API 路由 / SPA Fallback =====
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
