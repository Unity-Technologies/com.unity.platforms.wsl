using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Win32.SafeHandles;

namespace Unity
{
    namespace WSL
    {
        public class WSLDistro
        {
            public string Name { get; set; }

            public WSLDistro(string name)
            {
                Name = name;
            }
        }

        public struct ProcessResult
        {
            public bool Completed;
            public int ExitCode;
            public string Output;
        }

        public abstract class CommonTask
        {
            public CommonTask(string command)
            {
                Command = command;
            }

            public string Command { get; }
            public abstract Task<int> ExecuteFluentAsync(int parentResult);
        }

        public class OSProcessTask : CommonTask
        {
            public ProcessResult Result { get; private set; }

            protected bool UseShellExecute = false;
            protected bool RedirectOuput = true;
            protected bool CreateNoWindow = true;

            public async Task<ProcessResult> ExecuteShellCommand(string command, string arguments, int timeout)
            {
                var result = new ProcessResult();

                using (var process = new Process()) {
                    // If you run bash-script on Linux it is possible that ExitCode can be 255.
                    // To fix it you can try to add '#!/bin/bash' header to the script.

                    process.StartInfo.FileName = command;
                    process.StartInfo.Arguments = arguments;
                    process.StartInfo.UseShellExecute = UseShellExecute;
                    process.StartInfo.RedirectStandardInput = RedirectOuput;
                    process.StartInfo.RedirectStandardOutput = RedirectOuput;
                    process.StartInfo.RedirectStandardError = RedirectOuput;
                    process.StartInfo.CreateNoWindow = CreateNoWindow;

                    if (RedirectOuput)
                        process.StartInfo.StandardOutputEncoding = Encoding.Unicode;

                    var outputBuilder = new StringBuilder();
                    var outputCloseEvent = new TaskCompletionSource<bool>();

                    process.OutputDataReceived += (s, e) =>
                    {
                        // The output stream has been closed i.e. the process has terminated
                        if (e.Data == null) {
                            outputCloseEvent.SetResult(true);
                        }
                        else {
                            outputBuilder.AppendLine(e.Data);
                        }
                    };

                    var errorBuilder = new StringBuilder();
                    var errorCloseEvent = new TaskCompletionSource<bool>();

                    process.ErrorDataReceived += (s, e) =>
                    {
                        // The error stream has been closed i.e. the process has terminated
                        if (e.Data == null) {
                            errorCloseEvent.SetResult(true);
                        }
                        else {
                            errorBuilder.AppendLine(e.Data);
                        }
                    };

                    bool isStarted;

                    try {
                        isStarted = process.Start();
                    }
                    catch (Exception error) {
                        // Usually it occurs when an executable file is not found or is not executable

                        result.Completed = true;
                        result.ExitCode = -1;
                        result.Output = error.Message;

                        isStarted = false;
                    }

                    if (isStarted) {
                        // Reads the output stream first and then waits because deadlocks are possible
                        if (RedirectOuput) {
                            process.BeginOutputReadLine();
                            process.BeginErrorReadLine();
                        }

                        // Creates task to wait for process exit using timeout
                        var waitForExit = WaitForExitAsync(process, timeout);

                        // Create task to wait for process exit and closing all output streams
                        var processTask = Task.WhenAll(waitForExit, outputCloseEvent.Task, errorCloseEvent.Task);

                        // Waits process completion and then checks it was not completed by timeout
                        if (await Task.WhenAny(Task.Delay(timeout), processTask) == processTask && waitForExit.Result) {
                            result.Completed = true;
                            result.ExitCode = process.ExitCode;

                            // Adds process output if it was completed with error
                            if (process.ExitCode != 0) {
                                result.Output = $"{outputBuilder}{errorBuilder}";
                            }
                            else {
                                result.Output = $"{outputBuilder}";
                            }
                        }
                        else {
                            try {
                                // Kill hung process
                                process.Kill();
                            }
                            catch {
                            }
                        }
                    }
                }

                return result;
            }

            private static Task<bool> WaitForExitAsync(Process process, int timeout)
            {
                return Task.Run(() => process.WaitForExit(timeout));
            }

