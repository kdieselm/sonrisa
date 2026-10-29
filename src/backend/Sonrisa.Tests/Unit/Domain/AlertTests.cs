using Sonrisa.Domain.Entities;

namespace Sonrisa.Tests.Unit.Domain;

/// <summary>Spec §2.5, AC5: when an alert's "active since" moves.</summary>
public sealed class AlertTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private static Alert NewAlert() => new("user-1", "Quakes", "disaster", """{"minMagnitude":5}""", [1, 2], T0);

    [Fact]
    public void Re_enabling_resets_active_since_and_disabling_does_not()
    {
        var alert = NewAlert();

        alert.Disable();
        Assert.Equal(T0, alert.ActiveSince);

        alert.Enable(T0.AddHours(1));
        Assert.Equal(T0.AddHours(1), alert.ActiveSince);

        alert.Enable(T0.AddHours(2)); // already enabled: nothing changes
        Assert.Equal(T0.AddHours(1), alert.ActiveSince);
    }

    [Fact]
    public void A_changed_rule_resets_active_since_and_the_same_rule_does_not()
    {
        var alert = NewAlert();

        alert.ChangeRule("""{ "minMagnitude" : 5 }""", T0.AddHours(1));
        Assert.Equal(T0, alert.ActiveSince);

        alert.ChangeRule("""{"minMagnitude":6}""", T0.AddHours(2));
        Assert.Equal(T0.AddHours(2), alert.ActiveSince);
    }

    [Fact]
    public void New_destinations_and_a_new_name_do_not_reset_active_since()
    {
        var alert = NewAlert();

        alert.SetDestinations([2, 3]);
        alert.Rename("Big quakes");

        Assert.Equal(T0, alert.ActiveSince);
        Assert.Equal([2L, 3L], alert.Destinations.Select(d => d.DestinationId).Order());
    }

    [Fact]
    public void An_alert_needs_one_to_ten_destinations()
    {
        Assert.Throws<ArgumentException>(() => new Alert("u", "n", "news", "{}", [], T0));
        Assert.Throws<ArgumentException>(() => NewAlert().SetDestinations(Enumerable.Range(1, 11).Select(i => (long)i)));
    }

    [Fact]
    public void Fail_changes_only_pending_deliveries()
    {
        var delivery = new Delivery(1, 2, 3, T0);

        delivery.Fail(DeliveryFailure.AlertDeleted, "alert deleted");
        delivery.Fail(DeliveryFailure.SendFailed, "later");

        Assert.Equal((DeliveryStatus.Failed, DeliveryFailure.AlertDeleted, "alert deleted"),
            (delivery.Status, delivery.Failure, delivery.LastError));
        Assert.Null(delivery.NextAttemptAt);
    }
}
