using System.Text.Json;
using TimecardNotifier.Models;

namespace TimecardNotifier.Services;

/// <summary>Produces the Adaptive Card payload the back end posts to Teams (v1.4 renders on desktop, web and mobile).</summary>
public static class AdaptiveCardBuilder
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static string BuildJson(NotifierSettings s, DateOnly workday, string employeeId = "{employeeId}")
    {
        var card = new Dictionary<string, object>
        {
            ["type"] = "AdaptiveCard",
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["version"] = "1.4",
            ["body"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["type"] = "TextBlock", ["text"] = s.CardTitle, ["weight"] = "Bolder", ["size"] = "Medium", ["wrap"] = true,
                },
                new Dictionary<string, object>
                {
                    ["type"] = "TextBlock", ["text"] = s.CardMessage, ["wrap"] = true,
                },
                new Dictionary<string, object>
                {
                    ["type"] = "TextBlock", ["text"] = $"Week of {StartOfWeek(workday):MMMM d}", ["isSubtle"] = true, ["size"] = "Small", ["spacing"] = "Small", ["wrap"] = true,
                },
            },
            ["actions"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["type"] = "Action.OpenUrl", ["title"] = s.ButtonText, ["url"] = s.BuildTimecardUrl(workday, employeeId),
                },
            },
        };
        return JsonSerializer.Serialize(card, Pretty);
    }

    public static DateOnly StartOfWeek(DateOnly d) => TimeZoneCatalog.WeekStart(d); // Sunday (work week is Sun–Sat)
}
