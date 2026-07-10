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

        // Comma-separated filter names (e.g. "L, B, G, R, OIII, Ha, SII") giving the priority order
        // NightFrontMetadataStore picks the next outstanding calibration requirement in - broadband
        // filters typically need to be shot while the twilight sky is still brighter than narrowband.
        public string FlatFilterOrder {
            get {
                return Settings.Default.FlatFilterOrder;
            }
            set {
                Settings.Default.FlatFilterOrder = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        // How many days after a calibration requirement's flats were completed before it's pruned so
        // it can be reshot - see NightFrontMetadataStore.PruneStaleCompleted.
        public int FlatRefreshDays {
            get {
                return Settings.Default.FlatRefreshDays;
            }
            set {
                Settings.Default.FlatRefreshDays = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        // GA compute-budget preset (Fast/Balanced/Thorough - matching NightFront's own EffortPreset,
        // optimizer/EffortPreset.kt) for an unattended safety-recovery replan. Read by
        // NightFrontReplanInstruction (todos/nina-safety-delay-plan.md, Phase 3) and passed straight
        // through to the NightFront CLI as `replan --effort=<value>`. Defaults to Fast: most machines
        // NINA actually runs on overnight (a mini-PC or NUC bolted to the mount) are far less
        // powerful than whatever desktop the plan's config was tuned on, and a replan runs
        // unattended during a real weather interruption with no one watching a progress bar to
        // justify a slower solve. A user with a genuinely capable imaging PC can opt into Balanced
        // or Thorough here.
        public static readonly string[] ReplanEffortLevelOptions = { "Fast", "Balanced", "Thorough" };

        public string ReplanEffortLevel {
            get {
                return Settings.Default.ReplanEffortLevel;
            }
            set {
                Settings.Default.ReplanEffortLevel = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        // Path to whatever actually launches the NightFront CLI - e.g. a native NightFront-cli.exe
        // if one is ever packaged, or (today, since no such packaging exists yet) a small wrapper
        // batch/cmd script the user writes once that runs `java -jar <path-to-nightfront.jar> %*`.
        // NightFrontReplanInstruction runs this with `replan <args...>` appended - see its own doc
        // comment for the full argument list. This indirection (a configurable path, rather than
        // NightFrontReplanInstruction hardcoding how to invoke a JVM app) is a deliberate, narrower
        // stopgap: packaging a proper native CLI launcher alongside the existing GUI installer is a
        // real, separate follow-up (see todos/nina-safety-delay-plan.md's Phase 3 status), not
        // solved here.
        public string NightFrontCliPath {
            get {
                return Settings.Default.NightFrontCliPath;
            }
            set {
                Settings.Default.NightFrontCliPath = value;
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

        private ICommand selectNightFrontCliPathCommand;

        public ICommand SelectNightFrontCliPathCommand => selectNightFrontCliPathCommand ??= new CommunityToolkit.Mvvm.Input.RelayCommand(SelectNightFrontCliPath);

        private void SelectNightFrontCliPath() {
            var dialog = new VistaOpenFileDialog {
                Title = "Select the NightFront CLI executable (or a wrapper script that launches it)",
                Filter = "Executable files (*.exe;*.bat;*.cmd)|*.exe;*.bat;*.cmd|All files (*.*)|*.*",
                FileName = NightFrontCliPath
            };
            if (dialog.ShowDialog() == true) {
                NightFrontCliPath = dialog.FileName;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null) {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
