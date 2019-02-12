using System;
using System.IO;
using System.Threading;

namespace MFILDownloader
{
    class Program
    {
        public static string ExecutionPath;

        private static void Main()
        {
            InitConsole();
            MFILDownloader.Process();
            Log("Press enter to exit program.");
            Console.ReadLine();
        }

        public static void Log(string value = "", ConsoleColor color = ConsoleColor.White)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(value);
            Console.ResetColor();
        }

        private static void InitConsole()
        {
            // force culture number format
            var culture = (System.Globalization.CultureInfo)Thread.CurrentThread.CurrentCulture.Clone();
            culture.NumberFormat.NumberDecimalSeparator = ".";
            Thread.CurrentThread.CurrentCulture = culture;

            Console.Clear();
            ExecutionPath = Environment.CurrentDirectory;
            if (!ExecutionPath.EndsWith(Path.DirectorySeparatorChar))
                ExecutionPath += Path.DirectorySeparatorChar;
        }
    }
}
