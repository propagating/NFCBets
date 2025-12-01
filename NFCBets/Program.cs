using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NFCBets.EF.Models;
using NFCBets.Services;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NFCBets.Services.Interfaces;

namespace NFCBets
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices(services =>
                {
                    services.AddDbContext<NfcbetsContext>();
                    services.AddHttpClient<IFoodClubDataService, FoodClubDataService>(client =>
                    {
                        client.BaseAddress = new Uri("http://cdn.neofood.club/");
                    });
                    services.AddScoped<IFoodAdjustmentService, FoodAdjustmentService>();
                    services.AddScoped<IFeatureEngineeringService, FeatureEngineeringService>();
                    services.AddScoped<IMlModelService, MlModelService>();
                    services.AddScoped<IBettingStrategyService, BettingStrategyService>();
                    services.AddScoped<IDailyBettingPipeline, DailyBettingPipeline>();
                }).ConfigureLogging(logging => { logging.SetMinimumLevel(LogLevel.Warning);})
                .Build();

            var pipeline = host.Services.GetRequiredService<IDailyBettingPipeline>();
            var mlService = host.Services.GetRequiredService<IMlModelService>();
            var dataService = host.Services.GetRequiredService<IFoodClubDataService>();

            try
            {
                // One-time: Train the model (or load existing)
                var modelPath = "Models/foodclub_first_model.zip";
                
                if (!File.Exists(modelPath))
                {
                    Console.WriteLine("🏋️ Training new model...");
                    await mlService.TrainModelAsync();
                    mlService.SaveModel(modelPath);
                }
                else
                {
                    Console.WriteLine("📂 Loading existing model...");
                    mlService.LoadModel(modelPath);
                }

                // Generate recommendations for today
                var currentRound = 9705; // Get this from API or user input
                //make sure we have updated round data for the last 100 rounds at least
                Console.WriteLine($"Collecting data for last 100 rounds up to round {currentRound}...");
                await dataService.CollectRangeAsync(currentRound-100, currentRound);
                Console.Write($"Generating recommendations for round {currentRound}...");
                var recommendations = await pipeline.GenerateRecommendationsAsync(currentRound);

                // Display recommendations
                DisplayRecommendations(recommendations);

                // Save to file
                SaveRecommendationsToFile(recommendations);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
            }
        }

        static void DisplayRecommendations(DailyBettingRecommendations recommendations)
        {
            Console.WriteLine("\n═══════════════════════════════════════════════════");
            Console.WriteLine($"🎲 FOOD CLUB BETTING RECOMMENDATIONS - Round {recommendations.RoundId}");
            Console.WriteLine($"📅 Generated: {recommendations.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine("═══════════════════════════════════════════════════\n");

            foreach (var series in recommendations.BetSeries)
            {
                Console.WriteLine($"\n🎯 {series.Name.ToUpper()} STRATEGY ({series.RiskLevel})");
                Console.WriteLine($"   {series.Description}");
                Console.WriteLine("   ─────────────────────────────────────────────────");

                for (int i = 0; i < series.Bets.Count; i++)
                {
                    Console.WriteLine($"   {i + 1,2}. {series.Bets[i]}");
                }

                var totalEV = series.Bets.Sum(b => b.ExpectedValue);
                var avgEV = series.Bets.Average(b => b.ExpectedValue);
                Console.WriteLine($"   ─────────────────────────────────────────────────");
                Console.WriteLine($"   Total EV: {totalEV:+0.00;-0.00}, Average EV: {avgEV:+0.00;-0.00}");
            }
        }

        static void SaveRecommendationsToFile(DailyBettingRecommendations recommendations)
        {
            var fileName = $"Recommendations/round_{recommendations.RoundId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
            Directory.CreateDirectory("Recommendations");

            var json = JsonSerializer.Serialize(recommendations, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(fileName, json);

            Console.WriteLine($"\n💾 Recommendations saved to {fileName}");
        }
    }
}