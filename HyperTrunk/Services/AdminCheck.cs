using System.Security.Principal;

namespace HyperTrunk.Services
{
    public static class AdminCheck
    {
        // Filet de sécurité : le manifeste applicatif (app.manifest) force normalement
        // déjà l'élévation via l'UAC au démarrage. Ce contrôle ne sert que si
        // l'application est lancée d'une façon qui contourne le manifeste
        // (ex: "dotnet HyperTrunk.dll" au lieu de HyperTrunk.exe).
        public static bool IsRunningAsAdministrator()
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
