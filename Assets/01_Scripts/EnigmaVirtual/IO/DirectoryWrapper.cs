using System.IO;
using System.Linq;

namespace EnigmaVirtual.IO
{
    public static class DirectoryWrapper
    {
        /// <summary>
        /// Determines whether the given path refers to a directory.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static bool IsDirectory(string path)
        {
            if (path == null)
                throw new System.ArgumentNullException("path");

            path = path.Trim();

            if (Directory.Exists(path))
                return true;

            if (File.Exists(path))
                return false;

            // if has trailing slash then it's a directory
            if (new[] { "\\", "/" }.Any(x => path.EndsWith(x)))
                return true; // ends with slash

            // if has extension then its a file; directory otherwise
            return path.EndsWithAny("\\", "/") || string.IsNullOrWhiteSpace(Path.GetExtension(path));
        }

        /// <summary>
        /// Determines whether the given path refers to an existing directory on disk.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static bool Exists(string path)
        {
            return Directory.Exists(path);
        }

        /// <summary>
        /// Copies an existing file to a new file.Overwriting a file of the same name is allowed.
        /// </summary>
        /// <param name="sourcePath"></param>
        /// <param name="destinationPath"></param>
        /// <param name="removeOriginalFiles"></param>
        /// <param name="removeFolderStructure"></param>
        /// <param name="fileType"></param>
        public static void Copy(string sourcePath, string destinationPath, bool removeOriginalFiles = false, bool removeFolderStructure = false, string fileType = "*.*", bool overwriteFiles = false)
        {
            if (Directory.Exists(sourcePath))
            {
                if (!Directory.Exists(destinationPath))
                    CreateDirectory(destinationPath);

                string[] directories = Directory.GetDirectories(sourcePath);

                if (directories != null && directories.Length > 0)
                    foreach (string directory in directories)
                        Copy(directory, Path.Combine(destinationPath, directory.Replace(sourcePath, "")), removeOriginalFiles, removeFolderStructure, fileType);

                CopyFilesInDirectory(sourcePath, destinationPath, overwriteFiles, fileType);

                if (removeOriginalFiles)
                    Delete(sourcePath, removeFolderStructure);
            }
            else
            {
                if (!Directory.Exists(sourcePath))
                    Development.LogError($"Source folder [{sourcePath}] not found!");
            }
        }

        /// <summary>
        /// Allows to transfer files and/or directories from one directory to another.
        /// </summary>
        /// <param name="sourcePath"></param>
        /// <param name="destinationPath"></param>
        public static void Move(string sourcePath, string destinationPath)
        {
            Copy(sourcePath, destinationPath, true, true, "*.*");
        }

        /// <summary>
        /// Returns fliename from a url/path.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static string GetFileNameFromPath(string path)
        {
            path = path.Replace("/", @"\");

            int slashPos = path.LastIndexOf(@"\");

            return (slashPos > 0) ? path.Substring(slashPos + 1) : path;
        }

        /// <summary>
        /// Creates all directories and subdirectories in the specified path unless they already exist.
        /// </summary>
        /// <param name="path"></param>
        /// <returns>Success of operation.</returns>
        public static bool CreateDirectory(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            path = path.Replace("/", @"\");

            if (!Directory.Exists(path))
            {
                string[] folderStructure = path.Split('\\');
                string tempPath = folderStructure[0];

                for (int i = 1; i < folderStructure.Length; i++)
                {
                    tempPath += Path.Combine("/", folderStructure[i]);

                    if (!Directory.Exists(tempPath))
                        Directory.CreateDirectory(tempPath);
                }
            }
            return true;
        }

        /// <summary>
        /// Deletes the specified directory and, if indicated, any subdirectories and files in the directory.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="recursive"></param>
        public static void Delete(string path, bool recursive = false)
        {
            DirectoryInfo dir = new DirectoryInfo(path);
            foreach (FileInfo file in dir.GetFiles())
                file.Delete();

            foreach (DirectoryInfo subDirectory in dir.GetDirectories())
                Delete(subDirectory.FullName, recursive);

            if (recursive)
                dir.Delete(true);

            //if (recursive)
            //    foreach (DirectoryInfo subDirectory in dir.GetDirectories())
            //        subDirectory.Delete(true);
        }

        private static void CopyFilesInDirectory(string sourcePath, string destinationPath, bool overwriteFiles, string fileType = "*.*")
        {
            string[] files = Directory.GetFiles(sourcePath, fileType);
            foreach (string file in files)
            {
                string filePath = Path.Combine(destinationPath, GetFileNameFromPath(file));
                File.Copy(file, filePath, overwriteFiles);
            }
        }
    }
}