            public OSProcessTask(string command) : base(command) { }

            protected virtual string ConstructArguments()
            {
                return "";
            }

            public override async Task<int> ExecuteFluentAsync(int parentResult)
            {
                Result = await ExecuteShellCommand(Command, ConstructArguments(), 100000);
                return Result.ExitCode;
            }

            public static OSProcessTask Create(string command) => new OSProcessTask(command);
        }

        public class WSLExecTask : OSProcessTask
        {
            public string Distro; 

            public WSLExecTask() : base("wsl.exe")
            {
                UseShellExecute = true;
                RedirectOuput = false;
                CreateNoWindow = false;
            }

            protected override string ConstructArguments() 
            {
                if (string.IsNullOrEmpty(Distro))
                    return $"{WSLCommand()}";

                return $"-d {Distro} -e {WSLCommand()}";
            }

            protected virtual string WSLCommand()
            {
                return "";
            }
        }

        public class WSLRMDirTask : WSLExecTask
        {
            public string DirToRemove;

            public WSLRMDirTask()
            {
                UseShellExecute = false;
                RedirectOuput = true;
                CreateNoWindow = true;
            }

            protected override string WSLCommand() => $"rm -rf {DirToRemove}";

            public override async Task<int> ExecuteFluentAsync(int parentResult)
            {
                if (string.IsNullOrEmpty(DirToRemove))
                    return -1;

                return await base.ExecuteFluentAsync(parentResult);
            }
        }

        public class WSLMkDirDirTask : WSLExecTask
        {
            public string DirToCreate;

            public WSLMkDirDirTask()
            {
                UseShellExecute = false;
                RedirectOuput = true;
                CreateNoWindow = true;
            }

            protected override string WSLCommand() => $"mkdir {DirToCreate}";

            public override async Task<int> ExecuteFluentAsync(int parentResult)
            {
                if (string.IsNullOrEmpty(DirToCreate))
                    return -1;

                return await base.ExecuteFluentAsync(parentResult);
            }
        }

        public class WSLSetExecTask : WSLExecTask
        {
            public string ExecToSet;

            public WSLSetExecTask()
            {
                UseShellExecute = false;
                RedirectOuput = true;
                CreateNoWindow = true;
            }

            protected override string WSLCommand() => $"chmod +x {ExecToSet}";

            public override async Task<int> ExecuteFluentAsync(int parentResult)
            {
                if (string.IsNullOrEmpty(ExecToSet))
                    return -1;

                return await base.ExecuteFluentAsync(parentResult);
            }
        }

        public class WSLLaunchExecTask : WSLExecTask
        {
            public string ExecToLaunch;

            public WSLLaunchExecTask()
            {

            }

            protected override string WSLCommand() => $"{ExecToLaunch}";

            public override async Task<int> ExecuteFluentAsync(int parentResult)
            {
                if (string.IsNullOrEmpty(ExecToLaunch))
                    return -1;

                return await base.ExecuteFluentAsync(parentResult);
            }
        }

        public class WSLGetDistrosTask : WSLExecTask
        {
            public List<WSLDistro> _distros;

            public WSLGetDistrosTask()
            {
                _distros = new List<WSLDistro>();

                UseShellExecute = false;
                RedirectOuput = true;
                CreateNoWindow = true;
            }

            protected override string WSLCommand() => "-l -v";

            public override async Task<int> ExecuteFluentAsync(int parentResult)
            {
                int r = await base.ExecuteFluentAsync(parentResult);

                // we should parse the version here ? 
                if (r == 0) {
                    string[] lines = Result.Output.Split(
                        new[] { "\r\n", "\r", "\n" },
                        StringSplitOptions.None
                    );

                    // We grab all the distros and we only care about the WSL2 instances
                    foreach (string d in lines) {
                        if (d.Contains("NAME")) {
                            continue;
                        }

                        var thing = d.Replace("*", "").Trim();

                        var distroInofo = thing.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (distroInofo.Length == 0 || int.Parse(distroInofo[2]) == 1)
                            continue;

                        _distros.Add(new WSLDistro(distroInofo[0]));
                    }
                }

                return r;
            }
        }
    }
}

