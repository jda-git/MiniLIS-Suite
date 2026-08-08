using FluentAssertions;
using MiniLIS.Infrastructure.Services;
using System.Collections.Generic;
using Xunit;

namespace MiniLIS.Tests
{
    public class PercentileCalculatorTests
    {
        [Fact]
        public void NearestRank_median_of_five_known_values()
        {
            // rank = ceil(50/100 * 5) = ceil(2.5) = 3 -> índice 2 (0-based) = 30
            var sorted = new List<double> { 10, 20, 30, 40, 50 };

            PercentileCalculator.NearestRank(sorted, 50).Should().Be(30);
            PercentileCalculator.Median(sorted).Should().Be(30);
        }

        [Fact]
        public void NearestRank_p90_of_five_known_values()
        {
            // rank = ceil(90/100 * 5) = ceil(4.5) = 5 -> índice 4 (0-based) = 50
            var sorted = new List<double> { 10, 20, 30, 40, 50 };

            PercentileCalculator.NearestRank(sorted, 90).Should().Be(50);
        }

        [Fact]
        public void NearestRank_of_single_value_returns_that_value_for_any_percentile()
        {
            var sorted = new List<double> { 42 };

            PercentileCalculator.NearestRank(sorted, 1).Should().Be(42);
            PercentileCalculator.NearestRank(sorted, 50).Should().Be(42);
            PercentileCalculator.NearestRank(sorted, 100).Should().Be(42);
        }

        [Fact]
        public void NearestRank_of_empty_list_returns_zero_without_throwing()
        {
            var sorted = new List<double>();

            PercentileCalculator.NearestRank(sorted, 50).Should().Be(0);
        }

        [Fact]
        public void NearestRank_ten_values_matches_hand_computed_ranks()
        {
            var sorted = new List<double> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

            // P50: ceil(0.5*10)=5 -> índice 4 = 5
            PercentileCalculator.NearestRank(sorted, 50).Should().Be(5);
            // P90: ceil(0.9*10)=9 -> índice 8 = 9
            PercentileCalculator.NearestRank(sorted, 90).Should().Be(9);
            // P10: ceil(0.1*10)=1 -> índice 0 = 1
            PercentileCalculator.NearestRank(sorted, 10).Should().Be(1);
        }
    }
}
