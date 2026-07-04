using System.ComponentModel.Composition;
using System.Windows;

namespace JeffRidder.NINA.Nightfront.Sequencer {
    [Export(typeof(ResourceDictionary))]
    public partial class NightFrontSequencerTemplates : ResourceDictionary {
        public NightFrontSequencerTemplates() {
            InitializeComponent();
        }
    }
}
