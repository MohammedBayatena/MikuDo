namespace MikuDo.Services;

/// <summary>Moments as the user reads them: in their own time zone, near ones by the day's name.</summary>
public static class When
{
    /// <summary>
    /// "today at 14:32", "yesterday at 09:10", "on Monday at 14:32" within the
    /// week, "on 3 Oct at 14:32" within the year, "on 3 Oct 2025" before that.
    /// Lower case, to follow a word such as "Created".
    /// </summary>
    public static string Phrase(DateTime moment)
    {
        var local = moment.ToLocalTime();
        var time = local.ToString("t");
        var days = (DateTime.Today - local.Date).Days;

        return days switch
        {
            0 => $"today at {time}",
            1 => $"yesterday at {time}",
            > 1 and < 7 => $"on {local:dddd} at {time}",
            _ when local.Year == DateTime.Today.Year => $"on {local:d MMM} at {time}",
            _ => $"on {local:d MMM yyyy}"
        };
    }

    /// <summary>
    /// The shortest form, standing on its own where room is tight: "14:32"
    /// today, "Mon" within the week, "3 Oct" within the year, "Oct 2025"
    /// before that.
    /// </summary>
    public static string Brief(DateTime moment)
    {
        var local = moment.ToLocalTime();
        var days = (DateTime.Today - local.Date).Days;

        return days switch
        {
            0 => $"{local:t}",
            > 0 and < 7 => $"{local:ddd}",
            _ when local.Year == DateTime.Today.Year => $"{local:d MMM}",
            _ => $"{local:MMM yyyy}"
        };
    }

    /// <summary>The date and time in full, in the user's own way of writing them.</summary>
    public static string Full(DateTime moment)
    {
        var local = moment.ToLocalTime();
        return $"{local:dddd, d MMMM yyyy}, {local:t}";
    }

    /// <summary>The time zone the times are shown in, as Windows names it: "(UTC+03:00) Amman".</summary>
    public static string Zone => TimeZoneInfo.Local.DisplayName;
}
