using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using MFILDownloader.Installation.MPQ;
using MFILDownloader.Installation.MPQ.Native;
using MFILDownloader.Repositories;
using MFILDownloader.UI;

namespace MFILDownloader.Installation
{
    public class Installer : IDisposable
    {
        private readonly Repository _repo;
        private readonly string _dataDirectory;
        private readonly string _updateDirectory;
        private readonly ConsoleProgressBar _progressBar;

        public Installer(Repository repository)
        {
            _repo = repository;
            _dataDirectory = Path.Combine(_repo.DefaultDirectory, "Data");
            _updateDirectory = Path.Combine(_repo.DefaultDirectory, "Updates");
            _progressBar = new ConsoleProgressBar(false);
        }

        public void Start()
        {
            Program.Log("Starting installation...", ConsoleColor.Green);

            // Feel free to try and tackle this cluster
            if (_repo.Type == RepoType.Streamed)
            {
                if (UserInputs.SelectRepackStreamedArchvies())
                    RebuildChunkedArchives();
            }
            else
            {
                if (ExtractBaseMPQ())
                    ApplyBaseMPQUpdates();
            }
        }

        #region Direct Installation

        /// <summary>
        /// Extract the root directory files from Base-{OS}.MPQ
        /// </summary>
        /// <returns></returns>
        private bool ExtractBaseMPQ()
        {
            Program.Log("Extracting Base MPQ...");

            string baseMPQ = $"Base-{MFILDownloader.CurrentSession.OS}.mpq";
            string baseMPQpath = Path.Combine(_dataDirectory, baseMPQ);

            if (!File.Exists(baseMPQpath))
            {
                Program.Log($"{baseMPQ} not found!", ConsoleColor.Red);
                return false;
            }

            using (var mpq = new MpqArchive(baseMPQpath, FileAccess.Read))
            {
                if (!TryGetFileList(mpq, true, out var files))
                {
                    Program.Log($"{baseMPQ} missing ListFile, manual extraction required.", ConsoleColor.Red);
                    return false;
                }

                foreach (var file in files)
                    mpq.ExtractFile(file, Path.Combine(_repo.DefaultDirectory, file));
            }

            return true;
        }

        /// <summary>
        /// Extracts and runs the Blizzard Updater app for the repo's patch archives
        /// </summary>
        private void ApplyBaseMPQUpdates()
        {
            if (!Directory.Exists(_updateDirectory))
                return;

            Program.Log("Applying Base MPQ Updates...");

            var updateMPQs = Directory.EnumerateFiles(_updateDirectory, "*.mpq")
                                      .Select(x => Path.Combine("Updates", Path.GetFileName(x)));

            if (updateMPQs.Any())
            {
                string BNUpdatePath = Path.Combine(_repo.DefaultDirectory, "BNUpdate.exe");

                // this specific version supports all Cata and MoP patch archives whilst others don't
                // Note: intentionally renamed
                using (var fs = File.Create(BNUpdatePath))
                    fs.Write(Properties.Resources.BNUpdate, 0, Properties.Resources.BNUpdate.Length);

                string args = string.Format("--skippostlaunch=1 --patchlist=\"{0}\"", string.Join(" ", updateMPQs));
                Process.Start(BNUpdatePath, args);
            }
        }

        #endregion

        #region Stream Installation

        /// <summary>
        /// Streaming uses a seperate, chunked, MPQ format however the client only reads normal flat file MPQs and AFAICT only the 
        /// background downloader can open these so rebuilding is required.
        /// <para></para>
        /// [However] streamed mfils don't appear to contain patches for Base-Win so we can't recreate a working client anyway...
        /// </summary>
        private void RebuildChunkedArchives()
        {
            var chunkedMPQs = Directory.GetFiles(_dataDirectory, "*.mpq.*", SearchOption.AllDirectories)
                                       .Where(x => float.TryParse(Path.GetExtension(x), out _))
                                       .GroupBy(x => Path.ChangeExtension(x, null));

            if (!chunkedMPQs.Any())
                return;

            foreach (var grp in chunkedMPQs)
            {
                string archivename = grp.Key;
                string mpqName = grp.Key + ".0";

                if (MFILDownloader.CurrentSession.CompletedFiles.Contains(archivename))
                    continue;

                if (File.Exists(mpqName))
                {
                    if (File.Exists(archivename))
                        File.Delete(archivename);

                    DoFileSwap(archivename, mpqName);

                    MFILDownloader.CurrentSession.CompletedFiles.Add(archivename);
                    MFILDownloader.CurrentSession.SaveSession();
                }
            }
        }

        /// <summary>
        /// Moves the files from the chunked archive to a flat file one
        /// </summary>
        /// <param name="archivename"></param>
        /// <param name="block"></param>
        private void DoFileSwap(string archivename, string block)
        {
            Directory.CreateDirectory("Temp");

            string filename = Path.GetFileName(archivename);

            using (var mpq = MpqArchive.CreateNew(archivename, MpqArchiveVersion.Version3))
            using (var tmp = new MpqArchive(block, FileAccess.Read, OpenArchiveFlags.BLOCK4))
            {
                if (TryGetFileList(tmp, false, out var lf))
                {
                    _progressBar.Start();

                    string tempPath;
                    for (int i = 0; i < lf.Count; i++)
                    {
                        using (var fs = tmp.OpenFile(lf[i]))
                        {
                            // incremental patch files can't be written directly with StormLib SFileCreateFile?
                            if ((fs.TFileEntry.dwFlags & 0x100000u) != 0x100000u)
                            {
                                mpq.AddFileFromStream(fs);
                            }
                            else
                            {
                                tempPath = Path.Combine("Temp", lf[i]);
                                Directory.CreateDirectory(Path.GetDirectoryName(tempPath));

                                tmp.ExtractFile(lf[i], tempPath);
                                mpq.AddFileFromDisk(tempPath, lf[i], fs.TFileEntry.dwFlags);
                            }
                        }

                        _progressBar.Update(filename, i / (float)lf.Count);

                        if (i % 10000 == 0)
                            mpq.Flush();
                    }
                }
            }

            Console.WriteLine("Emptying Temp Folder...");
            DeleteDirectory("Temp");
            _progressBar.Stop();
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Attempts to return a list of all files in the archive
        /// </summary>
        /// <param name="mpq"></param>
        /// <param name="files"></param>
        /// <returns></returns>
        private bool TryGetFileList(MpqArchive mpq, bool removeLists, out List<string> files)
        {
            files = new List<string>();

            if (!mpq.HasFile("(ListFile)"))
                return false;

            using (var file = mpq.OpenFile("(ListFile)"))
            using (var sr = new StreamReader(file))
            {
                if (!file.CanRead || file.Length == 0)
                    return false;

                string[] filenames = sr.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                files.AddRange(filenames);
            }

            if (removeLists)
                files.RemoveAll(x => x.EndsWith(".lst"));

            return files.Count > 0;
        }

        /// <summary>
        /// Attempts to get around Explorer folder/file locks by using delayed recursive deletion
        /// </summary>
        /// <param name="path"></param>
        private void DeleteDirectory(string path)
        {
            foreach (string directory in Directory.GetDirectories(path))
                DeleteDirectory(directory);

            try
            {
                Directory.Delete(path, true);
            }
            catch (Exception ex)
            when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Thread.Sleep(50);
                Directory.Delete(path, true);
            }
        }

        public void Dispose()
        {
            _progressBar.Dispose();
        }

        #endregion
    }
}
