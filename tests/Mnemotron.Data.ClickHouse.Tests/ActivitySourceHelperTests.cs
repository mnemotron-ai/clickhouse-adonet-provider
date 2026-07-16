using System.Diagnostics;
using Mnemotron.Data.ClickHouse.Diagnostic;
using NUnit.Framework;

namespace Mnemotron.Data.ClickHouse.Tests;

[TestFixture]
public class ActivitySourceHelperTests : AbstractConnectionTestFixture
{
    [Test]
    public void ShouldCreateActivity()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => true,
            SampleUsingParentId = (ref ActivityCreationOptions<string> activityOptions) => ActivitySamplingResult.AllData,
            Sample = (ref ActivityCreationOptions<ActivityContext> activityOptions) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = connection.StartActivity("TestActivity");
        ClassicAssert.NotNull(activity);
    }
}
