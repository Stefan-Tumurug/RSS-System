using System;
using System.Diagnostics;
using System.IO;
using IWshRuntimeLibrary;
using RssPlayer.Components.Services;
using File = System.IO.File;

namespace RssPlayer.Components.Utilities
{
    public static class StartupHandler
    {
        private static readonly string AppName = "RemoteScreenPlayer";
        private static readonly string ShortcutPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            $"{AppName}.lnk");

        public static void ApplyStartupSetting(bool enable, LoggingService logger = null)
        {
            if (enable)
            {
                try
                {
                    CreateStartupShortcut(logger);
                }
                catch (Exception ex)
                {
                    logger?.LogError("[STARTUP HANDLER] Failed to create startup shortcut", ex);
                }
            }

            if (!enable)
            {
                try
                {
                    RemoveStartupShortcut(logger);
                }
                catch (Exception ex)
                {
                    logger?.LogError("[STARTUP HANDLER] Failed to remove startup shortcut", ex);
                }
            }
        }


        private static void CreateStartupShortcut(LoggingService logger = null)
        {
            try
            {
                string startMenuShortcut = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                    "Diviso\\Remote Screen Setup\\Remote Screen Player.appref-ms"
                );

                if (!File.Exists(startMenuShortcut))
                {
                    logger?.LogError("[STARTUP HANDLER] .appref-ms file not found at: " + startMenuShortcut);
                    return;
                }

                WshShell shell = new WshShell();
                IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(ShortcutPath);
                shortcut.TargetPath = startMenuShortcut;
                shortcut.WorkingDirectory = Path.GetDirectoryName(startMenuShortcut);
                shortcut.Save();

            }
            catch (Exception ex)
            {
                logger?.LogError("[STARTUP HANDLER] Failed to create startup shortcut", ex);
            }
        }


        private static void RemoveStartupShortcut(LoggingService logger = null)
        {
            try
            {
                if (File.Exists(ShortcutPath))
                {
                    File.Delete(ShortcutPath);
                    logger?.Log("[STARTUP HANDLER] Startup shortcut removed from " + ShortcutPath);
                }
            }
            catch (Exception ex)
            {
                logger?.LogError("[STARTUP HANDLER] Failed to remove startup shortcut", ex);
            }
        }
    }
}
