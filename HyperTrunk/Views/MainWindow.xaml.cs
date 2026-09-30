using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using System.Windows.Threading;
using HyperTrunk.Models;
using HyperTrunk.Services;
using HyperTrunk.ViewModels;

namespace HyperTrunk.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly ILuminexGroupsProvider _groupsProvider;

        public MainWindow(MainViewModel viewModel, ILuminexGroupsProvider groupsProvider)
        {
            InitializeComponent();

            _viewModel = viewModel;
            _groupsProvider = groupsProvider;
            DataContext = _viewModel;

            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            Title = string.IsNullOrEmpty(version) ? "HyperTrunk" : $"HyperTrunk v{version}";

            _viewModel.RequestAddVlanDialog += OnRequestAddVlanDialog;
            _viewModel.RequestEditVlanDialog += OnRequestEditVlanDialog;
            _viewModel.RequestShowError += OnRequestShowError;
            _viewModel.LogEntries.CollectionChanged += OnLogEntriesChanged;

            Closed += (s, e) => _viewModel.Dispose();
            Loaded += async (s, e) => await _viewModel.InitializeAsync();
        }

        private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // Reporté en priorité Background : appeler ScrollIntoView de façon synchrone
            // pendant l'événement CollectionChanged force une passe de mise en page
            // immédiate qui peut interrompre l'ajout d'autres lignes arrivant juste après
            // (plusieurs commandes Hyper-V peuvent journaliser coup sur coup), ce qui rend
            // le générateur d'éléments du ListBox incohérent. En attendant que la file du
            // dispatcher soit calme, tous les ajouts en attente sont d'abord appliqués.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (lstConsole.Items.Count > 0)
                    lstConsole.ScrollIntoView(lstConsole.Items[^1]);
            }), DispatcherPriority.Background);
        }

        private async void OnRequestAddVlanDialog(object? sender, EventArgs e)
        {
            var dialog = new AddVlanWindow(_groupsProvider) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                await _viewModel.CreateVlanFromDialogAsync(dialog.VlanName, dialog.VlanId, dialog.IpAddress, dialog.SubnetMask);
            }
        }

        private async void OnRequestEditVlanDialog(object? sender, VlanItemViewModel vlan)
        {
            var existing = new VlanItem
            {
                Name = vlan.Name,
                VlanId = vlan.VlanId,
                IpAddress = vlan.IpAddress,
                SubnetMask = vlan.SubnetMask
            };

            var dialog = new AddVlanWindow(_groupsProvider, existing) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                await _viewModel.EditVlanFromDialogAsync(dialog.VlanName, dialog.IpAddress, dialog.SubnetMask);
            }
        }

        private void OnRequestShowError(object? sender, string message)
        {
            MessageBox.Show(this, message, "HyperTrunk", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        // Ouvre le panneau "Activer ou désactiver des fonctionnalités Windows", où
        // l'utilisateur peut cocher Hyper-V.
        private void OpenWindowsFeatures_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("optionalfeatures.exe") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Unable to open Windows Features." + Environment.NewLine + ex.Message,
                    "HyperTrunk", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
