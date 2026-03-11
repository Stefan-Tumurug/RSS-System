using System;
using System.IO;
using System.Text;
using RssPlayer.Components.Configuration;
using RssPlayer.Components.Services;
using RssPlayer.Components.Utilities;

namespace RssPlayer.Components.Utilities
{
    public class HtmlRenderer
    {
        private static readonly PageGenerator _pageGenerator;
        private static readonly AppConfiguration _config = AppConfiguration.Instance;

        static HtmlRenderer()
        {
            _pageGenerator = new PageGenerator();
        }

        public static void SaveHtmlToFile(string htmlContent, string filePath)
        {
            try
            {
                ValidateAndCreateFilePath(filePath);
                File.WriteAllText(filePath, htmlContent, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error saving HTML file: {ex.Message}");
            }
        }
        private static void ValidateAndCreateFilePath(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    throw new ArgumentException("File path cannot be empty or whitespace.");
                }

                if (filePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                {
                    throw new ArgumentException($"Invalid file path characters in: {filePath}");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            }
            catch
            {
                throw;
            }
        }
        private static readonly object _pageGenerationLock = new object();

        public static string GenerateAndSaveRegistrationPage(string macAddress, string outputDirectory, string apiBaseUrl, LoggingService externalLogger = null)
        {
            lock (_pageGenerationLock)
            {
                try
                {
                    LoggingService logger = externalLogger;

                    logger ??= new LoggingService();

                    logger.Log($"🔍 Generating registration page for MAC: {macAddress}");
                    logger.Log($"📂 Output Directory: {outputDirectory}");

                    PageGenerator pageGenerator = new PageGenerator(logger);
                    string filePath = pageGenerator.GenerateRegistrationPage(macAddress, outputDirectory);

                    logger.Log($"✅ Registration page generated successfully: {filePath}");
                    return filePath;
                }
                catch (Exception ex)
                {
                    externalLogger?.LogError("Registration Page Generation Error", ex);

                    if (externalLogger == null)
                    {
                        Console.Error.WriteLine($"Registration Page Generation Error: {ex.Message}");
                    }

                    return null;
                }
            }
        }
        public static string GenerateAndSaveOfflineScreen(string macAddress, string outputDirectory, string apiBaseUrl, LoggingService externalLogger = null)
        {
            lock (_pageGenerationLock)
            {
                try
                {
                    LoggingService logger = externalLogger ?? new LoggingService();

                    logger.Log($"🔍 Generating offline screen for MAC: {macAddress}");
                    logger.Log($"📂 Output Directory: {outputDirectory}");

                    PageGenerator pageGenerator = new PageGenerator(logger);
                    string filePath = pageGenerator.GenerateOfflineScreen(macAddress);

                    logger.Log($"✅ Offline screen generated successfully: {filePath}");
                    return filePath;
                }
                catch (Exception ex)
                {
                    externalLogger?.LogError("Offline Screen Generation Error", ex);

                    if (externalLogger == null)
                    {
                        Console.Error.WriteLine($"Offline Screen Generation Error: {ex.Message}");
                    }

                    return null;
                }
            }
        }
    }
}