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

        // Lier (ou délier) un vSwitch à une carte physique réelle peut prendre nettement
        // plus longtemps qu'une opération sur un adaptateur virtuel - Windows doit
        // réinstaller le pilote du commutateur virtuel étendu sur la carte, ce qui est
        // connu pour être lent sur certains chipsets (notamment les cartes "Killer").
        private static readonly TimeSpan SwitchBindTimeout = TimeSpan.FromSeconds(120);

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
                    "Vérifiez que Hyper-V est bien installée sur cet ordinateur",
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

            LogList($"{result.Count} cartes réseau retenues :", result.Select(a => $"{a.Name} — {a.Description}"));

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
                SwitchBindTimeout, ct);
        }

        public Task DeleteSwitchAsync(string adapterName, CancellationToken ct = default)
        {
            string switchName = VSwitchNaming.ForAdapter(adapterName);
            return ExecuteAsync(ps => ps.AddCommand("Remove-VMSwitch")
                    .AddParameter("Name", switchName)
                    .AddParameter("Force", true),
                SwitchBindTimeout, ct);
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
                    .AddParameter("ErrorAction", "Ignore"),
                ReadTimeout, ct).ConfigureAwait(false);

            // Get-NetIPAddress renvoie PrefixOrigin comme le code CIM numérique brut ("1" pour
            // Manual, "2" WellKnown, "3" Dhcp, ...) quand on le lit via .Properties[...].Value
            // depuis l'API .NET hébergée - PowerShell ne traduit "Manual" en texte que via son
            // formateur de console interactive, jamais atteint ici. Comparer à la chaîne
            // "Manual" ne matchait donc jamais, et c'est ce qui faisait disparaître les IP du
            // tableau des VLAN même une fois correctement configurées (confirmé via un log de
            // diagnostic montrant prefixOrigin=[1] pour une IP qu'on savait manuelle).
            const string ManualPrefixOriginCode = "1";
            var manualIpByAlias = ipResults
                .Where(o => string.Equals(o.Properties["PrefixOrigin"]?.Value?.ToString(), ManualPrefixOriginCode, StringComparison.Ordinal))
                .GroupBy(o => o.Properties["InterfaceAlias"]?.Value?.ToString() ?? string.Empty)
                .ToDictionary(g => g.Key, g => g.First());

            var result = new List<VlanItem>();

            foreach (var vlanObj in vlanResults)
            {
                // "as PSObject" échoue toujours ici : .Value renvoie l'objet .NET brut
                // (Microsoft.HyperV.PowerShell.VMInternalNetworkAdapter), qui n'hérite PAS
                // de PSObject - contrairement à vlanObj lui-même, auto-enveloppé parce qu'il
                // sort directement du pipeline PowerShell. Il faut l'envelopper explicitement
                // avec AsPSObject pour pouvoir lire ses propriétés via .Properties[...].
                object? parentAdapterRaw = vlanObj.Properties["ParentAdapter"]?.Value;
                PSObject? parentAdapter = parentAdapterRaw is null ? null : PSObject.AsPSObject(parentAdapterRaw);
                string? name = parentAdapter?.Properties["Name"]?.Value?.ToString();
                string? switchName = parentAdapter?.Properties["SwitchName"]?.Value?.ToString();
                string? operationMode = vlanObj.Properties["OperationMode"]?.Value?.ToString();

                if (string.IsNullOrWhiteSpace(name)) continue;

                // Ne garder que les adaptateurs sur un switch géré par HyperTrunk (pas le
                // "Default Switch" intégré à Windows), et exclure le vNIC de management que
                // Hyper-V crée automatiquement pour le switch lui-même (son Name == le nom du
                // switch) - ce n'est pas un VLAN créé via "Add VLAN", il est Untagged par
                // défaut comme n'importe quel VLAN 1 et serait sinon confondu avec un vrai VLAN.
                if (string.IsNullOrWhiteSpace(switchName) ||
                    !switchName.StartsWith(VSwitchNaming.Prefix, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, switchName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int vlanId;
                if (string.Equals(operationMode, "Untagged", StringComparison.OrdinalIgnoreCase))
                {
                    // Le VLAN 1 (management) est créé en mode "Untagged" par CreateVlanAsync,
                    // pas "Access" - AccessVlanId y vaut toujours 0 dans ce mode, ce qui le
                    // ferait disparaître de la liste si on se basait dessus comme pour les
                    // autres VLANs.
                    vlanId = 1;
                }
                else
                {
                    object? vlanIdRaw = vlanObj.Properties["AccessVlanId"]?.Value;
                    if (vlanIdRaw is null || !int.TryParse(vlanIdRaw.ToString(), out vlanId) || vlanId == 0) continue;
                }

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

            (string interfaceGuid, int interfaceIndex) = await GetAdapterIdentityAsync(alias, ct).ConfigureAwait(false);

            // L'ancienne version de l'app (avant la refonte) désactivait le DHCP en écrivant
            // directement la clé de registre EnableDHCP, plutôt qu'en appelant
            // Set-NetIPInterface -Dhcp Disabled - et ça marchait de façon fiable. On a
            // confirmé pourquoi : Set-NetIPInterface passe par le provider CIM/CDXML, dont
            // l'état "ActiveStore" met parfois plusieurs secondes à se synchroniser juste
            // après la création de l'adaptateur vEthernet, ce qui faisait échouer
            // New-NetIPAddress avec "Inconsistent parameters PolicyStore PersistentStore and
            // Dhcp Enabled" alors même que Set-NetIPInterface avait rapporté un succès.
            // Écrire directement la clé de registre applique le changement immédiatement,
            // sans passer par cette couche intermédiaire.
            string registryPath = $@"HKLM:\SYSTEM\CurrentControlSet\services\Tcpip\Parameters\Interfaces\{interfaceGuid}";
            await ExecuteAsync(ps => ps.AddCommand("Set-ItemProperty")
                    .AddParameter("Path", registryPath)
                    .AddParameter("Name", "EnableDHCP")
                    .AddParameter("Value", 0),
                WriteTimeout, ct).ConfigureAwait(false);

            // Remove-NetIPAddress lève une erreur *terminante* (CimJobException "aucun objet
            // trouvé") quand il n'y a rien à supprimer - le cas normal sur un VLAN tout juste
            // créé. -ErrorAction ne peut rien y faire (ça ne s'applique qu'aux erreurs non-
            // terminantes). Et vérifier avant coup ne marche pas non plus : Get-NetIPAddress
            // filtré par -InterfaceIndex lève la même erreur terminante sur zéro résultat.
            // Donc on tente la suppression et on avale l'échec : "rien à supprimer" n'est pas
            // une vraie erreur ici, contrairement à un problème Hyper-V plus sérieux qui, lui,
            // ferait de toute façon échouer l'étape New-NetIPAddress juste après.
            try
            {
                await ExecuteAsync(ps => ps.AddCommand("Remove-NetIPAddress")
                        .AddParameter("InterfaceIndex", interfaceIndex)
                        .AddParameter("AddressFamily", "IPv4")
                        .AddParameter("Confirm", false),
                    WriteTimeout, ct).ConfigureAwait(false);
            }
            catch (HyperVOperationException)
            {
            }

            await ExecuteAsync(ps => ps.AddCommand("New-NetIPAddress")
                    .AddParameter("InterfaceIndex", interfaceIndex)
                    .AddParameter("AddressFamily", "IPv4")
                    .AddParameter("IPAddress", normalizedIp)
                    .AddParameter("PrefixLength", prefixLength),
                WriteTimeout, ct).ConfigureAwait(false);
        }

        private async Task<(string InterfaceGuid, int InterfaceIndex)> GetAdapterIdentityAsync(string alias, CancellationToken ct)
        {
            List<PSObject> results = await ExecuteAsync(ps => ps.AddCommand("Get-NetAdapter")
                    .AddParameter("Name", alias),
                ReadTimeout, ct).ConfigureAwait(false);

            PSObject adapter = results.FirstOrDefault()
                ?? throw new HyperVOperationException($"Adaptateur réseau introuvable : {alias}.");

            string? guid = adapter.Properties["InterfaceGuid"]?.Value?.ToString();
            object? indexRaw = adapter.Properties["InterfaceIndex"]?.Value;

            if (string.IsNullOrWhiteSpace(guid) || indexRaw is null || !int.TryParse(indexRaw.ToString(), out int index))
            {
                throw new HyperVOperationException($"Impossible de lire le GUID/index de l'adaptateur : {alias}.");
            }

            return (guid, index);
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
