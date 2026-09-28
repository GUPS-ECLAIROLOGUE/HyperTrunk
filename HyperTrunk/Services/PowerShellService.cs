using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HyperTrunk.Models;

namespace HyperTrunk.Services
{
    public class PowerShellService : IDisposable
    {
        public Action<string> OnLog { get; set; }

        private readonly Process _process;
        private readonly object _lock = new object();
        private const string FIN_MARQUEUR = "---FIN---";

        // =============================================
        // INITIALISATION : un seul processus PowerShell
        // ouvert pour toute la durée de l'application
        // =============================================

        public PowerShellService()
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "powershell.exe";
            psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -NoExit -Command -";
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;

            _process = Process.Start(psi);
        }

        // =============================================
        // MÉTHODE DE BASE
        // =============================================

        public string Executer(string commande)
        {
            lock (_lock)
            {
                Log("SND > " + commande);

                StringBuilder output = new StringBuilder();

                // On envoie la commande puis le marqueur de fin
                _process.StandardInput.WriteLine("[Console]::OutputEncoding = [System.Text.Encoding]::UTF8");
                _process.StandardInput.WriteLine(commande);
                _process.StandardInput.WriteLine($"Write-Output '{FIN_MARQUEUR}'");
                _process.StandardInput.Flush();

                // On lit jusqu'au marqueur de fin
                string ligne;
                while ((ligne = _process.StandardOutput.ReadLine()) != null)
                {
                    if (ligne.Trim() == FIN_MARQUEUR) break;
                    output.Append(ligne + "\r");
                }

                string result = output.ToString();
                if (!string.IsNullOrWhiteSpace(result))
                {
                    foreach (string l in result.Split('\n'))
                    {
                        string propre = l.TrimEnd('\r').TrimEnd();
                        if (!string.IsNullOrWhiteSpace(propre))
                            Log(propre);
                    }
                }
                // Log("==========================");

                return result;
            }
        }

        public Task<string> ExecuterAsync(string commande)
        {
            return Task.Run(() => Executer(commande));
        }

        // =============================================
        // NETWORK ADAPTERS
        // =============================================

        public List<string> RecupererAdaptateursPresentsFisiquement()
        {
            List<string> resultat = new List<string>();

            string retour = Executer(
                "Get-PnpDevice -Class Net | " +
                "Where-Object { $_.Status -eq 'OK' } | " +
                "Select-Object -ExpandProperty FriendlyName");

            foreach (string ligne in retour.Split('\n'))
            {
                string l = ligne.Trim();
                if (!string.IsNullOrWhiteSpace(l))
                    resultat.Add(l);
            }

            return resultat;
        }

        public Task<List<string>> RecupererAdaptateursPresentsFisiquementAsync()
        {
            return Task.Run(() => RecupererAdaptateursPresentsFisiquement());
        }

        // =============================================
        // VSWITCH
        // =============================================

        public void CreerVSwitch(string nomCarte)
        {
            string nomSwitch = "HyperTrunk_" + nomCarte.Replace(" ", "_");
            Executer($"New-VMSwitch -Name '{nomSwitch}' -NetAdapterName '{nomCarte}' -AllowManagementOS $true");
        }

        public void SupprimerVSwitch(string nomCarte)
        {
            string nomSwitch = "HyperTrunk_" + nomCarte.Replace(" ", "_");
            Executer($"Remove-VMSwitch '{nomSwitch}' -Force");
        }

        public List<string> RecupererCartesAvecVSwitch()
        {
            List<string> resultat = new List<string>();

            string retour = Executer(
                "Get-VMSwitch | Select -ExpandProperty NetAdapterInterfaceDescription");

            foreach (string ligne in retour.Split('\n'))
            {
                string l = ligne.Trim();
                if (!string.IsNullOrWhiteSpace(l))
                    resultat.Add(l);
            }

            return resultat;
        }

        public Task<List<string>> RecupererCartesAvecVSwitchAsync()
        {
            return Task.Run(() => RecupererCartesAvecVSwitch());
        }

        public bool VerifierHyperV()
        {
            string retour = Executer(
                "Get-WindowsOptionalFeature -FeatureName Microsoft-Hyper-V-All -Online");

            return !(retour.ToLower().Contains("state") && retour.ToLower().Contains("disabled"));
        }

        // =============================================
        // VLAN
        // =============================================

        public List<VlanItem> RecupererVlansExistants()
        {
            List<VlanItem> resultat = new List<VlanItem>();

            string retour = Executer(
                "Get-VMNetworkAdapterVlan -ManagementOS | " +
                "ForEach-Object { [PSCustomObject]@{ Name = $_.ParentAdapter.Name; VlanId = $_.AccessVlanId } } | " +
                "ConvertTo-Json");

            if (string.IsNullOrWhiteSpace(retour)) return resultat;

            try
            {
                int debut = retour.IndexOf('[');
                if (debut == -1) debut = retour.IndexOf('{');
                if (debut == -1) return resultat;

                string json = retour.Substring(debut);

                foreach (string bloc in json.Split('{'))
                {
                    if (!bloc.Contains("Name")) continue;

                    string nom = ExtraireValeurJson(bloc, "Name");
                    string vlanId = ExtraireValeurJson(bloc, "VlanId");

                    if (string.IsNullOrWhiteSpace(nom)) continue;
                    if (!int.TryParse(vlanId, out int id) || id == 0) continue;

                    string couleur = "#444444";
                    foreach (LuminexGroup g in LuminexGroups.All)
                    {
                        if (g.VlanId == id)
                        {
                            couleur = g.ColorHex;
                            break;
                        }
                    }

                    string ip = RecupererIpAdaptateur(nom);
                    string masque = RecupererMasqueAdaptateur(nom);

                    resultat.Add(new VlanItem
                    {
                        Name = nom,
                        VlanId = id,
                        ColorHex = couleur,
                        IpAddress = ip,
                        SubnetMask = masque
                    });
                }
            }
            catch { }

            return resultat;
        }

