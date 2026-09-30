namespace HyperTrunk.Models
{
    public class PhysicalAdapterInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool HasSwitch { get; set; }

        public string Display => HasSwitch
            ? $"{Name}  |  {Description}  |  vSwitch active ✔"
            : $"{Name}  |  {Description}  |  no vSwitch ✘";
    }
}
