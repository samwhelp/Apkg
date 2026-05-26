using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Sdk.Models;

namespace Aiursoft.Apkg.Models.ApkgUploadsViewModels;

public class ApkgPreviewTargetInfo
{
    public required ManifestTarget Target { get; init; }
    public required List<AptRepository> MatchingRepositories { get; init; }
    public bool HasMatch => MatchingRepositories.Count > 0;
}
