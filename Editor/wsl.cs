using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEngine;
using Microsoft.Win32;
using static Microsoft.Win32.RegistryKey;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Management;

public class UnityWslWindow : EditorWindow
{
    private static string ModuleName = "Unity WSL Module";
    private static string ProjPath = "";
    private bool useCstmDistro = true;
    private int _CstmDistroSelected = 0;

    [MenuItem("File/Build With WSL...")]
    static void Init()
    {
        if (!WSLTools.Is2004())
        {
            EditorUtility.DisplayDialog(ModuleName, "WSL Module requires Windows 10 Version 2004. Please upgrade your Windows to use this module.", "OK");
            return;
        }
        if (!WSLTools.IsWSLEnabled())
        {
            EditorUtility.DisplayDialog(ModuleName, "WSL is not enabled in your Windows 10.", "OK");
            return;
        }
        if (!WSLTools.IsVMPEnabled())
        {
            EditorUtility.DisplayDialog(ModuleName, "VirtualMachinePlatform is not enabled in your Windows 10. You need Virtual Machine Platform to use WSL2.", "OK");
            return;
        }
        UnityEditor.EditorWindow window = GetWindow(typeof(UnityWslWindow));
        window.titleContent.text = "Build With WSL";
        ProjPath = Application.dataPath.Remove(Application.dataPath.Length - 7);
        window.Show();
    }

    void OnGUI()
    {
        string distro = "Ubuntu-Unity3d";
        useCstmDistro = EditorGUILayout.Toggle("Use Ubuntu-Unity3d", useCstmDistro);
        if (!useCstmDistro)
        {
            string[] _options = WSLTools.GetInstalledWSL2Distros();
            _CstmDistroSelected = EditorGUILayout.Popup("Custom Distro", _CstmDistroSelected, _options);
            distro = _options[_CstmDistroSelected];
        }
        else
        {
            distro = "Ubuntu-Unity3d";
        }

        if (GUILayout.Button("Build"))
            Build(distro);
        if (GUILayout.Button("Unregister Ubuntu-Unity3d distro"))
            Unregister();
    }

    static void Build(string distro)
    {
        WSLExec _wslexec = new WSLExec();
        if (!WSLTools.IsWSLDistroInstalled(distro))
        {
            if (EditorUtility.DisplayDialog(ModuleName, "The specific distro is not installed on your machine. Do you want to install it?", "Yes", "No"))
            {
                //string homefolder = Environment.GetEnvironmentVariable("HOME");
                _wslexec.CallProc("wsl", $"--import Ubuntu-Unity3d .\\installer install.tar.gz --version 2", Encoding.UTF8, "C:\\Users\\Patrick\\ubuntu-unity3d");
            }
            else
            {
                EditorUtility.DisplayDialog(ModuleName, "WSL distro installation action cancelled.", "OK");
                return;
            }
        }
        EditorUtility.DisplayProgressBar(ModuleName, "Install Dependencies...", 0);
        _wslexec.RunCmdInWSL(distro, "apt install -y build-essential gnupg ca-certificates", true);
        EditorUtility.DisplayProgressBar(ModuleName, "Install Mono...", 0.20f);
        _wslexec.RunCmdInWSL(distro, "apt-key adv --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys 3FA7E0328081BFF6A14DA29AA6A19B38D3D831EF", true);
        _wslexec.RunCmdInWSL(distro, "echo \"deb https://download.mono-project.com/repo/ubuntu stable-bionic main\" | tee /etc/apt/sources.list.d/mono-official-stable.list", true);
        _wslexec.RunCmdInWSL(distro, "apt update", true);
        _wslexec.RunCmdInWSL(distro, "apt install -y mono-complete", true);
        EditorUtility.DisplayProgressBar(ModuleName, "Copy files...", 0.40f);
        string tmp_folder = _wslexec.RunCmdInWSL(distro, "mktemp -d -t ubuntu-unity3d-XXXXXXXXXX", false);
        UnityEngine.Debug.Log("Temp folder name: " + tmp_folder + ", Projpath: " + ProjPath);
        tmp_folder = tmp_folder.Replace("/tmp/", "");
        FileUtil.CopyFileOrDirectory(ProjPath, "\\\\wsl$\\" + distro + "\\tmp\\" + tmp_folder);
        EditorUtility.DisplayProgressBar(ModuleName, "Building...", 0.60f);
        EditorUtility.DisplayProgressBar(ModuleName, "Wrap up...", 1);
        EditorUtility.ClearProgressBar();
        string output = _wslexec.RunCmdInWSL(distro, "uname -r", false);
        EditorUtility.DisplayDialog(ModuleName, output, "OK");
    }

    static void Unregister()
    {
        WSLExec _wslexec = new WSLExec();
        if (WSLTools.IsWSLDistroInstalled("Ubuntu-Unity3d"))
        {
            _wslexec.CallProc("wsl", $"--unregister Ubuntu-Unity3d", Encoding.UTF8);
        }
        EditorUtility.DisplayDialog(ModuleName, "Unregister Complete", "OK");

    }

}


public class WSLExec
{
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool Wow64DisableWow64FsRedirection(ref IntPtr ptr);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool Wow64RevertWow64FsRedirection(IntPtr ptr);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsWow64Process2(
        IntPtr process,
        out ushort processMachine,
        out ushort nativeMachine
    );

