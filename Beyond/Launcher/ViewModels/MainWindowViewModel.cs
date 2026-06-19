using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using Launcher.Views;

namespace Launcher.ViewModels
{
    // One view-model per session: owns the named-pipe connection to that session's
    // game mod, mirrors mod settings, and exposes the state/commands every tool
    // window binds to. The feature-specific members live in the partial files
    // alongside this one (MainWindowViewModel.Spoofers.cs, .Autoskills.cs, etc.);
    // this file holds the connection lifecycle, the incoming-message dispatch, the
    // general control-panel state, and the tool-window launchers.
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly ModConnection _connection;
        private bool _isUpdatingFromMod;

        [ObservableProperty]
        private bool _isConnected;

        [ObservableProperty]
        private string _statusText = "Mod Not Loaded";

        [ObservableProperty]
        private string _statusColor = "#FF5252"; // Red

        // --- General control-panel toggles ---
        private bool _autoSkipCutscenes;
        public bool AutoSkipCutscenes
        {
            get => _autoSkipCutscenes;
            set => UpdateSetting(ref _autoSkipCutscenes, value, "autoSkipCutscenes");
        }

        private bool _isVsyncEnabled = true;
        public bool IsVsyncEnabled
        {
            get => _isVsyncEnabled;
            set
            {
                if (value)
                {
                    UncapFrames = false;
                }
                UpdateSetting(ref _isVsyncEnabled, value, "vsyncEnabled");
            }
        }

        private bool _uncapFrames;
        public bool UncapFrames
        {
            get => _uncapFrames;
            set
            {
                if (value)
                {
                    IsVsyncEnabled = false;
                }
                UpdateSetting(ref _uncapFrames, value, "uncapFrames");
            }
        }

        private bool _verticalSkillBar;
        public bool VerticalSkillBar
        {
            get => _verticalSkillBar;
            set => UpdateSetting(ref _verticalSkillBar, value, "verticalSkillBar");
        }

        private bool _hideUI;
        public bool HideUI
        {
            get => _hideUI;
            set => UpdateSetting(ref _hideUI, value, "hideUI");
        }

        private bool _hideOtherPlayers;
        public bool HideOtherPlayers
        {
            get => _hideOtherPlayers;
            set => UpdateSetting(ref _hideOtherPlayers, value, "hideOtherPlayers");
        }

        private bool _hideMonsters;
        public bool HideMonsters
        {
            get => _hideMonsters;
            set => UpdateSetting(ref _hideMonsters, value, "hideMonsters");
        }

        private bool _hideNPCs;
        public bool HideNPCs
        {
            get => _hideNPCs;
            set => UpdateSetting(ref _hideNPCs, value, "hideNPCs");
        }

        public ObservableCollection<string> GeneralLogs { get; } = new ObservableCollection<string>();

        // --- View slider & cutscene skipper ---
        private double _cameraZoom = 1.0;
        public double CameraZoom
        {
            get => _cameraZoom;
            set => UpdateSetting(ref _cameraZoom, value, "cameraZoom");
        }

        [RelayCommand]
        private void ResetCameraZoom()
        {
            CameraZoom = 1.0;
        }

        [RelayCommand]
        private void SkipCutscene()
        {
            _connection.SendCommand("SkipCutscene", null);
        }



        // --- Session metadata (managed by the shell) ---
        // Per-session pipe name. Bound to UnityWindowHost.PipeName (so the spawned
        // game's agent serves this exact pipe) and handed to the connection. Unique
        // per launcher session so multiple accounts coexist.
        public string SessionPipeName { get; } = "BeyondAgent_" + System.Guid.NewGuid().ToString("N");

        // Title shows in the session tab strip; IsSelected drives which session's
        // view is visible (all stay alive so every embedded game keeps running).
        [ObservableProperty] private string _title = "Session";
        [ObservableProperty] private bool _isSelected;

        public string? PresetUsername { get; set; }
        public string? PresetPassword { get; set; }
        // Configurator nickname for a predefined account. Passed to the game so the
        // mod pre-seeds the local name spoof when the player spawns.
        public string? PresetNickname { get; set; }

        private string? _gameDirectory;
        public string? GameDirectory
        {
            get => _gameDirectory;
            set
            {
                if (SetProperty(ref _gameDirectory, value))
                {
                    OnPropertyChanged(nameof(IsMelonLoaderDetected));
                    OnPropertyChanged(nameof(MelonLoaderStatusText));
                    OnPropertyChanged(nameof(MelonLoaderStatusColor));
                    OnPropertyChanged(nameof(IsControlPanelEnabled));
                }
            }
        }

