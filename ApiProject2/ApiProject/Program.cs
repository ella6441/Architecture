using ApiProject.Data;
using ApiProject.MiddleWare;
using ApiProject.Repositories.Implement;
using ApiProject.Repositories.Interface;
using ApiProject.Services;
using ApiProject.Services.Implement;
using ApiProject.Services.Interface;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Text;
using System.Threading.RateLimiting;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("Starting Gifts API");

    var builder = WebApplication.CreateBuilder(args);

    // Use Serilog
    builder.Host.UseSerilog();

    // Add controllers and JSON options
    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        });
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowAngular",
            policy =>
            {
                policy.WithOrigins("http://localhost:4200", "http://localhost")
                      .AllowAnyHeader()
                      .AllowAnyMethod();
            });
    });
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo { Title = "Gifts API", Version = "v1" });

        // JWT Security
        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "Bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Enter 'Bearer {token}'"
        });

        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                },
                new string[] {}
            }
        });
    });

    // Redis Cache
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = builder.Configuration["Redis:ConnectionString"];
        options.InstanceName = "GiftsApi:";  // prefix לכל המפתחות ב-Redis
    });
    builder.Services.AddSingleton<ApiProject.Services.Interface.ICacheService,
                                  ApiProject.Services.Implement.CacheService>();

    // Kafka producer — long-lived, thread-safe, one instance per app
    builder.Services.AddSingleton<ApiProject.Services.Interface.IKafkaProducerService,
                                  ApiProject.Services.Implement.KafkaProducerService>();

    // Database Context
    builder.Services.AddDbContext<ProjectContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));


    // Repositories
    builder.Services.AddScoped<IGiftRepository, GiftRepository>();
    builder.Services.AddScoped<ICartRepository, CartRepository>();
    builder.Services.AddScoped<IDonorRepository, DonorRepository>();
    builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
    builder.Services.AddScoped<IAuthRepository, AuthRepository>();
    builder.Services.AddScoped<ILotteryRepository, LotteryRepository>();
    builder.Services.AddScoped<ISalesRepository, SalesRepository>();

    // Services
    builder.Services.AddScoped<IGiftsService, GiftsService>();
    builder.Services.AddScoped<ICartService, CartService>();
    builder.Services.AddScoped<IDonorService, DonorService>();
    builder.Services.AddScoped<ICategoryService, CategoryService>();
    builder.Services.AddScoped<IAuthService, AuthService>();
    builder.Services.AddScoped<ILotteryService, LotteryService>();
    builder.Services.AddScoped<IEmailService, EmailService>();
    builder.Services.AddScoped<ISalesService, SalesService>();


    // JWT Authentication
    var jwtSettings = builder.Configuration.GetSection("Jwt");
    var secretKey = jwtSettings["Secret"] ?? throw new InvalidOperationException("JWT Secret is not configured");

    builder.Services.AddAuthentication(options =>
    {
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
            ValidIssuer = jwtSettings["Issuer"],
            ValidAudience = jwtSettings["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
            ClockSkew = TimeSpan.Zero
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue("jwt_token", out var cookieToken))
                    context.Token = cookieToken;
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                Log.Warning("JWT Authentication failed: {Error}", context.Exception.Message);
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                var userId = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                Log.Debug("JWT token validated for user {UserId}", userId);
                return Task.CompletedTask;
            }
        };
    });

    builder.Services.AddAuthorization();

    // Rate Limiting — Sliding Window
    builder.Services.AddRateLimiter(options =>
    {
        options.AddSlidingWindowLimiter("sliding", opt =>
        {
            opt.PermitLimit = builder.Configuration.GetValue<int>("RateLimiting:PermitLimit", 30);
            opt.Window = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("RateLimiting:WindowSeconds", 60));
            opt.SegmentsPerWindow = builder.Configuration.GetValue<int>("RateLimiting:SegmentsPerWindow", 6);
            opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            opt.QueueLimit = 0;
        });

        options.OnRejected = async (context, token) =>
        {
            context.HttpContext.Response.StatusCode = 429;
            Log.Warning("Rate limit exceeded for {IP}", context.HttpContext.Connection.RemoteIpAddress);
            await context.HttpContext.Response.WriteAsJsonAsync(new
            {
                message = "יותר מדי בקשות. נסה שוב מאוחר יותר.",
                statusCode = 429
            });
        };
    });

    var app = builder.Build();

    // Apply pending EF Core migrations and seed baseline data automatically on startup —
    // so a fresh container (new machine, empty DB volume) works with no manual steps.
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ProjectContext>();
        db.Database.Migrate();

        if (!db.categories.Any())
        {
            db.categories.AddRange(
                new ApiProject.Models.CategoryModel { Name = "אלקטרוניקה" },
                new ApiProject.Models.CategoryModel { Name = "ספרים" },
                new ApiProject.Models.CategoryModel { Name = "בית וגן" }
            );
            db.SaveChanges();
        }

        if (!db.donors.Any())
        {
            db.donors.Add(new ApiProject.Models.DonorModel
            {
                FirstName = "ישראל",
                LastName = "ישראלי",
                Email = "donor@example.com",
                Phone = "0501234567"
            });
            db.SaveChanges();
        }

        if (!db.gifts.Any())
        {
            var donorId = db.donors.First().Id;
            var categoryIds = db.categories.OrderBy(c => c.Id).Select(c => c.Id).ToList();

            db.gifts.AddRange(
                new ApiProject.Models.GiftModel { Name = "אוזניות אלחוטיות", Description = "אוזניות בלוטות איכותיות", TicketPrice = 149, Image = "5.jpg", DonorModelId = donorId, CategoryModelId = categoryIds[0], isRaffleDone = false },
                new ApiProject.Models.GiftModel { Name = "ספר בישול", Description = "ספר מתכונים", TicketPrice = 89, Image = "7.jpg", DonorModelId = donorId, CategoryModelId = categoryIds[1], isRaffleDone = false },
                new ApiProject.Models.GiftModel { Name = "מנורת עיצוב", Description = "מנורה לבית", TicketPrice = 199, Image = "10.jpg", DonorModelId = donorId, CategoryModelId = categoryIds[2], isRaffleDone = false }
            );
            db.SaveChanges();
        }
    }

    // Middleware pipeline
    app.UseSwagger();
    app.UseSwaggerUI();

    app.UseHttpsRedirection();

    app.UseCors("AllowAngular");

    app.UseRequestLogging();
    app.UseException();
    app.UseRateLimiter();

    app.UseAuthentication();
    app.UseAuthorization();

    // Map controllers with rate limiting
    app.MapControllers().RequireRateLimiting("sliding");

    Log.Information("Gifts API is now running");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }
