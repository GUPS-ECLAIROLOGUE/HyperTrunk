using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using HyperTrunk.Models;
using HyperTrunk.Services;

namespace HyperTrunk.ViewModels
{
    public class AddVlanViewModel : ViewModelBase
    {
        private string _vlanName = string.Empty;
        private LuminexGroup? _selectedGroup;
        private string _ipAddress = string.Empty;
        private SubnetMaskOption? _selectedMask;
        private string? _errorMessage;

        public ObservableCollection<LuminexGroup> Groups { get; }

        public ObservableCollection<SubnetMaskOption> MaskOptions { get; } = new()
        {
            new SubnetMaskOption("255.255.255.0 (/24)", "255.255.255.0"),
            new SubnetMaskOption("255.255.0.0 (/16)", "255.255.0.0"),
            new SubnetMaskOption("255.0.0.0 (/8)", "255.0.0.0"),
            new SubnetMaskOption("255.255.255.128 (/25)", "255.255.255.128"),
            new SubnetMaskOption("255.255.255.192 (/26)", "255.255.255.192"),
        };

        public bool IsNameReadOnly { get; }
        public bool IsGroupSelectionEnabled { get; }

        public string VlanName
        {
            get => _vlanName;
            set => SetProperty(ref _vlanName, value);
        }

        public LuminexGroup? SelectedGroup
        {
            get => _selectedGroup;
            set
            {
                if (SetProperty(ref _selectedGroup, value))
                    OnPropertyChanged(nameof(VlanIdText));
            }
        }

        public string VlanIdText => SelectedGroup is null ? string.Empty : SelectedGroup.VlanId.ToString();

        public string IpAddress
        {
            get => _ipAddress;
            set => SetProperty(ref _ipAddress, value);
        }

        public SubnetMaskOption? SelectedMask
        {
            get => _selectedMask;
            set => SetProperty(ref _selectedMask, value);
        }

        public string? ErrorMessage
        {
            get => _errorMessage;
            private set => SetProperty(ref _errorMessage, value);
        }

        public ICommand CreateCommand { get; }
        public ICommand CancelCommand { get; }

        // Résultat exposé une fois la validation réussie (lu par la fenêtre appelante).
        public string ResultVlanName { get; private set; } = string.Empty;
        public int ResultVlanId { get; private set; }
        public string ResultColorHex { get; private set; } = string.Empty;
        public string ResultIpAddress { get; private set; } = string.Empty;
        public string ResultSubnetMask { get; private set; } = string.Empty;

        public event EventHandler<bool>? RequestClose;

        public AddVlanViewModel(ILuminexGroupsProvider groupsProvider, VlanItem? existing = null)
        {
            Groups = new ObservableCollection<LuminexGroup>(groupsProvider.GetGroups());
            CreateCommand = new RelayCommand(Create);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(this, false));

            IsNameReadOnly = existing is not null;
            IsGroupSelectionEnabled = existing is null;

            if (existing is not null)
            {
                VlanName = existing.Name;
                IpAddress = existing.IpAddress;
                SelectedGroup = Groups.FirstOrDefault(g => g.VlanId == existing.VlanId);
                SelectedMask = MaskOptions.FirstOrDefault(m => m.Mask == existing.SubnetMask);
            }
        }

        private void Create()
        {
            ErrorMessage = null;

            if (string.IsNullOrWhiteSpace(VlanName))
            {
                ErrorMessage = "Le nom du VLAN est obligatoire.";
                return;
            }

            if (SelectedGroup is null)
            {
                ErrorMessage = "Sélectionne un groupe Luminex.";
                return;
            }

            if (!string.IsNullOrWhiteSpace(IpAddress) && SelectedMask is null)
            {
                ErrorMessage = "Choisis un masque.";
                return;
            }

            ResultVlanName = VlanName.Trim();
            ResultVlanId = SelectedGroup.VlanId;
            ResultColorHex = SelectedGroup.ColorHex;
            ResultIpAddress = IpAddress.Trim();
            ResultSubnetMask = SelectedMask?.Mask ?? string.Empty;

            RequestClose?.Invoke(this, true);
        }
    }
}
