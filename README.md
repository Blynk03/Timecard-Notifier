# Timecard Reminders — Front End

A Blazor WebAssembly (.NET 8) + Tailwind admin UI for the Teams timecard reminder system. Front end only: the back end is replaced by four mock services behind interfaces, so you can hook up the real API without touching the UI.

## Run it

```bash
dotnet run          # or open TimecardNotifier.csproj in Visual Studio / Rider and press F5
```

Styling works right away because `wwwroot/index.html` loads the Tailwind CDN as a dev fallback. For production, compile the CSS and remove the fallback:

```bash
npm install
npm run css:build   # writes wwwroot/css/app.css (the csproj also does this on build once node_modules exists)
```

Then delete the block marked `DEV FALLBACK` in `wwwroot/index.html`.

> Blazor needs modern .NET (6/8+), not the legacy .NET Framework 4.x. If the back end is on .NET Framework, it can still serve the API this UI calls.

## Pages

| Page | What it does |
|---|---|
| **Today's run** (`/`) | Pick a run date and see who gets a reminder, who doesn't and why, the busiest minute against the 20/min limit, and a timeline of each **send wave** against its 11:00 local deadline. **Simulate dispatch** replays the plan one minute at a time. |
| **Capacity planner** (`/capacity`) | What-if calculator: enter expected reminders per time zone and a date to see when each wave has to start, when it finishes and whether anyone would be late. Starts from today's list. |
| **Recipients** (`/queue`) | Everyone from the timecard query, with the missing workdays that triggered their reminder (or why they don't get one). Search and filter by time zone or status. |
| **Exemptions** (`/exemptions`) | Exempt cost centers. Employees in them are never reminded. Paste one or many codes, add an optional reason, or exempt a cost center with one click from today's list. |
| **Holidays** (`/holidays`) | The holiday calendar by country, with optional time-zone-specific holidays. Add or remove holidays, and use **Check a date** to see which zones would be skipped. |
| **Card preview** (`/card`) | Edit the title, the approved message, the button text and the timecard link. Preview the card in Teams (chat, activity feed or both; desktop or mobile), then copy the Adaptive Card JSON. |
| **Settings** (`/settings`) | The deadline (11:00 local), a safety margin (default: finish 15 minutes early), and whether holidays are respected. The send rate is shown but can't be changed. |

## Who gets a reminder (`Services/DispatchPlanner.cs`)

- The work week runs **Sunday to Saturday**.
- Each run checks the **weekdays earlier this week** (Sunday up to yesterday). Weekends never count, and a recipient's holidays (by country and time zone) never count.
- Anyone with **at least one** missing workday gets **one** reminder. Example: on Wednesday, missing Monday but not Tuesday still gets a reminder.
- **Friday catch-up:** on Monday (or a person's first workday of the week, if Monday is their holiday), the previous week's **Friday** is checked instead, unless that Friday was their holiday. Nothing else is checked on Monday, because that's the day employees start the new timecard.
- Nobody is reminded on a weekend or on their own holiday. Exempt cost centers are never reminded.

## When reminders go out (`Services/SendScheduler.cs`)

1. Time zones whose clocks match on the run date are combined into one **send wave** with one 11:00 deadline (DST-aware).
2. The send rate is fixed at **20 per minute** (Microsoft's limit), shared by every wave: 1,200 reminders an hour.
3. Each wave starts **as late as it can** while every wave still finishes by its target (11:00 minus the safety margin). It starts **as early as it needs to**, from midnight local onward. Big early waves start early, and quiet waves start close to the working day.
4. One shared queue, earliest deadline first. No minute ever has more than 20 sends.
5. Anyone whose minute is at or after 11:00 local is flagged **late**. That can only happen if a day's volume needs more than about a full day of sending.

With the estimated volumes in the mock data (Eastern 3,500, Central 3,000, Mountain 900, and a placeholder of 1,950 each for Pacific, Alaska and Hawaii) and a 15-minute margin:

| Wave | Oct 6 (daylight time) | Dec 8 (standard time) |
|---|---|---|
| Eastern (New York + Toronto, 3,500) | 05:20–08:15 | 04:40–07:35 |
| Central | 07:15–09:30 | 06:35–09:05 (with Mexico City) |
| Mountain | 08:30–09:30 (with Mexico City) | 08:05–08:50 |
| Pacific | 08:30–10:08 | 07:50–09:28 |
| Alaska | 09:07–10:45 | 08:30–10:08 |
| Hawaii | 09:05–10:43 | 09:07–10:45 |

Nobody is late on either date. Winter needs Eastern to start earlier because Hawaii doesn't change its clocks.

**Data timing:** the Snowflake query runs early each morning, so its results are ready before the first wave (about 04:40 Eastern in winter). The app picks up those results; it doesn't run the query itself.

## Connecting the back end

Replace the four registrations in `Program.cs`:

| Interface | Mock | Real implementation should… |
|---|---|---|
| `IRecipientSource` | `MockRecipientSource` | Read the Snowflake query's results: employees with the days that have no hours, from last week's Friday up to yesterday (`Recipient.MissingDays`). The planner applies the weekend, holiday, Friday catch-up and exemption rules. |
| `IHolidayService` | `MockHolidayService` | Read and write the company holiday calendar. |
| `IExemptionService` | `MockExemptionService` | Read and write the exempt cost center list. The back end should also apply it when sending. |
| `INotificationSender` | `MockNotificationSender` | Post the Adaptive Card (`AdaptiveCardBuilder.BuildJson`) as a Teams chat message and/or activity feed notification. The card's link opens the employee's earliest missing workday. |

`NotifierSettings` is in-memory. Persist it through an API when you have one.

## Project layout

```
Program.cs                 DI registrations (the back-end seams live here)
Models/                    Recipient, Holiday, CostCenterExemption, DispatchItem/SendWave/DispatchPlan, settings, time zone + week helpers
Services/                  Interfaces, mocks, DispatchPlanner (who + when), SendScheduler (timing math), DispatchState (app state + simulator), AdaptiveCardBuilder
Layout/MainLayout.razor    Sidebar shell (collapses to a top bar on mobile)
Pages/                     Dashboard, Capacity, Queue, Holidays, Exemptions, CardPreview, SettingsPage
Components/                StatusBadge, StatCard, WaveStatus, AdaptiveCardPreview, Icon
Styles/app.css             Tailwind source (+ small component classes)
```

The sample holidays are real 2026–27 dates for the US, Canada, Mexico and the UK, but they're placeholders. Replace them with the holidays your company observes.
