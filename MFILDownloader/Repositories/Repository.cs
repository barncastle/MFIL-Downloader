using System.Linq;
using System.Text.RegularExpressions;

namespace MFILDownloader.Repositories
{
    public class Repository
    {
        public string MfilName;
        public string FilePath;
        public string VersionName;
        public string DefaultDirectory;
        public string Mirror;
        public readonly RepoType Type;
        public readonly bool Valid;

        public string BaseUrl => Mirror + FilePath;

        public Repository(string filepath)
        {
            MfilName = System.IO.Path.GetFileName(filepath);
            FilePath = filepath.Replace(MfilName, "");
            Type = FilePath.Contains("direct") ? RepoType.Direct : RepoType.Streamed;

            Match match = Regex.Match(MfilName, @"(wow[tb]?)-(\d+)-", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            if (match.Success)
            {
                var (branch, build, region) = ExtractInfo(match);

                if (!RepositoriesManager.Builds.TryGetValue(build, out string buildname))
                    buildname = "0.0.0";

                VersionName = $"{buildname}.{build} - {branch} {region}".Trim();
                DefaultDirectory = $"{build}patch{buildname}" + System.IO.Path.DirectorySeparatorChar;

                Valid = true;
            }
            else
            {
                Program.Log("Invalid Mfil " + MfilName, System.ConsoleColor.Yellow);
                Valid = false;
            }
        }

        private (string branch, string build, string region) ExtractInfo(Match match)
        {
            string[] regions = new[] { "CN", "EU", "KR", "NA", "TW" };

            return (match.Groups[1].Value,
                    match.Groups[2].Value,
                    FilePath.Split('/').Intersect(regions).FirstOrDefault());
        }
    }

    public enum RepoType
    {
        Direct,
        Streamed
    }
}
