using System.ComponentModel;

namespace BarkFluff.DBEditor.Models
{
    public class SavedAccount : INotifyPropertyChanged
    {
        private string _displayName = string.Empty;
        private string _host = string.Empty;
        private string _database = string.Empty;
        private string _username = string.Empty;
        private string _password = string.Empty;

        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (_displayName != value)
                {
                    _displayName = value;
                    OnPropertyChanged(nameof(DisplayName));
                    OnPropertyChanged(nameof(DisplayInfo));
                }
            }
        }

        public string Host
        {
            get => _host;
            set
            {
                if (_host != value)
                {
                    _host = value;
                    OnPropertyChanged(nameof(Host));
                    OnPropertyChanged(nameof(DisplayInfo));
                }
            }
        }

        public string Database
        {
            get => _database;
            set
            {
                if (_database != value)
                {
                    _database = value;
                    OnPropertyChanged(nameof(Database));
                    OnPropertyChanged(nameof(DisplayInfo));
                }
            }
        }

        public string Username
        {
            get => _username;
            set
            {
                if (_username != value)
                {
                    _username = value;
                    OnPropertyChanged(nameof(Username));
                    OnPropertyChanged(nameof(DisplayInfo));
                }
            }
        }

        public string Password
        {
            get => _password;
            set
            {
                if (_password != value)
                {
                    _password = value;
                    OnPropertyChanged(nameof(Password));
                }
            }
        }

        public DateTime LastUsed { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Computed display info for UI: "username@host:port/database"
        /// </summary>
        public string DisplayInfo => $"{Username}@{Host}/{Database}";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
