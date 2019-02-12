using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Threading;
using MFILDownloader.Repositories;
using MFILDownloader.UI;

namespace MFILDownloader.Data
{
    public class FileDownloader : IDisposable
    {
        private readonly string BasePath;
        private readonly List<FileObject> Files;
        private readonly ConsoleProgressBar Progress;
        private readonly WebClient Client;

        private FileObject CurrentFile;
        private int CurrentFileIndex;
        private long TotalBytesToReceive;
        private bool HasRetried;

        public FileDownloader(Repository repository, List<FileObject> files)
        {
            Files = files;
            BasePath = repository.DefaultDirectory;
            Progress = new ConsoleProgressBar(true, Files.Count);

            Client = new WebClient();
            Client.DownloadProgressChanged += DownloadProgressChanged;
            Client.DownloadFileCompleted += DownloadFileCompleted;
        }


        public void Start()
        {
            CurrentFileIndex = 0;

            Program.Log("Downloading " + Files.Count + " Files", ConsoleColor.Yellow);
            DownloadNextFile();

            Console.CursorLeft = 0;
            Progress.Update(CurrentFile, 0, 0, 0);

            while (CurrentFile != null)
                Thread.Sleep(100);

            Console.Clear();
        }


        private void DownloadNextFile()
        {
            Console.Clear();
            Progress.Stop();

            if (CurrentFileIndex >= Files.Count)
            {
                CurrentFile = null;
                return;
            }

            CurrentFile = Files[CurrentFileIndex];

            if (!Directory.Exists(BasePath + CurrentFile.Directory))
                Directory.CreateDirectory(BasePath + CurrentFile.Directory);

            if (File.Exists(BasePath + CurrentFile.Path))
                File.Delete(BasePath + CurrentFile.Path);

            Client.DownloadFileAsync(new Uri(CurrentFile.Url), BasePath + CurrentFile.Path);
            Progress.Start();
        }

        private void DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            TotalBytesToReceive = e.TotalBytesToReceive;

            float perc = (float)Math.Round(e.BytesReceived / (double)TotalBytesToReceive, 4);
            Progress.Update(CurrentFile, perc, e.BytesReceived, e.TotalBytesToReceive);
        }

        private void DownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
        {
            if (IsComplete(e.Error))
            {
                HasRetried = false;
                MFILDownloader.CurrentSession.CompletedFiles.Add(CurrentFile.Path);
                MFILDownloader.CurrentSession.SaveSession();
                CurrentFileIndex++;
                DownloadNextFile();
            }
        }


        private bool IsComplete(Exception ex)
        {
            // check exceptions
            if (ex != null)
            {
                File.Delete(BasePath + CurrentFile.Path);

                if (!ex.Message.Contains("(404)"))
                {
                    Program.Log("Unable to download " + CurrentFile.Path, ConsoleColor.Red);
                    Program.Log(ex.Message, ConsoleColor.Red);
                    Thread.Sleep(2000);

                    // attempt a retry
                    return !Retry();
                }

                return true;
            }

            // check download is the right size
            if (new FileInfo(BasePath + CurrentFile.Path).Length != TotalBytesToReceive)
            {
                File.Delete(BasePath + CurrentFile.Path);
                Program.Log("Downloaded file size mismatch " + CurrentFile.Path, ConsoleColor.Red);
                Thread.Sleep(2000);

                // attempt to retry for the whole file
                return !Retry();
            }

            return true;
        }

        private bool Retry()
        {
            if (!HasRetried)
            {
                HasRetried = true;
                DownloadNextFile();
                return true;
            }

            return false;
        }

        public void Dispose()
        {
            Client.DownloadProgressChanged -= DownloadProgressChanged;
            Client.DownloadFileCompleted -= DownloadFileCompleted;
            Client.Dispose();
            Progress.Dispose();
        }
    }
}
