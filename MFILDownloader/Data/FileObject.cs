namespace MFILDownloader.Data
{
    public class FileObject
    {
        public string Url;
        public string Path;
        public string Info;

        public string Directory => Path.Replace(Filename, "");
        public string Filename => System.IO.Path.GetFileName(Path);

        public override string ToString() => Path;
    }
}
