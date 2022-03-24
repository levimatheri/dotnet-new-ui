namespace DotnetNewUI.NuGet;

using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

public static class PackageInspector
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static IReadOnlyList<CompositeTemplateManifest> GetTemplateManifestsFromPackage(string packagePath, bool isBuiltIn)
    {
        var (packageName, packageVersion) = GetPackageNameAndVersion(packagePath);

        using var file = File.OpenRead(packagePath);
        using var package = new ZipArchive(file);

        var templateManifestRegex = new Regex("^(content/)?(?<template>.*)/\\.template\\.config/template\\.json$");

        var templateManifests = package.Entries
            .Select(x => templateManifestRegex.Match(x.FullName) is { Success: true } ? x : null)
            .Where(x => x is not null)
            .Select(x => x!)
            .Select(x =>
            {
                var archive = x.Archive;
                var parentDirectory = Path.GetDirectoryName(x.FullName)!;

                var templateManifest = GetTemplateManifest(x);
                var ideHostManifest = TryGetIdeHostManifest(archive, parentDirectory);
                var base64Icon = TryGetBase64Icon(archive, parentDirectory, ideHostManifest?.Icon);

                return new CompositeTemplateManifest(packageName, packageVersion, base64Icon, isBuiltIn, templateManifest, ideHostManifest);
            })
            .ToList();

        return templateManifests;
    }

    public static (string PackageName, string Version) GetPackageNameAndVersion(string filePath)
    {
        var fileName = Path.GetFileName(filePath);

        var regex = new Regex("^(?<packagename>.*)\\.(?<version>\\d*\\.\\d*\\.\\d*-?.*)\\.nupkg$");
        var match = regex.Match(fileName);
        var packageName = match.Groups["packagename"].Value;
        var version = match.Groups["version"].Value;

        return (packageName, version);
    }

    private static TemplateManifest GetTemplateManifest(ZipArchiveEntry templateFile)
    {
        using var templateFileStream = templateFile.Open();
        var templateFileContent = JsonSerializer.Deserialize<TemplateManifest>(templateFileStream, JsonSerializerOptions)!;
        return templateFileContent;
    }

    private static TemplateIdeHostManifest? TryGetIdeHostManifest(ZipArchive archive, string parentDirectory)
    {
        var ideHostFilePath = Path.Combine(parentDirectory, "ide.host.json").Replace("\\", "/");
        var ideHostFile = archive.Entries.FirstOrDefault(e => e.FullName == ideHostFilePath);
        if (ideHostFile is not null)
        {
            using var ideHostFileStream = ideHostFile.Open();
            var ideHostFileContent = JsonSerializer.Deserialize<TemplateIdeHostManifest>(ideHostFileStream, JsonSerializerOptions)!;

            return ideHostFileContent;
        }

        return null;
    }

    private static string? TryGetBase64Icon(ZipArchive archive, string parentDirectory, string? relativeIconPath)
    {
        if (relativeIconPath is not null)
        {
            var iconFilePath = Path.Combine(parentDirectory, relativeIconPath).Replace("\\", "/");
            var iconFileType = Path.GetExtension(iconFilePath)[1..];
            var iconFile = archive.Entries.FirstOrDefault(e => e.FullName == iconFilePath);

            if (iconFile is not null)
            {
                using var iconFileStream = iconFile.Open();
                using var memoryStream = new MemoryStream();
                iconFileStream.CopyTo(memoryStream);
                var bytes = memoryStream.ToArray();
                var base64Icon = Convert.ToBase64String(bytes);

                return $"data:image/{iconFileType};base64,{base64Icon}";
            }
        }

        return null;
    }
}
