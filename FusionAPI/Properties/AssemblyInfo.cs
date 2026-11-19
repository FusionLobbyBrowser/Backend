using System.Reflection;
using System.Runtime.InteropServices;

using FusionAPI;

[assembly: AssemblyTitle("FusionAPI")]
[assembly: AssemblyDescription("An API used to fetch LabFusion lobbies across various platforms / layers.")]
#if DEBUG
[assembly: AssemblyConfiguration("Debug")]
#else
[assembly: AssemblyConfiguration("Release")]
#endif
[assembly: AssemblyCompany("Catboys")]
[assembly: AssemblyProduct("FusionAPI")]
[assembly: AssemblyCopyright("Copyright © HAHOOS 2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]
[assembly: Guid("47a9d840-440a-4813-80c3-10289765629a")]
[assembly: AssemblyVersion(Fusion.Version)]
[assembly: AssemblyFileVersion(Fusion.Version)]
[assembly: AssemblyInformationalVersion(Fusion.Version)]