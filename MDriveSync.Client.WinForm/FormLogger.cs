using Microsoft.Extensions.Logging;

namespace MDriveSync.Client.WinForm
{
    public class FormLogger : ILogger, IDisposable
    {
        private readonly string _categoryName;
        private readonly Action<string> _outputAction;

        public FormLogger(string categoryName, Action<string> outputAction)
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

    public class FormLoggerProvider : ILoggerProvider
    {
        private readonly Action<string> _outputAction;

        public FormLoggerProvider(Action<string> outputAction)
        {
            _outputAction = outputAction;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new FormLogger(categoryName, _outputAction);
        }

        public void Dispose()
        {
            // Dispose logic if needed
        }
    }

}
