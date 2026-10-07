using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using EnigmaVirtual;

public class PreBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    public int callbackOrder => 0; // Order of execution in the build process

    public void OnPreprocessBuild(BuildReport report)
    {
#if !UNITY_ANDROID && !UNITY_WEBGL && !UNITY_IOS
        Debug.Log("PreBuild: Creating batch file before build...");

        BatchFileCreator batchFileCreator = new BatchFileCreator();
        batchFileCreator.CreateBatchFile();

        Debug.Log("PreBuild: Batch file creation completed.");
#endif
    }

    public void OnPostprocessBuild(BuildReport report)
    {
#if !UNITY_ANDROID && !UNITY_WEBGL
        Debug.Log("PostBuild: Build completed at " + report.summary.outputPath);
#endif
    }
}
