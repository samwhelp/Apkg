using Aiursoft.Apkg.Models;

namespace Aiursoft.Apkg.Configuration;

public class SettingsMap
{
    public const string ProjectName = "ProjectName";
    public const string BrandName = "BrandName";
    public const string BrandHomeUrl = "BrandHomeUrl";
    public const string ProjectLogo = "ProjectLogo";
    public const string AllowUserAdjustNickname = "Allow_User_Adjust_Nickname";
    public const string AllowAnonymousViewPackageDetails = "Allow_Anonymous_View_Package_Details";
    public const string AllowAnonymousBrowseRepository = "Allow_Anonymous_Browse_Repository";
    public const string Icp = "Icp";
    public const string DummyNumber = "DummyNumber";
    public const string DummyChoice = "DummyChoice";
    public const string PublicAptServerDomain = "PublicAptServerDomain";

    public class FakeLocalizer
    {
        public string this[string name] => name;
    }

    private static readonly FakeLocalizer Localizer = new();

    public static readonly List<GlobalSettingDefinition> Definitions = new()
    {
        new GlobalSettingDefinition
        {
            Key = ProjectName,
            Name = Localizer["Project Name"],
            Description = Localizer["The name of the project displayed in the frontend."],
            Type = SettingType.Text,
            DefaultValue = "Aiursoft Apkg"
        },
        new GlobalSettingDefinition
        {
            Key = BrandName,
            Name = Localizer["Brand Name"],
            Description = Localizer["The brand name displayed in the footer."],
            Type = SettingType.Text,
            DefaultValue = "Aiursoft"
        },
        new GlobalSettingDefinition
        {
            Key = BrandHomeUrl,
            Name = Localizer["Brand Home URL"],
            Description = Localizer[" The link to the brand's home page."],
            Type = SettingType.Text,
            DefaultValue = "https://www.aiursoft.com/"
        },
        new GlobalSettingDefinition
        {
            Key = ProjectLogo,
            Name = Localizer["Project Logo"],
            Description = Localizer["The logo of the project displayed in the navbar and footer. Support jpg, png, svg."],
            Type = SettingType.File,
            DefaultValue = "",
            Subfolder = "project-logo",
            AllowedExtensions = "jpg png svg",
            MaxSizeInMb = 5
        },
        new GlobalSettingDefinition
        {
            Key = AllowUserAdjustNickname,
            Name = Localizer["Allow User Adjust Nickname"],
            Description = Localizer["Allow users to adjust their nickname in the profile management page."],
            Type = SettingType.Bool,
            DefaultValue = "True"
        },
        new GlobalSettingDefinition
        {
            Key = AllowAnonymousViewPackageDetails,
            Name = Localizer["Allow Anonymous View Package Details"],
            Description = Localizer["Allow anonymous users to view package details without login."],
            Type = SettingType.Bool,
            DefaultValue = "True"
        },
        new GlobalSettingDefinition
        {
            Key = AllowAnonymousBrowseRepository,
            Name = Localizer["Allow Anonymous Browse Repository"],
            Description = Localizer["Allow anonymous users to browse repositories and their packages without login."],
            Type = SettingType.Bool,
            DefaultValue = "True"
        },
        new GlobalSettingDefinition
        {
            Key = Icp,
            Name = Localizer["ICP Number"],
            Description = Localizer["The ICP license number for China mainland users. Leave empty to hide."],
            Type = SettingType.Text,
            DefaultValue = ""
        },
        new GlobalSettingDefinition
        {
            Key = DummyNumber,
            Name = Localizer["Dummy Number"],
            Description = Localizer["A dummy number for testing."],
            Type = SettingType.Number,
            DefaultValue = "0"
        },
        new GlobalSettingDefinition
        {
            Key = DummyChoice,
            Name = Localizer["Dummy Choice"],
            Description = Localizer["A dummy choice for testing."],
            Type = SettingType.Choice,
            DefaultValue = "A",
            ChoiceOptions = new Dictionary<string, string>
            {
                { "A", "Option A" },
                { "B", "Option B" }
            }
        },
        new GlobalSettingDefinition
        {
            Key = PublicAptServerDomain,
            Name = Localizer["Public APT Server Domain"],
            Description = Localizer["The public-facing domain (or full base URL) that APT clients use to reach the static repository files. This domain is stamped into every APT source configuration the server generates — including the one-line install guides on repository detail pages, the .sources file returned by the API, and the SDK usage instructions on API key pages.\n\nWhen rsync export (RepositoryExportJob) materializes the artifacts/ directory onto a separate static file server (nginx, caddy, or a CDN edge node), the domain that APT clients see is different from the web application's own host. Set this to the static server's domain so that apt update and apt install requests go directly to the static server, not to the web app.\n\nExamples:\n  apt.example.com          ← bare domain (the current request scheme — http or https — is prepended automatically)\n  https://apt.example.com  ← full URL (scheme included, used as-is)\n\nWhen to configure:\n  - You run RepositoryExportJob and serve the exported artifacts/ directory from a different machine, CDN, or reverse proxy.\n  - You want APT traffic to bypass the web application entirely and hit a lightweight static file server.\n\nWhen to leave empty:\n  - The web application itself serves the /artifacts/ routes directly (no separate static server).\n  - You are not using the rsync export feature.\n\nDefault (empty): the server uses the current HTTP request's host and scheme, which is correct for single-server deployments.\n\nWhat this setting affects:\n  - Repository Details page → one-line apt setup commands, GPG key download URL, APT URIs\n  - Package Details page → install instructions and APT source URIs\n  - API /api/sources/{id} → generated .sources file content\n  - API Key Usage page → SDK configuration examples"],
            Type = SettingType.Text,
            DefaultValue = ""
        }
    };
}
