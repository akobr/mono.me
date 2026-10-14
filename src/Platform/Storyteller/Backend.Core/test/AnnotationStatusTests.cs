namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

public class AnnotationStatusTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GetStatus_WithoutFlagOrWindow_IsActive()
    {
        AnnotationStatusExtensions.GetStatus(null, null, null, Now).ShouldBe(AnnotationStatus.Active);
        AnnotationStatusExtensions.GetStatus(false, null, null, Now).ShouldBe(AnnotationStatus.Active);
    }

    [Fact]
    public void GetStatus_Disabled_WinsOverTheWindow()
    {
        AnnotationStatusExtensions.GetStatus(true, Now.AddDays(1), Now.AddDays(-1), Now).ShouldBe(AnnotationStatus.Disabled);
    }

    [Fact]
    public void GetStatus_ValidFromInTheFuture_IsScheduled()
    {
        AnnotationStatusExtensions.GetStatus(null, Now.AddTicks(1), null, Now).ShouldBe(AnnotationStatus.Scheduled);
    }

    [Fact]
    public void GetStatus_ValidFromNow_IsActive()
    {
        AnnotationStatusExtensions.GetStatus(null, Now, Now.AddDays(1), Now).ShouldBe(AnnotationStatus.Active);
    }

    [Fact]
    public void GetStatus_ExpiresAtNow_IsExpired()
    {
        AnnotationStatusExtensions.GetStatus(null, null, Now, Now).ShouldBe(AnnotationStatus.Expired);
        AnnotationStatusExtensions.GetStatus(null, null, Now.AddTicks(1), Now).ShouldBe(AnnotationStatus.Active);
    }

    [Fact]
    public void GetStatus_ComparesInstantsWhateverTheOffset()
    {
        // 13:30 and 14:30 in Prague (UTC+2) are 11:30 and 12:30 UTC, around now (12:00 UTC); compared as strings,
        // both would be after it
        var prague = TimeSpan.FromHours(2);
        var validFrom = new DateTimeOffset(2026, 10, 14, 13, 30, 0, prague);
        var expiresAt = new DateTimeOffset(2026, 10, 14, 14, 30, 0, prague);

        AnnotationStatusExtensions.GetStatus(null, validFrom, expiresAt, Now).ShouldBe(AnnotationStatus.Active);
        AnnotationStatusExtensions.GetStatus(null, null, new DateTimeOffset(2026, 10, 14, 13, 59, 0, prague), Now).ShouldBe(AnnotationStatus.Expired);
    }

    [Fact]
    public void GetStatus_InvertedWindow_IsScheduledThenExpired()
    {
        var validFrom = Now.AddDays(1);
        var expiresAt = Now.AddDays(-1);

        AnnotationStatusExtensions.GetStatus(null, validFrom, expiresAt, Now).ShouldBe(AnnotationStatus.Scheduled);
        AnnotationStatusExtensions.GetStatus(null, validFrom, expiresAt, Now.AddDays(2)).ShouldBe(AnnotationStatus.Expired);
    }

    [Fact]
    public void GetStatus_OfAnnotation_ReadsItsMembers()
    {
        var subject = new Subject
        {
            ProjectName = "billing",
            ViewName = "default",
            AnnotationKey = "sbt.northwind",
            Name = "northwind",
            AnnotationType = AnnotationType.Subject,
            ExpiresAt = Now.AddMinutes(-1),
        };

        subject.GetStatus(Now).ShouldBe(AnnotationStatus.Expired);
        (subject with { IsDisabled = true }).GetStatus(Now).ShouldBe(AnnotationStatus.Disabled);
    }
}
