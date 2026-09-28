using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HyperTrunk.Models;

namespace HyperTrunk.Services
{
    public interface IHyperVService
    {
        // Prépare la session PowerShell interne et vérifie que les modules
        // nécessaires (Hyper-V, réseau) sont disponibles. À appeler une fois
        // au démarrage, pas dans un constructeur.
        Task InitializeAsync(CancellationToken ct = default);

        Task<bool> IsHyperVEnabledAsync(CancellationToken ct = default);

        Task<IReadOnlyList<PhysicalAdapterInfo>> GetPhysicalAdaptersAsync(CancellationToken ct = default);

        Task CreateSwitchAsync(string adapterName, CancellationToken ct = default);

        Task DeleteSwitchAsync(string adapterName, CancellationToken ct = default);

        Task<IReadOnlyList<VlanItem>> GetVlansAsync(CancellationToken ct = default);

        Task CreateVlanAsync(string switchName, string vlanName, int vlanId, CancellationToken ct = default);

        Task DeleteVlanAsync(string vlanName, CancellationToken ct = default);

        Task ConfigureIpAsync(string vlanName, string ipAddress, string subnetMask, CancellationToken ct = default);
    }
}
