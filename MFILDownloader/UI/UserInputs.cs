using System;
using MFILDownloader.Data;
using MFILDownloader.Repositories;

namespace MFILDownloader.UI
{
    public static class UserInputs
    {
        public static RepoType SelectRepositoryType()
        {
            Program.Log("Which type of data do you want to download :");
            Program.Log();
            Program.Log("[1] Direct - This will produce a working client");
            Program.Log("[2] Stream - This will produce a collection of archives");
            Program.Log();
            Program.Log("Select type :");
            int selectedIndex = HandleUserParams(2);
            return (RepoType)selectedIndex;
        }

        public static Repository SelectRepository(RepoType type)
        {
            var repos = RepositoriesManager.GetByType(type);
            Program.Log("Which version of World of Warcraft do you want to restore :");
            Program.Log();

            for (int i = 0; i < repos.Count; i++)
                Program.Log("[" + (i + 1).ToString("000") + "] " + repos[i].VersionName);

            Program.Log();
            Program.Log("Select version :");
            int selectedIndex = HandleUserParams(repos.Count);
            return repos[selectedIndex];
        }

        public static string SelectLocale(ManifestFile manifest)
        {
            var locales = manifest.GetLocales();
            Program.Log();
            Program.Log("Which locale do you want to use :");
            Program.Log();

            for (int i = 0; i < locales.Length; i++)
                Program.Log("[" + (i + 1).ToString("00") + "] " + locales[i]);

            Program.Log();
            Program.Log("Select locale :");
            int selectedIndex = HandleUserParams(locales.Length);
            return locales[selectedIndex];
        }

        public static string SelectOs()
        {
            var os = new[] { "Win", "OSX" };
            Program.Log();
            Program.Log("Which OS do you want to use :");
            Program.Log();

            for (int i = 0; i < os.Length; i++)
                Program.Log("[" + (i + 1).ToString("00") + "] " + os[i]);

            Program.Log();
            Program.Log("Select OS :");
            int selectedIndex = HandleUserParams(os.Length);
            return os[selectedIndex];
        }

        public static bool SelectContinueSession(Session previousSession)
        {
            Program.Log("A previous unfinished session was found for : ");
            Program.Log("WoW Version : " + previousSession.WoWRepositoryName);
            Program.Log("Locale      : " + previousSession.Locale);
            Program.Log("OS          : " + previousSession.OS);
            Program.Log();
            Program.Log("Do you want to continue this session ? (y/n) :");
            var result = HandleUserYesNo();
            Console.Clear();
            return result;
        }

        public static bool SelectRepackStreamedArchvies()
        {
            if (!MFILDownloader.CurrentSession.RepackArchives)
            {
                Program.Log("NOTE: Streams use a special chunked MPQ format. They can be opened with MPQ Editor " +
                            "by loading each '*.MPQ.0' as normal. Alternatively they can be repacked as " +
                            "normal MPQs (this will take a while).", ConsoleColor.Yellow);
                Program.Log();
                Program.Log("Do you want to repack these archives ? (y/n) :");

                MFILDownloader.CurrentSession.RepackArchives = HandleUserYesNo();
                MFILDownloader.CurrentSession.SaveSession();
            }

            Console.Clear();
            return MFILDownloader.CurrentSession.RepackArchives;
        }


        private static bool HandleUserYesNo()
        {
            while (true)
            {
                string input = Console.ReadLine().ToLower();
                if (input != "y" && input != "n")
                {
                    Program.Log("Please enter a correct response 'y' for yes or 'n' for no", ConsoleColor.Red);
                    continue;
                }
                return input == "y";
            }
        }

        private static int HandleUserParams(int max)
        {
            while (true)
            {
                string input = Console.ReadLine();

                if (!int.TryParse(input, out int output))
                {
                    Program.Log("Please enter a number", ConsoleColor.Red);
                    continue;
                }

                if (output < 1 || output > max)
                {
                    Program.Log($"Please enter a number between 1 and {max}", ConsoleColor.Red);
                    continue;
                }

                return output - 1;
            }
        }
    }
}