        public bool IsControlPanelEnabled => !IsMelonLoaderDetected;

        public bool IsMelonLoaderDetected
        {
            get
            {
                try
                {
                    if (Launcher.GameLocator.TryResolveGameExe(GameDirectory, out string? gameExe) && gameExe != null)
                    {
                        string? gameDir = System.IO.Path.GetDirectoryName(gameExe);
                        if (gameDir != null)
                        {
                            return System.IO.Directory.Exists(System.IO.Path.Combine(gameDir, "MelonLoader")) ||
                                   System.IO.File.Exists(System.IO.Path.Combine(gameDir, "version.dll")) ||
                                   System.IO.File.Exists(System.IO.Path.Combine(gameDir, "MelonLoader", "MelonLoader.dll"));
                        }
                    }
                }
                catch
                {
                    // Ignore
                }
                return false;
            }
        }

        public string MelonLoaderStatusText => IsMelonLoaderDetected ? "MelonLoader Detected" : "MelonLoader Not Detected";
        public string MelonLoaderStatusColor => IsMelonLoaderDetected ? "#4CAF50" : "#FF5252";

        // Constructor
        public MainWindowViewModel()
        {
            _connection = new ModConnection(SessionPipeName);
            _connection.ConnectionStateChanged += OnConnectionStateChanged;
            _connection.StatusReceived += OnStatusReceived;
            _connection.LogReceived += OnLogReceived;
            _connection.SniffedPacketReceived += OnSniffedPacketReceived;
            _connection.InterceptedPacketReceived += OnInterceptedPacketReceived;
            _connection.QuestRunnerLogReceived += OnQuestRunnerLogReceived;
            _connection.ItemCatalogReceived += OnItemCatalogReceived;
            _connection.MusicCatalogReceived += OnMusicCatalogReceived;
            _connection.QuestDirectoryReceived += OnQuestDirectoryReceived;
            _connection.QuestChainsReceived += OnQuestChainsReceived;

            _connection.Start();
            AddLog("[Launcher] Starting connection service...");
        }

        private void UpdateSetting<T>(ref T field, T value, string name, [CallerMemberName] string? propertyName = null)
        {
            if (SetProperty(ref field, value, propertyName))
            {
                if (!_isUpdatingFromMod)
                {
                    _connection.SetSetting(name, value);
                }
            }
        }

        private void OnConnectionStateChanged(bool connected)
        {
            IsConnected = connected;
            StatusText = connected ? "Mod Loaded" : "Mod Not Loaded";
            StatusColor = connected ? "#4CAF50" : "#FF5252"; // Green / Red
            AddLog(connected ? "[Launcher] Connected to game mod agent." : "[Launcher] Lost connection to game mod agent.");
        }

