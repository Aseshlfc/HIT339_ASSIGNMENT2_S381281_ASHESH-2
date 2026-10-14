using System.Threading.RateLimiting;
using LibraryManagementSystem.Api;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<LibraryContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<LibraryContext>()
    .AddDefaultTokenProviders();

builder.Services.AddScoped<ITransferService, TransferService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddSingleton<IReportPdfService, ReportPdfService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IReservationService, ReservationService>();
builder.Services.AddScoped<IDueDateReminderService, DueDateReminderService>();

// Section 2: self-service kiosk, CSV import and the public read-only API.
builder.Services.AddScoped<IKioskService, KioskService>();
builder.Services.AddScoped<IItemImportService, ItemImportService>();
builder.Services.Configure<PublicApiOptions>(builder.Configuration.GetSection(PublicApiOptions.SectionName));
builder.Services.AddScoped<ApiKeyFilter>();
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();

        // The kiosk is used by people, not programs, so it gets a readable message.
        if (context.HttpContext.Request.Path.StartsWithSegments("/kiosk"))
        {
            context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
            await context.HttpContext.Response.WriteAsync(
                "Too many attempts from this kiosk. Please wait a minute and try again, or ask library staff for help.",
                cancellationToken);
        }
    };
    // Limited per client address, not per API key: a caller trying many wrong keys
    // would otherwise get a fresh allowance for every guess.
    limiter.AddPolicy(PublicApiOptions.PolicyName, httpContext =>
    {
        var perMinute = httpContext.RequestServices
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<PublicApiOptions>>().Value.RequestsPerMinute;
        return RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, perMinute),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });
    // The kiosk has no staff login and card numbers are short, so card look-ups and
    // checkouts are limited per client address to slow down guessing card numbers.
    limiter.AddPolicy(KioskOptions.PolicyName, httpContext =>
    {
        var perMinute = httpContext.RequestServices
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<KioskOptions>>().Value.RequestsPerMinute;
        return RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, perMinute),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });
});

builder.Services.Configure<KioskOptions>(builder.Configuration.GetSection(KioskOptions.SectionName));

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.Name = "SimpleLibraryAuth";
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LibraryContext>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    db.Database.Migrate();
    await DbSeeder.SeedAsync(db, roleManager, userManager);
}

app.Run();
