using System.Xml.Serialization;

namespace Aiursoft.Apkg.Sdk.Models;

[XmlRoot("Manifest")]
public class ApkgManifest
{
    [XmlElement("Package")]
    public string Package { get; set; } = string.Empty;

    [XmlElement("Version")]
    public string Version { get; set; } = string.Empty;

    [XmlElement("Maintainer")]
    public string Maintainer { get; set; } = string.Empty;

    [XmlElement("Description")]
    public string Description { get; set; } = string.Empty;

    [XmlElement("Homepage")]
    public string Homepage { get; set; } = string.Empty;

    [XmlElement("License")]
    public string License { get; set; } = "MIT";

    /// <summary>
    /// APT component, e.g. "main", "universe", "restricted", "multiverse".
    /// </summary>
    [XmlElement("Component")]
    public string Component { get; set; } = "main";

    [XmlArray("Targets")]
    [XmlArrayItem("Target")]
    public List<ManifestTarget> Targets { get; set; } = [];
}
