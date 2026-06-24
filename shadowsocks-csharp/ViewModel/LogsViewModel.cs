using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shadowsocks.Controller;
using System;
using System.IO;

namespace Shadowsocks.ViewModel
{
    /// <summary>
    /// Backing view-model for the logs page. Tails the current Shadowsocks log file
    /// (the same file <see cref="Logging.LogFile"/> the legacy LogWindow reads) and
    /// hands newly appended text to the view through the <see cref="NewTextRead"/> event.
    /// Toolbar state (auto-scroll / word-wrap / font size) lives here so the page stays
    /// a thin shell over the data.
    /// </summary>
    public partial class LogsViewModel : ObservableObject
    {
        /// <summary>When seeking to the tail of a large file, only read this many bytes back.</summary>
        private const int MaxReadSize = 65536;

        private string _currentLogFile;
        private long _currentOffset;

        /// <summary>Toolbar: keep the view pinned to the newest line as text arrives.</summary>
        [ObservableProperty] private bool _autoScroll = true;

        /// <summary>Toolbar: wrap long lines instead of horizontal scrolling.</summary>
        [ObservableProperty] private bool _wordWrap;

        /// <summary>Toolbar: log font size (slider/stepper bound).</summary>
        [ObservableProperty] private double _fontSize = 13;

        /// <summary>Name of the log file currently being shown, for the header.</summary>
        [ObservableProperty] private string _logFileName = string.Empty;

        /// <summary>
        /// Raised on the UI thread (the page owns the timer) with text newly appended to the
        /// log file. The <c>reset</c> flag is true when the whole view should be replaced
        /// (first read, file rolled over, or after a clear) rather than appended to.
        /// </summary>
        public event Action<string, bool> NewTextRead;

        /// <summary>
        /// Reads any text appended since the last call. On the first read (or when the log file
        /// path changes) only the tail (up to <see cref="MaxReadSize"/> bytes) is loaded so a very
        /// large log never blows up memory. Designed to be polled from a <c>DispatcherTimer</c>.
        /// </summary>
        public void Refresh()
        {
            var newLogFile = Logging.LogFile;
            if (string.IsNullOrEmpty(newLogFile))
            {
                return;
            }

            var reset = false;
            if (newLogFile != _currentLogFile)
            {
                _currentOffset = 0;
                _currentLogFile = newLogFile;
                LogFileName = Logging.LogFileName ?? string.Empty;
                reset = true;
            }

            try
            {
                if (!File.Exists(newLogFile))
                {
                    return;
                }

                using var reader = new StreamReader(
                    new FileStream(newLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

                if (_currentOffset == 0)
                {
                    var maxSize = reader.BaseStream.Length;
                    if (maxSize > MaxReadSize)
                    {
                        // Skip to the last MaxReadSize bytes, then drop the (likely partial) first line.
                        reader.BaseStream.Seek(-MaxReadSize, SeekOrigin.End);
                        reader.ReadLine();
                    }
                    reset = true;
                }
                else
                {
                    if (_currentOffset > reader.BaseStream.Length)
                    {
                        // File was truncated/rolled under us; start over from the tail.
                        _currentOffset = 0;
                        reset = true;
                        var maxSize = reader.BaseStream.Length;
                        if (maxSize > MaxReadSize)
                        {
                            reader.BaseStream.Seek(-MaxReadSize, SeekOrigin.End);
                            reader.ReadLine();
                        }
                    }
                    else
                    {
                        reader.BaseStream.Seek(_currentOffset, SeekOrigin.Begin);
                    }
                }

                var txt = reader.ReadToEnd();
                _currentOffset = reader.BaseStream.Position;

                if (reset || !string.IsNullOrEmpty(txt))
                {
                    NewTextRead?.Invoke(txt ?? string.Empty, reset);
                }
            }
            catch (FileNotFoundException)
            {
            }
            catch (IOException)
            {
                // File temporarily locked while being written/rotated; try again next tick.
            }
        }

        /// <summary>
        /// Toolbar command: clears the on-disk log (via <see cref="Logging.Clear"/>) and resets
        /// the read offset so the view empties. The view clears its own text box in response to
        /// the reset that the next <see cref="Refresh"/> raises; we also raise one immediately.
        /// </summary>
        [RelayCommand]
        private void Clear()
        {
            Logging.Clear();
            _currentOffset = 0;
            _currentLogFile = null;
            NewTextRead?.Invoke(string.Empty, true);
        }
    }
}
