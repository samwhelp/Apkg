namespace Aiursoft.Apkg.WebTests.IntegrationTests;

[TestClass]
public class HomeControllerTests : TestBase
{
    [TestMethod]
    public async Task GetIndex()
    {
        var url = "/";
        var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
    }

    [TestMethod]
    public async Task GetIndex_ShouldRenderArchitectTestimonialSection()
    {
        // Arrange
        var url = "/";

        // Act
        var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        // Assert: section headers
        Assert.IsTrue(html.Contains("The Philosophy"),
            "Index page should render the 'The Philosophy' section label.");
        Assert.IsTrue(html.Contains("From the Architect"),
            "Index page should render the 'From the Architect' section heading.");

        // Assert: the quote card is present
        Assert.IsTrue(html.Contains("apkg-quote"),
            "Index page should render the architect quote blockquote with 'apkg-quote' class.");

        // Assert: key phrases from the quote text
        Assert.IsTrue(html.Contains("declarative manifest"),
            "Index page should contain the phrase 'declarative manifest' in the quote.");
        Assert.IsTrue(html.Contains("black art"),
            "Index page should contain the phrase 'black art' in the quote.");
        Assert.IsTrue(html.Contains("standard software engineering"),
            "Index page should contain the phrase 'standard software engineering' in the quote.");

        // Assert: author attribution
        Assert.IsTrue(html.Contains("Anduin Xue"),
            "Index page should display the author name 'Anduin Xue'.");
        Assert.IsTrue(html.Contains("Founder") && html.Contains("Architect") && html.Contains("Aiursoft"),
            "Index page should display the author title with 'Founder', 'Architect', and 'Aiursoft'.");
    }

    [TestMethod]
    public async Task GetSelfHost()
    {
        var response = await Http.GetAsync("/Home/SelfHost");
        response.EnsureSuccessStatusCode();
    }
}
