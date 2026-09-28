using HyperTrunk.Models;
using HyperTrunk.Services;
using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Linq;

namespace HyperTrunk
{
    public partial class MainWindow : Window
    {
        // =============================================
        // DONNÉES
        // =============================================

        private readonly PowerShellService _ps;
        private readonly List<VlanItem> _vlans = new List<VlanItem>();

        // =============================================
        // INITIALISATION
        // =============================================

        public MainWindow()
        {
            InitializeComponent();

            // On crée le service PowerShell et on le branche sur la console
            _ps = new PowerShellService();
            _ps.OnLog = AjouterConsole;
            this.Closed += (s, e) => _ps.Dispose();

            if (!VerifierAdmin())
            {
                voileAdmin.Visibility = Visibility.Visible;
                return;
            }

            VerifierHyperV();
            ChargerCartesReseau();
            ChargerVlansExistants();
        }

        // =============================================
        // HYPER-V
        // =============================================

        private void VerifierHyperV()
        {
            bool actif = _ps.VerifierHyperV();
            voileHyperV.Visibility = actif ? Visibility.Collapsed : Visibility.Visible;
        }

        // =============================================
        // CARTES RÉSEAU
        // =============================================

        private void ChargerCartesReseau()
        {
            lstAdapters.Items.Clear();

            List<string> vswitchs = _ps.RecupererCartesAvecVSwitch();
            List<string> physiques = _ps.RecupererAdaptateursPresentsFisiquement();

            foreach (NetworkInterface carte in NetworkInterface.GetAllNetworkInterfaces())
            {
                //On n'affiche que les Ethernet
                if (carte.NetworkInterfaceType != NetworkInterfaceType.Ethernet)
                    continue;

                // On n'affiche que les adaptateurs actuellement connectés
                if (!physiques.Any(p => p.Contains(carte.Description)))
                    continue;

                string nom = carte.Name.ToLower();
                
                if (nom.Contains("virtual") || nom.Contains("filter") ||
                    nom.Contains("wfp") || nom.Contains("miniport") ||
                    nom.Contains("fortinet") || nom.Contains("ssl") ||
                    nom.Contains("bouclage") || nom.Contains("tap") ||
                    nom.Contains("loopback") || nom.Contains("npcap") ||
                    nom.Contains("qos") || nom.Contains("packet") ||
                    nom.Contains("bluetooth") || nom.Contains("vethernet") ||
                    nom.Contains("vswitch") || nom.Contains("local") ||
                    nom.Contains("usb") || nom.Contains("host") ||
                    nom.Contains("noyau") || nom.Contains("hyper-v"))
                    continue;

                lstAdapters.Items.Add(new AdapterItem
                {
                    Name = carte.Name,
                    Description = carte.Description,
                    HasSwitch = vswitchs.Contains(carte.Description)
                });
            }
        }


        private void lstAdapters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ChargerVlans();
        }

        private void btnCreateSwitch_Click(object sender, RoutedEventArgs e)
        {
            if (lstAdapters.SelectedItem is not AdapterItem selected) return;

            _ps.CreerVSwitch(selected.Name);

            ChargerCartesReseau();
            ChargerVlansExistants();
        }

        private void btnDeleteSwitch_Click(object sender, RoutedEventArgs e)
        {
            if (lstAdapters.SelectedItem is not AdapterItem selected) return;

            _ps.SupprimerVSwitch(selected.Name);

            ChargerCartesReseau();
            ChargerVlansExistants();
        }

        private void btnRefreshNIC_Click(object sender, RoutedEventArgs e)
        {
            ChargerCartesReseau();
        }

        private void btnRefreshVLAN_Click(object sender, RoutedEventArgs e)
        {
            ChargerVlansExistants();
        }

        // =============================================
        // VLANs
        // =============================================

        private async void ChargerVlansExistants()
        {
            _vlans.Clear();
            List<VlanItem> vlans = await _ps.RecupererVlansExistantsAsync();
            _vlans.AddRange(vlans.OrderBy(v => v.VlanId));
            ChargerVlans();
        }

        private void ChargerVlans()
        {
            lstVlans.Items.Clear();

            foreach (VlanItem vlan in _vlans)
                lstVlans.Items.Add(vlan);
        }

