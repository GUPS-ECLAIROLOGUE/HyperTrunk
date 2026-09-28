using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using HyperTrunk.Logging;
using HyperTrunk.Models;
using HyperTrunk.Services;
using LogLevel = HyperTrunk.Logging.LogLevel;

namespace HyperTrunk.ViewModels
{
    public class MainViewModel : ViewModelBase, IDisposable
    {
        private const int MaxLogEntries = 2000;

        private readonly IHyperVService _hyperV;
        private readonly ILuminexGroupsProvider _groupsProvider;
        private readonly ILogger _logger;

        public ObservableCollection<PhysicalAdapterInfo> Adapters { get; } = new();
        public ObservableCollection<VlanItemViewModel> Vlans { get; } = new();
        public ObservableCollection<LogEntry> LogEntries { get; } = new();

        private PhysicalAdapterInfo? _selectedAdapter;
        public PhysicalAdapterInfo? SelectedAdapter
        {
            get => _selectedAdapter;
            set => SetProperty(ref _selectedAdapter, value);
        }

        private VlanItemViewModel? _selectedVlan;
        public VlanItemViewModel? SelectedVlan
        {
            get => _selectedVlan;
            set => SetProperty(ref _selectedVlan, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

        private string? _busyMessage;
        public string? BusyMessage
        {
            get => _busyMessage;
            private set => SetProperty(ref _busyMessage, value);
        }

        // Optimiste par défaut : le manifeste applicatif élève déjà l'application
        // dans l'immense majorité des cas, donc on évite un flash visuel du bandeau
        // "droits administrateur requis" pendant la courte vérification au démarrage.
        private bool _isAdmin = true;
        public bool IsAdmin
        {
            get => _isAdmin;
            private set => SetProperty(ref _isAdmin, value);
        }

        private bool _isHyperVAvailable = true;
        public bool IsHyperVAvailable
        {
            get => _isHyperVAvailable;
            private set => SetProperty(ref _isHyperVAvailable, value);
        }

        public ICommand LoadAdaptersCommand { get; }
        public ICommand LoadVlansCommand { get; }
        public ICommand CreateSwitchCommand { get; }
        public ICommand DeleteSwitchCommand { get; }
        public ICommand OpenAddVlanCommand { get; }
        public ICommand OpenEditVlanCommand { get; }
        public ICommand DeleteVlanCommand { get; }

        public event EventHandler? RequestAddVlanDialog;
        public event EventHandler<VlanItemViewModel>? RequestEditVlanDialog;
        public event EventHandler<string>? RequestShowError;

        public MainViewModel(IHyperVService hyperV, ILuminexGroupsProvider groupsProvider, ILogger logger)
        {
            _hyperV = hyperV;
            _groupsProvider = groupsProvider;
            _logger = logger;
            _logger.EntryLogged += OnLogEntryLogged;

            LoadAdaptersCommand = new AsyncRelayCommand(LoadAdaptersAsync, () => !IsBusy);
            LoadVlansCommand = new AsyncRelayCommand(LoadVlansAsync, () => !IsBusy);
            CreateSwitchCommand = new AsyncRelayCommand(CreateSwitchAsync, () => !IsBusy && SelectedAdapter is not null);
            DeleteSwitchCommand = new AsyncRelayCommand(DeleteSwitchAsync, () => !IsBusy && SelectedAdapter is not null);
            OpenAddVlanCommand = new RelayCommand(OpenAddVlan, () => !IsBusy);
            OpenEditVlanCommand = new RelayCommand(OpenEditVlan, () => !IsBusy && SelectedVlan is not null);
            DeleteVlanCommand = new AsyncRelayCommand(DeleteVlanAsync, () => !IsBusy && SelectedVlan is not null);
        }

        public async Task InitializeAsync()
        {
            IsAdmin = AdminCheck.IsRunningAsAdministrator();
            if (!IsAdmin)
            {
                _logger.Log(LogLevel.Warn, "L'application n'est pas lancée avec les droits administrateur.");
                return;
            }

            await RunBusyAsync("Vérification de Hyper-V...", async () =>
            {
                try
                {
                    await _hyperV.InitializeAsync();
                    IsHyperVAvailable = await _hyperV.IsHyperVEnabledAsync();
                }
                catch (HyperVOperationException ex)
                {
                    IsHyperVAvailable = false;
                    HandleError("Impossible d'initialiser Hyper-V.", ex);
                }
            });

            if (!IsHyperVAvailable) return;

            await LoadAdaptersCoreAsyncSafe();
            await LoadVlansCoreAsyncSafe();
        }

        // "internal" (plutôt que "private") pour que HyperTrunk.Tests puisse appeler
        // directement ces méthodes et les attendre de façon déterministe, plutôt que
        // de passer par ICommand.Execute (qui est "async void" et ne s'attend pas).
        internal Task LoadAdaptersAsync() => RunBusyAsync("Chargement des cartes réseau...", LoadAdaptersCoreAsyncSafe);

        internal Task LoadVlansAsync() => RunBusyAsync("Chargement des VLANs...", LoadVlansCoreAsyncSafe);

        internal Task CreateSwitchAsync() => RunBusyAsync("Création du vSwitch...", async () =>
        {
            if (SelectedAdapter is null) return;
            try
            {
                await _hyperV.CreateSwitchAsync(SelectedAdapter.Name);
                await LoadAdaptersCoreAsync();
                await LoadVlansCoreAsync();
            }
            catch (HyperVOperationException ex)
            {
                HandleError("Impossible de créer le vSwitch.", ex);
            }
        });

        internal Task DeleteSwitchAsync() => RunBusyAsync("Suppression du vSwitch...", async () =>
        {
            if (SelectedAdapter is null) return;
            try
            {
                await _hyperV.DeleteSwitchAsync(SelectedAdapter.Name);
                await LoadAdaptersCoreAsync();
                await LoadVlansCoreAsync();
            }
            catch (HyperVOperationException ex)
            {
                HandleError("Impossible de supprimer le vSwitch.", ex);
            }
        });

        internal Task DeleteVlanAsync() => RunBusyAsync("Suppression du VLAN...", async () =>
        {
            if (SelectedVlan is null) return;
            try
            {
                await _hyperV.DeleteVlanAsync(SelectedVlan.Name);
                await LoadVlansCoreAsync();
            }
            catch (HyperVOperationException ex)
            {
                HandleError("Impossible de supprimer le VLAN.", ex);
            }
        });

        private void OpenAddVlan()
        {
            if (SelectedAdapter is null)
            {
                RequestShowError?.Invoke(this, "Sélectionne une carte réseau avec un vSwitch actif.");
                return;
            }

            if (!SelectedAdapter.HasSwitch)
            {
                RequestShowError?.Invoke(this, "Cette carte n'a pas de vSwitch actif. Crée-le d'abord.");
                return;
            }

            RequestAddVlanDialog?.Invoke(this, EventArgs.Empty);
        }

        private void OpenEditVlan()
        {
            if (SelectedVlan is null) return;
            RequestEditVlanDialog?.Invoke(this, SelectedVlan);
        }

        public string? SwitchNameForSelectedAdapter => SelectedAdapter is null
            ? null
            : VSwitchNaming.ForAdapter(SelectedAdapter.Name);

        public Task CreateVlanFromDialogAsync(string vlanName, int vlanId, string ipAddress, string subnetMask)
        {
            return RunBusyAsync("Ajout du VLAN...", async () =>
            {
                string? switchName = SwitchNameForSelectedAdapter;
                if (switchName is null) return;

                try
                {
                    await _hyperV.CreateVlanAsync(switchName, vlanName, vlanId);

                    if (!string.IsNullOrWhiteSpace(ipAddress))
                        await _hyperV.ConfigureIpAsync(vlanName, ipAddress, subnetMask);

                    await LoadVlansCoreAsync();
                }
                catch (HyperVOperationException ex)
                {
                    HandleError("Impossible d'ajouter le VLAN.", ex);
                }
            });
        }

        public Task EditVlanFromDialogAsync(string vlanName, string ipAddress, string subnetMask)
        {
            return RunBusyAsync("Mise à jour du VLAN...", async () =>
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(ipAddress))
                        await _hyperV.ConfigureIpAsync(vlanName, ipAddress, subnetMask);

                    await LoadVlansCoreAsync();
                }
                catch (HyperVOperationException ex)
                {
                    HandleError("Impossible de mettre à jour le VLAN.", ex);
                }
            });
        }

