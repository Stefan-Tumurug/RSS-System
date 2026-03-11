using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using RSSAPI.Data;
using RSSAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RSSAPI.Services
{
    public class ScreenStatusMonitorService(
        IServiceProvider serviceProvider,
        ILogger<ScreenStatusMonitorService> logger) : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider = serviceProvider;
        private readonly ILogger<ScreenStatusMonitorService> _logger = logger;
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Screen Status Monitor Service is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckScreenStatusAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while checking screen statuses");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.LogInformation("Screen Status Monitor Service is stopping.");
        }

        private async Task CheckScreenStatusAsync()
        {
            using IServiceScope scope = _serviceProvider.CreateScope();
            ScreenDbContext dbContext = scope.ServiceProvider.GetRequiredService<ScreenDbContext>();

            try
            {
                List<Screen> screens = await dbContext.TblScreens
                    .Where(s => s.Status != "Offline")
                    .ToListAsync();

                DateTime now = DateTime.UtcNow;
                int screensMarkedOffline = 0;

                foreach (Screen screen in screens)
                {
                    if (screen.LastUpdated == default)
                    {
                        continue;
                    }

                    int refreshInterval = screen.RefreshInterval <= 0 ? 60 : screen.RefreshInterval;
                    TimeSpan timeSinceLastUpdate = now - screen.LastUpdated;
                    double missedIntervals = timeSinceLastUpdate.TotalMinutes / refreshInterval;

                    if (missedIntervals >= 3)
                    {
                        string previousStatus = screen.Status;
                        screen.Status = "Offline";

                        _logger.LogInformation(
                            "Screen {MacAddress} ({Name}) marked as offline after missing {MissedIntervals:F1} ping intervals. Last seen: {LastUpdated}",
                            screen.MacAddress,
                            screen.Name,
                            missedIntervals,
                            screen.LastUpdated);

                        await LogScreenStatusChange(dbContext, screen.MacAddress, previousStatus, "Offline");

                        screensMarkedOffline++;
                    }
                }

                if (screensMarkedOffline > 0)
                {
                    await dbContext.SaveChangesAsync();
                    _logger.LogInformation("Marked {Count} screens as offline due to missed pings", screensMarkedOffline);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CheckScreenStatusAsync");
                throw;
            }
        }

        private async Task LogScreenStatusChange(ScreenDbContext dbContext, string macAddress, string previousStatus, string newStatus)
        {
            try
            {
                Logs log = new()
                {
                    MacAddress = macAddress,
                    Action = $"Status automatically updated to {newStatus} (previous: {previousStatus}) - No ping received",
                    Timestamp = DateTime.UtcNow
                };

                dbContext.TblLogs.Add(log);

                string logDirectory = System.IO.Path.Combine(
                    "Logs",
                    DateTime.UtcNow.Year.ToString(),
                    DateTime.UtcNow.Month.ToString("D2"),
                    DateTime.UtcNow.Day.ToString("D2"));

                System.IO.Directory.CreateDirectory(logDirectory);

                string safeMacAddress = macAddress.Replace(":", "-").Replace("/", "_").Replace("\\", "_");
                string logFilePath = System.IO.Path.Combine(logDirectory, $"{safeMacAddress}.log");

                string logEntry = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} | {log.Action}{Environment.NewLine}";
                await System.IO.File.AppendAllTextAsync(logFilePath, logEntry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log status change for {MacAddress}", macAddress);
            }
        }
    }
}