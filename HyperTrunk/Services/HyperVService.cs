using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using HyperTrunk.Logging;
using HyperTrunk.Models;
using ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy;
using LogLevel = HyperTrunk.Logging.LogLevel;

namespace HyperTrunk.Services
{
    // Remplace l'ancien PowerShellService : au lieu d'ouvrir un powershell.exe séparé
    // et de lui envoyer du texte, on héberge PowerShell directement dans le processus
    // de l'application et on appelle les cmdlets Hyper-V avec de vrais paramètres
    // (AddParameter), pas du texte interpolé dans un script. Un seul "Runspace" est
    // réutilisé pour toute la session, protégé par un sémaphore pour qu'une seule
    // commande s'exécute à la fois.
    public class HyperVService : IHyperVService, IDisposable
    {
        private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(45);

        private readonly ILogger _logger;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private Runspace? _runspace;

        public HyperVService(ILogger logger)
        {
            _logger = logger;
        }

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            try
            {
                await ImportModuleAsync("Hyper-V", ct).ConfigureAwait(false);
                await ImportModuleAsync("NetAdapter", ct).ConfigureAwait(false);
                await ImportModuleAsync("NetTCPIP", ct).ConfigureAwait(false);
            }
            catch (HyperVOperationException ex)
            {
                throw new HyperVOperationException(
                    "Impossible de charger les modules PowerShell Hyper-V/réseau. " +
                    "Vérifie que la fonctionnalité Hyper-V est bien installée sur cette machine.",
                    ex.PowerShellErrors, ex.IsTimeout, ex);
            }
        }

        private Task ImportModuleAsync(string moduleName, CancellationToken ct) =>
            ExecuteAsync(ps => ps.AddCommand("Import-Module")
                    .AddParameter("Name", moduleName)
                    .AddParameter("ErrorAction", "Stop"),
                WriteTimeout, ct);

