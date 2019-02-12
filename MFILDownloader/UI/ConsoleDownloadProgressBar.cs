using System;
using System.Threading;
using MFILDownloader.Data;

namespace MFILDownloader.UI
{
    public class ConsoleProgressBar : IDisposable
    {
        private readonly int FilesCount;
        private int CurrentFileIndex;
        private ProgressData Data;

        private readonly bool IsDownload;
        private readonly Timer Timer;

        public ConsoleProgressBar(bool download, int filescount = 0)
        {
            IsDownload = download;
            FilesCount = filescount;
            CurrentFileIndex = 1;

            Timer = new Timer(DoUpdate, null, Timeout.Infinite, Timeout.Infinite);
            Data = new ProgressData();
        }


        public void Start()
        {
            Timer.Change(0, 1000);
        }

        public void Stop()
        {
            Timer.Change(Timeout.Infinite, Timeout.Infinite);
            CurrentFileIndex = CurrentFileIndex + 1;
            Console.Clear();
            Data.Name = null;
        }


        public void Update(FileObject file, float percent, long bytesReceived, long totalBytes)
        {
            Data.Name = file?.Path;
            Data.Percent = percent * 100f;
            Data.Received = bytesReceived;
            Data.Total = totalBytes;
        }

        public void Update(string archive, float percent)
        {
            Data.Name = archive;
            Data.Percent = percent * 100f;
        }

        private void DoUpdate(object state)
        {
            if (Data.Name == null)
                return;

            int y = Console.CursorTop;

            if (IsDownload)
            {
                SetText("Downloading : " + AlignText(Data.Name, 50, false), ConsoleColor.Yellow, 0, y + 1);
                SetText("File        : " + AlignText(CurrentFileIndex + "/" + FilesCount, 50, false), ConsoleColor.Yellow, 0, y + 2);
                SetText("File Size   : " + AlignText(HumanReadableByteCount(Data.Total), 50, false), ConsoleColor.Yellow, 0, y + 3);
                SetText("Downloaded  : " + AlignText(HumanReadableByteCount(Data.Received), 50, false), ConsoleColor.Yellow, 0, y + 4);
                SetText("Percent     : " + AlignText(Data.Percent + "%", 50, false), ConsoleColor.Yellow, 0, y + 5);
            }
            else
            {
                SetText("Repacking archives. This will take a while.", ConsoleColor.Yellow, 0, y);
                SetText("File        : " + AlignText(Data.Name, 50, false), ConsoleColor.Yellow, 0, y + 1);
                SetText("Percent     : " + AlignText(Data.Percent.ToString("P"), 50, false), ConsoleColor.Yellow, 0, y + 2);
            }

            Console.CursorTop = y;
        }


        private void SetPosition(int x, int y)
        {
            Console.CursorLeft = x;
            Console.CursorTop = y;
        }

        public string HumanReadableByteCount(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };

            int order = 0;
            while (bytes >= 1024 && ++order < sizes.Length)
                bytes /= 1024;

            return string.Format("{0:0.##} {1}", bytes, sizes[order]);
        }

        private string AlignText(string value, int length, bool alignRight)
        {
            if (value.Length > length)
                return value.Substring(0, length);

            if (value.Length < length)
                return alignRight ? value.PadLeft(length, ' ') : value.PadRight(length, ' ');

            return value;
        }

        public void SetText(string value, ConsoleColor color, int x, int y)
        {
            SetPosition(x, y);
            Console.ForegroundColor = color;
            Console.Write(value);
            Console.ResetColor();
        }


        public void Dispose()
        {
            Timer.Dispose();
        }


        private struct ProgressData
        {
            public string Name;
            public long Received;
            public long Total;
            public float Percent;
        }
    }
}
