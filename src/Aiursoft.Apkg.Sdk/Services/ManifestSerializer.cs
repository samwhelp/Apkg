using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Aiursoft.Apkg.Sdk.Models;

namespace Aiursoft.Apkg.Sdk.Services;

public class ManifestSerializer
{
    private static readonly XmlSerializer LegacySerializer = new(typeof(ApkgManifest));
    private static readonly XmlSerializer PackageSerializer = new(typeof(ApkgPackageManifest));

    public string Serialize(ApkgManifest manifest)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = false
        };

        using var ms = new MemoryStream();
        using (var writer = XmlWriter.Create(ms, settings))
            LegacySerializer.Serialize(writer, manifest);

        return new UTF8Encoding(false).GetString(ms.ToArray());
    }

    public ApkgManifest Deserialize(string xml)
    {
        using var reader = new StringReader(xml);
        return (ApkgManifest)LegacySerializer.Deserialize(reader)!;
    }

    public async Task<ApkgManifest> DeserializeFromFileAsync(string path)
    {
        var xml = await File.ReadAllTextAsync(path);
        return Deserialize(xml);
    }

    public async Task SerializeToFileAsync(ApkgManifest manifest, string path)
    {
        var xml = Serialize(manifest);
        await File.WriteAllTextAsync(path, xml, Encoding.UTF8);
    }

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
