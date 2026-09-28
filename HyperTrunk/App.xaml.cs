using System;
using System.Windows;
using System.Windows.Threading;
using HyperTrunk.Logging;
using HyperTrunk.Services;
using HyperTrunk.ViewModels;
using HyperTrunk.Views;
using LogLevel = HyperTrunk.Logging.LogLevel;

namespace HyperTrunk
{
    public partial class App : Application
    {
        private FileLogger? _logger;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += OnDispatcherUnhandledException;

            _logger = new FileLogger();
            var groupsProvider = new LuminexGroupsProvider(_logger);
            var hyperVService = new HyperVService(_logger);
            var mainViewModel = new MainViewModel(hyperVService, groupsProvider, _logger);

            var window = new MainWindow(mainViewModel, groupsProvider);
            window.Show();
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            _logger?.Log(LogLevel.Error, "Erreur non gérée : " + e.Exception);

            MessageBox.Show(
                "Une erreur inattendue est survenue et a été enregistrée dans le journal." +
                Environment.NewLine + Environment.NewLine + e.Exception.Message,
                "HyperTrunk",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            e.Handled = true;
        }
    }
}
