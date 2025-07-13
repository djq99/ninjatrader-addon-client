using System;

namespace NT8Bridge.Util
{
    public class ConsoleLogger : ILogger
    {
        private readonly object _lockObject = new object();

        public void LogTrace(string message)
        {
            WriteLog("TRACE", message);
        }

        public void LogDebug(string message)
        {
            WriteLog("DEBUG", message);
        }

        public void LogInformation(string message)
        {
            WriteLog("INFO", message);
        }

        public void LogWarning(string message)
        {
            WriteLog("WARN", message);
        }

        public void LogError(string message)
        {
            WriteLog("ERROR", message);
        }

        public void LogError(string message, Exception exception)
        {
            WriteLog("ERROR", $"{message} - Exception: {exception.Message}");
        }

        public void LogCritical(string message)
        {
            WriteLog("CRITICAL", message);
        }

        public void LogCritical(string message, Exception exception)
        {
            WriteLog("CRITICAL", $"{message} - Exception: {exception.Message}");
        }

        private void WriteLog(string level, string message)
        {
            lock (_lockObject)
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var logMessage = $"[{timestamp}] [{level}] NT8Bridge: {message}";
                
                // Color coding for different log levels
                var originalColor = Console.ForegroundColor;
                Console.ForegroundColor = level switch
                {
                    "ERROR" => ConsoleColor.Red,
                    "WARN" => ConsoleColor.Yellow,
                    "CRITICAL" => ConsoleColor.Magenta,
                    "DEBUG" => ConsoleColor.Gray,
                    _ => ConsoleColor.White
                };

                Console.WriteLine(logMessage);
                Console.ForegroundColor = originalColor;
            }
        }
    }
} 