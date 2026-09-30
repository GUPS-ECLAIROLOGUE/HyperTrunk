using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using HyperTrunk.Logging;
using HyperTrunk.Models;

namespace HyperTrunk.Services
{
    public interface ILuminexGroupsProvider
    {
        IReadOnlyList<LuminexGroup> GetGroups();
    }

    // Charge la liste des groupes Luminex depuis un fichier "luminex-groups.json"
    // à côté de l'exécutable, s'il existe. Sinon, utilise la liste intégrée par défaut
    // (LuminexGroups.All) - donc le logiciel fonctionne sans aucune configuration,
    // mais reste personnalisable sans recompiler.
    public class LuminexGroupsProvider : ILuminexGroupsProvider
    {
        private const string ConfigFileName = "luminex-groups.json";

        private readonly ILogger _logger;
        private IReadOnlyList<LuminexGroup>? _cached;

        public LuminexGroupsProvider(ILogger logger)
        {
            _logger = logger;
        }

        public IReadOnlyList<LuminexGroup> GetGroups()
        {
            if (_cached is not null) return _cached;

            string path = Path.Combine(AppContext.BaseDirectory, ConfigFileName);

            if (!File.Exists(path))
            {
                TryWriteDefaults(path);
                _cached = LuminexGroups.All;
                return _cached;
            }

            try
            {
                string json = File.ReadAllText(path);
                List<LuminexGroup>? groups = JsonSerializer.Deserialize<List<LuminexGroup>>(json);

                if (groups is null || groups.Count == 0)
                {
                    _logger.Log(LogLevel.Warn, $"{ConfigFileName} is empty or invalid, using default Luminex groups.");
                    _cached = LuminexGroups.All;
                }
                else
                {
                    _cached = groups;
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Warn, $"Unable to read {ConfigFileName} ({ex.Message}), using default Luminex groups.");
                _cached = LuminexGroups.All;
            }

            return _cached;
        }

        private static void TryWriteDefaults(string path)
        {
            try
            {
                string json = JsonSerializer.Serialize(LuminexGroups.All, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch
            {
                // Si on ne peut pas écrire le fichier de démarrage, les valeurs par défaut
                // en mémoire restent utilisées - ce n'est pas bloquant.
            }
        }
    }
}
