using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.ApkgUploadsViewModels;

public class ApkgUploadsUploadViewModel : UiStackLayoutViewModel
{
    public ApkgUploadsUploadViewModel()
    {
        PageTitle = "Upload APKG Package";
    }

    [Display(Name = ".apkg File")]
    [Required(ErrorMessage = "Please upload a valid .apkg file.")]
    [MaxLength(512)]
    [RegularExpression(@"^apkg-upload/.*", ErrorMessage = "Please upload a valid .apkg file.")]
    public string? ApkgFilePath { get; set; }
}
