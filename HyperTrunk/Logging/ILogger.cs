using System;

namespace HyperTrunk.Logging
{
    public interface ILogger
    {
        event EventHandler<LogEntry>? EntryLogged;

        void Log(LogLevel level, string message, bool isCommand = false);
    }
}
