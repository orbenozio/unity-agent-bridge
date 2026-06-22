using NUnit.Framework;

// A couple of trivial EditMode tests so `run_tests` has something real to run,
// and to tie the live demo to the CI story (the same tests run headless via -runTests).
public class DemoTests
{
    [Test]
    public void Math_Adds()
    {
        Assert.AreEqual(4, 2 + 2);
    }

    [Test]
    public void ProjectName_ContainsMcp()
    {
        Assert.IsTrue("unity-mcp-bridge".Contains("mcp"));
    }
}
