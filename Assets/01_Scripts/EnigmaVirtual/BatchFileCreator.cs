using System.IO;
using UnityEngine;

namespace EnigmaVirtual
{
    public class BatchFileCreator : MonoBehaviour
    {
        void Start()
        {
            CreateBatchFile();
        }

        public void CreateBatchFile()
        {
            string companyName = Application.companyName;
            string productName = Application.productName;
            string streamingAssetsPath = Application.streamingAssetsPath;

            // Define the target folder path inside AppData/LocalLow with backslashes
            // Windows batch script: literal backslashes are correct and required here.
            // AppPaths.ConfigFolder is a bare segment, so the separator is appended explicitly
            // rather than baked into the shared value the way configDirectory used to be.
            string targetFolder = "%userprofile%\\AppData\\LocalLow\\" + companyName + "\\" + productName
                                  + "\\" + AppPaths.PersistentStreamingFolder + "\\" + AppPaths.ConfigFolder + "\\";
            //string settingsFile = targetFolder + "\\settings.json";
            string sourceSettingsFile = "settings.json";
            string sourceAppSettingsFile = "appSettings.json";
            string sourceAppDataFile = "appData.json";

            // Create batch file content with additional shortcut creation
            string batchContent = "@echo off\n" +
                                "setlocal\n\n" +
                                ":: Set the target folder path\n" +
                                "set \"TARGET_FOLDER=" + targetFolder + "\"\n\n" +

                                ":: Ensure target folder exists\n" +
                                "if not exist \"" + targetFolder + "\" mkdir \"" + targetFolder + "\"\n" +

                                ":: Ensure settings file exists\n" +
                                "if not exist \"" + targetFolder + "\\" + sourceSettingsFile + "\" (\n" +
                                "    copy \"%~dp0" + AppPaths.ConfigFolder + "\\" + sourceSettingsFile + "\" \"" + targetFolder + "\" /Y\n" +
                                ")\n\n" +

                                ":: Ensure appSettings file exists\n" +
                                "if not exist \"" + targetFolder + "\\" + sourceAppSettingsFile + "\" (\n" +
                                "    copy \"%~dp0" + AppPaths.ConfigFolder + "\\" + sourceAppSettingsFile + "\" \"" + targetFolder + "\" /Y\n" +
                                ")\n\n" +

                                ":: Ensure appData file exists\n" +
                                "if not exist \"" + targetFolder + "\\" + sourceAppDataFile + "\" (\n" +
                                "    copy \"%~dp0" + AppPaths.ConfigFolder + "\\" + sourceAppDataFile + "\" \"" + targetFolder + "\" /Y\n" +
                                ")\n\n" +

                                "explorer \"" + targetFolder + "\"\n" +

                                ":: Set the shortcut name\n" +
                                "set \"SHORTCUT_NAME=" + productName + " Settings\"\n\n" +

                                ":: Get the Desktop path for the current user\n" +
                                "set \"DESKTOP_PATH=%USERPROFILE%\\Desktop\"\n\n" +

                                ":: Check if Desktop path exists\r\n" +
                                "if not exist \"%DESKTOP_PATH%\" (\r\n    echo Error: Could not determine desktop path.\r\n    pause\r\n    exit /b\r\n)\n\n" +

                                ":: Create shortcut using VBScript directly in batch\r\n" +
                                "echo Set oWS = WScript.CreateObject(\"WScript.Shell\") > \"%temp%\\CreateShortcut.vbs\"\r\n" +
                                "echo sLinkFile = \"%DESKTOP_PATH%\\%SHORTCUT_NAME%.lnk\" >> \"%temp%\\CreateShortcut.vbs\"\r\n" +
                                "echo Set oLink = oWS.CreateShortcut(sLinkFile) >> \"%temp%\\CreateShortcut.vbs\"\r\n" +
                                "echo oLink.TargetPath = \"%TARGET_FOLDER%\" >> \"%temp%\\CreateShortcut.vbs\"\r\n" +
                                "echo oLink.WorkingDirectory = \"%TARGET_FOLDER%\" >> \"%temp%\\CreateShortcut.vbs\"\r\n" +
                                "echo oLink.IconLocation = \"%SystemRoot%\\system32\\SHELL32.dll,278\" >> \"%temp%\\CreateShortcut.vbs\"\r\n" +
                                "echo oLink.Save >> \"%temp%\\CreateShortcut.vbs\"\n\n" +

                                ":: Run the VBScript\r\n" +
                                "cscript //nologo \"%temp%\\CreateShortcut.vbs\"\r\n\r\n" +

                                ":: Delete the temporary VBScript\r\n" +
                                "del \"%temp%\\CreateShortcut.vbs\"\r\n\r\n" +
                                "echo Shortcut created successfully!\r\n" +

                                //"pause\n"+
                                "exit"
                                ;

            // Define the path for the .bat file
            string batFilePath = Path.Combine(streamingAssetsPath, "config.bat");

            // Write the batch file
            try
            {
                File.WriteAllText(batFilePath, batchContent);
                Debug.Log("Batch file created successfully at: " + batFilePath);
            }
            catch (IOException e)
            {
                Debug.LogError("Error writing batch file: " + e.Message);
            }
        }
    }
}
