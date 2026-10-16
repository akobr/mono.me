using _42.Platform.Storyteller.Transferring;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;

public class ViewArchiveIncludeTests
{
    [Fact]
    public void All_IsEveryKindInImportOrder()
    {
        ViewArchiveInclude.All.ShouldBe(
            [ViewArchiveContent.Annotations, ViewArchiveContent.Schemas, ViewArchiveContent.Templates, ViewArchiveContent.Configurations]);
    }

    [Fact]
    public void Resolve_TakesEveryKindForNoFilter()
    {
        ViewArchiveInclude.Resolve(null).ShouldBe(ViewArchiveInclude.All);
        ViewArchiveInclude.Resolve([]).ShouldBe(ViewArchiveInclude.All);
    }

    [Fact]
    public void Resolve_OrdersTheKindsAndDropsRepeats()
    {
        ViewArchiveInclude.Resolve([ViewArchiveContent.Configurations, ViewArchiveContent.Annotations, ViewArchiveContent.Configurations])
            .ShouldBe([ViewArchiveContent.Annotations, ViewArchiveContent.Configurations]);
    }
}
