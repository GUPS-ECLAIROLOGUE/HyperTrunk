using System;
using System.Collections.Generic;

namespace HyperTrunk.Services
{
    // Remplace le comportement précédent où les erreurs PowerShell étaient
    // silencieusement perdues (flux d'erreur jamais lu). Toute commande Hyper-V
    // qui échoue lève désormais cette exception avec le détail exact des erreurs.
    public class HyperVOperationException : Exception
    {
        public IReadOnlyList<string> PowerShellErrors { get; }
        public bool IsTimeout { get; }

        public HyperVOperationException(
            string message,
            IReadOnlyList<string>? powerShellErrors = null,
            bool isTimeout = false,
            Exception? innerException = null)
            : base(message, innerException)
        {
            PowerShellErrors = powerShellErrors ?? Array.Empty<string>();
            IsTimeout = isTimeout;
        }
    }
}
