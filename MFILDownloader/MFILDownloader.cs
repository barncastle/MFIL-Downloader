using System;
using System.Collections.Generic;
using MFILDownloader.Data;
using MFILDownloader.Installation;
using MFILDownloader.Repositories;
using MFILDownloader.UI;

namespace MFILDownloader
{
    public static class MFILDownloader
    {
        public static Session CurrentSession;

        public static void Process()
        {
            Session previousSession = Session.LoadSession();

            if (previousSession == null)
                EntryPointNewSession();
            else if (!UserInputs.SelectContinueSession(previousSession))
                EntryPointNewSession();
            else
                EntryPointResumeSession(previousSession);
        }

        private static void EntryPointNewSession()
        {
            RepoType repoType = UserInputs.SelectRepositoryType();
            Repository repository = UserInputs.SelectRepository(repoType);
            ManifestFile manifest = ManifestFile.FromRepository(repository);
            if (manifest == null)
                return;

            string locale = UserInputs.SelectLocale(manifest);
            string os = UserInputs.SelectOs();

            CurrentSession = new Session(repository.MfilName, locale, os);
            CurrentSession.SaveSession();
            StartProcess(manifest);
        }

        private static void EntryPointResumeSession(Session previousSession)
        {
            CurrentSession = previousSession;

            Repository repository = RepositoriesManager.GetRepositoryByMfil(CurrentSession.Mfil);
            ManifestFile manifest = ManifestFile.FromRepository(repository);
            if (manifest == null)
                return;

            CurrentSession.SaveSession();
            StartProcess(manifest);
        }

        private static void StartProcess(ManifestFile manifest)
        {
            Program.Log("Generating file list");

            Repository repository = RepositoriesManager.GetRepositoryByMfil(CurrentSession.Mfil);
            List<FileObject> files = manifest.GenerateFileList();

            using (var downloader = new FileDownloader(repository, files))
                downloader.Start();

            using (var installer = new Installer(repository))
                installer.Start();

            CurrentSession.Delete();

            Program.Log("Download Complete !!", ConsoleColor.Green);
        }
    }
}
