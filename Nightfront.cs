using JeffRidder.NINA.Nightfront.Properties;
using NINA.Core.Utility;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;
using Ookii.Dialogs.Wpf;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Settings = JeffRidder.NINA.Nightfront.Properties.Settings;

namespace JeffRidder.NINA.Nightfront {
    /// <summary>
    /// This class exports the IPluginManifest interface and will be used for the general plugin information and options
    /// The base class "PluginBase" will populate all the necessary Manifest Meta Data out of the AssemblyInfo attributes. Please fill these accoringly
    ///
    /// An instance of this class will be created and set as datacontext on the plugin options tab in N.I.N.A. to be able to configure global plugin settings
    /// The user interface for the settings will be defined by a DataTemplate with the key having the naming convention "Nightfront_Options" where Nightfront corresponds to the AssemblyTitle - found in Options.xaml
    /// </summary>
    [Export(typeof(IPluginManifest))]
    public class Nightfront : PluginBase, INotifyPropertyChanged {
        private readonly IProfileService profileService;

        [ImportingConstructor]
        public Nightfront(IProfileService profileService) {
            if (Settings.Default.UpdateSettings) {
                Settings.Default.Upgrade();
                Settings.Default.UpdateSettings = false;
                CoreUtil.SaveSettings(Settings.Default);
            }

            this.profileService = profileService;
        }

        // The folder NightFront Update checks for today's plan file. Global (not per-profile), since
        // a NightFront installation typically exports to one location regardless of equipment profile.
        public string NightFrontDataFolder {
            get {
                return Settings.Default.NightFrontDataFolder;
            }
            set {
                Settings.Default.NightFrontDataFolder = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        private ICommand selectNightFrontDataFolderCommand;

        public ICommand SelectNightFrontDataFolderCommand => selectNightFrontDataFolderCommand ??= new CommunityToolkit.Mvvm.Input.RelayCommand(SelectNightFrontDataFolder);

        private void SelectNightFrontDataFolder() {
            var dialog = new VistaFolderBrowserDialog {
                Description = "Select the folder where NightFront exports nightly plan files",
                UseDescriptionForTitle = true,
                SelectedPath = NightFrontDataFolder
            };
            if (dialog.ShowDialog() == true) {
                NightFrontDataFolder = dialog.SelectedPath;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null) {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
