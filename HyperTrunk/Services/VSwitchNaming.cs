namespace HyperTrunk.Services
{
    // Règle de nommage des vSwitch Hyper-V créés par HyperTrunk.
    // Point unique de vérité : avant, cette règle était dupliquée entre
    // MainWindow.xaml.cs et PowerShellService.cs.
    public static class VSwitchNaming
    {
        public const string Prefix = "HyperTrunk_";

        public static string ForAdapter(string adapterName)
        {
            return Prefix + adapterName.Replace(" ", "_");
        }
    }
}
