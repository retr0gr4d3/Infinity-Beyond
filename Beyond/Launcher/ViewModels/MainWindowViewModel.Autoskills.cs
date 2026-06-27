using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;

namespace Launcher.ViewModels
{
    // AutoskillsWindow: the autoskill engine — enable toggles, the
    // saved-skillset manager, and per-key delay / wait / free configuration.
    public partial class MainWindowViewModel
    {
        // --- Enable toggles ---
        public bool AutoskillsActive
        {
            get;
            set => UpdateSetting(ref field, value, "autoskillsActive");
        }

        // --- Skillset manager ---
        public string SkillsetEditCombo
        {
            get;
            set => UpdateSetting(ref field, value, "skillsetEditCombo");
        } = "";
        public string SkillsetEditName
        {
            get;
            set => UpdateSetting(ref field, value, "skillsetEditName");
        } = "";
        public string SkillsetFileInput
        {
            get;
            set => UpdateSetting(ref field, value, "skillsetFileInput");
        } = "";
        public string SkillsetImportExportText
        {
            get;
            set => UpdateSetting(ref field, value, "skillsetImportExportText");
        } = "";

        public int SelectedSkillsetIndex
        {
            get;
            set
            {
                if (SetProperty(ref field, value) && !_isUpdatingFromMod)
                {
                    _connection.SendCommand("SelectSkillset", new JObject { ["Index"] = value });
                }
            }
        } = -1;

        public ObservableCollection<SkillsetEntry> SavedSkillsets { get; } = [];

        // --- Per-key delay ---
        private string _skill1Delay = "1000";
        public string Skill1Delay
        {
            get => _skill1Delay;
            set
            {
                if (SetProperty(ref _skill1Delay, value) && !_isUpdatingFromMod)
                {
                    SendDelaysUpdate();
                }
            }
        }

        private string _skill2Delay = "1000";
        public string Skill2Delay
        {
            get => _skill2Delay;
            set
            {
                if (SetProperty(ref _skill2Delay, value) && !_isUpdatingFromMod)
                {
                    SendDelaysUpdate();
                }
            }
        }

        private string _skill3Delay = "1000";
        public string Skill3Delay
        {
            get => _skill3Delay;
            set
            {
                if (SetProperty(ref _skill3Delay, value) && !_isUpdatingFromMod)
                {
                    SendDelaysUpdate();
                }
            }
        }

        private string _skill4Delay = "1000";
        public string Skill4Delay
        {
            get => _skill4Delay;
            set
            {
                if (SetProperty(ref _skill4Delay, value) && !_isUpdatingFromMod)
                {
                    SendDelaysUpdate();
                }
            }
        }

        private string _skill5Delay = "1000";
        public string Skill5Delay
        {
            get => _skill5Delay;
            set
            {
                if (SetProperty(ref _skill5Delay, value) && !_isUpdatingFromMod)
                {
                    SendDelaysUpdate();
                }
            }
        }

        private void SendDelaysUpdate()
        {
            string consolidated = $"{Skill1Delay},{Skill2Delay},{Skill3Delay},{Skill4Delay},{Skill5Delay}";
            _connection.SetSetting("delayInputs", consolidated);
        }

        // --- Per-key wait (mutually exclusive with free) ---
        private bool _skill1Wait;
        public bool Skill1Wait
        {
            get => _skill1Wait;
            set
            {
                if (value && _skill1Free)
                {
                    _skill1Free = false;
                    OnPropertyChanged(nameof(Skill1Free));
                    SendFreesUpdate();
                }
                if (SetProperty(ref _skill1Wait, value) && !_isUpdatingFromMod)
                {
                    SendWaitsUpdate();
                }
            }
        }

        private bool _skill2Wait;
        public bool Skill2Wait
        {
            get => _skill2Wait;
            set
            {
                if (value && _skill2Free)
                {
                    _skill2Free = false;
                    OnPropertyChanged(nameof(Skill2Free));
                    SendFreesUpdate();
                }
                if (SetProperty(ref _skill2Wait, value) && !_isUpdatingFromMod)
                {
                    SendWaitsUpdate();
                }
            }
        }

        private bool _skill3Wait;
        public bool Skill3Wait
        {
            get => _skill3Wait;
            set
            {
                if (value && _skill3Free)
                {
                    _skill3Free = false;
                    OnPropertyChanged(nameof(Skill3Free));
                    SendFreesUpdate();
                }
                if (SetProperty(ref _skill3Wait, value) && !_isUpdatingFromMod)
                {
                    SendWaitsUpdate();
                }
            }
        }

