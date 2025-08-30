﻿// See https://aka.ms/new-console-template for more information

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

[Preserve(AllMembers = true)]
public class BeatmapSet : RealmObject
{
    [PrimaryKey] public Guid ID { get; private set; }
    [Indexed] public long OnlineID { get; private set; }
    public IList<RealmNamedFileUsage> Files { get; } = null!;
}

[Preserve(AllMembers = true)]
public class BeatmapMetadata : RealmObject
{
    public string Title { get; private set; } = null!;
    public string TitleUnicode { get; private set; } = null!;
    public string Artist { get; private set; } = null!;
    public string ArtistUnicode { get; private set; } = null!;
    public string Source { get; private set; } = null!;
}

[Preserve(AllMembers = true)]
public class Beatmap : RealmObject
{
    [PrimaryKey] public Guid ID { get; private set; }
    [Indexed] public string MD5Hash { get; private set; } = null!;
    [Indexed] public long OnlineID { get; private set; }

    public BeatmapMetadata Metadata { get; private set; } = null!;
    public BeatmapSet BeatmapSet { get; private set; } = null!;
}

[Preserve(AllMembers = true)]
public class RealmNamedFileUsage : EmbeddedObject
{
    public File File { get; private set; } = null!;
    public string Filename { get; private set; } = null!;
}

[Preserve(AllMembers = true)]
public class File : RealmObject
{
    [PrimaryKey] public string Hash { get; private set; } = null!;
}

[Preserve(AllMembers = true)]
public record FileOutput
{
    public required string Filename { get; init; }
    public required string Path { get; init; }
}

[Preserve(AllMembers = true)]
static class Api
{
    public static Realm Realm { get; private set; } = null!;
    private static string LazerPath { get; set; } = null!;

    private static string OutPath { get; set; } = null!;

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
        Console.WriteLine("Validating paths, this can take a while...");

        var files = Directory.EnumerateFiles(OutPath, "*", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            var info = System.IO.File.ResolveLinkTarget(file, false);
            
            if (info is not null && !info.Exists)
            {
                Console.WriteLine($"Removing {file}");
                System.IO.File.Delete(file);
            }
        }

        var dirs = Directory.EnumerateDirectories(OutPath, "*", SearchOption.AllDirectories);

        foreach (var dir in dirs)
        {
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Console.WriteLine($"Removing {dir}");
                Directory.Delete(dir);
            }
        }

        Console.WriteLine("Validated paths");
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

[Preserve(AllMembers = true)]
internal static class Program
{
    [Preserve(AllMembers = true)]
    public class Args
    {
        public required string LazerPath { get; init; }
        public required string OutPath { get; init; }
        public string? ReplayPath { get; init; }
        public bool IsCopy { get; init; }
        public bool IsQuiet { get; init; }
        public bool IsVerbose { get; init; }
        public bool All { get; init; }
        public bool Validate { get; init; }
        public string? MD5Hash { get; init; }
        public long? OnlineID { get; init; }

        public bool CannotRun()
        {
            return this is { All: false, Validate: false, MD5Hash: null, OnlineID: null, ReplayPath: null };
        }
        
        public bool CannotContinue()
        {
            return this is { All: false, MD5Hash: null, OnlineID: null, ReplayPath: null };
        }
    }

    private static void Run(Args args)
    {
        if (args.CannotRun())
        {
            Console.WriteLine("Nothing to do, use one of [-a, -m, -i, -r] options");
            return;
        }

        Api.Init(args.LazerPath, args.OutPath);

        if (args.IsQuiet)
        {
            Console.SetOut(TextWriter.Null);
        }

        if (args.Validate)
        {
            Api.ValidatePaths();

            if (args.CannotContinue())
            {
                return;
            }
        }

        if (args.All)
        {
            Api.CreateLinksAll(args.IsCopy);
        }

        if (args.MD5Hash is not null || args.ReplayPath is not null)
        {
            var md5hash = args.MD5Hash ?? Api.GetMD5HashFromReplay(args.ReplayPath);

            var beatmap = Api.Realm.All<Beatmap>().First(b => b.MD5Hash == md5hash);

            Api.CreateLinks(beatmap, args.IsCopy);

            return;
        }

        if (args.OnlineID == 0)
        {
            var beatmap = Api.Realm.All<Beatmap>().First(b => b.OnlineID == args.OnlineID);

            Api.CreateLinks(beatmap, args.IsCopy);
        }
    }

    private static bool CheckDrives(string lazerPath, string outPath)
    {
        if (Path.GetPathRoot(lazerPath) != Path.GetPathRoot(outPath))
        {
            Console.WriteLine("These are on different drives");
            Console.WriteLine(" ");
            Console.WriteLine($"Lazer path: {lazerPath}");
            Console.WriteLine($"Output Path: {outPath}");
            Console.WriteLine(" ");
            return true;
        }

        return false;
    }

    private static bool ReadKey()
    {
        var key = Console.ReadKey();
        return key.Key == ConsoleKey.Escape;
    }

    private static bool CheckArg(string arg, IEnumerable<Option> options)
    {
        return options.Any(o => o.Name == arg || o.Aliases.Any(a => a == arg));
    }

