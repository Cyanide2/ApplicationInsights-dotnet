﻿namespace Microsoft.ApplicationInsights.Extensibility.Implementation.Tracing.DiagnosticsModule
{
    using System;
    using System.Diagnostics;
    using System.IO;

    using Microsoft.ApplicationInsights.Common.Extensions;

    using static System.FormattableString;

    /// <summary>
    /// This sender works with the DiagnosticTelemetryModule. This will subscribe to events and output to a text file log.
    /// </summary>
    internal class FileDiagnosticsSender : IDiagnosticsSender, IDisposable
    {
        private readonly object lockObj = new object();
        private readonly string logFileName = FileHelper.GenerateFileName();
        private readonly DefaultTraceListener defaultTraceListener = new DefaultTraceListener();

        private string logDirectory = Environment.ExpandEnvironmentVariables("%TEMP%");
        private bool isEnabled = false; // TODO: NEED MORE PERFORMANT FILE WRITTER BEFORE ENABLING THIS BY DEFAULT
        private bool disposedValue;

        public FileDiagnosticsSender()
        {
            this.SetAndValidateLogsFolder(this.LogDirectory, this.logFileName);
        }

        public string LogDirectory 
        {
            get => this.logDirectory;
            set
            {
                if (!this.IsSetByEnvironmentVariable && this.SetAndValidateLogsFolder(value, this.logFileName))
                {
                    this.logDirectory = value;
                }
            }
        }

        public bool Enabled 
        { 
            get => this.isEnabled;
            set => this.isEnabled = this.IsSetByEnvironmentVariable ? this.isEnabled : value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether this class was configured via Environment Variable.
        /// If this class is set by the environment variable, lock the other properties to prevent TelemetryConfigurationFactory or customer code from overriding.
        /// We are enabling SysAdmins or DevOps to be able to override this behavior via Environment Variable.
        /// </summary>
        internal bool IsSetByEnvironmentVariable { get; set; }

        /// <summary>
        /// Gets or sets the log file path.
        /// </summary>
        private string LogFilePath 
        {
            get => this.defaultTraceListener.LogFileName;
            set => this.defaultTraceListener.LogFileName = value;
        }

        /// <summary>
        /// Write a trace to file.
        /// </summary>
        /// <param name="eventData">TraceEvent to be written to file.</param>
        public void Send(TraceEvent eventData)
        {
            if (this.Enabled)
            {
                //// We previously depended on TraceSource & DefaultTraceListener for writing to file. 
                //// This has some overhead, but the path we were utilizing calls a lock and uses a StreamWriter.
                //// I've copied the implementation below.
                //// https://referencesource.microsoft.com/#System/compmod/system/diagnostics/TraceSource.cs,260
                //// https://referencesource.microsoft.com/#System/compmod/system/diagnostics/DefaultTraceListener.cs,204

                var message = Invariant($"{DateTime.UtcNow.ToInvariantString("o")}: {eventData.MetaData.Level}: {eventData}");

                lock (this.lockObj)
                {
                    try
                    {
                        FileInfo file = new FileInfo(this.LogFilePath);
                        using (Stream stream = file.Open(FileMode.OpenOrCreate))
                        {
                            using (StreamWriter writer = new StreamWriter(stream))
                            {
                                stream.Position = stream.Length;
                                writer.WriteLine(message);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // no op
                    }
                }
            }
        }

        public void Dispose()
        {
            this.Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposedValue)
            {
                if (disposing)
                {
                    this.defaultTraceListener.Dispose();
                }

                this.disposedValue = true;
            }
        }

        private static void WriteFileHeader(string logFilePath)
        {
            string[] lines =
            {
                // this.SelfDiagnosticsConfig,
                ".NET SDK version: " + SdkVersionUtils.GetSdkVersion(string.Empty),
                string.Empty,
            };

            System.IO.File.WriteAllLines(logFilePath, lines);
        }

        private bool SetAndValidateLogsFolder(string fileDirectory, string fileName)
        {
            bool result = false;
            try
            {
                if (!string.IsNullOrWhiteSpace(fileDirectory) && !string.IsNullOrWhiteSpace(fileName))
                {
                    // Validate
                    string expandedDirectory = Environment.ExpandEnvironmentVariables(fileDirectory);
                    var logsDirectory = new DirectoryInfo(expandedDirectory);
                    FileHelper.TestDirectoryPermissions(logsDirectory);

                    string fullLogFileName = Path.Combine(expandedDirectory, fileName);

                    // Set
                    this.LogFilePath = fullLogFileName;
                    WriteFileHeader(fullLogFileName);

                    result = true;
                }
            }
            catch (Exception ex)
            {
                // NotSupportedException: The given path's format is not supported
                // UnauthorizedAccessException
                // ArgumentException: // Path does not specify a valid file path or contains invalid DirectoryInfo characters.
                // DirectoryNotFoundException: The specified path is invalid, such as being on an unmapped drive.
                // IOException: The subdirectory cannot be created. -or- A file or directory already has the name specified by path. -or-  The specified path, file name, or both exceed the system-defined maximum length.
                // SecurityException: The caller does not have code access permission to create the directory.

                Trace.WriteLine(new Exception(Invariant($"{nameof(FileDiagnosticsSender)}.{nameof(this.SetAndValidateLogsFolder)} failed. Path: {this.logDirectory} File: {this.logFileName}; Error: {ex.Message}{Environment.NewLine}")));
            }

            return result;
        }
    }
}
