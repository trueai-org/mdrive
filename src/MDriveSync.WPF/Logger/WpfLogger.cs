using Microsoft.Extensions.Logging;

namespace MDriveSync.WPF
{
    public class WpfLogger : ILogger, IDisposable
    {
        private readonly string _categoryName;
        private readonly Action<string> _outputAction;

        public WpfLogger(string categoryName, Action<string> outputAction)
        {
            _categoryName = categoryName;
            _outputAction = outputAction;
        }

        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = $"{DateTime.Now} [{logLevel}] {_categoryName}: {formatter(state, exception)}";
            _outputAction?.Invoke(message);
        }

        public void Dispose()
        {
            // Dispose logic if needed
        }
    }

    public class WpfLoggerProvider : ILoggerProvider
    {
        private readonly Action<string> _outputAction;

        public WpfLoggerProvider(Action<string> outputAction)
        {
            _outputAction = outputAction;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new WpfLogger(categoryName, _outputAction);
        }

        public void Dispose()
        {
            // Dispose logic if needed
        }
    }

}
