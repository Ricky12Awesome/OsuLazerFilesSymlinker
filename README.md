# OsuLazerFilesSymlinker
Creates [symlinks](https://en.wikipedia.org/wiki/Symbolic_link) from [osu lazers hashed based files](https://osu.ppy.sh/wiki/en/Client/Release_stream/Lazer/File_storage) into classic like format

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

if you delete a map in lazer you can drag and drop the `osu!/Songs` folder on the exe to revalidate it (can also use `-v` options)

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

### Danser
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