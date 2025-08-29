// See https://aka.ms/new-console-template for more information

using System.Runtime.InteropServices;
using System.Text;
using CommandLine;
using Realms;

// ReSharper disable InvertIf
// ReSharper disable ConvertIfStatementToReturnStatement
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Local
// ReSharper disable ClassNeverInstantiated.Local
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedAutoPropertyAccessor.Local
// ReSharper disable ReplaceAutoPropertyWithComputedProperty

namespace OsuLazerFilesSymlinker;

public class BeatmapSet : RealmObject
{
    [PrimaryKey] public Guid ID { get; private set; }
    [Indexed] public long OnlineID { get; private set; }
    public IList<RealmNamedFileUsage> Files { get; } = null!;
}

public class BeatmapMetadata : RealmObject
{
    public string Title { get; private set; } = null!;
    public string TitleUnicode { get; private set; } = null!;
    public string Artist { get; private set; } = null!;
    public string ArtistUnicode { get; private set; } = null!;
    public string Source { get; private set; } = null!;
}

public class Beatmap : RealmObject
{
    [PrimaryKey] public Guid ID { get; private set; }
    [Indexed] public string MD5Hash { get; private set; } = null!;
    [Indexed] public long OnlineID { get; private set; }

    public BeatmapMetadata Metadata { get; private set; } = null!;
    public BeatmapSet BeatmapSet { get; private set; } = null!;
}

public class RealmNamedFileUsage : EmbeddedObject
{
    public File File { get; private set; } = null!;
    public string Filename { get; private set; } = null!;
}

public class File : RealmObject
{
    [PrimaryKey] public string Hash { get; private set; } = null!;
}

public record FileOutput
{
    public required string Filename { get; init; }
    public required string Path { get; init; }
}

static class Api
{
    public static Realm Realm { get; private set; } = null!;
    public static string LazerPath { get; private set; } = null!;

    public static string OutPath { get; set; } = null!;

    public static void InitRealm(string? _lazerPath, string outPath)
    {
        var lazerPath = _lazerPath ?? GetDefaultLazerPath();
        var realmPath = Path.Join(lazerPath, "client.realm");
        var filesPath = Path.Join(lazerPath, "files");

        if (!Path.Exists(lazerPath)) throw new FileNotFoundException(lazerPath);
        if (!Path.Exists(realmPath)) throw new FileNotFoundException(realmPath);
        if (!Path.Exists(filesPath)) throw new FileNotFoundException(filesPath);

        if (System.IO.File.Exists(outPath))
        {
            throw new FileLoadException("not a directory", outPath);
        }

        if (!Directory.Exists(outPath))
        {
            Directory.CreateDirectory(outPath);
        }

        var config = new RealmConfiguration(realmPath)
        {
            SchemaVersion = 51,
            IsReadOnly = true,
            Schema = new[]
            {
                typeof(File),
                typeof(Beatmap),
                typeof(BeatmapSet),
                typeof(BeatmapMetadata),
                typeof(RealmNamedFileUsage),
            }
        };

        Realm = Realm.GetInstance(config);
        LazerPath = lazerPath;
        OutPath = outPath;
    }

    // https://osu.ppy.sh/wiki/en/Client/Release_stream/Lazer/File_storage
    public static string GetDefaultLazerPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var appdata = Environment.GetEnvironmentVariable("APPDATA");

            return appdata is null ? "" : Path.Join(appdata, "osu");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var home = Environment.GetEnvironmentVariable("HOME");

            return home is null ? "" : Path.Join(home, ".local", "share", "osu");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetEnvironmentVariable("HOME");

