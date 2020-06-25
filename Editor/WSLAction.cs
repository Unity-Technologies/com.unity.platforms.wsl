using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using UnityEditor;

using UnityEngine;


namespace Unity
{
    namespace WSL
    {
        public static class WSLHelpers
        {
            public static async Task<Result> AndThen<T, Result>(this Task<T> task, Func<T, Task<Result>> fn)
            {
                T value = await task;
                return await fn(value);
            }

            public static async Task<Result> Chain<T, Result>(this Task<T> task, Func<Task<Result>> fn)
            {
                _ = await task;
                return await fn();
            }
        }

        // This class is the base for an action 
        // which is a collection of WSLTasks that get executed
        public abstract class WSLAction
        {
            protected Task<int> _task;

            public string Name { get; private set; }
            public WSLAction(string name)
            {
                Name = name;
            }

            public Task<int> Start()
            {
                return Task.FromResult(0);
            }

            public abstract Task<int> Execute();
        }

        public class WSLTestWindow : EditorWindow
        {
            private List<WSLDistro> _distros = new List<WSLDistro>();
            private int _choiceIndex = 0;
            private string buildFolderToCopy;
            private string exeName;

            [MenuItem("WSL/Test Window")]
            public static void Launch()
            {
                WSLTestWindow window = GetWindow<WSLTestWindow>();
                window.Show();
            }

            private async Task GetDistros()
            {
                var distros = new WSLGetDistrosTask();
                _ = await distros.ExecuteFluentAsync(0);
                _distros.Clear();
                _distros.AddRange(distros._distros);
            }

            private async Task MakeTempDir(string dir)
            {
                var selectedDistro = _distros[_choiceIndex];
                await new WSLMkDirDirTask() { Distro = selectedDistro.Name, DirToCreate = $"/tmp/{dir}" }.ExecuteFluentAsync(0);            
            }

            private async Task CleanupTempDir(string dir)
            {
                var selectedDistro = _distros[_choiceIndex];
                _ = await new WSLRMDirTask() { Distro = selectedDistro.Name, DirToRemove = $"/tmp/{dir}" }.ExecuteFluentAsync(0);
            }

            private void CopyFileToWSLFolder(string source, string dest)
            {
                FileUtil.CopyFileOrDirectory(source, dest);
            }

            private async Task RunServer()
            {
                var selectedDistro = _distros[_choiceIndex];
                await new WSLSetExecTask() { Distro = selectedDistro.Name, ExecToSet = $"/tmp/{Path.GetFileNameWithoutExtension(exeName)}/{exeName}" }.ExecuteFluentAsync(0);
                await new WSLLaunchExecTask() { Distro = selectedDistro.Name, ExecToLaunch = $"/tmp/{Path.GetFileNameWithoutExtension(exeName)}/{exeName}" }.ExecuteFluentAsync(0);
            }

            protected virtual void OnGUI()
            {
                if (GUILayout.Button("Get Distros")) {
                    EditorUtility.DisplayProgressBar("WSL Query", "Getting WSL Distros", 0f);
                    _ = GetDistros();

                    EditorUtility.ClearProgressBar();
                }

                _choiceIndex = EditorGUILayout.Popup(_choiceIndex, _distros.Select(d => d.Name).ToArray());
                if (GUILayout.Button("Select Build to deploy")) {
                    buildFolderToCopy = EditorUtility.OpenFolderPanel("Build to Deploy", "", "");
                    var files = Directory.GetFiles(buildFolderToCopy, "*.x86_64");
                    if (files.Length != 0) {
                        var fileInfo = new FileInfo(files[0]);
                        exeName = fileInfo.Name;
                    }
                }

                EditorGUILayout.LabelField(buildFolderToCopy);
                if (GUILayout.Button("Copy to Select WSL Instance")) {
                    _ = CleanupTempDir(Path.GetFileNameWithoutExtension(exeName));
                    var selectedDistro = _distros[_choiceIndex];
                    CopyFileToWSLFolder(buildFolderToCopy, $"\\\\wsl$\\{selectedDistro.Name}\\tmp\\{Path.GetFileNameWithoutExtension(exeName)}\\");
                }
                EditorGUILayout.LabelField(exeName);

                if (GUILayout.Button("Launch Server")) {
                    _ = RunServer();
                }
            }
        }
    }
}
