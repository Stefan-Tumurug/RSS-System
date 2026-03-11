using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RSSAPI.Data;
using RSSAPI.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace RSSAPI.Controllers
{
    [Route("api/screens")]
    [ApiController]
    public class ScreenController(ScreenDbContext context, ILogger<ScreenController> logger) : ControllerBase
    {
        private readonly ScreenDbContext context = context;
        private readonly ILogger<ScreenController> logger = logger;

        private static async Task WriteLogToFileAsync(string macAddress, string action, DateTime? localTime = null)
        {
            DateTime logTime = localTime ?? DateTime.UtcNow;
            string logDirectory = Path.Combine("Logs", logTime.Year.ToString(), logTime.Month.ToString("D2"), logTime.Day.ToString("D2"));
            Directory.CreateDirectory(logDirectory);

            string safeMacAddress = macAddress.Replace(":", "-").Replace("/", "_").Replace("\\", "_");
            string logFilePath = Path.Combine(logDirectory, $"{safeMacAddress}.log");

            string logEntry = $"{logTime:yyyy-MM-dd HH:mm:ss} | {action}{Environment.NewLine}";
            await System.IO.File.AppendAllTextAsync(logFilePath, logEntry);
        }

        private async Task LogScreenAction(string macAddress, string action, int timezoneOffset = 0, string? previousStatus = null)
        {
            try
            {
                if (action.StartsWith("Status updated to"))
                {
                    string newStatus = action.Replace("Status updated to ", "");
                    if (previousStatus != null && previousStatus == newStatus)
                    {
                        logger.LogInformation("[LogScreenAction] Skipping log - No status change for MAC: {MacAddress}", macAddress);
                        return;
                    }

                    Screen? screen = await context.TblScreens.FirstOrDefaultAsync(s => s.MacAddress == macAddress);
                    if (screen != null && screen.Status == newStatus)
                    {
                        logger.LogInformation("[LogScreenAction] Skipping redundant status log for MAC: {MacAddress}", macAddress);
                        return;
                    }
                }

                Logs log = new()
                {
                    MacAddress = macAddress,
                    Action = action,
                    Timestamp = DateTime.UtcNow
                };

                context.TblLogs.Add(log);
                await context.SaveChangesAsync();

                DateTime userLocalTime = log.Timestamp.AddMinutes(-timezoneOffset);
                await WriteLogToFileAsync(macAddress, action, userLocalTime);
            }
            catch (Exception ex)
            {
                logger.LogError("[LogScreenAction] Failed to log action: {ErrorMessage}", ex.Message);
            }
        }

		[HttpPost("player/register")]
		public async Task<IActionResult> RegisterPlayer([FromBody] PlayerRegistration request)
		{
			try
			{
				if (!ModelState.IsValid)
				{
					return BadRequest(new
					{
						Success = false,
						ErrorMessage = "Invalid model state",
						Item = null as object
					});
				}

				if (string.IsNullOrEmpty(request.MacAddress))
				{
					return BadRequest(new
					{
						Success = false,
						ErrorMessage = "MacAddress is required.",
						Item = null as object
					});
				}

				Screen? screen = await context.TblScreens.FirstOrDefaultAsync(p => p.MacAddress == request.MacAddress);

				if (screen == null)
				{
					Screen newScreen = new()
                    {
						MacAddress = request.MacAddress,
						Name = request.Name ?? "Default Name",
						Url = request.Url,
						Status = "Online",
						LastUpdated = DateTime.UtcNow,
						LastSeenOnline = DateTime.UtcNow,
						IdleScreenUrl = request.IdleScreenUrl,
						Address = request.Address,
						OperatingSystem = request.OperatingSystem,
						AutoRestart = request.AutoRestart,
						RefreshInterval = request.RefreshInterval,
						ScreenResolution = request.ScreenResolution ?? "1920x1080",
						StartupEnabled = request.StartupEnabled
					};

					context.TblScreens.Add(newScreen);
					await LogScreenAction(request.MacAddress, "Player Registered");
					screen = newScreen; 
				}

				if (screen != null)
				{
					screen.Name = request.Name ?? screen.Name;
					screen.Url = request.Url ?? screen.Url;
					screen.Status = "Online";
					screen.LastUpdated = DateTime.UtcNow;
					screen.LastSeenOnline = DateTime.UtcNow;
					screen.IdleScreenUrl = request.IdleScreenUrl ?? screen.IdleScreenUrl;
					screen.Address = request.Address ?? screen.Address;
					screen.OperatingSystem = request.OperatingSystem ?? screen.OperatingSystem;
					screen.AutoRestart = request.AutoRestart;
					screen.StartupEnabled = request.StartupEnabled ?? screen.StartupEnabled;

					if (request.RefreshInterval != 0)
					{
						screen.RefreshInterval = request.RefreshInterval;
					}

					if (!string.IsNullOrEmpty(request.ScreenResolution))
					{
						screen.ScreenResolution = request.ScreenResolution;
					}

					await LogScreenAction(request.MacAddress, "Player Configuration Updated");
				}

				await context.SaveChangesAsync();
				await LogScreenAction(request.MacAddress, "Status updated to Online");

				return Ok(new
				{
					Success = true,
					ErrorMessage = string.Empty,
					Item = "Player registered or updated successfully."
				});
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error in RegisterPlayer");
				return BadRequest(new
				{
					Success = false,
					ErrorMessage = "An error occurred while registering the player.",
					Item = null as object
				});
			}
		}


		[HttpGet("player/{macAddress}/config")]
        public async Task<IActionResult> GetPlayerConfig(string macAddress)
        {
            try
            {
                Screen? screen = await context.TblScreens.FirstOrDefaultAsync(p => p.MacAddress == macAddress);
                if (screen == null)
                {
                    return NotFound(new
                    {
                        Success = false,
                        ErrorMessage = "Player not found.",
                        Item = null as object
                    });
                }

                return Ok(new
                {
                    Success = true,
                    ErrorMessage = string.Empty,
                    Item = new
                    {
                        screen.MacAddress,
                        Name = screen.Name ?? "No Name",
                        Url = string.IsNullOrWhiteSpace(screen.Url) ? null : screen.Url,
                        screen.AutoRestart,
                        screen.IdleScreenUrl,
                        screen.RefreshInterval,
                        screen.ScreenResolution,
                        screen.Address,
                        screen.OperatingSystem,
                        screen.StartupEnabled
                    }
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in GetPlayerConfig");
                return BadRequest(new
                {
                    Success = false,
                    ErrorMessage = "An error occurred while retrieving player configuration.",
                    Item = null as object
                });
            }
        }

        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
        [HttpPost("player/{macAddress}/config")]
        public async Task<IActionResult> SavePlayerConfig(string macAddress, [FromBody] Config updatedConfig)
        {
            try
            {
                if (updatedConfig == null)
                {
                    return BadRequest(new
                    {
                        Success = false,
                        ErrorMessage = "Invalid configuration data.",
                        Item = null as object
                    });
                }

                Screen? screen = await context.TblScreens.FirstOrDefaultAsync(p => p.MacAddress == macAddress);
                if (screen == null)
                {
                    return NotFound(new
                    {
                        Success = false,
                        ErrorMessage = "Player not found.",
                        Item = null as object
                    });
                }

                List<string> changes = [];

                if (!string.IsNullOrWhiteSpace(updatedConfig.Name) && updatedConfig.Name != screen.Name)
                {
                    changes.Add($"Name changed from '{screen.Name}' to '{updatedConfig.Name}'");
                    screen.Name = updatedConfig.Name;
                }

                if (!string.IsNullOrWhiteSpace(updatedConfig.Url) && updatedConfig.Url != screen.Url)
                {
                    changes.Add($"URL changed from '{screen.Url}' to '{updatedConfig.Url}'");
                    screen.Url = updatedConfig.Url;
                }

                if (updatedConfig.AutoRestart != screen.AutoRestart)
                {
                    changes.Add($"AutoRestart changed from '{screen.AutoRestart}' to '{updatedConfig.AutoRestart}'");
                    screen.AutoRestart = updatedConfig.AutoRestart;
                }

                if (!string.IsNullOrWhiteSpace(updatedConfig.Address) && updatedConfig.Address != screen.Address)
                {
                    changes.Add($"Address changed from '{screen.Address}' to '{updatedConfig.Address}'");
                    screen.Address = updatedConfig.Address;
                }

                if (!string.IsNullOrWhiteSpace(updatedConfig.OperatingSystem) && updatedConfig.OperatingSystem != screen.OperatingSystem)
                {
                    changes.Add($"Operating System changed from '{screen.OperatingSystem}' to '{updatedConfig.OperatingSystem}'");
                    screen.OperatingSystem = updatedConfig.OperatingSystem;
                }

                if (!string.IsNullOrWhiteSpace(updatedConfig.IdleScreenUrl) && updatedConfig.IdleScreenUrl != screen.IdleScreenUrl)
                {
                    changes.Add($"IdleScreenUrl changed from '{screen.IdleScreenUrl}' to '{updatedConfig.IdleScreenUrl}'");
                    screen.IdleScreenUrl = updatedConfig.IdleScreenUrl;
                }
                if (updatedConfig.StartupEnabled != screen.StartupEnabled)
                {
                    changes.Add($"StartupEnabled changed from '{screen.StartupEnabled}' to '{updatedConfig.StartupEnabled}'");
                    screen.StartupEnabled = updatedConfig.StartupEnabled;
                }
                if (updatedConfig.RefreshInterval != screen.RefreshInterval)
                {
                    changes.Add($"RefreshInterval changed from '{screen.RefreshInterval}' to '{updatedConfig.RefreshInterval}'");
                    screen.RefreshInterval = updatedConfig.RefreshInterval;
                }

                if (!string.IsNullOrWhiteSpace(updatedConfig.ScreenResolution) && updatedConfig.ScreenResolution != screen.ScreenResolution)
                {
                    changes.Add($"TblScreen Resolution changed from '{screen.ScreenResolution}' to '{updatedConfig.ScreenResolution}'");
                    screen.ScreenResolution = updatedConfig.ScreenResolution;
                }


                screen.LastUpdated = DateTime.UtcNow;

                await context.SaveChangesAsync();

                if (changes.Count > 0)
                {
                    string detailedLog = $"Configuration Updated: {string.Join("; ", changes)}";
                    await LogScreenAction(macAddress, detailedLog);
                }

                return Ok(new
                {
                    Success = true,
                    ErrorMessage = string.Empty,
                    Item = "Configuration updated successfully."
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in SavePlayerConfig");
                return BadRequest(new
                {
                    Success = false,
                    ErrorMessage = "An error occurred while saving player configuration.",
                    Item = null as object
                });
            }
        }
        [Authorize]
        [HttpGet("screens")]
        public async Task<IActionResult> GetScreens()
        {
            try
            {
                List<TblScreenDto> screens = await context.TblScreens
                    .Select(s => new TblScreenDto
                    {
                        MacAddress = s.MacAddress ?? string.Empty,
                        Name = s.Name,
                        Url = s.Url,
                        Status = s.Status == "Idle" ? "Idle" : s.Status ?? "Unknown",
                        AutoRestart = s.AutoRestart,
                        LastUpdated = s.LastUpdated,
                        LastSeenOnline = s.LastSeenOnline,
                        RefreshInterval = s.RefreshInterval,
                        ScreenResolution = s.ScreenResolution,
                        Address = s.Address,
                        OperatingSystem = s.OperatingSystem,
                        StartupEnabled = s.StartupEnabled
                    })
                    .ToListAsync();

                return Ok(new
                {
                    Success = true,
                    ErrorMessage = string.Empty,
                    Items = screens
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in GetScreens");
                return BadRequest(new
                {
                    Success = false,
                    ErrorMessage = "An error occurred while retrieving screens.",
                    Items = new List<TblScreenDto>()
                });
            }
        }

        [Authorize]
        [HttpGet("player/{macAddress}/logs")]
        public async Task<IActionResult> GetLogsForScreen(
    string macAddress,
    int timezoneOffset = 0,
    DateTime? startDate = null,
    DateTime? endDate = null,
    int page = 1,
    int pageSize = 50)
        {
            try
            {
                logger.LogInformation("[API] Fetching logs for {MacAddress} with StartDate: {StartDate}, EndDate: {EndDate}, Page: {Page}, PageSize: {PageSize}",
                    macAddress, startDate, endDate, page, pageSize);

                if (page < 1 || pageSize < 1)
                {
                    return BadRequest(new
                    {
                        Success = false,
                        ErrorMessage = "Page and page size must be greater than zero.",
                        Item = default(object?)
                    });
                }

                IQueryable<Logs> query = context.TblLogs.Where(l => l.MacAddress == macAddress);

                if (startDate.HasValue)
                {
                    query = query.Where(l => l.Timestamp >= startDate.Value);
                    logger.LogInformation("[API] Filtering logs with StartDate: {StartDate}", startDate);
                }

                if (endDate.HasValue)
                {
                    query = query.Where(l => l.Timestamp <= endDate.Value);
                    logger.LogInformation("[API] Filtering logs with EndDate: {EndDate}", endDate);
                }

                int totalLogs = await query.CountAsync();
                List<Logs> logs = await query
                    .OrderByDescending(l => l.Timestamp)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                logger.LogInformation("[API] Found {TotalLogs} logs, returning {ReturnedLogs} logs", totalLogs, logs.Count);

                return Ok(new
                {
                    Success = true,
                    ErrorMessage = string.Empty,
                    Item = new
                    {
                        Logs = logs.Select(l => new
                        {
                            l.ID,
                            l.MacAddress,
                            l.Action,
                            Timestamp = l.Timestamp.AddMinutes(-timezoneOffset)
                        }).ToList(),
                        TotalLogs = totalLogs,
                        CurrentPage = page,
                        TotalPages = (int)Math.Ceiling((double)totalLogs / pageSize)
                    }
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[API] Error in GetLogsForScreen");
                return BadRequest(new
                {
                    Success = false,
                    ErrorMessage = "An error occurred while retrieving logs.",
                    Item = default(object?)
                });
            }
        }


        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
        [HttpDelete("player/{macAddress}")]
        public async Task<IActionResult> DeleteScreen(string macAddress)
        {
            try
            {
                Screen? screen = await context.TblScreens.FirstOrDefaultAsync(p => p.MacAddress == macAddress);
                if (screen == null)
                {
                    return NotFound(new
                    {
                        Success = false,
                        ErrorMessage = "TblScreen not found.",
                        Item = null as object
                    });
                }

                context.TblScreens.Remove(screen);
                await context.SaveChangesAsync();
                await LogScreenAction(macAddress, "TblScreen Deleted");

                return Ok(new
                {
                    Success = true,
                    ErrorMessage = string.Empty,
                    Item = "TblScreen deleted successfully."
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in DeleteScreen");
                return BadRequest(new
                {
                    Success = false,
                    ErrorMessage = "An error occurred while deleting the screen.",
                    Item = null as object
                });
            }
        }

        [HttpPost("player/{macAddress}/status")]
        public async Task<IActionResult> UpdatePlayerStatus(string macAddress, [FromBody] StatusUpdate statusUpdate)
        {
            try
            {
                if (statusUpdate == null || string.IsNullOrWhiteSpace(statusUpdate.Status))
                {
                    logger.LogWarning("[UpdatePlayerStatus] Received empty or invalid status update for {MacAddress}", macAddress);
                    return BadRequest(new
                    {
                        Success = false,
                        ErrorMessage = "Status is required.",
                        Item = null as object
                    });
                }

                logger.LogInformation("[UpdatePlayerStatus] Received status update: {MacAddress} -> {Status}", macAddress, statusUpdate.Status);

                Screen? screen = await context.TblScreens.FirstOrDefaultAsync(p => p.MacAddress == macAddress);
                if (screen == null)
                {
                    logger.LogWarning("[UpdatePlayerStatus] No screen found for MAC {MacAddress}", macAddress);
                    return NotFound(new
                    {
                        Success = false,
                        ErrorMessage = "Player not found.",
                        Item = null as object
                    });
                }

                string previousStatus = screen.Status ?? "Unknown";
                bool statusChanged = previousStatus != statusUpdate.Status;

                screen.Status = statusUpdate.Status;
                screen.LastUpdated = statusUpdate.LastUpdated ?? DateTime.UtcNow;

                if (statusUpdate.Status == "Online" || statusUpdate.Status == "Idle")
                {
                    screen.LastSeenOnline = statusUpdate.LastSeenOnline ?? DateTime.UtcNow;
                }

                await context.SaveChangesAsync();
                if (statusChanged)
                {
                    logger.LogInformation("[UpdatePlayerStatus] Status successfully updated for {MacAddress}: {PreviousStatus} -> {NewStatus}", macAddress, previousStatus, screen.Status);

                    await LogScreenAction(macAddress, $"Status updated to {statusUpdate.Status}", 0, previousStatus);

                    if (previousStatus != "Idle" && statusUpdate.Status == "Idle")
                    {
                        logger.LogInformation("[UpdatePlayerStatus] Logging 'Screen went idle' for {MacAddress}", macAddress);
                        await LogScreenAction(macAddress, "Screen went idle", 0);
                    }
                    if (previousStatus == "Idle" && statusUpdate.Status == "Online")
                    {
                        logger.LogInformation("[UpdatePlayerStatus] Logging 'Screen resumed from idle' for {MacAddress}", macAddress);
                        await LogScreenAction(macAddress, "Screen resumed from idle", 0);
                    }
                    if (previousStatus == "Online" && statusUpdate.Status == "Offline")
                    {
                        logger.LogInformation("[UpdatePlayerStatus] Logging 'Screen went offline' for {MacAddress}", macAddress);
                        await LogScreenAction(macAddress, "Screen went offline", 0);
                    }
                    if (previousStatus == "Offline" && statusUpdate.Status == "Idle")
                    {
                        logger.LogInformation("[UpdatePlayerStatus] Logging 'Screen switched to idle from offline' for {MacAddress}", macAddress);
                        await LogScreenAction(macAddress, "Screen switched to idle from offline", 0);
                    }
                    if (previousStatus == "Offline" && statusUpdate.Status == "Online")
                    {
                        logger.LogInformation("[UpdatePlayerStatus] Logging 'Screen came online' for {MacAddress}", macAddress);
                        await LogScreenAction(macAddress, "Screen came online", 0);
                    }
                }
                {
                    logger.LogInformation("[UpdatePlayerStatus] No status change detected for {MacAddress}. Skipping update.", macAddress);
                }

                return Ok(new
                {
                    Success = true,
                    ErrorMessage = string.Empty,
                    Item = "Status updated successfully."
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in UpdatePlayerStatus");
                return BadRequest(new
                {
                    Success = false,
                    ErrorMessage = "An error occurred while updating player status.",
                    Item = null as object
                });
            }
        }

        [HttpGet("health")]
        public async Task<IActionResult> HealthCheck()
        {
            try
            {
                bool dbCheck = await context.TblScreens.AnyAsync();

                string? rootPath = Path.GetPathRoot(Environment.CurrentDirectory) ?? throw new InvalidOperationException("Unable to determine the root path of the current directory.");
                DriveInfo driveInfo = new(rootPath);
                long availableSpace = driveInfo.AvailableFreeSpace;

                bool internetAccessible = false;
                try
                {
                    using HttpClient client = new();
                    HttpResponseMessage response = await client.GetAsync("https://www.google.com");
                    internetAccessible = response.IsSuccessStatusCode;
                }
                catch
                {
                    internetAccessible = false;
                }

                return Ok(new
                {
                    Success = true,
                    ErrorMessage = string.Empty,
                    Item = new
                    {
                        Status = "Healthy",
                        DatabaseAccessible = dbCheck,
                        AvailableDiskSpaceMB = availableSpace / (1024 * 1024),
                        InternetAccessible = internetAccessible,
                        Timestamp = DateTime.UtcNow
                    }
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[API] Health check failed");
                return StatusCode(500, new
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Item = new { Status = "Unhealthy", Timestamp = DateTime.UtcNow }
                });
            }
        }
    }

    public class TblScreenDto
    {
        public string MacAddress { get; set; } = string.Empty;
        public string Name { get; set; } = "No Name";
        public string Url { get; set; } = "No URL configured";
        public string Status { get; set; } = "Unknown";
        public bool AutoRestart { get; set; }
        public DateTime LastUpdated { get; set; }
        public DateTime? LastSeenOnline { get; set; }
        public int RefreshInterval { get; set; } = 60;
        public string ScreenResolution { get; set; } = "1920x1080";
        public string? Address { get; set; }
        public string? OperatingSystem { get; set; }
        public bool? StartupEnabled { get; set; }
    }

    public class StatusUpdate
    {
        public string? Status { get; set; }
        public DateTime? LastUpdated { get; set; }
        public DateTime? LastSeenOnline { get; set; }
    }
}