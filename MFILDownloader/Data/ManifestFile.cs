using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using MFILDownloader.Repositories;

namespace MFILDownloader.Data
{
    public class ManifestFile
    {
        public int Version { get; private set; }

        private const StringComparison StrComp = StringComparison.OrdinalIgnoreCase;

        private List<string> Lines;
        private Dictionary<string, string> ServerPaths;
        private string BaseUrl;

        public ManifestFile()
        {
            ServerPaths = new Dictionary<string, string>();
            Lines = new List<string>();
        }

        public string[] GetLocales()
        {
            if (Version <= 1)
                return null;

            IEnumerable<string> locales;
            if (Version == 2)
            {
                locales = ServerPaths.Keys.Where(x => x.StartsWith("locale_")).Select(x => x.Replace("locale_", ""));
            }
            else
            {
                var ignoredTags = new HashSet<string> { "base", "OSX", "Win", "ALT", "EXP1", "EXP2", "EXP3", "EXP4" };
                locales = Lines.Where(x => x.StartsWith("tag=")).Select(x => x.Replace("tag=", "")).Where(x => !ignoredTags.Contains(x));
            }

            return locales.Distinct().OrderBy(x => x).ToArray();
        }

        public List<FileObject> GenerateFileList()
        {
            Repository repository = RepositoriesManager.GetRepositoryByMfil(MFILDownloader.CurrentSession.Mfil);
            BaseUrl = repository.BaseUrl;

            var fileObjects = new List<FileObject>();
            for (int i = 0; i < Lines.Count; i++)
            {
                if (IsLineARepositorFile(Lines[i]))
                {
                    string path = GetToken(i, "path=");
                    string name = GetToken(i, "name=");
                    string size = GetToken(i, "size=");

                    if (path == null || name == null)
                        continue;

                    string fileinfo = path.Replace("locale_", "");
                    string filepath = GetFilePath(i);

                    if (repository.Type == RepoType.Direct)
                    {
                        // direct download
                        fileObjects.Add(new FileObject()
                        {
                            Info = fileinfo,
                            Path = filepath,
                            Url = Lines[i]
                        });
                    }
                    else
                    {
                        string fileurl = Path.Combine(BaseUrl, ServerPaths[path], name);

                        // each MPQ has upto 30 parts of varying size
                        for (int j = 0; j < 30; j++)
                        {
                            fileObjects.Add(new FileObject()
                            {
                                Info = fileinfo,
                                Path = filepath + "." + j,
                                Url = fileurl + "." + j,
                            });
                        }
                    }
                }
            }

            // Add the manifest itself
            fileObjects.Add(new FileObject()
            {
                Path = repository.Type == RepoType.Direct ? ".mfil." : repository.MfilName,
                Url = BaseUrl + repository.MfilName,
            });

            // remove invalid objects
            fileObjects.RemoveAll(x => !IsAcceptedFile(repository, x));
            return fileObjects;
        }


        private bool IsAcceptedFile(Repository repository, FileObject file)
        {
            // check for completion
            if (MFILDownloader.CurrentSession.CompletedFiles.Contains(file.Path))
            {
                if (!File.Exists(Path.Combine(Program.ExecutionPath, repository.DefaultDirectory, file.Path)))
                {
                    // force re-download of deleted completed files
                    MFILDownloader.CurrentSession.CompletedFiles.Remove(file.Path);
                    MFILDownloader.CurrentSession.SaveSession();
                }
                else
                {
                    // skip already completed
                    Program.Log($"Skipping {file.Filename} already downloaded", ConsoleColor.DarkGray);
                    return false;
                }
            }

            if (string.IsNullOrWhiteSpace(file.Path))
                return false;

            // manifest file
            if (file.Path.EndsWith("mfil", StrComp))
                return true;

            // architecture dependant
            if (file.Directory.Equals("Updates/", StrComp))
                return file.Filename.IndexOf(MFILDownloader.CurrentSession.OS, StrComp) > -1;

            // locale dependant
            if (file.Filename.StartsWith("alternate.MPQ", StrComp) && !file.Info.Equals(MFILDownloader.CurrentSession.Locale, StrComp))
                return false;

            if (file.Directory.Equals("Data/", StrComp))
                return true;

            if (file.Directory.StartsWith("Data/Interface/", StrComp))
                return true;

            if (file.Directory.StartsWith("Data/" + MFILDownloader.CurrentSession.Locale, StrComp))
                return true;

            return false;
        }

        private string GetFilePath(int index)
        {
            return Version == 2 ? Lines[index].Replace(BaseUrl, "") : GetToken(index, "name=");
        }

        private string GetToken(int index, string token)
        {
            for (int i = 1; i <= 5; i++)
            {
                string next = Lines[index + i];

                if (IsLineARepositorFile(next))
                    return null;

                if (next.StartsWith(token))
                    return next.Replace(token, "");
            }
            return null;
        }

        private bool IsLineARepositorFile(string line)
        {
            return line.StartsWith(BaseUrl, StrComp) && line[line.Length - 4] == '.';
        }


        public static ManifestFile FromRepository(Repository repository)
        {
            string[] mirrors = new[]
            {
                "http://ak.worldofwarcraft.com.edgesuite.net/",
                "http://dist.blizzard.com.edgesuite.net/",
                "http://blizzard.vo.llnwd.net/o16/content/",
                "http://client01.pdl.wow.battlenet.com.cn/",
                "http://client02.pdl.wow.battlenet.com.cn/",
                "http://client03.pdl.wow.battlenet.com.cn/",
                "http://client04.pdl.wow.battlenet.com.cn/"
            };

            var manifest = new ManifestFile();
            var client = new WebClient();

            foreach (var mirror in mirrors)
            {
                try
                {
                    repository.Mirror = mirror;
                    string content = client.DownloadString(repository.BaseUrl + repository.MfilName);

                    string[] lines = content.Split('\n');

                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i].Trim();

                        if (line.StartsWith("version=") && int.TryParse(line.Replace("version=", ""), out int version))
                            manifest.Version = version;

                        if (line.StartsWith("serverpath="))
                            manifest.ServerPaths[line.Replace("serverpath=", "")] = lines[i + 1].Trim().Replace("path=", "");

                        manifest.Lines.Add(line.Replace("file=", repository.BaseUrl));
                    }

                    // unsupported
                    if (manifest.Version == 1)
                        return null;

                    return manifest;
                }
                catch { }
            }

            // manifest is corrupt or not accessible
            Program.Log("Unable to retrieve Manifest file", ConsoleColor.Red);
            return null;
        }
    }
}