        // Mirrors the full settings snapshot pushed by the mod into the matching
        // properties (which live across the feature partials). The guard flag stops
        // these assignments from echoing straight back to the mod.
        private void OnStatusReceived(JObject settings)
        {
            _isUpdatingFromMod = true;
            try
            {
                if (settings.TryGetValue("autoSkipCutscenes", out var val)) AutoSkipCutscenes = (bool)val;
                if (settings.TryGetValue("vsyncEnabled", out val)) IsVsyncEnabled = (bool)val;
                if (settings.TryGetValue("uncapFrames", out val)) UncapFrames = (bool)val;
                if (settings.TryGetValue("cameraZoom", out var valZoom)) CameraZoom = (double)valZoom;
                if (settings.TryGetValue("forceMergeShop", out val)) ForceMergeShop = (bool)val;
                if (settings.TryGetValue("autoskillsActive", out val)) AutoskillsActive = (bool)val;
                if (settings.TryGetValue("spoofedName", out val)) SpoofedName = (string?)val ?? "";
                if (settings.TryGetValue("helmSpoofActive", out val)) HelmSpoofActive = (bool)val;
                if (settings.TryGetValue("helmSpoofBundle", out val)) HelmSpoofBundle = (string?)val ?? "";
                if (settings.TryGetValue("armorSpoofActive", out val)) ArmorSpoofActive = (bool)val;
                if (settings.TryGetValue("armorSpoofBundle", out val)) ArmorSpoofBundle = (string?)val ?? "";
                if (settings.TryGetValue("backSpoofActive", out val)) BackSpoofActive = (bool)val;
                if (settings.TryGetValue("backSpoofBundle", out val)) BackSpoofBundle = (string?)val ?? "";
                if (settings.TryGetValue("weaponSpoofActive", out val)) WeaponSpoofActive = (bool)val;
                if (settings.TryGetValue("weaponSpoofBundle", out val)) WeaponSpoofBundle = (string?)val ?? "";
                if (settings.TryGetValue("petSpoofActive", out val)) PetSpoofActive = (bool)val;
                if (settings.TryGetValue("petSpoofBundle", out val)) PetSpoofBundle = (string?)val ?? "";
                if (settings.TryGetValue("monTransformActive", out val)) MonTransformActive = (bool)val;
                if (settings.TryGetValue("monTransformBundle", out val)) MonTransformBundle = (string?)val ?? "";
                if (settings.TryGetValue("petCombatAnimActive", out val)) PetCombatAnimActive = (bool)val;
                if (settings.TryGetValue("genderSpoofActive", out val)) GenderSpoofActive = (bool)val;
                if (settings.TryGetValue("snifferServerActive", out val)) SnifferServerActive = (bool)val;
                if (settings.TryGetValue("snifferClientActive", out val)) SnifferClientActive = (bool)val;
                if (settings.TryGetValue("interceptActive", out val)) InterceptActive = (bool)val;
                if (settings.TryGetValue("interceptorLoggingActive", out val)) InterceptorLoggingActive = (bool)val;
                if (settings.TryGetValue("retroAutoskillsActive", out val)) RetroAutoskillsActive = (bool)val;
                if (settings.TryGetValue("verticalSkillBar", out val)) VerticalSkillBar = (bool)val;
                if (settings.TryGetValue("hideUI", out val)) HideUI = (bool)val;
                if (settings.TryGetValue("hideOtherPlayers", out val)) HideOtherPlayers = (bool)val;
                if (settings.TryGetValue("hideMonsters", out val)) HideMonsters = (bool)val;
                if (settings.TryGetValue("hideNPCs", out val)) HideNPCs = (bool)val;

                if (settings.TryGetValue("playerAccessLevel", out val)) PlayerAccessLevel = (int)val;
                if (settings.TryGetValue("playerUpgradeDays", out val)) PlayerUpgradeDays = (int)val;
                if (settings.TryGetValue("isMember", out val)) IsMember = (bool)val;

                if (settings.TryGetValue("skillsetEditCombo", out val)) SkillsetEditCombo = (string?)val ?? "";
                if (settings.TryGetValue("skillsetEditName", out val)) SkillsetEditName = (string?)val ?? "";
                if (settings.TryGetValue("skillsetFileInput", out val)) SkillsetFileInput = (string?)val ?? "";
                if (settings.TryGetValue("skillsetImportExportText", out val)) SkillsetImportExportText = (string?)val ?? "";
                if (settings.TryGetValue("selectedSkillsetIndex", out val)) SelectedSkillsetIndex = (int)val;
                if (settings.TryGetValue("savedSkillsets", out val))
                {
                    var arr = val as JArray;
                    if (arr != null)
                    {
                        SavedSkillsets.Clear();
                        foreach (var token in arr)
                        {
                            var entry = token.ToObject<SkillsetEntry>();
                            if (entry != null) SavedSkillsets.Add(entry);
                        }
                    }
                }
                if (settings.TryGetValue("retroDelayInputs", out val))
                {
                    string? delStr = (string?)val;
                    if (!string.IsNullOrEmpty(delStr))
                    {
                        string[] parts = delStr.Split(',');
                        if (parts.Length > 0) _skill1Delay = parts[0];
                        if (parts.Length > 1) _skill2Delay = parts[1];
                        if (parts.Length > 2) _skill3Delay = parts[2];
                        if (parts.Length > 3) _skill4Delay = parts[3];
                        if (parts.Length > 4) _skill5Delay = parts[4];
                        OnPropertyChanged(nameof(Skill1Delay));
                        OnPropertyChanged(nameof(Skill2Delay));
                        OnPropertyChanged(nameof(Skill3Delay));
                        OnPropertyChanged(nameof(Skill4Delay));
                        OnPropertyChanged(nameof(Skill5Delay));
                    }
                }
                if (settings.TryGetValue("retroSkillWaits", out val))
                {
                    string? waitStr = (string?)val;
                    if (!string.IsNullOrEmpty(waitStr))
                    {
                        string[] parts = waitStr.Split(',');
                        if (parts.Length > 0) bool.TryParse(parts[0], out _skill1Wait);
                        if (parts.Length > 1) bool.TryParse(parts[1], out _skill2Wait);
                        if (parts.Length > 2) bool.TryParse(parts[2], out _skill3Wait);
                        if (parts.Length > 3) bool.TryParse(parts[3], out _skill4Wait);
                        if (parts.Length > 4) bool.TryParse(parts[4], out _skill5Wait);
                        OnPropertyChanged(nameof(Skill1Wait));
                        OnPropertyChanged(nameof(Skill2Wait));
                        OnPropertyChanged(nameof(Skill3Wait));
                        OnPropertyChanged(nameof(Skill4Wait));
                        OnPropertyChanged(nameof(Skill5Wait));
                    }
                }
                if (settings.TryGetValue("retroSkillFrees", out val))
                {
                    string? freeStr = (string?)val;
                    if (!string.IsNullOrEmpty(freeStr))
                    {
                        string[] parts = freeStr.Split(',');
                        if (parts.Length > 0) bool.TryParse(parts[0], out _skill1Free);
                        if (parts.Length > 1) bool.TryParse(parts[1], out _skill2Free);
                        if (parts.Length > 2) bool.TryParse(parts[2], out _skill3Free);
                        if (parts.Length > 3) bool.TryParse(parts[3], out _skill4Free);
                        if (parts.Length > 4) bool.TryParse(parts[4], out _skill5Free);
                        OnPropertyChanged(nameof(Skill1Free));
                        OnPropertyChanged(nameof(Skill2Free));
                        OnPropertyChanged(nameof(Skill3Free));
                        OnPropertyChanged(nameof(Skill4Free));
                        OnPropertyChanged(nameof(Skill5Free));
                    }
                }
            }
            finally
            {
                _isUpdatingFromMod = false;
            }
        }

