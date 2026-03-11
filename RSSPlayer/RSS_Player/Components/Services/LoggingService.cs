using System;
using System.IO;
using RssPlayer.Components.Configuration;

namespace RssPlayer.Components.Services
{
    public class LoggingService
    {
        private readonly string _logFilePath;
        private readonly bool _consoleOutput;

        public LoggingService(bool consoleOutput = true)
        {
            try
            {
                _logFilePath = AppConfiguration.Instance.LogFilePath;
                _consoleOutput = consoleOutput;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing LoggingService: {ex.Message}");
                _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "application.log");
                _consoleOutput = true;
            }
        }

        public void Log(string message)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string logMessage = $"[{timestamp}] {message}";

                OutputLog(logMessage);
                WriteToFile(logMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Logging failed: {ex.Message}");
            }
        }

        private void OutputLog(string logMessage)
        {
            try
            {
                if (_consoleOutput)
                {
                    Console.WriteLine(logMessage);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Console output failed: {ex.Message}");
            }
        }

        private void WriteToFile(string logMessage)
        {
            try
            {
                EnsureLogDirectoryExists();
                File.AppendAllText(_logFilePath, logMessage + Environment.NewLine);
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"Unauthorized access when writing log: {ex.Message}");
            }
            catch (PathTooLongException ex)
            {
                Console.WriteLine($"Log file path too long: {ex.Message}");
            }
            catch (DirectoryNotFoundException ex)
            {
                Console.WriteLine($"Log directory not found: {ex.Message}");
            }
            catch (IOException ex)
            {
                Console.WriteLine($"IO error when writing log: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error writing log: {ex.Message}");
            }
        }

        private void EnsureLogDirectoryExists()
        {
            try
            {
                string directoryPath = Path.GetDirectoryName(_logFilePath);
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating log directory: {ex.Message}");
            }
        }

        public void LogError(string message, Exception ex)
        {
            try
            {
                Log($"ERROR: {message} - {ex.Message}");
                Log($"Stack Trace: {ex.StackTrace}");

                if (ex.InnerException != null)
                {
                    Log($"Inner Exception: {ex.InnerException.Message}");
                }
            }
            catch (Exception logEx)
            {
                Console.WriteLine($"Failed to log error: {logEx.Message}");
            }
        }

        public void LogWarning(string message)
        {
            Log($"WARNING: {message}");
        }

        public void LogSuccess(string message)
        {
            Log($"SUCCESS: {message}");
        }

        public void LogError(string message)
        {
            Log($"ERROR: {message}");
        }
    }
}