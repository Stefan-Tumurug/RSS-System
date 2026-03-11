using System;
using System.Net.Http;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using RssPlayer.Components.Configuration;
using RssPlayer.Components.Services;
using RssPlayer.Components.Utilities;
using MessageBoxBtns = System.Windows.Forms.MessageBoxButtons;
namespace RssPlayer
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AppConfiguration config = AppConfiguration.Instance;
            ServiceCollection services = new ServiceCollection();

            services.AddSingleton<LoggingService>();
            services.AddSingleton<HttpClient>(provider =>
            {
                HttpClient client = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(config.ApiTimeoutSeconds)
                };
                return client;
            });
            services.AddSingleton<NetworkService>();
            services.AddSingleton<ApiService>();
            services.AddSingleton<StyleProvider>();
            services.AddSingleton<PageManager>();
            services.AddSingleton<MaintenanceService>();
            services.AddSingleton<PlayerForm>();
            services.AddSingleton<IdleService>();
            services.AddSingleton<ConfigMonitorService>();
            services.AddSingleton<AppConfiguration>(AppConfiguration.Instance);
            ServiceProvider serviceProvider = services.BuildServiceProvider();

            LoggingService logger = serviceProvider.GetRequiredService<LoggingService>();
            logger.Log("Application Started");

            try
            {
                PlayerForm mainForm = serviceProvider.GetRequiredService<PlayerForm>();
                Application.Run(mainForm);
            }
            catch (Exception ex)
            {
                logger.LogError("Fatal application error", ex);
                MessageBox.Show(
                    $"An error occurred: {ex.Message}\n\nThe application will now close.",
                    "Application Error",
                    MessageBoxBtns.OK,
                    MessageBoxIcon.Error
                );
            }
            finally
            {
                logger.Log("Application Shutting Down");
            }
        }
    }
}