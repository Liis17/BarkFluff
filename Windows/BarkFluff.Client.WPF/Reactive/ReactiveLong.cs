using System.ComponentModel;

namespace BarkFluff.Client.WPF.Reactive
{
    public class ReactiveLong : INotifyPropertyChanged, IDisposable
    {
        private long _value;
        private bool _disposed = false;

        public long Value
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

        public ReactiveLong(long initialValue = 0)
        {
            _value = initialValue;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

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