        private bool _skill4Wait;
        public bool Skill4Wait
        {
            get => _skill4Wait;
            set
            {
                if (value && _skill4Free)
                {
                    _skill4Free = false;
                    OnPropertyChanged(nameof(Skill4Free));
                    SendFreesUpdate();
                }
                if (SetProperty(ref _skill4Wait, value) && !_isUpdatingFromMod)
                {
                    SendWaitsUpdate();
                }
            }
        }

        private bool _skill5Wait;
        public bool Skill5Wait
        {
            get => _skill5Wait;
            set
            {
                if (value && _skill5Free)
                {
                    _skill5Free = false;
                    OnPropertyChanged(nameof(Skill5Free));
                    SendFreesUpdate();
                }
                if (SetProperty(ref _skill5Wait, value) && !_isUpdatingFromMod)
                {
                    SendWaitsUpdate();
                }
            }
        }

        private void SendWaitsUpdate()
        {
            string consolidated = $"{Skill1Wait},{Skill2Wait},{Skill3Wait},{Skill4Wait},{Skill5Wait}";
            _connection.SetSetting("skillWaits", consolidated);
        }

        // --- Per-key free (mutually exclusive with wait) ---
        private bool _skill1Free;
        public bool Skill1Free
        {
            get => _skill1Free;
            set
            {
                if (value && _skill1Wait)
                {
                    _skill1Wait = false;
                    OnPropertyChanged(nameof(Skill1Wait));
                    SendWaitsUpdate();
                }
                if (SetProperty(ref _skill1Free, value) && !_isUpdatingFromMod)
                {
                    SendFreesUpdate();
                }
            }
        }

        private bool _skill2Free;
        public bool Skill2Free
        {
            get => _skill2Free;
            set
            {
                if (value && _skill2Wait)
                {
                    _skill2Wait = false;
                    OnPropertyChanged(nameof(Skill2Wait));
                    SendWaitsUpdate();
                }
                if (SetProperty(ref _skill2Free, value) && !_isUpdatingFromMod)
                {
                    SendFreesUpdate();
                }
            }
        }

        private bool _skill3Free;
        public bool Skill3Free
        {
            get => _skill3Free;
            set
            {
                if (value && _skill3Wait)
                {
                    _skill3Wait = false;
                    OnPropertyChanged(nameof(Skill3Wait));
                    SendWaitsUpdate();
                }
                if (SetProperty(ref _skill3Free, value) && !_isUpdatingFromMod)
                {
                    SendFreesUpdate();
                }
            }
        }

        private bool _skill4Free;
        public bool Skill4Free
        {
            get => _skill4Free;
            set
            {
                if (value && _skill4Wait)
                {
                    _skill4Wait = false;
                    OnPropertyChanged(nameof(Skill4Wait));
                    SendWaitsUpdate();
                }
                if (SetProperty(ref _skill4Free, value) && !_isUpdatingFromMod)
                {
                    SendFreesUpdate();
                }
            }
        }

        private bool _skill5Free;
        public bool Skill5Free
        {
            get => _skill5Free;
            set
            {
                if (value && _skill5Wait)
                {
                    _skill5Wait = false;
                    OnPropertyChanged(nameof(Skill5Wait));
                    SendWaitsUpdate();
                }
                if (SetProperty(ref _skill5Free, value) && !_isUpdatingFromMod)
                {
                    SendFreesUpdate();
                }
            }
        }

        private void SendFreesUpdate()
        {
            string consolidated = $"{Skill1Free},{Skill2Free},{Skill3Free},{Skill4Free},{Skill5Free}";
            _connection.SetSetting("skillFrees", consolidated);
        }

        // --- Skillset file / import-export commands ---
        [RelayCommand]
        private void SaveSkillset()
        {
            _connection.SendCommand("SaveSkillset", null);
        }

        [RelayCommand]
        private void DeleteSkillset()
        {
            _connection.SendCommand("DeleteSkillset", null);
        }

        [RelayCommand]
        private void ImportSkillset()
        {
            if (!string.IsNullOrEmpty(SkillsetImportExportText))
            {
                _connection.SendCommand("ImportSkillset", new JObject { ["Payload"] = SkillsetImportExportText });
            }
        }

        [RelayCommand]
        private void ExportSkillset()
        {
            _connection.SendCommand("ExportSkillset", null);
        }

        [RelayCommand]
        private void LoadSkillsetFile()
        {
            _connection.SendCommand("LoadSkillsetFile", null);
        }

        [RelayCommand]
        private void SaveSkillsetFile()
        {
            _connection.SendCommand("SaveSkillsetFile", null);
        }
    }
}