        private async Task LoadAdaptersCoreAsync()
        {
            var adapters = await _hyperV.GetPhysicalAdaptersAsync();
            Adapters.Clear();
            foreach (var a in adapters) Adapters.Add(a);
        }

        private async Task LoadAdaptersCoreAsyncSafe()
        {
            try
            {
                await LoadAdaptersCoreAsync();
            }
            catch (HyperVOperationException ex)
            {
                HandleError("Impossible de charger les cartes réseau.", ex);
            }
        }

        private async Task LoadVlansCoreAsync()
        {
            var vlans = await _hyperV.GetVlansAsync();
            var groups = _groupsProvider.GetGroups();

            Vlans.Clear();
            foreach (var v in vlans)
            {
                string color = groups.FirstOrDefault(g => g.VlanId == v.VlanId)?.ColorHex ?? "#444444";
                Vlans.Add(new VlanItemViewModel(v, color));
            }
        }

        private async Task LoadVlansCoreAsyncSafe()
        {
            try
            {
                await LoadVlansCoreAsync();
            }
            catch (HyperVOperationException ex)
            {
                HandleError("Impossible de charger les VLANs.", ex);
            }
        }

        private async Task RunBusyAsync(string message, Func<Task> action)
        {
            IsBusy = true;
            BusyMessage = message;
            try
            {
                await action();
            }
            finally
            {
                IsBusy = false;
                BusyMessage = null;
            }
        }

        private void HandleError(string friendlyMessage, HyperVOperationException ex)
        {
            _logger.Log(LogLevel.Error, friendlyMessage + " " + ex.Message);
            foreach (var detail in ex.PowerShellErrors)
                _logger.Log(LogLevel.Error, detail);

            RequestShowError?.Invoke(this, friendlyMessage + Environment.NewLine + ex.Message);
        }

        private void OnLogEntryLogged(object? sender, LogEntry e)
        {
            void Add()
            {
                LogEntries.Add(e);
                while (LogEntries.Count > MaxLogEntries)
                    LogEntries.RemoveAt(0);
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
                Add();
            else
                dispatcher.BeginInvoke(Add);
        }

        public void Dispose()
        {
            _logger.EntryLogged -= OnLogEntryLogged;
        }
    }
}
