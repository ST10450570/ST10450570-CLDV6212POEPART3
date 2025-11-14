using ABCRetails.Data;
using ABCRetails.Models;
using ABCRetails.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace ABCRetails
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddControllersWithViews();

            // Add this to your service registration
            builder.Services.AddScoped<ICustomerSyncService, CustomerSyncService>(); 

            // Configure HttpClient for Functions API
            builder.Services.AddHttpClient<IFunctionsApiService, FunctionsApiService>(client =>
            {
                var functionsBaseUrl = builder.Configuration["FunctionsBaseUrl"];
                if (string.IsNullOrEmpty(functionsBaseUrl))
                {
                    throw new InvalidOperationException("FunctionsBaseUrl is not configured in appsettings.json.");
                }
                client.BaseAddress = new Uri(functionsBaseUrl);
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            });

            // Add Entity Framework and SQL Server
            builder.Services.AddDbContext<AuthDbContext>(options =>
                options.UseSqlServer(builder.Configuration.GetConnectionString("AuthDb")));

            // Add Authentication
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.LoginPath = "/Login";
                    options.LogoutPath = "/Login/Logout";
                    options.AccessDeniedPath = "/Home/AccessDenied";
                    options.ExpireTimeSpan = TimeSpan.FromHours(2);
                });

            builder.Services.AddAuthorization();

            builder.Services.AddLogging();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();

            // Add authentication & authorization middleware
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            // Initialize database and seed initial data
            InitializeDatabase(app);

            app.Run();
        }

        private static void InitializeDatabase(WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var services = scope.ServiceProvider;
            try
            {
                var context = services.GetRequiredService<AuthDbContext>();
                context.Database.EnsureCreated();

                // Seed initial admin and customer users if they don't exist
                if (!context.Users.Any())
                {
                    context.Users.AddRange(
                        new User
                        {
                            Username = "admin01",
                            Email = "admin@abcretail.com",
                            PasswordHash = "adminpass123_hashed",
                            Role = "Admin",
                            CreatedAt = DateTime.UtcNow
                        },
                        new User
                        {
                            Username = "customer1",
                            Email = "customer1@gmail.com",
                            PasswordHash = "customerpass123_hashed",
                            Role = "Customer",
                            CreatedAt = DateTime.UtcNow
                        }
                    );
                    context.SaveChanges();

                    var logger = services.GetRequiredService<ILogger<Program>>();
                    logger.LogInformation("Database seeded with default users.");
                }
            }
            catch (Exception ex)
            {
                var logger = services.GetRequiredService<ILogger<Program>>();
                logger.LogError(ex, "An error occurred while initializing the database.");
            }
        }
    }
}