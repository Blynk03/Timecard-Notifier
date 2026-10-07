using TimecardNotifier.Models;

namespace TimecardNotifier.Services;

/// <summary>Pretends to post to Teams. Replace with a call to the back end (Graph / bot) sender.</summary>
public sealed class MockNotificationSender : INotificationSender
{
    public Task<SendResult> SendAsync(DispatchItem item, NotifierSettings settings, DateOnly linkDate, CancellationToken ct = default)
    {
        var result = item.Recipient.HasTeamsAccount
            ? new SendResult(true)
            : new SendResult(false, "User not found in Teams");
        return Task.FromResult(result);
    }
}
