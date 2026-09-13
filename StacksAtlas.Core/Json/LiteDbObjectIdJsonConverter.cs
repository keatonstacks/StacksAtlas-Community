using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using LiteDB;

namespace StacksAtlas.Core.Json;

/// <summary>
/// Serializes LiteDB ObjectId as a 24-char hex string for System.Text.Json (REST + SignalR).
/// Without this converter, ObjectId round-trips as ObjectId.Empty and Hub event rows collapse.
/// </summary>
public sealed class LiteDbObjectIdJsonConverter : JsonConverter<ObjectId>
{
    public override ObjectId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var hex = reader.GetString();
            return TryParseHex(hex, out var id) ? id : ObjectId.Empty;
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var timestamp = 0;
            var machine = 0;
            var pid = 0;
            var increment = 0;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;
                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                var prop = reader.GetString();
                reader.Read();
                switch (prop?.ToLowerInvariant())
                {
                    case "timestamp":
                        timestamp = reader.GetInt32();
                        break;
                    case "machine":
                        machine = reader.GetInt32();
                        break;
                    case "pid":
                        pid = reader.GetInt32();
                        break;
                    case "increment":
                        increment = reader.GetInt32();
                        break;
                }
            }

            if (timestamp != 0 || increment != 0)
                return new ObjectId(timestamp, machine, (short)pid, increment);
        }

        return ObjectId.Empty;
    }

    public override void Write(Utf8JsonWriter writer, ObjectId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }

    private static bool TryParseHex(string? hex, out ObjectId id)
    {
        id = ObjectId.Empty;
        if (string.IsNullOrWhiteSpace(hex) || hex.Length != 24)
            return false;

        try
        {
            id = new ObjectId(hex);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
