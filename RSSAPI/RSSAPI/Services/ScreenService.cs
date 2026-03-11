using Microsoft.EntityFrameworkCore;
using RSSAPI.Data;
using RSSAPI.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RSSAPI.Services
{
    public class ScreenService(ScreenDbContext context)
    {
        private readonly ScreenDbContext context = context;

        public async Task<List<Screen>> GetScreensAsync()
        {
            try
            {
                return await context.TblScreens.ToListAsync();
            }
            catch (Exception ex)
            {
                Log.Error($"Error fetching screens: {ex.Message}");
                return [];
            }
        }

        public async Task<Screen?> GetScreenByMacAddressAsync(string macAddress)
        {
            try
            {
                return await context.TblScreens.FirstOrDefaultAsync(s => s.MacAddress == macAddress);
            }
            catch (Exception ex)
            {
                Log.Error($"Error fetching screen with MAC {macAddress}: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> UpdateScreenAsync(Screen screenData)
        {
            try
            {
                Screen? screen = await context.TblScreens.FirstOrDefaultAsync(s => s.MacAddress == screenData.MacAddress);
                if (screen == null) return false;

                screen.Url = screenData.Url ?? screen.Url;
                screen.Status = screenData.Status ?? screen.Status;
                screen.LastUpdated = DateTime.UtcNow;

                await context.SaveChangesAsync();
                Log.Information($"TblScreen {screenData.MacAddress} updated successfully.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Error updating screen {screenData.MacAddress}: {ex.Message}");
                return false;
            }
        }
    }
}