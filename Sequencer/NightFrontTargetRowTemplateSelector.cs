using System.Windows;
using System.Windows.Controls;

namespace JeffRidder.NINA.Nightfront.Sequencer {

    /// <summary>
    /// Picks the per-target row template for a NightFrontTargetSummary, or a minimal fallback row
    /// for anything else. NightFrontContainer.Items isn't guaranteed to contain only
    /// DeepSkyObjectContainer targets (NightFrontJsonImporter.BuildItem can put any supported
    /// instruction type at the top level of a plan), so items that aren't targets get a generic row
    /// instead of forcing target-only bindings onto them.
    /// </summary>
    public class NightFrontTargetRowTemplateSelector : DataTemplateSelector {
        public DataTemplate TargetRowTemplate { get; set; }

        public DataTemplate GenericRowTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container) {
            return item is NightFrontTargetSummary ? TargetRowTemplate : GenericRowTemplate;
        }
    }
}
