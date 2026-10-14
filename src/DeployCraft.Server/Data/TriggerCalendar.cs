using System.Globalization;

namespace DeployCraft.Server.Data;

/// <summary>
/// Calendrier des déclencheurs : prochain déclenchement et résumé en français.
/// Partagé par le service (qui déclenche) et la console (qui affiche).
/// </summary>
public static class TriggerCalendar
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Premier déclenchement strictement après <paramref name="after"/> (heure locale), ou null s'il n'y en a plus.</summary>
    public static DateTime? Next(ScheduleTrigger t, DateTime after)
    {
        if (!t.Enabled) return null;
        var next = t.Type switch
        {
            TriggerType.Once => t.Start > after ? t.Start : (DateTime?)null,
            TriggerType.Interval => NextInterval(t, after),
            _ => NextCalendarDay(t, after),
        };
        if (next is { } n && t.Type != TriggerType.Once && t.End is { } end && n > end) return null;
        return next;
    }

    /// <summary>Prochain déclenchement d'une planification parmi ses déclencheurs actifs.</summary>
    public static DateTime? Next(Schedule s, DateTime after) =>
        s.Triggers.Select(t => Next(t, after)).Where(d => d != null).Min();

    private static DateTime? NextInterval(ScheduleTrigger t, DateTime after)
    {
        var step = TimeSpan.FromMinutes(Math.Max(1, t.IntervalMinutes));
        var candidate = AlignAfter(t.Start, step, after);
        if (!t.UseTimeWindow) return candidate;

        // Hors de la plage horaire : on saute au début de la plage suivante, puis on se recale sur la grille.
        for (var i = 0; i < 1000; i++)
        {
            if (t.End is { } end && candidate > end) return null;
            if (InWindow(candidate.TimeOfDay, t.WindowStart, t.WindowEnd)) return candidate;
            var windowStart = candidate.Date + t.WindowStart;
            if (windowStart <= candidate) windowStart = windowStart.AddDays(1);
            candidate = AlignAfter(t.Start, step, windowStart.AddTicks(-1));
        }
        return null;
    }

    /// <summary>Premier point de la grille début + k × pas (k ≥ 0) strictement après <paramref name="after"/>.</summary>
    private static DateTime AlignAfter(DateTime start, TimeSpan step, DateTime after)
    {
        if (start > after) return start;
        var k = (after - start).Ticks / step.Ticks + 1;
        return start + TimeSpan.FromTicks(step.Ticks * k);
    }

    /// <summary>Plage « entre » / « et » ; si la fin précède le début, la plage passe minuit.</summary>
    public static bool InWindow(TimeSpan time, TimeSpan from, TimeSpan to) =>
        from <= to ? time >= from && time <= to : time >= from || time <= to;

    private static DateTime? NextCalendarDay(ScheduleTrigger t, DateTime after)
    {
        var time = t.Start.TimeOfDay;
        var day = (after > t.Start ? after : t.Start).Date;
        // Une année et demie suffit à trouver le prochain jour de n'importe quelle règle mensuelle.
        for (var i = 0; i < 550; i++, day = day.AddDays(1))
        {
            var candidate = day + time;
            if (candidate <= after || candidate < t.Start) continue;
            if (t.End is { } end && candidate > end) return null;
            if (Matches(t, day)) return candidate;
        }
        return null;
    }

    private static bool Matches(ScheduleTrigger t, DateTime day) => t.Type switch
    {
        TriggerType.Daily => true,
        TriggerType.Weekly => t.Days.Contains(day.DayOfWeek),
        TriggerType.Monthly when t.MonthWeek is { } week => day.DayOfWeek == t.MonthWeekDay && (week >= 5
            ? day.AddDays(7).Month != day.Month
            : (day.Day - 1) / 7 + 1 == week),
        TriggerType.Monthly => t.MonthDay <= 0
            ? day.AddDays(1).Month != day.Month
            : day.Day == Math.Min(t.MonthDay, DateTime.DaysInMonth(day.Year, day.Month)),
        _ => false,
    };

    // ---------- Résumés ----------

    public static string Describe(ScheduleTrigger t)
    {
        var at = Time(t.Start.TimeOfDay);
        var text = t.Type switch
        {
            TriggerType.Once => $"Une fois, le {t.Start:dd/MM/yyyy} à {at}",
            TriggerType.Interval => $"Intervalle de {Interval(t.IntervalMinutes)}, à partir du {t.Start:dd/MM/yyyy} {at}"
                                    + (t.UseTimeWindow ? $", entre {Time(t.WindowStart)} et {Time(t.WindowEnd)}" : ""),
            TriggerType.Daily => $"Tous les jours à {at} à partir du {t.Start:dd/MM/yyyy}",
            TriggerType.Weekly => $"Chaque semaine le {DaysText(t.Days)} à {at} à partir du {t.Start:dd/MM/yyyy}",
            _ => $"Chaque mois {MonthText(t)} à {at} à partir du {t.Start:dd/MM/yyyy}",
        };
        if (t.Type != TriggerType.Once && t.End is { } end) text += $", jusqu'au {end:dd/MM/yyyy} {Time(end.TimeOfDay)}";
        if (!t.Enabled) text += " (désactivé)";
        return text;
    }

    public static string Time(TimeSpan t) => $"{t.Hours:00}:{t.Minutes:00}";

    public static string Interval(int minutes)
    {
        var parts = new List<string>();
        if (minutes / 1440 > 0) parts.Add($"{minutes / 1440} j");
        if (minutes % 1440 / 60 > 0) parts.Add($"{minutes % 1440 / 60} h");
        if (minutes % 60 > 0 || parts.Count == 0) parts.Add($"{minutes % 60} min");
        return string.Join(" ", parts);
    }

    public static string DayName(DayOfWeek d) => Fr.DateTimeFormat.GetDayName(d);

    private static string DaysText(List<DayOfWeek> days) => days.Count == 0
        ? "(aucun jour)"
        : string.Join(", ", days.OrderBy(d => ((int)d + 6) % 7).Select(DayName));

    public static readonly string[] WeekNames = ["premier", "deuxième", "troisième", "quatrième", "dernier"];

    private static string MonthText(ScheduleTrigger t) => t.MonthWeek is { } w
        ? $"le {WeekNames[Math.Clamp(w, 1, 5) - 1]} {DayName(t.MonthWeekDay)}"
        : t.MonthDay <= 0 ? "le dernier jour" : $"le {t.MonthDay}";
}
