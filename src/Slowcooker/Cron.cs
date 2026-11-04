using System.Globalization;
using Cronos;

namespace Slowcooker;

/// <summary>
/// A cron schedule you can read. Start from a preset and refine it:
/// <code>
/// Cron.Daily.At(6, 30)                        // "30 6 * * *"
/// Cron.Weekly.On(DayOfWeek.Monday).At(9)      // "0 9 * * 1"
/// Cron.Monthly.OnDay(1).At(0, 15)             // "15 0 1 * *"
/// Cron.Weekdays.At(18)                        // "0 18 * * 1-5"
/// Cron.Hourly.AtMinute(45)                    // "45 * * * *"
/// Cron.Every(TimeSpan.FromMinutes(15))        // "*/15 * * * *"
/// Cron.Every(TimeSpan.FromSeconds(10))        // "*/10 * * * * *"
/// Cron.Parse("0 3 * * sun")                   // anything Cronos understands
/// </code>
/// A <see cref="Cron"/> converts implicitly to its expression string, so every API that takes a cron string takes one of these.
/// Use <see cref="Next"/> and <see cref="Upcoming"/> to preview when it fires in a given time zone.
/// </summary>
public sealed class Cron : IEquatable<Cron>
{
    private readonly string? _second; // null = 5-field expression
    private readonly string _minute;
    private readonly string _hour;
    private readonly string _dayOfMonth;
    private readonly string _month;
    private readonly string _dayOfWeek;
    private readonly CronExpression _parsed;

    private Cron(string? second, string minute, string hour, string dayOfMonth, string month, string dayOfWeek)
    {
        _second = second;
        _minute = minute;
        _hour = hour;
        _dayOfMonth = dayOfMonth;
        _month = month;
        _dayOfWeek = dayOfWeek;
        Expression = second is null
            ? $"{minute} {hour} {dayOfMonth} {month} {dayOfWeek}"
            : $"{second} {minute} {hour} {dayOfMonth} {month} {dayOfWeek}";
        _parsed = ParseCore(Expression);
    }

    private Cron(string expression)
    {
        _parsed = ParseCore(expression);
        var fields = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var offset = fields.Length == 6 ? 1 : 0;
        _second = offset == 1 ? fields[0] : null;
        _minute = fields[offset];
        _hour = fields[offset + 1];
        _dayOfMonth = fields[offset + 2];
        _month = fields[offset + 3];
        _dayOfWeek = fields[offset + 4];
        Expression = string.Join(' ', fields);
    }

    /// <summary>The expression in standard 5-field form, or 6-field form when it has seconds precision.</summary>
    public string Expression { get; }

    /// <summary>True when the expression has a seconds field.</summary>
    public bool HasSeconds => _second is not null;

    // ---------------------------------------------------------------- Presets

    /// <summary>Every second. Mostly for tests and demos.</summary>
    public static Cron EverySecond { get; } = new("*", "*", "*", "*", "*", "*");

    /// <summary>At the start of every minute.</summary>
    public static Cron EveryMinute { get; } = new(null, "*", "*", "*", "*", "*");

    /// <summary>At the start of every hour. Refine with <see cref="AtMinute"/>.</summary>
    public static Cron Hourly { get; } = new(null, "0", "*", "*", "*", "*");

    /// <summary>Every day at midnight. Refine with <see cref="At(int, int)"/>.</summary>
    public static Cron Daily { get; } = new(null, "0", "0", "*", "*", "*");

    /// <summary>Every Sunday at midnight. Refine with <see cref="On(DayOfWeek[])"/> and <see cref="At(int, int)"/>.</summary>
    public static Cron Weekly { get; } = new(null, "0", "0", "*", "*", "0");

    /// <summary>Monday to Friday at midnight. Refine with <see cref="At(int, int)"/>.</summary>
    public static Cron Weekdays { get; } = new(null, "0", "0", "*", "*", "1-5");

    /// <summary>Saturday and Sunday at midnight.</summary>
    public static Cron Weekends { get; } = new(null, "0", "0", "*", "*", "0,6");

    /// <summary>The first of every month at midnight. Refine with <see cref="OnDay"/> and <see cref="At(int, int)"/>.</summary>
    public static Cron Monthly { get; } = new(null, "0", "0", "1", "*", "*");

    /// <summary>1 January at midnight. Refine with <see cref="InMonth"/>, <see cref="OnDay"/> and <see cref="At(int, int)"/>.</summary>
    public static Cron Yearly { get; } = new(null, "0", "0", "1", "1", "*");

    /// <summary>A schedule that never fires (31 February). Useful to pause a recurring job without deleting it.</summary>
    public static Cron Never { get; } = new(null, "0", "0", "31", "2", "*");

    /// <summary>
    /// A fixed interval: whole seconds below a minute, whole minutes below an hour, whole hours below a day, or whole days.
    /// Intervals are aligned to the clock (every 15 minutes means :00, :15, :30, :45), as cron does.
    /// </summary>
    public static Cron Every(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be positive.");

        if (interval < TimeSpan.FromMinutes(1))
            return interval.Ticks % TimeSpan.TicksPerSecond == 0
                ? new Cron(Step(interval.Seconds), "*", "*", "*", "*", "*")
                : throw new ArgumentException("Intervals below a minute must be whole seconds.", nameof(interval));

        if (interval < TimeSpan.FromHours(1))
            return interval.Ticks % TimeSpan.TicksPerMinute == 0
                ? new Cron(null, Step((int)interval.TotalMinutes), "*", "*", "*", "*")
                : throw new ArgumentException("Intervals below an hour must be whole minutes.", nameof(interval));

        if (interval < TimeSpan.FromDays(1))
            return interval.Ticks % TimeSpan.TicksPerHour == 0
                ? new Cron(null, "0", Step((int)interval.TotalHours), "*", "*", "*")
                : throw new ArgumentException("Intervals below a day must be whole hours.", nameof(interval));

        return interval.Ticks % TimeSpan.TicksPerDay == 0 && interval.TotalDays <= 31
            ? new Cron(null, "0", "0", Step((int)interval.TotalDays), "*", "*")
            : throw new ArgumentException("Intervals of a day or more must be whole days, at most 31.", nameof(interval));

        static string Step(int n) => n == 1 ? "*" : $"*/{n}";
    }