        private void btnAddVlan_Click(object sender, RoutedEventArgs e)
        {
            if (lstAdapters.SelectedItem is not AdapterItem selected)
            {
                MessageBox.Show("Sélectionne une carte réseau avec un vSwitch actif.");
                return;
            }

            if (!selected.HasSwitch)
            {
                MessageBox.Show("Cette carte n'a pas de vSwitch actif. Crée-le d'abord.");
                return;
            }

            AddVlanWindow popup = new AddVlanWindow();
            popup.Owner = this;

            if (popup.ShowDialog() == true)
            {
                string nomSwitch = "HyperTrunk_" + selected.Name.Replace(" ", "_");

                // Création de l'adaptateur virtuel + assignation du VLAN ID
                _ps.CreerVlan(nomSwitch, popup.VlanName, popup.VlanId);

                // Configuration IP si renseignée
                if (!string.IsNullOrWhiteSpace(popup.IpAddress))
                    _ps.ConfigurerIp(popup.VlanName, popup.IpAddress, popup.SubnetMask);

                // On mémorise le VLAN dans notre liste interne
                _vlans.Add(new VlanItem
                {
                    Name = popup.VlanName,
                    VlanId = popup.VlanId,
                    ColorHex = popup.VlanColorHex,
                    IpAddress = popup.IpAddress,
                    SubnetMask = popup.SubnetMask
                });

                _vlans.Sort((a, b) => a.VlanId.CompareTo(b.VlanId));

                ChargerVlans();
            }
        }

        private void btnEditVlan_Click(object sender, RoutedEventArgs e)
        {
            if (lstVlans.SelectedItem is not VlanItem vlanExistant) return;

            // On ouvre la popup pré-remplie avec les infos du VLAN sélectionné
            AddVlanWindow popup = new AddVlanWindow(vlanExistant);
            popup.Owner = this;

            if (popup.ShowDialog() == true)
            {
                // Mise à jour de l'IP si elle a changé
                if (!string.IsNullOrWhiteSpace(popup.IpAddress))
                    _ps.ConfigurerIp(popup.VlanName, popup.IpAddress, popup.SubnetMask);

                // Mise à jour dans la liste interne
                vlanExistant.IpAddress = popup.IpAddress;
                vlanExistant.SubnetMask = popup.SubnetMask;

                ChargerVlans();
            }
        }

        private void btnDeleteVlan_Click(object sender, RoutedEventArgs e)
        {
            if (lstVlans.SelectedItem is not VlanItem selected) return;

            _ps.SupprimerVlan(selected.Name);

            _vlans.Remove(selected);

            ChargerVlans();
        }

        // =============================================
        // CONSOLE
        // =============================================

        private void AjouterConsole(string texte)
        {
            Dispatcher.Invoke(() =>
            {
                if (texte.StartsWith("SND >"))
                {
                    string horodatage = $"[{DateTime.Now:HH:mm:ss}] ";

                    TextRange rangeHeure = new TextRange(
                        txtConsole.Document.ContentEnd,
                        txtConsole.Document.ContentEnd);
                    rangeHeure.Text = horodatage;
                    rangeHeure.ApplyPropertyValue(TextElement.ForegroundProperty,
                        Brushes.Gray);

                    TextRange rangeSnd = new TextRange(
                        txtConsole.Document.ContentEnd,
                        txtConsole.Document.ContentEnd);
                    rangeSnd.Text = texte.TrimEnd() + "\n";
                    rangeSnd.ApplyPropertyValue(TextElement.ForegroundProperty,
                        new SolidColorBrush(Color.FromRgb(0, 120, 0)));
                }
                else
                {
                    // On nettoie les lignes vides multiples
                    string propre = texte.TrimEnd();
                    if (string.IsNullOrWhiteSpace(propre)) return;

                    TextRange rangeTexte = new TextRange(
                        txtConsole.Document.ContentEnd,
                        txtConsole.Document.ContentEnd);
                    rangeTexte.Text = propre + "\n";
                    rangeTexte.ApplyPropertyValue(TextElement.ForegroundProperty,
                        Brushes.Black);
                }

                txtConsole.ScrollToEnd();
            });
        }

        // =============================================
        // VERIFICATION ADMIN
        // =============================================

        private bool VerifierAdmin()
        {
            using (System.Security.Principal.WindowsIdentity identity =
                   System.Security.Principal.WindowsIdentity.GetCurrent())
            {
                System.Security.Principal.WindowsPrincipal principal =
                    new System.Security.Principal.WindowsPrincipal(identity);

                return principal.IsInRole(
                    System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
        }

        // =============================================
        // MODÈLE CARTE RÉSEAU (reste ici car utilisé
        // uniquement dans cette fenêtre)
        // =============================================

        public class AdapterItem
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public bool HasSwitch { get; set; }

            public string Display => HasSwitch
                ? $"{Name}  |  {Description}  |  vSwitch actif ✔"
                : $"{Name}  |  {Description}  |  pas de vSwitch ✘";
        }

    }
}