using System.ComponentModel;

namespace BarkFluff.Client.WPF.Reactive
{
    public class ReactiveString : INotifyPropertyChanged, IDisposable
    {
        private string _value;
        private bool _disposed = false;

        public string Value
        {
            get => _value;
            set
            {
                if (_value != value)
                {
                    _value = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ReactiveString(string initialValue = "")
        {
            _value = initialValue;
        }

        protected virtual void OnPropertyChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    PropertyChanged = null;
                }
                _disposed = true;
            }
        }
    }
}
