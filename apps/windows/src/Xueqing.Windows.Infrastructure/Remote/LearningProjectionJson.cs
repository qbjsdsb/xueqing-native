using System.Globalization;
using System.Text.Json;
using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Infrastructure.Remote;

internal static class LearningProjectionJson
{
    public static void RequireObject(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"Projection {name} must be an object.");
        }
    }

    public static string GetRequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"Projection property '{propertyName}' must be a string.");
        }

        return property.GetString()
            ?? throw new InvalidDataException($"Projection property '{propertyName}' is null.");
    }

    public static string GetRequiredNonBlankString(JsonElement element, string propertyName)
    {
        var value = GetRequiredString(element, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"Projection property '{propertyName}' must not be blank.");
        }

        return value;
    }

    public static Guid GetRequiredGuid(JsonElement element, string propertyName)
    {
        var value = GetRequiredString(element, propertyName);
        if (!Guid.TryParseExact(value, "D", out var parsed) || parsed == Guid.Empty)
        {
            throw new InvalidDataException($"Projection property '{propertyName}' must be a non-empty canonical UUID.");
        }

        return parsed;
    }

    public static DateTimeOffset GetRequiredTimestamp(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            !property.TryGetDateTimeOffset(out var parsed))
        {
            throw new InvalidDataException($"Projection property '{propertyName}' must be an ISO timestamp.");
        }

        return parsed;
    }

    public static DateOnly GetRequiredDate(JsonElement element, string propertyName)
    {
        var value = GetRequiredString(element, propertyName);
        if (!DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            throw new InvalidDataException($"Projection property '{propertyName}' must be an ISO business date.");
        }

        return parsed;
    }

    public static DateOnly? GetOptionalDate(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String ||
            !DateOnly.TryParseExact(
                property.GetString(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            throw new InvalidDataException($"Projection property '{propertyName}' must be null or an ISO business date.");
        }

        return parsed;
    }

    public static long GetRequiredPositiveVersion(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt64(out var parsed) ||
            parsed < 1)
        {
            throw new InvalidDataException($"Projection property '{propertyName}' must be a positive integer version.");
        }

        return parsed;
    }

    public static bool GetRequiredBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException($"Projection property '{propertyName}' must be boolean.");
        }

        return property.GetBoolean();
    }

    public static LearningCaseState GetOpenCaseState(JsonElement element, string propertyName) =>
        GetRequiredString(element, propertyName) switch
        {
            "new" => LearningCaseState.New,
            "confirmed" => LearningCaseState.Confirmed,
            "intervening" => LearningCaseState.Intervening,
            "pending_verification" => LearningCaseState.PendingVerification,
            "stable" => LearningCaseState.Stable,
            "closed" => throw new InvalidDataException("Personal open-learning projection must not contain a closed Case."),
            _ => throw new InvalidDataException("Projection contains an unsupported Learning Case state."),
        };

    public static ActionDueBucket GetDueBucket(JsonElement element, string propertyName) =>
        GetRequiredString(element, propertyName) switch
        {
            "overdue" => ActionDueBucket.Overdue,
            "today" => ActionDueBucket.Today,
            "undated" => ActionDueBucket.Undated,
            "future" => ActionDueBucket.Future,
            _ => throw new InvalidDataException("Projection contains an unsupported Action due bucket."),
        };

    public static ActionDueBucket ExpectedDueBucket(DateOnly? dueOn, DateOnly businessDate)
    {
        if (dueOn is null)
        {
            return ActionDueBucket.Undated;
        }

        if (dueOn.Value < businessDate)
        {
            return ActionDueBucket.Overdue;
        }

        if (dueOn.Value == businessDate)
        {
            return ActionDueBucket.Today;
        }

        return ActionDueBucket.Future;
    }

    public static int BucketRank(ActionDueBucket bucket) => bucket switch
    {
        ActionDueBucket.Overdue => 0,
        ActionDueBucket.Today => 1,
        ActionDueBucket.Undated => 2,
        ActionDueBucket.Future => 3,
        _ => throw new InvalidDataException("Unsupported Action due bucket."),
    };
}
