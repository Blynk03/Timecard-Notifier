using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using TimecardNotifier;
using TimecardNotifier.Models;
using TimecardNotifier.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// ── Back-end seams ───────────────────────────────────────────────────────────
// These four interfaces are the only things the back end needs to implement.
// Swap the Mock* classes for HTTP-backed implementations when the API exists.
builder.Services.AddSingleton<IRecipientSource, MockRecipientSource>();     // the "missing entries" DB query
builder.Services.AddSingleton<IHolidayService, MockHolidayService>();       // company holiday calendar
builder.Services.AddSingleton<IExemptionService, MockExemptionService>();   // exempt cost centers
builder.Services.AddSingleton<INotificationSender, MockNotificationSender>(); // Teams chat / activity feed sender

// ── Front-end state ──────────────────────────────────────────────────────────
builder.Services.AddSingleton<NotifierSettings>();
builder.Services.AddSingleton<DispatchPlanner>();
builder.Services.AddSingleton<DispatchState>();

await builder.Build().RunAsync();
