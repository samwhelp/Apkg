using System.Xml.Serialization;

namespace Aiursoft.Apkg.Sdk.Models;

public class ManifestTarget
{
    [XmlElement("Distro")]
    public string Distro { get; set; } = "ubuntu";

    /// <summary>
    /// Space-separated list of APT suite names this target applies to.
    /// e.g. "plucky plucky-updates plucky-security"
    /// </summary>
    [XmlElement("Suites")]
    public string Suites { get; set; } = string.Empty;

    [XmlElement("Architecture")]
    public string Architecture { get; set; } = "amd64";

    /// <summary>
    /// Relative path inside the .apkg archive, e.g. "debs/mypkg_1.0_amd64.deb"
    /// </summary>
    [XmlElement("DebFile")]
    public string DebFile { get; set; } = string.Empty;

    [XmlIgnore]
    public string[] SuiteList => Suites
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
