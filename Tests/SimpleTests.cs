using NUnit.Framework;

namespace Tests
{
	[TestFixture]
	public class SimpleTests
	{
		[Test]
		public void SimpleTest_ShouldPass()
		{
			Assert.Pass("This test should pass.");
		}
	}
}
