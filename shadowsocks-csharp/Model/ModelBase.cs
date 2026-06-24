using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Shadowsocks.Model
{
    /// <summary>
    /// Base class for domain models that need change notification.
    /// Kept in the <see cref="Shadowsocks.Model"/> namespace so that model types
    /// do not carry a dependency on <see cref="Shadowsocks.ViewModel"/>.
    /// </summary>
    public abstract class ModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        protected virtual bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = @"")
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