    public static void Main(string[] _args)
    {
        RootCommand root = new();

        Option<DirectoryInfo> lazerPath = new("--dir", "-d")
        {
            DefaultValueFactory = _ => new DirectoryInfo(Api.GetDefaultLazerPath()),
            Description = "Path to lazer directory"
        };
        Option<DirectoryInfo> outPath = new("--out", "-o")
        {
            DefaultValueFactory = _ =>
                new DirectoryInfo("./YOU-CAN-RENAME-THIS-AND-MOVE-THIS-ON-SAME-DRIVE"),
            Description = "Path to output directory"
        };
        Option<FileInfo?> replayPath = new("--replay", "-r")
        {
            DefaultValueFactory = _ => null,
            Description = "Path to replay file"
        };
        Option<bool> isCopy = new("--copy", "-c")
        {
            DefaultValueFactory = _ => false,
            Description = "copy instead of symlinks"
        };
        Option<bool> isQuiet = new("--quiet", "-q")
        {
            DefaultValueFactory = _ => false,
            Description = "suppress output"
        };
        Option<bool> isVerbose = new("--verbose")
        {
            DefaultValueFactory = _ => false,
            Description = "show detailed info (does nothing now)"
        };
        Option<bool> all = new("--all", "-a")
        {
            DefaultValueFactory = _ => false,
            Description = "symlink all beatmaps, enabled if ran with no other args"
        };
        Option<bool> validate = new("--validate", "-v")
        {
            DefaultValueFactory = _ => false,
            Description = "validate symlinks in output directory"
        };
        Option<string?> md5hash = new("--md5", "-m")
        {
            DefaultValueFactory = _ => null,
            Description = "beatmap md5hash"
        };
        Option<long?> onlineId = new("--id", "-i")
        {
            DefaultValueFactory = _ => null,
            Description = "beatmap online id (not beatset)"
        };

        root.Options.Add(lazerPath);
        root.Options.Add(outPath);
        root.Options.Add(replayPath);
        root.Options.Add(isCopy);
        root.Options.Add(isQuiet);
        root.Options.Add(isVerbose);
        root.Options.Add(all);
        root.Options.Add(validate);
        root.Options.Add(md5hash);
        root.Options.Add(onlineId);

        var defLazerPath = lazerPath.DefaultValueFactory(null!).FullName;
        var defOutPath = outPath.DefaultValueFactory(null!).FullName;

        if (!Directory.Exists(defLazerPath))
        {
            Console.WriteLine($"{defLazerPath} not found");
            Console.WriteLine("Maybe you have a custom lazer install? use -d to specify where to find it");
            return;
        }

        switch (_args.Length)
        {
            // drag and dropped on exe
            case 1:
            {
                if (CheckArg(_args[0], root.Options))
                {
                    break;
                }

                var args = new Args
                {
                    LazerPath = defLazerPath,
                    OutPath = Path.GetFullPath(_args[0]),
                    ReplayPath = null,
                    IsCopy = false,
                    IsQuiet = false,
                    IsVerbose = true,
                    All = true,
                    Validate = true,
                    MD5Hash = null,
                    OnlineID = null,
                };

                if (System.IO.File.Exists(args.OutPath))
                {
                    Console.WriteLine($"{args.OutPath} is not a directory");
                    ReadKey();
                    return;
                }

                if (CheckDrives(args.LazerPath, args.OutPath))
                {
                    return;
                }

                if (Directory.Exists(args.OutPath) && Directory.EnumerateFileSystemEntries(args.OutPath).Any())
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("");
                    Console.WriteLine("Output directory contains files, confirm its correct");
                    Console.ResetColor();
                    Console.WriteLine("");
                }

                Console.WriteLine("This will validate existing symlinks and symlink all beatmaps!");
                Console.WriteLine(" ");
                Console.WriteLine($"Lazer path: {args.LazerPath}");
                Console.WriteLine($"Output Path: {args.OutPath}");
                Console.WriteLine(" ");
                Console.WriteLine("Press <ESC> or <Ctrl+C> to exit...");
                Console.WriteLine("Press any key to continue...");

                if (ReadKey())
                {
                    return;
                }

                Run(args);

                return;
            }
            // double-clicking exe directly
            case 0:
            {
                var args = new Args
                {
                    LazerPath = defLazerPath,
                    OutPath = defOutPath,
                    ReplayPath = null,
                    IsCopy = false,
                    IsQuiet = false,
                    IsVerbose = true,
                    All = true,
                    Validate = false,
                    MD5Hash = null,
                    OnlineID = null,
                };

                if (CheckDrives(args.LazerPath, args.OutPath))
                {
                    return;
                }

                Console.WriteLine("This will symlink all beatmaps!");
                Console.WriteLine(" ");
                Console.WriteLine($"Lazer path: {args.LazerPath}");
                Console.WriteLine($"Output Path: {args.OutPath}");
                Console.WriteLine(" ");
                Console.WriteLine("Press <ESC> or <Ctrl+C> to exit...");
                Console.WriteLine("Press any key to continue...");

                if (ReadKey())
                {
                    return;
                }

                Run(args);

                return;
            }
        }

        root.SetAction(parsed =>
        {
            var parsedArgs = new Args
            {
                LazerPath = parsed.GetValue(lazerPath)?.FullName!,
                OutPath = parsed.GetValue(outPath)?.FullName!,
                ReplayPath = parsed.GetValue(replayPath)?.FullName,
                IsCopy = parsed.GetValue(isCopy),
                IsQuiet = parsed.GetValue(isQuiet),
                IsVerbose = parsed.GetValue(isVerbose),
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