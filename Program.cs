// See https://aka.ms/new-console-template for more information

using System.CommandLine;
using System.Runtime.InteropServices;
using System.Text;
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

    public static string OutPath { get; private set; } = null!;

    public static void Init(string? _lazerPath, string outPath)
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
    public class Args
    {
        public required string LazerPath { get; init; }
        public required string OutPath { get; init; }
        public string? ReplayPath { get; init; }
        public bool IsCopy { get; init; }
        public bool All { get; init; }
        public bool Validate { get; init; }
        public string? MD5Hash { get; init; }
        public long OnlineID { get; init; }
    }

    private static void Run(Args args)
    {
        Api.Init(args.LazerPath, args.OutPath);

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

    public static void Main(string[] _args)
    {
        RootCommand root = new();

        Option<DirectoryInfo> lazerPath = new("--dir", "-d")
        {
            Description = "Path to lazer directory"
        };
        Option<FileInfo> replayPath = new("--replay", "-r")
        {
            Description = "Path to replay file"
        };
        Option<DirectoryInfo> outPath = new("--out", "-o")
        {
            Required = true,
            Description = "Path to output directory"
        };
        Option<bool> isCopy = new("--copy", "-c")
        {
            Description = "copy instead of symlinks"
        };
        Option<bool> all = new("--all", "-a")
        {
            Description = "symlink all beatmaps"
        };
        Option<bool> validate = new("--validate", "-v")
        {
            Description = "validate symlinks in output directory"
        };
        Option<string> md5hash = new("--md5", "-m")
        {
            Description = "beatmap md5hash"
        };
        Option<long> onlineId = new("--id", "-i")
        {
            Description = "beatmap online id (not beatset)"
        };

        root.Options.Add(lazerPath);
        root.Options.Add(outPath);
        root.Options.Add(replayPath);
        root.Options.Add(isCopy);
        root.Options.Add(all);
        root.Options.Add(validate);
        root.Options.Add(md5hash);
        root.Options.Add(onlineId);

        root.SetAction(parsed =>
        {
            var parsedArgs = new Args
            {
                LazerPath = parsed.GetValue(lazerPath)?.FullName ?? Api.GetDefaultLazerPath(),
                OutPath = parsed.GetRequiredValue(outPath).FullName,
                ReplayPath = parsed.GetValue(replayPath)?.FullName,
                IsCopy = parsed.GetValue(isCopy),
                All = parsed.GetValue(all),
                Validate = parsed.GetValue(validate),
                MD5Hash = parsed.GetValue(md5hash),
                OnlineID = parsed.GetValue(onlineId),
            };

            Run(parsedArgs);

            return 0;
        });

        root.Parse(_args).Invoke();
    }
}