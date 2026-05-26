using Aiursoft.Apkg.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.ApkgUploadsViewModels;

public class ApkgUploadsIndexViewModel : UiStackLayoutViewModel
{
    public ApkgUploadsIndexViewModel()
    {
        PageTitle = "My Packages (APKG)";
    }

    public required List<ApkgUpload> Uploads { get; init; }
    public bool IsAdmin { get; init; }
}
