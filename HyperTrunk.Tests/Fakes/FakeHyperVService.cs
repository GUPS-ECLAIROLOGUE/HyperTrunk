using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HyperTrunk.Models;
using HyperTrunk.Services;

namespace HyperTrunk.Tests.Fakes
{
    // Représente Hyper-V en mémoire, sans aucun appel réel - permet de tester
    // la logique des ViewModels sans machine Hyper-V/matériel Luminex.
    public class FakeHyperVService : IHyperVService
    {
        public List<PhysicalAdapterInfo> Adapters { get; set; } = new();
        public List<VlanItem> Vlans { get; set; } = new();
        public bool HyperVEnabled { get; set; } = true;
        public Exception? ExceptionToThrow { get; set; }

        public int CreateSwitchCallCount { get; private set; }
        public int DeleteSwitchCallCount { get; private set; }
        public int CreateVlanCallCount { get; private set; }
        public int DeleteVlanCallCount { get; private set; }
        public int ConfigureIpCallCount { get; private set; }

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> IsHyperVEnabledAsync(CancellationToken ct = default) => Task.FromResult(HyperVEnabled);

        public Task<IReadOnlyList<PhysicalAdapterInfo>> GetPhysicalAdaptersAsync(CancellationToken ct = default)
        {
            ThrowIfConfigured();
            return Task.FromResult((IReadOnlyList<PhysicalAdapterInfo>)Adapters);
        }

        public Task CreateSwitchAsync(string adapterName, CancellationToken ct = default)
        {
            CreateSwitchCallCount++;
            ThrowIfConfigured();
            return Task.CompletedTask;
        }

        public Task DeleteSwitchAsync(string adapterName, CancellationToken ct = default)
        {
            DeleteSwitchCallCount++;
            ThrowIfConfigured();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<VlanItem>> GetVlansAsync(CancellationToken ct = default)
        {
            ThrowIfConfigured();
            return Task.FromResult((IReadOnlyList<VlanItem>)Vlans);
        }

        public Task CreateVlanAsync(string switchName, string vlanName, int vlanId, CancellationToken ct = default)
        {
            CreateVlanCallCount++;
            ThrowIfConfigured();
            Vlans.Add(new VlanItem { Name = vlanName, VlanId = vlanId });
            return Task.CompletedTask;
        }

        public Task DeleteVlanAsync(string vlanName, CancellationToken ct = default)
        {
            DeleteVlanCallCount++;
            ThrowIfConfigured();
            Vlans.RemoveAll(v => v.Name == vlanName);
            return Task.CompletedTask;
        }

        public Task ConfigureIpAsync(string vlanName, string ipAddress, string subnetMask, CancellationToken ct = default)
        {
            ConfigureIpCallCount++;
            ThrowIfConfigured();
            return Task.CompletedTask;
        }

        private void ThrowIfConfigured()
        {
            if (ExceptionToThrow is not null) throw ExceptionToThrow;
        }
    }
}
