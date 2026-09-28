using System;
using System.Collections.Generic;
using HyperTrunk.Logging;

namespace HyperTrunk.Tests.Fakes
{
    public class FakeLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = new();

        public event EventHandler<LogEntry>? EntryLogged;

        public void Log(LogLevel level, string message, bool isCommand = false)
        {
            var entry = new LogEntry { Timestamp = DateTime.Now, Level = level, Message = message, IsCommand = isCommand };
            Entries.Add(entry);
            EntryLogged?.Invoke(this, entry);
        }
    }
}
