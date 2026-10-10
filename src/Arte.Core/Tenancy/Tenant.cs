namespace Arte.Core.Tenancy;

/// <summary>What kind of shop this is; it picks the vehicle kinds offered at intake (changeable in Settings).</summary>
public static class BusinessTypes
{
    public const string MotorcycleRepair = "motorcycle_repair";   // موتورسازی
    public const string CarRepair = "car_repair";                 // تعمیرات خودرو
    public const string QuickService = "quick_service";           // آپاراتی و تعویض روغن
    public static readonly IReadOnlySet<string> All = new HashSet<string> { MotorcycleRepair, CarRepair, QuickService };

    public static string[] DefaultVehicleKinds(string type) => type switch
    {
        MotorcycleRepair => ["motorcycle"],
        CarRepair => ["car", "suv", "van", "pickup"],
        _ => ["car", "suv", "van", "pickup", "motorcycle"],
    };
}

public sealed class Tenant
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public required string Vertical { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public long LastCaseNumber { get; set; }
    /// <summary>Business setting: every new case must be assigned to someone at intake.</summary>
    public bool RequireAssigneeOnIntake { get; set; } = true;
    /// <summary>Business setting: a «منتظر تأیید مشتری» step after diagnosis. Off by default: the customer already asked for the work.</summary>
    public bool RequireCustomerApproval { get; set; }
    /// <summary>Business setting: the master checks finished work before the customer is told it is ready.</summary>
    public bool RequireFinalReview { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Platform admin switch: an inactive branch cannot be used at all (data is kept).</summary>
    public bool IsActive { get; set; } = true;
    public string? DeactivatedReason { get; set; }

    public string BusinessType { get; set; } = BusinessTypes.MotorcycleRepair;
    /// <summary>Vehicle kinds this shop takes in (asset kinds); intake offers only these.</summary>
    public string[] VehicleKinds { get; set; } = ["car", "suv", "van", "pickup", "motorcycle"];

    // ── What the customer is told, and what their tracking page shows ──
    /// <summary>Master switch for SMS to customers (each SMS costs the platform credit).</summary>
    public bool CustomerSmsEnabled { get; set; }
    public bool SmsOnOpened { get; set; } = true;
    public bool SmsOnReady { get; set; } = true;
    public bool SmsOnDelivered { get; set; } = true;
    /// <summary>Tracking page: every stage with its time (off: only the current status).</summary>
    public bool TrackShowStages { get; set; } = true;
    /// <summary>Tracking page: the parts and labor list.</summary>
    public bool TrackShowItems { get; set; } = true;
    /// <summary>Tracking page: total, paid and balance.</summary>
    public bool TrackShowAmounts { get; set; } = true;
    /// <summary>New photos start as visible to the customer (each photo can still be switched).</summary>
    public bool PhotosVisibleByDefault { get; set; }

    // ── Customer satisfaction survey (a paid add-on) ──
    /// <summary>The add-on is on for this business. Only the platform admin changes it.</summary>
    public bool SurveyEnabled { get; set; }
    /// <summary>Business setting: send the survey SMS after delivery.</summary>
    public bool SurveySendOn { get; set; } = true;
    /// <summary>Minutes after delivery (then moved into sending hours, 09:00–23:00).</summary>
    public int SurveyDelayMinutes { get; set; } = 30;
}
