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

        // removeExisting : tente d'abord de supprimer une IP existante sur l'adaptateur avant
        // d'assigner la nouvelle. Utile pour éditer un VLAN déjà configuré, inutile (et
        // trompeur dans les logs) pour un VLAN tout juste créé, qui n'a jamais d'IP dessus.
        Task ConfigureIpAsync(string vlanName, string ipAddress, string subnetMask, bool removeExisting = true, CancellationToken ct = default);
    }
}
