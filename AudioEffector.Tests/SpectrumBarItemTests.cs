using AudioEffector.Presentation.ViewModels;
using Xunit;

namespace AudioEffector.Tests
{
    public class SpectrumBarItemTests
    {
        [Fact]
        public void PropertyChanged_FiresWhenValueChanges()
        {
            var item = new SpectrumBarItem();
            string? changedProp = null;
            item.PropertyChanged += (s, e) => changedProp = e.PropertyName;

            item.Value = 42.5;

            Assert.Equal(42.5, item.Value);
            Assert.Equal(nameof(SpectrumBarItem.Value), changedProp);
        }

        [Fact]
        public void PropertyChanged_FiresWhenPeakValueChanges()
        {
            var item = new SpectrumBarItem();
            string? changedProp = null;
            item.PropertyChanged += (s, e) => changedProp = e.PropertyName;

            item.PeakValue = 65.0;

            Assert.Equal(65.0, item.PeakValue);
            Assert.Equal(nameof(SpectrumBarItem.PeakValue), changedProp);
        }

        [Fact]
        public void PeakHoldCount_CanBeAssigned()
        {
            var item = new SpectrumBarItem();
            item.PeakHoldCount = 15;

            Assert.Equal(15, item.PeakHoldCount);
        }
    }
}
