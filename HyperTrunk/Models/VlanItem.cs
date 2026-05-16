namespace HyperTrunk.Models
{
    public class VlanItem
    {
        // Nom de l'adaptateur virtuel (ex: "VLAN_Scène")
        public string Name { get; set; }

        // L'ID VLAN Hyper-V (ex: 200)
        public int VlanId { get; set; }

        // Couleur hex Luminex (ex: "#C62828")
        public string ColorHex { get; set; }

        // Adresse IP (ex: "192.168.2.1") — vide si pas configurée
        public string IpAddress { get; set; }

        // Masque de sous-réseau (ex: "255.255.255.0") — vide si pas configuré
        public string SubnetMask { get; set; }

        // Texte affiché dans la liste de la fenêtre principale
        public string Display
        {
            get
            {
                string ip = string.IsNullOrWhiteSpace(IpAddress)
                    ? "No IP"
                    : $"{IpAddress} / {SubnetMask}";

                return $"{Name}  |  VLAN ID {VlanId}  |  {ip}";
            }
        }
    }
}