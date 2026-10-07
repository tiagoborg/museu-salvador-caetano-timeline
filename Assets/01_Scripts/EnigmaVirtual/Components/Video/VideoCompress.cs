using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace EnigmaVirtual.Components.Video
{
    public class VideoCompress : MonoBehaviour
    {
        public event Action<string> onVideoCompressSuccessEvent;
        public event Action<int> onVideoCompressFailureEvent;
        public event Action onVideoCompressStartEvent;
        public event Action<string> onVideoCompressErrorEvent;

        Process process = null;
        //System.IO.StreamWriter messageStream;

        private bool isCompressing = false;
        private string inputFile;
        private string outputFile;
        private bool deleteOriginalOnSuccess;
        int exitCode = -1;
        string error = string.Empty;


        public void StartCompressing(string inputFile, string outputFile, bool deleteOriginalOnSuccess = false)
        {
            if (isCompressing)
            {
                onVideoCompressErrorEvent?.Invoke("Process already in use!");
                return;
            }

            this.inputFile = inputFile;
            this.outputFile = outputFile;
            this.deleteOriginalOnSuccess = deleteOriginalOnSuccess;

            exitCode = -1;
            error = string.Empty;

            try
            {
                isCompressing = true;
                //Development.LogWarning(Application.persistentDataPath);
                process = new Process();
                process.EnableRaisingEvents = false;
                process.StartInfo.FileName = Application.streamingAssetsPath + "/ffmpeg.exe";
                process.StartInfo.Arguments = string.Format("-y -i \"{0}\" -threads 8 -vcodec h264 -acodec aac \"{1}\" -loglevel quiet -stats",
                   /*Application.persistentDataPath + "/" +*/ inputFile,
                   /*Application.persistentDataPath + "/" +*/ outputFile);
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                //process.StartInfo.RedirectStandardInput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                process.StartInfo.WorkingDirectory = Application.persistentDataPath;
                process.OutputDataReceived += new DataReceivedEventHandler(DataReceived);
                process.ErrorDataReceived += new DataReceivedEventHandler(ErrorReceived);
                process.Start();
                process.BeginOutputReadLine();
                //process.WaitForExit(); // if you want program to halt until script is finished
                //messageStream = process.StandardInput;

                Development.Log("Successfully launched ffmpeg");
                onVideoCompressStartEvent?.Invoke();
                process.WaitForExit();
                process.Close();

                if (string.IsNullOrEmpty(error))
                    ProcessDataReceived();
                else
                    ProcessErrorReceived();
            }
            catch (System.Exception e)
            {
                isCompressing = false;
                UnityEngine.Debug.LogError("Unable to launch app: " + e.Message);
                onVideoCompressErrorEvent?.Invoke("Unable to launch app: " + e.Message);
            }
        }

        private void ProcessDataReceived()
        {
            isCompressing = false;

            if (exitCode != 0)
            {
                Development.LogError("Error compressing mp4 file!");
                onVideoCompressFailureEvent?.Invoke(exitCode);
            }
            else
            {
                Development.Log("Mp4 file compressing success!");
                onVideoCompressSuccessEvent?.Invoke(outputFile);

                if (deleteOriginalOnSuccess)
                    DeleteFile(inputFile);
            }
        }

        private void ProcessErrorReceived()
        {
            onVideoCompressErrorEvent?.Invoke(error);
        }

        private void DataReceived(object sender, DataReceivedEventArgs eventArgs)
        {
            Development.Log("DataReceived - ExitCode: " + ((Process)sender).ExitCode);
            exitCode = ((Process)sender).ExitCode;
        }

        private void ErrorReceived(object sender, DataReceivedEventArgs eventArgs)
        {
            Development.LogError(eventArgs.Data);

            error = eventArgs.Data;
        }

        private void OnApplicationQuit()
        {
            if (process != null && !process.HasExited)
                process.Kill();
        }

        private void DeleteFile(string filePath)
        {
            Development.CheckPoint();

            if (File.Exists(filePath))
            {
                Development.Log(string.Concat(filePath, " found, deleting..."));
                File.Delete(filePath);
            }
            else
                Development.LogWarning(string.Concat(filePath, " not found!"));
        }
    }
}
