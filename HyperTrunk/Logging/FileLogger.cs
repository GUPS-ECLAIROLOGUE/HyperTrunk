using System;
using System.IO;

namespace HyperTrunk.Logging
{
    // Journal minimal : écrit dans un fichier sous %LocalAppData%\HyperTrunk\logs
    // et relaie chaque entrée à qui veut l'afficher (le panneau console de l'UI).
    // Volontairement simple pour la taille de cette application (pas de Serilog/NLog).
    public class FileLogger : ILogger
    {
        private readonly object _fileLock = new();
        private readonly string _logFilePath = string.Empty;
        private readonly bool _fileLoggingEnabled;

        public event EventHandler<LogEntry>? EntryLogged;

        public FileLogger()
        {
            try
            {
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HyperTrunk", "logs");

                Directory.CreateDirectory(logDir);
                _logFilePath = Path.Combine(logDir, $"hypertrunk-{DateTime.Now:yyyyMMdd}.log");
                _fileLoggingEnabled = true;
            }
            catch
            {
                // Si on ne peut pas écrire sur le disque, l'application continue quand même :
                // les logs restent visibles dans le panneau console.
                _fileLoggingEnabled = false;
            }
        }

        public void Log(LogLevel level, string message, bool isCommand = false)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message,
                IsCommand = isCommand
            };

            WriteToFile(entry);
            EntryLogged?.Invoke(this, entry);
        }

        private void WriteToFile(LogEntry entry)
        {
            if (!_fileLoggingEnabled) return;

            string line = $"[{entry.Timestamp:HH:mm:ss}] [{entry.Level}] {entry.Message}";

            lock (_fileLock)
            {
                try
                {
                    File.AppendAllText(_logFilePath, line + Environment.NewLine);
                }
                catch
                {
                    // Une erreur d'écriture du journal ne doit jamais interrompre l'application.
                }
            }
        }
    }
}
