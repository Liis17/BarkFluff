using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;

namespace BarkFluff.Client.WPF.Reactive
{
    public class ReactiveBool : INotifyPropertyChanged, IDisposable
    {
        private bool _value;
        private bool _disposed = false;

        public bool Value
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

        public ReactiveBool(bool initialValue = false)
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
