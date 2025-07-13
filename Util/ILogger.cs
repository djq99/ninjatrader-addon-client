using System;

namespace NT8Bridge.Util
{
    public interface ILogger
    {
        void LogTrace(string message);
        void LogDebug(string message);
        void LogInformation(string message);
        void LogWarning(string message);
        void LogError(string message);
        void LogError(string message, Exception exception);
        void LogCritical(string message);
        void LogCritical(string message, Exception exception);
    }
} 