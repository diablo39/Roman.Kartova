using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Kartova.Audit.Domain;

/// <summary>
/// Produces a deterministic, jsonb-stable UTF-8 byte encoding of an audit row's hashable
/// fields. Fields are written in a fixed order; <c>data</c> keys are sorted ordinal and all
/// values are JSON strings (or null). Because Postgres jsonb normalizes on store, the verifier
/// re-canonicalizes the round-tripped <c>data</c> dictionary — sorting + string-only values make
/// that round-trip a no-op for the hash (design spec §5). <c>occurred_at</c> is formatted to
/// microsecond precision (6 fractional digits) to match Postgres <c>timestamptz</c> resolution.
/// </summary>
public static class AuditCanonicalSerializer
{
    private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.ffffffZ";

    public static byte[] Serialize(
        Guid tenantId,
        long seq,
        DateTimeOffset occurredAt,
        AuditActorType actorType,
        Guid? actorId,
        string action,
        string targetType,
        string targetId,
        IReadOnlyDictionary<string, string?>? data,
        byte[] prevHash)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("tenant_id", tenantId.ToString("D"));
            w.WriteNumber("seq", seq);
            // occurred_at is formatted to microsecond precision; the "ffffff" specifier truncates
            // sub-µs ticks, matching Postgres timestamptz resolution so the hash is stable across a
            // DB round-trip. (The writer separately truncates the value it STORES.)
            w.WriteString("occurred_at", occurredAt.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));
            w.WriteString("actor_type", actorType.ToString());
            if (actorId is { } a) w.WriteString("actor_id", a.ToString("D"));
            else w.WriteNull("actor_id");
            w.WriteString("action", action);
            w.WriteString("target_type", targetType);
            w.WriteString("target_id", targetId);

            w.WritePropertyName("data");
            if (data is null)
            {
                w.WriteNullValue();
            }
            else
            {
                w.WriteStartObject();
                foreach (var key in data.Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    var value = data[key];
                    if (value is null) w.WriteNull(key);
                    else w.WriteString(key, value);
                }
                w.WriteEndObject();
            }

            w.WriteString("prev_hash", Convert.ToBase64String(prevHash));
            w.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }
}
