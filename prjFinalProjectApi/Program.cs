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
using prjFinalProjectApi.Services;  //amy

var builder = WebApplication.CreateBuilder(args);


// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
    );
});

builder.Services.AddControllers();
builder.Services.AddHttpClient<LinePayService>(); //amy

// 3. Swagger
builder.Services.AddScoped<EmailSender>();
builder.Services.AddSingleton<OneTimeTokenHelper>();

// Swagger + JWT
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
        { new OpenApiSecurityScheme
            { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
          Array.Empty<string>() }
    });

    // 🔹 解決 DTO 名稱重複 (原因1)
    c.CustomSchemaIds(type => type.FullName?.Replace("+", "."));

    // 🔹 解決 DateOnly / TimeOnly 無法序列化 (原因2)
    c.MapType<DateOnly>(() => new OpenApiSchema { Type = "string", Format = "date" });
    c.MapType<TimeOnly>(() => new OpenApiSchema { Type = "string", Format = "time" });

});

// EF Core
var conn = builder.Configuration.GetConnectionString("NursingHomeConnection");
builder.Services.AddDbContext<DbNursingHomeContext>(opt => opt.UseSqlServer(conn));


// Line 登入
builder.Services.AddHttpClient();

builder.Services.AddSignalR(); // SignalR


//  綁定 Jwt 強型別設定 + 啟動期檢查
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
          ?? throw new InvalidOperationException("Jwt 設定缺失");
if (string.IsNullOrWhiteSpace(jwt.Key) ||
    string.IsNullOrWhiteSpace(jwt.Issuer) ||
    string.IsNullOrWhiteSpace(jwt.Audience))
    throw new InvalidOperationException("Jwt:Key/Issuer/Audience 不可為空");

// JWT 驗證
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpClient();

// ChatGPT
builder.Services.AddScoped<IAIService>(sp =>
    new OllamaService(sp.GetRequiredService<HttpClient>(), "http://192.168.61.90:11434/api/generate"));

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseRouting(); // 添加路由，確保 CORS 生效 (先有路由，才能有 CORS)
app.UseCors("AllowAll");
app.UseHttpsRedirection();
app.UseStaticFiles(new StaticFileOptions     // wwwroot
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.Append("Access-Control-Allow-Origin", "*"); // 允許所有來源
        ctx.Context.Response.Headers.Append("Access-Control-Allow-Methods", "GET"); // 只允許 GET
        ctx.Context.Response.Headers.Append("Access-Control-Allow-Headers", "Content-Type");
    }
});
app.UseAuthentication();   // 先驗證
app.UseAuthorization();    // 再授權
app.MapHub<ChatHub>("/chathub"); // SignalR
app.MapControllers();
app.Run();
