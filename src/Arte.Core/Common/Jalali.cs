using System.Globalization;

namespace Arte.Core.Common;

/// <summary>
/// Jalali calendar helpers. Tehran is a fixed +3:30 offset (no daylight-saving table): a calendar day is
/// [midnight, next midnight) in that offset. Employment start and end dates are inclusive.
/// </summary>
public static class Jalali
{
    public static readonly TimeSpan TehranOffset = TimeSpan.FromHours(3.5);

    public static DateOnly ToTehranDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(instant.ToOffset(TehranOffset).DateTime);

    public static (int Year, int Month, int Day) Parts(DateOnly date)
    {
        var cal = new PersianCalendar();
        var dt = date.ToDateTime(TimeOnly.MinValue);
        return (cal.GetYear(dt), cal.GetMonth(dt), cal.GetDayOfMonth(dt));
    }

    public static int DaysInMonth(int year, int month) => new PersianCalendar().GetDaysInMonth(year, month);

    public static DateOnly Date(int year, int month, int day) =>
        DateOnly.FromDateTime(new PersianCalendar().ToDateTime(year, month, day, 0, 0, 0, 0));

    /// <summary>UTC instant of Jalali year/month/1 at Tehran midnight. The month is [this, <see cref="MonthEndUtc"/>).</summary>
    public static DateTimeOffset MonthStartUtc(int year, int month)
    {
        var date = Date(year, month, 1);
        return new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TehranOffset).ToUniversalTime();
    }

    public static DateTimeOffset MonthEndUtc(int year, int month)
    {
        var nextMonth = month == 12 ? 1 : month + 1;
        var nextYear = month == 12 ? year + 1 : year;
        return MonthStartUtc(nextYear, nextMonth);
    }

    public static bool ValidMonth(int year, int month) => year is >= 1300 and <= 1500 && month is >= 1 and <= 12;

    public static IReadOnlyList<(int Year, int Month)> MonthsOverlapping(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        if (toUtc <= fromUtc) return [];
        var start = ToTehranDate(fromUtc);
        var end = ToTehranDate(toUtc.AddTicks(-1));
        var (year, month, _) = Parts(start);
        var (endYear, endMonth, _) = Parts(end);
        var months = new List<(int, int)>();
        while (year < endYear || (year == endYear && month <= endMonth))
        {
            months.Add((year, month));
            month++;
            if (month == 13) { month = 1; year++; }
        }
        return months;
    }

    public static bool EmployedOn(DateOnly day, DateOnly started, DateOnly? ended) =>
        day >= started && (ended is null || day <= ended);

    public static bool EmploymentOverlaps(DateOnly started, DateOnly? ended, DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        if (toUtc <= fromUtc) return false;
        var rangeStart = ToTehranDate(fromUtc);
        var rangeEnd = ToTehranDate(toUtc.AddTicks(-1));
        var empEnd = ended ?? DateOnly.MaxValue;
        return started <= rangeEnd && empEnd >= rangeStart;
    }

    /// <summary>
    /// A Tehran calendar day counts when its [midnight, next midnight) overlaps <paramref name="fromUtc"/>…<paramref name="toUtc"/>.
    /// </summary>
    public static bool DayIntersects(DateOnly day, DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        var dayStart = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TehranOffset);
        var dayEnd = dayStart.AddDays(1);
        return dayStart < toUtc && dayEnd > fromUtc;
    }

    /// <summary>
    /// Fixed monthly salary for the half-open instant range, split by real Jalali month length.
    /// Each month is rounded half away from zero, then the months are added. Days outside employment are skipped.
    /// </summary>
    public static long ProrateFixedMonthly(long monthlyRials, DateOnly started, DateOnly? ended, DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        if (monthlyRials <= 0 || toUtc <= fromUtc) return 0;
        long total = 0;
        foreach (var (year, month) in MonthsOverlapping(fromUtc, toUtc))
        {
            var daysInMonth = DaysInMonth(year, month);
            var active = 0;
            for (var day = 1; day <= daysInMonth; day++)
            {
                var date = Date(year, month, day);
                if (!EmployedOn(date, started, ended) || !DayIntersects(date, fromUtc, toUtc)) continue;
                active++;
            }
            if (active == 0) continue;
            total += (long)Math.Round(monthlyRials * (decimal)active / daysInMonth, MidpointRounding.AwayFromZero);
        }
        return total;
    }
}
