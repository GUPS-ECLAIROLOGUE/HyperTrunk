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
        public ICommand CopyConsoleCommand { get; }

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
            CopyConsoleCommand = new RelayCommand(CopyConsoleToClipboard, () => LogEntries.Count > 0);
        }

        // Les TextBlock du panneau console ne permettent pas la sélection de texte native de
        // WPF - ce bouton copie tout le contenu affiché dans le presse-papiers en une fois,
        // pour pouvoir le recoller ailleurs (ex: le transmettre pour du support).
        private void CopyConsoleToClipboard()
        {
            string text = string.Join(Environment.NewLine,
                LogEntries.Select(e => $"[{e.Timestamp:HH:mm:ss}] {e.Message}"));

            if (text.Length > 0)
                System.Windows.Clipboard.SetText(text);
        }

        public async Task InitializeAsync()
        {
            IsAdmin = AdminCheck.IsRunningAsAdministrator();
            if (!IsAdmin)
            {
                _logger.Log(LogLevel.Warn, "The application is not running with administrator rights.");
                return;
            }

            await RunBusyAsync("Checking Hyper-V...", async () =>
            {
                try
                {
                    await _hyperV.InitializeAsync();
                    IsHyperVAvailable = await _hyperV.IsHyperVEnabledAsync();
                }
                catch (HyperVOperationException ex)
                {
                    // Pas de HandleError/popup ici : le panneau "Hyper-V n'est pas activé sur
                    // cet ordinateur" (lié à IsHyperVAvailable) informe déjà l'utilisateur de
                    // façon claire, un MessageBox par-dessus serait redondant.
                    IsHyperVAvailable = false;
                    _logger.Log(LogLevel.Error, "Unable to initialize Hyper-V. " + ex.Message);
                    foreach (var detail in ex.PowerShellErrors)
                        _logger.Log(LogLevel.Error, detail);
                }
            });

            if (!IsHyperVAvailable) return;

            await LoadAdaptersCoreAsyncSafe();
            await LoadVlansCoreAsyncSafe();
        }

        // "internal" (plutôt que "private") pour que HyperTrunk.Tests puisse appeler
        // directement ces méthodes et les attendre de façon déterministe, plutôt que
        // de passer par ICommand.Execute (qui est "async void" et ne s'attend pas).
        internal Task LoadAdaptersAsync() => RunBusyAsync("Loading network adapters...", LoadAdaptersCoreAsyncSafe);

        internal Task LoadVlansAsync() => RunBusyAsync("Loading VLANs...", LoadVlansCoreAsyncSafe);

        internal Task CreateSwitchAsync() => RunBusyAsync("Creating vSwitch...", async () =>
        {
            if (SelectedAdapter is null) return;

            // Le rafraîchissement (LoadAdapters/LoadVlans) est volontairement hors du
            // try/catch de l'action principale et utilise les variantes "Safe" : juste
            // après la création/suppression d'un vSwitch, Hyper-V peut mettre un instant à
            // stabiliser son état WMI, et une requête de rafraîchissement immédiate peut
            // échouer ("objet introuvable") alors que l'action elle-même a bien réussi. Sans
            // cette séparation, cet échec de rafraîchissement était signalé comme un échec de
            // l'action principale, alors que le vSwitch avait bel et bien été créé/supprimé.
            try
            {
                await _hyperV.CreateSwitchAsync(SelectedAdapter.Name);
            }
            catch (HyperVOperationException ex)
            {
                HandleError("Unable to create the vSwitch.", ex);
            }

            await LoadAdaptersCoreAsyncSafe();
            await LoadVlansCoreAsyncSafe();
        });

        internal Task DeleteSwitchAsync() => RunBusyAsync("Removing vSwitch...", async () =>
        {
            if (SelectedAdapter is null) return;

            // Voir le commentaire équivalent dans CreateSwitchAsync ci-dessus.
            try
            {
                await _hyperV.DeleteSwitchAsync(SelectedAdapter.Name);
            }
            catch (HyperVOperationException ex)
            {
                HandleError("Unable to remove the vSwitch.", ex);
            }

            await LoadAdaptersCoreAsyncSafe();
            await LoadVlansCoreAsyncSafe();
        });

        internal Task DeleteVlanAsync() => RunBusyAsync("Removing VLAN...", async () =>
        {
            if (SelectedVlan is null) return;

            // Voir le commentaire dans DeleteSwitchAsync : le rafraîchissement est séparé de
            // l'action principale pour ne pas lui attribuer à tort un échec de chargement.
            try
            {
                await _hyperV.DeleteVlanAsync(SelectedVlan.Name);
            }
            catch (HyperVOperationException ex)
            {
                HandleError("Unable to remove the VLAN.", ex);
            }

            await LoadVlansCoreAsyncSafe();
        });

        private void OpenAddVlan()
        {
            if (SelectedAdapter is null)
            {
                RequestShowError?.Invoke(this, "Please select a network adapter with an active vSwitch.");
                return;
            }

            if (!SelectedAdapter.HasSwitch)
            {
                RequestShowError?.Invoke(this, "This adapter has no active vSwitch. Please create it first.");
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
            return RunBusyAsync("Adding VLAN...", async () =>
            {
                string? switchName = SwitchNameForSelectedAdapter;
                if (switchName is null) return;

                // Voir le commentaire dans DeleteSwitchAsync : le rafraîchissement est séparé
                // de l'action principale pour ne pas lui attribuer à tort un échec de
                // chargement.
                try
                {
                    await _hyperV.CreateVlanAsync(switchName, vlanName, vlanId);

                    // removeExisting: false - l'adaptateur vient d'être créé, il n'a jamais
                    // eu d'IP ; tenter d'en supprimer une n'aurait fait que journaliser une
                    // erreur PowerShell trompeuse ("rien à supprimer") sans raison.
                    if (!string.IsNullOrWhiteSpace(ipAddress))
                        await _hyperV.ConfigureIpAsync(vlanName, ipAddress, subnetMask, removeExisting: false);
                }
                catch (HyperVOperationException ex)
                {
                    HandleError("Unable to add the VLAN.", ex);
                }

                await LoadVlansCoreAsyncSafe();
            });
        }

        public Task EditVlanFromDialogAsync(string vlanName, string ipAddress, string subnetMask)
        {
            return RunBusyAsync("Updating VLAN...", async () =>
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(ipAddress))
                        await _hyperV.ConfigureIpAsync(vlanName, ipAddress, subnetMask);
                }
                catch (HyperVOperationException ex)
                {
                    HandleError("Unable to update the VLAN.", ex);
                }

                await LoadVlansCoreAsyncSafe();
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
                HandleError("Unable to load network adapters.", ex);
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
                HandleError("Unable to load VLANs.", ex);
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
