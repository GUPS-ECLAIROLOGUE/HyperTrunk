namespace HyperTrunk.Models
{
    // DTO représentant un VLAN tel qu'il existe réellement dans Hyper-V.
    // La couleur d'affichage (issue des groupes Luminex) est résolue côté ViewModel,
    // pas ici : cette classe ne décrit que des faits Hyper-V.
    public class VlanItem
    {
        public string Name { get; set; } = string.Empty;
        public int VlanId { get; set; }
        public string IpAddress { get; set; } = string.Empty;
        public string SubnetMask { get; set; } = string.Empty;
    }
}