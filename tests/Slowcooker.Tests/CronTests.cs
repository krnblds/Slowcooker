namespace Slowcooker.Tests;

public class CronTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 15, 0, TimeSpan.Zero); // a Wednesday

    [Theory]
    [InlineData("daily", "0 0 * * *")]
    [InlineData("daily-at", "30 6 * * *")]
    [InlineData("hourly", "0 * * * *")]
    [InlineData("hourly-at", "45 * * * *")]
    [InlineData("weekly", "0 0 * * 0")]
    [InlineData("weekly-on-at", "0 9 * * 1")]
    [InlineData("weekly-on-many", "0 0 * * 1,3,5")]
    [InlineData("monthly", "0 0 1 * *")]
    [InlineData("monthly-on-at", "15 0 15 * *")]
    [InlineData("monthly-last", "0 23 L * *")]
    [InlineData("yearly", "0 0 1 1 *")]
    [InlineData("yearly-in-on", "0 0 25 12 *")]
    [InlineData("weekdays-at", "0 18 * * 1-5")]
    [InlineData("weekends", "0 0 * * 0,6")]
    [InlineData("every-minute", "* * * * *")]
    [InlineData("every-second", "* * * * * *")]
    [InlineData("never", "0 0 31 2 *")]
    public void Presets_and_refinements_produce_the_expected_expressions(string shape, string expected)
    {
        Cron cron = shape switch
        {
            "daily" => Cron.Daily,
            "daily-at" => Cron.Daily.At(6, 30),
            "hourly" => Cron.Hourly,
            "hourly-at" => Cron.Hourly.AtMinute(45),
            "weekly" => Cron.Weekly,
            "weekly-on-at" => Cron.Weekly.On(DayOfWeek.Monday).At(9),
            "weekly-on-many" => Cron.Weekly.On(DayOfWeek.Friday, DayOfWeek.Monday, DayOfWeek.Wednesday),
            "monthly" => Cron.Monthly,
            "monthly-on-at" => Cron.Monthly.OnDay(15).At(0, 15),
            "monthly-last" => Cron.Monthly.OnLastDay().At(23),
            "yearly" => Cron.Yearly,
            "yearly-in-on" => Cron.Yearly.InMonth(12).OnDay(25),
            "weekdays-at" => Cron.Weekdays.At(18),
            "weekends" => Cron.Weekends,
            "every-minute" => Cron.EveryMinute,
            "every-second" => Cron.EverySecond,
            _ => Cron.Never,
        };
        Assert.Equal(expected, cron.Expression);
        Assert.Equal(expected, cron);           // implicit string conversion
        Assert.Equal(expected, cron.ToString());
    }

    [Fact]
    public void At_always_pins_hour_and_minute_so_Hourly_At_needs_Parse_for_a_wildcard_hour()
    {
        Assert.Equal("45 0 * * *", Cron.Hourly.At(0, 45).Expression);
        Assert.Equal("45 * * * *", Cron.Hourly.AtMinute(45).Expression);
    }

    [Theory]
    [InlineData(10, "*/10 * * * * *")]
    [InlineData(60, "* * * * *")]
    [InlineData(15 * 60, "*/15 * * * *")]
    [InlineData(3600, "0 * * * *")]
    [InlineData(6 * 3600, "0 */6 * * *")]
    [InlineData(86400, "0 0 * * *")]
    [InlineData(7 * 86400, "0 0 */7 * *")]
    public void Every_maps_intervals_onto_cron_steps(int seconds, string expected)
        => Assert.Equal(expected, Cron.Every(TimeSpan.FromSeconds(seconds)).Expression);

    [Theory]
    [InlineData(0)]
    [InlineData(90)]          // 1.5 minutes
    [InlineData(90 * 60)]     // 1.5 hours
    [InlineData(36 * 3600)]   // 1.5 days
    [InlineData(40 * 86400)]  // > 31 days
    public void Every_rejects_intervals_cron_cannot_express(int seconds)
        => Assert.ThrowsAny<ArgumentException>(() => Cron.Every(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Refinements_validate_their_ranges()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Cron.Daily.At(24));
        Assert.Throws<ArgumentOutOfRangeException>(() => Cron.Daily.At(1, 60));
        Assert.Throws<ArgumentOutOfRangeException>(() => Cron.Monthly.OnDay(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Cron.Yearly.InMonth(13));
        Assert.Throws<ArgumentException>(() => Cron.Weekly.On());
    }

    [Fact]
    public void Parse_accepts_five_and_six_field_expressions_and_rejects_garbage()
    {
        Assert.False(Cron.Parse("0 3 * * sun").HasSeconds);
        Assert.True(Cron.Parse("*/5 * * * * *").HasSeconds);
        Assert.Throws<ArgumentException>(() => Cron.Parse("every tuesday"));
        Assert.Throws<ArgumentException>(() => Cron.Parse(" "));
        Assert.False(Cron.TryParse("nope", out _));
        Assert.True(Cron.TryParse("0 0 * * *", out var cron));
        Assert.Equal(Cron.Daily, cron);
    }

    [Fact]
    public void Next_and_Upcoming_evaluate_in_the_given_time_zone()
    {
        var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"); // UTC+1 in November
        var cron = Cron.Daily.At(6);

        Assert.Equal(new DateTimeOffset(2026, 11, 5, 5, 0, 0, TimeSpan.Zero), cron.Next(Now, berlin));
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 6, 0, 0, TimeSpan.Zero), cron.Next(Now));

        var upcoming = cron.Upcoming(Now, berlin, 3);
        Assert.Equal(3, upcoming.Count);
        Assert.Equal(upcoming[0].AddDays(1), upcoming[1]);
        Assert.Equal(upcoming[1].AddDays(1), upcoming[2]);
    }

    [Fact]
    public void Never_has_no_occurrences()
    {
        Assert.Null(Cron.Never.Next(Now));
        Assert.Empty(Cron.Never.Upcoming(Now, count: 5));
    }

    [Fact]
    public void Values_are_equal_when_their_expressions_are()
    {
        Assert.Equal(Cron.Daily.At(6, 30), Cron.Parse("30 6 * * *"));
        Assert.NotEqual(Cron.Daily, Cron.Hourly);
        Assert.Equal(Cron.Daily.GetHashCode(), Cron.Parse("0 0 * * *").GetHashCode());
    }
}
