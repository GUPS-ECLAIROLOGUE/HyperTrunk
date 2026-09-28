using System.Linq;

namespace HyperTrunk.Services
{
    // Exclut les adaptateurs virtuels/systèmes de la liste des cartes réseau physiques
    // proposées à l'utilisateur (ex: cartes Hyper-V déjà virtuelles, VPN, Bluetooth...).
    // Logique purement locale (.NET), n'a rien à voir avec Hyper-V.
    public static class AdapterNameFilter
    {
        private static readonly string[] ExcludedSubstrings =
        {
            "virtual", "filter", "wfp", "miniport", "fortinet", "ssl",
            "bouclage", "tap", "loopback", "npcap", "qos", "packet",
            "bluetooth", "vethernet", "vswitch", "local", "usb", "host",
            "noyau", "hyper-v"
        };

        public static bool IsExcluded(string adapterName)
        {
            string name = adapterName.ToLowerInvariant();
            return ExcludedSubstrings.Any(name.Contains);
        }
    }
}
