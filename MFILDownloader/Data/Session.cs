using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using MFILDownloader.Repositories;

namespace MFILDownloader.Data
{
    [Serializable]
    public class Session
    {
        public string WoWRepositoryName;
        public string Mfil;
        public string Locale;
        public string OS;
        public HashSet<string> CompletedFiles;
        public bool RepackArchives;

        private const string SessionFilename = "session.xml";

        private Session()
        {
            CompletedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public Session(string mfil, string locale, string os) : this()
        {
            Mfil = mfil;
            Locale = locale;
            OS = os;

            Repository rep = RepositoriesManager.GetRepositoryByMfil(mfil) ?? throw new Exception("Unknown mfil file");
            WoWRepositoryName = rep.VersionName;
        }

        public static Session LoadSession()
        {
            string sessionPath = Program.ExecutionPath + SessionFilename;

            if (File.Exists(sessionPath))
            {
                var xml = new XmlSerializer(typeof(Session));
                using (var fs = File.OpenRead(sessionPath))
                    return (Session)xml.Deserialize(fs);
            }

            return null;
        }

        public bool SaveSession()
        {
            try
            {
                var xml = new XmlSerializer(typeof(Session));
                using (var fs = File.Create(Program.ExecutionPath + SessionFilename))
                    xml.Serialize(fs, this);

                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Delete()
        {
            string sessionPath = Program.ExecutionPath + SessionFilename;
            if (File.Exists(sessionPath))
                File.Delete(sessionPath);
        }
    }
}
