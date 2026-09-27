using System.ComponentModel;
using System.Globalization;
using System.ComponentModel.DataAnnotations.Schema;

namespace Pleiades.Orchestration;

/// <summary>
/// A deadline moment (PEP111): a civil (wall-clock) <see cref="Moment"/> plus an optional <see cref="TimeZone"/>.
/// Unlike an occurrence's <see cref="Epoch"/>, a due carries no granularity or duration — it is a single point by
/// which something is due. Owned by its incentive/checkpoint (EF <c>OwnsOne</c>); round-trips to one compact
/// frontmatter field through <see cref="DueTypeConverter"/> (<c>2026-07-20</c>, <c>2026-07-20T14:30</c>, or
/// <c>2026-07-20T14:30 America/New_York</c>).
/// </summary>
[TypeConverter(typeof(DueTypeConverter))]
public sealed class Due
{
	/// <summary>
	/// Gets or sets the civil (wall-clock) moment the item is due. Interpreted in <see cref="TimeZone"/>, or as
	/// floating local time when that is <see langword="null"/>; never an absolute UTC instant.
	/// </summary>
	public DateTime Moment { get; set; }

	/// <summary>
	/// Gets or sets the time zone <see cref="Moment"/> is anchored in. <see langword="null"/> means wall/floating
	/// time (no zone).
	/// </summary>
	public string? TimeZone { get; set; }

	/// <summary>Gets the calendar day of <see cref="Moment"/>. In an EF query, filter on <see cref="Moment"/> instead.</summary>
	[NotMapped]
	public DateOnly Date => DateOnly.FromDateTime(Moment);

	/// <summary>Gets whether the due is a whole-day deadline (no meaningful time of day).</summary>
	[NotMapped]
	public bool IsAllDay => Moment.TimeOfDay == TimeSpan.Zero;

	/// <summary>Builds a whole-day due on <paramref name="date"/> (midnight, floating).</summary>
	public static Due On(DateOnly date, string? timeZone = null) => new() { Moment = date.ToDateTime(TimeOnly.MinValue), TimeZone = timeZone };

	/// <summary>Builds a due at an exact civil moment.</summary>
	public static Due At(DateTime moment, string? timeZone = null) => new() { Moment = moment, TimeZone = timeZone };
}

/// <summary>
/// Round-trips a <see cref="Due"/> to and from its compact frontmatter form: a date (<c>2026-07-20</c>) for a
/// whole-day due, a minute-precise datetime (<c>2026-07-20T14:30</c>) when it carries a time, and a trailing
/// space-separated zone (<c>2026-07-20T14:30 America/New_York</c>) when anchored.
/// </summary>
public sealed class DueTypeConverter : TypeConverter
{
	/// <inheritdoc />
	public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
		=> sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

	/// <inheritdoc />
	public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
		=> destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

	/// <inheritdoc />
	public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
	{
		if (value is not string text || string.IsNullOrWhiteSpace(text))
		{
			return base.ConvertFrom(context, culture, value);
		}

		var trimmed = text.Trim();
		// A trailing space separates the (space-free) zone from the moment; a plain date/datetime has no space.
		var split = trimmed.LastIndexOf(' ');
		var momentPart = split < 0 ? trimmed : trimmed[..split].Trim();
		var zonePart = split < 0 ? null : trimmed[(split + 1)..].Trim();

		DateTime moment;
		if (DateOnly.TryParseExact(momentPart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
		{
			moment = dateOnly.ToDateTime(TimeOnly.MinValue);
		}
		else if (DateTime.TryParse(momentPart, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
		{
			moment = parsed;
		}
		else
		{
			throw new FormatException($"'{text}' is not a valid due: expected a date, a datetime, or a datetime with a trailing zone.");
		}

		return new Due { Moment = moment, TimeZone = string.IsNullOrWhiteSpace(zonePart) ? null : zonePart };
	}

	/// <inheritdoc />
	public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
	{
		if (destinationType != typeof(string) || value is not Due due)
		{
			return base.ConvertTo(context, culture, value, destinationType);
		}

		var moment = due.IsAllDay
			? due.Moment.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
			: due.Moment.Second == 0
				? due.Moment.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture)
				: due.Moment.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
		return string.IsNullOrWhiteSpace(due.TimeZone) ? moment : $"{moment} {due.TimeZone}";
	}
}
