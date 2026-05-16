using System.Windows;
using System.Windows.Controls;
using HyperTrunk.Models;

namespace HyperTrunk
{
    public partial class AddVlanWindow : Window
    {
        // Les 4 valeurs qu'on retourne à MainWindow
        public string VlanName { get; private set; }
        public int VlanId { get; private set; }
        public string VlanColorHex { get; private set; }
        public string IpAddress { get; private set; }
        public string SubnetMask { get; private set; }

        public AddVlanWindow()
        {
            InitializeComponent();

            // On charge la liste des groupes Luminex dans le ComboBox
            cmbGroup.ItemsSource = LuminexGroups.All;
        }

        // Constructeur pour l'édition d'un VLAN existant
        public AddVlanWindow(VlanItem vlanExistant) : this()
        {
            txtNom.Text = vlanExistant.Name;
            txtNom.IsReadOnly = true; // Le nom ne peut pas changer après création
            txtIp.Text = vlanExistant.IpAddress;

            // Resélectionne le bon groupe dans le ComboBox
            foreach (LuminexGroup g in LuminexGroups.All)
            {
                if (g.VlanId == vlanExistant.VlanId)
                {
                    cmbGroup.SelectedItem = g;
                    break;
                }
            }

            // Resélectionne le bon masque
            foreach (ComboBoxItem item in cmbMasque.Items)
            {
                if (item.Tag?.ToString() == vlanExistant.SubnetMask)
                {
                    cmbMasque.SelectedItem = item;
                    break;
                }
            }
        }

        // Quand l'utilisateur choisit un groupe,
        // le VLAN ID se remplit automatiquement
        private void cmbGroup_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbGroup.SelectedItem is LuminexGroup groupe)
            {
                txtVlanId.Text = groupe.VlanId.ToString();
            }
        }

        private void btnCreate_Click(object sender, RoutedEventArgs e)
        {
            // --- VALIDATION ---

            if (string.IsNullOrWhiteSpace(txtNom.Text))
            {
                AfficherErreur("Le nom du VLAN est obligatoire.");
                return;
            }

            if (cmbGroup.SelectedItem == null)
            {
                AfficherErreur("Sélectionne un groupe Luminex.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(txtIp.Text) && cmbMasque.SelectedItem == null)
            {
                AfficherErreur("Si tu entres une IP, tu dois aussi choisir un masque.");
                return;
            }

            // --- TOUT EST OK : on remplit les propriétés ---

            LuminexGroup groupe = (LuminexGroup)cmbGroup.SelectedItem;

            VlanName = txtNom.Text.Trim();
            VlanId = groupe.VlanId;
            VlanColorHex = groupe.ColorHex;
            IpAddress = txtIp.Text.Trim();

            if (cmbMasque.SelectedItem is ComboBoxItem masqueItem)
                SubnetMask = masqueItem.Tag.ToString();

            DialogResult = true;
            Close();
        }

        private void AfficherErreur(string message)
        {
            txtErreur.Text = message;
            txtErreur.Visibility = Visibility.Visible;
        }
    }
}