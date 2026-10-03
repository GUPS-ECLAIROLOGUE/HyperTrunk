namespace HyperTrunk.Models
{
    public class PhysicalAdapterInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool HasSwitch { get; set; }

        // Vrai si le switch lié à cette carte est un switch "HyperTrunk_*" (et pas un
        // switch External créé ailleurs, ex: pour des VM) - sert à griser "Create vSwitch",
        // HyperTrunk ne gérant qu'un seul vSwitch à la fois.
        public bool HasHyperTrunkSwitch { get; set; }

        public string Display => HasSwitch
            ? $"{Name}  |  {Description}  |  vSwitch active ✔"
            : $"{Name}  |  {Description}  |  no vSwitch ✘";
    }
}
