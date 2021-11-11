using System.Diagnostics;
using System.Text;

namespace MySC
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> Logger;
        private readonly IConfiguration Configuration;

        public enum TaskType
        {
            Simple = 0,
            Daemon = 1
        }

        public Worker(ILogger<Worker> logger, IConfiguration configuration)
        {
            Logger = logger;
            Configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken token)
        {

            string? command = Configuration.GetValue<string>(nameof(command))?.Trim();
            if (string.IsNullOrEmpty(command))
            {
                Logger.LogError("{message} is required", nameof(command));
                return;
            }


            ProcessStartInfo si = new(command)
            {
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false                
            };
            string? folder = Configuration.GetValue<string>(nameof(folder))?.Trim();
            if (!string.IsNullOrEmpty(folder))
            {
                si.WorkingDirectory = folder;
            }
            string? arguments = Configuration.GetValue<string>(nameof(arguments))?.Trim();
            if (!string.IsNullOrEmpty(arguments))
            {
                si.Arguments = arguments;
            }

            string cmd = BuildCommand(command, folder, arguments);

            TaskType type = Configuration.GetValue(nameof(type), TaskType.Simple);
            int delay = Configuration.GetValue(nameof(delay),30000);

            while (!token.IsCancellationRequested)
            {
                switch (type)
                {
                    case TaskType.Simple:
                        {
                            await RunSimpleTask(si, cmd, delay, token);
                            break;
                        }
                    case TaskType.Daemon:
                        {
                            await RunDaemonTask(si, cmd, delay, token);
                            break;
                        }
                }
            }
        }

        private string BuildCommand(string command, string? folder, string? arguments)
        {
            StringBuilder cmd = new(string.IsNullOrEmpty(folder) ? command : Path.Combine(folder, command));
            if (!string.IsNullOrEmpty(arguments))
            {
                cmd.Append(' ');
                cmd.Append(arguments);
            }
            return cmd.ToString();

        }

        private async Task RunDaemonTask(ProcessStartInfo si, string cmd, int delay, CancellationToken token)
        {
            si.RedirectStandardOutput = false;
            si.RedirectStandardError = false;
            using Process process = new()
            {
                StartInfo = si
            };            

            bool started = false;
            try
            {
                started = process.Start();
                if (started)
                {
                    Logger.LogInformation("[{pid}]{cmd}", process.Id, cmd);                    
                    await process.WaitForExitAsync(token);
                }
            }            
            catch (Exception ex)
            {
                if(ex is TaskCanceledException) { return; }
                Logger.LogError(ex, "[{pid}]{cmd}", process.Id, cmd);
            }
            finally
            {
                if (started && !process.HasExited)
                {
                    process.Kill();
                }
                Logger.LogInformation("[{pid}]{cmd}({code})", process.Id, cmd, process.ExitCode);
            }

            await Task.Delay(delay, token);
        }

        private async Task RunSimpleTask(ProcessStartInfo si, string cmd, int delay, CancellationToken token)
        {
            si.RedirectStandardOutput = true;
            si.RedirectStandardError = true;
            using Process process = new()
            {
                StartInfo = si
            };

            StringBuilder? output_log = null;
            StringBuilder? error_log = null;
            DataReceivedEventHandler? output_handler = null;
            DataReceivedEventHandler? error_handler = null;

            if (si.RedirectStandardOutput)
            {
                output_log = new();
                output_handler = SimpleTaskDataReceivedHandler(output_log);
                process.OutputDataReceived += output_handler;
            }
            if (si.RedirectStandardError)
            {
                error_log = new();
                error_handler = SimpleTaskDataReceivedHandler(error_log);
                process.ErrorDataReceived += error_handler;
            }


            bool started = false;
            try
            {
                started = process.Start();
                if (started)
                {
                    Logger.LogDebug("[{pid}]{cmd}", process.Id, cmd);
                    if (si.RedirectStandardOutput) { process.BeginOutputReadLine(); }
                    if (si.RedirectStandardError) { process.BeginErrorReadLine(); }
                    await process.WaitForExitAsync(token);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[{pid}]{cmd}", process.Id, cmd);
            }
            finally
            {
                if (started)
                {
                    if (si.RedirectStandardOutput)
                    {
                        process.CancelOutputRead();
                        process.OutputDataReceived -= output_handler;
                    }
                    if (output_log?.Length > 0)
                    {
                        output_log.Insert(0, Environment.NewLine);
                        Logger.LogInformation("[{pid}]{cmd}{output}", process.Id, cmd, output_log);
                    }

                    if (si.RedirectStandardError)
                    {
                        process.CancelErrorRead();
                        process.ErrorDataReceived -= error_handler;
                    }
                    if (error_log?.Length > 0)
                    {
                        error_log.Insert(0, Environment.NewLine);
                        Logger.LogError("[{pid}]{cmd}{output}", process.Id, cmd, error_log);
                    }
                }
                Logger.LogDebug("[{pid}]{cmd}({code})", process.Id, cmd, process.ExitCode);
            }

            await Task.Delay(delay, token);

        }

        private DataReceivedEventHandler SimpleTaskDataReceivedHandler(StringBuilder log)
        {
            return new DataReceivedEventHandler((sender, e) =>
            {
                if (e.Data is null) { return; }
                log.AppendLine(e.Data);
            });
        }       
    }
}