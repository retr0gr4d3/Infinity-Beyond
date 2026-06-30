using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;

namespace Launcher.ViewModels
{
    // DropFilter: two independent lists drive auto-looting and auto-dusting.
    // - Keep list  -> matching drops are pulled into the inventory (getDrop).
    // - Reject list -> matching drops are dusted (discardDrop).
    // - Drops matching neither list are left in the drop window.
    //
    // Each list is comma-separated. An entry is a Name, a numeric ID, or
    // "Name:rarity". A bare Name/ID matches at any rarity (rarity is ignored for
    // regular items). The ":rarity" qualifier only applies to gems, e.g.
    //   Lucky Cape Gem:rare, Lucky Cape Gem:mythic, Blinding Light of Destiny
    public partial class MainWindowViewModel
    {
        // Drops to auto-loot into the inventory.
        [ObservableProperty]
        private string _keepListInput = "";

        // Drops to auto-dust.
        [ObservableProperty]
        private string _rejectListInput = "";

        // Strict whitelist: dust every drop NOT in the keep list. Disables the
        // reject box (it's redundant when everything unlisted is deleted).
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RejectEnabled))]
        private bool _deleteOthers;

        public bool RejectEnabled => !DeleteOthers;

        // Status feedback
        [ObservableProperty]
        private string _dropFilterStatus = "Ready";

        // Running state drives the Start/Stop toggle button
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FilterToggleLabel))]
        private bool _isFilterRunning;

        public string FilterToggleLabel => IsFilterRunning ? "Stop Filter" : "Start Filter";

        [RelayCommand]
        private void ToggleDropFilter()
        {
            // Stop: disable the filter on the agent but keep the UI config so it can be restarted.
            if (IsFilterRunning)
            {
                _connection.SendCommand("ClearDropFilter", null);
                IsFilterRunning = false;
                DropFilterStatus = "Stopped";
                return;
            }

            string keep = (KeepListInput ?? "").Trim();
            string reject = (RejectListInput ?? "").Trim();

            // "Delete others" without a keep list would dust the entire loot
            // window — almost certainly a mistake, so require a keep list for it.
            if (DeleteOthers && keep.Length == 0)
            {
                DropFilterStatus = "Add items to Keep before enabling 'delete all others'.";
                return;
            }

            if (keep.Length == 0 && reject.Length == 0 && !DeleteOthers)
            {
                DropFilterStatus = "Add at least one Keep or Reject entry first.";
                return;
            }

            var payload = new JObject
            {
                ["Keep"] = keep,
                ["Reject"] = DeleteOthers ? "" : reject,
                ["DeleteOthers"] = DeleteOthers,
            };

            _connection.SendCommand("ApplyDropFilter", payload);

            IsFilterRunning = true;
            DropFilterStatus = DeleteOthers
                ? $"Running. Keep ONLY: {keep} (deleting all others)"
                : $"Running. Keep: {(keep.Length > 0 ? keep : "(none)")} | Reject: {(reject.Length > 0 ? reject : "(none)")}";
        }

        [RelayCommand]
        private void ClearDropFilter()
        {
            _connection.SendCommand("ClearDropFilter", null);
            IsFilterRunning = false;
            KeepListInput = "";
            RejectListInput = "";
            DeleteOthers = false;
            DropFilterStatus = "Filter cleared";
        }
    }
}