    /// <summary>Parses a 5-field (minute precision) or 6-field (seconds precision) expression. Throws <see cref="ArgumentException"/> if invalid.</summary>
    public static Cron Parse(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        return new Cron(expression);
    }

    public static bool TryParse(string? expression, out Cron? cron)
    {
        cron = null;
        if (string.IsNullOrWhiteSpace(expression)) return false;
        try { cron = new Cron(expression); return true; }
        catch (ArgumentException) { return false; }
    }

    // ---------------------------------------------------------------- Refinement

    /// <summary>Sets the time of day. Seconds are cleared.</summary>
    public Cron At(int hour, int minute = 0)
    {
        Range(hour, 0, 23, nameof(hour));
        Range(minute, 0, 59, nameof(minute));
        return new Cron(_second is null ? null : "0", Num(minute), Num(hour), _dayOfMonth, _month, _dayOfWeek);
    }

    /// <summary>Sets the minute and leaves the hour as it is. <c>Cron.Hourly.AtMinute(45)</c> is "45 * * * *".</summary>
    public Cron AtMinute(int minute)
    {
        Range(minute, 0, 59, nameof(minute));
        return new Cron(_second is null ? null : "0", Num(minute), _hour, _dayOfMonth, _month, _dayOfWeek);
    }

    /// <summary>Sets the time of day using a <see cref="TimeOnly"/>. Seconds are kept if non-zero.</summary>
    public Cron At(TimeOnly time)
        => new(time.Second == 0 ? (_second is null ? null : "0") : Num(time.Second), Num(time.Minute), Num(time.Hour), _dayOfMonth, _month, _dayOfWeek);

    /// <summary>Restricts the schedule to the given days of the week.</summary>
    public Cron On(params DayOfWeek[] days)
    {
        if (days is null || days.Length == 0) throw new ArgumentException("Give at least one day.", nameof(days));
        var field = string.Join(',', days.Distinct().OrderBy(d => d).Select(d => Num((int)d)));
        return new Cron(_second, _minute, _hour, _dayOfMonth, _month, field);
    }

    /// <summary>Restricts the schedule to the given day of the month (1..31). Months without that day are skipped.</summary>
    public Cron OnDay(int dayOfMonth)
    {
        Range(dayOfMonth, 1, 31, nameof(dayOfMonth));
        return new Cron(_second, _minute, _hour, Num(dayOfMonth), _month, _dayOfWeek);
    }

    /// <summary>Restricts the schedule to the last day of every month.</summary>
    public Cron OnLastDay() => new(_second, _minute, _hour, "L", _month, _dayOfWeek);

    /// <summary>Restricts the schedule to the given month (1..12).</summary>
    public Cron InMonth(int month)
    {
        Range(month, 1, 12, nameof(month));
        return new Cron(_second, _minute, _hour, _dayOfMonth, Num(month), _dayOfWeek);
    }

    // ---------------------------------------------------------------- Evaluation

    /// <summary>The first occurrence strictly after <paramref name="after"/> in <paramref name="timeZone"/> (UTC when omitted), or null if there is none.</summary>
    public DateTimeOffset? Next(DateTimeOffset after, TimeZoneInfo? timeZone = null)
        => _parsed.GetNextOccurrence(after, timeZone ?? TimeZoneInfo.Utc, inclusive: false);

    /// <summary>The next <paramref name="count"/> occurrences after <paramref name="after"/>. Fewer are returned when the schedule runs out.</summary>
    public IReadOnlyList<DateTimeOffset> Upcoming(DateTimeOffset after, TimeZoneInfo? timeZone = null, int count = 5)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        var zone = timeZone ?? TimeZoneInfo.Utc;
        var list = new List<DateTimeOffset>(count);
        var cursor = after;
        for (var i = 0; i < count; i++)
        {
            var next = _parsed.GetNextOccurrence(cursor, zone, inclusive: false);
            if (next is null) break;
            list.Add(next.Value);
            cursor = next.Value;
        }
        return list;
    }

    // ---------------------------------------------------------------- Plumbing

    public override string ToString() => Expression;
    public static implicit operator string(Cron cron) => cron.Expression;
    public bool Equals(Cron? other) => other is not null && Expression == other.Expression;
    public override bool Equals(object? obj) => Equals(obj as Cron);
    public override int GetHashCode() => Expression.GetHashCode(StringComparison.Ordinal);

    private static CronExpression ParseCore(string expression)
    {
        var fields = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var format = fields.Length == 6 ? CronFormat.IncludeSeconds : CronFormat.Standard;
        try
        {
            return CronExpression.Parse(expression, format);
        }
        catch (CronFormatException ex)
        {
            throw new ArgumentException($"'{expression}' is not a valid cron expression: {ex.Message}", nameof(expression), ex);
        }
    }

    private static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);

    private static void Range(int value, int min, int max, string name)
    {
        if (value < min || value > max) throw new ArgumentOutOfRangeException(name, value, $"{name} must be between {min} and {max}.");
    }
}
