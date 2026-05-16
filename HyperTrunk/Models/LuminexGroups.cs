using System.Collections.Generic;

namespace HyperTrunk.Models
{
    public class LuminexGroup
    {
        public string Name { get; set; }      // Ex: "Group02"
        public int VlanId { get; set; }        // Ex: 200
        public string ColorHex { get; set; }   // Ex: "#C62828"

        // Texte affiché dans le ComboBox de la popup
        public string Display => $"{Name}  —  VLAN {VlanId}";
    }

    public static class LuminexGroups
    {
        public static readonly List<LuminexGroup> All = new List<LuminexGroup>
        {
            new LuminexGroup { Name = "Managment", VlanId = 1,  ColorHex = "#325197" },
            new LuminexGroup { Name = "Group02", VlanId = 200,  ColorHex = "#E80000" },
            new LuminexGroup { Name = "Group03", VlanId = 300,  ColorHex = "#32CD32" },
            new LuminexGroup { Name = "Group04", VlanId = 400,  ColorHex = "#00E8E8" },
            new LuminexGroup { Name = "Group05", VlanId = 500,  ColorHex = "#CC00CC" },
            new LuminexGroup { Name = "Group06", VlanId = 600,  ColorHex = "#FF8200" },
            new LuminexGroup { Name = "Group07", VlanId = 700,  ColorHex = "#E8E800" },
            new LuminexGroup { Name = "Group08", VlanId = 800,  ColorHex = "#FF0099" },
            new LuminexGroup { Name = "Group09", VlanId = 900,  ColorHex = "#20B2AA" },
            new LuminexGroup { Name = "Group10", VlanId = 1000, ColorHex = "#FA8072" },
            new LuminexGroup { Name = "Group11", VlanId = 1100, ColorHex = "#0033FF" },
            new LuminexGroup { Name = "Group12", VlanId = 1200, ColorHex = "#008000" },
            new LuminexGroup { Name = "Group13", VlanId = 1300, ColorHex = "#BB5555" },
            new LuminexGroup { Name = "Group14", VlanId = 1400, ColorHex = "#8B0000" },
            new LuminexGroup { Name = "Group15", VlanId = 1500, ColorHex = "#4B0082" },
            new LuminexGroup { Name = "Group16", VlanId = 1600, ColorHex = "#999900" },
            new LuminexGroup { Name = "Group17", VlanId = 1700, ColorHex = "#7CE800" },
            new LuminexGroup { Name = "Group18", VlanId = 1800, ColorHex = "#660066" },
            new LuminexGroup { Name = "Group19", VlanId = 1900, ColorHex = "#2F4F4F" },
            new LuminexGroup { Name = "Group20", VlanId = 2000, ColorHex = "#0066CC" },
        };
    }
}