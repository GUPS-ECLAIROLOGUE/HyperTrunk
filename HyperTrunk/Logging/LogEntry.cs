using System;

namespace HyperTrunk.Logging
{
    public class LogEntry
    {
        public DateTime Timestamp { get; init; }
        public LogLevel Level { get; init; }
        public string Message { get; init; } = string.Empty;

        // Distingue les lignes "commande envoyée à Hyper-V" du reste,
        // pour la coloration dans le panneau console (remplace l'ancien
        // test texte fragile "texte.StartsWith(\"SND >\")").
        public bool IsCommand { get; init; }
    }
}