    private readonly bool _isWow64;

    public WSLExec()
    {
        _isWow64 = IsWow64Process2(GetCurrentProcess(), out _, out _);
    }

    public string CallProc(string command, string arguments, Encoding outputEncoding, string workingDirectory = null)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = outputEncoding
            }
        };

        if (workingDirectory != null)
        {
            process.StartInfo.WorkingDirectory = workingDirectory;
        }

        try
        {
            StartProc(process);
            return process.StandardOutput.ReadToEnd();
        }
        finally
        {
            process.Dispose();
        }
    }

    public void CallDtcdProc(string command, string arguments, bool useShell = false)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                UseShellExecute = useShell,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = false,
            }
        };
        try
        {
            StartProc(process);
        }
        finally
        {
            process.Dispose();
        }
    }

    public string CallElvtProc(string command, string arguments, Encoding outputEncoding, string workingDirectory = null)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                UseShellExecute = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = true,
                Verb = "runas"

            }
        };

        if (workingDirectory != null)
        {
            process.StartInfo.WorkingDirectory = workingDirectory;
        }
        try
        {
            StartProc(process);
            return "";
        }
        finally
        {
            process.Dispose();
        }
    }

    private void StartProc(Process process)
    {
        var ptr = new IntPtr();
        var isWow64FsRedirectionDisabled = false;

        if (_isWow64)
        {
            isWow64FsRedirectionDisabled = Wow64DisableWow64FsRedirection(ref ptr);
        }
        try
        {
            process.Start();
        }
        finally
        {
            if (isWow64FsRedirectionDisabled)
            {
                Wow64RevertWow64FsRedirection(ptr);
            }
        }
    }

    public string RunCmdInWSL(string distro, string cmd, bool isRoot)
    {
        var userRoot = isRoot ? " --user root " : "";
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "wsl",
                Arguments = "--distribution " + distro + userRoot + " -- " + cmd,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            }
        };
        try
        {
            StartProc(process);
            return process.StandardOutput.ReadToEnd();
        }
        finally
        {
            process.Dispose();
        }
    }

    public string RunCmdInWSLDtcd(string distro, string cmd, bool isRoot)
    {
        var userRoot = isRoot ? " --user root " : "";
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "wsl",
                Arguments = "--distribution " + distro + userRoot + " -- " + cmd,
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = false,
            }
        };
        try
        {
            StartProc(process);
            return "";
        }
        finally
        {
            process.Dispose();
        }
    }
}

public class WSLTools
{
    public static string[] GetInstalledWSL2Distros()
    {
        List<string> output = new List<string>();
        var baseKey = OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);

        try
        {
            var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Lxss", false);

            if (key != null)
            {
                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    var subKey = key.OpenSubKey(subKeyName);
                    if (subKey == null)
                    {
                        continue;
                    }

                    if (!subKey.GetValueNames().Contains("Version"))
                    {
                        continue;
                    }
                    else if ((int)subKey.GetValue("Version") != 2)
                    {
                        continue;
                    }

                    if ((int)subKey.GetValue("State") != 1)
                    {
                        continue;
                    }

                    var RegDistroName = (string)subKey.GetValue("DistributionName");

                    if (RegDistroName.Contains("docker") || RegDistroName.Contains("Ubuntu-Unity3d"))
                    {
                        continue;
                    }

                    UnityEngine.Debug.Log("Found " + RegDistroName);

                    output.Add(RegDistroName);
                }
            }
        }
        finally
        {
            baseKey.Dispose();
        }

        return output.ToArray();

    }
    public static bool IsWSLEnabled()
    {
        try
        {
            WSLExec _wslexec = new WSLExec();
            var output = _wslexec.CallProc("wsl", $"--help", Encoding.Unicode);

            return output != null && output.Contains("https://aka.ms/wslinstall") == false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public static bool IsVMPEnabled()
    {
        try
        {
            WSLExec _wslexec = new WSLExec();
            string output = _wslexec.CallProc("powershell.exe", "-NoProfile -NonInteractive -Command \"Write-Output (Get-WmiObject -query \\\"select InstallState from Win32_OptionalFeature where name = 'VirtualMachinePlatform'\\\").InstallState\"", Encoding.ASCII);
            output = output.TrimEnd(new char[] { '\r', '\n' });
            UnityEngine.Debug.Log(output);
            return output.Contains("1");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public static bool IsWSLDistroInstalled(string distroName)
    {
        var baseKey = OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        try
        {
            var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Lxss", false);

            if (key != null)
            {
                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    var subKey = key.OpenSubKey(subKeyName);
                    var regDistroName = (string)subKey?.GetValue("DistributionName");
                    if (distroName == regDistroName)
                    {
                        return true;
                    }
                }
            }
        }
        finally
        {
            baseKey.Dispose();
        }
        return false;
    }

    public static bool Is2004()
    {
        int win10version = 0;
        OperatingSystem os = Environment.OSVersion;
        Version ver = os.Version;
        if (ver.Major == 10)
            win10version = ver.Build;
        UnityEngine.Debug.Log("Windows Build: " + win10version);
        return win10version >= 19041;
    }
}

