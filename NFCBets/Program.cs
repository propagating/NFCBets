using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NFCBets.EF.Models;
using NFCBets.Services;
using Microsoft.EntityFrameworkCore;

namespace NFCBets.Testing
{
    class Program
    {
        static void ConfigureServices(IServiceCollection services)
        {
            var connectionString = "Server=localhost;Database=NFCBets;Trusted_Connection=True;encrypt=true;TrustServerCertificate=true;";
            
            // Database (single-threaded)
            services.AddDbContext<NfcbetsContext>(options => options.UseSqlServer(connectionString), ServiceLifetime.Scoped);

            // Core services
            services.AddScoped<IFoodAdjustmentService, FoodAdjustmentService>();
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Warning);
            });
        }

        static async Task Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices(ConfigureServices)
                .Build();

            var mode = args.Length > 0 ? args[0].ToLower() : "train";
            var logger = host.Services.GetRequiredService<ILogger<Program>>();

            //TODO: add switch statement to determine what you would like to do from retrieving round ata to training the model, to running the model for a specific round
        }
    }
}