using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Formatting.Json;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace JsonLogging
{
    class Program
    {
        public static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
              .Enrich.FromLogContext()
              .WriteTo.Console(new JsonFormatter(renderMessage:true))
              .CreateLogger();

            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    //.AddConsole(o =>
                    //{
                    //    o.FormatterName = "myjson";
                    //    //o.FormatterName = "json";
                    //    o.LogToStandardErrorThreshold = LogLevel.Trace;
                    //    //o.IncludeScopes = true;
                    //})
                    //.AddConsoleFormatter<MyJsonConsoleFormatter, JsonConsoleFormatterOptions>(o =>
                    //{
                    //    o.IncludeScopes = true;
                    //    //o.JsonWriterOptions = new JsonWriterOptions { Indented = false };
                    //    o.JsonWriterOptions = new JsonWriterOptions { Indented = true };
                    //    o.TimestampFormat = "o";
                    //})
                    .AddSerilog(dispose: true);
            });

            var logger = loggerFactory.CreateLogger<Program>();
            using (logger.BeginScope("This is a scope message with number: {CustomNumber}", 123))
            {
                logger.LogInformation("Random log message");
            }

            using (logger.BeginScope(new { Message = "Hello" }))
            using (logger.BeginScope(new KeyValuePair<string, string>("key", "value")))
            using (logger.BeginScope(new KeyValuePair<string, object>("anotherkey", "anothervalue")))
            using (logger.BeginScope(new Dictionary<string, object> { ["yetanotherkey"] = "yetanothervalue" }))
            using (logger.BeginScope("A string"))
            using (logger.BeginScope("This is a scope message with number: {CustomNumber}", 11123))
            using (logger.BeginScope("{AnotherNumber}{FinalNumber}", 2, 42))
            using (logger.BeginScope("{AnotherNumber}", 3))
            {
                logger.LogInformation(new Exception(), "exception message with {0} and {CustomNumber}", "stacktrace", 123);
            }

            using (logger.BeginScope("This is a scope message with a {ScopeNumber}", 2))
            {
                logger.LogInformation("This is a message with a {Number}", 1);
            }

            using (logger.BeginScope("{Number}", 2))
            using (logger.BeginScope("{AnotherNumber}", 3))
            {
                logger.LogInformation("{LogEntryNumber}", 1);
            }

            using (logger.BeginScope("{AnotherNumber}", 3))
            using (logger.BeginScope("{Number}", 2))
            {
                logger.LogInformation("{LogEntryNumber}", 1);
            }

            using (logger.BeginScope("{@m}", 3))
            using (logger.BeginScope("{{Message}}", 2))
            {
                logger.LogInformation("{LogEntryNumber}", 1);
            }

            logger.LogInformation("{LogEntryObject}", new {a=1, b=2 });
        }
    }

    // Below code is taken from https://github.com/dotnet/runtime/
    // Licensed to the .NET Foundation under one or more agreements.
    // The .NET Foundation licenses this file to you under the MIT license.

    public class CustomOptions : JsonConsoleFormatterOptions
    {
        public bool ExcludeNotes { get; set; }
        public bool DisableColors { get; set; }
    }

    public class MyJsonConsoleFormatter : ConsoleFormatter, IDisposable
    {
        private IDisposable _optionsReloadToken;

        public MyJsonConsoleFormatter(IOptionsMonitor<JsonConsoleFormatterOptions> options)
            : base("myjson")
        {
            ReloadLoggerOptions(options.CurrentValue);
            _optionsReloadToken = options.OnChange(ReloadLoggerOptions);
        }

        public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider scopeProvider, TextWriter textWriter)
        {
            string message = logEntry.Formatter(logEntry.State, logEntry.Exception);
            if (logEntry.Exception == null && message == null)
            {
                return;
            }
            LogLevel logLevel = logEntry.LogLevel;
            string category = logEntry.Category;
            int eventId = logEntry.EventId.Id;
            Exception exception = logEntry.Exception;
            const int DefaultBufferSize = 1024;
            using (var output = new PooledByteBufferWriter(DefaultBufferSize))
            {
                using (var writer = new Utf8JsonWriter(output, FormatterOptions.JsonWriterOptions))
                {
                    writer.WriteStartObject();
                    var timestampFormat = FormatterOptions.TimestampFormat;
                    if (timestampFormat != null)
                    {
                        var dateTime = FormatterOptions.UseUtcTimestamp ? DateTime.UtcNow : DateTime.Now;
                        writer.WriteString("Timestamp", dateTime.ToString(timestampFormat));
                    }
                    writer.WriteNumber(nameof(logEntry.EventId), eventId);
                    writer.WriteString(nameof(logEntry.LogLevel), GetLogLevelString(logLevel));
                    writer.WriteString(nameof(logEntry.Category), category);
                    writer.WriteString("Message", message);

                    if (exception != null)
                    {
                        writer.WriteStartObject(nameof(Exception));
                        writer.WriteString(nameof(exception.Message), exception.Message.ToString());
                        writer.WriteString("Type", exception.GetType().ToString());
                        writer.WriteStartArray(nameof(exception.StackTrace));
                        string stackTrace = exception?.StackTrace;
                        if (stackTrace != null)
                        {
#if NETCOREAPP
                            foreach (var stackTraceLines in stackTrace?.Split(Environment.NewLine))
#else
                            foreach (var stackTraceLines in stackTrace?.Split(new string[] { Environment.NewLine }, StringSplitOptions.None))
#endif
                            {
                                writer.WriteStringValue(stackTraceLines);
                            }
                        }
                        writer.WriteEndArray();
                        writer.WriteNumber(nameof(exception.HResult), exception.HResult);
                        writer.WriteEndObject();
                    }

                    if (logEntry.State is IReadOnlyCollection<KeyValuePair<string, object>> stateDictionary)
                    {
                        writer.WriteStartObject(nameof(logEntry.State));
                        foreach (KeyValuePair<string, object> item in stateDictionary)
                        {
                            writer.WriteString(item.Key, ToInvariantString(item.Value));
                        }
                        writer.WriteEndObject();
                    }
                    else if (logEntry.State != null)
                    {
                        writer.WriteString(nameof(logEntry.State), logEntry.State.ToString());
                    }
                    WriteScopeInformation(writer, scopeProvider);
                    writer.WriteEndObject();
                    writer.Flush();
                }
#if NETCOREAPP
                textWriter.Write(Encoding.UTF8.GetString(output.WrittenMemory.Span));
#else
                textWriter.Write(Encoding.UTF8.GetString(output.WrittenMemory.Span.ToArray()));
#endif
            }
            textWriter.Write(Environment.NewLine);
        }

        private static string GetLogLevelString(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Trace => "Trace",
                LogLevel.Debug => "Debug",
                LogLevel.Information => "Information",
                LogLevel.Warning => "Warning",
                LogLevel.Error => "Error",
                LogLevel.Critical => "Critical",
                _ => throw new ArgumentOutOfRangeException(nameof(logLevel))
            };
        }

        private void WriteScopeInformation(Utf8JsonWriter writer, IExternalScopeProvider scopeProvider)
        {
            if (FormatterOptions.IncludeScopes && scopeProvider != null)
            {
                writer.WriteStartArray("Scopes");
                scopeProvider.ForEachScope((scope, state) =>
                {
                    if (scope is IReadOnlyCollection<KeyValuePair<string, object>> scopes)
                    {
                        state.WriteStartObject();
                        foreach (KeyValuePair<string, object> item in scopes)
                        {
                            state.WriteString(item.Key, ToInvariantString(item.Value));
                        }
                        state.WriteEndObject();
                    }
                    else
                    {
                        state.WriteStringValue(ToInvariantString(scope));
                    }
                }, writer);
                writer.WriteEndArray();
            }
        }

        private static string ToInvariantString(object obj) => Convert.ToString(obj, CultureInfo.InvariantCulture);

        internal JsonConsoleFormatterOptions FormatterOptions { get; set; }

        private void ReloadLoggerOptions(JsonConsoleFormatterOptions options)
        {
            FormatterOptions = options;
        }

        public void Dispose()
        {
            _optionsReloadToken?.Dispose();
        }
    }

    internal sealed class PooledByteBufferWriter : IBufferWriter<byte>, IDisposable
    {
        private byte[] _rentedBuffer;
        private int _index;

        private const int MinimumBufferSize = 256;

        public PooledByteBufferWriter(int initialCapacity)
        {
            Debug.Assert(initialCapacity > 0);

            _rentedBuffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
            _index = 0;
        }

        public ReadOnlyMemory<byte> WrittenMemory
        {
            get
            {
                Debug.Assert(_rentedBuffer != null);
                Debug.Assert(_index <= _rentedBuffer.Length);
                return _rentedBuffer.AsMemory(0, _index);
            }
        }

        public int WrittenCount
        {
            get
            {
                Debug.Assert(_rentedBuffer != null);
                return _index;
            }
        }

        public int Capacity
        {
            get
            {
                Debug.Assert(_rentedBuffer != null);
                return _rentedBuffer.Length;
            }
        }

        public int FreeCapacity
        {
            get
            {
                Debug.Assert(_rentedBuffer != null);
                return _rentedBuffer.Length - _index;
            }
        }

        public void Clear()
        {
            ClearHelper();
        }

        private void ClearHelper()
        {
            Debug.Assert(_rentedBuffer != null);
            Debug.Assert(_index <= _rentedBuffer.Length);

            _rentedBuffer.AsSpan(0, _index).Clear();
            _index = 0;
        }

        // Returns the rented buffer back to the pool
        public void Dispose()
        {
            if (_rentedBuffer == null)
            {
                return;
            }

            ClearHelper();
            ArrayPool<byte>.Shared.Return(_rentedBuffer);
            _rentedBuffer = null!;
        }

        public void Advance(int count)
        {
            Debug.Assert(_rentedBuffer != null);
            Debug.Assert(count >= 0);
            Debug.Assert(_index <= _rentedBuffer.Length - count);

            _index += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            CheckAndResizeBuffer(sizeHint);
            return _rentedBuffer.AsMemory(_index);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            CheckAndResizeBuffer(sizeHint);
            return _rentedBuffer.AsSpan(_index);
        }

#if BUILDING_INBOX_LIBRARY
        internal ValueTask WriteToStreamAsync(Stream destination, CancellationToken cancellationToken)
        {
            return destination.WriteAsync(WrittenMemory, cancellationToken);
        }
#else
        internal Task WriteToStreamAsync(Stream destination, CancellationToken cancellationToken)
        {
            return destination.WriteAsync(_rentedBuffer, 0, _index, cancellationToken);
        }
#endif

        private void CheckAndResizeBuffer(int sizeHint)
        {
            Debug.Assert(_rentedBuffer != null);
            Debug.Assert(sizeHint >= 0);

            if (sizeHint == 0)
            {
                sizeHint = MinimumBufferSize;
            }

            int availableSpace = _rentedBuffer.Length - _index;

            if (sizeHint > availableSpace)
            {
                int currentLength = _rentedBuffer.Length;
                int growBy = Math.Max(sizeHint, currentLength);

                int newSize = currentLength + growBy;

                if ((uint)newSize > int.MaxValue)
                {
                    newSize = currentLength + sizeHint;
                    if ((uint)newSize > int.MaxValue)
                    {
                        throw new OutOfMemoryException(Convert.ToString((uint)newSize));
                    }
                }

                byte[] oldBuffer = _rentedBuffer;

                _rentedBuffer = ArrayPool<byte>.Shared.Rent(newSize);

                Debug.Assert(oldBuffer.Length >= _index);
                Debug.Assert(_rentedBuffer.Length >= _index);

                Span<byte> previousBuffer = oldBuffer.AsSpan(0, _index);
                previousBuffer.CopyTo(_rentedBuffer);
                previousBuffer.Clear();
                ArrayPool<byte>.Shared.Return(oldBuffer);
            }

            Debug.Assert(_rentedBuffer.Length - _index > 0);
            Debug.Assert(_rentedBuffer.Length - _index >= sizeHint);
        }
    }

    
}
