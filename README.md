# OsuLazerFilesSymlinker

Creates [symlinks](https://en.wikipedia.org/wiki/Symbolic_link)
from [osu lazers hashed based files](https://osu.ppy.sh/wiki/en/Client/Release_stream/Lazer/File_storage) into classic
like format

## Download

Download latest executable directly from
[releases](https://github.com/Ricky12Awesome/OsuLazerFilesSymlinker/releases/latest)

- [Windows x64](https://github.com/Ricky12Awesome/OsuLazerFilesSymlinker/releases/latest/download//OsuLazerFilesSymlinker-win-x64.exe)
- [Linux x64](https://github.com/Ricky12Awesome/OsuLazerFilesSymlinker/releases/latest/download//OsuLazerFilesSymlinker-linux-x64)
- [macOS x64](https://github.com/Ricky12Awesome/OsuLazerFilesSymlinker/releases/latest/download//OsuLazerFilesSymlinker-osx-x64)
- [macOS ARM64](https://github.com/Ricky12Awesome/OsuLazerFilesSymlinker/releases/latest/download//OsuLazerFilesSymlinker-osx-arm64)

## Can this be used for osu classic?

Yes this technically works but

**avoid downloading and deleting maps in classic**

if you delete a map in lazer you can drag and drop the `osu!/Songs` folder on the exe to revalidate it (can also use
`-v` options)

**make sure you import your maps from classic to lazer first**

**do not import maps after you symlinked them already**

This will mess up the order of stuff like `Date Added`

## Basic Usage

SImply run the exe and it will create a folder
`YOU-CAN-RENAME-THIS-AND-MOVE-THIS-ON-SAME-DRIVE`
you can rename the folder to anything and be move anywhere on same drive
if you move this folder to a different drive it will copy files and not be symlinks anymore (at least on windows)

You can also drag and drop a folder on the program, and it will use that for output directory

## CLI Usage

`<executable>` is the path to the executable file like `./path/to/OsuLazerFilesSymlinker-win-x64.exe`

on windows, you can drag and drop the exe into terminal to paste path directly

---
Probably the most common use case, this will map ALL beatmaps from lazer to `./songs` directory
these files take no space since they're symlinks pointing to the original file

```sh
<executable> -o ./songs -a
```

---
Should run this if any maps gets deleted, this will validate all files in `./songs` and remove invalid symlinks

```sh
<executable> -o ./songs -v
```

---
This will link only the beatmap used in this replay

```sh
<executable> -o ./songs -r path/to/replay.osr
```

---

## Export

This will export into json (use `PrettyJson` for formated json)

```sh
<executable> -e Json -o out.json
```

---
This will export into binary format (this is experimental)

```sh
<executable> -e Binray -o out.bin
```

---

### Format

#### Json

```json5
{
  "BeatmapSets": [
    {
      "OnlineID": -1,
      "Files": {
        "audio.mp3": "47b895484e7751f3ab429694ff6dbf21e774ab023e4f6c5b481476f04ff22f0f",
        "cYsmix - triangles (peppy) [peppy].osu": "a1556d0801b3a6b175dda32ef546f0ec812b400499f575c44fccbe9c67f9b1e5"
      },
      "Beatmaps": [
        {
          "MD5Hash": "27d9765612170a9517f0a5e8b4613f06",
          "OnlineID": 0,
          "Title": "triangles",
          "TitleUnicode": "triangles",
          "Artist": "cYsmix",
          "ArtistUnicode": "cYsmix",
          "Source": null,
          "AudioFile": "audio.mp3",
          "BackgroundFile": null
        }
      ]
    },
    // ..
  ]
}
```

#### Binary (Experimental)

- `signed int` BeatmapSet Count
    - `signed long` OnlineID
    - `signed int` Files Count
        - `string` Filename
        - `string` Hash
    - `signed int` Beatmap Count
        - `string` MD5Hash
        - `signed long` OnlineID
        - `string` Title
        - `string` TitleUnicode
        - `string` Artist
        - `string` ArtistUnicode
        - `string` Source
        - `string` AudioFile
        - `string` BackgroundFile


- `string` type is formatted like
    - `signed int` Length
    - `bytes` UTF-8 Data
    - if Length is zero it will just be `signed int 0`

Example (simplified)

```csharp
1                                                                   // BeatmapSet Count
-1                                                                  // OnlineID
2                                                                   // Files Count
"audio.mp3"                                                         // files index 0
"47b895484e7751f3ab429694ff6dbf21e774ab023e4f6c5b481476f04ff22f0f"
"cYsmix - triangles (peppy) [peppy].osu"                            // files index 1
"a1556d0801b3a6b175dda32ef546f0ec812b400499f575c44fccbe9c67f9b1e5"
1                                                                   // Beatmap Count
"27d9765612170a9517f0a5e8b4613f06"                                  // MD5Hash
0                                                                   // OnlineID
"triangles"                                                         // Title
"triangles"                                                         // TitleUnicode
"cYsmix"                                                            // Artist
"cYsmix"                                                            // ArtistUnicode
0                                                                   // Source
"audio.mp3"                                                         // AudioFile
0                                                                   // BackgroundFile
```

## Danser

I mainly made for [Danser](https://github.com/Wieku/danser-go)

on windows `\` needs to be escaped so do `\\` instead

edit `OsuSongsDir` in `settings/default.json` in danser directory
`OsuSongsDir` needs to be full path, not relative

```json5
{
  "General": {
    "OsuSongsDir": "path\\to\\output\\directory"
    // ...
  }
  // ...
}
```