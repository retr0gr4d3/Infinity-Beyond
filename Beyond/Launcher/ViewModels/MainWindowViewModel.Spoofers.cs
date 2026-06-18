using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;

namespace Launcher.ViewModels
{
    // SpoofersWindow: local cosmetic spoofs (name, gear, transform, pets) plus the
    // catalogs that feed the pickers and the BGM jukebox.
    public partial class MainWindowViewModel
    {
        // --- Cosmetic toggles ---
        private bool _helmSpoofActive;
        public bool HelmSpoofActive
        {
            get => _helmSpoofActive;
            set => UpdateSetting(ref _helmSpoofActive, value, "helmSpoofActive");
        }

        private bool _armorSpoofActive;
        public bool ArmorSpoofActive
        {
            get => _armorSpoofActive;
            set => UpdateSetting(ref _armorSpoofActive, value, "armorSpoofActive");
        }

        private bool _backSpoofActive;
        public bool BackSpoofActive
        {
            get => _backSpoofActive;
            set => UpdateSetting(ref _backSpoofActive, value, "backSpoofActive");
        }

        private bool _weaponSpoofActive;
        public bool WeaponSpoofActive
        {
            get => _weaponSpoofActive;
            set => UpdateSetting(ref _weaponSpoofActive, value, "weaponSpoofActive");
        }

        private bool _petSpoofActive;
        public bool PetSpoofActive
        {
            get => _petSpoofActive;
            set => UpdateSetting(ref _petSpoofActive, value, "petSpoofActive");
        }

        private bool _monTransformActive;
        public bool MonTransformActive
        {
            get => _monTransformActive;
            set => UpdateSetting(ref _monTransformActive, value, "monTransformActive");
        }

        private bool _petCombatAnimActive;
        public bool PetCombatAnimActive
        {
            get => _petCombatAnimActive;
            set => UpdateSetting(ref _petCombatAnimActive, value, "petCombatAnimActive");
        }

        private bool _genderSpoofActive;
        public bool GenderSpoofActive
        {
            get => _genderSpoofActive;
            set => UpdateSetting(ref _genderSpoofActive, value, "genderSpoofActive");
        }

        // --- Bundle / name fields ---
        [ObservableProperty] private string _spoofedName = "";
        [ObservableProperty] private string _helmSpoofBundle = "";
        [ObservableProperty] private string _armorSpoofBundle = "";
        [ObservableProperty] private string _backSpoofBundle = "";
        [ObservableProperty] private string _weaponSpoofBundle = "";
        [ObservableProperty] private string _petSpoofBundle = "";
        [ObservableProperty] private string _monTransformBundle = "";

        [ObservableProperty] private string _jukeboxTrackId = "1";

        // --- Apply commands ---
        [RelayCommand]
        private void ApplySpoofedName()
        {
            // Name spoofing is always active: the mod renames whenever a non-empty
            // name is set, and clears it when blank. No enable toggle.
            _connection.SetSetting("spoofedName", SpoofedName);
        }

        [RelayCommand]
        private void ApplyHelmSpoof()
        {
            _connection.SetSetting("helmSpoofBundle", HelmSpoofBundle);
        }

        [RelayCommand]
        private void ApplyArmorSpoof()
        {
            _connection.SetSetting("armorSpoofBundle", ArmorSpoofBundle);
        }

        [RelayCommand]
        private void ApplyBackSpoof()
        {
            _connection.SetSetting("backSpoofBundle", BackSpoofBundle);
        }

        [RelayCommand]
        private void ApplyWeaponSpoof()
        {
            _connection.SetSetting("weaponSpoofBundle", WeaponSpoofBundle);
        }

        [RelayCommand]
        private void ApplyPetSpoof()
        {
            _connection.SetSetting("petSpoofBundle", PetSpoofBundle);
        }

        [RelayCommand]
        private void ApplyMonTransform()
        {
            _connection.SetSetting("monTransformBundle", MonTransformBundle);
        }