            return home is null ? "" : Path.Join(home, "Library", "Application Support", "osu");
        }

        return "";
    }

    // https://osu.ppy.sh/wiki/en/Client/File_formats/osr_%28file_format%29
    public static string GetMD5HashFromReplay(string? path)
    {
        if (!Path.Exists(path)) throw new FileLoadException("Path to replay file not found");

        using var stream = System.IO.File.OpenRead(path);

        if (!stream.CanRead) throw new FileLoadException("Failed to read replay file.");

        Span<byte> buf = stackalloc byte[39];
        stream.ReadExactly(buf);

        return Encoding.UTF8.GetString(buf[7..]);
    }

    public static void ValidatePaths()
    {
        var files = Directory.EnumerateFiles(OutPath, "*", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            var info = System.IO.File.ResolveLinkTarget(file, false)!;

            if (!info.Exists)
            {
                System.IO.File.Delete(file);
            }
        }

        var dirs = Directory.EnumerateDirectories(OutPath, "*", SearchOption.AllDirectories);

        foreach (var dir in dirs)
        {
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
            }
        }
    }

    public static void CreateLinksAll(bool isCopy = false)
    {
        var beatmaps = Realm.All<Beatmap>();

        foreach (var beatmap in beatmaps)
        {
            CreateLinks(beatmap, isCopy);
        }
    }

    public static void CreateLinks(Beatmap beatmap, bool isCopy = false)
    {
        var mapFiles = beatmap.BeatmapSet.Files;

        // just id because parsing title and artist can be invalid for paths
        var dirname = beatmap.BeatmapSet.OnlineID.ToString();
        var dirpath = Path.Join(OutPath, dirname);

        if (Directory.Exists(dirpath))
        {
            return;
        }

        Directory.CreateDirectory(dirpath);

        foreach (var f in mapFiles)
        {
            var src = Path.Join(LazerPath, "files", f.File.Hash[..1], f.File.Hash[..2], f.File.Hash);
            var dst = Path.Join(dirpath, f.Filename);

            Console.WriteLine($"{src} -> \\{dirname}\\{f.Filename}");

            Directory.CreateDirectory(Directory.GetParent(dst)!.FullName)
                ;
            if (isCopy)
            {
                System.IO.File.Copy(src, dst, false);
                return;
            }


            System.IO.File.CreateSymbolicLink(dst, src);
        }
    }
}

internal static class Program
{
    private class Args
    {
        [Option('d', "dir", Required = false, HelpText = "lazer directory")]
        public string LazerPath { get; private set; } = Api.GetDefaultLazerPath();

        [Option('r', "replay", Required = false, HelpText = "replay filepath")]
        public string? ReplayPath { get; private set; }

        [Option('o', "output", Required = true, HelpText = "output directory")]
        public string OutPath { get; private set; } = null!;

        [Option('c', "copy", Required = false, HelpText = "copy instead of symlinks")]
        public bool IsCopy { get; private set; }

        [Option('a', "all", Required = false, HelpText = "symlink all beatmaps")]
        public bool All { get; private set; }

        [Option('v', "validate", Required = false, HelpText = "validate output directory files")]
        public bool Validate { get; private set; }

        [Option('h', "md5", Required = false, HelpText = "beatmap md5hash")]
        public string? MD5Hash { get; private set; }

        [Option('i', "id", Required = false, HelpText = "beatmap onlineid (not beatset)")]
        public long OnlineID { get; private set; } = 0;
    }

    public static void Main(string[] _args)
    {
        var parsed = Parser.Default.ParseArguments<Args>(_args);

        if (parsed.Errors.Any())
        {
            throw new Exception(string.Join("\n", parsed.Errors));
        }

        var args = parsed.Value!;

        Api.InitRealm(args.LazerPath, args.OutPath);

        if (args.Validate)
        {
            Api.ValidatePaths();

            if (args.OnlineID == 0 || args.MD5Hash is null || args.ReplayPath is null)
            {
                return;
            }
        }

        if (args.All)
        {
            Api.CreateLinksAll(args.IsCopy);
            return;
        }

        if (args.OnlineID == 0)
        {
            var md5hash = args.MD5Hash ?? Api.GetMD5HashFromReplay(args.ReplayPath);

            var beatmap = Api.Realm.All<Beatmap>().First(b => b.MD5Hash == md5hash);

            Api.CreateLinks(beatmap, args.IsCopy);
        }
        else
        {
            var beatmap = Api.Realm.All<Beatmap>().First(b => b.OnlineID == args.OnlineID);

            Api.CreateLinks(beatmap, args.IsCopy);
        }
    }
}