using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.Apkg.Entities;

[ExcludeFromCodeCoverage]
public class ApkgUpload
{
    [Key]
    public int Id { get; set; }

    [Required]
    public required string UploadedByUserId { get; set; }

    [ForeignKey(nameof(UploadedByUserId))]
    public User? UploadedByUser { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    [Required]
    [MaxLength(256)]
    public required string FileName { get; set; }

    [Required]
    [MaxLength(128)]
    public required string Package { get; set; }

    [Required]
    [MaxLength(128)]
    public required string Version { get; set; }

    [Required]
    [MaxLength(128)]
    public required string Component { get; set; }

    [MaxLength(256)]
    public string? Description { get; set; }

    [MaxLength(256)]
    public string? Maintainer { get; set; }

    [MaxLength(512)]
    public string? Homepage { get; set; }

    [MaxLength(512)]
    public string? VaultPath { get; set; }

    public bool IsPublished { get; set; }

    public bool IsListed { get; set; } = true;

    public ICollection<LocalPackage> Packages { get; set; } = [];
}
