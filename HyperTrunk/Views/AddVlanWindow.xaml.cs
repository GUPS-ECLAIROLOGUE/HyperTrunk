using System.Windows;
using HyperTrunk.Models;
using HyperTrunk.Services;
using HyperTrunk.ViewModels;

namespace HyperTrunk.Views
{
    public partial class AddVlanWindow : Window
    {
        private readonly AddVlanViewModel _viewModel;

        public string VlanName => _viewModel.ResultVlanName;
        public int VlanId => _viewModel.ResultVlanId;
        public string VlanColorHex => _viewModel.ResultColorHex;
        public string IpAddress => _viewModel.ResultIpAddress;
        public string SubnetMask => _viewModel.ResultSubnetMask;

        public AddVlanWindow(ILuminexGroupsProvider groupsProvider, VlanItem? existing = null)
        {
            InitializeComponent();

            _viewModel = new AddVlanViewModel(groupsProvider, existing);
            _viewModel.RequestClose += (s, ok) =>
            {
                DialogResult = ok;
                Close();
            };

            DataContext = _viewModel;
        }
    }
}
