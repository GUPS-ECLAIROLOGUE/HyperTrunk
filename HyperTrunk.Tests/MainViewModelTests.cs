using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HyperTrunk.Models;
using HyperTrunk.Services;
using HyperTrunk.Tests.Fakes;
using HyperTrunk.ViewModels;

namespace HyperTrunk.Tests
{
    public class MainViewModelTests
    {
        private static MainViewModel CreateViewModel(FakeHyperVService? hyperV = null, FakeLogger? logger = null)
        {
            return new MainViewModel(
                hyperV ?? new FakeHyperVService(),
                new FakeLuminexGroupsProvider(),
                logger ?? new FakeLogger());
        }

        [Fact]
        public async Task LoadAdaptersAsync_PopulatesAdaptersFromService()
        {
            var hyperV = new FakeHyperVService
            {
                Adapters = new List<PhysicalAdapterInfo> { new() { Name = "Eth1", Description = "Carte 1" } }
            };
            var vm = CreateViewModel(hyperV);

            await vm.LoadAdaptersAsync();

            Assert.Single(vm.Adapters);
            Assert.Equal("Eth1", vm.Adapters[0].Name);
            Assert.False(vm.IsBusy);
        }

        [Fact]
        public async Task LoadVlansAsync_ResolvesColorFromLuminexGroups()
        {
            var hyperV = new FakeHyperVService
            {
                Vlans = new List<VlanItem> { new() { Name = "Scene", VlanId = 200 } }
            };
            var vm = CreateViewModel(hyperV);

            await vm.LoadVlansAsync();

            Assert.Single(vm.Vlans);
            Assert.Equal("#E80000", vm.Vlans[0].ColorHex); // Group02 = VLAN 200 dans la liste par défaut
        }

        [Fact]
        public async Task LoadVlansAsync_UnknownVlanId_UsesFallbackColor()
        {
            var hyperV = new FakeHyperVService
            {
                Vlans = new List<VlanItem> { new() { Name = "Inconnu", VlanId = 9999 } }
            };
            var vm = CreateViewModel(hyperV);

            await vm.LoadVlansAsync();

            Assert.Equal("#444444", vm.Vlans[0].ColorHex);
        }

        [Fact]
        public async Task CreateSwitchAsync_NoSelectedAdapter_DoesNotCallService()
        {
            var hyperV = new FakeHyperVService();
            var vm = CreateViewModel(hyperV);

            await vm.CreateSwitchAsync();

            Assert.Equal(0, hyperV.CreateSwitchCallCount);
        }

        [Fact]
        public async Task CreateSwitchAsync_Success_CallsServiceAndReloadsAdapters()
        {
            var hyperV = new FakeHyperVService
            {
                Adapters = new List<PhysicalAdapterInfo> { new() { Name = "Eth1", HasSwitch = true } }
            };
            var vm = CreateViewModel(hyperV);
            vm.SelectedAdapter = new PhysicalAdapterInfo { Name = "Eth1" };

            await vm.CreateSwitchAsync();

            Assert.Equal(1, hyperV.CreateSwitchCallCount);
            Assert.Single(vm.Adapters);
            Assert.False(vm.IsBusy);
        }

        [Fact]
        public async Task CreateSwitchAsync_ServiceThrows_LogsErrorAndRaisesShowError_WithoutThrowing()
        {
            var hyperV = new FakeHyperVService
            {
                ExceptionToThrow = new HyperVOperationException("Adaptateur déjà utilisé.")
            };
            var logger = new FakeLogger();
            var vm = CreateViewModel(hyperV, logger);
            vm.SelectedAdapter = new PhysicalAdapterInfo { Name = "Eth1" };

            string? shownError = null;
            vm.RequestShowError += (s, msg) => shownError = msg;

            var exception = await Record.ExceptionAsync(() => vm.CreateSwitchAsync());

            Assert.Null(exception);
            Assert.NotNull(shownError);
            Assert.Contains(logger.Entries, e => e.Level == HyperTrunk.Logging.LogLevel.Error);
            Assert.False(vm.IsBusy);
        }

        [Fact]
        public async Task CreateSwitchCommand_HyperTrunkSwitchExistsOnAnotherAdapter_IsDisabled()
        {
            var hyperV = new FakeHyperVService
            {
                Adapters = new List<PhysicalAdapterInfo>
                {
                    new() { Name = "Eth1", HasSwitch = true, HasHyperTrunkSwitch = true },
                    new() { Name = "Eth2" }
                }
            };
            var vm = CreateViewModel(hyperV);
            await vm.LoadAdaptersAsync();
            vm.SelectedAdapter = vm.Adapters.Single(a => a.Name == "Eth2");

            Assert.False(vm.CreateSwitchCommand.CanExecute(null));
        }

        [Fact]
        public async Task CreateSwitchCommand_OnlyNonHyperTrunkSwitchExists_IsEnabled()
        {
            var hyperV = new FakeHyperVService
            {
                Adapters = new List<PhysicalAdapterInfo>
                {
                    new() { Name = "Eth1", HasSwitch = true }, // switch External créé hors HyperTrunk
                    new() { Name = "Eth2" }
                }
            };
            var vm = CreateViewModel(hyperV);
            await vm.LoadAdaptersAsync();
            vm.SelectedAdapter = vm.Adapters.Single(a => a.Name == "Eth2");

            Assert.True(vm.CreateSwitchCommand.CanExecute(null));
        }

        [Fact]
        public async Task DeleteVlanAsync_Success_CallsServiceAndReloads()
        {
            var hyperV = new FakeHyperVService
            {
                Vlans = new List<VlanItem> { new() { Name = "Scene", VlanId = 200 } }
            };
            var vm = CreateViewModel(hyperV);
            await vm.LoadVlansAsync();
            vm.SelectedVlan = vm.Vlans.Single();

            hyperV.Vlans.Clear(); // simule la suppression côté service

            await vm.DeleteVlanAsync();

            Assert.Equal(1, hyperV.DeleteVlanCallCount);
            Assert.Empty(vm.Vlans);
        }
    }
}
