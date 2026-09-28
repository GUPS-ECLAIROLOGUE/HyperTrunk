using HyperTrunk.Models;

namespace HyperTrunk.ViewModels
{
    // Habille un VlanItem (fait Hyper-V pur) avec la couleur Luminex résolue
    // et le texte d'affichage, pour le binding dans la fenêtre principale.
    public class VlanItemViewModel : ViewModelBase
    {
        private string _ipAddress;
        private string _subnetMask;

        public string Name { get; }
        public int VlanId { get; }
        public string ColorHex { get; }

        public string IpAddress
        {
            get => _ipAddress;
            set
            {
                if (SetProperty(ref _ipAddress, value))
                    OnPropertyChanged(nameof(Display));
            }
        }

        public string SubnetMask
        {
            get => _subnetMask;
            set
            {
                if (SetProperty(ref _subnetMask, value))
                    OnPropertyChanged(nameof(Display));
            }
        }

        public string Display => string.IsNullOrWhiteSpace(IpAddress)
            ? $"{Name}  |  VLAN ID {VlanId}  |  No IP"
            : $"{Name}  |  VLAN ID {VlanId}  |  {IpAddress} / {SubnetMask}";

        public VlanItemViewModel(VlanItem item, string colorHex)
        {
            Name = item.Name;
            VlanId = item.VlanId;
            ColorHex = colorHex;
            _ipAddress = item.IpAddress;
            _subnetMask = item.SubnetMask;
        }
    }
}
