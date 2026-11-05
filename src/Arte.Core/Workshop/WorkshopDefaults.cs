namespace Arte.Core.Workshop;

/// <summary>
/// A starting point for the intake checklists. The business edits it freely in Settings (add, rename, reorder, switch off);
/// it is only copied into a business when the manager asks for it.
/// </summary>
public static class WorkshopDefaults
{
    private static readonly (string Group, string Title)[] Common =
    [
        ("بدنه و ظاهر", "خط‌وخش و ضربه‌دیدگی بدنه و قاب‌ها"),
        ("بدنه و ظاهر", "وضعیت باک، رنگ و آرم‌ها"),
        ("بدنه و ظاهر", "آینه‌ها"),
        ("برق", "باتری و ترمینال‌ها"),
        ("برق", "استارت برقی"),
        ("برق", "چراغ جلو (کوتاه و بلند)"),
        ("برق", "چراغ ترمز و راهنماها"),
        ("برق", "بوق"),
        ("برق", "سوئیچ و قفل فرمان"),
        ("برق", "فیوزها و سیم‌کشی قابل مشاهده"),
        ("موتور", "روشن شدن و دور آرام"),
        ("موتور", "صدای غیرعادی موتور"),
        ("موتور", "نشتی روغن یا بنزین"),
        ("موتور", "سطح و رنگ روغن موتور"),
        ("موتور", "فیلتر هوا"),
        ("موتور", "شمع"),
        ("اگزوز", "اگزوز (صدا، نشتی، شل بودن)"),
        ("ترمز", "لنت یا کفشک ترمز جلو"),
        ("ترمز", "لنت یا کفشک ترمز عقب"),
        ("ترمز", "سطح روغن ترمز و دسته‌ها"),
        ("جلوبندی و تعلیق", "کمک‌فنر جلو (نشتی، نرمی)"),
        ("جلوبندی و تعلیق", "کمک‌فنر عقب"),
        ("جلوبندی و تعلیق", "بلبرینگ فرمان (لقی و سفتی)"),
        ("چرخ و لاستیک", "باد و آج لاستیک جلو"),
        ("چرخ و لاستیک", "باد و آج لاستیک عقب"),
        ("چرخ و لاستیک", "رینگ، پره‌ها و لقی چرخ‌ها"),
        ("کنترل‌ها", "گاز (کابل و برگشت)"),
        ("کنترل‌ها", "کیلومترشمار و چراغ‌های هشدار"),
    ];

    private static readonly (string Group, string Title)[] GearedOnly =
    [
        ("انتقال قدرت", "کلاچ (فاصله‌ی دسته، گرفتن و سُر خوردن)"),
        ("انتقال قدرت", "جابه‌جا شدن دنده‌ها و خلاص"),
        ("انتقال قدرت", "زنجیر و دنده‌ها (کشش، روغن‌کاری، فرسودگی)"),
    ];

    private static readonly (string Group, string Title)[] AutomaticOnly =
    [
        ("انتقال قدرت", "تسمه و گوی‌های CVT (صدا، لرزش، سُر خوردن)"),
        ("انتقال قدرت", "کلاچ سانتریفیوژ و بوش‌ها"),
        ("انتقال قدرت", "روغن گیربکس (در مدل‌هایی که دارند)"),
    ];

    /// <summary>The default checklist for a motor type, in the order the people at the desk walk around the motorcycle.</summary>
    public static IReadOnlyList<(string Group, string Title)> Checklist(string motorType) =>
        [.. Common, .. (motorType == MotorTypes.Automatic ? AutomaticOnly : GearedOnly)];
}
