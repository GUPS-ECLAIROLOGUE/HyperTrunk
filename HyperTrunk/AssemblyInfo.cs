using System.Runtime.CompilerServices;
using System.Windows;

// Permet au projet de tests d'appeler directement les méthodes "internal"
// (ex: la logique des commandes de MainViewModel), sans matériel Hyper-V.
[assembly: InternalsVisibleTo("HyperTrunk.Tests")]

[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]
