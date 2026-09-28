using System.Collections.Generic;
using HyperTrunk.Models;
using HyperTrunk.Tests.Fakes;
using HyperTrunk.ViewModels;

namespace HyperTrunk.Tests
{
    public class AddVlanViewModelTests
    {
        private static AddVlanViewModel CreateViewModel(VlanItem? existing = null)
        {
            var groups = new List<LuminexGroup>
            {
                new() { Name = "Group02", VlanId = 200, ColorHex = "#E80000" }
            };
            return new AddVlanViewModel(new FakeLuminexGroupsProvider(groups), existing);
        }

        [Fact]
        public void Create_MissingName_SetsErrorAndDoesNotClose()
        {
            var vm = CreateViewModel();
            bool closeRaised = false;
            vm.RequestClose += (s, ok) => closeRaised = true;
            vm.SelectedGroup = vm.Groups[0];

            vm.CreateCommand.Execute(null);

            Assert.False(closeRaised);
            Assert.Equal("Le nom du VLAN est obligatoire.", vm.ErrorMessage);
        }

        [Fact]
        public void Create_MissingGroup_SetsError()
        {
            var vm = CreateViewModel();
            vm.VlanName = "Scene";

            vm.CreateCommand.Execute(null);

            Assert.Equal("Sélectionne un groupe Luminex.", vm.ErrorMessage);
        }

        [Fact]
        public void Create_IpWithoutMask_SetsError()
        {
            var vm = CreateViewModel();
            vm.VlanName = "Scene";
            vm.SelectedGroup = vm.Groups[0];
            vm.IpAddress = "192.168.1.1";

            vm.CreateCommand.Execute(null);

            Assert.Equal("Si tu entres une IP, tu dois aussi choisir un masque.", vm.ErrorMessage);
        }

        [Fact]
        public void Create_ValidInput_RaisesRequestCloseTrueAndFillsResult()
        {
            var vm = CreateViewModel();
            vm.VlanName = " Scene ";
            vm.SelectedGroup = vm.Groups[0];

            bool? closedWith = null;
            vm.RequestClose += (s, ok) => closedWith = ok;

            vm.CreateCommand.Execute(null);

            Assert.True(closedWith);
            Assert.Equal("Scene", vm.ResultVlanName);
            Assert.Equal(200, vm.ResultVlanId);
            Assert.Equal("#E80000", vm.ResultColorHex);
        }

        [Fact]
        public void ExistingVlan_NameIsReadOnlyAndGroupPreselected()
        {
            var existing = new VlanItem { Name = "Scene", VlanId = 200, IpAddress = "10.0.0.1", SubnetMask = "255.255.255.0" };
            var vm = CreateViewModel(existing);

            Assert.True(vm.IsNameReadOnly);
            Assert.False(vm.IsGroupSelectionEnabled);
            Assert.NotNull(vm.SelectedGroup);
            Assert.Equal(200, vm.SelectedGroup!.VlanId);
            Assert.NotNull(vm.SelectedMask);
            Assert.Equal("255.255.255.0", vm.SelectedMask!.Mask);
        }
    }
}
