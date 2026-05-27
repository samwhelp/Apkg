using System.Xml.Serialization;
using Aiursoft.Apkg.Sdk.Models;

namespace Aiursoft.Apkg.Sdk.Services;

public class ManifestSerializer
{
    private static readonly XmlSerializer PackageSerializer = new(typeof(ApkgPackageManifest));

    public ApkgPackageManifest DeserializePackageManifest(string xml)
    {
        using var reader = new StringReader(xml);
        return (ApkgPackageManifest)PackageSerializer.Deserialize(reader)!;
    }

    public async Task<ApkgPackageManifest> DeserializePackageManifestFromFileAsync(string path)
    {
        var xml = await File.ReadAllTextAsync(path);
        return DeserializePackageManifest(xml);
    }
}