        public async Task<bool> IsHyperVEnabledAsync(CancellationToken ct = default)
        {
            var results = await ExecuteAsync(ps => ps.AddCommand("Get-WindowsOptionalFeature")
                    .AddParameter("FeatureName", "Microsoft-Hyper-V-All")
                    .AddParameter("Online", true),
                ReadTimeout, ct).ConfigureAwait(false);

            string? state = results.FirstOrDefault()?.Properties["State"]?.Value?.ToString();
            return string.Equals(state, "Enabled", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<IReadOnlyList<PhysicalAdapterInfo>> GetPhysicalAdaptersAsync(CancellationToken ct = default)
        {
            var pnpResults = await ExecuteAsync(
                ps => ps.AddCommand("Get-PnpDevice").AddParameter("Class", "Net"),
                ReadTimeout, ct).ConfigureAwait(false);

            // Remarque : "FriendlyName" est un alias ajouté par Get-PnpDevice uniquement
            // quand le module est chargé sous Windows PowerShell 5.1 "classique". Le moteur
            // PowerShell hébergé par l'application (édition Core) ne l'expose pas toujours ;
            // "Name" est la propriété CIM brute, elle est donc plus fiable ici et contient
            // la même information (le nom convivial de l'appareil).
            var presentDescriptions = pnpResults
                .Where(o => string.Equals(o.Properties["Status"]?.Value?.ToString(), "OK", StringComparison.OrdinalIgnoreCase))
                .Select(o => o.Properties["Name"]?.Value?.ToString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .ToList();

            LogList("Adaptateurs PnP présents (Status=OK) :", presentDescriptions);

            var switchResults = await ExecuteAsync(
                ps => ps.AddCommand("Get-VMSwitch"),
                ReadTimeout, ct).ConfigureAwait(false);

            var adaptersWithSwitch = switchResults
                .Select(o => o.Properties["NetAdapterInterfaceDescription"]?.Value?.ToString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .ToHashSet();

            var result = new List<PhysicalAdapterInfo>();

            foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Ethernet)
                    continue;

                if (!presentDescriptions.Any(p => p.Contains(nic.Description)))
                    continue;

                if (AdapterNameFilter.IsExcluded(nic.Name))
                    continue;

                result.Add(new PhysicalAdapterInfo
                {
                    Name = nic.Name,
                    Description = nic.Description,
                    HasSwitch = adaptersWithSwitch.Contains(nic.Description)
                });
            }

            LogList($"{result.Count} carte(s) réseau retenue(s) :", result.Select(a => $"{a.Name} — {a.Description}"));

            return result;
        }

        // Affiche chaque élément sur sa propre ligne de journal, plutôt qu'une seule
        // ligne à rallonge - plus lisible dans la console et le fichier de log.
        private void LogList(string header, IEnumerable<string> items)
        {
            _logger.Log(LogLevel.Debug, header);
            foreach (string item in items)
                _logger.Log(LogLevel.Debug, "   - " + item);
        }

        public Task CreateSwitchAsync(string adapterName, CancellationToken ct = default)
        {
            string switchName = VSwitchNaming.ForAdapter(adapterName);
            return ExecuteAsync(ps => ps.AddCommand("New-VMSwitch")
                    .AddParameter("Name", switchName)
                    .AddParameter("NetAdapterName", adapterName)
                    .AddParameter("AllowManagementOS", true),
                WriteTimeout, ct);
        }

        public Task DeleteSwitchAsync(string adapterName, CancellationToken ct = default)
        {
            string switchName = VSwitchNaming.ForAdapter(adapterName);
            return ExecuteAsync(ps => ps.AddCommand("Remove-VMSwitch")
                    .AddParameter("Name", switchName)
                    .AddParameter("Force", true),
                WriteTimeout, ct);
        }

        public async Task<IReadOnlyList<VlanItem>> GetVlansAsync(CancellationToken ct = default)
        {
            // Deux allers-retours au total, quel que soit le nombre de VLANs configurés
            // (avant : 1 + 2×N allers-retours, un par VLAN, pour récupérer IP/masque).
            var vlanResults = await ExecuteAsync(
                ps => ps.AddCommand("Get-VMNetworkAdapterVlan").AddParameter("ManagementOS", true),
                ReadTimeout, ct).ConfigureAwait(false);

            var ipResults = await ExecuteAsync(
                ps => ps.AddCommand("Get-NetIPAddress")
                    .AddParameter("AddressFamily", AddressFamily.InterNetwork)
                    .AddParameter("ErrorAction", "SilentlyContinue"),
                ReadTimeout, ct).ConfigureAwait(false);

            var manualIpByAlias = ipResults
                .Where(o => string.Equals(o.Properties["PrefixOrigin"]?.Value?.ToString(), "Manual", StringComparison.OrdinalIgnoreCase))
                .GroupBy(o => o.Properties["InterfaceAlias"]?.Value?.ToString() ?? string.Empty)
                .ToDictionary(g => g.Key, g => g.First());

            var result = new List<VlanItem>();

            foreach (var vlanObj in vlanResults)
            {
                var parentAdapter = vlanObj.Properties["ParentAdapter"]?.Value as PSObject;
                string? name = parentAdapter?.Properties["Name"]?.Value?.ToString();
                object? vlanIdRaw = vlanObj.Properties["AccessVlanId"]?.Value;

                if (string.IsNullOrWhiteSpace(name) || vlanIdRaw is null) continue;
                if (!int.TryParse(vlanIdRaw.ToString(), out int vlanId) || vlanId == 0) continue;

                string ip = string.Empty;
                string mask = string.Empty;
                string alias = $"vEthernet ({name})";

                if (manualIpByAlias.TryGetValue(alias, out var ipObj))
                {
                    ip = ipObj.Properties["IPAddress"]?.Value?.ToString() ?? string.Empty;
                    if (int.TryParse(ipObj.Properties["PrefixLength"]?.Value?.ToString(), out int prefix))
                        mask = IpUtils.PrefixLengthToMask(prefix);
                }

                result.Add(new VlanItem { Name = name!, VlanId = vlanId, IpAddress = ip, SubnetMask = mask });
            }

            return result.OrderBy(v => v.VlanId).ToList();
        }

        public async Task CreateVlanAsync(string switchName, string vlanName, int vlanId, CancellationToken ct = default)
        {
            await ExecuteAsync(ps => ps.AddCommand("Add-VMNetworkAdapter")
                    .AddParameter("ManagementOS", true)
                    .AddParameter("Name", vlanName)
                    .AddParameter("SwitchName", switchName),
                WriteTimeout, ct).ConfigureAwait(false);

            if (vlanId == 1)
            {
                await ExecuteAsync(ps => ps.AddCommand("Set-VMNetworkAdapterVlan")
                        .AddParameter("ManagementOS", true)
                        .AddParameter("VMNetworkAdapterName", vlanName)
                        .AddParameter("Untagged", true),
                    WriteTimeout, ct).ConfigureAwait(false);
            }
            else
            {
                await ExecuteAsync(ps => ps.AddCommand("Set-VMNetworkAdapterVlan")
                        .AddParameter("ManagementOS", true)
                        .AddParameter("VMNetworkAdapterName", vlanName)
                        .AddParameter("Access", true)
                        .AddParameter("VlanId", vlanId),
                    WriteTimeout, ct).ConfigureAwait(false);
            }
        }

        public Task DeleteVlanAsync(string vlanName, CancellationToken ct = default)
        {
            return ExecuteAsync(ps => ps.AddCommand("Remove-VMNetworkAdapter")
                    .AddParameter("ManagementOS", true)
                    .AddParameter("Name", vlanName),
                WriteTimeout, ct);
        }

        public async Task ConfigureIpAsync(string vlanName, string ipAddress, string subnetMask, CancellationToken ct = default)
        {
            string alias = $"vEthernet ({vlanName})";
            string normalizedIp = IpUtils.NormalizeIp(ipAddress);
            int prefixLength = IpUtils.MaskToPrefixLength(subnetMask);

            await ExecuteAsync(ps => ps.AddCommand("Set-NetIPInterface")
                    .AddParameter("InterfaceAlias", alias)
                    .AddParameter("AddressFamily", "IPv4")
                    .AddParameter("Dhcp", "Disabled"),
                WriteTimeout, ct).ConfigureAwait(false);

            await ExecuteAsync(ps => ps.AddCommand("Remove-NetIPAddress")
                    .AddParameter("InterfaceAlias", alias)
                    .AddParameter("AddressFamily", "IPv4")
                    .AddParameter("Confirm", false)
                    .AddParameter("ErrorAction", "SilentlyContinue"),
                WriteTimeout, ct).ConfigureAwait(false);

            await ExecuteAsync(ps => ps.AddCommand("New-NetIPAddress")
                    .AddParameter("InterfaceAlias", alias)
                    .AddParameter("AddressFamily", "IPv4")
                    .AddParameter("IPAddress", normalizedIp)
                    .AddParameter("PrefixLength", prefixLength),
                WriteTimeout, ct).ConfigureAwait(false);
        }

        // =============================================
        // MÉCANIQUE INTERNE
        // =============================================

        private async Task<List<PSObject>> ExecuteAsync(Action<PowerShell> configure, TimeSpan timeout, CancellationToken ct)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await Task.Run(() =>
                {
                    EnsureRunspaceOpen();

                    using PowerShell ps = PowerShell.Create();
                    ps.Runspace = _runspace;
                    configure(ps);

                    string commandText = string.Join(" | ", ps.Commands.Commands.Select(c => c.CommandText));
                    _logger.Log(LogLevel.Debug, commandText, isCommand: true);

                    return InvokeWithTimeout(ps, timeout);
                }, ct).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        private List<PSObject> InvokeWithTimeout(PowerShell ps, TimeSpan timeout)
        {
            var output = new PSDataCollection<PSObject>();
            IAsyncResult asyncResult = ps.BeginInvoke<PSObject, PSObject>(null, output);

            bool completed = asyncResult.AsyncWaitHandle.WaitOne(timeout);
            if (!completed)
            {
                ps.Stop();
                throw new HyperVOperationException(
                    $"L'opération Hyper-V a dépassé le délai maximum ({timeout.TotalSeconds:0}s).",
                    isTimeout: true);
            }

            ps.EndInvoke(asyncResult);

            if (ps.HadErrors)
            {
                var errors = ps.Streams.Error
                    .Select(e => e.Exception?.Message ?? e.ToString())
                    .ToList();

                foreach (string err in errors)
                    _logger.Log(LogLevel.Error, err);

                throw new HyperVOperationException(
                    errors.FirstOrDefault() ?? "Une commande Hyper-V a échoué.",
                    errors);
            }

            return output.ToList();
        }

        private void EnsureRunspaceOpen()
        {
            if (_runspace is not null && _runspace.RunspaceStateInfo.State == RunspaceState.Opened)
                return;

            // Certains modules Windows (NetAdapter, NetTCPIP...) sont des modules "CDXML"
            // dont le chargement est traité comme l'exécution d'un script : si la politique
            // d'exécution PowerShell de la machine est restrictive (le cas par défaut sur
            // beaucoup de PC), leur import échoue. L'ancien code contournait déjà ça en
            // lançant powershell.exe avec "-ExecutionPolicy Bypass" ; on fait l'équivalent
            // ici, mais uniquement pour cette session interne à l'application - la politique
            // d'exécution globale de la machine n'est pas modifiée.
            InitialSessionState iss = InitialSessionState.CreateDefault();
            iss.ExecutionPolicy = ExecutionPolicy.Bypass;

            _runspace?.Dispose();
            _runspace = RunspaceFactory.CreateRunspace(iss);
            _runspace.Open();
        }

        public void Dispose()
        {
            try
            {
                _runspace?.Close();
                _runspace?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Warn, "Erreur lors de la fermeture de la session PowerShell : " + ex.Message);
            }

            _gate.Dispose();
        }
    }
}