        private void OnLogReceived(string message)
        {
            AddLog(message);
        }

        private void AddLog(string msg)
        {
            GeneralLogs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {msg}");
            if (GeneralLogs.Count > 100) GeneralLogs.RemoveAt(GeneralLogs.Count - 1);
        }

        public void Shutdown()
        {
            _connection.Stop();
        }

        // Broadcast passthroughs used by ShellViewModel to fan an action out to
        // every session ("work together").
        public void SetSettingExternal(string name, object value) => _connection.SetSetting(name, value);
        public void SendCommandExternal(string type) => _connection.SendCommand(type, null);

        // --- Floating tool windows ---
        // Each tool window is a singleton: opening it again re-focuses the live
        // instance, and closing it clears the cached reference.
        private readonly Dictionary<Type, Window> _toolWindows = new();

        private void ShowToolWindow<T>() where T : Window, new()
        {
            if (_toolWindows.TryGetValue(typeof(T), out var existing))
            {
                existing.Activate();
                return;
            }

            if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            {
                return;
            }

            var window = new T { DataContext = this };
            _toolWindows[typeof(T)] = window;
            window.Closed += (_, _) => _toolWindows.Remove(typeof(T));
            window.Show(desktop.MainWindow!);
        }

        [RelayCommand] private void OpenAutoskills() => ShowToolWindow<AutoskillsWindow>();
        [RelayCommand] private void OpenFakeDevWindow() => ShowToolWindow<FakeDevWindow>();
        [RelayCommand] private void OpenShopLoaderWindow() => ShowToolWindow<ShopLoaderWindow>();
        [RelayCommand] private void OpenQuestLoaderWindow() => ShowToolWindow<QuestLoaderWindow>();
        [RelayCommand] private void OpenInterceptorWindow() => ShowToolWindow<InterceptorWindow>();
        [RelayCommand] private void OpenSnifferWindow() => ShowToolWindow<SnifferWindow>();
        [RelayCommand] private void OpenSenderWindow() => ShowToolWindow<SenderWindow>();
        [RelayCommand] private void OpenReceiverWindow() => ShowToolWindow<ReceiverWindow>();
        [RelayCommand] private void OpenQuestRunnerWindow() => ShowToolWindow<QuestRunnerWindow>();
        [RelayCommand] private void OpenSpoofersWindow() => ShowToolWindow<SpoofersWindow>();
        [RelayCommand] private void OpenChainEditorWindow() => ShowToolWindow<ChainEditorWindow>();
    }
}