        // --- Jukebox ---
        [RelayCommand]
        private void PlayJukebox()
        {
            if (int.TryParse(JukeboxTrackId, out int id))
            {
                _connection.SendCommand("PlayJukebox", new JObject { ["TrackId"] = id });
            }
        }

        [RelayCommand]
        private void StopJukebox()
        {
            _connection.SendCommand("PlayJukebox", new JObject { ["TrackId"] = 0 }); // 0 in our logic triggers Stop
        }

        [RelayCommand]
        private void RestoreAreaBGM()
        {
            _connection.SendCommand("RestoreAreaBGM", null);
        }

        // --- Catalogs ---
        public ObservableCollection<CatalogEntry> HelmsCatalog { get; } = new();
        public ObservableCollection<CatalogEntry> ArmorsCatalog { get; } = new();
        public ObservableCollection<CatalogEntry> BacksCatalog { get; } = new();
        public ObservableCollection<CatalogEntry> WeaponsCatalog { get; } = new();
        public ObservableCollection<CatalogEntry> PetsCatalog { get; } = new();
        public ObservableCollection<CatalogEntry> MonstersCatalog { get; } = new();
        public ObservableCollection<JukeboxTrack> JukeboxTracks { get; } = new();

        [ObservableProperty] private CatalogEntry? _selectedHelmCatalog;
        [ObservableProperty] private CatalogEntry? _selectedArmorCatalog;
        [ObservableProperty] private CatalogEntry? _selectedBackCatalog;
        [ObservableProperty] private CatalogEntry? _selectedWeaponCatalog;
        [ObservableProperty] private CatalogEntry? _selectedPetCatalog;
        [ObservableProperty] private CatalogEntry? _selectedMonsterCatalog;
        [ObservableProperty] private JukeboxTrack? _selectedJukeboxTrack;

        partial void OnSelectedHelmCatalogChanged(CatalogEntry? value) { if (value != null) HelmSpoofBundle = value.Bundle; }
        partial void OnSelectedArmorCatalogChanged(CatalogEntry? value) { if (value != null) ArmorSpoofBundle = value.Bundle; }
        partial void OnSelectedBackCatalogChanged(CatalogEntry? value) { if (value != null) BackSpoofBundle = value.Bundle; }
        partial void OnSelectedWeaponCatalogChanged(CatalogEntry? value) { if (value != null) WeaponSpoofBundle = value.Bundle; }
        partial void OnSelectedPetCatalogChanged(CatalogEntry? value) { if (value != null) PetSpoofBundle = value.Bundle; }
        partial void OnSelectedMonsterCatalogChanged(CatalogEntry? value) { if (value != null) MonTransformBundle = value.Bundle; }
        partial void OnSelectedJukeboxTrackChanged(JukeboxTrack? value) { if (value != null) JukeboxTrackId = value.Id.ToString(); }

        private void OnItemCatalogReceived(JObject msg)
        {
            PopulateCatalog(msg["Helms"], HelmsCatalog);
            PopulateCatalog(msg["Armors"], ArmorsCatalog);
            PopulateCatalog(msg["Backs"], BacksCatalog);
            PopulateCatalog(msg["Weapons"], WeaponsCatalog);
            PopulateCatalog(msg["Pets"], PetsCatalog);
            PopulateCatalog(msg["Monsters"], MonstersCatalog);
        }

        private void PopulateCatalog(JToken? token, ObservableCollection<CatalogEntry> collection)
        {
            collection.Clear();
            if (token is JArray arr)
            {
                foreach (var t in arr)
                {
                    collection.Add(new CatalogEntry
                    {
                        Name = (string?)t["name"] ?? "",
                        Bundle = (string?)t["bundle"] ?? ""
                    });
                }
            }
        }

        private void OnMusicCatalogReceived(JObject msg)
        {
            JukeboxTracks.Clear();
            if (msg["Tracks"] is JArray arr)
            {
                foreach (var t in arr)
                {
                    JukeboxTracks.Add(new JukeboxTrack
                    {
                        Id = (int?)t["id"] ?? 0,
                        Name = (string?)t["name"] ?? "",
                        Length = (float?)t["length"] ?? 0f
                    });
                }
            }
        }
    }
}