        public Task<List<VlanItem>> RecupererVlansExistantsAsync()
        {
            return Task.Run(() => RecupererVlansExistants());
        }

        public void CreerVlan(string nomSwitch, string nomVlan, int vlanId)
        {
            Executer($"Add-VMNetworkAdapter -ManagementOS -Name '{nomVlan}' -SwitchName '{nomSwitch}'");

            if (vlanId == 1)
            {
                Executer($"Set-VMNetworkAdapterVlan -ManagementOS -VMNetworkAdapterName '{nomVlan}' -Untagged");
            }
            else
            {
                Executer($"Set-VMNetworkAdapterVlan -ManagementOS -VMNetworkAdapterName '{nomVlan}' -Access -VlanId {vlanId}");
            }
        }

        public void SupprimerVlan(string nomVlan)
        {
            Executer($"Remove-VMNetworkAdapter -ManagementOS -Name '{nomVlan}'");
        }

        public void ConfigurerIp(string nomVlan, string ip, string masque)
        {
            string interfaceName = $"vEthernet ({nomVlan})";
            string ipPropre = NettoyerIp(ip);

            Executer($"$a = Get-NetAdapter | Where-Object {{ $_.Name -eq '{interfaceName}' }}; " +
                     $"Set-ItemProperty -Path \"HKLM:\\SYSTEM\\CurrentControlSet\\services\\Tcpip\\Parameters\\Interfaces\\$($a.InterfaceGuid)\" " +
                     $"-Name EnableDHCP -Value 0");

            Executer($"$a = Get-NetAdapter | Where-Object {{ $_.Name -eq '{interfaceName}' }}; " +
                     $"Remove-NetIPAddress -InterfaceIndex $a.InterfaceIndex -AddressFamily IPv4 -Confirm:$false -ErrorAction SilentlyContinue");

            Executer($"$a = Get-NetAdapter | Where-Object {{ $_.Name -eq '{interfaceName}' }}; " +
                     $"New-NetIPAddress -InterfaceIndex $a.InterfaceIndex -AddressFamily IPv4 -IPAddress '{ipPropre}' -PrefixLength {MasqueEnPrefixe(masque)}");
        }

        // =============================================
        // UTILITAIRES PRIVÉS
        // =============================================

        private string RecupererIpAdaptateur(string nomVlan)
        {
            string retour = Executer(
                $"$a = Get-NetAdapter | Where-Object {{ $_.Name -eq 'vEthernet ({nomVlan})' }}; " +
                $"Get-NetIPAddress -InterfaceIndex $a.InterfaceIndex -AddressFamily IPv4 " +
                $"-ErrorAction SilentlyContinue | " +
                $"Where-Object {{ $_.PrefixOrigin -eq 'Manual' }} | " +
                $"Select-Object -First 1 -ExpandProperty IPAddress");

            return retour.Trim();
        }

        private string RecupererMasqueAdaptateur(string nomVlan)
        {
            string retour = Executer(
                $"$a = Get-NetAdapter | Where-Object {{ $_.Name -eq 'vEthernet ({nomVlan})' }}; " +
                $"$ip = Get-NetIPAddress -InterfaceIndex $a.InterfaceIndex -AddressFamily IPv4 " +
                $"-ErrorAction SilentlyContinue | " +
                $"Where-Object {{ $_.PrefixOrigin -eq 'Manual' }} | Select-Object -First 1; " +
                $"if ($ip) {{ $prefix = [int]$ip.PrefixLength; " +
                $"$mask = [UInt32]([UInt32]::MaxValue -shl (32 - $prefix)); " +
                $"$bytes = [BitConverter]::GetBytes($mask); " +
                $"[Array]::Reverse($bytes); " +
                $"([IPAddress]$bytes).ToString() }}");

            return retour.Trim();
        }

        private string ExtraireValeurJson(string bloc, string cle)
        {
            string recherche = $"\"{cle}\":";
            int index = bloc.IndexOf(recherche);
            if (index == -1) return null;

            int debut = index + recherche.Length;
            string reste = bloc.Substring(debut).Trim();

            if (reste.StartsWith("\""))
            {
                int fin = reste.IndexOf("\"", 1);
                return fin == -1 ? null : reste.Substring(1, fin - 1);
            }
            else
            {
                string chiffres = "";
                foreach (char c in reste)
                {
                    if (char.IsDigit(c)) chiffres += c;
                    else break;
                }
                return chiffres;
            }
        }

        private void Log(string texte)
        {
            OnLog?.Invoke(texte);
        }

        private int MasqueEnPrefixe(string masque)
        {
            int prefixe = 0;
            foreach (string octet in masque.Split('.'))
            {
                int valeur = int.Parse(octet);
                while (valeur > 0)
                {
                    prefixe += valeur & 1;
                    valeur >>= 1;
                }
            }
            return prefixe;
        }

        private string NettoyerIp(string ip)
        {
            string[] octets = ip.Split('.');
            if (octets.Length != 4) return ip;

            for (int i = 0; i < 4; i++)
                octets[i] = int.Parse(octets[i]).ToString();

            return string.Join(".", octets);
        }

        // =============================================
        // NETTOYAGE : on ferme le processus PowerShell
        // quand l'application se ferme
        // =============================================

        public void Dispose()
        {
            try
            {
                _process.StandardInput.WriteLine("exit");
                _process.StandardInput.Flush();
                _process.WaitForExit(2000);
                _process.Dispose();
            }
            catch { }
        }
    }
}