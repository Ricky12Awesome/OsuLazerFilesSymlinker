// See https://aka.ms/new-console-template for more information

using System.CommandLine;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    public IList<Beatmap> Beatmaps { get; } = null!;
}

[Preserve(AllMembers = true)]
public class BeatmapMetadata : RealmObject
{
    public string Title { get; private set; } = null!;
    public string TitleUnicode { get; private set; } = null!;
    public string Artist { get; private set; } = null!;
    public string ArtistUnicode { get; private set; } = null!;
    public string Source { get; private set; } = null!;
    public string AudioFile { get; private set; } = null!;
    public string BackgroundFile { get; private set; } = null!;
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

    [Ignored] public string Path => System.IO.Path.Join(Hash[..1], Hash[..2], Hash);
}

[Preserve(AllMembers = true)]
public record FileOutput
{
    public required string Filename { get; init; }
    public required string Path { get; init; }
}

[Preserve(AllMembers = true)]
class Api
{
    public Realm Realm { get; private set; } = null!;
    public string LazerPath { get; private set; } = null!;
    public bool Verbose { get; private set; }

    public Api(string? _lazerPath, bool verbose)
    {
        var lazerPath = _lazerPath ?? GetDefaultLazerPath();
        var realmPath = Path.Join(lazerPath, "client.realm");
        var filesPath = Path.Join(lazerPath, "files");

        if (!Path.Exists(lazerPath)) throw new FileNotFoundException(lazerPath);
        if (!Path.Exists(realmPath)) throw new FileNotFoundException(realmPath);
        if (!Path.Exists(filesPath)) throw new FileNotFoundException(filesPath);

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
        Verbose = verbose;
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
    public string GetMD5HashFromReplay(string? path)
    {
        if (!Path.Exists(path)) throw new FileLoadException("Path to replay file not found");

        using var stream = System.IO.File.OpenRead(path);

        if (!stream.CanRead) throw new FileLoadException("Failed to read replay file.");

        Span<byte> buf = stackalloc byte[39];
        stream.ReadExactly(buf);

        return Encoding.UTF8.GetString(buf[7..]);
    }

    public void ValidatePaths(string outPath)
    {
        Console.WriteLine("Validating paths, this can take a while...");

        var files = Directory.EnumerateFiles(outPath, "*", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            var info = System.IO.File.ResolveLinkTarget(file, false);

            if (info is not null && !info.Exists)
            {
                Console.WriteLine($"Removing {file}");
                System.IO.File.Delete(file);
            }
        }

        var dirs = Directory.EnumerateDirectories(outPath, "*", SearchOption.AllDirectories);

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

    public void CreateLinksAll(string outPath, bool isCopy = false)
    {
        Console.WriteLine("Creating links...");
        var beatmaps = Realm.All<Beatmap>();

        foreach (var beatmap in beatmaps)
        {
            CreateLinks(beatmap, outPath, isCopy);
        }

        Console.WriteLine("Done.");
    }

    public void CreateLinks(BeatmapSet set, string outPath, bool isCopy = false)
    {
        var dirname = set.OnlineID.ToString();
        var dirpath = Path.Join(outPath, dirname);

        if (Directory.Exists(dirpath))
        {
            return;
        }

        Directory.CreateDirectory(dirpath);

        foreach (var f in set.Files)
        {
            var src = Path.Join(LazerPath, "files", f.File.Hash[..1], f.File.Hash[..2], f.File.Hash);
            var dst = Path.Join(dirpath, f.Filename);

            if (Verbose)
            {
                Console.WriteLine($"{src} -> {dirname}/{f.Filename}");
            }

            Directory.CreateDirectory(Directory.GetParent(dst)!.FullName);

            if (isCopy)
            {
                System.IO.File.Copy(src, dst, false);
                return;
            }

            System.IO.File.CreateSymbolicLink(dst, src);
        }
    }

    public void CreateLinks(Beatmap beatmap, string outPath, bool isCopy = false)
    {
        var mapFiles = beatmap.BeatmapSet.Files;

        // just id because parsing title and artist can be invalid for paths
        var dirname = beatmap.BeatmapSet.OnlineID.ToString();
        var dirpath = Path.Join(outPath, dirname);

        if (Directory.Exists(dirpath))
        {
            return;
        }

        Directory.CreateDirectory(dirpath);

        foreach (var f in mapFiles)
        {
            var src = Path.Join(LazerPath, "files", f.File.Hash[..1], f.File.Hash[..2], f.File.Hash);
            var dst = Path.Join(dirpath, f.Filename);

            if (Verbose)
            {
                Console.WriteLine($"{src} -> {dirname}/{f.Filename}");
            }

            Directory.CreateDirectory(Directory.GetParent(dst)!.FullName);

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
        public required string? DiffLazerPath { get; init; }
        public const string DefaultOutPath = "./YOU-CAN-RENAME-THIS-AND-MOVE-THIS-ON-SAME-DRIVE";
        public required string OutPath { get; init; }
        public required string? ReplayPath { get; init; }
        public required bool IsCopy { get; init; }
        public required bool IsQuiet { get; init; }
        public required bool IsVerbose { get; init; }
        public required bool All { get; init; }
        public required ExportFormat? Export { get; init; }
        public required bool Validate { get; init; }
        public required string? MD5Hash { get; init; }
        public required long? OnlineID { get; init; }

        public bool CannotRun()
        {
            return this is
            {
                All: false,
                DiffLazerPath: null,
                Validate: false,
                MD5Hash: null,
                OnlineID: null,
                ReplayPath: null,
                Export: null,
            };
        }

        public bool CannotContinue()
        {
            return this is { All: false, DiffLazerPath: null, MD5Hash: null, OnlineID: null, ReplayPath: null };
        }

        public enum ExportFormat
        {
            Json,
            PrettyJson,
            Binary1,
            Binary2
        }
    }

    private static void ExportJson(this Api api, string? outPath, bool pretty)
    {
        var sets = api.Realm.All<BeatmapSet>();
        var root = new JsonObject();
        var beatmapSetsRoot = new JsonArray();

        foreach (var set in sets)
        {
            var beatmapSetRoot = new JsonObject();
            var beatmapsRoot = new JsonArray();
            var filesRoot = new JsonObject();

            foreach (var file in set.Files)
            {
                filesRoot[file.Filename] = file.File.Hash;
            }

            beatmapSetRoot.Add("OnlineID", set.OnlineID);
            beatmapSetRoot.Add("Files", filesRoot);

            foreach (var beatmap in set.Beatmaps)
            {
                var beatmapRoot = new JsonObject
                {
                    ["MD5Hash"] = beatmap.MD5Hash,
                    ["OnlineID"] = beatmap.OnlineID,
                    ["Title"] = beatmap.Metadata.Title,
                    ["TitleUnicode"] = beatmap.Metadata.TitleUnicode != string.Empty
                        ? beatmap.Metadata.TitleUnicode
                        : null,
                    ["Artist"] = beatmap.Metadata.Artist,
                    ["ArtistUnicode"] = beatmap.Metadata.ArtistUnicode != string.Empty
                        ? beatmap.Metadata.ArtistUnicode
                        : null,
                    ["Source"] = beatmap.Metadata.Source != string.Empty ? beatmap.Metadata.Source : null,
                    ["AudioFile"] = beatmap.Metadata.AudioFile,
                    ["BackgroundFile"] = beatmap.Metadata.BackgroundFile != string.Empty
                        ? beatmap.Metadata.BackgroundFile
                        : null
                };

                beatmapsRoot.Add(beatmapRoot);
            }

            beatmapSetRoot.Add("Beatmaps", beatmapsRoot);
            beatmapSetsRoot.Add(beatmapSetRoot);
        }

        root.Add("BeatmapSets", beatmapSetsRoot);

        var json = root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = pretty,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

        if (outPath is not null)
        {
            Console.WriteLine($"Saving to {outPath}...");

            var file = System.IO.File.CreateText(outPath);
            file.Write(json);
            file.Close();
            Console.WriteLine($"Done.");
        }
        else
        {
            Console.WriteLine(json);
        }
    }

    private static void WriteString(this BinaryWriter writer, string value, bool mode)
    {
        var bytes = Encoding.UTF8.GetBytes(value);

        if (mode)
        {
            if (bytes.Length >= byte.MaxValue)
            {
                throw new OverflowException("String too long, use Binary2 instead");
            }

            writer.Write((byte)bytes.Length);
        }
        else
        {
            writer.Write((uint)bytes.Length);
        }

        writer.Write(bytes);
    }

    private static void WriteHash(this BinaryWriter writer, string value)
    {
        var bytes = Convert.FromHexString(value);
        writer.Write(bytes);
    }

    private static void ExportBinary(this Api api, string? outPath, bool mode)
    {
        var sets = api.Realm.All<BeatmapSet>().ToList();
        var stream = outPath is not null
            ? new FileStream(outPath, FileMode.OpenOrCreate)
            : Console.OpenStandardOutput();

        var writer = new BinaryWriter(stream);

        writer.Write(mode);
        writer.Write((uint)sets.Count);
        foreach (var set in sets)
        {
            writer.Write(set.OnlineID);
            writer.Write((uint)set.Files.Count);

            foreach (var file in set.Files)
            {
                writer.WriteString(file.Filename, mode);
                writer.WriteHash(file.File.Hash);
            }

            writer.Write((uint)set.Beatmaps.Count);
            foreach (var beatmap in set.Beatmaps)
            {
                writer.WriteHash(beatmap.MD5Hash);
                writer.Write(beatmap.OnlineID);
                writer.WriteString(beatmap.Metadata.Title, mode);
                writer.WriteString(beatmap.Metadata.TitleUnicode, mode);
                writer.WriteString(beatmap.Metadata.Artist, mode);
                writer.WriteString(beatmap.Metadata.ArtistUnicode, mode);
                writer.WriteString(beatmap.Metadata.Source, mode);
                writer.WriteString(beatmap.Metadata.AudioFile, mode);
                writer.WriteString(beatmap.Metadata.BackgroundFile, mode);
            }
        }

        writer.Flush();
        writer.Close();
    }

    private static void RunExport(this Api api, Args.ExportFormat format, string? outPath)
    {
        outPath = outPath is Args.DefaultOutPath ? null : outPath;

        if (Directory.Exists(outPath))
        {
            Console.WriteLine($"{outPath} is a directory not a file");
            return;
        }

        switch (format)
        {
            case Args.ExportFormat.Json:
                api.ExportJson(outPath, false);
                break;
            case Args.ExportFormat.PrettyJson:
                api.ExportJson(outPath, true);
                break;
            case Args.ExportFormat.Binary1:
                api.ExportBinary(outPath, true);
                break;
            case Args.ExportFormat.Binary2:
                api.ExportBinary(outPath, false);
                break;
        }
    }

    private static void RunDiff(this Api api, string outPath, string diffLazerPath, bool isCopy)
    {
        var diff = new Api(diffLazerPath, api.Verbose);
        var lookup = diff.Realm.All<BeatmapSet>().ToLookup(set => set.OnlineID);
        var main = api.Realm.All<BeatmapSet>();

        Console.WriteLine("Creating links (difference)...");
        foreach (var set in main)
        {
            if (lookup.Contains(set.OnlineID))
            {
                continue;
            }

            api.CreateLinks(set, outPath, isCopy);
        }
        Console.WriteLine("Done.");
    }

    private static void Run(Args args)
    {
        if (args.CannotRun())
        {
            Console.WriteLine("Nothing to do, use one of [-a, -m, -i, -r] options");
            return;
        }

        var api = new Api(args.LazerPath, args.IsVerbose);

        if (args.IsQuiet)
        {
            Console.SetOut(TextWriter.Null);
        }

        if (args.Export is not null)
        {
            api.RunExport(args.Export.Value, args.OutPath);
            return;
        }

        if (System.IO.File.Exists(args.OutPath))
        {
            throw new FileLoadException("not a directory", args.OutPath);
        }

        if (!Directory.Exists(args.OutPath))
        {
            Directory.CreateDirectory(args.OutPath);
        }

        if (args.Validate)
        {
            api.ValidatePaths(args.OutPath);

            if (args.CannotContinue())
            {
                return;
            }
        }

        if (args.DiffLazerPath is not null)
        {
            api.RunDiff(args.OutPath, args.DiffLazerPath, args.IsCopy);

            return;
        }

        if (args.All)
        {
            api.CreateLinksAll(args.OutPath, args.IsCopy);

            return;
        }

        if (args.MD5Hash is not null || args.ReplayPath is not null)
        {
            var md5hash = args.MD5Hash ?? api.GetMD5HashFromReplay(args.ReplayPath);

            var beatmap = api.Realm.All<Beatmap>().First(b => b.MD5Hash == md5hash);

            api.CreateLinks(beatmap, args.OutPath, args.IsCopy);

            return;
        }

        if (args.OnlineID is not null)
        {
            var onlineID = args.OnlineID;
            // Why do I need this?
            Func<IList<Beatmap>, bool> hasAny = maps => maps.Any(map => map.OnlineID == onlineID);

            var beatmap = api.Realm.All<BeatmapSet>().First(b =>
                b.OnlineID == onlineID || hasAny(b.Beatmaps));
            // this will not work "Unhandled exception: System.NotSupportedException: The method 'Any' is not supported"
            // b.OnlineID == onlineID || b.Beatmaps.Any(map => map.OnlineID == onlineID));

            api.CreateLinks(beatmap, args.OutPath, args.IsCopy);
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
        Option<DirectoryInfo?> diffLazerPath = new("--diff")
        {
            DefaultValueFactory = _ => null,
            Description = "Other Path to lazer directory (will implicitly use --all)"
        };
        Option<string> outPath = new("--out", "-o")
        {
            DefaultValueFactory = _ => Args.DefaultOutPath,
            Description = "Path to output directory or file (file is for exports)"
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
        Option<Args.ExportFormat?> export = new("--export", "-e")
        {
            DefaultValueFactory = _ => null,
            Description = "Exports to a specified format, if no out file is specified prints to stdout"
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
        root.Options.Add(diffLazerPath);
        root.Options.Add(outPath);
        root.Options.Add(replayPath);
        root.Options.Add(isCopy);
        root.Options.Add(isQuiet);
        root.Options.Add(isVerbose);
        root.Options.Add(all);
        root.Options.Add(export);
        root.Options.Add(validate);
        root.Options.Add(md5hash);
        root.Options.Add(onlineId);

        var defLazerPath = lazerPath.DefaultValueFactory(null!).FullName;

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
                    DiffLazerPath = null,
                    OutPath = Path.GetFullPath(_args[0]),
                    ReplayPath = null,
                    IsCopy = false,
                    IsQuiet = false,
                    IsVerbose = true,
                    All = true,
                    Export = null,
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
                var defOutPath = new DirectoryInfo(outPath.DefaultValueFactory(null!)).FullName;

                var args = new Args
                {
                    LazerPath = defLazerPath,
                    DiffLazerPath = null,
                    OutPath = defOutPath,
                    ReplayPath = null,
                    IsCopy = false,
                    IsQuiet = false,
                    IsVerbose = true,
                    All = true,
                    Export = null,
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
                DiffLazerPath = parsed.GetValue(diffLazerPath)?.FullName!,
                OutPath = parsed.GetValue(outPath)!,
                ReplayPath = parsed.GetValue(replayPath)?.FullName,
                IsCopy = parsed.GetValue(isCopy),
                IsQuiet = parsed.GetValue(isQuiet),
                IsVerbose = parsed.GetValue(isVerbose),
                All = parsed.GetValue(all),
                Export = parsed.GetValue(export),
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