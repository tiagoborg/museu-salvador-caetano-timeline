using System.IO;

namespace EnigmaVirtual
{
    public class DownloadVO {

        public string ServerPath;
        public string DestinationPath;
        public string FileName;
        public string DestinationName;

        public DownloadVO(string serverPath,string destinationPath,string fileName)
        {
            ServerPath = serverPath;
            DestinationPath = destinationPath;
            // The Directory.CreateDirectory that used to sit here is gone. It ran on a
            // CMS-supplied path, before any validation, from a CONSTRUCTOR - so merely describing a
            // download created arbitrary directory trees anywhere writable, and threw out of a
            // constructor nobody guards. FileDownloader.TryResolveSavePath creates the folder after
            // containing the path.
            FileName = fileName;
            DestinationName = FileName;
        }

        public DownloadVO(string serverPath, string destinationPath, string fileName, string destinationName)
        {
            ServerPath = serverPath;
            DestinationPath = destinationPath;
            FileName = fileName;
            DestinationName = destinationName;
        }

        public string DownloadPath
        {
            get
            {
                return ServerPath+FileName;
            }
        }

        public string SavePath
        {
            get
            {
                return DestinationPath + DestinationName;
            }
        }
    }
}
