using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Launcher.ViewModels
{
    public class AccountEntry
    {
        public string Username { get; set; } = "";
        public string Nickname { get; set; } = "";
        public string Password { get; set; } = "";
        // Shown in the account list so the real player ID stays hidden. Falls
        // back to Username for legacy entries saved before nicknames existed.
        public string DisplayText => string.IsNullOrWhiteSpace(Nickname) ? Username : Nickname;
    }

    public class LauncherConfig
    {
        public string GameDirectory { get; set; } = "";
        public System.Collections.Generic.List<AccountEntry> Accounts { get; set; } = new();
    }

    public partial class ConfiguratorViewModel : ObservableObject
    {
        public ObservableCollection<AccountEntry> Accounts { get; } = new();

        [ObservableProperty]
        private string _gameDirectory = "";

        [ObservableProperty]
        private string _newUsername = "";

        [ObservableProperty]
        private string _newNickname = "";

        [ObservableProperty]
        private string _newPassword = "";

        public event Action<string?, string?, string?>? OnLaunchRequested;
        public Func<System.Threading.Tasks.Task<string?>>? OnRequestFolderBrowse;
        // Shows a modal warning to the user (wired up by the view).
        public Action<string>? OnShowWarning;

        public ConfiguratorViewModel()
        {
            LoadAccounts();
        }

        [RelayCommand]
        private void AddAccount()
        {
            if (string.IsNullOrWhiteSpace(NewUsername)) return;

            Accounts.Add(new AccountEntry
            {
                Username = NewUsername.Trim(),
                Nickname = (NewNickname ?? "").Trim(),
                Password = NewPassword ?? ""
            });

            SaveAccounts();

            NewUsername = "";
            NewNickname = "";
            NewPassword = "";
        }

        [RelayCommand]
        private void RemoveAccount(AccountEntry account)
        {
            if (account != null)
            {
                Accounts.Remove(account);
                SaveAccounts();
            }
        }

        [RelayCommand]
        private void LaunchAccount(AccountEntry account)
        {
            if (account == null || !EnsureGameAvailable()) return;

            OnLaunchRequested?.Invoke(account.Username, account.Password, account.Nickname);
        }

        [RelayCommand]
        private void LaunchAll()
        {
            if (!EnsureGameAvailable()) return;

            foreach (var account in Accounts)
            {
                OnLaunchRequested?.Invoke(account.Username, account.Password, account.Nickname);
            }
        }

        // Verifies the game exists (configured directory or local game/ folder)
        // before launching. Warns the user and aborts when it cannot be found.
        private bool EnsureGameAvailable()
        {
            if (Launcher.GameLocator.Exists(GameDirectory))
            {
                return true;
            }

            OnShowWarning?.Invoke(Launcher.GameLocator.NotFoundMessage);
            return false;
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task BrowseDirectory()
        {
            if (OnRequestFolderBrowse != null)
            {
                var path = await OnRequestFolderBrowse();
                if (path != null)
                {
                    GameDirectory = path;
                    SaveAccounts();
                }
            }
        }

        private string GetConfigPath()
        {
            return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserData", "accounts.json");
        }

        private void LoadAccounts()
        {
            try
            {
                string path = GetConfigPath();
                if (System.IO.File.Exists(path))
                {
                    string json = System.IO.File.ReadAllText(path);
                    LauncherConfig? config = null;
                    try
                    {
                        config = Newtonsoft.Json.JsonConvert.DeserializeObject<LauncherConfig>(json);
                        // If it successfully loaded but the accounts array was null or empty, and it was actually a plain accounts list:
                        if (config != null && (config.Accounts == null || config.Accounts.Count == 0) && json.TrimStart().StartsWith("["))
                        {
                            config = null; // force fallback
                        }
                    }
                    catch
                    {
                        // Fallback
                    }

                    if (config == null)
                    {
                        try
                        {
                            var accountsList = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.List<AccountEntry>>(json);
                            if (accountsList != null)
                            {
                                config = new LauncherConfig
                                {
                                    GameDirectory = "",
                                    Accounts = accountsList
                                };
                            }
                        }
                        catch
                        {
                            // Ignore
                        }
                    }

                    if (config != null)
                    {
                        GameDirectory = config.GameDirectory ?? "";
                        Accounts.Clear();
                        if (config.Accounts != null)
                        {
                            foreach (var acc in config.Accounts)
                            {
                                Accounts.Add(acc);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Failed to load config: {ex.Message}");
            }
        }

        private void SaveAccounts()
        {
            try
            {
                string path = GetConfigPath();
                string? dir = System.IO.Path.GetDirectoryName(path);
                if (dir != null && !System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }
                var config = new LauncherConfig
                {
                    GameDirectory = GameDirectory ?? "",
                    Accounts = new System.Collections.Generic.List<AccountEntry>(Accounts)
                };
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(config, Newtonsoft.Json.Formatting.Indented);
                System.IO.File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Failed to save config: {ex.Message}");
            }
        }
    }
}
